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
    Name = "基本处理",
    Author = "Revival Scripts",
    Description = "基本图像处理：亮度、对比度、饱和度、色调、白平衡调整",
    Version = "1.0",
    Category = "图像处理",
    Color = "#90EE90"
)]
public class BasicProcessingScript : RevivalScriptBase
{
    [ScriptParameter(DisplayName = "亮度", Description = "调整图像亮度 (-100 到 100)", Order = 0)]
    public double Brightness { get; set; } = 0;

    [ScriptParameter(DisplayName = "对比度", Description = "调整图像对比度 (-100 到 100)", Order = 1)]
    public double Contrast { get; set; } = 0;

    [ScriptParameter(DisplayName = "饱和度", Description = "调整图像饱和度 (-100 到 100)", Order = 2)]
    public double Saturation { get; set; } = 0;

    [ScriptParameter(DisplayName = "色调", Description = "调整图像色调 (-180 到 180)", Order = 3)]
    public double Hue { get; set; } = 0;

    [ScriptParameter(DisplayName = "白平衡 R", Description = "红色通道白平衡 (0.5 到 2.0)", Order = 4)]
    public double WhiteBalanceR { get; set; } = 1.0;

    [ScriptParameter(DisplayName = "白平衡 G", Description = "绿色通道白平衡 (0.5 到 2.0)", Order = 5)]
    public double WhiteBalanceG { get; set; } = 1.0;

    [ScriptParameter(DisplayName = "白平衡 B", Description = "蓝色通道白平衡 (0.5 到 2.0)", Order = 6)]
    public double WhiteBalanceB { get; set; } = 1.0;

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
        // 检查输入是否有效
        if (!inputs.TryGetValue("f32bmp", out var inputObj) || inputObj == null)
        {
            return new Dictionary<string, object> { ["f32bmp"] = null };
        }

        if (!(inputObj is Mat inputMat) || inputMat.Empty())
        {
            return new Dictionary<string, object> { ["f32bmp"] = null };
        }

        // 检查是否需要处理
        bool needProcessing = Math.Abs(Brightness) > 0.001 || Math.Abs(Contrast) > 0.001 ||
                             Math.Abs(Saturation) > 0.001 || Math.Abs(Hue) > 0.001 ||
                             Math.Abs(WhiteBalanceR - 1.0) > 0.001 || Math.Abs(WhiteBalanceG - 1.0) > 0.001 ||
                             Math.Abs(WhiteBalanceB - 1.0) > 0.001;

        if (!needProcessing)
        {
            // 无需处理，直接返回原图
            return new Dictionary<string, object> { ["f32bmp"] = inputMat.Clone() };
        }

        try
        {
            // 确保输入是RGBA格式
            Mat workingMat = EnsureRGBAFormat(inputMat);
            
            // 大图像优化: 检查图像大小，如果很大则启用分块处理
            bool useTiling = (workingMat.Rows * workingMat.Cols > 2048 * 2048);
            
            Mat resultMat;
            if (useTiling)
            {
                resultMat = ProcessWithTiling(workingMat);
            }
            else
            {
                resultMat = ProcessDirectly(workingMat);
            }

            return new Dictionary<string, object> { ["f32bmp"] = resultMat };
        }
        catch (Exception ex)
        {
            throw new ApplicationException($"基本处理节点处理失败: {ex.Message}", ex);
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
    /// 直接处理小图像
    /// </summary>
    private Mat ProcessDirectly(Mat inputMat)
    {
        Mat resultMat = inputMat.Clone();
        
        // 分离Alpha通道
        Mat[] channels = Cv2.Split(resultMat);
        Mat alphaMat = channels[3].Clone();
        
        // 处理RGB通道
        Mat rgbMat = new Mat();
        Cv2.Merge(new Mat[] { channels[0], channels[1], channels[2] }, rgbMat);
        
        // 应用基本调整
        ApplyBasicAdjustments(rgbMat);
        
        // 应用HSV调整
        if (Math.Abs(Saturation) > 0.001 || Math.Abs(Hue) > 0.001)
        {
            ApplyHSVAdjustments(rgbMat);
        }
        
        // 应用白平衡
        ApplyWhiteBalance(rgbMat);
        
        // 重新合并Alpha通道
        Mat[] finalChannels = Cv2.Split(rgbMat);
        Mat finalMat = new Mat();
        Cv2.Merge(new Mat[] { finalChannels[0], finalChannels[1], finalChannels[2], alphaMat }, finalMat);
        
        // 清理资源
        foreach (var ch in channels) ch.Dispose();
        foreach (var ch in finalChannels) ch.Dispose();
        alphaMat.Dispose();
        rgbMat.Dispose();
        resultMat.Dispose();
        
        return finalMat;
    }

    /// <summary>
    /// 分块处理大图像
    /// </summary>
    private Mat ProcessWithTiling(Mat inputMat)
    {
        const int tileSize = 1024;
        Mat resultMat = new Mat(inputMat.Size(), inputMat.Type());

        // 分离Alpha通道
        Mat[] channels = Cv2.Split(inputMat);
        Mat alphaMat = channels[3].Clone();

        for (int y = 0; y < inputMat.Rows; y += tileSize)
        {
            int yEnd = Math.Min(y + tileSize, inputMat.Rows);
            for (int x = 0; x < inputMat.Cols; x += tileSize)
            {
                int xEnd = Math.Min(x + tileSize, inputMat.Cols);

                // 提取RGB块
                OpenCvSharp.Rect tileRect = new OpenCvSharp.Rect(x, y, xEnd - x, yEnd - y);
                Mat rgbTile = new Mat();
                Cv2.Merge(new Mat[] {
                    channels[0][tileRect],
                    channels[1][tileRect],
                    channels[2][tileRect]
                }, rgbTile);

                // 处理块
                ApplyBasicAdjustments(rgbTile);

                if (Math.Abs(Saturation) > 0.001 || Math.Abs(Hue) > 0.001)
                {
                    ApplyHSVAdjustments(rgbTile);
                }

                ApplyWhiteBalance(rgbTile);

                // 将处理后的块放回结果
                Mat[] tileChannels = Cv2.Split(rgbTile);
                tileChannels[0].CopyTo(resultMat[tileRect]);
                tileChannels[1].CopyTo(resultMat[tileRect]);
                tileChannels[2].CopyTo(resultMat[tileRect]);
                alphaMat[tileRect].CopyTo(resultMat[tileRect]);

                // 清理
                rgbTile.Dispose();
                foreach (var ch in tileChannels) ch.Dispose();
            }
        }

        // 清理资源
        foreach (var ch in channels) ch.Dispose();
        alphaMat.Dispose();

        return resultMat;
    }

    /// <summary>
    /// 应用基本调整（亮度、对比度）
    /// </summary>
    private void ApplyBasicAdjustments(Mat mat)
    {
        // 亮度调整
        if (Math.Abs(Brightness) > 0.001)
        {
            double factor = 1.0 + Brightness / 100.0;
            mat.ConvertTo(mat, -1, factor, 0);
        }

        // 对比度调整
        if (Math.Abs(Contrast) > 0.001)
        {
            double factor = (259.0 * (Contrast + 255.0)) / (255.0 * (259.0 - Contrast));
            mat.ConvertTo(mat, -1, factor, -factor * 0.5 + 0.5);
        }

        // 确保值在有效范围内
        Cv2.Threshold(mat, mat, 1.0, 1.0, ThresholdTypes.Trunc);
        Cv2.Threshold(mat, mat, 0.0, 0.0, ThresholdTypes.Tozero);
    }

    /// <summary>
    /// 应用HSV调整（饱和度、色调）
    /// </summary>
    private void ApplyHSVAdjustments(Mat rgbMat)
    {
        Mat hsvMat = new Mat();
        Cv2.CvtColor(rgbMat, hsvMat, ColorConversionCodes.RGB2HSV);

        Mat[] hsvChannels = Cv2.Split(hsvMat);

        // 色调调整
        if (Math.Abs(Hue) > 0.001)
        {
            double hueShift = Hue / 360.0 * 180.0; // OpenCV HSV H范围是0-180
            hsvChannels[0].ConvertTo(hsvChannels[0], -1, 1.0, hueShift);

            // 处理色调环绕
            Mat mask = new Mat();
            Cv2.Threshold(hsvChannels[0], mask, 180, 1, ThresholdTypes.Binary);
            hsvChannels[0] -= mask * 180;

            Cv2.Threshold(hsvChannels[0], mask, 0, 1, ThresholdTypes.BinaryInv);
            hsvChannels[0] += mask * 180;

            mask.Dispose();
        }

        // 饱和度调整
        if (Math.Abs(Saturation) > 0.001)
        {
            double satFactor = 1.0 + Saturation / 100.0;
            hsvChannels[1].ConvertTo(hsvChannels[1], -1, satFactor, 0);
            Cv2.Threshold(hsvChannels[1], hsvChannels[1], 255, 255, ThresholdTypes.Trunc);
        }

        // 重新合并并转换回RGB
        Cv2.Merge(hsvChannels, hsvMat);
        Cv2.CvtColor(hsvMat, rgbMat, ColorConversionCodes.HSV2RGB);

        // 清理资源
        hsvMat.Dispose();
        foreach (var ch in hsvChannels) ch.Dispose();
    }

    /// <summary>
    /// 应用白平衡调整
    /// </summary>
    private void ApplyWhiteBalance(Mat rgbMat)
    {
        if (Math.Abs(WhiteBalanceR - 1.0) > 0.001 ||
            Math.Abs(WhiteBalanceG - 1.0) > 0.001 ||
            Math.Abs(WhiteBalanceB - 1.0) > 0.001)
        {
            Mat[] channels = Cv2.Split(rgbMat);

            channels[0].ConvertTo(channels[0], -1, WhiteBalanceR, 0);
            channels[1].ConvertTo(channels[1], -1, WhiteBalanceG, 0);
            channels[2].ConvertTo(channels[2], -1, WhiteBalanceB, 0);

            // 确保值在有效范围内
            foreach (var ch in channels)
            {
                Cv2.Threshold(ch, ch, 1.0, 1.0, ThresholdTypes.Trunc);
                Cv2.Threshold(ch, ch, 0.0, 0.0, ThresholdTypes.Tozero);
            }

            Cv2.Merge(channels, rgbMat);

            // 清理资源
            foreach (var ch in channels) ch.Dispose();
        }
    }

    public override FrameworkElement CreateParameterControl()
    {
        var mainPanel = new StackPanel { Margin = new Thickness(5) };

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
        
        // 应用Aero主题样式 - 使用interfacepanelbar的渐变背景
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
        var viewModel = CreateViewModel() as BasicProcessingViewModel;
        mainPanel.DataContext = viewModel;

        // 标题
        var titleLabel = new Label
        {
            Content = "基本图像处理",
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial")
        };
        mainPanel.Children.Add(titleLabel);

        // 创建滑块控件
        mainPanel.Children.Add(CreateSliderControl("亮度", nameof(Brightness), -100, 100, viewModel));
        mainPanel.Children.Add(CreateSliderControl("对比度", nameof(Contrast), -100, 100, viewModel));
        mainPanel.Children.Add(CreateSliderControl("饱和度", nameof(Saturation), -100, 100, viewModel));
        mainPanel.Children.Add(CreateSliderControl("色调", nameof(Hue), -180, 180, viewModel));
        mainPanel.Children.Add(CreateSliderControl("白平衡 R", nameof(WhiteBalanceR), 0.5, 2.0, viewModel));
        mainPanel.Children.Add(CreateSliderControl("白平衡 G", nameof(WhiteBalanceG), 0.5, 2.0, viewModel));
        mainPanel.Children.Add(CreateSliderControl("白平衡 B", nameof(WhiteBalanceB), 0.5, 2.0, viewModel));

        // 创建按钮样式
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
        
        // 重置按钮
        var resetButton = new Button
        {
            Content = "重置所有参数",
            Margin = new Thickness(2, 10, 2, 2),
            Padding = new Thickness(10, 5, 10, 5),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF000000")),
            BorderThickness = new Thickness(1)
        };
        
        // 设置按钮样式
        var buttonStyle = new Style(typeof(Button));
        
        // 普通状态
        var idleTrigger = new Trigger { Property = Button.IsMouseOverProperty, Value = false };
        idleTrigger.Setters.Add(new Setter(Button.BackgroundProperty, buttonIdleBrush));
        buttonStyle.Triggers.Add(idleTrigger);
        
        // 悬停状态
        var hoverTrigger = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
        hoverTrigger.Setters.Add(new Setter(Button.BackgroundProperty, buttonHoverBrush));
        buttonStyle.Triggers.Add(hoverTrigger);
        
        // 按下状态
        var pressedTrigger = new Trigger { Property = Button.IsPressedProperty, Value = true };
        pressedTrigger.Setters.Add(new Setter(Button.BackgroundProperty, buttonPressedBrush));
        buttonStyle.Triggers.Add(pressedTrigger);
        
        resetButton.Style = buttonStyle;

        resetButton.Click += (s, e) =>
        {
            viewModel?.ResetToDefault();
        };

        mainPanel.Children.Add(resetButton);

        return mainPanel;
    }

    private StackPanel CreateSliderControl(string label, string propertyName, double min, double max, BasicProcessingViewModel viewModel)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 5, 0, 5) };

        // 标签和值显示
        var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };

        var labelControl = new Label
        {
            Content = label + ":",
            Width = 80,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11
        };

        var valueLabel = new Label
        {
            Width = 60,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11,
            HorizontalContentAlignment = HorizontalAlignment.Right
        };

        headerPanel.Children.Add(labelControl);
        headerPanel.Children.Add(valueLabel);
        panel.Children.Add(headerPanel);

        // 创建滑块样式
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

        // 滑块
        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            TickFrequency = (max - min) / 20,
            IsSnapToTickEnabled = false,
            Margin = new Thickness(5, 0, 5, 0)
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
        
        slider.Style = sliderStyle;

        // 数据绑定
        var binding = new Binding(propertyName)
        {
            Source = viewModel,
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        };
        slider.SetBinding(Slider.ValueProperty, binding);

        var valueBinding = new Binding(propertyName)
        {
            Source = viewModel,
            Mode = BindingMode.OneWay,
            StringFormat = "F2"
        };
        valueLabel.SetBinding(Label.ContentProperty, valueBinding);

        panel.Children.Add(slider);
        return panel;
    }

    public override IScriptViewModel CreateViewModel()
    {
        return new BasicProcessingViewModel(this);
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
            [nameof(Brightness)] = Brightness,
            [nameof(Contrast)] = Contrast,
            [nameof(Saturation)] = Saturation,
            [nameof(Hue)] = Hue,
            [nameof(WhiteBalanceR)] = WhiteBalanceR,
            [nameof(WhiteBalanceG)] = WhiteBalanceG,
            [nameof(WhiteBalanceB)] = WhiteBalanceB,
            ["NodeInstanceId"] = NodeInstanceId
        };
    }

    public override void DeserializeParameters(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(Brightness), out var brightness))
            Brightness = Convert.ToDouble(brightness);
        if (data.TryGetValue(nameof(Contrast), out var contrast))
            Contrast = Convert.ToDouble(contrast);
        if (data.TryGetValue(nameof(Saturation), out var saturation))
            Saturation = Convert.ToDouble(saturation);
        if (data.TryGetValue(nameof(Hue), out var hue))
            Hue = Convert.ToDouble(hue);
        if (data.TryGetValue(nameof(WhiteBalanceR), out var wbR))
            WhiteBalanceR = Convert.ToDouble(wbR);
        if (data.TryGetValue(nameof(WhiteBalanceG), out var wbG))
            WhiteBalanceG = Convert.ToDouble(wbG);
        if (data.TryGetValue(nameof(WhiteBalanceB), out var wbB))
            WhiteBalanceB = Convert.ToDouble(wbB);
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

public class BasicProcessingViewModel : ScriptViewModelBase
{
    private BasicProcessingScript BasicProcessingScript => (BasicProcessingScript)Script;

    public double Brightness
    {
        get => BasicProcessingScript.Brightness;
        set
        {
            if (Math.Abs(BasicProcessingScript.Brightness - value) > 0.001)
            {
                BasicProcessingScript.Brightness = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(Brightness), value);
            }
        }
    }

    public double Contrast
    {
        get => BasicProcessingScript.Contrast;
        set
        {
            if (Math.Abs(BasicProcessingScript.Contrast - value) > 0.001)
            {
                BasicProcessingScript.Contrast = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(Contrast), value);
            }
        }
    }

    public double Saturation
    {
        get => BasicProcessingScript.Saturation;
        set
        {
            if (Math.Abs(BasicProcessingScript.Saturation - value) > 0.001)
            {
                BasicProcessingScript.Saturation = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(Saturation), value);
            }
        }
    }

    public double Hue
    {
        get => BasicProcessingScript.Hue;
        set
        {
            if (Math.Abs(BasicProcessingScript.Hue - value) > 0.001)
            {
                BasicProcessingScript.Hue = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(Hue), value);
            }
        }
    }

    public double WhiteBalanceR
    {
        get => BasicProcessingScript.WhiteBalanceR;
        set
        {
            if (Math.Abs(BasicProcessingScript.WhiteBalanceR - value) > 0.001)
            {
                BasicProcessingScript.WhiteBalanceR = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(WhiteBalanceR), value);
            }
        }
    }

    public double WhiteBalanceG
    {
        get => BasicProcessingScript.WhiteBalanceG;
        set
        {
            if (Math.Abs(BasicProcessingScript.WhiteBalanceG - value) > 0.001)
            {
                BasicProcessingScript.WhiteBalanceG = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(WhiteBalanceG), value);
            }
        }
    }

    public double WhiteBalanceB
    {
        get => BasicProcessingScript.WhiteBalanceB;
        set
        {
            if (Math.Abs(BasicProcessingScript.WhiteBalanceB - value) > 0.001)
            {
                BasicProcessingScript.WhiteBalanceB = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(WhiteBalanceB), value);
            }
        }
    }

    public BasicProcessingViewModel(BasicProcessingScript script) : base(script)
    {
    }

    private void NotifyParameterChanged(string parameterName, object value)
    {
        if (Script is RevivalScriptBase rsb)
        {
            rsb.OnParameterChanged(parameterName, value);
        }
    }

    public async Task ResetToDefault()
    {
        Brightness = 0;
        Contrast = 0;
        Saturation = 0;
        Hue = 0;
        WhiteBalanceR = 1.0;
        WhiteBalanceG = 1.0;
        WhiteBalanceB = 1.0;
        await Task.CompletedTask;
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
            [nameof(Brightness)] = Brightness,
            [nameof(Contrast)] = Contrast,
            [nameof(Saturation)] = Saturation,
            [nameof(Hue)] = Hue,
            [nameof(WhiteBalanceR)] = WhiteBalanceR,
            [nameof(WhiteBalanceG)] = WhiteBalanceG,
            [nameof(WhiteBalanceB)] = WhiteBalanceB
        };
    }

    public override async Task SetParameterDataAsync(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(Brightness), out var brightness))
            Brightness = Convert.ToDouble(brightness);
        if (data.TryGetValue(nameof(Contrast), out var contrast))
            Contrast = Convert.ToDouble(contrast);
        if (data.TryGetValue(nameof(Saturation), out var saturation))
            Saturation = Convert.ToDouble(saturation);
        if (data.TryGetValue(nameof(Hue), out var hue))
            Hue = Convert.ToDouble(hue);
        if (data.TryGetValue(nameof(WhiteBalanceR), out var wbR))
            WhiteBalanceR = Convert.ToDouble(wbR);
        if (data.TryGetValue(nameof(WhiteBalanceG), out var wbG))
            WhiteBalanceG = Convert.ToDouble(wbG);
        if (data.TryGetValue(nameof(WhiteBalanceB), out var wbB))
            WhiteBalanceB = Convert.ToDouble(wbB);
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
