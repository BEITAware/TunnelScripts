using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Tunnel_Next.Services.Scripting;
using OpenCvSharp;

[TunnelExtensionScript(
    Name = "动态连接响应UI",
    Author = "示例",
    Description = "根据连接数量动态变化GUI的示例脚本，无需重新加载",
    Version = "1.0",
    Category = "示例",
    Color = "#FF6B35"
)]
public class DynamicConnectionUIScript : DynamicUITunnelExtensionScriptBase
{
    private double _baseIntensity = 50;
    private string _blendMode = "Normal";
    private double _contrastBoost = 100;
    private double _input2Weight = 30;
    private double _input3Weight = 30;
    private bool _loadingParameters;

    // 参数
    [ScriptParameter(DisplayName = "基础强度", Description = "始终显示的基础参数", Order = 0)]
    public double BaseIntensity { get => _baseIntensity; set => SetOption(ref _baseIntensity, value); }

    [ScriptParameter(DisplayName = "混合模式", Description = "多输入时的混合模式", Order = 1)]
    public string BlendMode { get => _blendMode; set => SetOption(ref _blendMode, value); }

    [ScriptParameter(DisplayName = "对比度增强", Order = 2)]
    public double ContrastBoost { get => _contrastBoost; set => SetOption(ref _contrastBoost, value); }

    [ScriptParameter(DisplayName = "输入2权重", Order = 3)]
    public double Input2Weight { get => _input2Weight; set => SetOption(ref _input2Weight, value); }

    [ScriptParameter(DisplayName = "输入3权重", Order = 4)]
    public double Input3Weight { get => _input3Weight; set => SetOption(ref _input3Weight, value); }

    private void SetOption<T>(ref T field, T value, [CallerMemberName] string name = "")
    {
        if (SetProperty(ref field, value, name) && !_loadingParameters)
            OnParameterChanged(name, value);
    }

    // 内部状态
    private string[] _connectedPorts = Array.Empty<string>();
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

        var size = mainInput.Size();
        using var primary = ToFloatBgra(mainInput, size);
        using var original = primary.CvtColor(ColorConversionCodes.BGRA2BGR);
        using var result = original.Clone();
        bool hasAdditionalInput = false;
        foreach (var port in new[] { "input2", "input3" })
        {
            if (!inputs.TryGetValue(port, out var value) || value is not Mat extra || extra.Empty()) continue;
            hasAdditionalInput = true;
            using var bgra = ToFloatBgra(extra, size);
            using var rgb = bgra.CvtColor(ColorConversionCodes.BGRA2BGR);
            using var blended = new Mat();
            Blend(result, rgb, BlendMode, blended);
            double weight = Math.Clamp(port == "input2" ? Input2Weight : Input3Weight, 0, 100) / 100;
            Cv2.AddWeighted(result, 1 - weight, blended, weight, 0, result);
        }

        if (!hasAdditionalInput)
        {
            double contrast = Math.Clamp(ContrastBoost, 0, 200) / 100;
            result.ConvertTo(result, -1, contrast, 0.5 * (1 - contrast));
        }
        result.ConvertTo(result, -1, Math.Clamp(BaseIntensity, 0, 100) / 100);

        if (inputs.TryGetValue("mask", out var maskValue) && maskValue is Mat mask && !mask.Empty())
        {
            using var bgra = ToFloatBgra(mask, size);
            using var coverage = bgra.CvtColor(ColorConversionCodes.BGRA2GRAY);
            Cv2.Threshold(coverage, coverage, 0, 0, ThresholdTypes.Tozero);
            Cv2.Threshold(coverage, coverage, 1, 1, ThresholdTypes.Trunc);
            using var weights = coverage.CvtColor(ColorConversionCodes.GRAY2BGR);
            Cv2.Subtract(result, original, result);
            Cv2.Multiply(result, weights, result);
            Cv2.Add(result, original, result);
        }

        // Process color only; the primary image supplies the output alpha channel.
        var output = result.CvtColor(ColorConversionCodes.BGR2BGRA);
        using var alpha = primary.ExtractChannel(3);
        Cv2.InsertChannel(alpha, output, 3);
        return new Dictionary<string, object> { ["f32bmp"] = output };
    }

    private static Mat ToFloatBgra(Mat image, OpenCvSharp.Size size)
    {
        using var normalized = new Mat();
        double scale = image.Depth() == MatType.CV_8U ? 1.0 / 255 :
                       image.Depth() == MatType.CV_16U ? 1.0 / 65535 : 1;
        image.ConvertTo(normalized, MatType.CV_32F, scale);
        if (normalized.Size() != size) Cv2.Resize(normalized, normalized, size);
        return normalized.Channels() switch
        {
            1 => normalized.CvtColor(ColorConversionCodes.GRAY2BGRA),
            3 => normalized.CvtColor(ColorConversionCodes.BGR2BGRA),
            4 => normalized.Clone(),
            _ => throw new ArgumentException("输入图像须为灰度、BGR 或 BGRA")
        };
    }

    private static void Blend(Mat current, Mat extra, string mode, Mat output)
    {
        switch (mode)
        {
            case "Multiply":
                Cv2.Multiply(current, extra, output);
                break;
            case "Screen":
                using (var product = new Mat())
                {
                    Cv2.Multiply(current, extra, product);
                    Cv2.Add(current, extra, output);
                    Cv2.Subtract(output, product, output);
                }
                break;
            case "Overlay":
                using (var high = new Mat())
                using (var inverse = new Mat())
                {
                    Cv2.Multiply(current, extra, output, 2);
                    current.ConvertTo(high, -1, 2, -1);
                    Cv2.Threshold(high, high, 0, 0, ThresholdTypes.Tozero);
                    extra.ConvertTo(inverse, -1, -2, 1);
                    Cv2.Multiply(high, inverse, high);
                    Cv2.Add(output, high, output);
                }
                break;
            default:
                extra.CopyTo(output);
                break;
        }
    }

    public override void OnConnectionChanged(ScriptConnectionInfo connectionInfo)
    {
        // 更新连接计数
        _connectedPorts = connectionInfo.InputConnections.Where(pair => pair.Value)
            .Select(pair => pair.Key).OrderBy(key => key, StringComparer.Ordinal).ToArray();
        
        // 调用基类方法请求UI更新
        base.OnConnectionChanged(connectionInfo);
    }

    public override string? GetUIUpdateToken()
    {
        // 返回基于连接数量的UI标识符
        return string.Join(",", _connectedPorts);
    }

    public override bool TryUpdateUI(FrameworkElement existingControl, ScriptConnectionInfo changeInfo)
    {
        // 尝试增量更新UI
        if (ReferenceEquals(existingControl, _mainPanel) && _dynamicPanel != null && _connectionStatusLabel != null)
        {
            // 更新连接状态标签
            Application.Current?.Dispatcher.Invoke(() =>
            {
                _connectionStatusLabel.Content = $"连接状态: {changeInfo.TotalInputConnections} 个输入已连接";
                _connectionStatusLabel.Foreground = changeInfo.TotalInputConnections > 0 ? Brushes.Green : Brushes.Gray;

                // 重建动态面板内容
                _dynamicPanel.Children.Clear();
                BuildDynamicContent(_dynamicPanel);
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
            Content = $"连接状态: {_connectedPorts.Length} 个输入已连接",
            Foreground = _connectedPorts.Length > 0 ? Brushes.Green : Brushes.Gray,
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
        BuildDynamicContent(_dynamicPanel);

        return _mainPanel;
    }

    private void BuildDynamicContent(StackPanel dynamicPanel)
    {
        var resources = LoadStyleResources();

        // 根据连接数量显示不同的控件
        var extraPorts = new[] { "input2", "input3" }.Where(port => _connectedPorts.Contains(port)).ToArray();
        if (!_connectedPorts.Contains("input1"))
        {
            var noConnectionLabel = new Label 
            { 
                Content = "请连接主输入",
                Foreground = Brushes.Orange,
                FontStyle = FontStyles.Italic
            };
            dynamicPanel.Children.Add(noConnectionLabel);
        }
        else if (extraPorts.Length == 0)
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
            var multiInputLabel = new Label { Content = $"多输入混合模式 ({extraPorts.Length + 1} 输入)" };
            if (resources.Contains("DefaultLabelStyle")) 
                multiInputLabel.Style = resources["DefaultLabelStyle"] as Style;
            dynamicPanel.Children.Add(multiInputLabel);

            // 多输入特有的参数
            var blendModeCombo = new ComboBox();
            blendModeCombo.Items.Add("Normal");
            blendModeCombo.Items.Add("Multiply");
            blendModeCombo.Items.Add("Screen");
            blendModeCombo.Items.Add("Overlay");
            blendModeCombo.SetBinding(ComboBox.SelectedItemProperty,
                new Binding(nameof(BlendMode)) { Source = this, Mode = BindingMode.TwoWay });

            var blendLabel = new Label { Content = "混合模式:" };
            if (resources.Contains("DefaultLabelStyle")) 
                blendLabel.Style = resources["DefaultLabelStyle"] as Style;

            dynamicPanel.Children.Add(blendLabel);
            dynamicPanel.Children.Add(blendModeCombo);

            // 为每个额外输入添加权重控制
            foreach (var port in extraPorts)
            {
                var name = port == "input2" ? nameof(Input2Weight) : nameof(Input3Weight);
                var weightSlider = CreateSliderControl($"输入{port.Last()}权重", name, 0, 100, resources);
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
        
        var slider = new Slider { Minimum = min, Maximum = max };
        slider.SetBinding(Slider.ValueProperty,
            new Binding(propertyName) { Source = this, Mode = BindingMode.TwoWay });
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
            [nameof(BlendMode)] = BlendMode,
            [nameof(ContrastBoost)] = ContrastBoost,
            [nameof(Input2Weight)] = Input2Weight,
            [nameof(Input3Weight)] = Input3Weight
        };
    }

    public override void DeserializeParameters(Dictionary<string, object> data)
    {
        _loadingParameters = true;
        try
        {
            if (data.TryGetValue(nameof(BaseIntensity), out var intensity)) BaseIntensity = Convert.ToDouble(intensity);
            if (data.TryGetValue(nameof(BlendMode), out var mode)) BlendMode = mode?.ToString() ?? "Normal";
            if (data.TryGetValue(nameof(ContrastBoost), out var contrast)) ContrastBoost = Convert.ToDouble(contrast);
            if (data.TryGetValue(nameof(Input2Weight), out var second)) Input2Weight = Convert.ToDouble(second);
            if (data.TryGetValue(nameof(Input3Weight), out var third)) Input3Weight = Convert.ToDouble(third);
        }
        finally { _loadingParameters = false; }
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

    public override Dictionary<string, object> GetParameterData() => Script.SerializeParameters();

    public override Task SetParameterDataAsync(Dictionary<string, object> data)
    {
        Script.DeserializeParameters(data);
        OnPropertyChanged(nameof(BaseIntensity));
        return Task.CompletedTask;
    }

    public override Task ResetToDefaultAsync()
    {
        BaseIntensity = 50;
        Script.ContrastBoost = 100;
        Script.Input2Weight = 30;
        Script.Input3Weight = 30;
        Script.BlendMode = "Normal";
        return Task.CompletedTask;
    }
}
