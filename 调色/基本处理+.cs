using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using Tunnel_Next.Services.Scripting;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.WIC;
using System.Runtime.InteropServices;

[TunnelExtensionScript(
    Name = "基本处理",
    Author = "BEITAware",
    Description = "基本图像处理：亮度、对比度、饱和度、色调、白平衡调整",
    Version = "1.0",
    Category = "图像处理",
    Color = "#90EE90"
)]
public class BasicProcessingScript : TunnelExtensionScriptBase
{
    [ScriptParameter(DisplayName = "亮度", Description = "调整图像亮度 (-100 到 100)", Order = 0)]
    public double Brightness { get; set; } = 0;

    [ScriptParameter(DisplayName = "对比度", Description = "调整图像对比度 (-100 到 100)", Order = 1)]
    public double Contrast { get; set; } = 0;

    [ScriptParameter(DisplayName = "饱和度", Description = "调整图像饱和度 (-100 到 100)", Order = 2)]
    public double Saturation { get; set; } = 0;

    [ScriptParameter(DisplayName = "色调", Description = "调整图像色调 (-180 到 180)", Order = 3)]
    public double Hue { get; set; } = 0;

    [ScriptParameter(DisplayName = "白平衡 R", Description = "红色通道白平衡 (0.5 到 2.0)", Order = 4)]
    public double WhiteBalanceR { get; set; } = 1.0;

    [ScriptParameter(DisplayName = "白平衡 G", Description = "绿色通道白平衡 (0.5 到 2.0)", Order = 5)]
    public double WhiteBalanceG { get; set; } = 1.0;

    [ScriptParameter(DisplayName = "白平衡 B", Description = "蓝色通道白平衡 (0.5 到 2.0)", Order = 6)]
    public double WhiteBalanceB { get; set; } = 1.0;

    public string NodeInstanceId { get; set; } = string.Empty;

    public override Dictionary<string, PortDefinition> GetInputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            ["F32texture"] = new PortDefinition("F32texture", false, "输入图像")
        };
    }

    public override Dictionary<string, PortDefinition> GetOutputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            ["F32texture"] = new PortDefinition("F32texture", false, "输出图像")
        };
    }

    // D3D11设备和上下文
    private ID3D11Device _device;
    private ID3D11DeviceContext _deviceContext;
    private IWICImagingFactory _wicFactory;

    public override Dictionary<string, object> Process(Dictionary<string, object> inputs, IScriptContext context)
    {
        // 检查输入是否有效
        if (!inputs.TryGetValue("F32texture", out var inputObj) || inputObj == null)
        {
            return new Dictionary<string, object> { ["F32texture"] = null };
        }

        if (!(inputObj is ID3D11Texture2D inputTexture))
        {
            return new Dictionary<string, object> { ["F32texture"] = null };
        }

        // 捕获参数快照，确保整个处理周期一致
        double pBrightness = Brightness;
        double pContrast = Contrast;
        double pSaturation = Saturation;
        double pHue = Hue;
        double pWBR = WhiteBalanceR;
        double pWBG = WhiteBalanceG;
        double pWBB = WhiteBalanceB;

        // 检查是否需要处理
        bool needProcessing = Math.Abs(pBrightness) > 0.001 || Math.Abs(pContrast) > 0.001 ||
                             Math.Abs(pSaturation) > 0.001 || Math.Abs(pHue) > 0.001 ||
                             Math.Abs(pWBR - 1.0) > 0.001 || Math.Abs(pWBG - 1.0) > 0.001 ||
                             Math.Abs(pWBB - 1.0) > 0.001;

        if (!needProcessing)
        {
            // 无需处理，直接返回原图的克隆
            return new Dictionary<string, object> { ["F32texture"] = CloneTexture(inputTexture) };
        }

        try
        {
            // 初始化D3D11设备和WIC工厂
            InitializeDeviceAndWIC();

            // 获取纹理描述
            var desc = inputTexture.Description;

            // 大图像优化: 检查图像大小，如果很大则启用分块处理
            bool useTiling = desc.Width * desc.Height > 2048 * 2048;

            ID3D11Texture2D resultTexture;
            if (useTiling)
            {
                resultTexture = ProcessWithTiling(inputTexture, pBrightness, pContrast, pSaturation, pHue, pWBR, pWBG, pWBB);
            }
            else
            {
                resultTexture = ProcessDirectly(inputTexture, pBrightness, pContrast, pSaturation, pHue, pWBR, pWBG, pWBB);
            }

            return new Dictionary<string, object> { ["F32texture"] = resultTexture };
        }
        catch (Exception ex)
        {
            // Add diagnostic information to the exception
            string analysis = string.Empty;
            if (inputObj is ID3D11Texture2D tex)
            {
                try
                {
                    var desc = tex.Description;
                    analysis = $"Input Texture Analysis: Size={desc.Width}x{desc.Height}, Format={desc.Format}, MipLevels={desc.MipLevels}.";
                }
                catch
                {
                    analysis = "Input Texture Analysis failed.";
                }
            }
            throw new ApplicationException($"基本处理节点处理失败: {ex.Message}. {analysis}", ex);
        }
    }

    private void InitializeDeviceAndWIC()
    {
        if (_device == null)
        {
            // 创建D3D11设备
            Vortice.Direct3D11.D3D11.D3D11CreateDevice(
                null,
                Vortice.Direct3D.DriverType.Hardware,
                Vortice.Direct3D11.DeviceCreationFlags.None,
                null,
                out _device,
                out _deviceContext);
        }

        if (_wicFactory == null)
        {
            // 创建WIC工厂
            _wicFactory = new IWICImagingFactory2();
        }
    }

    private ID3D11Texture2D CloneTexture(ID3D11Texture2D sourceTexture)
    {
        var desc = sourceTexture.Description;
        var clonedTexture = _device.CreateTexture2D(desc);
        _deviceContext.CopyResource(clonedTexture, sourceTexture);
        return clonedTexture;
    }

    /// <summary>
    /// 直接处理纹理
    /// </summary>
    private ID3D11Texture2D ProcessDirectly(
        ID3D11Texture2D inputTexture,
        double brightness,
        double contrast,
        double saturation,
        double hue,
        double wbR,
        double wbG,
        double wbB)
    {
        var desc = inputTexture.Description;

        // 创建输出纹理
        var outputTexture = _device.CreateTexture2D(desc);

        // 创建可映射的临时纹理用于CPU访问
        var stagingDesc = desc;
        stagingDesc.Usage = ResourceUsage.Staging;
        stagingDesc.BindFlags = BindFlags.None;
        stagingDesc.CPUAccessFlags = CpuAccessFlags.Read | CpuAccessFlags.Write;

        var stagingTexture = _device.CreateTexture2D(stagingDesc);

        // 复制输入纹理到临时纹理
        _deviceContext.CopyResource(stagingTexture, inputTexture);

        // 映射纹理进行CPU处理
        var mappedResource = _deviceContext.Map(stagingTexture, 0, MapMode.ReadWrite, Vortice.Direct3D11.MapFlags.None);

        try
        {
            ProcessPixelData(mappedResource, desc, brightness, contrast, saturation, hue, wbR, wbG, wbB);
        }
        finally
        {
            _deviceContext.Unmap(stagingTexture, 0);
        }

        // 复制处理后的数据到输出纹理
        _deviceContext.CopyResource(outputTexture, stagingTexture);

        stagingTexture.Dispose();

        return outputTexture;
    }

    /// <summary>
    /// 处理像素数据
    /// </summary>
    private unsafe void ProcessPixelData(
        MappedSubresource mappedResource,
        Texture2DDescription desc,
        double brightness,
        double contrast,
        double saturation,
        double hue,
        double wbR,
        double wbG,
        double wbB)
    {
        var dataPtr = (float*)mappedResource.DataPointer;
        var rowPitch = mappedResource.RowPitch / sizeof(float);

        for (uint y = 0; y < desc.Height; y++)
        {
            for (uint x = 0; x < desc.Width; x++)
            {
                var pixelIndex = y * rowPitch + x * 4;

                // 读取RGBA值
                float r = dataPtr[pixelIndex];
                float g = dataPtr[pixelIndex + 1];
                float b = dataPtr[pixelIndex + 2];
                float a = dataPtr[pixelIndex + 3];

                // 应用白平衡
                r = (float)(r * wbR);
                g = (float)(g * wbG);
                b = (float)(b * wbB);

                // 应用亮度调整
                if (Math.Abs(brightness) > 0.001)
                {
                    double factor = 1.0 + brightness / 100.0;
                    r = (float)(r * factor);
                    g = (float)(g * factor);
                    b = (float)(b * factor);
                }

                // 应用对比度调整
                if (Math.Abs(contrast) > 0.001)
                {
                    double factor = (259.0 * (contrast + 255.0)) / (255.0 * (259.0 - contrast));
                    r = (float)(factor * (r - 0.5) + 0.5);
                    g = (float)(factor * (g - 0.5) + 0.5);
                    b = (float)(factor * (b - 0.5) + 0.5);
                }

                // 应用HSV调整（饱和度和色调）
                if (Math.Abs(saturation) > 0.001 || Math.Abs(hue) > 0.001)
                {
                    RgbToHsv(r, g, b, out float h, out float s, out float v);

                    // 色调调整
                    if (Math.Abs(hue) > 0.001)
                    {
                        h += (float)(hue / 360.0);
                        if (h > 1.0f) h -= 1.0f;
                        if (h < 0.0f) h += 1.0f;
                    }

                    // 饱和度调整
                    if (Math.Abs(saturation) > 0.001)
                    {
                        s = (float)(s * (1.0 + saturation / 100.0));
                        s = Math.Max(0.0f, Math.Min(1.0f, s));
                    }

                    HsvToRgb(h, s, v, out r, out g, out b);
                }

                // 确保值在有效范围内
                r = Math.Max(0.0f, Math.Min(1.0f, r));
                g = Math.Max(0.0f, Math.Min(1.0f, g));
                b = Math.Max(0.0f, Math.Min(1.0f, b));

                // 写回像素值
                dataPtr[pixelIndex] = r;
                dataPtr[pixelIndex + 1] = g;
                dataPtr[pixelIndex + 2] = b;
                dataPtr[pixelIndex + 3] = a;
            }
        }
    }

    /// <summary>
    /// RGB转HSV
    /// </summary>
    private void RgbToHsv(float r, float g, float b, out float h, out float s, out float v)
    {
        float max = Math.Max(r, Math.Max(g, b));
        float min = Math.Min(r, Math.Min(g, b));
        float delta = max - min;

        v = max;
        s = max == 0 ? 0 : delta / max;

        if (delta == 0)
        {
            h = 0;
        }
        else if (max == r)
        {
            h = ((g - b) / delta) / 6.0f;
            if (h < 0) h += 1.0f;
        }
        else if (max == g)
        {
            h = ((b - r) / delta + 2.0f) / 6.0f;
        }
        else
        {
            h = ((r - g) / delta + 4.0f) / 6.0f;
        }
    }

    /// <summary>
    /// HSV转RGB
    /// </summary>
    private void HsvToRgb(float h, float s, float v, out float r, out float g, out float b)
    {
        if (s == 0)
        {
            r = g = b = v;
            return;
        }

        h *= 6.0f;
        int i = (int)Math.Floor(h);
        float f = h - i;
        float p = v * (1.0f - s);
        float q = v * (1.0f - s * f);
        float t = v * (1.0f - s * (1.0f - f));

        switch (i % 6)
        {
            case 0: r = v; g = t; b = p; break;
            case 1: r = q; g = v; b = p; break;
            case 2: r = p; g = v; b = t; break;
            case 3: r = p; g = q; b = v; break;
            case 4: r = t; g = p; b = v; break;
            default: r = v; g = p; b = q; break;
        }
    }

    /// <summary>
    /// 分块处理大图像
    /// </summary>
    private ID3D11Texture2D ProcessWithTiling(
        ID3D11Texture2D inputTexture,
        double brightness,
        double contrast,
        double saturation,
        double hue,
        double wbR,
        double wbG,
        double wbB)
    {
        // 对于大纹理，我们简化处理，直接使用ProcessDirectly
        // 在实际应用中，可以考虑使用Compute Shader进行GPU并行处理
        return ProcessDirectly(inputTexture, brightness, contrast, saturation, hue, wbR, wbG, wbB);
    }
    /// <summary>
    /// 清理资源
    /// </summary>
    public void Dispose()
    {
        _deviceContext?.Dispose();
        _device?.Dispose();
        _wicFactory?.Dispose();
    }







    public override FrameworkElement CreateParameterControl()
    {
        var mainPanel = new StackPanel { Margin = new Thickness(5) };

        // 加载资源
        var resources = new ResourceDictionary();
        var resourcePaths = new[]
        {
            "/Tunnel-Next;component/Resources/ScriptsControls/SharedBrushes.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/LabelStyles.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/PanelStyles.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/SliderStyles.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/ScriptButtonStyles.xaml"
        };
        foreach (var path in resourcePaths)
        {
            try { resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(path, UriKind.Relative) }); }
            catch { /* 静默处理 */ }
        }

        if (resources.Contains("MainPanelStyle")) mainPanel.Style = resources["MainPanelStyle"] as Style;

        // ViewModel
        var viewModel = CreateViewModel() as BasicProcessingViewModel;
        mainPanel.DataContext = viewModel;

        // 标题
        var titleLabel = new Label { Content = "基本处理" };
        if (resources.Contains("TitleLabelStyle")) titleLabel.Style = resources["TitleLabelStyle"] as Style;
        mainPanel.Children.Add(titleLabel);

        // 滑块控件
        mainPanel.Children.Add(CreateSliderControl("亮度", nameof(viewModel.Brightness), -100, 100, viewModel, resources));
        mainPanel.Children.Add(CreateSliderControl("对比度", nameof(viewModel.Contrast), -100, 100, viewModel, resources));
        mainPanel.Children.Add(CreateSliderControl("饱和度", nameof(viewModel.Saturation), -100, 100, viewModel, resources));
        mainPanel.Children.Add(CreateSliderControl("色调", nameof(viewModel.Hue), -180, 180, viewModel, resources));
        mainPanel.Children.Add(CreateSliderControl("白平衡 R", nameof(viewModel.WhiteBalanceR), 0.5, 2.0, viewModel, resources));
        mainPanel.Children.Add(CreateSliderControl("白平衡 G", nameof(viewModel.WhiteBalanceG), 0.5, 2.0, viewModel, resources));
        mainPanel.Children.Add(CreateSliderControl("白平衡 B", nameof(viewModel.WhiteBalanceB), 0.5, 2.0, viewModel, resources));

        // 重置按钮
        var resetButton = new Button { Content = "重置所有参数", Margin = new Thickness(0, 10, 0, 0) };
        if (resources.Contains("SelectFileScriptButtonStyle")) resetButton.Style = resources["SelectFileScriptButtonStyle"] as Style;
        resetButton.Click += async (s, e) => await viewModel.ResetToDefault();
        mainPanel.Children.Add(resetButton);

        return mainPanel;
    }

    private FrameworkElement CreateSliderControl(string label, string propertyName, double min, double max, BasicProcessingViewModel viewModel, ResourceDictionary resources)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 5, 0, 0) };

        var labelControl = new Label { Content = label };
        if (resources.Contains("DefaultLabelStyle")) labelControl.Style = resources["DefaultLabelStyle"] as Style;

        var slider = new Slider { Minimum = min, Maximum = max };
        if (resources.Contains("DefaultSliderStyle")) slider.Style = resources["DefaultSliderStyle"] as Style;
        slider.SetBinding(Slider.ValueProperty, new Binding(propertyName) { Mode = BindingMode.TwoWay });

        panel.Children.Add(labelControl);
        panel.Children.Add(slider);

        return panel;
    }

    public override IScriptViewModel CreateViewModel()
    {
        return new BasicProcessingViewModel(this);
    }

    public override async Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
    {
        // 参数变化处理
        await Task.CompletedTask;
    }

    public override Dictionary<string, object> SerializeParameters()
    {
        return new Dictionary<string, object>
        {
            [nameof(Brightness)] = Brightness,
            [nameof(Contrast)] = Contrast,
            [nameof(Saturation)] = Saturation,
            [nameof(Hue)] = Hue,
            [nameof(WhiteBalanceR)] = WhiteBalanceR,
            [nameof(WhiteBalanceG)] = WhiteBalanceG,
            [nameof(WhiteBalanceB)] = WhiteBalanceB,
            ["NodeInstanceId"] = NodeInstanceId
        };
    }

    public override void DeserializeParameters(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(Brightness), out var brightness))
            Brightness = Convert.ToDouble(brightness);
        if (data.TryGetValue(nameof(Contrast), out var contrast))
            Contrast = Convert.ToDouble(contrast);
        if (data.TryGetValue(nameof(Saturation), out var saturation))
            Saturation = Convert.ToDouble(saturation);
        if (data.TryGetValue(nameof(Hue), out var hue))
            Hue = Convert.ToDouble(hue);
        if (data.TryGetValue(nameof(WhiteBalanceR), out var wbR))
            WhiteBalanceR = Convert.ToDouble(wbR);
        if (data.TryGetValue(nameof(WhiteBalanceG), out var wbG))
            WhiteBalanceG = Convert.ToDouble(wbG);
        if (data.TryGetValue(nameof(WhiteBalanceB), out var wbB))
            WhiteBalanceB = Convert.ToDouble(wbB);
        if (data.TryGetValue("NodeInstanceId", out var nodeId))
            NodeInstanceId = nodeId?.ToString() ?? string.Empty;
    }

    public void InitializeNodeInstance(string nodeId)
    {
        if (!string.IsNullOrEmpty(nodeId))
        {
            NodeInstanceId = nodeId;
        }
    }
}

public class BasicProcessingViewModel : ScriptViewModelBase
{
    private BasicProcessingScript BasicProcessingScript => (BasicProcessingScript)Script;

    public double Brightness
    {
        get => BasicProcessingScript.Brightness;
        set
        {
            if (Math.Abs(BasicProcessingScript.Brightness - value) > 0.001)
            {
                BasicProcessingScript.Brightness = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(Brightness), value);
            }
        }
    }

    public double Contrast
    {
        get => BasicProcessingScript.Contrast;
        set
        {
            if (Math.Abs(BasicProcessingScript.Contrast - value) > 0.001)
            {
                BasicProcessingScript.Contrast = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(Contrast), value);
            }
        }
    }

    public double Saturation
    {
        get => BasicProcessingScript.Saturation;
        set
        {
            if (Math.Abs(BasicProcessingScript.Saturation - value) > 0.001)
            {
                BasicProcessingScript.Saturation = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(Saturation), value);
            }
        }
    }

    public double Hue
    {
        get => BasicProcessingScript.Hue;
        set
        {
            if (Math.Abs(BasicProcessingScript.Hue - value) > 0.001)
            {
                BasicProcessingScript.Hue = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(Hue), value);
            }
        }
    }

    public double WhiteBalanceR
    {
        get => BasicProcessingScript.WhiteBalanceR;
        set
        {
            if (Math.Abs(BasicProcessingScript.WhiteBalanceR - value) > 0.001)
            {
                BasicProcessingScript.WhiteBalanceR = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(WhiteBalanceR), value);
            }
        }
    }

    public double WhiteBalanceG
    {
        get => BasicProcessingScript.WhiteBalanceG;
        set
        {
            if (Math.Abs(BasicProcessingScript.WhiteBalanceG - value) > 0.001)
            {
                BasicProcessingScript.WhiteBalanceG = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(WhiteBalanceG), value);
            }
        }
    }

    public double WhiteBalanceB
    {
        get => BasicProcessingScript.WhiteBalanceB;
        set
        {
            if (Math.Abs(BasicProcessingScript.WhiteBalanceB - value) > 0.001)
            {
                BasicProcessingScript.WhiteBalanceB = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(WhiteBalanceB), value);
            }
        }
    }

    public BasicProcessingViewModel(BasicProcessingScript script) : base(script)
    {
    }

    private void NotifyParameterChanged(string parameterName, object value)
    {
        if (Script is TunnelExtensionScriptBase rsb)
        {
            rsb.OnParameterChanged(parameterName, value);
        }
    }

    public async Task ResetToDefault()
    {
        Brightness = 0;
        Contrast = 0;
        Saturation = 0;
        Hue = 0;
        WhiteBalanceR = 1.0;
        WhiteBalanceG = 1.0;
        WhiteBalanceB = 1.0;
        await Task.CompletedTask;
    }

    public override async Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
    {
        await Task.CompletedTask;
    }

    public override ScriptValidationResult ValidateParameter(string parameterName, object value)
    {
        return new ScriptValidationResult(true);
    }

    public override Dictionary<string, object> GetParameterData()
    {
        return new Dictionary<string, object>
        {
            [nameof(Brightness)] = Brightness,
            [nameof(Contrast)] = Contrast,
            [nameof(Saturation)] = Saturation,
            [nameof(Hue)] = Hue,
            [nameof(WhiteBalanceR)] = WhiteBalanceR,
            [nameof(WhiteBalanceG)] = WhiteBalanceG,
            [nameof(WhiteBalanceB)] = WhiteBalanceB
        };
    }

    public override async Task SetParameterDataAsync(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(Brightness), out var brightness))
            Brightness = Convert.ToDouble(brightness);
        if (data.TryGetValue(nameof(Contrast), out var contrast))
            Contrast = Convert.ToDouble(contrast);
        if (data.TryGetValue(nameof(Saturation), out var saturation))
            Saturation = Convert.ToDouble(saturation);
        if (data.TryGetValue(nameof(Hue), out var hue))
            Hue = Convert.ToDouble(hue);
        if (data.TryGetValue(nameof(WhiteBalanceR), out var wbR))
            WhiteBalanceR = Convert.ToDouble(wbR);
        if (data.TryGetValue(nameof(WhiteBalanceG), out var wbG))
            WhiteBalanceG = Convert.ToDouble(wbG);
        if (data.TryGetValue(nameof(WhiteBalanceB), out var wbB))
            WhiteBalanceB = Convert.ToDouble(wbB);
        await Task.CompletedTask;
    }

    public override async Task ResetToDefaultAsync()
    {
        await ResetToDefault();
    }

    public override void Dispose()
    {
        base.Dispose();
    }
}
