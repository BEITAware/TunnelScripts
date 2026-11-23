using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Tunnel_Next.Services.Scripting;
using OpenCvSharp;

[TunnelExtensionScript(
    Name = "比较加和",
    Author = "BEITAware",
    Description = "比较加和节点 - 比较多个图像并叠加显示",
    Version = "1.0",
    Category = "数学",
    Color = "#48B6FF"
)]
public class ComparisonSumScript : TunnelExtensionScriptBase
{
    [ScriptParameter(DisplayName = "比较类型", Description = "选择比较操作类型", Order = 0)]
    public string CompareType { get; set; } = "greater";

    [ScriptParameter(DisplayName = "阈值", Description = "比较阈值(0-255)", Order = 1)]
    public int Threshold { get; set; } = 128;

    [ScriptParameter(DisplayName = "Alpha强度", Description = "Alpha通道强度(0-100%)", Order = 2)]
    public int AlphaScale { get; set; } = 100;

    [ScriptParameter(DisplayName = "混合模式", Description = "图像混合模式", Order = 3)]
    public string BlendMode { get; set; } = "normal";

    public string NodeInstanceId { get; set; } = string.Empty;

    public override Dictionary<string, PortDefinition> GetInputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            ["f32bmp"] = new PortDefinition("f32bmp", true, "输入图像")
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
        var images = inputs.Select(pair => (Order: InputOrder(pair.Key), Image: pair.Value as Mat))
                           .Where(layer => layer.Order >= 0 && layer.Image != null && !layer.Image.Empty())
                           .OrderBy(layer => layer.Order)
                           .ToList();
        if (images.Count == 0)
            return new() { ["f32bmp"] = new Mat(100, 100, MatType.CV_32FC4, Scalar.All(0)) };

        var size = images[0].Image.Size();
        if (images.Any(layer => layer.Image.Size() != size))
            throw new ArgumentException("比较加和的所有输入图像必须具有相同尺寸。");

        var output = new Mat(size, MatType.CV_32FC4, Scalar.All(0));
        try
        {
            foreach (var layer in images)
            {
                using var source = ToFloatBgra(layer.Image);
                Blend(output, source);
            }
            return new() { ["f32bmp"] = output };
        }
        catch
        {
            output.Dispose();
            throw;
        }
    }

    // The host appends its type name and numeric index, e.g. F32bmp_1.
    private static long InputOrder(string name)
    {
        if (name.Equals("f32bmp", StringComparison.OrdinalIgnoreCase)) return 0;
        if (name.StartsWith("f32bmp_", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(name.Substring(7), out int index) && index >= 0)
            return (long)index + 1;
        return -1;
    }

    private static Mat ToFloatBgra(Mat input)
    {
        double scale = input.Depth() == MatType.CV_8U ? 1.0 / 255 :
                       input.Depth() == MatType.CV_16U ? 1.0 / 65535 : 1;
        if (input.Channels() == 4)
        {
            var result = new Mat();
            input.ConvertTo(result, MatType.CV_32F, scale);
            return result;
        }
        var conversion = input.Channels() switch {
            1 => ColorConversionCodes.GRAY2BGRA,
            3 => ColorConversionCodes.BGR2BGRA,
            _ => throw new ArgumentException("仅支持灰度、BGR 和 BGRA 图像。")
        };
        using var floats = new Mat();
        input.ConvertTo(floats, MatType.CV_32F, scale);
        var bgra = new Mat();
        Cv2.CvtColor(floats, bgra, conversion);
        return bgra;
    }

    private unsafe void Blend(Mat background, Mat layer)
    {
        float threshold = Math.Clamp(Threshold, 0, 255) / 255f;
        float strength = Math.Clamp(AlphaScale, 0, 100) / 100f;
        for (int y = 0; y < background.Rows; y++)
        {
            var dst = (float*)background.Ptr(y);
            var src = (float*)layer.Ptr(y);
            for (int x = 0; x < background.Cols; x++, dst += 4, src += 4)
            {
                float mean = (src[0] + src[1] + src[2]) / 3;
                float difference = Math.Abs(mean - threshold);
                float mask = CompareType switch {
                    "less" => mean < threshold ? 1 : 0,
                    "equal" => difference <= 0.1f ? 1 : 0,
                    "abs_diff" => Math.Max(0, 1 - 2 * difference),
                    _ => mean > threshold ? 1 : 0
                };
                float alpha = mask * strength * Math.Clamp(src[3], 0f, 1f);
                if (alpha <= 0) continue;
                float dstAlpha = dst[3];
                float retained = dstAlpha * (1 - alpha);
                float outAlpha = alpha + retained;
                for (int c = 0; c < 3; c++)
                {
                    float top = src[c], bottom = dst[c];
                    float blend = BlendMode switch {
                        "multiply" => top * bottom,
                        "add" => Math.Min(1, top + bottom),
                        "screen" => 1 - (1 - top) * (1 - bottom),
                        _ => top
                    };
                    float mixed = top * (1 - dstAlpha) + blend * dstAlpha;
                    dst[c] = Math.Min(1, (mixed * alpha + bottom * retained) / outAlpha);
                }
                dst[3] = outAlpha;
            }
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
            "/Tunnel-Next;component/Resources/ScriptsControls/ComboBoxStyles.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/SliderStyles.xaml",
        };
        foreach (var path in resourcePaths)
        {
            try { resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(path, UriKind.Relative) }); }
            catch { /* 静默处理 */ }
        }

        if (resources.Contains("MainPanelStyle")) mainPanel.Style = resources["MainPanelStyle"] as Style;

        var viewModel = CreateViewModel() as ComparisonSumViewModel;
        mainPanel.DataContext = viewModel;

        // 标题
        var titleLabel = new Label { Content = "比较加和设置" };
        if (resources.Contains("TitleLabelStyle")) titleLabel.Style = resources["TitleLabelStyle"] as Style;
        mainPanel.Children.Add(titleLabel);

        // 比较类型
        var compareTypeLabel = new Label { Content = "比较类型:", Margin = new Thickness(0, 5, 0, 0) };
        if(resources.Contains("DefaultLabelStyle")) compareTypeLabel.Style = resources["DefaultLabelStyle"] as Style;
        mainPanel.Children.Add(compareTypeLabel);
        
        var compareTypeComboBox = new ComboBox
        {
            Margin = new Thickness(0, 0, 0, 10),
            ItemsSource = new Dictionary<string, string>
            {
                { "greater", "大于" },
                { "less", "小于" },
                { "equal", "等于" },
                { "abs_diff", "绝对差值" }
            },
            SelectedValuePath = "Key",
            DisplayMemberPath = "Value"
        };
        if(resources.Contains("DefaultComboBoxStyle")) compareTypeComboBox.Style = resources["DefaultComboBoxStyle"] as Style;
        compareTypeComboBox.SetBinding(ComboBox.SelectedValueProperty, new Binding(nameof(viewModel.CompareType)) { Mode = BindingMode.TwoWay });
        mainPanel.Children.Add(compareTypeComboBox);
        
        // 阈值
        var thresholdLabel = new Label { Content = $"阈值: {viewModel.Threshold}" };
        if(resources.Contains("DefaultLabelStyle")) thresholdLabel.Style = resources["DefaultLabelStyle"] as Style;
        mainPanel.Children.Add(thresholdLabel);

        var thresholdSlider = new Slider
        {
            Minimum = 0,
            Maximum = 255,
            Margin = new Thickness(0, 0, 0, 10)
        };
        if(resources.Contains("DefaultSliderStyle")) thresholdSlider.Style = resources["DefaultSliderStyle"] as Style;
        thresholdSlider.SetBinding(Slider.ValueProperty, new Binding(nameof(viewModel.Threshold)) { Mode = BindingMode.TwoWay });
        thresholdSlider.ValueChanged += (s, e) => thresholdLabel.Content = $"阈值: {(int)e.NewValue}";
        mainPanel.Children.Add(thresholdSlider);
        
        // Alpha强度
        var alphaScaleLabel = new Label { Content = $"Alpha强度: {viewModel.AlphaScale}%" };
        if(resources.Contains("DefaultLabelStyle")) alphaScaleLabel.Style = resources["DefaultLabelStyle"] as Style;
        mainPanel.Children.Add(alphaScaleLabel);

        var alphaScaleSlider = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            Margin = new Thickness(0, 0, 0, 10)
        };
        if(resources.Contains("DefaultSliderStyle")) alphaScaleSlider.Style = resources["DefaultSliderStyle"] as Style;
        alphaScaleSlider.SetBinding(Slider.ValueProperty, new Binding(nameof(viewModel.AlphaScale)) { Mode = BindingMode.TwoWay });
        alphaScaleSlider.ValueChanged += (s, e) => alphaScaleLabel.Content = $"Alpha强度: {(int)e.NewValue}%";
        mainPanel.Children.Add(alphaScaleSlider);
        
        // 混合模式
        var blendModeLabel = new Label { Content = "混合模式:", Margin = new Thickness(0, 5, 0, 0) };
        if(resources.Contains("DefaultLabelStyle")) blendModeLabel.Style = resources["DefaultLabelStyle"] as Style;
        mainPanel.Children.Add(blendModeLabel);

        var blendModeComboBox = new ComboBox
        {
            Margin = new Thickness(0, 0, 0, 10),
            ItemsSource = new Dictionary<string, string>
            {
                { "normal", "正常" },
                { "add", "线性减淡(添加)" },
                { "multiply", "正片叠底" },
                { "screen", "滤色" }
            },
            SelectedValuePath = "Key",
            DisplayMemberPath = "Value"
        };
        if(resources.Contains("DefaultComboBoxStyle")) blendModeComboBox.Style = resources["DefaultComboBoxStyle"] as Style;
        blendModeComboBox.SetBinding(ComboBox.SelectedValueProperty, new Binding(nameof(viewModel.BlendMode)) { Mode = BindingMode.TwoWay });
        mainPanel.Children.Add(blendModeComboBox);

        return mainPanel;
    }

    public override IScriptViewModel CreateViewModel()
    {
        return new ComparisonSumViewModel(this);
    }

    public override async Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
    {
        await Task.CompletedTask;
    }

    public override Dictionary<string, object> SerializeParameters()
    {
        return new Dictionary<string, object>
        {
            [nameof(CompareType)] = CompareType,
            [nameof(Threshold)] = Threshold,
            [nameof(AlphaScale)] = AlphaScale,
            [nameof(BlendMode)] = BlendMode,
            ["NodeInstanceId"] = NodeInstanceId
        };
    }

    public override void DeserializeParameters(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(CompareType), out var compareType))
            CompareType = compareType?.ToString() ?? "greater";
        if (data.TryGetValue(nameof(Threshold), out var threshold) && int.TryParse(threshold?.ToString(), out var parsedThreshold))
            Threshold = Math.Clamp(parsedThreshold, 0, 255);
        if (data.TryGetValue(nameof(AlphaScale), out var alphaScale) && int.TryParse(alphaScale?.ToString(), out var parsedAlphaScale))
            AlphaScale = Math.Clamp(parsedAlphaScale, 0, 100);
        if (data.TryGetValue(nameof(BlendMode), out var blendMode))
            BlendMode = blendMode?.ToString() ?? "normal";
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

public class ComparisonSumViewModel : ScriptViewModelBase
{
    private ComparisonSumScript ComparisonSumScript => (ComparisonSumScript)Script;

    public string CompareType
    {
        get => ComparisonSumScript.CompareType;
        set
        {
            if (ComparisonSumScript.CompareType != value)
            {
                ComparisonSumScript.CompareType = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(CompareType), value);
            }
        }
    }

    public int Threshold
    {
        get => ComparisonSumScript.Threshold;
        set
        {
            if (ComparisonSumScript.Threshold != value)
            {
                ComparisonSumScript.Threshold = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(Threshold), value);
            }
        }
    }

    public int AlphaScale
    {
        get => ComparisonSumScript.AlphaScale;
        set
        {
            if (ComparisonSumScript.AlphaScale != value)
            {
                ComparisonSumScript.AlphaScale = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(AlphaScale), value);
            }
        }
    }

    public string BlendMode
    {
        get => ComparisonSumScript.BlendMode;
        set
        {
            if (ComparisonSumScript.BlendMode != value)
            {
                ComparisonSumScript.BlendMode = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(BlendMode), value);
            }
        }
    }

    public ComparisonSumViewModel(ComparisonSumScript script) : base(script)
    {
    }

    private void NotifyParameterChanged(string parameterName, object value)
    {
        if (Script is TunnelExtensionScriptBase rsb)
        {
            rsb.OnParameterChanged(parameterName, value);
        }
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
            [nameof(CompareType)] = CompareType,
            [nameof(Threshold)] = Threshold,
            [nameof(AlphaScale)] = AlphaScale,
            [nameof(BlendMode)] = BlendMode
        };
    }

    public override async Task SetParameterDataAsync(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(CompareType), out var compareType))
            CompareType = compareType?.ToString() ?? "greater";
        if (data.TryGetValue(nameof(Threshold), out var threshold) && int.TryParse(threshold?.ToString(), out var parsedThreshold))
            Threshold = Math.Clamp(parsedThreshold, 0, 255);
        if (data.TryGetValue(nameof(AlphaScale), out var alphaScale) && int.TryParse(alphaScale?.ToString(), out var parsedAlphaScale))
            AlphaScale = Math.Clamp(parsedAlphaScale, 0, 100);
        if (data.TryGetValue(nameof(BlendMode), out var blendMode))
            BlendMode = blendMode?.ToString() ?? "normal";
        await Task.CompletedTask;
    }

    public override async Task ResetToDefaultAsync()
    {
        CompareType = "greater";
        Threshold = 128;
        AlphaScale = 100;
        BlendMode = "normal";
        await Task.CompletedTask;
    }

    public override void Dispose()
    {
        base.Dispose();
    }
}
