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
using Rect = OpenCvSharp.Rect;

[TunnelExtensionScript(
    Name = "傅里叶逆变换A",
    Author = "BEITAware",
    Description = "傅里叶逆变换 - 从幅度谱和相位谱重建图像，支持RGBA格式",
    Version = "1.0",
    Category = "频域处理",
    Color = "#FF6600"
)]
public class InverseFourierTransformScript : TunnelExtensionScriptBase
{
    [ScriptParameter(DisplayName = "逆对数变换", Description = "对幅度谱应用逆对数变换", Order = 0)]
    public bool InverseLogTransform { get; set; } = true;

    [ScriptParameter(DisplayName = "零频率居中", Description = "零频率位于频谱中心", Order = 1)]
    public bool CenterFFT { get; set; } = true;

    [ScriptParameter(DisplayName = "保持原始尺寸", Description = "裁剪结果图像到原始尺寸", Order = 2)]
    public bool PreserveOriginalSize { get; set; } = true;

    [ScriptParameter(DisplayName = "归一化输出", Description = "归一化输出图像到0-1范围", Order = 3)]
    public bool NormalizeOutput { get; set; } = false;

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
            ["f32bmp"] = new PortDefinition("f32bmp", false, "重建图像")
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
        bool log = InverseLogTransform, center = CenterFFT, crop = PreserveOriginalSize;
        bool enhanceMagnitude = Flag("EnhancePreview", true), enhancePhase = Flag("EnhancePhaseContrast", true);
        bool alpha = Flag("TransformAlpha", false), windowed = Flag("ApplyWindow", false);
        const bool fastPhase = false, doubleDft = false;
        const double scale = 1;
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
            if (NormalizeOutput)
                for (int c = 0; c < 3; c++)
                {
                    using var visible = new Mat(channels[c], new Rect(0, 0, crop ? cols : magnitude.Cols, crop ? rows : magnitude.Rows));
                    Cv2.Normalize(visible, visible, 0, 1, NormTypes.MinMax);
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
                ["InverseLogTransform"] = InverseLogTransform,
                ["CenterFFT"] = CenterFFT,
                ["PreserveOriginalSize"] = PreserveOriginalSize,
                ["NormalizeOutput"] = NormalizeOutput
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
            "/Tunnel-Next;component/Resources/ScriptsControls/TextBlockStyles.xaml"
        };

        foreach (var path in resourcePaths)
        {
            try
            {
                resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(path, UriKind.Relative) });
            }
            catch (Exception ex)
            {
                // 可以记录资源加载失败的日志
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

        // 创建复选框
        mainPanel.Children.Add(CreateBindingCheckBox("逆对数变换", "InverseLogTransform", viewModel, resources));
        mainPanel.Children.Add(CreateBindingCheckBox("零频率居中", "CenterFFT", viewModel, resources));
        mainPanel.Children.Add(CreateBindingCheckBox("保持原始尺寸", "PreserveOriginalSize", viewModel, resources));
        mainPanel.Children.Add(CreateBindingCheckBox("归一化输出", "NormalizeOutput", viewModel, resources));

        return mainPanel;
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
            [nameof(InverseLogTransform)] = InverseLogTransform,
            [nameof(CenterFFT)] = CenterFFT,
            [nameof(PreserveOriginalSize)] = PreserveOriginalSize,
            [nameof(NormalizeOutput)] = NormalizeOutput,
            ["NodeInstanceId"] = NodeInstanceId
        };
    }

    public override void DeserializeParameters(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(InverseLogTransform), out var inverseLog))
            InverseLogTransform = Convert.ToBoolean(inverseLog);
        if (data.TryGetValue(nameof(CenterFFT), out var centerFFT))
            CenterFFT = Convert.ToBoolean(centerFFT);
        if (data.TryGetValue(nameof(PreserveOriginalSize), out var preserveSize))
            PreserveOriginalSize = Convert.ToBoolean(preserveSize);
        if (data.TryGetValue(nameof(NormalizeOutput), out var normalizeOutput))
            NormalizeOutput = Convert.ToBoolean(normalizeOutput);
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

public class InverseFourierTransformViewModel : ScriptViewModelBase
{
    private InverseFourierTransformScript InverseFourierTransformScript => (InverseFourierTransformScript)Script;

    public bool InverseLogTransform
    {
        get => InverseFourierTransformScript.InverseLogTransform;
        set
        {
            if (InverseFourierTransformScript.InverseLogTransform != value)
            {
                InverseFourierTransformScript.InverseLogTransform = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(InverseLogTransform), value);
            }
        }
    }

    public bool CenterFFT
    {
        get => InverseFourierTransformScript.CenterFFT;
        set
        {
            if (InverseFourierTransformScript.CenterFFT != value)
            {
                InverseFourierTransformScript.CenterFFT = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(CenterFFT), value);
            }
        }
    }

    public bool PreserveOriginalSize
    {
        get => InverseFourierTransformScript.PreserveOriginalSize;
        set
        {
            if (InverseFourierTransformScript.PreserveOriginalSize != value)
            {
                InverseFourierTransformScript.PreserveOriginalSize = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(PreserveOriginalSize), value);
            }
        }
    }

    public bool NormalizeOutput
    {
        get => InverseFourierTransformScript.NormalizeOutput;
        set
        {
            if (InverseFourierTransformScript.NormalizeOutput != value)
            {
                InverseFourierTransformScript.NormalizeOutput = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(NormalizeOutput), value);
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
        InverseLogTransform = true;
        CenterFFT = true;
        PreserveOriginalSize = true;
        NormalizeOutput = false;
        await Task.CompletedTask;
    }

    public void ApplyFrequencyFilter()
    {
        // 这里可以实现频率域滤波的逻辑
        // 目前只是一个占位符功能
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
            [nameof(InverseLogTransform)] = InverseLogTransform,
            [nameof(CenterFFT)] = CenterFFT,
            [nameof(PreserveOriginalSize)] = PreserveOriginalSize,
            [nameof(NormalizeOutput)] = NormalizeOutput
        };
    }

    public override async Task SetParameterDataAsync(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(InverseLogTransform), out var inverseLog))
            InverseLogTransform = Convert.ToBoolean(inverseLog);
        if (data.TryGetValue(nameof(CenterFFT), out var centerFFT))
            CenterFFT = Convert.ToBoolean(centerFFT);
        if (data.TryGetValue(nameof(PreserveOriginalSize), out var preserveSize))
            PreserveOriginalSize = Convert.ToBoolean(preserveSize);
        if (data.TryGetValue(nameof(NormalizeOutput), out var normalizeOutput))
            NormalizeOutput = Convert.ToBoolean(normalizeOutput);
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
