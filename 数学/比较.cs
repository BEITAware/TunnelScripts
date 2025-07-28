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
    Name = "比较",
    Author = "BEITAware",
    Description = "逐像素比较两张图像，RGB通道独立比较",
    Version = "1.0",
    Category = "数学",
    Color = "#9370DB"
)]
public class ComparisonScript : TunnelExtensionScriptBase
{
    [ScriptParameter(DisplayName = "比较类型", Description = "选择比较操作类型", Order = 0)]
    public string ComparisonType { get; set; } = "大于";

    [ScriptParameter(DisplayName = "X偏移", Description = "第二个图像的X轴偏移量", Order = 1)]
    public int OffsetX { get; set; } = 0;

    [ScriptParameter(DisplayName = "Y偏移", Description = "第二个图像的Y轴偏移量", Order = 2)]
    public int OffsetY { get; set; } = 0;

    [ScriptParameter(DisplayName = "容差阈值", Description = "比较的容差阈值", Order = 3)]
    public float Threshold { get; set; } = 0.001f;

    public string NodeInstanceId { get; set; } = string.Empty;

    public override Dictionary<string, PortDefinition> GetInputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            ["f32bmp"] = new PortDefinition("f32bmp", false, "第一张图像"),
            ["f32bmp2"] = new PortDefinition("f32bmp", false, "第二张图像")
        };
    }

    public override Dictionary<string, PortDefinition> GetOutputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            ["f32bmp"] = new PortDefinition("f32bmp", false, "比较结果图像")
        };
    }

    public override Dictionary<string, object> Process(Dictionary<string, object> inputs, IScriptContext context)
    {
        // 检查第一个输入
        if (!inputs.TryGetValue("f32bmp", out var input1Obj) || input1Obj == null)
        {
            return new Dictionary<string, object> { ["f32bmp"] = null };
        }

        if (!(input1Obj is Mat input1Mat) || input1Mat.Empty())
        {
            return new Dictionary<string, object> { ["f32bmp"] = null };
        }

        // 检查第二个输入
        if (!inputs.TryGetValue("f32bmp2", out var input2Obj) || input2Obj == null)
        {
            return new Dictionary<string, object> { ["f32bmp"] = input1Mat };
        }

        if (!(input2Obj is Mat input2Mat) || input2Mat.Empty())
        {
            return new Dictionary<string, object> { ["f32bmp"] = input1Mat };
        }

        try
        {
            // 确保输入是RGBA格式
            Mat workingMat1 = EnsureRGBAFormat(input1Mat);
            Mat workingMat2 = EnsureRGBAFormat(input2Mat);
            
            // 执行比较操作
            Mat resultMat = PerformComparison(workingMat1, workingMat2);

            return new Dictionary<string, object> { ["f32bmp"] = resultMat };
        }
        catch (Exception ex)
        {
            throw new ApplicationException($"比较节点处理失败: {ex.Message}", ex);
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
    /// 执行比较操作
    /// </summary>
    private Mat PerformComparison(Mat img1, Mat img2)
    {
        int h1 = img1.Height;
        int w1 = img1.Width;
        int h2 = img2.Height;
        int w2 = img2.Width;

        // 创建输出图像（四通道浮点图像）
        Mat output = Mat.Zeros(h1, w1, MatType.CV_32FC4);

        // 计算有效重叠区域
        int x1 = Math.Max(0, OffsetX);
        int y1 = Math.Max(0, OffsetY);
        int x2 = Math.Max(0, -OffsetX);
        int y2 = Math.Max(0, -OffsetY);

        int w = Math.Min(w1 - x1, w2 - x2);
        int h = Math.Min(h1 - y1, h2 - y2);

        if (w <= 0 || h <= 0)
        {
            // 没有重叠区域，返回全黑图像，但Alpha通道为1.0
            Mat[] outputChannels = Cv2.Split(output);
            outputChannels[3].SetTo(1.0);
            Cv2.Merge(outputChannels, output);
            foreach (var ch in outputChannels) ch.Dispose();
            return output;
        }

        // 创建ROI进行比较
        using (var roi1 = new Mat(img1, new OpenCvSharp.Rect(x1, y1, w, h)))
        using (var roi2 = new Mat(img2, new OpenCvSharp.Rect(x2, y2, w, h)))
        using (var outputRoi = new Mat(output, new OpenCvSharp.Rect(x1, y1, w, h)))
        {
            PerformChannelComparison(roi1, roi2, outputRoi);
        }

        return output;
    }

    /// <summary>
    /// 执行通道比较
    /// </summary>
    private void PerformChannelComparison(Mat roi1, Mat roi2, Mat outputRoi)
    {
        Mat[] channels1 = Cv2.Split(roi1);
        Mat[] channels2 = Cv2.Split(roi2);
        Mat[] outputChannels = new Mat[4];

        // 对RGB通道分别进行比较
        for (int i = 0; i < 3; i++)
        {
            outputChannels[i] = new Mat();

            switch (ComparisonType)
            {
                case "大于":
                    using (var thresholdMat = new Mat())
                    {
                        Cv2.Add(channels2[i], new Scalar(Threshold), thresholdMat);
                        Cv2.Compare(channels1[i], thresholdMat, outputChannels[i], CmpType.GT);
                    }
                    break;
                case "等于":
                    using (var diff = new Mat())
                    {
                        Cv2.Absdiff(channels1[i], channels2[i], diff);
                        Cv2.Compare(diff, new Scalar(Threshold), outputChannels[i], CmpType.LE);
                    }
                    break;
                case "小于":
                    using (var thresholdMat = new Mat())
                    {
                        Cv2.Subtract(channels2[i], new Scalar(Threshold), thresholdMat);
                        Cv2.Compare(channels1[i], thresholdMat, outputChannels[i], CmpType.LT);
                    }
                    break;
                case "大于等于":
                    using (var thresholdMat = new Mat())
                    {
                        Cv2.Subtract(channels2[i], new Scalar(Threshold), thresholdMat);
                        Cv2.Compare(channels1[i], thresholdMat, outputChannels[i], CmpType.GE);
                    }
                    break;
                case "小于等于":
                    using (var thresholdMat = new Mat())
                    {
                        Cv2.Add(channels2[i], new Scalar(Threshold), thresholdMat);
                        Cv2.Compare(channels1[i], thresholdMat, outputChannels[i], CmpType.LE);
                    }
                    break;
                case "不等于":
                    using (var diff = new Mat())
                    {
                        Cv2.Absdiff(channels1[i], channels2[i], diff);
                        Cv2.Compare(diff, new Scalar(Threshold), outputChannels[i], CmpType.GT);
                    }
                    break;
                default:
                    Cv2.Compare(channels1[i], channels2[i], outputChannels[i], CmpType.GT);
                    break;
            }

            // 转换为浮点值（0.0或1.0）
            outputChannels[i].ConvertTo(outputChannels[i], MatType.CV_32F, 1.0 / 255.0);
        }

        // Alpha通道设置为1.0
        outputChannels[3] = Mat.Ones(outputRoi.Size(), MatType.CV_32F);

        Cv2.Merge(outputChannels, outputRoi);

        // 清理资源
        foreach (var ch in channels1) ch.Dispose();
        foreach (var ch in channels2) ch.Dispose();
        foreach (var ch in outputChannels) ch.Dispose();
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
            "/Tunnel-Next;component/Resources/ScriptsControls/TextBoxIdleStyles.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/SliderStyles.xaml",
        };
        foreach (var path in resourcePaths)
        {
            try { resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(path, UriKind.Relative) }); }
            catch { /* 静默处理 */ }
        }

        if (resources.Contains("MainPanelStyle")) mainPanel.Style = resources["MainPanelStyle"] as Style;

        // ViewModel
        var viewModel = CreateViewModel() as ComparisonViewModel;
        mainPanel.DataContext = viewModel;

        // 标题
        var titleLabel = new Label { Content = "比较设置" };
        if (resources.Contains("TitleLabelStyle")) titleLabel.Style = resources["TitleLabelStyle"] as Style;
        mainPanel.Children.Add(titleLabel);

        // 比较类型
        var typeLabel = new Label { Content = "比较类型:", Margin = new Thickness(0, 5, 0, 0) };
        if(resources.Contains("DefaultLabelStyle")) typeLabel.Style = resources["DefaultLabelStyle"] as Style;
        mainPanel.Children.Add(typeLabel);

        var typeComboBox = new ComboBox
        {
            Margin = new Thickness(0, 0, 0, 10),
            ItemsSource = new List<string> { "大于", "等于", "小于", "大于等于", "小于等于", "不等于" }
        };
        if(resources.Contains("DefaultComboBoxStyle")) typeComboBox.Style = resources["DefaultComboBoxStyle"] as Style;
        typeComboBox.SetBinding(ComboBox.SelectedItemProperty, new Binding(nameof(viewModel.ComparisonType)) { Mode = BindingMode.TwoWay });
        mainPanel.Children.Add(typeComboBox);
        
        // X Offset
        var offsetXLabel = new Label { Content = "X偏移:" };
        if(resources.Contains("DefaultLabelStyle")) offsetXLabel.Style = resources["DefaultLabelStyle"] as Style;
        mainPanel.Children.Add(offsetXLabel);
        
        var offsetXTextBox = new TextBox { Margin = new Thickness(0, 0, 0, 10) };
        if(resources.Contains("DefaultTextBoxStyle")) offsetXTextBox.Style = resources["DefaultTextBoxStyle"] as Style;
        offsetXTextBox.SetBinding(TextBox.TextProperty, new Binding(nameof(viewModel.OffsetX)) { Mode = BindingMode.TwoWay });
        mainPanel.Children.Add(offsetXTextBox);

        // Y Offset
        var offsetYLabel = new Label { Content = "Y偏移:" };
        if(resources.Contains("DefaultLabelStyle")) offsetYLabel.Style = resources["DefaultLabelStyle"] as Style;
        mainPanel.Children.Add(offsetYLabel);

        var offsetYTextBox = new TextBox { Margin = new Thickness(0, 0, 0, 10) };
        if(resources.Contains("DefaultTextBoxStyle")) offsetYTextBox.Style = resources["DefaultTextBoxStyle"] as Style;
        offsetYTextBox.SetBinding(TextBox.TextProperty, new Binding(nameof(viewModel.OffsetY)) { Mode = BindingMode.TwoWay });
        mainPanel.Children.Add(offsetYTextBox);
        
        // Threshold
        var thresholdLabel = new Label { Content = $"容差阈值: {viewModel.Threshold:F3}" };
        if(resources.Contains("DefaultLabelStyle")) thresholdLabel.Style = resources["DefaultLabelStyle"] as Style;
        mainPanel.Children.Add(thresholdLabel);

        var thresholdSlider = new Slider
        {
            Minimum = 0,
            Maximum = 1,
            Margin = new Thickness(0, 0, 0, 10)
        };
        if(resources.Contains("DefaultSliderStyle")) thresholdSlider.Style = resources["DefaultSliderStyle"] as Style;
        var thresholdBinding = new Binding(nameof(viewModel.Threshold)) { Mode = BindingMode.TwoWay };
        thresholdSlider.SetBinding(Slider.ValueProperty, thresholdBinding);
        thresholdSlider.ValueChanged += (s, e) =>
        {
            thresholdLabel.Content = $"容差阈值: {e.NewValue:F3}";
        };
        mainPanel.Children.Add(thresholdSlider);

        return mainPanel;
    }

    public override IScriptViewModel CreateViewModel()
    {
        return new ComparisonViewModel(this);
    }

    public override async Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
    {
        await Task.CompletedTask;
    }

    public override Dictionary<string, object> SerializeParameters()
    {
        return new Dictionary<string, object>
        {
            [nameof(ComparisonType)] = ComparisonType,
            [nameof(OffsetX)] = OffsetX,
            [nameof(OffsetY)] = OffsetY,
            [nameof(Threshold)] = Threshold,
            ["NodeInstanceId"] = NodeInstanceId
        };
    }

    public override void DeserializeParameters(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(ComparisonType), out var comparisonType))
            ComparisonType = comparisonType?.ToString() ?? "大于";
        if (data.TryGetValue(nameof(OffsetX), out var offsetX))
            OffsetX = Convert.ToInt32(offsetX);
        if (data.TryGetValue(nameof(OffsetY), out var offsetY))
            OffsetY = Convert.ToInt32(offsetY);
        if (data.TryGetValue(nameof(Threshold), out var threshold))
            Threshold = Convert.ToSingle(threshold);
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

public class ComparisonViewModel : ScriptViewModelBase
{
    private ComparisonScript ComparisonScript => (ComparisonScript)Script;

    public string ComparisonType
    {
        get => ComparisonScript.ComparisonType;
        set
        {
            if (ComparisonScript.ComparisonType != value)
            {
                ComparisonScript.ComparisonType = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(ComparisonType), value);
            }
        }
    }

    public int OffsetX
    {
        get => ComparisonScript.OffsetX;
        set
        {
            if (ComparisonScript.OffsetX != value)
            {
                ComparisonScript.OffsetX = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(OffsetX), value);
            }
        }
    }

    public int OffsetY
    {
        get => ComparisonScript.OffsetY;
        set
        {
            if (ComparisonScript.OffsetY != value)
            {
                ComparisonScript.OffsetY = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(OffsetY), value);
            }
        }
    }

    public float Threshold
    {
        get => ComparisonScript.Threshold;
        set
        {
            if (Math.Abs(ComparisonScript.Threshold - value) > 0.0001f)
            {
                ComparisonScript.Threshold = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(Threshold), value);
            }
        }
    }

    public ComparisonViewModel(ComparisonScript script) : base(script)
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
            [nameof(ComparisonType)] = ComparisonType,
            [nameof(OffsetX)] = OffsetX,
            [nameof(OffsetY)] = OffsetY,
            [nameof(Threshold)] = Threshold
        };
    }

    public override async Task SetParameterDataAsync(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(ComparisonType), out var comparisonType))
            ComparisonType = comparisonType?.ToString() ?? "大于";
        if (data.TryGetValue(nameof(OffsetX), out var offsetX))
            OffsetX = Convert.ToInt32(offsetX);
        if (data.TryGetValue(nameof(OffsetY), out var offsetY))
            OffsetY = Convert.ToInt32(offsetY);
        if (data.TryGetValue(nameof(Threshold), out var threshold))
            Threshold = Convert.ToSingle(threshold);
        await Task.CompletedTask;
    }

    public override async Task ResetToDefaultAsync()
    {
        ComparisonType = "大于";
        OffsetX = 0;
        OffsetY = 0;
        Threshold = 0.001f;
        await Task.CompletedTask;
    }

    public override void Dispose()
    {
        base.Dispose();
    }
}
