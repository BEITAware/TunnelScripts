using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Tunnel_Next.Services.Scripting;
using OpenCvSharp;
using System.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks;

[TunnelExtensionScript(
    Name = "图层混合",
    Author = "BEITAware",
    Description = "多图层混合工具",
    Version = "2.0",
    Category = "图像处理",
    Color = "#FF6B35"
)]
public class FlexibleImageBlendScript : TunnelExtensionScriptBase
{
    public enum BlendMode
    {
        // 基础混合模式
        Normal,         // 正常
        Average,        // 平均混合
        Add,            // 相加混合
        Max,            // 最大值
        Min,            // 最小值

        // 变暗混合模式
        Darken,         // 变暗
        Multiply,       // 正片叠底
        ColorBurn,      // 颜色加深
        LinearBurn,     // 线性加深

        // 变亮混合模式
        Lighten,        // 变亮
        Screen,         // 滤色
        ColorDodge,     // 颜色减淡
        LinearDodge,    // 线性减淡(添加)

        // 对比混合模式
        Overlay,        // 叠加
        SoftLight,      // 柔光
        HardLight,      // 强光
        VividLight,     // 亮光
        LinearLight,    // 线性光
        PinLight,       // 点光
        HardMix,        // 实色混合

        // 差异混合模式
        Difference,     // 差值
        Exclusion,      // 排除
        Subtract,       // 减去
        Divide          // 划分
    }

    [ScriptParameter(DisplayName = "混合模式", Description = "选择图像混合模式")]
    public BlendMode MixMode { get; set; } = BlendMode.Normal;

    [ScriptParameter(DisplayName = "输出强度", Description = "输出图像强度调节 (0.1-2.0)")]
    public float OutputIntensity { get; set; } = 1.0f;

    [ScriptParameter(DisplayName = "全局不透明度", Description = "所有图层的全局不透明度 (0.0-1.0)")]
    public float GlobalOpacity { get; set; } = 1.0f;

    [ScriptParameter(DisplayName = "尊重Alpha通道", Description = "是否考虑输入图像的Alpha通道")]
    public bool RespectAlpha { get; set; } = true;

    [ScriptParameter(DisplayName = "启用并行处理", Description = "对大图像启用多线程并行处理")]
    public bool EnableParallelProcessing { get; set; } = true;

    [ScriptParameter(DisplayName = "逆序混合", Description = "是否反转图层混合顺序")]
    public bool ReverseOrder { get; set; } = false;

    public override Dictionary<string, PortDefinition> GetInputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            // 第一个输入端口是固定的
            ["Input1"] = new PortDefinition("f32bmp", false, "第一个图像输入"),

            // 第二个输入端口是灵活的，连接后会自动添加更多端口
            ["Input2"] = new PortDefinition("f32bmp", true, "灵活图像输入 - 连接后会自动添加更多端口")
        };
    }

    public override Dictionary<string, PortDefinition> GetOutputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            ["Output"] = new PortDefinition("f32bmp", false, "混合后的图像输出")
        };
    }

    public override Dictionary<string, object> Process(Dictionary<string, object> inputs, IScriptContext context)
    {
        var ordered = inputs.Where(pair => pair.Value is Mat image && !image.Empty())
            .OrderBy(pair => pair.Key.StartsWith("F32bmp_", StringComparison.OrdinalIgnoreCase))
            .ThenBy(pair => System.Text.RegularExpressions.Regex.Replace(pair.Key, @"\d+", m => m.Value.PadLeft(10, '0')),
                    StringComparer.OrdinalIgnoreCase)
            .Select(pair => (Mat)pair.Value).ToList();
        if (ordered.Count == 0) return new Dictionary<string, object>();
        if (!ReverseOrder) ordered.Reverse();

        var size = ordered[0].Size();
        var result = new Mat(size, MatType.CV_32FC4, Scalar.All(0));
        try
        {
            for (int i = 0; i < ordered.Count; i++)
            {
                using var layer = ToBgra(ordered[i]);
                if (layer.Size() != size) Cv2.Resize(layer, layer, size);
                BlendOver(result, layer, i == 0 ? BlendMode.Normal : MixMode);
            }
            if (OutputIntensity != 1f)
            {
                using var alpha = new Mat();
                Cv2.ExtractChannel(result, alpha, 3);
                result.ConvertTo(result, -1, OutputIntensity);
                Cv2.InsertChannel(alpha, result, 3);
            }
            return new Dictionary<string, object> { ["Output"] = result };
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    private static Mat ToBgra(Mat input)
    {
        using var normalized = new Mat();
        double scale = input.Depth() == MatType.CV_8U ? 1.0 / 255 :
                       input.Depth() == MatType.CV_16U ? 1.0 / 65535 : 1;
        input.ConvertTo(normalized, MatType.CV_32F, scale);
        var output = new Mat();
        switch (normalized.Channels())
        {
            case 1: Cv2.CvtColor(normalized, output, ColorConversionCodes.GRAY2BGRA); break;
            case 3: Cv2.CvtColor(normalized, output, ColorConversionCodes.BGR2BGRA); break;
            case 4: normalized.CopyTo(output); break;
            default: throw new ArgumentException("图层需要 1、3 或 4 个通道");
        }
        return output;
    }

    private unsafe void BlendOver(Mat result, Mat source, BlendMode mode)
    {
        void BlendRow(int y)
        {
            var bottom = (float*)result.Ptr(y);
            var top = (float*)source.Ptr(y);
            for (int x = 0; x < result.Cols; x++, bottom += 4, top += 4)
            {
                float srcAlpha = Math.Clamp(RespectAlpha ? top[3] : 1f, 0f, 1f) * GlobalOpacity;
                if (srcAlpha <= 0) continue;
                float dstAlpha = bottom[3];
                float outAlpha = srcAlpha + dstAlpha * (1 - srcAlpha);
                for (int c = 0; c < 3; c++)
                {
                    float blend = BlendComponent(bottom[c], top[c], mode);
                    float srcColor = top[c] * (1 - dstAlpha) + blend * dstAlpha;
                    bottom[c] = (srcColor * srcAlpha + bottom[c] * dstAlpha * (1 - srcAlpha)) / outAlpha;
                }
                bottom[3] = outAlpha;
            }
        }
        if (EnableParallelProcessing && result.Total() > 10000)
            Parallel.For(0, result.Rows, BlendRow);
        else
            for (int y = 0; y < result.Rows; y++) BlendRow(y);
    }

    private static float Burn(float b, float s) => b >= 1 ? 1 : s <= 0 ? 0 : 1 - Math.Min(1f, (1 - b) / s);
    private static float Dodge(float b, float s) => b <= 0 ? 0 : s >= 1 ? 1 : Math.Min(1f, b / (1 - s));
    private static float VividLight(float b, float s) => s < 0.5f ? Burn(b, 2*s) : Dodge(b, 2*s - 1);

    private static float BlendComponent(float b, float s, BlendMode mode)
    {
        switch (mode)
        {
            case BlendMode.Normal: return s;
            case BlendMode.Average: return (b + s) / 2;
            case BlendMode.Add: return b + s;
            case BlendMode.Max: case BlendMode.Lighten: return Math.Max(b, s);
            case BlendMode.Min: case BlendMode.Darken: return Math.Min(b, s);
            case BlendMode.Multiply: return b * s;
            case BlendMode.ColorBurn: return Burn(b, s);
            case BlendMode.LinearBurn: return Math.Max(0, b + s - 1);
            case BlendMode.Screen: return b + s - b * s;
            case BlendMode.ColorDodge: return Dodge(b, s);
            case BlendMode.LinearDodge: return Math.Min(1, b + s);
            case BlendMode.Overlay: return b < 0.5f ? 2*b*s : 1 - 2*(1-b)*(1-s);
            case BlendMode.HardLight: return s < 0.5f ? 2*b*s : 1 - 2*(1-b)*(1-s);
            case BlendMode.SoftLight:
                float d = b <= 0.25f ? ((16*b - 12)*b + 4)*b : MathF.Sqrt(b);
                return s <= 0.5f ? b - (1 - 2*s)*b*(1-b) : b + (2*s - 1)*(d-b);
            case BlendMode.VividLight: return VividLight(b, s);
            case BlendMode.LinearLight: return Math.Clamp(b + 2*s - 1, 0, 1);
            case BlendMode.PinLight: return s < 0.5f ? Math.Min(b, 2*s) : Math.Max(b, 2*s - 1);
            case BlendMode.HardMix: return VividLight(b, s) < 0.5f ? 0 : 1;
            case BlendMode.Difference: return Math.Abs(b - s);
            case BlendMode.Exclusion: return b + s - 2*b*s;
            case BlendMode.Subtract: return Math.Max(0, b - s);
            case BlendMode.Divide: return s == 0 ? 1 : Math.Min(1, b / s);
            default: throw new ArgumentOutOfRangeException(nameof(mode));
        }
    }

    public override FrameworkElement CreateParameterControl()
    {
        var panel = new StackPanel { Margin = new Thickness(5) };

        // 应用Aero主题样式 - 使用interfacepanelbar的渐变背景
        panel.Background = new LinearGradientBrush(
            new GradientStopCollection
            {
                new GradientStop((Color)ColorConverter.ConvertFromString("#FF1A1F28"), 0),
                new GradientStop((Color)ColorConverter.ConvertFromString("#FF1C2432"), 0.510204),
                new GradientStop((Color)ColorConverter.ConvertFromString("#FE1C2533"), 0.562152),
                new GradientStop((Color)ColorConverter.ConvertFromString("#FE30445F"), 0.87013),
                new GradientStop((Color)ColorConverter.ConvertFromString("#FE384F6C"), 0.918367),
                new GradientStop((Color)ColorConverter.ConvertFromString("#FF405671"), 0.974026)
            },
            new System.Windows.Point(0.499999, 0), new System.Windows.Point(0.499999, 1)
        );

        // 标题
        var title = new TextBlock
        {
            Text = "灵活端口图像混合",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 10),
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial")
        };
        panel.Children.Add(title);

        // 混合模式选择
        var modeLabel = new Label
        {
            Content = "混合模式:",
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11
        };
        var modeCombo = new ComboBox
        {
            Margin = new Thickness(0, 0, 0, 10),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F0F0F0")),
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#333333")),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A1F28")),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 4, 6, 4),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11
        };

        // 创建混合模式的友好显示名称
        var blendModeDisplayNames = new Dictionary<BlendMode, string>
        {
            { BlendMode.Normal, "正常" },
            { BlendMode.Average, "平均" },
            { BlendMode.Add, "相加" },
            { BlendMode.Max, "最大值" },
            { BlendMode.Min, "最小值" },
            { BlendMode.Darken, "变暗" },
            { BlendMode.Multiply, "正片叠底" },
            { BlendMode.ColorBurn, "颜色加深" },
            { BlendMode.LinearBurn, "线性加深" },
            { BlendMode.Lighten, "变亮" },
            { BlendMode.Screen, "滤色" },
            { BlendMode.ColorDodge, "颜色减淡" },
            { BlendMode.LinearDodge, "线性减淡" },
            { BlendMode.Overlay, "叠加" },
            { BlendMode.SoftLight, "柔光" },
            { BlendMode.HardLight, "强光" },
            { BlendMode.VividLight, "亮光" },
            { BlendMode.LinearLight, "线性光" },
            { BlendMode.PinLight, "点光" },
            { BlendMode.HardMix, "实色混合" },
            { BlendMode.Difference, "差值" },
            { BlendMode.Exclusion, "排除" },
            { BlendMode.Subtract, "减去" },
            { BlendMode.Divide, "划分" }
        };

        modeCombo.ItemsSource = blendModeDisplayNames;
        modeCombo.DisplayMemberPath = "Value";
        modeCombo.SelectedValuePath = "Key";
        modeCombo.SelectedValue = MixMode;
        modeCombo.SelectionChanged += (s, e) =>
        {
            if (modeCombo.SelectedValue is BlendMode newMode)
            {
                var oldValue = MixMode;
                MixMode = newMode;
                OnParameterChanged(nameof(MixMode), newMode);
            }
        };

        // 全局不透明度滑块
        var globalOpacityLabel = new Label
        {
            Content = $"全局不透明度: {GlobalOpacity:F2}",
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11
        };
        var globalOpacitySlider = new Slider
        {
            Minimum = 0.0,
            Maximum = 1.0,
            Value = GlobalOpacity,
            TickFrequency = 0.01,
            IsSnapToTickEnabled = false,
            Margin = new Thickness(0, 0, 0, 10),
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF"))
        };
        globalOpacitySlider.ValueChanged += (s, e) =>
        {
            var oldValue = GlobalOpacity;
            GlobalOpacity = (float)e.NewValue;
            globalOpacityLabel.Content = $"全局不透明度: {GlobalOpacity:F2}";
            OnParameterChanged(nameof(GlobalOpacity), GlobalOpacity);
        };

        // 输出强度滑块
        var intensityLabel = new Label
        {
            Content = $"输出强度: {OutputIntensity:F2}",
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11
        };
        var intensitySlider = new Slider
        {
            Minimum = 0.1,
            Maximum = 2.0,
            Value = OutputIntensity,
            TickFrequency = 0.1,
            IsSnapToTickEnabled = false,
            Margin = new Thickness(0, 0, 0, 10),
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF"))
        };
        intensitySlider.ValueChanged += (s, e) =>
        {
            var oldValue = OutputIntensity;
            OutputIntensity = (float)e.NewValue;
            intensityLabel.Content = $"输出强度: {OutputIntensity:F2}";
            OnParameterChanged(nameof(OutputIntensity), OutputIntensity);
        };

        // Alpha通道处理复选框
        var alphaCheckBox = new CheckBox
        {
            Content = "尊重Alpha通道",
            IsChecked = RespectAlpha,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 5)
        };
        alphaCheckBox.Checked += (s, e) =>
        {
            var oldValue = RespectAlpha;
            RespectAlpha = true;
            OnParameterChanged(nameof(RespectAlpha), RespectAlpha);
        };
        alphaCheckBox.Unchecked += (s, e) =>
        {
            var oldValue = RespectAlpha;
            RespectAlpha = false;
            OnParameterChanged(nameof(RespectAlpha), RespectAlpha);
        };

        // 并行处理复选框
        var parallelCheckBox = new CheckBox
        {
            Content = "启用并行处理",
            IsChecked = EnableParallelProcessing,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 10)
        };
        parallelCheckBox.Checked += (s, e) =>
        {
            var oldValue = EnableParallelProcessing;
            EnableParallelProcessing = true;
            OnParameterChanged(nameof(EnableParallelProcessing), EnableParallelProcessing);
        };
        parallelCheckBox.Unchecked += (s, e) =>
        {
            var oldValue = EnableParallelProcessing;
            EnableParallelProcessing = false;
            OnParameterChanged(nameof(EnableParallelProcessing), EnableParallelProcessing);
        };

        // 逆序混合复选框
        var reverseCheckBox = new CheckBox
        {
            Content = "逆序混合",
            IsChecked = ReverseOrder,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 10)
        };
        reverseCheckBox.Checked += (s, e) =>
        {
            var oldValue = ReverseOrder;
            ReverseOrder = true;
            OnParameterChanged(nameof(ReverseOrder), ReverseOrder);
        };
        reverseCheckBox.Unchecked += (s, e) =>
        {
            var oldValue = ReverseOrder;
            ReverseOrder = false;
            OnParameterChanged(nameof(ReverseOrder), ReverseOrder);
        };

        // 说明文本
        var description = new TextBlock
        {
            Text = "专业的多图层混合工具，支持24种混合模式。连接多个图像到灵活端口，系统会自动添加更多输入端口。支持不同尺寸的图像自动调整和Alpha通道处理。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#777777")),
            FontSize = 11,
            Margin = new Thickness(0, 5, 0, 0),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial")
        };

        panel.Children.Add(modeLabel);
        panel.Children.Add(modeCombo);
        panel.Children.Add(globalOpacityLabel);
        panel.Children.Add(globalOpacitySlider);
        panel.Children.Add(intensityLabel);
        panel.Children.Add(intensitySlider);
        panel.Children.Add(alphaCheckBox);
        panel.Children.Add(parallelCheckBox);
        panel.Children.Add(reverseCheckBox);
        panel.Children.Add(description);

        return panel;
    }

    public override IScriptViewModel CreateViewModel()
    {
        return new FlexibleImageBlendViewModel(this);
    }

    public override Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
    {
        switch (parameterName)
        {
            case nameof(MixMode):
                if (newValue is BlendMode mode)
                    MixMode = mode;
                break;
            case nameof(OutputIntensity):
                if (newValue is float intensity)
                    OutputIntensity = Math.Max(0.1f, Math.Min(2.0f, intensity));
                break;
            case nameof(GlobalOpacity):
                if (newValue is float opacity)
                    GlobalOpacity = Math.Max(0.0f, Math.Min(1.0f, opacity));
                break;
            case nameof(RespectAlpha):
                if (newValue is bool respectAlpha)
                    RespectAlpha = respectAlpha;
                break;
            case nameof(EnableParallelProcessing):
                if (newValue is bool enableParallel)
                    EnableParallelProcessing = enableParallel;
                break;
            case nameof(ReverseOrder):
                if (newValue is bool revOrder)
                    ReverseOrder = revOrder;
                break;
        }
        return Task.CompletedTask;
    }

    public override Dictionary<string, object> SerializeParameters()
    {
        return new Dictionary<string, object>
        {
            [nameof(MixMode)] = MixMode.ToString(),
            [nameof(OutputIntensity)] = OutputIntensity,
            [nameof(GlobalOpacity)] = GlobalOpacity,
            [nameof(RespectAlpha)] = RespectAlpha,
            [nameof(EnableParallelProcessing)] = EnableParallelProcessing,
            [nameof(ReverseOrder)] = ReverseOrder
        };
    }

    public override void DeserializeParameters(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(MixMode), out var modeValue) &&
            Enum.TryParse<BlendMode>(modeValue?.ToString(), out var mode))
        {
            MixMode = mode;
        }

        if (data.TryGetValue(nameof(OutputIntensity), out var intensityValue) &&
            float.TryParse(intensityValue?.ToString(), out var intensity))
        {
            OutputIntensity = Math.Max(0.1f, Math.Min(2.0f, intensity));
        }

        if (data.TryGetValue(nameof(GlobalOpacity), out var opacityValue) &&
            float.TryParse(opacityValue?.ToString(), out var opacity))
        {
            GlobalOpacity = Math.Max(0.0f, Math.Min(1.0f, opacity));
        }

        if (data.TryGetValue(nameof(RespectAlpha), out var respectAlphaValue) &&
            bool.TryParse(respectAlphaValue?.ToString(), out var respectAlpha))
        {
            RespectAlpha = respectAlpha;
        }

        if (data.TryGetValue(nameof(EnableParallelProcessing), out var enableParallelValue) &&
            bool.TryParse(enableParallelValue?.ToString(), out var enableParallel))
        {
            EnableParallelProcessing = enableParallel;
        }

        if (data.TryGetValue(nameof(ReverseOrder), out var revOrderValue) &&
            bool.TryParse(revOrderValue?.ToString(), out var revOrder))
        {
            ReverseOrder = revOrder;
        }
    }
}

public class FlexibleImageBlendViewModel : ScriptViewModelBase
{
    private readonly FlexibleImageBlendScript _script;

    public FlexibleImageBlendViewModel(FlexibleImageBlendScript script) : base(script)
    {
        _script = script;
    }

    public override Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
    {
        return _script.OnParameterChangedAsync(parameterName, oldValue, newValue);
    }

    public override ScriptValidationResult ValidateParameter(string parameterName, object value)
    {
        switch (parameterName)
        {
            case nameof(FlexibleImageBlendScript.OutputIntensity):
                if (value is float intensityVal && (intensityVal < 0.1f || intensityVal > 2.0f))
                    return new ScriptValidationResult(false, "输出强度必须在0.1到2.0之间");
                break;
            case nameof(FlexibleImageBlendScript.GlobalOpacity):
                if (value is float opacityVal && (opacityVal < 0.0f || opacityVal > 1.0f))
                    return new ScriptValidationResult(false, "全局不透明度必须在0.0到1.0之间");
                break;
            case nameof(FlexibleImageBlendScript.EnableParallelProcessing):
                if (value is bool enableParallel)
                    return new ScriptValidationResult(true);
                break;
            case nameof(FlexibleImageBlendScript.ReverseOrder):
                // 布尔值无需额外验证
                return new ScriptValidationResult(true);
        }
        return new ScriptValidationResult(true);
    }

    public override Dictionary<string, object> GetParameterData()
    {
        return _script.SerializeParameters();
    }

    public override Task SetParameterDataAsync(Dictionary<string, object> data)
    {
        _script.DeserializeParameters(data);
        return Task.CompletedTask;
    }

    public override Task ResetToDefaultAsync()
    {
        _script.MixMode = FlexibleImageBlendScript.BlendMode.Normal;
        _script.OutputIntensity = 1.0f;
        _script.GlobalOpacity = 1.0f;
        _script.RespectAlpha = true;
        _script.EnableParallelProcessing = true;
        _script.ReverseOrder = false;
        return Task.CompletedTask;
    }
}
