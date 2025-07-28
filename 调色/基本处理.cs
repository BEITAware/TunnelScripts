using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using Tunnel_Next.Services.Scripting;
using OpenCvSharp;

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
            ["f32bmp"] = new PortDefinition("f32bmp", false, "输入图像")
        };
    }

    public override Dictionary<string, PortDefinition> GetOutputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            ["f32bmp"] = new PortDefinition("f32bmp", false, "输出图像")
        };
    }

    public override Dictionary<string, object> Process(Dictionary<string, object> inputs, IScriptContext context)
    {
        // 检查输入是否有效
        if (!inputs.TryGetValue("f32bmp", out var inputObj) || inputObj == null)
        {
            return new Dictionary<string, object> { ["f32bmp"] = null };
        }

        if (!(inputObj is Mat inputMat) || inputMat.Empty())
        {
            return new Dictionary<string, object> { ["f32bmp"] = null };
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
            // 无需处理，直接返回原图
            return new Dictionary<string, object> { ["f32bmp"] = inputMat.Clone() };
        }

        try
        {
            // 确保输入是RGBA格式
            Mat workingMat = EnsureRGBAFormat(inputMat);
            
            // 大图像优化: 检查图像大小，如果很大则启用分块处理
            bool useTiling = (workingMat.Rows * workingMat.Cols > 2048 * 2048);
            
            Mat resultMat;
            if (useTiling)
            {
                resultMat = ProcessWithTiling(workingMat, pBrightness, pContrast, pSaturation, pHue, pWBR, pWBG, pWBB);
            }
            else
            {
                resultMat = ProcessDirectly(workingMat, pBrightness, pContrast, pSaturation, pHue, pWBR, pWBG, pWBB);
            }

            return new Dictionary<string, object> { ["f32bmp"] = resultMat };
        }
        catch (Exception ex)
        {
            // Add diagnostic information to the exception
            string analysis = string.Empty;
            if (inputObj is Mat m && !m.Empty())
            {
                try
                {
                    Cv2.MinMaxLoc(m, out double min, out double max);
                    analysis = $"Input Mat Analysis: Size={m.Size()}, Type={m.Type()}, Channels={m.Channels()}, ValueRange=[{min}, {max}].";
                }
                catch
                {
                    analysis = "Input Mat Analysis failed.";
                }
            }
            throw new ApplicationException($"基本处理节点处理失败: {ex.Message}. {analysis}", ex);
        }
    }

    /// <summary>
    /// 确保图像是RGBA格式
    /// </summary>
    private Mat EnsureRGBAFormat(Mat inputMat)
    {
        if (inputMat.Channels() == 4 && inputMat.Type() == MatType.CV_32FC4)
        {
            return inputMat.Clone();
        }

        Mat rgbaMat = new Mat();
        
        if (inputMat.Channels() == 3)
        {
            // RGB -> RGBA
            Cv2.CvtColor(inputMat, rgbaMat, ColorConversionCodes.RGB2RGBA);
        }
        else if (inputMat.Channels() == 1)
        {
            // 灰度 -> RGB -> RGBA
            Mat rgbMat = new Mat();
            Cv2.CvtColor(inputMat, rgbMat, ColorConversionCodes.GRAY2RGB);
            Cv2.CvtColor(rgbMat, rgbaMat, ColorConversionCodes.RGB2RGBA);
            rgbMat.Dispose();
        }
        else if (inputMat.Channels() == 4)
        {
            rgbaMat = inputMat.Clone();
        }
        else
        {
            throw new NotSupportedException($"不支持 {inputMat.Channels()} 通道的图像");
        }

        // 确保是32位浮点格式
        if (rgbaMat.Type() != MatType.CV_32FC4)
        {
            Mat floatMat = new Mat();
            rgbaMat.ConvertTo(floatMat, MatType.CV_32FC4, 1.0 / 255.0);
            rgbaMat.Dispose();
            return floatMat;
        }

        return rgbaMat;
    }

    /// <summary>
    /// 直接处理小图像
    /// </summary>
    private Mat ProcessDirectly(
        Mat inputMat,
        double brightness,
        double contrast,
        double saturation,
        double hue,
        double wbR,
        double wbG,
        double wbB)
    {
        // 分离Alpha通道
        Mat[] channels = Cv2.Split(inputMat);
        Mat alphaMat = channels[3].Clone();
        
        // 处理RGB通道
        Mat rgbMat = new Mat();
        Cv2.Merge(new Mat[] { channels[0], channels[1], channels[2] }, rgbMat);
        
        // 应用基本调整
        ApplyBasicAdjustments(rgbMat, brightness, contrast);
        
        // 应用HSV调整
        if (Math.Abs(saturation) > 0.001 || Math.Abs(hue) > 0.001)
        {
            ApplyHSVAdjustments(rgbMat, saturation, hue);
        }
        
        // 应用白平衡
        ApplyWhiteBalance(rgbMat, wbR, wbG, wbB);
        
        // 重新合并Alpha通道
        Mat[] finalChannels = Cv2.Split(rgbMat);
        Mat finalMat = new Mat();
        Cv2.Merge(new Mat[] { finalChannels[0], finalChannels[1], finalChannels[2], alphaMat }, finalMat);
        
        // 将结果拷贝回原 Mat 并返回原引用，避免多余分配
        finalMat.CopyTo(inputMat);
        finalMat.Dispose();
        return inputMat;
    }

    /// <summary>
    /// 分块处理大图像
    /// </summary>
    private Mat ProcessWithTiling(
        Mat inputMat,
        double brightness,
        double contrast,
        double saturation,
        double hue,
        double wbR,
        double wbG,
        double wbB)
    {
        // ---------- 1. 预备 ----------
        // 创建最终结果 Mat，与输入尺寸 / 类型 相同
        var resultMat = new Mat(inputMat.Size(), inputMat.Type());

        // 动态 tileSize：长边至少被分成 2 块，且面积 <=1M 像素，最小 256
        int maxSide = Math.Max(inputMat.Rows, inputMat.Cols);
        int tileSize = 1024;
        while (tileSize > 256 && maxSide / tileSize < 2)
        {
            tileSize >>= 1;
        }

        int tilesY = (inputMat.Rows + tileSize - 1) / tileSize;
        int tilesX = (inputMat.Cols + tileSize - 1) / tileSize;
        int totalTiles = tilesY * tilesX;

        // 用数组存储处理后的 tile，索引 => (yIdx * tilesX + xIdx)
        var processedTiles = new Mat[totalTiles];

        // ---------- 2. 并行处理各 tile ----------
        var parallelOpts = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };

        Parallel.For(0, totalTiles, parallelOpts, idx =>
        {
            int yIdx = idx / tilesX;
            int xIdx = idx % tilesX;

            int y = yIdx * tileSize;
            int x = xIdx * tileSize;
            int height = Math.Min(tileSize, inputMat.Rows - y);
            int width = Math.Min(tileSize, inputMat.Cols - x);
            var tileRect = new OpenCvSharp.Rect(x, y, width, height);

            // Clone ROI，线程独享
            var tileRgba = inputMat[tileRect].Clone(); // 独立内存，稍后由主线程释放
            processedTiles[idx] = ProcessDirectly(tileRgba, brightness, contrast, saturation, hue, wbR, wbG, wbB); // 处理后暂存
        });

        // ---------- 3. 合并结果（再次并行，ROI 不重叠，因此线程安全） ----------
        Parallel.For(0, totalTiles, parallelOpts, idx =>
        {
            int yIdx = idx / tilesX;
            int xIdx = idx % tilesX;

            int y = yIdx * tileSize;
            int x = xIdx * tileSize;
            int height = Math.Min(tileSize, inputMat.Rows - y);
            int width = Math.Min(tileSize, inputMat.Cols - x);
            var tileRect = new OpenCvSharp.Rect(x, y, width, height);

            var tile = processedTiles[idx];
            if (tile != null)
            {
                tile.CopyTo(resultMat[tileRect]);
                tile.Dispose();
                processedTiles[idx] = null;
            }
        });

        return resultMat;
    }

    /// <summary>
    /// 应用基本调整（亮度、对比度）
    /// </summary>
    private void ApplyBasicAdjustments(Mat mat, double brightness, double contrast)
    {
        // 亮度调整
        if (Math.Abs(brightness) > 0.001)
        {
            double factor = 1.0 + brightness / 100.0;
            mat.ConvertTo(mat, -1, factor, 0);
        }

        // 对比度调整
        if (Math.Abs(contrast) > 0.001)
        {
            double factor = (259.0 * (contrast + 255.0)) / (255.0 * (259.0 - contrast));
            mat.ConvertTo(mat, -1, factor, -factor * 0.5 + 0.5);
        }

        // 确保值在有效范围内
        Cv2.Threshold(mat, mat, 1.0, 1.0, ThresholdTypes.Trunc);
        Cv2.Threshold(mat, mat, 0.0, 0.0, ThresholdTypes.Tozero);
    }

    /// <summary>
    /// 应用HSV调整（饱和度、色调）
    /// </summary>
    private void ApplyHSVAdjustments(Mat rgbMat, double saturation, double hue)
    {
        Mat hsvMat = new Mat();
        Cv2.CvtColor(rgbMat, hsvMat, ColorConversionCodes.RGB2HSV);

        Mat[] hsvChannels = Cv2.Split(hsvMat);

        // 色调调整
        if (Math.Abs(hue) > 0.001)
        {
            double hueShift = hue / 360.0 * 180.0; // OpenCV HSV H范围是0-180
            hsvChannels[0].ConvertTo(hsvChannels[0], -1, 1.0, hueShift);

            // 处理色调环绕
            Mat mask = new Mat();
            Cv2.Threshold(hsvChannels[0], mask, 180, 1, ThresholdTypes.Binary);
            hsvChannels[0] -= mask * 180;

            Cv2.Threshold(hsvChannels[0], mask, 0, 1, ThresholdTypes.BinaryInv);
            hsvChannels[0] += mask * 180;

            mask.Dispose();
        }

        // 饱和度调整
        if (Math.Abs(saturation) > 0.001)
        {
            double satFactor = 1.0 + saturation / 100.0;
            hsvChannels[1].ConvertTo(hsvChannels[1], -1, satFactor, 0);
            Cv2.Threshold(hsvChannels[1], hsvChannels[1], 255, 255, ThresholdTypes.Trunc);
        }

        // 重新合并并转换回RGB
        Cv2.Merge(hsvChannels, hsvMat);
        Cv2.CvtColor(hsvMat, rgbMat, ColorConversionCodes.HSV2RGB);

        // 清理资源
        hsvMat.Dispose();
        foreach (var ch in hsvChannels) ch.Dispose();
    }

    /// <summary>
    /// 应用白平衡调整
    /// </summary>
    private void ApplyWhiteBalance(Mat rgbMat, double wbR, double wbG, double wbB)
    {
        if (Math.Abs(wbR - 1.0) > 0.001 ||
            Math.Abs(wbG - 1.0) > 0.001 ||
            Math.Abs(wbB - 1.0) > 0.001)
        {
            Mat[] channels = Cv2.Split(rgbMat);

            channels[0].ConvertTo(channels[0], -1, wbR, 0);
            channels[1].ConvertTo(channels[1], -1, wbG, 0);
            channels[2].ConvertTo(channels[2], -1, wbB, 0);

            // 确保值在有效范围内
            foreach (var ch in channels)
            {
                Cv2.Threshold(ch, ch, 1.0, 1.0, ThresholdTypes.Trunc);
                Cv2.Threshold(ch, ch, 0.0, 0.0, ThresholdTypes.Tozero);
            }

            Cv2.Merge(channels, rgbMat);

            // 清理资源
            foreach (var ch in channels) ch.Dispose();
        }
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
