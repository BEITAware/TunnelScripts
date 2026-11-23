using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Tunnel_Next.Services.Scripting;
using OpenCvSharp;
using Size = OpenCvSharp.Size;
using Rect = OpenCvSharp.Rect;

[TunnelExtensionScript(
    Name = "傅里叶变换A",
    Author = "BEITAware",
    Description = "傅里叶变换 - 生成幅度谱和相位谱，支持RGBA格式",
    Version = "1.0",
    Category = "频域处理",
    Color = "#3399FF"
)]
public class FourierTransformScript : TunnelExtensionScriptBase
{
    [ScriptParameter(DisplayName = "应用对数变换", Description = "对幅度谱应用对数变换以增强可视化效果", Order = 0)]
    public bool ApplyLogTransform { get; set; } = true;

    [ScriptParameter(DisplayName = "零频率居中", Description = "将零频率移动到频谱中心", Order = 1)]
    public bool CenterFFT { get; set; } = true;

    [ScriptParameter(DisplayName = "存储元数据", Description = "在幅度谱中存储原始图像信息", Order = 2)]
    public bool StoreMetadata { get; set; } = true;

    [ScriptParameter(DisplayName = "应用汉宁窗", Description = "应用汉宁窗减少频谱泄漏；权重为零的边缘像素无法还原", Order = 3)]
    public bool ApplyWindow { get; set; } = false;

    [ScriptParameter(DisplayName = "增强预览效果", Description = "增强幅度谱的预览效果", Order = 4)]
    public bool EnhancePreview { get; set; } = true;

    [ScriptParameter(DisplayName = "变换Alpha通道", Description = "是否对Alpha通道也进行傅里叶变换", Order = 5)]
    public bool TransformAlpha { get; set; } = false;

    [ScriptParameter(DisplayName = "增强相位谱对比度", Description = "增强相位谱的对比度以改善可视化效果", Order = 6)]
    public bool EnhancePhaseContrast { get; set; } = true;

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
            ["f32bmp_magnitude"] = new PortDefinition("f32bmp", false, "幅度谱图像"),
            ["f32bmp_phase"] = new PortDefinition("f32bmp", false, "相位谱图像")
        };
    }

    private int _rows, _cols;

    public override Dictionary<string, object> Process(Dictionary<string, object> inputs, IScriptContext context)
    {
        if (!inputs.TryGetValue("f32bmp", out var value) || !(value is Mat input) || input.Empty())
            return new Dictionary<string, object> { ["f32bmp_magnitude"] = null, ["f32bmp_phase"] = null };
        using var rgba = EnsureRGBAFormat(input);
        _rows = rgba.Rows;
        _cols = rgba.Cols;
        // Without reconstruction metadata, transform at the exact input dimensions.
        int rows = StoreMetadata ? Cv2.GetOptimalDFTSize(_rows) : _rows;
        int cols = StoreMetadata ? Cv2.GetOptimalDFTSize(_cols) : _cols;
        var channels = Cv2.Split(rgba);
        var magnitudes = new Mat[4];
        var phases = new Mat[4];
        var magnitudeOutput = new Mat();
        var phaseOutput = new Mat();
        try
        {
            for (int c = 0; c < 4; c++)
            {
                magnitudes[c] = new Mat(rows, cols, MatType.CV_32F);
                phases[c] = new Mat(rows, cols, MatType.CV_32F);
                if (c == 3 && !TransformAlpha)
                {
                    Cv2.CopyMakeBorder(channels[c], magnitudes[c], 0, rows - _rows, 0, cols - _cols, BorderTypes.Constant, Scalar.All(0));
                    magnitudes[c].CopyTo(phases[c]);
                }
                else
                {
                    Transform(channels[c], magnitudes[c], phases[c]);
                }
            }
            Cv2.Merge(magnitudes, magnitudeOutput);
            Cv2.Merge(phases, phaseOutput);
            return new Dictionary<string, object> { ["f32bmp_magnitude"] = magnitudeOutput, ["f32bmp_phase"] = phaseOutput };
        }
        catch
        {
            magnitudeOutput.Dispose();
            phaseOutput.Dispose();
            throw;
        }
        finally
        {
            foreach (var m in channels) m.Dispose();
            foreach (var m in magnitudes) m?.Dispose();
            foreach (var m in phases) m?.Dispose();
        }
    }

    private void Transform(Mat channel, Mat magnitude, Mat phase)
    {
        if (ApplyWindow)
        {
            channel.GetArray(out float[] pixels);
            var wx = WindowAxis(channel.Cols);
            var wy = WindowAxis(channel.Rows);
            for (int y = 0; y < channel.Rows; y++)
                for (int x = 0; x < channel.Cols; x++)
                    pixels[y * channel.Cols + x] *= (float)(wx[x] * wy[y]);
            channel.SetArray(pixels);
        }
        int rows = magnitude.Rows, cols = magnitude.Cols;
        using var padded = new Mat();
        Cv2.CopyMakeBorder(channel, padded, 0, rows - channel.Rows, 0, cols - channel.Cols, BorderTypes.Constant, Scalar.All(0));
        using var spectrum = new Mat();
        Cv2.Dft(padded, spectrum, DftFlags.ComplexOutput);
        spectrum.GetArray(out Vec2f[] coefficients);
        var amplitudes = new float[coefficients.Length];
        var angles = new float[coefficients.Length];
        double scale = ApplyLogTransform ? Math.Log(1.0 + coefficients.Length) : coefficients.Length;
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < cols; x++)
            {
                var z = coefficients[y * cols + x];
                double re = z.Item0, im = z.Item1;
                double m = Math.Sqrt(re * re + im * im);
                m = (ApplyLogTransform ? Math.Log(1.0 + m) : m) / scale;
                double a = Math.Atan2(im, re) / (2 * Math.PI);
                if (a < 0) a += 1;
                int index = CenterFFT ? ((y + rows / 2) % rows) * cols + (x + cols / 2) % cols : y * cols + x;
                amplitudes[index] = (float)(EnhancePreview ? Math.Sqrt(m) : m);
                angles[index] = (float)(EnhancePhaseContrast ? Math.Sqrt(a) : a);
            }
        magnitude.SetArray(amplitudes);
        phase.SetArray(angles);
    }

    private static Mat EnsureRGBAFormat(Mat input)
    {
        using var values = new Mat();
        double scale = input.Depth() == MatType.CV_8U ? 1.0 / 255 :
                       input.Depth() == MatType.CV_16U ? 1.0 / 65535 : 1.0;
        input.ConvertTo(values, MatType.CV_32F, scale);
        if (values.Channels() == 4) return values.Clone();
        if (values.Channels() != 1 && values.Channels() != 3)
            throw new ArgumentException("输入图像需要 1、3 或 4 个通道。");
        var rgba = new Mat();
        Cv2.CvtColor(values, rgba, values.Channels() == 1 ? ColorConversionCodes.GRAY2RGBA : ColorConversionCodes.RGB2RGBA);
        return rgba;
    }

    private static double[] WindowAxis(int length)
    {
        var weights = new double[length];
        for (int i = 0; i < length; i++)
            weights[i] = length <= 2 ? 1 : Math.Sin(Math.PI * i / (length - 1));
        return weights;
    }

    public override Dictionary<string, object> InjectMetadata(Dictionary<string, object> currentMetadata)
    {
        var metadata = new Dictionary<string, object>(base.InjectMetadata(currentMetadata));
        // Port encoding flags travel with the spectra. Optional provenance enables padded transforms.
        metadata["傅里叶变换参数"] = new Dictionary<string, object>
        {
            ["EncodingVersion"] = 2, ["ApplyLogTransform"] = ApplyLogTransform, ["CenterFFT"] = CenterFFT,
            ["ApplyWindow"] = ApplyWindow, ["TransformAlpha"] = TransformAlpha,
            ["EnhancePreview"] = EnhancePreview, ["EnhancePhaseContrast"] = EnhancePhaseContrast
        };
        metadata.Remove("傅里叶重建参数");
        metadata.Remove("傅里叶重建信息");
        if (StoreMetadata)
        {
            metadata["傅里叶重建参数"] = new Dictionary<string, object> { ["OriginalRows"] = _rows, ["OriginalCols"] = _cols };
            metadata["傅里叶重建信息"] = new Dictionary<string, object> { ["NodeInstanceId"] = NodeInstanceId, ["Version"] = "2" };
        }
        metadata["处理历史"] = metadata.TryGetValue("处理历史", out var history) ? history + " → 傅里叶变换" : "傅里叶变换";
        return metadata;
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
            "/Tunnel-Next;component/Resources/ScriptsControls/CheckBoxStyles.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/TextBlockStyles.xaml"
        };
        foreach (var path in resourcePaths)
        {
            try { resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(path, UriKind.Relative) }); }
            catch { /* 静默处理 */ }
        }

        if (resources.Contains("MainPanelStyle")) mainPanel.Style = resources["MainPanelStyle"] as Style;

        var viewModel = CreateViewModel() as FourierTransformViewModel;
        mainPanel.DataContext = viewModel;

        var titleLabel = new Label { Content = "傅里叶变换设置" };
        if (resources.Contains("TitleLabelStyle")) titleLabel.Style = resources["TitleLabelStyle"] as Style;
        mainPanel.Children.Add(titleLabel);

        mainPanel.Children.Add(CreateCheckBoxControl("应用对数变换", nameof(viewModel.ApplyLogTransform), viewModel, resources));
        mainPanel.Children.Add(CreateCheckBoxControl("零频率居中", nameof(viewModel.CenterFFT), viewModel, resources));
        mainPanel.Children.Add(CreateCheckBoxControl("存储元数据", nameof(viewModel.StoreMetadata), viewModel, resources));
        mainPanel.Children.Add(CreateCheckBoxControl("应用汉宁窗", nameof(viewModel.ApplyWindow), viewModel, resources));
        mainPanel.Children.Add(CreateCheckBoxControl("增强预览效果", nameof(viewModel.EnhancePreview), viewModel, resources));
        mainPanel.Children.Add(CreateCheckBoxControl("变换Alpha通道", nameof(viewModel.TransformAlpha), viewModel, resources));
        mainPanel.Children.Add(CreateCheckBoxControl("增强相位谱对比度", nameof(viewModel.EnhancePhaseContrast), viewModel, resources));

        return mainPanel;
    }

    private CheckBox CreateCheckBoxControl(string content, string propertyName, FourierTransformViewModel viewModel, ResourceDictionary resources)
    {
        var checkBox = new CheckBox
        {
            Content = content,
            Margin = new Thickness(0, 5, 0, 5)
        };
        if (resources.Contains("DefaultCheckBoxStyle")) checkBox.Style = resources["DefaultCheckBoxStyle"] as Style;

        var binding = new Binding(propertyName)
        {
            Source = viewModel,
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        };
        checkBox.SetBinding(CheckBox.IsCheckedProperty, binding);

        return checkBox;
    }

    public override IScriptViewModel CreateViewModel()
    {
        return new FourierTransformViewModel(this);
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
            [nameof(ApplyLogTransform)] = ApplyLogTransform,
            [nameof(CenterFFT)] = CenterFFT,
            [nameof(StoreMetadata)] = StoreMetadata,
            [nameof(ApplyWindow)] = ApplyWindow,
            [nameof(EnhancePreview)] = EnhancePreview,
            [nameof(TransformAlpha)] = TransformAlpha,
            [nameof(EnhancePhaseContrast)] = EnhancePhaseContrast,
            ["NodeInstanceId"] = NodeInstanceId
        };
    }

    public override void DeserializeParameters(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(ApplyLogTransform), out var applyLog))
            ApplyLogTransform = Convert.ToBoolean(applyLog);
        if (data.TryGetValue(nameof(CenterFFT), out var centerFFT))
            CenterFFT = Convert.ToBoolean(centerFFT);
        if (data.TryGetValue(nameof(StoreMetadata), out var storeMeta))
            StoreMetadata = Convert.ToBoolean(storeMeta);
        if (data.TryGetValue(nameof(ApplyWindow), out var applyWindow))
            ApplyWindow = Convert.ToBoolean(applyWindow);
        if (data.TryGetValue(nameof(EnhancePreview), out var enhancePreview))
            EnhancePreview = Convert.ToBoolean(enhancePreview);
        if (data.TryGetValue(nameof(TransformAlpha), out var transformAlpha))
            TransformAlpha = Convert.ToBoolean(transformAlpha);
        if (data.TryGetValue(nameof(EnhancePhaseContrast), out var enhancePhase))
            EnhancePhaseContrast = Convert.ToBoolean(enhancePhase);
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

public class FourierTransformViewModel : ScriptViewModelBase
{
    private FourierTransformScript FourierTransformScript => (FourierTransformScript)Script;

    public bool ApplyLogTransform
    {
        get => FourierTransformScript.ApplyLogTransform;
        set
        {
            if (FourierTransformScript.ApplyLogTransform != value)
            {
                FourierTransformScript.ApplyLogTransform = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(ApplyLogTransform), value);
            }
        }
    }

    public bool CenterFFT
    {
        get => FourierTransformScript.CenterFFT;
        set
        {
            if (FourierTransformScript.CenterFFT != value)
            {
                FourierTransformScript.CenterFFT = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(CenterFFT), value);
            }
        }
    }

    public bool StoreMetadata
    {
        get => FourierTransformScript.StoreMetadata;
        set
        {
            if (FourierTransformScript.StoreMetadata != value)
            {
                FourierTransformScript.StoreMetadata = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(StoreMetadata), value);
            }
        }
    }

    public bool ApplyWindow
    {
        get => FourierTransformScript.ApplyWindow;
        set
        {
            if (FourierTransformScript.ApplyWindow != value)
            {
                FourierTransformScript.ApplyWindow = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(ApplyWindow), value);
            }
        }
    }

    public bool EnhancePreview
    {
        get => FourierTransformScript.EnhancePreview;
        set
        {
            if (FourierTransformScript.EnhancePreview != value)
            {
                FourierTransformScript.EnhancePreview = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(EnhancePreview), value);
            }
        }
    }

    public bool TransformAlpha
    {
        get => FourierTransformScript.TransformAlpha;
        set
        {
            if (FourierTransformScript.TransformAlpha != value)
            {
                FourierTransformScript.TransformAlpha = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(TransformAlpha), value);
            }
        }
    }

    public bool EnhancePhaseContrast
    {
        get => FourierTransformScript.EnhancePhaseContrast;
        set
        {
            if (FourierTransformScript.EnhancePhaseContrast != value)
            {
                FourierTransformScript.EnhancePhaseContrast = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(EnhancePhaseContrast), value);
            }
        }
    }

    public FourierTransformViewModel(FourierTransformScript script) : base(script)
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
        ApplyLogTransform = true;
        CenterFFT = true;
        StoreMetadata = true;
        ApplyWindow = false;
        EnhancePreview = true;
        TransformAlpha = false;
        EnhancePhaseContrast = true;
        await Task.CompletedTask;
    }

    public void EnhanceMagnitudePreview()
    {
        ApplyLogTransform = true;
        CenterFFT = true;
        EnhancePreview = true;
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
            [nameof(ApplyLogTransform)] = ApplyLogTransform,
            [nameof(CenterFFT)] = CenterFFT,
            [nameof(StoreMetadata)] = StoreMetadata,
            [nameof(ApplyWindow)] = ApplyWindow,
            [nameof(EnhancePreview)] = EnhancePreview,
            [nameof(TransformAlpha)] = TransformAlpha,
            [nameof(EnhancePhaseContrast)] = EnhancePhaseContrast
        };
    }

    public override async Task SetParameterDataAsync(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(ApplyLogTransform), out var applyLog))
            ApplyLogTransform = Convert.ToBoolean(applyLog);
        if (data.TryGetValue(nameof(CenterFFT), out var centerFFT))
            CenterFFT = Convert.ToBoolean(centerFFT);
        if (data.TryGetValue(nameof(StoreMetadata), out var storeMeta))
            StoreMetadata = Convert.ToBoolean(storeMeta);
        if (data.TryGetValue(nameof(ApplyWindow), out var applyWindow))
            ApplyWindow = Convert.ToBoolean(applyWindow);
        if (data.TryGetValue(nameof(EnhancePreview), out var enhancePreview))
            EnhancePreview = Convert.ToBoolean(enhancePreview);
        if (data.TryGetValue(nameof(TransformAlpha), out var transformAlpha))
            TransformAlpha = Convert.ToBoolean(transformAlpha);
        if (data.TryGetValue(nameof(EnhancePhaseContrast), out var enhancePhase))
            EnhancePhaseContrast = Convert.ToBoolean(enhancePhase);
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
