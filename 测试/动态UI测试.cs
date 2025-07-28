using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Tunnel_Next.Services.Scripting;
using OpenCvSharp;

[TunnelExtensionScript(
    Name = "动态UI测试",
    Author = "测试",
    Description = "测试动态UI更新功能",
    Version = "1.0",
    Category = "测试",
    Color = "#FF6B35"
)]
public class DynamicUITestScript : DynamicUITunnelExtensionScriptBase
{
    [ScriptParameter(DisplayName = "基础参数", Order = 0)]
    public double BaseValue { get; set; } = 50;

    private int _connectionCount = 0;
    private StackPanel? _dynamicPanel;
    private Label? _statusLabel;

    public override Dictionary<string, PortDefinition> GetInputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            ["input1"] = new PortDefinition("f32bmp", false, "输入1"),
            ["input2"] = new PortDefinition("f32bmp", false, "输入2"),
            ["input3"] = new PortDefinition("f32bmp", false, "输入3"),
            ["input4"] = new PortDefinition("f32bmp", false, "输入4")
        };
    }

    public override Dictionary<string, PortDefinition> GetOutputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            ["output"] = new PortDefinition("f32bmp", false, "输出")
        };
    }

    public override Dictionary<string, object> Process(Dictionary<string, object> inputs, IScriptContext context)
    {
        // 简单处理：返回第一个有效输入
        var firstInput = inputs.Values.FirstOrDefault(v => v is Mat mat && !mat.Empty());
        return new Dictionary<string, object> { ["output"] = firstInput ?? new Mat() };
    }

    public override void OnConnectionChanged(ScriptConnectionInfo connectionInfo)
    {
        _connectionCount = connectionInfo.TotalInputConnections;
        System.Diagnostics.Debug.WriteLine($"[动态UI测试] 连接数量变化: {_connectionCount}");
        base.OnConnectionChanged(connectionInfo);
    }

    public override string? GetUIUpdateToken()
    {
        return $"connections:{_connectionCount}";
    }

    public override bool TryUpdateUI(FrameworkElement existingControl, ScriptConnectionInfo changeInfo)
    {
        if (_dynamicPanel != null && _statusLabel != null)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                // 更新状态标签
                _statusLabel.Content = $"当前连接数: {changeInfo.TotalInputConnections}";
                _statusLabel.Foreground = changeInfo.TotalInputConnections > 0 ? Brushes.Green : Brushes.Gray;

                // 清除并重建动态控件
                _dynamicPanel.Children.Clear();
                
                for (int i = 1; i <= changeInfo.TotalInputConnections; i++)
                {
                    var inputPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
                    inputPanel.Children.Add(new Label { Content = $"输入{i}权重:", Width = 80 });
                    inputPanel.Children.Add(new Slider { Minimum = 0, Maximum = 100, Value = 50, Width = 150 });
                    _dynamicPanel.Children.Add(inputPanel);
                }

                if (changeInfo.TotalInputConnections == 0)
                {
                    _dynamicPanel.Children.Add(new Label 
                    { 
                        Content = "没有连接的输入", 
                        Foreground = Brushes.Orange,
                        FontStyle = FontStyles.Italic 
                    });
                }
            });

            return true; // 增量更新成功
        }

        return false; // 需要重建整个UI
    }

    public override FrameworkElement CreateParameterControl()
    {
        var mainPanel = new StackPanel { Margin = new Thickness(5) };

        // 标题
        mainPanel.Children.Add(new Label 
        { 
            Content = "动态UI测试面板", 
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 10)
        });

        // 基础参数
        var basePanel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        basePanel.Children.Add(new Label { Content = "基础参数:" });
        var baseSlider = new Slider { Minimum = 0, Maximum = 100, Value = BaseValue };
        baseSlider.ValueChanged += (s, e) => BaseValue = e.NewValue;
        basePanel.Children.Add(baseSlider);
        mainPanel.Children.Add(basePanel);

        // 连接状态
        _statusLabel = new Label 
        { 
            Content = $"当前连接数: {_connectionCount}",
            Foreground = _connectionCount > 0 ? Brushes.Green : Brushes.Gray,
            FontWeight = FontWeights.Bold
        };
        mainPanel.Children.Add(_statusLabel);

        // 动态控件区域
        _dynamicPanel = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        mainPanel.Children.Add(_dynamicPanel);

        // 初始化动态控件
        if (_connectionCount == 0)
        {
            _dynamicPanel.Children.Add(new Label 
            { 
                Content = "没有连接的输入", 
                Foreground = Brushes.Orange,
                FontStyle = FontStyles.Italic 
            });
        }

        return mainPanel;
    }

    public override IScriptViewModel CreateViewModel()
    {
        return new TestViewModel(this);
    }

    public override async Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
    {
        await Task.CompletedTask;
    }

    public override Dictionary<string, object> SerializeParameters()
    {
        return new Dictionary<string, object>
        {
            [nameof(BaseValue)] = BaseValue
        };
    }

    public override void DeserializeParameters(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(BaseValue), out var value))
            BaseValue = Convert.ToDouble(value);
    }

    private class TestViewModel : ScriptViewModelBase
    {
        private DynamicUITestScript Script => (DynamicUITestScript)base.Script;

        public TestViewModel(DynamicUITestScript script) : base(script) { }

        public double BaseValue
        {
            get => Script.BaseValue;
            set
            {
                if (Math.Abs(Script.BaseValue - value) > 0.001)
                {
                    Script.BaseValue = value;
                    OnPropertyChanged();
                }
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
            return new Dictionary<string, object> { [nameof(BaseValue)] = BaseValue };
        }

        public override async Task SetParameterDataAsync(Dictionary<string, object> data)
        {
            if (data.TryGetValue(nameof(BaseValue), out var value))
                BaseValue = Convert.ToDouble(value);
            await Task.CompletedTask;
        }

        public override async Task ResetToDefaultAsync()
        {
            BaseValue = 50;
            await Task.CompletedTask;
        }
    }
}
