using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Tunnel_Next.Services.Scripting;
using OpenCvSharp;

[RevivalScript(
    Name = "动态连接响应UI",
    Author = "示例",
    Description = "根据连接数量动态变化GUI的示例脚本，无需重新加载",
    Version = "1.0",
    Category = "示例",
    Color = "#FF6B35"
)]
public class DynamicConnectionUIScript : DynamicUIRevivalScriptBase
{
    // 参数
    [ScriptParameter(DisplayName = "基础强度", Description = "始终显示的基础参数", Order = 0)]
    public double BaseIntensity { get; set; } = 50;

    [ScriptParameter(DisplayName = "混合模式", Description = "多输入时的混合模式", Order = 1)]
    public string BlendMode { get; set; } = "Normal";

    // 内部状态
    private int _currentInputCount = 0;
    private StackPanel? _mainPanel;
    private StackPanel? _dynamicPanel;
    private Label? _connectionStatusLabel;

    public override Dictionary<string, PortDefinition> GetInputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            ["input1"] = new PortDefinition("f32bmp", false, "主输入"),
            ["input2"] = new PortDefinition("f32bmp", false, "可选输入2"),
            ["input3"] = new PortDefinition("f32bmp", false, "可选输入3"),
            ["mask"] = new PortDefinition("f32bmp", false, "遮罩输入")
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
        // 处理逻辑
        if (!inputs.TryGetValue("input1", out var input1) || !(input1 is Mat mainInput) || mainInput.Empty())
        {
            return new Dictionary<string, object> { ["f32bmp"] = new Mat() };
        }

        Mat result = mainInput.Clone();

        // 根据连接数量执行不同处理
        var connectedInputs = inputs.Where(kvp => kvp.Value != null && kvp.Value is Mat mat && !mat.Empty()).ToList();
        if (connectedInputs.Count > 1)
        {
            // 多输入混合处理
            foreach (var kvp in connectedInputs.Skip(1))
            {
                if (kvp.Value is Mat additionalInput && !additionalInput.Empty())
                {
                    // 简单的混合处理
                    Cv2.AddWeighted(result, 0.7, additionalInput, 0.3, 0, result);
                }
            }
        }

        // 应用基础强度
        result.ConvertTo(result, -1, BaseIntensity / 100.0, 0);

        return new Dictionary<string, object> { ["f32bmp"] = result };
    }

    public override void OnConnectionChanged(ScriptConnectionInfo connectionInfo)
    {
        // 更新连接计数
        _currentInputCount = connectionInfo.TotalInputConnections;
        
        // 调用基类方法请求UI更新
        base.OnConnectionChanged(connectionInfo);
    }

    public override string? GetUIUpdateToken()
    {
        // 返回基于连接数量的UI标识符
        return $"inputs:{_currentInputCount}";
    }

    public override bool TryUpdateUI(FrameworkElement existingControl, ScriptConnectionInfo changeInfo)
    {
        // 尝试增量更新UI
        if (existingControl is StackPanel mainPanel && _dynamicPanel != null && _connectionStatusLabel != null)
        {
            // 更新连接状态标签
            Application.Current?.Dispatcher.Invoke(() =>
            {
                _connectionStatusLabel.Content = $"连接状态: {changeInfo.TotalInputConnections} 个输入已连接";
                _connectionStatusLabel.Foreground = changeInfo.TotalInputConnections > 0 ? Brushes.Green : Brushes.Gray;

                // 重建动态面板内容
                _dynamicPanel.Children.Clear();
                BuildDynamicContent(_dynamicPanel, changeInfo.TotalInputConnections);
            });

            return true; // 增量更新成功
        }

        return false; // 需要重建整个UI
    }

    public override FrameworkElement CreateParameterControl()
    {
        _mainPanel = new StackPanel { Margin = new Thickness(5) };

        // 加载样式资源
        var resources = LoadStyleResources();
        if (resources.Contains("MainPanelStyle")) 
            _mainPanel.Style = resources["MainPanelStyle"] as Style;

        // 标题
        var titleLabel = new Label { Content = "动态连接响应UI" };
        if (resources.Contains("TitleLabelStyle")) 
            titleLabel.Style = resources["TitleLabelStyle"] as Style;
        _mainPanel.Children.Add(titleLabel);

        // 连接状态显示
        _connectionStatusLabel = new Label 
        { 
            Content = "连接状态: 未检测到连接",
            Foreground = Brushes.Gray,
            FontSize = 10
        };
        _mainPanel.Children.Add(_connectionStatusLabel);

        // 基础参数（始终显示）
        var baseIntensitySlider = CreateSliderControl("基础强度", nameof(BaseIntensity), 0, 100, resources);
        _mainPanel.Children.Add(baseIntensitySlider);

        // 动态参数面板
        _dynamicPanel = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        _mainPanel.Children.Add(_dynamicPanel);

        // 初始化动态UI
        BuildDynamicContent(_dynamicPanel, _currentInputCount);

        return _mainPanel;
    }

    private void BuildDynamicContent(StackPanel dynamicPanel, int connectionCount)
    {
        var resources = LoadStyleResources();

        // 根据连接数量显示不同的控件
        if (connectionCount == 0)
        {
            var noConnectionLabel = new Label 
            { 
                Content = "请连接至少一个输入",
                Foreground = Brushes.Orange,
                FontStyle = FontStyles.Italic
            };
            dynamicPanel.Children.Add(noConnectionLabel);
        }
        else if (connectionCount == 1)
        {
            var singleInputLabel = new Label { Content = "单输入模式" };
            if (resources.Contains("DefaultLabelStyle")) 
                singleInputLabel.Style = resources["DefaultLabelStyle"] as Style;
            dynamicPanel.Children.Add(singleInputLabel);

            // 单输入特有的参数
            var contrastSlider = CreateSliderControl("对比度增强", "ContrastBoost", 0, 200, resources);
            dynamicPanel.Children.Add(contrastSlider);
        }
        else
        {
            var multiInputLabel = new Label { Content = $"多输入混合模式 ({connectionCount} 输入)" };
            if (resources.Contains("DefaultLabelStyle")) 
                multiInputLabel.Style = resources["DefaultLabelStyle"] as Style;
            dynamicPanel.Children.Add(multiInputLabel);

            // 多输入特有的参数
            var blendModeCombo = new ComboBox();
            blendModeCombo.Items.Add("Normal");
            blendModeCombo.Items.Add("Multiply");
            blendModeCombo.Items.Add("Screen");
            blendModeCombo.Items.Add("Overlay");
            blendModeCombo.SelectedItem = BlendMode;
            blendModeCombo.SelectionChanged += (s, e) => 
            {
                BlendMode = blendModeCombo.SelectedItem?.ToString() ?? "Normal";
            };

            var blendLabel = new Label { Content = "混合模式:" };
            if (resources.Contains("DefaultLabelStyle")) 
                blendLabel.Style = resources["DefaultLabelStyle"] as Style;

            dynamicPanel.Children.Add(blendLabel);
            dynamicPanel.Children.Add(blendModeCombo);

            // 为每个额外输入添加权重控制
            for (int i = 2; i <= connectionCount; i++)
            {
                var weightSlider = CreateSliderControl($"输入{i}权重", $"Input{i}Weight", 0, 100, resources);
                dynamicPanel.Children.Add(weightSlider);
            }
        }
    }

    private FrameworkElement CreateSliderControl(string label, string propertyName, double min, double max, ResourceDictionary resources)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 5, 0, 0) };

        var labelControl = new Label { Content = label };
        if (resources.Contains("DefaultLabelStyle")) 
            labelControl.Style = resources["DefaultLabelStyle"] as Style;
        
        var slider = new Slider { Minimum = min, Maximum = max, Value = 50 };
        if (resources.Contains("DefaultSliderStyle")) 
            slider.Style = resources["DefaultSliderStyle"] as Style;
        
        panel.Children.Add(labelControl);
        panel.Children.Add(slider);
        
        return panel;
    }

    private ResourceDictionary LoadStyleResources()
    {
        var resources = new ResourceDictionary();
        var resourcePaths = new[]
        {
            "/Tunnel-Next;component/Resources/ScriptsControls/SharedBrushes.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/LabelStyles.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/SliderStyles.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/ScriptButtonStyles.xaml"
        };
        
        foreach (var path in resourcePaths)
        {
            try 
            { 
                resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(path, UriKind.Relative) }); 
            }
            catch { /* 静默处理 */ }
        }
        
        return resources;
    }

    public override IScriptViewModel CreateViewModel()
    {
        return new DynamicUIViewModel(this);
    }

    public override async Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
    {
        await Task.CompletedTask;
    }

    public override Dictionary<string, object> SerializeParameters()
    {
        return new Dictionary<string, object>
        {
            [nameof(BaseIntensity)] = BaseIntensity,
            [nameof(BlendMode)] = BlendMode
        };
    }

    public override void DeserializeParameters(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(BaseIntensity), out var intensity))
            BaseIntensity = Convert.ToDouble(intensity);
        if (data.TryGetValue(nameof(BlendMode), out var mode))
            BlendMode = mode?.ToString() ?? "Normal";
    }
}

public class DynamicUIViewModel : ScriptViewModelBase
{
    private DynamicConnectionUIScript Script => (DynamicConnectionUIScript)base.Script;

    public double BaseIntensity
    {
        get => Script.BaseIntensity;
        set
        {
            if (Math.Abs(Script.BaseIntensity - value) > 0.001)
            {
                Script.BaseIntensity = value;
                OnPropertyChanged();
            }
        }
    }

    public DynamicUIViewModel(DynamicConnectionUIScript script) : base(script) { }

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
            [nameof(BaseIntensity)] = BaseIntensity
        };
    }

    public override async Task SetParameterDataAsync(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(BaseIntensity), out var intensity))
            BaseIntensity = Convert.ToDouble(intensity);
        await Task.CompletedTask;
    }

    public override async Task ResetToDefaultAsync()
    {
        BaseIntensity = 50;
        await Task.CompletedTask;
    }
}
