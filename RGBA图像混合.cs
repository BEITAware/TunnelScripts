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
    Name = "RGBA图像混合",
    Author = "Revival Scripts",
    Description = "混合两个RGBA图像，支持Alpha通道混合",
    Version = "1.0",
    Category = "图像处理",
    Color = "#E74C3C"
)]
public class RGBABlendScript : RevivalScriptBase
{
    [ScriptParameter(DisplayName = "混合模式", Description = "选择混合模式", Order = 0)]
    public string BlendMode { get; set; } = "正常混合";

    [ScriptParameter(DisplayName = "混合强度", Description = "混合强度 (0.0-1.0)", Order = 1)]
    public double BlendStrength { get; set; } = 0.5;

    [ScriptParameter(DisplayName = "保持Alpha", Description = "是否保持原始Alpha通道", Order = 2)]
    public bool PreserveAlpha { get; set; } = true;

    // 处理节点实例标识
    public string NodeInstanceId { get; set; } = string.Empty;

    public override Dictionary<string, PortDefinition> GetInputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            ["f32bmp_base"] = new PortDefinition("f32bmp", false, "基础RGBA图像"),
            ["f32bmp_overlay"] = new PortDefinition("f32bmp", false, "叠加RGBA图像")
        };
    }

    public override Dictionary<string, PortDefinition> GetOutputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            ["f32bmp"] = new PortDefinition("f32bmp", false, "混合后的RGBA图像")
        };
    }

    public override Dictionary<string, object> Process(Dictionary<string, object> inputs, IScriptContext context)
    {

        if (!inputs.TryGetValue("f32bmp_base", out var baseObj) || baseObj is not Mat baseMat)
        {
            throw new ArgumentException("需要输入基础RGBA图像");
        }

        if (!inputs.TryGetValue("f32bmp_overlay", out var overlayObj) || overlayObj is not Mat overlayMat)
        {
            throw new ArgumentException("需要输入叠加RGBA图像");
        }

        if (baseMat.Channels() != 4 || overlayMat.Channels() != 4)
        {
            throw new ArgumentException("输入图像必须是4通道RGBA格式");
        }

        var outputMat = BlendRGBAImages(baseMat, overlayMat);

        return new Dictionary<string, object>
        {
            ["f32bmp"] = outputMat
        };
    }

    /// <summary>
    /// 混合两个RGBA图像
    /// </summary>
    private Mat BlendRGBAImages(Mat baseMat, Mat overlayMat)
    {

        // 确保两个图像尺寸相同
        if (baseMat.Size() != overlayMat.Size())
        {
            var resizedOverlay = new Mat();
            Cv2.Resize(overlayMat, resizedOverlay, baseMat.Size());
            overlayMat = resizedOverlay;
        }

        var outputMat = new Mat();

        switch (BlendMode)
        {
            case "正常混合":
                PerformNormalBlend(baseMat, overlayMat, outputMat);
                break;

            case "Alpha混合":
                PerformAlphaBlend(baseMat, overlayMat, outputMat);
                break;

            case "相加":
                PerformAddBlend(baseMat, overlayMat, outputMat);
                break;

            case "相乘":
                PerformMultiplyBlend(baseMat, overlayMat, outputMat);
                break;

            case "屏幕":
                PerformScreenBlend(baseMat, overlayMat, outputMat);
                break;

            default:
                PerformNormalBlend(baseMat, overlayMat, outputMat);
                break;
        }

        return outputMat;
    }

    /// <summary>
    /// 正常混合
    /// </summary>
    private void PerformNormalBlend(Mat baseMat, Mat overlayMat, Mat outputMat)
    {
        // 简单的线性混合
        Cv2.AddWeighted(baseMat, 1.0 - BlendStrength, overlayMat, BlendStrength, 0, outputMat);

        if (!PreserveAlpha)
        {
            // 如果不保持Alpha，也混合Alpha通道
            return;
        }

        // 保持基础图像的Alpha通道
        var baseChannels = new Mat[4];
        var outputChannels = new Mat[4];
        
        Cv2.Split(baseMat, out baseChannels);
        Cv2.Split(outputMat, out outputChannels);

        // 用基础图像的Alpha通道替换输出的Alpha通道
        outputChannels[3].Dispose();
        outputChannels[3] = baseChannels[3].Clone();

        Cv2.Merge(outputChannels, outputMat);

        // 清理资源
        foreach (var channel in baseChannels) channel.Dispose();
        foreach (var channel in outputChannels) channel.Dispose();
    }

    /// <summary>
    /// Alpha混合 - 使用叠加图像的Alpha通道进行混合
    /// </summary>
    private void PerformAlphaBlend(Mat baseMat, Mat overlayMat, Mat outputMat)
    {
        outputMat.Create(baseMat.Size(), baseMat.Type());

        // 分离通道
        var baseChannels = new Mat[4];
        var overlayChannels = new Mat[4];
        
        Cv2.Split(baseMat, out baseChannels);
        Cv2.Split(overlayMat, out overlayChannels);

        var outputChannels = new Mat[4];

        // 对RGB通道进行Alpha混合
        for (int i = 0; i < 3; i++)
        {
            outputChannels[i] = new Mat();
            
            // Alpha混合公式: result = base * (1 - overlay_alpha * strength) + overlay * (overlay_alpha * strength)
            var alphaWeight = new Mat();
            overlayChannels[3].ConvertTo(alphaWeight, MatType.CV_32F);
            alphaWeight *= BlendStrength;

            var invAlphaWeight = new Mat();
            Cv2.Subtract(Scalar.All(1.0), alphaWeight, invAlphaWeight);

            var basePart = new Mat();
            var overlayPart = new Mat();
            
            Cv2.Multiply(baseChannels[i], invAlphaWeight, basePart);
            Cv2.Multiply(overlayChannels[i], alphaWeight, overlayPart);
            
            Cv2.Add(basePart, overlayPart, outputChannels[i]);

            // 清理临时Mat
            alphaWeight.Dispose();
            invAlphaWeight.Dispose();
            basePart.Dispose();
            overlayPart.Dispose();
        }

        // 处理Alpha通道
        if (PreserveAlpha)
        {
            outputChannels[3] = baseChannels[3].Clone();
        }
        else
        {
            // 混合Alpha通道
            outputChannels[3] = new Mat();
            Cv2.AddWeighted(baseChannels[3], 1.0 - BlendStrength, overlayChannels[3], BlendStrength, 0, outputChannels[3]);
        }

        Cv2.Merge(outputChannels, outputMat);

        // 清理资源
        foreach (var channel in baseChannels) channel.Dispose();
        foreach (var channel in overlayChannels) channel.Dispose();
        foreach (var channel in outputChannels) channel.Dispose();
    }

    /// <summary>
    /// 相加混合
    /// </summary>
    private void PerformAddBlend(Mat baseMat, Mat overlayMat, Mat outputMat)
    {
        var weightedOverlay = new Mat();
        overlayMat.ConvertTo(weightedOverlay, overlayMat.Type(), BlendStrength);
        
        Cv2.Add(baseMat, weightedOverlay, outputMat);
        
        weightedOverlay.Dispose();

        // 保持Alpha通道
        if (PreserveAlpha)
        {
            PreserveBaseAlpha(baseMat, outputMat);
        }
    }

    /// <summary>
    /// 相乘混合
    /// </summary>
    private void PerformMultiplyBlend(Mat baseMat, Mat overlayMat, Mat outputMat)
    {
        var blendedOverlay = new Mat();
        Cv2.AddWeighted(overlayMat, BlendStrength, Scalar.All(1.0 - BlendStrength), 0, 0, blendedOverlay);
        
        Cv2.Multiply(baseMat, blendedOverlay, outputMat);
        
        blendedOverlay.Dispose();

        // 保持Alpha通道
        if (PreserveAlpha)
        {
            PreserveBaseAlpha(baseMat, outputMat);
        }
    }

    /// <summary>
    /// 屏幕混合
    /// </summary>
    private void PerformScreenBlend(Mat baseMat, Mat overlayMat, Mat outputMat)
    {
        var invBase = new Mat();
        var invOverlay = new Mat();
        var invResult = new Mat();

        Cv2.Subtract(Scalar.All(1.0), baseMat, invBase);
        Cv2.Subtract(Scalar.All(1.0), overlayMat, invOverlay);
        
        var weightedInvOverlay = new Mat();
        invOverlay.ConvertTo(weightedInvOverlay, invOverlay.Type(), BlendStrength);
        Cv2.AddWeighted(invOverlay, 1.0 - BlendStrength, Scalar.All(0), 0, 0, weightedInvOverlay);
        
        Cv2.Multiply(invBase, weightedInvOverlay, invResult);
        Cv2.Subtract(Scalar.All(1.0), invResult, outputMat);

        // 清理资源
        invBase.Dispose();
        invOverlay.Dispose();
        invResult.Dispose();
        weightedInvOverlay.Dispose();

        // 保持Alpha通道
        if (PreserveAlpha)
        {
            PreserveBaseAlpha(baseMat, outputMat);
        }
    }

    /// <summary>
    /// 保持基础图像的Alpha通道
    /// </summary>
    private void PreserveBaseAlpha(Mat baseMat, Mat outputMat)
    {
        var baseChannels = new Mat[4];
        var outputChannels = new Mat[4];
        
        Cv2.Split(baseMat, out baseChannels);
        Cv2.Split(outputMat, out outputChannels);

        outputChannels[3].Dispose();
        outputChannels[3] = baseChannels[3].Clone();

        Cv2.Merge(outputChannels, outputMat);

        // 清理资源
        foreach (var channel in baseChannels) channel.Dispose();
        foreach (var channel in outputChannels) channel.Dispose();
    }

    public override Dictionary<string, object> InjectMetadata(Dictionary<string, object> currentMetadata)
    {
        var metadata = new Dictionary<string, object>(currentMetadata);
        metadata["BlendMode"] = BlendMode;
        metadata["BlendStrength"] = BlendStrength;
        metadata["PreserveAlpha"] = PreserveAlpha;
        metadata["NodeInstanceId"] = NodeInstanceId;
        return metadata;
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

        var viewModel = CreateViewModel() as RGBABlendViewModel;
        mainPanel.DataContext = viewModel;

        // 标题
        var titleLabel = new Label
        {
            Content = "RGBA图像混合",
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial")
        };
        mainPanel.Children.Add(titleLabel);

        // 混合模式选择
        CreateBlendModeControls(mainPanel, viewModel);

        // 混合强度控制
        CreateBlendStrengthControls(mainPanel, viewModel);

        // 保持Alpha选项
        CreatePreserveAlphaControls(mainPanel, viewModel);

        return mainPanel;
    }

    private void CreateBlendModeControls(StackPanel parent, RGBABlendViewModel viewModel)
    {
        var modeLabel = new Label
        {
            Content = "混合模式:",
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11
        };
        parent.Children.Add(modeLabel);

        var modeComboBox = new ComboBox
        {
            Margin = new Thickness(0, 0, 0, 10),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11
        };

        modeComboBox.Items.Add("正常混合");
        modeComboBox.Items.Add("Alpha混合");
        modeComboBox.Items.Add("相加");
        modeComboBox.Items.Add("相乘");
        modeComboBox.Items.Add("屏幕");

        var modeBinding = new System.Windows.Data.Binding("BlendMode")
        {
            Source = viewModel,
            Mode = System.Windows.Data.BindingMode.TwoWay,
            UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
        };
        modeComboBox.SetBinding(ComboBox.SelectedItemProperty, modeBinding);

        parent.Children.Add(modeComboBox);
    }

    private void CreateBlendStrengthControls(StackPanel parent, RGBABlendViewModel viewModel)
    {
        var strengthLabel = new Label
        {
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11
        };

        var labelBinding = new System.Windows.Data.Binding("BlendStrengthText")
        {
            Source = viewModel,
            Mode = System.Windows.Data.BindingMode.OneWay,
            UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
        };
        strengthLabel.SetBinding(Label.ContentProperty, labelBinding);

        parent.Children.Add(strengthLabel);

        var strengthSlider = new Slider
        {
            Minimum = 0.0,
            Maximum = 1.0,
            Margin = new Thickness(0, 0, 0, 10),
            TickFrequency = 0.1,
            TickPlacement = System.Windows.Controls.Primitives.TickPlacement.BottomRight,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF"))
        };

        var sliderBinding = new System.Windows.Data.Binding("BlendStrength")
        {
            Source = viewModel,
            Mode = System.Windows.Data.BindingMode.TwoWay,
            UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
        };
        strengthSlider.SetBinding(Slider.ValueProperty, sliderBinding);

        parent.Children.Add(strengthSlider);
    }

    private void CreatePreserveAlphaControls(StackPanel parent, RGBABlendViewModel viewModel)
    {
        var preserveCheckBox = new CheckBox
        {
            Content = "保持基础图像Alpha通道",
            Margin = new Thickness(0, 0, 0, 10),
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11
        };

        var checkBinding = new System.Windows.Data.Binding("PreserveAlpha")
        {
            Source = viewModel,
            Mode = System.Windows.Data.BindingMode.TwoWay,
            UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
        };
        preserveCheckBox.SetBinding(CheckBox.IsCheckedProperty, checkBinding);

        parent.Children.Add(preserveCheckBox);
    }

    public override IScriptViewModel CreateViewModel()
    {
        return new RGBABlendViewModel(this);
    }

    public override async Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
    {
        switch (parameterName)
        {
            case nameof(BlendMode):
                BlendMode = newValue?.ToString() ?? "正常混合";
                break;
            case nameof(BlendStrength):
                if (double.TryParse(newValue?.ToString(), out var blendStrength))
                    BlendStrength = Math.Clamp(blendStrength, 0.0, 1.0);
                break;
            case nameof(PreserveAlpha):
                if (bool.TryParse(newValue?.ToString(), out var preserveAlpha))
                    PreserveAlpha = preserveAlpha;
                break;
        }
        await Task.CompletedTask;
    }

    public override Dictionary<string, object> SerializeParameters()
    {
        return new Dictionary<string, object>
        {
            [nameof(BlendMode)] = BlendMode,
            [nameof(BlendStrength)] = BlendStrength,
            [nameof(PreserveAlpha)] = PreserveAlpha,
            [nameof(NodeInstanceId)] = NodeInstanceId
        };
    }

    public override void DeserializeParameters(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(BlendMode), out var blendMode))
            BlendMode = blendMode?.ToString() ?? "正常混合";

        if (data.TryGetValue(nameof(BlendStrength), out var blendStrength) && double.TryParse(blendStrength?.ToString(), out var bs))
            BlendStrength = Math.Clamp(bs, 0.0, 1.0);

        if (data.TryGetValue(nameof(PreserveAlpha), out var preserveAlpha) && bool.TryParse(preserveAlpha?.ToString(), out var pa))
            PreserveAlpha = pa;

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

public class RGBABlendViewModel : ScriptViewModelBase
{
    private RGBABlendScript RGBABlendScript => (RGBABlendScript)Script;

    public string BlendMode
    {
        get => RGBABlendScript.BlendMode;
        set
        {
            if (RGBABlendScript.BlendMode != value)
            {
                RGBABlendScript.BlendMode = value;
                OnPropertyChanged();

                if (Script is RevivalScriptBase rsb)
                {
                    rsb.OnParameterChanged(nameof(BlendMode), value);
                }
            }
        }
    }

    public double BlendStrength
    {
        get => RGBABlendScript.BlendStrength;
        set
        {
            var clampedValue = Math.Clamp(value, 0.0, 1.0);
            if (Math.Abs(RGBABlendScript.BlendStrength - clampedValue) > 0.001)
            {
                RGBABlendScript.BlendStrength = clampedValue;
                OnPropertyChanged();
                OnPropertyChanged(nameof(BlendStrengthText));

                if (Script is RevivalScriptBase rsb)
                {
                    rsb.OnParameterChanged(nameof(BlendStrength), clampedValue);
                }
            }
        }
    }

    public string BlendStrengthText => $"混合强度: {BlendStrength:F2}";

    public bool PreserveAlpha
    {
        get => RGBABlendScript.PreserveAlpha;
        set
        {
            if (RGBABlendScript.PreserveAlpha != value)
            {
                RGBABlendScript.PreserveAlpha = value;
                OnPropertyChanged();

                if (Script is RevivalScriptBase rsb)
                {
                    rsb.OnParameterChanged(nameof(PreserveAlpha), value);
                }
            }
        }
    }

    public string NodeInstanceId => RGBABlendScript.NodeInstanceId;

    public RGBABlendViewModel(RGBABlendScript script) : base(script) { }

    public override async Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
    {
        await Task.CompletedTask;
    }

    public override ScriptValidationResult ValidateParameter(string parameterName, object value)
    {
        switch (parameterName)
        {
            case nameof(BlendStrength):
                if (!double.TryParse(value?.ToString(), out var blendStrength) || blendStrength < 0.0 || blendStrength > 1.0)
                    return new ScriptValidationResult(false, "混合强度必须在0.0-1.0之间");
                break;
        }
        return new ScriptValidationResult(true);
    }

    public override Dictionary<string, object> GetParameterData()
    {
        return new Dictionary<string, object>
        {
            [nameof(BlendMode)] = BlendMode,
            [nameof(BlendStrength)] = BlendStrength,
            [nameof(PreserveAlpha)] = PreserveAlpha
        };
    }

    public override async Task SetParameterDataAsync(Dictionary<string, object> data)
    {
        await RunOnUIThreadAsync(() =>
        {
            if (data.TryGetValue(nameof(BlendMode), out var blendMode))
                BlendMode = blendMode?.ToString() ?? "正常混合";

            if (data.TryGetValue(nameof(BlendStrength), out var blendStrength) && double.TryParse(blendStrength?.ToString(), out var bs))
                BlendStrength = bs;

            if (data.TryGetValue(nameof(PreserveAlpha), out var preserveAlpha) && bool.TryParse(preserveAlpha?.ToString(), out var pa))
                PreserveAlpha = pa;
        });
    }

    public override async Task ResetToDefaultAsync()
    {
        await RunOnUIThreadAsync(() =>
        {
            BlendMode = "正常混合";
            BlendStrength = 0.5;
            PreserveAlpha = true;
        });
    }

    public override void Dispose()
    {
        base.Dispose();
    }
}
