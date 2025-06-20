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
    Name = "加法",
    Author = "BEITAware",
    Description = "图像加法运算：支持图像与常量或图像与图像的加法运算，可设置偏移",
    Version = "1.0",
    Category = "数学",
    Color = "#FF8C00"
)]
public class AdditionScript : RevivalScriptBase
{
    [ScriptParameter(DisplayName = "启用10bit数值缩放", Description = "将0-1023范围缩放到0-1范围", Order = 0)]
    public bool Enable10BitScaling { get; set; } = false;

    [ScriptParameter(DisplayName = "X偏移", Description = "第二个输入的X轴偏移量", Order = 1)]
    public int OffsetX { get; set; } = 0;

    [ScriptParameter(DisplayName = "Y偏移", Description = "第二个输入的Y轴偏移量", Order = 2)]
    public int OffsetY { get; set; } = 0;

    public string NodeInstanceId { get; set; } = string.Empty;

    public override Dictionary<string, PortDefinition> GetInputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            ["f32bmp"] = new PortDefinition("f32bmp", false, "输入图像"),
            ["constant"] = new PortDefinition("constant", false, "常量或第二张图像")
        };
    }

    public override Dictionary<string, PortDefinition> GetOutputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            ["f32bmp"] = new PortDefinition("f32bmp", false, "输出图像"),
            ["constant"] = new PortDefinition("constant", false, "常量输出")
        };
    }

    public override Dictionary<string, object> Process(Dictionary<string, object> inputs, IScriptContext context)
    {
        // 检查第一个输入
        if (!inputs.TryGetValue("f32bmp", out var inputObj) || inputObj == null)
        {
            return new Dictionary<string, object> { ["f32bmp"] = null, ["constant"] = null };
        }

        if (!(inputObj is Mat inputMat) || inputMat.Empty())
        {
            return new Dictionary<string, object> { ["f32bmp"] = null, ["constant"] = null };
        }

        // 检查第二个输入
        if (!inputs.TryGetValue("constant", out var constantObj) || constantObj == null)
        {
            return new Dictionary<string, object> { ["f32bmp"] = inputMat, ["constant"] = null };
        }

        if (!(constantObj is Mat constantMat) || constantMat.Empty())
        {
            return new Dictionary<string, object> { ["f32bmp"] = inputMat, ["constant"] = null };
        }

        try
        {
            // 确保输入是RGBA格式
            Mat workingMat = EnsureRGBAFormat(inputMat);
            Mat constantWorkingMat = EnsureRGBAFormat(constantMat);
            
            // 执行加法操作
            Mat resultMat = PerformAddition(workingMat, constantWorkingMat);

            return new Dictionary<string, object> 
            { 
                ["f32bmp"] = resultMat, 
                ["constant"] = constantWorkingMat 
            };
        }
        catch (Exception ex)
        {
            throw new ApplicationException($"加法节点处理失败: {ex.Message}", ex);
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
    /// 执行加法操作
    /// </summary>
    private Mat PerformAddition(Mat inputMat, Mat constantMat)
    {
        Mat resultMat = inputMat.Clone();
        
        // 检查是否为常量值（1x1像素）还是图像
        bool isImageInput = constantMat.Width > 1 || constantMat.Height > 1;
        
        if (isImageInput)
        {
            // 图像与图像的加法运算（带偏移）
            PerformImageAddition(resultMat, constantMat);
        }
        else
        {
            // 图像与常量的加法运算
            PerformConstantAddition(resultMat, constantMat);
        }

        return resultMat;
    }

    /// <summary>
    /// 执行图像与图像的加法运算
    /// </summary>
    private void PerformImageAddition(Mat resultMat, Mat constantMat)
    {
        int h1 = resultMat.Height;
        int w1 = resultMat.Width;
        int h2 = constantMat.Height;
        int w2 = constantMat.Width;

        // 计算有效重叠区域
        int x1 = Math.Max(0, OffsetX);
        int y1 = Math.Max(0, OffsetY);
        int x2 = Math.Max(0, -OffsetX);
        int y2 = Math.Max(0, -OffsetY);

        int w = Math.Min(w1 - x1, w2 - x2);
        int h = Math.Min(h1 - y1, h2 - y2);

        if (w <= 0 || h <= 0) return;

        // 创建ROI进行加法运算
        using (var roi1 = new Mat(resultMat, new OpenCvSharp.Rect(x1, y1, w, h)))
        using (var roi2 = new Mat(constantMat, new OpenCvSharp.Rect(x2, y2, w, h)))
        using (var tempResult = new Mat())
        {
            // 只对RGB通道进行加法，保留Alpha通道
            Mat[] channels1 = Cv2.Split(roi1);
            Mat[] channels2 = Cv2.Split(roi2);

            for (int i = 0; i < 3; i++) // 只处理RGB通道
            {
                using (var addResult = new Mat())
                {
                    Cv2.Add(channels1[i], channels2[i], addResult);
                    // 限制在0-1范围内
                    Cv2.Threshold(addResult, channels1[i], 1.0, 1.0, ThresholdTypes.Trunc);
                }
            }

            Cv2.Merge(channels1, tempResult);
            tempResult.CopyTo(roi1);

            // 清理资源
            foreach (var ch in channels1) ch.Dispose();
            foreach (var ch in channels2) ch.Dispose();
        }
    }

    /// <summary>
    /// 执行图像与常量的加法运算
    /// </summary>
    private void PerformConstantAddition(Mat resultMat, Mat constantMat)
    {
        // 获取常量值
        var constantValue = constantMat.At<Vec4f>(0, 0)[0];

        // 如果启用10bit缩放
        if (Enable10BitScaling)
        {
            constantValue = constantValue / 1023.0f;
        }

        // 创建常量标量（只对RGB通道）
        var scalar = new Scalar(constantValue, constantValue, constantValue, 0);

        using (var tempResult = new Mat())
        {
            Cv2.Add(resultMat, scalar, tempResult);

            // 分离通道，只限制RGB通道的值范围
            Mat[] channels = Cv2.Split(tempResult);
            for (int i = 0; i < 3; i++) // 只处理RGB通道
            {
                Cv2.Threshold(channels[i], channels[i], 1.0, 1.0, ThresholdTypes.Trunc);
            }

            Cv2.Merge(channels, resultMat);

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
            "/Tunnel-Next;component/Resources/ScriptsControls/TextBoxIdleStyles.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/CheckBoxStyles.xaml"
        };
        foreach (var path in resourcePaths)
        {
            try { resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(path, UriKind.Relative) }); }
            catch { /* 静默处理 */ }
        }

        if (resources.Contains("MainPanelStyle")) mainPanel.Style = resources["MainPanelStyle"] as Style;

        // ViewModel
        var viewModel = CreateViewModel() as AdditionViewModel;
        mainPanel.DataContext = viewModel;

        // 标题
        var titleLabel = new Label { Content = "加法设置" };
        if (resources.Contains("TitleLabelStyle")) titleLabel.Style = resources["TitleLabelStyle"] as Style;
        mainPanel.Children.Add(titleLabel);
        
        // 10-bit Scaling CheckBox
        var scalingCheckBox = new CheckBox { Content = "启用10bit数值缩放", Margin = new Thickness(0, 5, 0, 10) };
        if(resources.Contains("DefaultCheckBoxStyle")) scalingCheckBox.Style = resources["DefaultCheckBoxStyle"] as Style;
        scalingCheckBox.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(viewModel.Enable10BitScaling)) { Mode = BindingMode.TwoWay });
        mainPanel.Children.Add(scalingCheckBox);

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

        return mainPanel;
    }

    public override IScriptViewModel CreateViewModel()
    {
        return new AdditionViewModel(this);
    }

    public override async Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
    {
        await Task.CompletedTask;
    }

    public override Dictionary<string, object> SerializeParameters()
    {
        return new Dictionary<string, object>
        {
            [nameof(Enable10BitScaling)] = Enable10BitScaling,
            [nameof(OffsetX)] = OffsetX,
            [nameof(OffsetY)] = OffsetY,
            ["NodeInstanceId"] = NodeInstanceId
        };
    }

    public override void DeserializeParameters(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(Enable10BitScaling), out var enable10Bit))
            Enable10BitScaling = Convert.ToBoolean(enable10Bit);
        if (data.TryGetValue(nameof(OffsetX), out var offsetX))
            OffsetX = Convert.ToInt32(offsetX);
        if (data.TryGetValue(nameof(OffsetY), out var offsetY))
            OffsetY = Convert.ToInt32(offsetY);
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

public class AdditionViewModel : ScriptViewModelBase
{
    private AdditionScript AdditionScript => (AdditionScript)Script;

    public bool Enable10BitScaling
    {
        get => AdditionScript.Enable10BitScaling;
        set
        {
            if (AdditionScript.Enable10BitScaling != value)
            {
                AdditionScript.Enable10BitScaling = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(Enable10BitScaling), value);
            }
        }
    }

    public int OffsetX
    {
        get => AdditionScript.OffsetX;
        set
        {
            if (AdditionScript.OffsetX != value)
            {
                AdditionScript.OffsetX = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(OffsetX), value);
            }
        }
    }

    public int OffsetY
    {
        get => AdditionScript.OffsetY;
        set
        {
            if (AdditionScript.OffsetY != value)
            {
                AdditionScript.OffsetY = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(OffsetY), value);
            }
        }
    }

    public AdditionViewModel(AdditionScript script) : base(script)
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
            [nameof(Enable10BitScaling)] = Enable10BitScaling,
            [nameof(OffsetX)] = OffsetX,
            [nameof(OffsetY)] = OffsetY
        };
    }

    public override async Task SetParameterDataAsync(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(Enable10BitScaling), out var enable10Bit))
            Enable10BitScaling = Convert.ToBoolean(enable10Bit);
        if (data.TryGetValue(nameof(OffsetX), out var offsetX))
            OffsetX = Convert.ToInt32(offsetX);
        if (data.TryGetValue(nameof(OffsetY), out var offsetY))
            OffsetY = Convert.ToInt32(offsetY);
        await Task.CompletedTask;
    }

    public override async Task ResetToDefaultAsync()
    {
        Enable10BitScaling = false;
        OffsetX = 0;
        OffsetY = 0;
        await Task.CompletedTask;
    }

    public override void Dispose()
    {
        base.Dispose();
    }
}
