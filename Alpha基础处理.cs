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
    Name = "Alpha通道处理",
    Author = "BEITAware",
    Description = "处理图像的Alpha透明度通道",
    Version = "1.0",
    Category = "图像处理",
    Color = "#9B59B6"
)]
public class AlphaChannelScript : RevivalScriptBase
{
    [ScriptParameter(DisplayName = "操作类型", Description = "选择Alpha通道操作", Order = 0)]
    public string Operation { get; set; } = "调整透明度";

    [ScriptParameter(DisplayName = "透明度值", Description = "透明度值 (0.0-1.0)", Order = 1)]
    public double AlphaValue { get; set; } = 1.0;

    [ScriptParameter(DisplayName = "反转Alpha", Description = "反转Alpha通道", Order = 2)]
    public bool InvertAlpha { get; set; } = false;

    // 处理节点实例标识
    public string NodeInstanceId { get; set; } = string.Empty;

    public override Dictionary<string, PortDefinition> GetInputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            ["f32bmp"] = new PortDefinition("f32bmp", false, "输入RGBA图像")
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

        if (!inputs.TryGetValue("f32bmp", out var inputObj) || inputObj is not Mat inputMat)
        {
            throw new ArgumentException("需要输入RGBA图像");
        }

        if (inputMat.Channels() != 4)
        {
            throw new ArgumentException("输入图像必须是4通道RGBA格式");
        }

        var outputMat = ProcessAlphaChannel(inputMat);

        return new Dictionary<string, object>
        {
            ["f32bmp"] = outputMat
        };
    }

    /// <summary>
    /// 处理Alpha通道
    /// </summary>
    private Mat ProcessAlphaChannel(Mat inputMat)
    {
        var outputMat = inputMat.Clone();
        

        // 分离通道
        var channels = new Mat[4];
        Cv2.Split(outputMat, out channels);

        var alphaChannel = channels[3]; // Alpha通道

        switch (Operation)
        {
            case "调整透明度":
                // 将Alpha通道乘以指定值
                alphaChannel *= AlphaValue;
                break;

            case "设置透明度":
                // 将Alpha通道设置为指定值
                alphaChannel.SetTo(new Scalar(AlphaValue));
                break;

            case "渐变透明":
                // 创建从左到右的渐变透明效果
                CreateGradientAlpha(alphaChannel);
                break;

            case "保持原样":
                // 不修改Alpha通道
                break;
        }

        // 如果需要反转Alpha通道
        if (InvertAlpha)
        {
            var invertedAlpha = new Mat();
            Cv2.BitwiseNot(alphaChannel, invertedAlpha);
            alphaChannel.Dispose();
            channels[3] = invertedAlpha;
        }

        // 合并通道
        var result = new Mat();
        Cv2.Merge(channels, result);

        // 清理资源
        foreach (var channel in channels)
        {
            channel.Dispose();
        }

        return result;
    }

    /// <summary>
    /// 创建渐变Alpha效果
    /// </summary>
    private void CreateGradientAlpha(Mat alphaChannel)
    {
        var width = alphaChannel.Width;
        var height = alphaChannel.Height;

        // 创建从左到右的线性渐变
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float gradientValue = (float)x / width * (float)AlphaValue;
                alphaChannel.Set(y, x, gradientValue);
            }
        }
    }

    public override Dictionary<string, object> InjectMetadata(Dictionary<string, object> currentMetadata)
    {
        var metadata = new Dictionary<string, object>(currentMetadata);
        metadata["AlphaOperation"] = Operation;
        metadata["AlphaValue"] = AlphaValue;
        metadata["InvertAlpha"] = InvertAlpha;
        metadata["NodeInstanceId"] = NodeInstanceId;
        return metadata;
    }

    public override FrameworkElement CreateParameterControl()
    {
        var mainPanel = new StackPanel { Margin = new Thickness(5) };

        // 加载所有需要的资源字典
        var resources = new ResourceDictionary();
        var resourcePaths = new[]
        {
            "/Tunnel-Next;component/Resources/ScriptsControls/SharedBrushes.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/LabelStyles.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/PanelStyles.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/ComboBoxStyles.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/SliderStyles.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/CheckBoxStyles.xaml"
        };

        foreach (var path in resourcePaths)
        {
            try
            {
                resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(path, UriKind.Relative) });
            }
            catch (Exception)
            {
                // 静默处理资源加载失败
            }
        }

        // 应用主面板样式
        if (resources.Contains("MainPanelStyle"))
        {
            mainPanel.Style = resources["MainPanelStyle"] as Style;
        }

        var viewModel = CreateViewModel() as AlphaChannelViewModel;
        mainPanel.DataContext = viewModel;

        // 标题
        var titleLabel = new Label { Content = "Alpha通道处理" };
        if (resources.Contains("TitleLabelStyle"))
        {
            titleLabel.Style = resources["TitleLabelStyle"] as Style;
        }
        mainPanel.Children.Add(titleLabel);

        // 操作类型选择
        CreateOperationControls(mainPanel, viewModel, resources);

        // Alpha值控制
        CreateAlphaValueControls(mainPanel, viewModel, resources);

        // 反转Alpha选项
        CreateInvertAlphaControls(mainPanel, viewModel, resources);

        return mainPanel;
    }

    private void CreateOperationControls(StackPanel parent, AlphaChannelViewModel viewModel, ResourceDictionary resources)
    {
        var operationLabel = new Label { Content = "操作类型:" };
        if (resources.Contains("DefaultLabelStyle"))
        {
            operationLabel.Style = resources["DefaultLabelStyle"] as Style;
        }
        parent.Children.Add(operationLabel);

        var operationComboBox = new ComboBox { Margin = new Thickness(0, 0, 0, 10) };
        if (resources.Contains("DefaultComboBoxStyle"))
        {
            operationComboBox.Style = resources["DefaultComboBoxStyle"] as Style;
        }

        operationComboBox.Items.Add("调整透明度");
        operationComboBox.Items.Add("设置透明度");
        operationComboBox.Items.Add("渐变透明");
        operationComboBox.Items.Add("保持原样");

        var operationBinding = new System.Windows.Data.Binding("Operation")
        {
            Source = viewModel,
            Mode = System.Windows.Data.BindingMode.TwoWay,
            UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
        };
        operationComboBox.SetBinding(ComboBox.SelectedItemProperty, operationBinding);

        parent.Children.Add(operationComboBox);
    }

    private void CreateAlphaValueControls(StackPanel parent, AlphaChannelViewModel viewModel, ResourceDictionary resources)
    {
        var alphaLabel = new Label();
        if (resources.Contains("DefaultLabelStyle"))
        {
            alphaLabel.Style = resources["DefaultLabelStyle"] as Style;
        }

        var labelBinding = new System.Windows.Data.Binding("AlphaValueText")
        {
            Source = viewModel,
            Mode = System.Windows.Data.BindingMode.OneWay,
        };
        alphaLabel.SetBinding(Label.ContentProperty, labelBinding);

        parent.Children.Add(alphaLabel);

        var alphaSlider = new Slider
        {
            Margin = new Thickness(0, 0, 0, 10),
            Minimum = 0.0,
            Maximum = 1.0,
            Value = viewModel.AlphaValue,
            LargeChange = 0.1,
            SmallChange = 0.01
        };

        if (resources.Contains("DefaultSliderStyle")) // Assuming a style key exists
        {
            alphaSlider.Style = resources["DefaultSliderStyle"] as Style;
        }

        var sliderBinding = new System.Windows.Data.Binding("AlphaValue")
        {
            Source = viewModel,
            Mode = System.Windows.Data.BindingMode.TwoWay,
            UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
        };
        alphaSlider.SetBinding(Slider.ValueProperty, sliderBinding);

        parent.Children.Add(alphaSlider);
    }

    private void CreateInvertAlphaControls(StackPanel parent, AlphaChannelViewModel viewModel, ResourceDictionary resources)
    {
        var invertCheckBox = new CheckBox { Content = "反转Alpha通道" };
        if (resources.Contains("DefaultCheckBoxStyle"))
        {
            invertCheckBox.Style = resources["DefaultCheckBoxStyle"] as Style;
        }
        
        var invertBinding = new System.Windows.Data.Binding("InvertAlpha")
        {
            Source = viewModel,
            Mode = System.Windows.Data.BindingMode.TwoWay,
            UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
        };
        invertCheckBox.SetBinding(CheckBox.IsCheckedProperty, invertBinding);
        parent.Children.Add(invertCheckBox);
    }

    public override IScriptViewModel CreateViewModel()
    {
        return new AlphaChannelViewModel(this);
    }

    public override async Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
    {
        switch (parameterName)
        {
            case nameof(Operation):
                Operation = newValue?.ToString() ?? "调整透明度";
                break;
            case nameof(AlphaValue):
                if (double.TryParse(newValue?.ToString(), out var alphaValue))
                    AlphaValue = Math.Clamp(alphaValue, 0.0, 1.0);
                break;
            case nameof(InvertAlpha):
                if (bool.TryParse(newValue?.ToString(), out var invertAlpha))
                    InvertAlpha = invertAlpha;
                break;
        }
        await Task.CompletedTask;
    }

    public override Dictionary<string, object> SerializeParameters()
    {
        return new Dictionary<string, object>
        {
            [nameof(Operation)] = Operation,
            [nameof(AlphaValue)] = AlphaValue,
            [nameof(InvertAlpha)] = InvertAlpha,
            [nameof(NodeInstanceId)] = NodeInstanceId
        };
    }

    public override void DeserializeParameters(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(Operation), out var operation))
            Operation = operation?.ToString() ?? "调整透明度";

        if (data.TryGetValue(nameof(AlphaValue), out var alphaValue) && double.TryParse(alphaValue?.ToString(), out var av))
            AlphaValue = Math.Clamp(av, 0.0, 1.0);

        if (data.TryGetValue(nameof(InvertAlpha), out var invertAlpha) && bool.TryParse(invertAlpha?.ToString(), out var ia))
            InvertAlpha = ia;

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

public class AlphaChannelViewModel : ScriptViewModelBase
{
    private AlphaChannelScript AlphaChannelScript => (AlphaChannelScript)Script;

    public string Operation
    {
        get => AlphaChannelScript.Operation;
        set
        {
            if (AlphaChannelScript.Operation != value)
            {
                AlphaChannelScript.Operation = value;
                OnPropertyChanged();

                if (Script is RevivalScriptBase rsb)
                {
                    rsb.OnParameterChanged(nameof(Operation), value);
                }
            }
        }
    }

    public double AlphaValue
    {
        get => AlphaChannelScript.AlphaValue;
        set
        {
            var clampedValue = Math.Clamp(value, 0.0, 1.0);
            if (Math.Abs(AlphaChannelScript.AlphaValue - clampedValue) > 0.001)
            {
                AlphaChannelScript.AlphaValue = clampedValue;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AlphaValueText));

                if (Script is RevivalScriptBase rsb)
                {
                    rsb.OnParameterChanged(nameof(AlphaValue), clampedValue);
                }
            }
        }
    }

    public string AlphaValueText => $"透明度值: {AlphaValue:F2}";

    public bool InvertAlpha
    {
        get => AlphaChannelScript.InvertAlpha;
        set
        {
            if (AlphaChannelScript.InvertAlpha != value)
            {
                AlphaChannelScript.InvertAlpha = value;
                OnPropertyChanged();

                if (Script is RevivalScriptBase rsb)
                {
                    rsb.OnParameterChanged(nameof(InvertAlpha), value);
                }
            }
        }
    }

    public string NodeInstanceId => AlphaChannelScript.NodeInstanceId;

    public AlphaChannelViewModel(AlphaChannelScript script) : base(script) { }

    public override async Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
    {
        await Task.CompletedTask;
    }

    public override ScriptValidationResult ValidateParameter(string parameterName, object value)
    {
        switch (parameterName)
        {
            case nameof(AlphaValue):
                if (!double.TryParse(value?.ToString(), out var alphaValue) || alphaValue < 0.0 || alphaValue > 1.0)
                    return new ScriptValidationResult(false, "透明度值必须在0.0-1.0之间");
                break;
        }
        return new ScriptValidationResult(true);
    }

    public override Dictionary<string, object> GetParameterData()
    {
        return new Dictionary<string, object>
        {
            [nameof(Operation)] = Operation,
            [nameof(AlphaValue)] = AlphaValue,
            [nameof(InvertAlpha)] = InvertAlpha
        };
    }

    public override async Task SetParameterDataAsync(Dictionary<string, object> data)
    {
        await RunOnUIThreadAsync(() =>
        {
            if (data.TryGetValue(nameof(Operation), out var operation))
                Operation = operation?.ToString() ?? "调整透明度";

            if (data.TryGetValue(nameof(AlphaValue), out var alphaValue) && double.TryParse(alphaValue?.ToString(), out var av))
                AlphaValue = av;

            if (data.TryGetValue(nameof(InvertAlpha), out var invertAlpha) && bool.TryParse(invertAlpha?.ToString(), out var ia))
                InvertAlpha = ia;
        });
    }

    public override async Task ResetToDefaultAsync()
    {
        await RunOnUIThreadAsync(() =>
        {
            Operation = "调整透明度";
            AlphaValue = 1.0;
            InvertAlpha = false;
        });
    }

    public override void Dispose()
    {
        base.Dispose();
    }
}
