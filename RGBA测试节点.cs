using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using Tunnel_Next.Services.Scripting;
using OpenCvSharp;

[RevivalScript(
    Name = "RGBA测试节点",
    Author = "Revival Scripts",
    Description = "测试RGBA支持的节点，生成测试图像或验证RGBA数据",
    Version = "1.0",
    Category = "测试",
    Color = "#FF5722"
)]
public class RGBATestScript : RevivalScriptBase
{
    [ScriptParameter(DisplayName = "测试模式", Description = "选择测试模式", Order = 0)]
    public string TestMode { get; set; } = "生成RGBA测试图";

    [ScriptParameter(DisplayName = "图像尺寸", Description = "生成图像的尺寸", Order = 1)]
    public int ImageSize { get; set; } = 256;

    // 处理节点实例标识
    public string NodeInstanceId { get; set; } = string.Empty;

    public override Dictionary<string, PortDefinition> GetInputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            ["f32bmp"] = new PortDefinition("f32bmp", true, "输入RGBA图像（可选）")
        };
    }

    public override Dictionary<string, PortDefinition> GetOutputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            ["f32bmp"] = new PortDefinition("f32bmp", false, "输出RGBA图像")
        };
    }

    public override Dictionary<string, object> Process(Dictionary<string, object> inputs, IScriptContext context)
    {

        Mat outputMat;

        switch (TestMode)
        {
            case "生成RGBA测试图":
                outputMat = GenerateRGBATestImage();
                break;

            case "验证RGBA数据":
                outputMat = VerifyRGBAData(inputs);
                break;

            case "生成渐变Alpha":
                outputMat = GenerateGradientAlpha();
                break;

            case "生成棋盘Alpha":
                outputMat = GenerateCheckerboardAlpha();
                break;

            default:
                outputMat = GenerateRGBATestImage();
                break;
        }

        return new Dictionary<string, object>
        {
            ["f32bmp"] = outputMat
        };
    }

    /// <summary>
    /// 生成RGBA测试图像
    /// </summary>
    private Mat GenerateRGBATestImage()
    {

        var outputMat = new Mat(ImageSize, ImageSize, MatType.CV_32FC4);

        // 创建四个象限，每个象限不同的颜色和透明度
        var halfSize = ImageSize / 2;

        // 左上角：红色，完全不透明
        var topLeft = new OpenCvSharp.Rect(0, 0, halfSize, halfSize);
        outputMat[topLeft].SetTo(new Scalar(1.0, 0.0, 0.0, 1.0)); // RGBA: 红色，Alpha=1.0

        // 右上角：绿色，75%透明度
        var topRight = new OpenCvSharp.Rect(halfSize, 0, halfSize, halfSize);
        outputMat[topRight].SetTo(new Scalar(0.0, 1.0, 0.0, 0.75)); // RGBA: 绿色，Alpha=0.75

        // 左下角：蓝色，50%透明度
        var bottomLeft = new OpenCvSharp.Rect(0, halfSize, halfSize, halfSize);
        outputMat[bottomLeft].SetTo(new Scalar(0.0, 0.0, 1.0, 0.5)); // RGBA: 蓝色，Alpha=0.5

        // 右下角：白色，25%透明度
        var bottomRight = new OpenCvSharp.Rect(halfSize, halfSize, halfSize, halfSize);
        outputMat[bottomRight].SetTo(new Scalar(1.0, 1.0, 1.0, 0.25)); // RGBA: 白色，Alpha=0.25

        return outputMat;
    }

    /// <summary>
    /// 验证RGBA数据
    /// </summary>
    private Mat VerifyRGBAData(Dictionary<string, object> inputs)
    {

        if (!inputs.TryGetValue("f32bmp", out var inputObj) || inputObj is not Mat inputMat)
        {
            return GenerateRGBATestImage();
        }

        if (inputMat.Channels() != 4)
        {
            throw new ArgumentException("输入图像必须是4通道RGBA格式");
        }

        // 分析RGBA数据
        var channels = new Mat[4];
        Cv2.Split(inputMat, out channels);

        // 计算每个通道的统计信息
        for (int i = 0; i < 4; i++)
        {
            var channelName = i switch { 0 => "R", 1 => "G", 2 => "B", 3 => "A", _ => "?" };
            Cv2.MinMaxLoc(channels[i], out double min, out double max);
            var mean = Cv2.Mean(channels[i]);
        }

        // 清理资源
        foreach (var channel in channels) channel.Dispose();

        // 返回原始图像的克隆
        return inputMat.Clone();
    }

    /// <summary>
    /// 生成渐变Alpha图像
    /// </summary>
    private Mat GenerateGradientAlpha()
    {

        var outputMat = new Mat(ImageSize, ImageSize, MatType.CV_32FC4);

        // 使用分离通道的方式安全填充数据
        var rChannel = new Mat(ImageSize, ImageSize, MatType.CV_32F);
        var gChannel = new Mat(ImageSize, ImageSize, MatType.CV_32F);
        var bChannel = new Mat(ImageSize, ImageSize, MatType.CV_32F);
        var aChannel = new Mat(ImageSize, ImageSize, MatType.CV_32F);

        // 填充各个通道
        for (int y = 0; y < ImageSize; y++)
        {
            for (int x = 0; x < ImageSize; x++)
            {
                float r = (float)x / ImageSize;
                float g = (float)y / ImageSize;
                float b = 0.5f;
                float a = (float)x / ImageSize; // 从左到右渐变透明

                rChannel.Set<float>(y, x, r);
                gChannel.Set<float>(y, x, g);
                bChannel.Set<float>(y, x, b);
                aChannel.Set<float>(y, x, a);
            }
        }

        // 合并通道
        var channels = new Mat[] { rChannel, gChannel, bChannel, aChannel };
        Cv2.Merge(channels, outputMat);

        // 清理资源
        foreach (var channel in channels)
        {
            channel.Dispose();
        }

        return outputMat;
    }

    /// <summary>
    /// 生成棋盘Alpha图像
    /// </summary>
    private Mat GenerateCheckerboardAlpha()
    {

        var outputMat = new Mat(ImageSize, ImageSize, MatType.CV_32FC4);
        var checkSize = ImageSize / 8; // 8x8棋盘

        // 使用分离通道的方式安全填充数据
        var rChannel = new Mat(ImageSize, ImageSize, MatType.CV_32F);
        var gChannel = new Mat(ImageSize, ImageSize, MatType.CV_32F);
        var bChannel = new Mat(ImageSize, ImageSize, MatType.CV_32F);
        var aChannel = new Mat(ImageSize, ImageSize, MatType.CV_32F);

        // 填充各个通道
        for (int y = 0; y < ImageSize; y++)
        {
            for (int x = 0; x < ImageSize; x++)
            {
                int checkX = x / checkSize;
                int checkY = y / checkSize;
                bool isWhite = (checkX + checkY) % 2 == 0;

                float r = isWhite ? 1.0f : 0.0f;
                float g = isWhite ? 1.0f : 0.0f;
                float b = isWhite ? 1.0f : 0.0f;
                float a = isWhite ? 1.0f : 0.5f; // 白色完全不透明，黑色半透明

                rChannel.Set<float>(y, x, r);
                gChannel.Set<float>(y, x, g);
                bChannel.Set<float>(y, x, b);
                aChannel.Set<float>(y, x, a);
            }
        }

        // 合并通道
        var channels = new Mat[] { rChannel, gChannel, bChannel, aChannel };
        Cv2.Merge(channels, outputMat);

        // 清理资源
        foreach (var channel in channels)
        {
            channel.Dispose();
        }

        return outputMat;
    }

    public override Dictionary<string, object> InjectMetadata(Dictionary<string, object> currentMetadata)
    {
        var metadata = new Dictionary<string, object>(currentMetadata);
        metadata["TestMode"] = TestMode;
        metadata["ImageSize"] = ImageSize;
        metadata["NodeInstanceId"] = NodeInstanceId;
        metadata["IsRGBATest"] = true;
        return metadata;
    }

    public override FrameworkElement CreateParameterControl()
    {
        var mainPanel = new StackPanel { Margin = new Thickness(5) };

        // 加载设计资源
        // 使用本地样式代替外部资源文件
        LinearGradientBrush textBoxIdleBrush = new LinearGradientBrush();
        textBoxIdleBrush.StartPoint = new System.Windows.Point(0.437947, 5.5271);
        textBoxIdleBrush.EndPoint = new System.Windows.Point(0.437947, -4.52682);
        textBoxIdleBrush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#91007BFF"), 0.142857));
        textBoxIdleBrush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#00FFFFFF"), 0.502783));
        textBoxIdleBrush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#C30099FF"), 0.792208));
        
        LinearGradientBrush textBoxActivatedBrush = new LinearGradientBrush();
        textBoxActivatedBrush.StartPoint = new System.Windows.Point(0.437947, 5.5271);
        textBoxActivatedBrush.EndPoint = new System.Windows.Point(0.437947, -4.52682);
        textBoxActivatedBrush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#AF00C7FF"), 0.413729));
        textBoxActivatedBrush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#00FFFFFF"), 0.495362));
        textBoxActivatedBrush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#FF00ECFF"), 0.692022));
        
        RadialGradientBrush buttonIdleBrush = new RadialGradientBrush();
        buttonIdleBrush.RadiusX = 2.15218;
        buttonIdleBrush.RadiusY = 1.68352;
        buttonIdleBrush.Center = new System.Windows.Point(0.499961, 0.992728);
        buttonIdleBrush.GradientOrigin = new System.Windows.Point(0.499961, 0.992728);
        buttonIdleBrush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#29FFFFFF"), 0));
        buttonIdleBrush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#00000004"), 0.380334));
        buttonIdleBrush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#00FFFFFF"), 0.41744));
        buttonIdleBrush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#5EFFFFFF"), 0.769944));
        buttonIdleBrush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#4AFFFFFF"), 0.892393));
        
        RadialGradientBrush buttonPressedBrush = new RadialGradientBrush();
        buttonPressedBrush.RadiusX = 2.15219;
        buttonPressedBrush.RadiusY = 1.68352;
        buttonPressedBrush.Center = new System.Windows.Point(0.499962, 0.992728);
        buttonPressedBrush.GradientOrigin = new System.Windows.Point(0.499962, 0.992728);
        buttonPressedBrush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#FF38CBF4"), 0.0426716));
        buttonPressedBrush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#00000004"), 0.506494));
        buttonPressedBrush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#00FFFFFF"), 0.517625));
        buttonPressedBrush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#5EFFFFFF"), 0.736549));
        buttonPressedBrush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#4AFFFFFF"), 0.892393));
        
        LinearGradientBrush buttonHoverBrush = new LinearGradientBrush();
        buttonHoverBrush.StartPoint = new System.Windows.Point(0.5, -0.667874);
        buttonHoverBrush.EndPoint = new System.Windows.Point(0.5, 1.66788);
        buttonHoverBrush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#00FFFFFF"), 0));
        buttonHoverBrush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#1AFFFFFF"), 0.135436));
        buttonHoverBrush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#17FFFFFF"), 0.487941));
        buttonHoverBrush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#00000004"), 0.517625));
        buttonHoverBrush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#FF1F8EAD"), 0.729128));
        
        LinearGradientBrush sliderBrush = new LinearGradientBrush();
        sliderBrush.StartPoint = new System.Windows.Point(0.5, 21.1807);
        sliderBrush.EndPoint = new System.Windows.Point(0.5, -20.1807);
        sliderBrush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#2600C7FF"), 0.48166));
        sliderBrush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#00FFFFFF"), 0.500902));
        sliderBrush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#2500E3FF"), 0.50932));
        
        RadialGradientBrush sliderHandleBrush = new RadialGradientBrush();
        sliderHandleBrush.RadiusX = 1.58251;
        sliderHandleBrush.RadiusY = 0.882493;
        sliderHandleBrush.Center = new System.Windows.Point(0.500127, 1.00007);
        sliderHandleBrush.GradientOrigin = new System.Windows.Point(0.500127, 1.00007);
        sliderHandleBrush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#63FFFFFF"), 0));
        sliderHandleBrush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#00FFFFFF"), 0.320505));
        sliderHandleBrush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#7000E3FF"), 0.711365));
        sliderHandleBrush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#8E00FFF6"), 0.890559));
        sliderHandleBrush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#B853FFEC"), 1));
        
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

        var viewModel = CreateViewModel() as RGBATestViewModel;
        mainPanel.DataContext = viewModel;

        // 标题
        var titleLabel = new Label
        {
            Content = "RGBA测试设置",
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial")
        };
        mainPanel.Children.Add(titleLabel);

        // 测试模式选择
        var modeLabel = new Label
        {
            Content = "测试模式:",
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11
        };
        mainPanel.Children.Add(modeLabel);

        var modeComboBox = new ComboBox
        {
            Margin = new Thickness(0, 0, 0, 10),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11,
            Background = textBoxIdleBrush,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF000000")),
            BorderThickness = new Thickness(1)
        };

        modeComboBox.Items.Add("生成RGBA测试图");
        modeComboBox.Items.Add("验证RGBA数据");
        modeComboBox.Items.Add("生成渐变Alpha");
        modeComboBox.Items.Add("生成棋盘Alpha");

        var modeBinding = new System.Windows.Data.Binding("TestMode")
        {
            Source = viewModel,
            Mode = System.Windows.Data.BindingMode.TwoWay,
            UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
        };
        modeComboBox.SetBinding(ComboBox.SelectedItemProperty, modeBinding);

        mainPanel.Children.Add(modeComboBox);

        // 图像尺寸设置
        var sizeLabel = new Label
        {
            Content = $"图像尺寸: {ImageSize}x{ImageSize}",
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11
        };

        var sizeBinding = new System.Windows.Data.Binding("ImageSizeText")
        {
            Source = viewModel,
            Mode = System.Windows.Data.BindingMode.OneWay,
            UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
        };
        sizeLabel.SetBinding(Label.ContentProperty, sizeBinding);

        mainPanel.Children.Add(sizeLabel);

        var sizeSlider = new Slider
        {
            Minimum = 64,
            Maximum = 512,
            Value = ImageSize,
            Margin = new Thickness(0, 0, 0, 10),
            TickFrequency = 64,
            TickPlacement = System.Windows.Controls.Primitives.TickPlacement.BottomRight,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF"))
        };
        
        // 创建滑块样式
        var sliderStyle = new Style(typeof(Slider));
        
        // 滑块轨道样式
        var trackStyle = new Style(typeof(Track));
        trackStyle.Setters.Add(new Setter(Control.BackgroundProperty, sliderBrush));
        
        // 滑块手柄样式
        var thumbStyle = new Style(typeof(Thumb));
        thumbStyle.Setters.Add(new Setter(Control.BackgroundProperty, sliderHandleBrush));
        thumbStyle.Setters.Add(new Setter(FrameworkElement.WidthProperty, 16.0));
        thumbStyle.Setters.Add(new Setter(FrameworkElement.HeightProperty, 16.0));
        
        sliderStyle.Resources.Add(typeof(Track), trackStyle);
        sliderStyle.Resources.Add(typeof(Thumb), thumbStyle);
        
        sizeSlider.Style = sliderStyle;

        var sliderBinding = new System.Windows.Data.Binding("ImageSize")
        {
            Source = viewModel,
            Mode = System.Windows.Data.BindingMode.TwoWay,
            UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
        };
        sizeSlider.SetBinding(Slider.ValueProperty, sliderBinding);

        mainPanel.Children.Add(sizeSlider);

        return mainPanel;
    }

    public override IScriptViewModel CreateViewModel()
    {
        return new RGBATestViewModel(this);
    }

    public override async Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
    {
        switch (parameterName)
        {
            case nameof(TestMode):
                TestMode = newValue?.ToString() ?? "生成RGBA测试图";
                break;
            case nameof(ImageSize):
                if (int.TryParse(newValue?.ToString(), out var imageSize))
                    ImageSize = Math.Clamp(imageSize, 64, 512);
                break;
        }
        await Task.CompletedTask;
    }

    public override Dictionary<string, object> SerializeParameters()
    {
        return new Dictionary<string, object>
        {
            [nameof(TestMode)] = TestMode,
            [nameof(ImageSize)] = ImageSize,
            [nameof(NodeInstanceId)] = NodeInstanceId
        };
    }

    public override void DeserializeParameters(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(TestMode), out var testMode))
            TestMode = testMode?.ToString() ?? "生成RGBA测试图";

        if (data.TryGetValue(nameof(ImageSize), out var imageSize) && int.TryParse(imageSize?.ToString(), out var size))
            ImageSize = Math.Clamp(size, 64, 512);

        if (data.TryGetValue(nameof(NodeInstanceId), out var nodeId))
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

public class RGBATestViewModel : ScriptViewModelBase
{
    private RGBATestScript RGBATestScript => (RGBATestScript)Script;

    public string TestMode
    {
        get => RGBATestScript.TestMode;
        set
        {
            if (RGBATestScript.TestMode != value)
            {
                RGBATestScript.TestMode = value;
                OnPropertyChanged();

                if (Script is RevivalScriptBase rsb)
                {
                    rsb.OnParameterChanged(nameof(TestMode), value);
                }
            }
        }
    }

    public int ImageSize
    {
        get => RGBATestScript.ImageSize;
        set
        {
            var clampedValue = Math.Clamp(value, 64, 512);
            if (RGBATestScript.ImageSize != clampedValue)
            {
                RGBATestScript.ImageSize = clampedValue;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ImageSizeText));

                if (Script is RevivalScriptBase rsb)
                {
                    rsb.OnParameterChanged(nameof(ImageSize), clampedValue);
                }
            }
        }
    }

    public string ImageSizeText => $"图像尺寸: {ImageSize}x{ImageSize}";

    public string NodeInstanceId => RGBATestScript.NodeInstanceId;

    public RGBATestViewModel(RGBATestScript script) : base(script) { }

    public override async Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
    {
        await Task.CompletedTask;
    }

    public override ScriptValidationResult ValidateParameter(string parameterName, object value)
    {
        switch (parameterName)
        {
            case nameof(ImageSize):
                if (!int.TryParse(value?.ToString(), out var imageSize) || imageSize < 64 || imageSize > 512)
                    return new ScriptValidationResult(false, "图像尺寸必须在64-512之间");
                break;
        }
        return new ScriptValidationResult(true);
    }

    public override Dictionary<string, object> GetParameterData()
    {
        return new Dictionary<string, object>
        {
            [nameof(TestMode)] = TestMode,
            [nameof(ImageSize)] = ImageSize
        };
    }

    public override async Task SetParameterDataAsync(Dictionary<string, object> data)
    {
        await RunOnUIThreadAsync(() =>
        {
            if (data.TryGetValue(nameof(TestMode), out var testMode))
                TestMode = testMode?.ToString() ?? "生成RGBA测试图";

            if (data.TryGetValue(nameof(ImageSize), out var imageSize) && int.TryParse(imageSize?.ToString(), out var size))
                ImageSize = size;
        });
    }

    public override async Task ResetToDefaultAsync()
    {
        await RunOnUIThreadAsync(() =>
        {
            TestMode = "生成RGBA测试图";
            ImageSize = 256;
        });
    }

    public override void Dispose()
    {
        base.Dispose();
    }
}
