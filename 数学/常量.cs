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
    Name = "常量",
    Author = "Revival Scripts",
    Description = "提供一个数值常量：创建1x1像素的RGBA图像，填充用户指定的常量值",
    Version = "1.0",
    Category = "数学",
    Color = "#7FFFAA"
)]
public class ConstantScript : RevivalScriptBase
{
    [ScriptParameter(DisplayName = "数值", Description = "常量数值", Order = 0)]
    public string Value { get; set; } = "1.0";

    public string NodeInstanceId { get; set; } = string.Empty;

    public override Dictionary<string, PortDefinition> GetInputPorts()
    {
        return new Dictionary<string, PortDefinition>();
    }

    public override Dictionary<string, PortDefinition> GetOutputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            ["constant"] = new PortDefinition("constant", false, "常量输出")
        };
    }

    public override Dictionary<string, object> Process(Dictionary<string, object> inputs, IScriptContext context)
    {
        try
        {
            // 尝试转换为浮点数
            if (!float.TryParse(Value, out float constantValue))
            {
                constantValue = 1.0f; // 默认值
            }

            // 创建1x1像素的RGBA图像
            Mat constantPixel = new Mat(1, 1, MatType.CV_32FC4);
            constantPixel.Set<Vec4f>(0, 0, new Vec4f(constantValue, constantValue, constantValue, 1.0f));

            return new Dictionary<string, object> { ["constant"] = constantPixel };
        }
        catch (Exception ex)
        {
            // 错误情况下返回默认值(1.0)
            Mat defaultPixel = new Mat(1, 1, MatType.CV_32FC4);
            defaultPixel.Set<Vec4f>(0, 0, new Vec4f(1.0f, 1.0f, 1.0f, 1.0f));
            return new Dictionary<string, object> { ["constant"] = defaultPixel };
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
        var viewModel = CreateViewModel() as ConstantViewModel;
        mainPanel.DataContext = viewModel;

        // 标题
        var titleLabel = new Label
        {
            Content = "数值常量",
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial")
        };
        mainPanel.Children.Add(titleLabel);

        // 数值输入框
        var valueLabel = new Label
        {
            Content = "数值:",
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11,
            Margin = new Thickness(5, 10, 5, 0)
        };
        mainPanel.Children.Add(valueLabel);

        var valueTextBox = new TextBox
        {
            Margin = new Thickness(5, 0, 5, 5),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11,
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF2D3748")),
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF4A5568")),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(5, 3, 5, 3)
        };

        var valueBinding = new Binding(nameof(Value))
        {
            Source = viewModel,
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        };
        valueTextBox.SetBinding(TextBox.TextProperty, valueBinding);
        mainPanel.Children.Add(valueTextBox);

        // 说明文本
        var descriptionLabel = new Label
        {
            Content = "输入数值常量，将创建1x1像素的RGBA图像",
            FontSize = 10,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CCFFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            Margin = new Thickness(5, 0, 5, 10)
        };
        mainPanel.Children.Add(descriptionLabel);

        return mainPanel;
    }

    public override IScriptViewModel CreateViewModel()
    {
        return new ConstantViewModel(this);
    }

    public override async Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
    {
        await Task.CompletedTask;
    }

    public override Dictionary<string, object> SerializeParameters()
    {
        return new Dictionary<string, object>
        {
            [nameof(Value)] = Value,
            ["NodeInstanceId"] = NodeInstanceId
        };
    }

    public override void DeserializeParameters(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(Value), out var value))
            Value = value?.ToString() ?? "1.0";
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

public class ConstantViewModel : ScriptViewModelBase
{
    private ConstantScript ConstantScript => (ConstantScript)Script;

    public string Value
    {
        get => ConstantScript.Value;
        set
        {
            if (ConstantScript.Value != value)
            {
                ConstantScript.Value = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(Value), value);
            }
        }
    }

    public ConstantViewModel(ConstantScript script) : base(script)
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
        if (parameterName == nameof(Value))
        {
            if (float.TryParse(value?.ToString(), out _))
            {
                return new ScriptValidationResult(true);
            }
            return new ScriptValidationResult(false, "请输入有效的数值");
        }
        return new ScriptValidationResult(true);
    }

    public override Dictionary<string, object> GetParameterData()
    {
        return new Dictionary<string, object>
        {
            [nameof(Value)] = Value
        };
    }

    public override async Task SetParameterDataAsync(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(Value), out var value))
            Value = value?.ToString() ?? "1.0";
        await Task.CompletedTask;
    }

    public override async Task ResetToDefaultAsync()
    {
        Value = "1.0";
        await Task.CompletedTask;
    }

    public override void Dispose()
    {
        base.Dispose();
    }
}
