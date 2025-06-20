using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Tunnel_Next.Services.Scripting;
using OpenCvSharp;

[RevivalScript(
    Name = "比较加和",
    Author = "BEITAware",
    Description = "比较加和节点 - 比较多个图像并叠加显示",
    Version = "1.0",
    Category = "数学",
    Color = "#48B6FF"
)]
public class ComparisonSumScript : RevivalScriptBase
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
        // 收集所有输入图像
        var imageInputs = new List<Mat>();
        
        // 提取所有f32bmp输入（包括带序号的）
        foreach (var kvp in inputs)
        {
            if ((kvp.Key == "f32bmp" || kvp.Key.StartsWith("f32bmp_")) && kvp.Value is Mat mat && !mat.Empty())
            {
                imageInputs.Add(mat);
            }
        }

        if (imageInputs.Count == 0)
        {
            // 返回默认的黑色图像
            Mat defaultImage = Mat.Zeros(100, 100, MatType.CV_32FC4);
            return new Dictionary<string, object> { ["f32bmp"] = defaultImage };
        }

        try
        {
            // 确保所有输入都是RGBA格式
            var workingImages = new List<Mat>();
            foreach (var img in imageInputs)
            {
                workingImages.Add(EnsureRGBAFormat(img));
            }

            // 执行比较加和操作
            Mat resultMat = PerformComparisonSum(workingImages);

            // 清理工作图像
            foreach (var img in workingImages)
            {
                img.Dispose();
            }

            return new Dictionary<string, object> { ["f32bmp"] = resultMat };
        }
        catch (Exception ex)
        {
            throw new ApplicationException($"比较加和节点处理失败: {ex.Message}", ex);
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
    /// 执行比较加和操作
    /// </summary>
    private Mat PerformComparisonSum(List<Mat> images)
    {
        if (images.Count == 0) return Mat.Zeros(100, 100, MatType.CV_32FC4);

        // 准备输出图像（与第一个输入图像大小相同）
        Mat baseImg = Mat.Zeros(images[0].Size(), MatType.CV_32FC4);
        float alphaScale = AlphaScale / 100.0f;
        float normalizedThreshold = Threshold / 255.0f;

        // 从底层到顶层处理每个图像
        foreach (var img in images)
        {
            // 确保图像有4个通道（RGBA）
            Mat processedImg = img.Clone();
            
            // 根据比较类型计算alpha通道
            ApplyComparisonToAlpha(processedImg, normalizedThreshold, alphaScale);
            
            // 根据混合模式叠加图像
            BlendImages(baseImg, processedImg);
            
            processedImg.Dispose();
        }

        // 确保值在有效范围内
        Mat result = new Mat();
        Cv2.Threshold(baseImg, result, 1.0, 1.0, ThresholdTypes.Trunc);
        baseImg.Dispose();

        return result;
    }

    /// <summary>
    /// 根据比较类型应用Alpha通道
    /// </summary>
    private void ApplyComparisonToAlpha(Mat img, float threshold, float alphaScale)
    {
        Mat[] channels = Cv2.Split(img);

        // 计算RGB平均值
        using (var avgMat = new Mat())
        {
            Cv2.Add(channels[0], channels[1], avgMat);
            Cv2.Add(avgMat, channels[2], avgMat);
            Cv2.Divide(avgMat, new Scalar(3.0), avgMat);

            switch (CompareType)
            {
                case "greater":
                    // 大于阈值部分变透明
                    Cv2.Compare(avgMat, new Scalar(threshold), channels[3], CmpType.GT);
                    break;
                case "less":
                    // 小于阈值部分变透明
                    Cv2.Compare(avgMat, new Scalar(threshold), channels[3], CmpType.LT);
                    break;
                case "equal":
                    // 接近阈值部分变透明（使用高斯函数形成平滑过渡）
                    using (var diff = new Mat())
                    {
                        Cv2.Absdiff(avgMat, new Scalar(threshold), diff);
                        // 简化的接近度计算
                        Cv2.Compare(diff, new Scalar(0.1), channels[3], CmpType.LE);
                    }
                    break;
                case "abs_diff":
                    // 绝对差值作为透明度
                    using (var diff = new Mat())
                    {
                        Cv2.Absdiff(avgMat, new Scalar(threshold), diff);
                        using (var scaledDiff = new Mat())
                        {
                            Cv2.Multiply(diff, new Scalar(2.0), scaledDiff);
                            Cv2.Subtract(new Scalar(1.0), scaledDiff, channels[3]);
                        }
                        Cv2.Threshold(channels[3], channels[3], 0, 0, ThresholdTypes.Tozero);
                    }
                    break;
                default:
                    Cv2.Compare(avgMat, new Scalar(threshold), channels[3], CmpType.GT);
                    break;
            }

            // 转换为浮点并应用缩放
            channels[3].ConvertTo(channels[3], MatType.CV_32F, alphaScale / 255.0);
        }

        Cv2.Merge(channels, img);

        // 清理资源
        foreach (var ch in channels) ch.Dispose();
    }

    /// <summary>
    /// 混合图像
    /// </summary>
    private void BlendImages(Mat baseImg, Mat srcImg)
    {
        Mat[] baseChannels = Cv2.Split(baseImg);
        Mat[] srcChannels = Cv2.Split(srcImg);

        switch (BlendMode)
        {
            case "normal":
                // 正常混合：src_alpha * src + (1 - src_alpha) * dst
                for (int i = 0; i < 3; i++)
                {
                    using (var temp1 = new Mat())
                    using (var temp2 = new Mat())
                    using (var oneMinusAlpha = new Mat())
                    {
                        Cv2.Multiply(srcChannels[i], srcChannels[3], temp1);
                        Cv2.Subtract(new Scalar(1.0), srcChannels[3], oneMinusAlpha);
                        Cv2.Multiply(baseChannels[i], oneMinusAlpha, temp2);
                        Cv2.Add(temp1, temp2, baseChannels[i]);
                    }
                }
                break;
            case "multiply":
                // 正片叠底：src * dst
                for (int i = 0; i < 3; i++)
                {
                    using (var blended = new Mat())
                    using (var temp1 = new Mat())
                    using (var temp2 = new Mat())
                    using (var oneMinusAlpha = new Mat())
                    {
                        Cv2.Multiply(srcChannels[i], baseChannels[i], blended);
                        Cv2.Multiply(blended, srcChannels[3], temp1);
                        Cv2.Subtract(new Scalar(1.0), srcChannels[3], oneMinusAlpha);
                        Cv2.Multiply(baseChannels[i], oneMinusAlpha, temp2);
                        Cv2.Add(temp1, temp2, baseChannels[i]);
                    }
                }
                break;
            case "screen":
                // 滤色：1 - (1 - src) * (1 - dst)
                for (int i = 0; i < 3; i++)
                {
                    using (var oneMinus1 = new Mat())
                    using (var oneMinus2 = new Mat())
                    using (var blended = new Mat())
                    using (var temp1 = new Mat())
                    using (var temp2 = new Mat())
                    using (var oneMinusAlpha = new Mat())
                    {
                        Cv2.Subtract(new Scalar(1.0), srcChannels[i], oneMinus1);
                        Cv2.Subtract(new Scalar(1.0), baseChannels[i], oneMinus2);
                        Cv2.Multiply(oneMinus1, oneMinus2, blended);
                        Cv2.Subtract(new Scalar(1.0), blended, blended);
                        Cv2.Multiply(blended, srcChannels[3], temp1);
                        Cv2.Subtract(new Scalar(1.0), srcChannels[3], oneMinusAlpha);
                        Cv2.Multiply(baseChannels[i], oneMinusAlpha, temp2);
                        Cv2.Add(temp1, temp2, baseChannels[i]);
                    }
                }
                break;
            default:
                // 默认使用正常混合
                goto case "normal";
        }

        // 更新Alpha通道（取最大值）
        Cv2.Max(baseChannels[3], srcChannels[3], baseChannels[3]);

        Cv2.Merge(baseChannels, baseImg);

        // 清理资源
        foreach (var ch in baseChannels) ch.Dispose();
        foreach (var ch in srcChannels) ch.Dispose();
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
        if (data.TryGetValue(nameof(Threshold), out var threshold))
            Threshold = Convert.ToInt32(threshold);
        if (data.TryGetValue(nameof(AlphaScale), out var alphaScale))
            AlphaScale = Convert.ToInt32(alphaScale);
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
        if (Script is RevivalScriptBase rsb)
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
        if (data.TryGetValue(nameof(Threshold), out var threshold))
            Threshold = Convert.ToInt32(threshold);
        if (data.TryGetValue(nameof(AlphaScale), out var alphaScale))
            AlphaScale = Convert.ToInt32(alphaScale);
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
