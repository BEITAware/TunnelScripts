using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Tunnel_Next.Services.Scripting;
using OpenCvSharp;

[TunnelExtensionScript(
    Name = "倒影",
    Author = "BEITAware",
    Description = "在图像下方添加镜面倒影效果",
    Version = "1.0",
    Category = "几何",
    Color = "#00BFFF"
)]
public class ReflectionScript : TunnelExtensionScriptBase
{
    [ScriptParameter(DisplayName = "倒影高度比例", Description = "倒影占原图高度的比例 (0.0-1.0)", Order = 0)]
    public double ReflectionRatio { get; set; } = 1.0;

    [ScriptParameter(DisplayName = "分离量(像素)", Description = "倒影与原图之间的距离，可为负以重叠", Order = 1)]
    public int Separation { get; set; } = 0;

    [ScriptParameter(DisplayName = "自适应边界", Description = "裁切掉四周完全透明的区域", Order = 2)]
    public bool AdaptiveBoundary { get; set; } = false;

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
            ["f32bmp"] = new PortDefinition("f32bmp", false, "输出图像 (含倒影)"),
            ["f32bmp_reflection"] = new PortDefinition("f32bmp_reflection", false, "仅倒影")
        };
    }

    public override Dictionary<string, object> Process(Dictionary<string, object> inputs, IScriptContext context)
    {
        if (!inputs.TryGetValue("f32bmp", out var inputObj) || inputObj == null)
        {
            return new Dictionary<string, object> { ["f32bmp"] = null };
        }

        if (!(inputObj is Mat inputMat) || inputMat.Empty())
        {
            return new Dictionary<string, object> { ["f32bmp"] = null };
        }

        try
        {
            Mat workingMat = EnsureRGBAFormat(inputMat);

            // 生成倒影（垂直翻转）
            Mat reflection = new Mat();
            Cv2.Flip(workingMat, reflection, FlipMode.X);

            // 根据比例裁切倒影
            ReflectionRatio = Math.Clamp(ReflectionRatio, 0.0, 1.0);
            int reflectHeight = (int)(workingMat.Rows * ReflectionRatio);
            if (reflectHeight < reflection.Rows)
            {
                OpenCvSharp.Rect roi = new OpenCvSharp.Rect(0, 0, reflection.Cols, reflectHeight);
                reflection = new Mat(reflection, roi).Clone();
            }

            // 根据分离量计算输出尺寸
            int startY = workingMat.Rows + Separation; // 可以为负
            if (startY < 0) startY = 0;

            int outRows = Math.Max(workingMat.Rows, startY + reflection.Rows);
            int outCols = workingMat.Cols;
            Mat output = new Mat(outRows, outCols, workingMat.Type(), new Scalar(0, 0, 0, 0));

            // 拷贝原图
            using (var dstTop = output.RowRange(0, workingMat.Rows))
            {
                workingMat.CopyTo(dstTop);
            }

            // 拷贝倒影至指定位置
            using (var dstBottom = output.RowRange(startY, startY + reflection.Rows))
            {
                reflection.CopyTo(dstBottom);
            }

            // 仅倒影输出，与 output 同尺寸
            Mat reflectionOnly = new Mat(outRows, outCols, workingMat.Type(), new Scalar(0, 0, 0, 0));
            using (var dstRef = reflectionOnly.RowRange(startY, startY + reflection.Rows))
            {
                reflection.CopyTo(dstRef);
            }

            // 自适应边界裁切
            if (AdaptiveBoundary)
            {
                Mat trimmedOutput = TrimTransparentBorders(output);
                output.Dispose();
                output = trimmedOutput;

                Mat trimmedRef = TrimTransparentBorders(reflectionOnly);
                reflectionOnly.Dispose();
                reflectionOnly = trimmedRef;
            }

            workingMat.Dispose();
            reflection.Dispose();

            return new Dictionary<string, object>
            {
                ["f32bmp"] = output,
                ["f32bmp_reflection"] = reflectionOnly
            };
        }
        catch (Exception ex)
        {
            throw new ApplicationException($"倒影节点处理失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 确保图像为 RGBA 32F 格式
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
            Cv2.CvtColor(inputMat, rgbaMat, ColorConversionCodes.RGB2RGBA);
        }
        else if (inputMat.Channels() == 1)
        {
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
    /// 裁切掉四周完全透明的区域
    /// </summary>
    private Mat TrimTransparentBorders(Mat rgbaMat)
    {
        Mat[] channels = Cv2.Split(rgbaMat);
        Mat alpha = channels[3];

        Mat alpha8U = new Mat();
        alpha.ConvertTo(alpha8U, MatType.CV_8UC1, 255.0);

        Mat thresh = new Mat();
        Cv2.Threshold(alpha8U, thresh, 0, 255, ThresholdTypes.Binary);

        using var nonZero = new Mat();
        Cv2.FindNonZero(thresh, nonZero);

        alpha8U.Dispose();
        thresh.Dispose();
        foreach (var ch in channels) ch.Dispose();

        if (nonZero.Empty())
        {
            return rgbaMat;
        }

        OpenCvSharp.Rect bounding = Cv2.BoundingRect(nonZero);
        Mat cropped = new Mat(rgbaMat, bounding).Clone();
        return cropped;
    }

    public override FrameworkElement CreateParameterControl()
    {
        var mainPanel = new StackPanel { Margin = new Thickness(5) };

        var resources = new ResourceDictionary();
        var resourcePaths = new[]
        {
            "/Tunnel-Next;component/Resources/ScriptsControls/SharedBrushes.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/LabelStyles.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/PanelStyles.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/SliderStyles.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/TextBlockStyles.xaml"
        };
        foreach (var path in resourcePaths)
        {
            try { resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(path, UriKind.Relative) }); }
            catch { }
        }
        if (resources.Contains("MainPanelStyle")) mainPanel.Style = resources["MainPanelStyle"] as Style;

        var viewModel = CreateViewModel() as ReflectionViewModel;
        mainPanel.DataContext = viewModel;

        var titleLabel = new Label { Content = "倒影" };
        if (resources.Contains("TitleLabelStyle")) titleLabel.Style = resources["TitleLabelStyle"] as Style;
        mainPanel.Children.Add(titleLabel);

        // 倒影高度比例
        var ratioLabel = new Label();
        if (resources.Contains("DefaultLabelStyle")) ratioLabel.Style = resources["DefaultLabelStyle"] as Style;
        ratioLabel.SetBinding(Label.ContentProperty, new Binding(nameof(viewModel.RatioText)) { Mode = BindingMode.OneWay });
        mainPanel.Children.Add(ratioLabel);

        var ratioSlider = new Slider
        {
            Minimum = 0.0,
            Maximum = 1.0,
            Margin = new Thickness(0, 0, 0, 10)
        };
        if (resources.Contains("DefaultSliderStyle")) ratioSlider.Style = resources["DefaultSliderStyle"] as Style;
        ratioSlider.SetBinding(Slider.ValueProperty, new Binding(nameof(viewModel.ReflectionRatio)) { Mode = BindingMode.TwoWay });
        mainPanel.Children.Add(ratioSlider);

        // 分离量
        var sepLabel = new Label();
        if (resources.Contains("DefaultLabelStyle")) sepLabel.Style = resources["DefaultLabelStyle"] as Style;
        sepLabel.SetBinding(Label.ContentProperty, new Binding(nameof(viewModel.SeparationText)) { Mode = BindingMode.OneWay });
        mainPanel.Children.Add(sepLabel);

        var sepSlider = new Slider
        {
            Minimum = -2000,
            Maximum = 2000,
            Margin = new Thickness(0, 0, 0, 10)
        };
        if (resources.Contains("DefaultSliderStyle")) sepSlider.Style = resources["DefaultSliderStyle"] as Style;
        sepSlider.SetBinding(Slider.ValueProperty, new Binding(nameof(viewModel.Separation)) { Mode = BindingMode.TwoWay });
        mainPanel.Children.Add(sepSlider);

        // 自适应边界
        var adaptiveCheckBox = new CheckBox { Content = "自适应边界", Margin = new Thickness(0,0,0,5) };
        if(resources.Contains("DefaultCheckBoxStyle")) adaptiveCheckBox.Style = resources["DefaultCheckBoxStyle"] as Style;
        adaptiveCheckBox.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(viewModel.AdaptiveBoundary)) { Mode = BindingMode.TwoWay });
        mainPanel.Children.Add(adaptiveCheckBox);

        return mainPanel;
    }

    public override IScriptViewModel CreateViewModel()
    {
        return new ReflectionViewModel(this);
    }

    public override async Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
    {
        await Task.CompletedTask;
    }

    public override Dictionary<string, object> SerializeParameters()
    {
        return new Dictionary<string, object>
        {
            [nameof(ReflectionRatio)] = ReflectionRatio,
            [nameof(Separation)] = Separation,
            [nameof(AdaptiveBoundary)] = AdaptiveBoundary,
            ["NodeInstanceId"] = NodeInstanceId
        };
    }

    public override void DeserializeParameters(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(ReflectionRatio), out var ratio) && double.TryParse(ratio?.ToString(), out var r))
            ReflectionRatio = Math.Clamp(r, 0.0, 1.0);
        if (data.TryGetValue(nameof(Separation), out var sep) && int.TryParse(sep?.ToString(), out var s))
            Separation = s;
        if (data.TryGetValue(nameof(AdaptiveBoundary), out var ab) && bool.TryParse(ab?.ToString(), out var a))
            AdaptiveBoundary = a;
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

public class ReflectionViewModel : ScriptViewModelBase
{
    private ReflectionScript ReflectionScript => (ReflectionScript)Script;

    public double ReflectionRatio
    {
        get => ReflectionScript.ReflectionRatio;
        set
        {
            var clamped = Math.Clamp(value, 0.0, 1.0);
            if (Math.Abs(ReflectionScript.ReflectionRatio - clamped) > 0.001)
            {
                ReflectionScript.ReflectionRatio = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RatioText));
                NotifyParameterChanged(nameof(ReflectionRatio), clamped);
            }
        }
    }

    public string RatioText => $"倒影高度比例: {ReflectionRatio:F2}";

    public int Separation
    {
        get => ReflectionScript.Separation;
        set
        {
            if (ReflectionScript.Separation != value)
            {
                ReflectionScript.Separation = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SeparationText));
                NotifyParameterChanged(nameof(Separation), value);
            }
        }
    }

    public bool AdaptiveBoundary
    {
        get => ReflectionScript.AdaptiveBoundary;
        set
        {
            if (ReflectionScript.AdaptiveBoundary != value)
            {
                ReflectionScript.AdaptiveBoundary = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(AdaptiveBoundary), value);
            }
        }
    }

    public string SeparationText => $"分离量: {Separation}px";

    public ReflectionViewModel(ReflectionScript script) : base(script) { }

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
            [nameof(ReflectionRatio)] = ReflectionRatio,
            [nameof(Separation)] = Separation,
            [nameof(AdaptiveBoundary)] = AdaptiveBoundary
        };
    }

    public override async Task SetParameterDataAsync(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(ReflectionRatio), out var ratio) && double.TryParse(ratio?.ToString(), out var r))
            ReflectionRatio = Math.Clamp(r, 0.0, 1.0);
        if (data.TryGetValue(nameof(Separation), out var sep) && int.TryParse(sep?.ToString(), out var s))
            Separation = s;
        if (data.TryGetValue(nameof(AdaptiveBoundary), out var ab) && bool.TryParse(ab?.ToString(), out var a))
            AdaptiveBoundary = a;
        await Task.CompletedTask;
    }

    public override async Task ResetToDefaultAsync()
    {
        ReflectionRatio = 1.0;
        Separation = 0;
        AdaptiveBoundary = false;
        await Task.CompletedTask;
    }
} 