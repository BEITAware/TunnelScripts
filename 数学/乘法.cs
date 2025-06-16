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
    Name = "乘法",
    Author = "Revival Scripts",
    Description = "图像乘法运算：支持图像与常量或图像与图像的乘法运算，可设置偏移",
    Version = "1.0",
    Category = "数学",
    Color = "#8A2BE2"
)]
public class MultiplicationScript : RevivalScriptBase
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
            ["f32bmp"] = new PortDefinition("f32bmp", true, "输入图像"),
            ["constant"] = new PortDefinition("constant", true, "常量或第二张图像")
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
            
            // 执行乘法操作
            Mat resultMat = PerformMultiplication(workingMat, constantWorkingMat);

            return new Dictionary<string, object> 
            { 
                ["f32bmp"] = resultMat, 
                ["constant"] = constantWorkingMat 
            };
        }
        catch (Exception ex)
        {
            throw new ApplicationException($"乘法节点处理失败: {ex.Message}", ex);
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
    /// 执行乘法操作
    /// </summary>
    private Mat PerformMultiplication(Mat inputMat, Mat constantMat)
    {
        Mat resultMat = inputMat.Clone();
        
        // 检查是否为常量值（1x1像素）还是图像
        bool isImageInput = constantMat.Width > 1 || constantMat.Height > 1;
        
        if (isImageInput)
        {
            // 图像与图像的乘法运算（带偏移）
            PerformImageMultiplication(resultMat, constantMat);
        }
        else
        {
            // 图像与常量的乘法运算
            PerformConstantMultiplication(resultMat, constantMat);
        }

        return resultMat;
    }

    /// <summary>
    /// 执行图像与图像的乘法运算
    /// </summary>
    private void PerformImageMultiplication(Mat resultMat, Mat constantMat)
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

        // 创建ROI进行乘法运算
        using (var roi1 = new Mat(resultMat, new OpenCvSharp.Rect(x1, y1, w, h)))
        using (var roi2 = new Mat(constantMat, new OpenCvSharp.Rect(x2, y2, w, h)))
        using (var tempResult = new Mat())
        {
            // 只对RGB通道进行乘法，保留Alpha通道
            Mat[] channels1 = Cv2.Split(roi1);
            Mat[] channels2 = Cv2.Split(roi2);

            for (int i = 0; i < 3; i++) // 只处理RGB通道
            {
                using (var mulResult = new Mat())
                {
                    Cv2.Multiply(channels1[i], channels2[i], mulResult);
                    // 限制在0-1范围内
                    Cv2.Threshold(mulResult, channels1[i], 1.0, 1.0, ThresholdTypes.Trunc);
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
    /// 执行图像与常量的乘法运算
    /// </summary>
    private void PerformConstantMultiplication(Mat resultMat, Mat constantMat)
    {
        // 获取常量值
        var constantValue = constantMat.At<Vec4f>(0, 0)[0];

        // 如果启用10bit缩放
        if (Enable10BitScaling)
        {
            constantValue = constantValue / 1023.0f;
        }

        // 创建常量标量（只对RGB通道）
        var scalar = new Scalar(constantValue, constantValue, constantValue, 1.0);

        using (var tempResult = new Mat())
        {
            Cv2.Multiply(resultMat, scalar, tempResult);

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

        // 应用Aero主题样式
        mainPanel.Background = new LinearGradientBrush(
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

        // 创建ViewModel
        var viewModel = CreateViewModel() as MultiplicationViewModel;
        mainPanel.DataContext = viewModel;

        // 标题
        var titleLabel = new Label
        {
            Content = "图像乘法",
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial")
        };
        mainPanel.Children.Add(titleLabel);

        // 10bit缩放选项
        var scalingCheckBox = new CheckBox
        {
            Content = "启用10bit数值缩放(0-1023)",
            Margin = new Thickness(5, 10, 5, 5),
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11
        };

        var scalingBinding = new Binding(nameof(Enable10BitScaling))
        {
            Source = viewModel,
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        };
        scalingCheckBox.SetBinding(CheckBox.IsCheckedProperty, scalingBinding);
        mainPanel.Children.Add(scalingCheckBox);

        // X偏移滑块
        var xOffsetLabel = new Label
        {
            Content = "X偏移:",
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11,
            Margin = new Thickness(5, 10, 5, 0)
        };
        mainPanel.Children.Add(xOffsetLabel);

        var xOffsetSlider = new Slider
        {
            Minimum = -1000,
            Maximum = 1000,
            TickFrequency = 100,
            IsSnapToTickEnabled = true,
            Margin = new Thickness(5, 0, 5, 5)
        };

        var xOffsetBinding = new Binding(nameof(OffsetX))
        {
            Source = viewModel,
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        };
        xOffsetSlider.SetBinding(Slider.ValueProperty, xOffsetBinding);
        mainPanel.Children.Add(xOffsetSlider);

        // Y偏移滑块
        var yOffsetLabel = new Label
        {
            Content = "Y偏移:",
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11,
            Margin = new Thickness(5, 10, 5, 0)
        };
        mainPanel.Children.Add(yOffsetLabel);

        var yOffsetSlider = new Slider
        {
            Minimum = -1000,
            Maximum = 1000,
            TickFrequency = 100,
            IsSnapToTickEnabled = true,
            Margin = new Thickness(5, 0, 5, 10)
        };

        var yOffsetBinding = new Binding(nameof(OffsetY))
        {
            Source = viewModel,
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        };
        yOffsetSlider.SetBinding(Slider.ValueProperty, yOffsetBinding);
        mainPanel.Children.Add(yOffsetSlider);

        return mainPanel;
    }

    public override IScriptViewModel CreateViewModel()
    {
        return new MultiplicationViewModel(this);
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

public class MultiplicationViewModel : ScriptViewModelBase
{
    private MultiplicationScript MultiplicationScript => (MultiplicationScript)Script;

    public bool Enable10BitScaling
    {
        get => MultiplicationScript.Enable10BitScaling;
        set
        {
            if (MultiplicationScript.Enable10BitScaling != value)
            {
                MultiplicationScript.Enable10BitScaling = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(Enable10BitScaling), value);
            }
        }
    }

    public int OffsetX
    {
        get => MultiplicationScript.OffsetX;
        set
        {
            if (MultiplicationScript.OffsetX != value)
            {
                MultiplicationScript.OffsetX = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(OffsetX), value);
            }
        }
    }

    public int OffsetY
    {
        get => MultiplicationScript.OffsetY;
        set
        {
            if (MultiplicationScript.OffsetY != value)
            {
                MultiplicationScript.OffsetY = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(OffsetY), value);
            }
        }
    }

    public MultiplicationViewModel(MultiplicationScript script) : base(script)
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
