using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Tunnel_Next.Services.Scripting;
using OpenCvSharp;
using Size = OpenCvSharp.Size;
using Rect = OpenCvSharp.Rect;

/// <summary>
/// 重建质量枚举
/// </summary>
public enum ReconstructionQuality
{
    Low,
    Medium,
    High,
    Ultra
}

[TunnelExtensionScript(
    Name = "傅里叶逆变换",
    Author = "BEITAware",
    Description = "傅里叶逆变换 - 从幅度谱和相位谱重建原始图像，支持RGBA格式",
    Version = "1.0",
    Category = "频域处理",
    Color = "#FF6633"
)]
public class InverseFourierTransformScript : TunnelExtensionScriptBase
{
    [ScriptParameter(DisplayName = "自动检测参数", Description = "从元数据自动检测原始变换参数", Order = 0)]
    public bool AutoDetectParameters { get; set; } = true;

    [ScriptParameter(DisplayName = "应用了对数变换", Description = "输入的幅度谱是否应用了对数变换", Order = 1)]
    public bool WasLogTransformed { get; set; } = true;

    [ScriptParameter(DisplayName = "零频率已居中", Description = "输入的频谱是否已将零频率移到中心", Order = 2)]
    public bool WasCentered { get; set; } = true;

    [ScriptParameter(DisplayName = "应用了汉宁窗", Description = "补偿原始汉宁窗；零权重边缘无法还原", Order = 3)]
    public bool WasWindowed { get; set; } = false;

    [ScriptParameter(DisplayName = "增强了相位谱对比度", Description = "输入的相位谱是否增强了对比度", Order = 4)]
    public bool WasPhaseEnhanced { get; set; } = true;

    [ScriptParameter(DisplayName = "变换了Alpha通道", Description = "原始变换是否对Alpha通道也进行了变换", Order = 5)]
    public bool WasAlphaTransformed { get; set; } = false;

    [ScriptParameter(DisplayName = "重建质量", Description = "低/中使用快速极坐标，高/超高使用精确三角函数；中/超高采用双精度逆变换", Order = 6)]
    public ReconstructionQuality Quality { get; set; } = ReconstructionQuality.High;

    [ScriptParameter(DisplayName = "幅度缩放因子", Description = "手动调整幅度谱的缩放因子", Order = 7)]
    public double MagnitudeScale { get; set; } = 1.0;

    public string NodeInstanceId { get; set; } = string.Empty;

    public override Dictionary<string, PortDefinition> GetInputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            ["f32bmp_magnitude"] = new PortDefinition("f32bmp", false, "幅度谱图像"),
            ["f32bmp_phase"] = new PortDefinition("f32bmp", false, "相位谱图像")
        };
    }

    public override Dictionary<string, PortDefinition> GetOutputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            ["f32bmp"] = new PortDefinition("f32bmp", false, "重建的图像")
        };
    }

    private Dictionary<string, object> _encoding = new Dictionary<string, object>();
    private Dictionary<string, object> _dimensions = new Dictionary<string, object>();

    public override void ExtractMetadata(Dictionary<string, object> upstream)
    {
        base.ExtractMetadata(upstream);
        _encoding = ReadFields(upstream, "傅里叶变换参数");
        _dimensions = ReadFields(upstream, "傅里叶重建参数");
    }

    private static Dictionary<string, object> ReadFields(Dictionary<string, object> upstream, string key)
    {
        if (upstream.TryGetValue(key, out var value))
        {
            if (value is Dictionary<string, object> fields) return fields;
            // Tunnel deep-copies propagated metadata through System.Text.Json.
            if (value is JsonElement json && json.ValueKind == JsonValueKind.Object)
            {
                var result = new Dictionary<string, object>();
                foreach (var property in json.EnumerateObject()) result[property.Name] = property.Value.ToString();
                return result;
            }
        }
        return new Dictionary<string, object>();
    }

    private bool Flag(string key, bool fallback) => _encoding.TryGetValue(key, out var value) ? Convert.ToBoolean(value) : fallback;
    private int Length(string key, int fallback) => _dimensions.TryGetValue(key, out var value) ? Convert.ToInt32(value) : fallback;

    public override Dictionary<string, object> Process(Dictionary<string, object> inputs, IScriptContext context)
    {
        if (!inputs.TryGetValue("f32bmp_magnitude", out var m) || !(m is Mat magnitude) || magnitude.Empty() ||
            !inputs.TryGetValue("f32bmp_phase", out var p) || !(p is Mat phase) || phase.Empty())
            return new Dictionary<string, object> { ["f32bmp"] = null };
        if (magnitude.Size() != phase.Size()) throw new ArgumentException("幅度谱和相位谱的尺寸必须相同。");
        if (_encoding.Count > 0 && (!_encoding.TryGetValue("EncodingVersion", out var version) || Convert.ToInt32(version) != 2))
            throw new ArgumentException("频谱编码版本不兼容。请使用配套的正变换节点重新生成频谱。");
        int rows = Length("OriginalRows", magnitude.Rows), cols = Length("OriginalCols", magnitude.Cols);
        if (rows < 1 || rows > magnitude.Rows || cols < 1 || cols > magnitude.Cols)
            throw new ArgumentException("原始图像尺寸超出频谱范围。");
        bool log = AutoDetectParameters ? Flag("ApplyLogTransform", WasLogTransformed) : WasLogTransformed;
        bool center = AutoDetectParameters ? Flag("CenterFFT", WasCentered) : WasCentered;
        bool windowed = AutoDetectParameters ? Flag("ApplyWindow", WasWindowed) : WasWindowed;
        bool enhancePhase = AutoDetectParameters ? Flag("EnhancePhaseContrast", WasPhaseEnhanced) : WasPhaseEnhanced;
        bool alpha = AutoDetectParameters ? Flag("TransformAlpha", WasAlphaTransformed) : WasAlphaTransformed;
        bool enhanceMagnitude = Flag("EnhancePreview", true);
        double scale = MagnitudeScale;
        if (!(scale > 0 && scale <= 100) || !Enum.IsDefined(typeof(ReconstructionQuality), Quality))
            throw new ArgumentException("请选择有效的重建质量和 (0, 100] 范围内的幅度倍率。");
        bool fastPhase = Quality == ReconstructionQuality.Low || Quality == ReconstructionQuality.Medium;
        bool doubleDft = Quality == ReconstructionQuality.Medium || Quality == ReconstructionQuality.Ultra;
        const bool crop = true;
        using var magRgba = EnsureRGBAFormat(magnitude);
        using var phaseRgba = EnsureRGBAFormat(phase);
        var magnitudes = Cv2.Split(magRgba);
        var phases = Cv2.Split(phaseRgba);
        var channels = new Mat[4];
        var result = new Mat();
        try
        {
            for (int c = 0; c < channels.Length; c++)
            {
                if (c == 3 && !alpha) channels[c] = magnitudes[c].Clone();
                else channels[c] = Reconstruct(magnitudes[c], phases[c], log, center, enhanceMagnitude,
                    enhancePhase, windowed, rows, cols, scale, fastPhase, doubleDft);
            }
            using var merged = new Mat();
            Cv2.Merge(channels, merged);
            int outputRows = crop ? rows : merged.Rows, outputCols = crop ? cols : merged.Cols;
            using var roi = new Mat(merged, new Rect(0, 0, outputCols, outputRows));
            roi.CopyTo(result);
            return new Dictionary<string, object> { ["f32bmp"] = result };
        }
        catch
        {
            result.Dispose();
            throw;
        }
        finally
        {
            foreach (var channel in magnitudes) channel.Dispose();
            foreach (var channel in phases) channel.Dispose();
            foreach (var channel in channels) channel?.Dispose();
        }
    }

    private static Mat Reconstruct(Mat magnitude, Mat phase, bool log, bool center, bool enhanceMagnitude,
        bool enhancePhase, bool windowed, int originalRows, int originalCols, double scale, bool fastPhase, bool doubleDft)
    {
        int rows = magnitude.Rows, cols = magnitude.Cols;
        magnitude.GetArray(out float[] amplitudes);
        phase.GetArray(out float[] angles);
        double norm = log ? Math.Log(1.0 + amplitudes.Length) : amplitudes.Length;
        var first = new double[amplitudes.Length];
        var second = new double[amplitudes.Length];
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < cols; x++)
            {
                // Read the forward-shifted bin, including the unmatched row or column of an odd dimension.
                int i = center ? ((y + rows / 2) % rows) * cols + (x + cols / 2) % cols : y * cols + x;
                double m = amplitudes[i], a = angles[i];
                m = (enhanceMagnitude ? m * m : m) * norm;
                if (log) m = Math.Exp(m) - 1;
                m *= scale;
                a = (enhancePhase ? a * a : a) * (2 * Math.PI);
                int k = y * cols + x;
                first[k] = fastPhase ? m : m * Math.Cos(a);
                second[k] = fastPhase ? a : m * Math.Sin(a);
            }
        using var a0 = new Mat(rows, cols, MatType.CV_64F);
        using var a1 = new Mat(rows, cols, MatType.CV_64F);
        a0.SetArray(first);
        a1.SetArray(second);
        using var real = new Mat();
        using var imaginary = new Mat();
        using var spectrum = new Mat();
        if (fastPhase)
        {
            Cv2.PolarToCart(a0, a1, real, imaginary);
            Cv2.Merge(new[] { real, imaginary }, spectrum);
        }
        else Cv2.Merge(new[] { a0, a1 }, spectrum);
        using var working = new Mat();
        spectrum.ConvertTo(working, doubleDft ? MatType.CV_64F : MatType.CV_32F);
        using var spatial = new Mat();
        Cv2.Idft(working, spatial, DftFlags.Scale | DftFlags.RealOutput);
        var result = new Mat();
        spatial.ConvertTo(result, MatType.CV_32F);
        if (windowed)
        {
            var wx = WindowAxis(originalCols);
            var wy = WindowAxis(originalRows);
            result.GetArray(out float[] pixels);
            for (int y = 0; y < originalRows; y++)
                for (int x = 0; x < originalCols; x++)
                {
                    double weight = wx[x] * wy[y];
                    int i = y * cols + x;
                    pixels[i] = weight > 1e-6 ? (float)(pixels[i] / weight) : 0;
                }
            result.SetArray(pixels);
        }
        return result;
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
        // 调用基类方法
        var metadata = base.InjectMetadata(currentMetadata);

        // 注入傅里叶逆变换相关的元数据
        if (!metadata.ContainsKey("傅里叶逆变换参数"))
        {
            metadata["傅里叶逆变换参数"] = new Dictionary<string, object>
            {
                ["AutoDetectParameters"] = AutoDetectParameters,
                ["WasLogTransformed"] = WasLogTransformed,
                ["WasCentered"] = WasCentered,
                ["WasWindowed"] = WasWindowed,
                ["WasPhaseEnhanced"] = WasPhaseEnhanced,
                ["WasAlphaTransformed"] = WasAlphaTransformed,
                ["Quality"] = Quality.ToString(),
                ["MagnitudeScale"] = MagnitudeScale
            };
        }

        // 注入重建信息
        if (!metadata.ContainsKey("傅里叶重建信息"))
        {
            metadata["傅里叶重建信息"] = new Dictionary<string, object>
            {
                ["NodeInstanceId"] = NodeInstanceId,
                ["ReconstructionTimestamp"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                ["Version"] = "1.0"
            };
        }

        if (!metadata.ContainsKey("处理历史"))
        {
            metadata["处理历史"] = "傅里叶逆变换";
        }
        else
        {
            metadata["处理历史"] = metadata["处理历史"] + " → 傅里叶逆变换";
        }

        return metadata;
    }

    public override FrameworkElement CreateParameterControl()
    {
        var mainPanel = new StackPanel { Margin = new Thickness(5) };

        // 加载所有需要的资源字典
        var resources = new ResourceDictionary();
        var resourcePaths = new[]
        {
            "/Tunnel-Next;component/Resources/ScriptsControls/SharedBrushes.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/LabelStyles.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/CheckBoxStyles.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/ComboBoxStyles.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/SliderStyles.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/TextBlockStyles.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/ExpanderStyles.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/ScriptButtonStyles.xaml"
        };

        foreach (var path in resourcePaths)
        {
            try
            {
                resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(path, UriKind.Relative) });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"资源加载失败: {path} - {ex.Message}");
            }
        }
        
        if (resources.Contains("Layer_2"))
        {
            mainPanel.Background = resources["Layer_2"] as Brush;
        }

        var viewModel = CreateViewModel() as InverseFourierTransformViewModel;
        mainPanel.DataContext = viewModel;

        var titleLabel = new Label
        {
            Content = "傅里叶逆变换设置",
        };
        if (resources.Contains("TitleLabelStyle"))
        {
            titleLabel.Style = resources["TitleLabelStyle"] as Style;
        }
        mainPanel.Children.Add(titleLabel);

        // 自动检测参数
        mainPanel.Children.Add(CreateBindingCheckBox("自动检测参数", "AutoDetectParameters", viewModel, resources));

        // 手动参数区域
        var manualParamsExpander = new Expander
        {
            Header = "手动参数设置",
            IsExpanded = !viewModel.AutoDetectParameters,
            Margin = new Thickness(0, 5, 0, 0)
        };
        if (resources.Contains("DefaultExpanderStyle"))
        {
            manualParamsExpander.Style = resources["DefaultExpanderStyle"] as Style;
        }

        var manualParamsPanel = new StackPanel { Margin = new Thickness(15, 0, 0, 0) };
        manualParamsPanel.Children.Add(CreateBindingCheckBox("应用了对数变换", "WasLogTransformed", viewModel, resources));
        manualParamsPanel.Children.Add(CreateBindingCheckBox("零频率已居中", "WasCentered", viewModel, resources));
        manualParamsPanel.Children.Add(CreateBindingCheckBox("应用了汉宁窗", "WasWindowed", viewModel, resources));
        manualParamsPanel.Children.Add(CreateBindingCheckBox("增强了相位谱对比度", "WasPhaseEnhanced", viewModel, resources));
        manualParamsPanel.Children.Add(CreateBindingCheckBox("变换了Alpha通道", "WasAlphaTransformed", viewModel, resources));
        manualParamsExpander.Content = manualParamsPanel;
        
        // 绑定Expander的IsExpanded到!AutoDetectParameters
        var expanderBinding = new Binding("AutoDetectParameters")
        {
            Source = viewModel,
            Converter = new InvertBooleanConverter()
        };
        manualParamsExpander.SetBinding(Expander.IsExpandedProperty, expanderBinding);
        
        mainPanel.Children.Add(manualParamsExpander);

        // 重建质量
        manualParamsPanel.Children.Add(CreateLabel("重建质量:", resources));
        var qualityComboBox = new ComboBox
        {
            ItemsSource = Enum.GetValues(typeof(ReconstructionQuality)),
            Margin = new Thickness(0, 2, 0, 10)
        };
        if (resources.Contains("DefaultComboBoxStyle"))
        {
            qualityComboBox.Style = resources["DefaultComboBoxStyle"] as Style;
        }
        qualityComboBox.SetBinding(ComboBox.SelectedItemProperty, new Binding("Quality") { Source = viewModel, Mode = BindingMode.TwoWay });
        manualParamsPanel.Children.Add(qualityComboBox);

        // 幅度缩放因子
        manualParamsPanel.Children.Add(CreateLabel("幅度缩放因子:", resources));
        var scaleSlider = new Slider
        {
            Minimum = 0.1,
            Maximum = 5.0,
            SmallChange = 0.1,
            LargeChange = 0.5,
            Margin = new Thickness(0, 2, 0, 10)
        };
        if (resources.Contains("DefaultSliderStyle"))
        {
            scaleSlider.Style = resources["DefaultSliderStyle"] as Style;
        }
        scaleSlider.SetBinding(Slider.ValueProperty, new Binding("MagnitudeScale") { Source = viewModel, Mode = BindingMode.TwoWay });
        manualParamsPanel.Children.Add(scaleSlider);

        // 操作按钮
        var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };

        var detectButton = new Button
        {
            Content = "重新检测参数",
            Margin = new Thickness(0, 0, 5, 0)
        };
        if (resources.Contains("ScriptButtonStyle"))
        {
            detectButton.Style = resources["ScriptButtonStyle"] as Style;
        }
        detectButton.Click += (s, e) => viewModel?.ForceDetectParameters();
        buttonPanel.Children.Add(detectButton);

        var resetButton = new Button
        {
            Content = "重置所有参数"
        };
        if (resources.Contains("ScriptButtonStyle"))
        {
            resetButton.Style = resources["ScriptButtonStyle"] as Style;
        }
        resetButton.Click += async (s, e) => await viewModel?.ResetToDefault();
        buttonPanel.Children.Add(resetButton);

        mainPanel.Children.Add(buttonPanel);

        return mainPanel;
    }

    private Label CreateLabel(string content, ResourceDictionary resources)
    {
        var label = new Label { Content = content };
        if (resources.Contains("DefaultLabelStyle"))
        {
            label.Style = resources["DefaultLabelStyle"] as Style;
        }
        return label;
    }

    private CheckBox CreateBindingCheckBox(string content, string propertyName, object viewModel, ResourceDictionary resources)
    {
        var checkBox = new CheckBox
        {
            Content = content,
            Margin = new Thickness(0, 5, 0, 5),
        };

        if (resources.Contains("DefaultCheckBoxStyle"))
        {
            checkBox.Style = resources["DefaultCheckBoxStyle"] as Style;
        }

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
        return new InverseFourierTransformViewModel(this);
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
            [nameof(AutoDetectParameters)] = AutoDetectParameters,
            [nameof(WasLogTransformed)] = WasLogTransformed,
            [nameof(WasCentered)] = WasCentered,
            [nameof(WasWindowed)] = WasWindowed,
            [nameof(WasPhaseEnhanced)] = WasPhaseEnhanced,
            [nameof(WasAlphaTransformed)] = WasAlphaTransformed,
            [nameof(Quality)] = Quality.ToString(),
            [nameof(MagnitudeScale)] = MagnitudeScale,
            ["NodeInstanceId"] = NodeInstanceId
        };
    }

    public override void DeserializeParameters(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(AutoDetectParameters), out var autoDetect))
            AutoDetectParameters = Convert.ToBoolean(autoDetect);
        if (data.TryGetValue(nameof(WasLogTransformed), out var wasLog))
            WasLogTransformed = Convert.ToBoolean(wasLog);
        if (data.TryGetValue(nameof(WasCentered), out var wasCentered))
            WasCentered = Convert.ToBoolean(wasCentered);
        if (data.TryGetValue(nameof(WasWindowed), out var wasWindowed))
            WasWindowed = Convert.ToBoolean(wasWindowed);
        if (data.TryGetValue(nameof(WasPhaseEnhanced), out var wasPhaseEnhanced))
            WasPhaseEnhanced = Convert.ToBoolean(wasPhaseEnhanced);
        if (data.TryGetValue(nameof(WasAlphaTransformed), out var wasAlphaTransformed))
            WasAlphaTransformed = Convert.ToBoolean(wasAlphaTransformed);
        if (data.TryGetValue(nameof(Quality), out var quality))
            Quality = Enum.Parse<ReconstructionQuality>(quality.ToString());
        if (data.TryGetValue(nameof(MagnitudeScale), out var magnitudeScale))
            MagnitudeScale = Convert.ToDouble(magnitudeScale);
        if (data.TryGetValue("NodeInstanceId", out var nodeId))
            NodeInstanceId = nodeId?.ToString() ?? string.Empty;
    }

    public void InitializeNodeInstance(string nodeId)
    {
        NodeInstanceId = nodeId;
    }

    /// <summary>
    /// 强制从元数据中检测参数
    /// </summary>
    public void ForceParameterDetection()
    {
        // 确保元数据已提取
        if (_encoding.Count > 0)
        {
            WasLogTransformed = Flag("ApplyLogTransform", WasLogTransformed);
            WasCentered = Flag("CenterFFT", WasCentered);
            WasWindowed = Flag("ApplyWindow", WasWindowed);
            WasPhaseEnhanced = Flag("EnhancePhaseContrast", WasPhaseEnhanced);
            WasAlphaTransformed = Flag("TransformAlpha", WasAlphaTransformed);

            // 手动触发ViewModel更新，确保UI反映变化
            if (CreateViewModel() is InverseFourierTransformViewModel viewModel)
            {
                viewModel.RaiseAllPropertiesChanged();
            }
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("[傅里叶逆变换] 没有可用的元数据来强制检测。");
        }
    }
}

public class InvertBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is bool)
        {
            return !(bool)value;
        }
        return value;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is bool)
        {
            return !(bool)value;
        }
        return value;
    }
}

public class InverseFourierTransformViewModel : ScriptViewModelBase
{
    private InverseFourierTransformScript InverseFourierTransformScript => (InverseFourierTransformScript)Script;

    public void RaiseAllPropertiesChanged()
    {
        OnPropertyChanged(nameof(AutoDetectParameters));
        OnPropertyChanged(nameof(WasLogTransformed));
        OnPropertyChanged(nameof(WasCentered));
        OnPropertyChanged(nameof(WasWindowed));
        OnPropertyChanged(nameof(WasPhaseEnhanced));
        OnPropertyChanged(nameof(WasAlphaTransformed));
        OnPropertyChanged(nameof(Quality));
        OnPropertyChanged(nameof(MagnitudeScale));
    }

    public bool AutoDetectParameters
    {
        get => InverseFourierTransformScript.AutoDetectParameters;
        set
        {
            if (InverseFourierTransformScript.AutoDetectParameters != value)
            {
                InverseFourierTransformScript.AutoDetectParameters = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(AutoDetectParameters), value);
            }
        }
    }

    public bool WasLogTransformed
    {
        get => InverseFourierTransformScript.WasLogTransformed;
        set
        {
            if (InverseFourierTransformScript.WasLogTransformed != value)
            {
                InverseFourierTransformScript.WasLogTransformed = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(WasLogTransformed), value);
            }
        }
    }

    public bool WasCentered
    {
        get => InverseFourierTransformScript.WasCentered;
        set
        {
            if (InverseFourierTransformScript.WasCentered != value)
            {
                InverseFourierTransformScript.WasCentered = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(WasCentered), value);
            }
        }
    }

    public bool WasWindowed
    {
        get => InverseFourierTransformScript.WasWindowed;
        set
        {
            if (InverseFourierTransformScript.WasWindowed != value)
            {
                InverseFourierTransformScript.WasWindowed = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(WasWindowed), value);
            }
        }
    }

    public bool WasPhaseEnhanced
    {
        get => InverseFourierTransformScript.WasPhaseEnhanced;
        set
        {
            if (InverseFourierTransformScript.WasPhaseEnhanced != value)
            {
                InverseFourierTransformScript.WasPhaseEnhanced = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(WasPhaseEnhanced), value);
            }
        }
    }

    public bool WasAlphaTransformed
    {
        get => InverseFourierTransformScript.WasAlphaTransformed;
        set
        {
            if (InverseFourierTransformScript.WasAlphaTransformed != value)
            {
                InverseFourierTransformScript.WasAlphaTransformed = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(WasAlphaTransformed), value);
            }
        }
    }

    public ReconstructionQuality Quality
    {
        get => InverseFourierTransformScript.Quality;
        set
        {
            if (InverseFourierTransformScript.Quality != value)
            {
                InverseFourierTransformScript.Quality = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(Quality), value);
            }
        }
    }

    public double MagnitudeScale
    {
        get => InverseFourierTransformScript.MagnitudeScale;
        set
        {
            if (Math.Abs(InverseFourierTransformScript.MagnitudeScale - value) > 1e-6)
            {
                InverseFourierTransformScript.MagnitudeScale = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(MagnitudeScale), value);
            }
        }
    }

    public InverseFourierTransformViewModel(InverseFourierTransformScript script) : base(script)
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
        AutoDetectParameters = true;
        WasLogTransformed = true;
        WasCentered = true;
        WasWindowed = false;
        WasPhaseEnhanced = true;
        WasAlphaTransformed = false;
        Quality = ReconstructionQuality.High;
        MagnitudeScale = 1.0;
        await Task.CompletedTask;
    }

    public void ForceDetectParameters()
    {
        // 强制重新检测参数
        AutoDetectParameters = true;

        // 如果脚本实例可用，强制重新检测
        if (InverseFourierTransformScript != null)
        {
            InverseFourierTransformScript.ForceParameterDetection();
        }

        // 通知参数已更改
        OnPropertyChanged(nameof(AutoDetectParameters));
        OnPropertyChanged(nameof(WasLogTransformed));
        OnPropertyChanged(nameof(WasCentered));
        OnPropertyChanged(nameof(WasWindowed));
        OnPropertyChanged(nameof(WasPhaseEnhanced));
        OnPropertyChanged(nameof(WasAlphaTransformed));
    }

    public override async Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
    {
        await Task.CompletedTask;
    }

    public override ScriptValidationResult ValidateParameter(string parameterName, object value)
    {
        if (parameterName == nameof(MagnitudeScale))
        {
            if (value is double scale)
            {
                if (scale <= 0)
                {
                    return new ScriptValidationResult(false, "幅度缩放因子必须大于0");
                }
                if (scale > 100)
                {
                    return new ScriptValidationResult(false, "幅度缩放因子不应超过100");
                }
            }
        }

        return new ScriptValidationResult(true);
    }

    public override Dictionary<string, object> GetParameterData()
    {
        return new Dictionary<string, object>
        {
            [nameof(AutoDetectParameters)] = AutoDetectParameters,
            [nameof(WasLogTransformed)] = WasLogTransformed,
            [nameof(WasCentered)] = WasCentered,
            [nameof(WasWindowed)] = WasWindowed,
            [nameof(WasPhaseEnhanced)] = WasPhaseEnhanced,
            [nameof(WasAlphaTransformed)] = WasAlphaTransformed,
            [nameof(Quality)] = Quality,
            [nameof(MagnitudeScale)] = MagnitudeScale
        };
    }

    public override async Task SetParameterDataAsync(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(AutoDetectParameters), out var autoDetect))
            AutoDetectParameters = Convert.ToBoolean(autoDetect);
        if (data.TryGetValue(nameof(WasLogTransformed), out var wasLog))
            WasLogTransformed = Convert.ToBoolean(wasLog);
        if (data.TryGetValue(nameof(WasCentered), out var wasCentered))
            WasCentered = Convert.ToBoolean(wasCentered);
        if (data.TryGetValue(nameof(WasWindowed), out var wasWindowed))
            WasWindowed = Convert.ToBoolean(wasWindowed);
        if (data.TryGetValue(nameof(WasPhaseEnhanced), out var wasPhaseEnhanced))
            WasPhaseEnhanced = Convert.ToBoolean(wasPhaseEnhanced);
        if (data.TryGetValue(nameof(WasAlphaTransformed), out var wasAlphaTransformed))
            WasAlphaTransformed = Convert.ToBoolean(wasAlphaTransformed);
        if (data.TryGetValue(nameof(Quality), out var quality))
            Quality = (ReconstructionQuality)quality;
        if (data.TryGetValue(nameof(MagnitudeScale), out var magnitudeScale))
            MagnitudeScale = Convert.ToDouble(magnitudeScale);
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