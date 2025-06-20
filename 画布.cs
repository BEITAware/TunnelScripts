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
    Name = "画布",
    Author = "BEITAware",
    Description = "创建指定大小的空白透明画布",
    Version = "1.0",
    Category = "生成器",
    Color = "#2E8B57"
)]
public class CanvasScript : RevivalScriptBase
{
    [ScriptParameter(DisplayName = "宽度", Description = "画布宽度（像素）", Order = 0)]
    public int Width { get; set; } = 512;

    [ScriptParameter(DisplayName = "高度", Description = "画布高度（像素）", Order = 1)]
    public int Height { get; set; } = 512;

    public string NodeInstanceId { get; set; } = string.Empty;

    public override Dictionary<string, PortDefinition> GetInputPorts()
    {
        // 画布节点不需要输入端口
        return new Dictionary<string, PortDefinition>();
    }

    public override Dictionary<string, PortDefinition> GetOutputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            ["f32bmp"] = new PortDefinition("f32bmp", false, "输出画布")
        };
    }

    public override Dictionary<string, object> Process(Dictionary<string, object> inputs, IScriptContext context)
    {
        try
        {
            // 确保宽高至少为1像素
            int canvasWidth = Math.Max(1, Width);
            int canvasHeight = Math.Max(1, Height);

            // 创建一个空白且透明的RGBA图像
            // RGBA格式，R,G,B,A四个通道，值范围[0.0, 1.0]
            Mat canvas = Mat.Zeros(canvasHeight, canvasWidth, MatType.CV_32FC4);

            // Alpha通道设置为0（完全透明）
            // RGB已经是0，表示黑色
            // 由于Mat.Zeros已经初始化为0，所以不需要额外设置

            return new Dictionary<string, object> { ["f32bmp"] = canvas };
        }
        catch (Exception ex)
        {
            throw new ApplicationException($"画布节点创建失败: {ex.Message}", ex);
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
            "/Tunnel-Next;component/Resources/ScriptsControls/ScriptButtonStyles.xaml"
        };
        foreach (var path in resourcePaths)
        {
            try { resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(path, UriKind.Relative) }); }
            catch { /* 静默处理 */ }
        }

        if (resources.Contains("MainPanelStyle")) mainPanel.Style = resources["MainPanelStyle"] as Style;

        // ViewModel
        var viewModel = CreateViewModel() as CanvasViewModel;
        mainPanel.DataContext = viewModel;

        // 标题
        var titleLabel = new Label { Content = "画布设置" };
        if (resources.Contains("TitleLabelStyle")) titleLabel.Style = resources["TitleLabelStyle"] as Style;
        mainPanel.Children.Add(titleLabel);

        // 宽度和高度设置
        mainPanel.Children.Add(CreateNumberControl("宽度", nameof(Width), viewModel, resources));
        mainPanel.Children.Add(CreateNumberControl("高度", nameof(Height), viewModel, resources));

        // 预设按钮
        var presetPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 5) };
        var presetLabel = new Label { Content = "预设尺寸:" };
        if (resources.Contains("DefaultLabelStyle")) presetLabel.Style = resources["DefaultLabelStyle"] as Style;
        presetPanel.Children.Add(presetLabel);

        var presets = new[]
        {
            new { Name = "512²", W = 512, H = 512 }, new { Name = "1024²", W = 1024, H = 1024 },
            new { Name = "1920×1080", W = 1920, H = 1080 }, new { Name = "2K", W = 2048, H = 2048 }
        };

        foreach (var preset in presets)
        {
            var button = new Button { Content = preset.Name, Margin = new Thickness(2) };
            if (resources.Contains("SelectFileScriptButtonStyle")) button.Style = resources["SelectFileScriptButtonStyle"] as Style;
            button.Click += (s, e) => { viewModel.Width = preset.W; viewModel.Height = preset.H; };
            presetPanel.Children.Add(button);
        }
        mainPanel.Children.Add(presetPanel);

        // 信息显示
        var infoLabel = new Label { Margin = new Thickness(0, 5, 0, 0) };
        if(resources.Contains("DefaultLabelStyle")) infoLabel.Style = resources["DefaultLabelStyle"] as Style;
        var infoBinding = new MultiBinding { StringFormat = "画布尺寸: {0} × {1} 像素" };
        infoBinding.Bindings.Add(new Binding(nameof(Width)) { Source = viewModel });
        infoBinding.Bindings.Add(new Binding(nameof(Height)) { Source = viewModel });
        infoLabel.SetBinding(Label.ContentProperty, infoBinding);
        mainPanel.Children.Add(infoLabel);

        return mainPanel;
    }

    private StackPanel CreateNumberControl(string label, string propertyName, CanvasViewModel viewModel, ResourceDictionary resources)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 5, 0, 5) };
        
        var labelControl = new Label { Content = label + ":" };
        if (resources.Contains("DefaultLabelStyle")) labelControl.Style = resources["DefaultLabelStyle"] as Style;
        panel.Children.Add(labelControl);
        
        var textBox = new TextBox { Margin = new Thickness(0, 2, 0, 2) };
        if (resources.Contains("DefaultTextBoxStyle")) textBox.Style = resources["DefaultTextBoxStyle"] as Style;
        textBox.SetBinding(TextBox.TextProperty, new Binding(propertyName) { Source = viewModel, Mode = BindingMode.TwoWay });
        panel.Children.Add(textBox);

        return panel;
    }

    public override IScriptViewModel CreateViewModel()
    {
        return new CanvasViewModel(this);
    }

    public override async Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
    {
        await Task.CompletedTask;
    }

    public override Dictionary<string, object> SerializeParameters()
    {
        return new Dictionary<string, object>
        {
            [nameof(Width)] = Width,
            [nameof(Height)] = Height,
            ["NodeInstanceId"] = NodeInstanceId
        };
    }

    public override void DeserializeParameters(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(Width), out var width))
            Width = Convert.ToInt32(width);
        if (data.TryGetValue(nameof(Height), out var height))
            Height = Convert.ToInt32(height);
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

public class CanvasViewModel : ScriptViewModelBase
{
    private CanvasScript CanvasScript => (CanvasScript)Script;

    public int Width
    {
        get => CanvasScript.Width;
        set
        {
            if (CanvasScript.Width != value)
            {
                CanvasScript.Width = Math.Max(1, value);
                OnPropertyChanged();
                NotifyParameterChanged(nameof(Width), CanvasScript.Width);
            }
        }
    }

    public int Height
    {
        get => CanvasScript.Height;
        set
        {
            if (CanvasScript.Height != value)
            {
                CanvasScript.Height = Math.Max(1, value);
                OnPropertyChanged();
                NotifyParameterChanged(nameof(Height), CanvasScript.Height);
            }
        }
    }

    public CanvasViewModel(CanvasScript script) : base(script)
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
        if (parameterName == nameof(Width) || parameterName == nameof(Height))
        {
            if (value is int intValue && intValue >= 1 && intValue <= 8192)
            {
                return new ScriptValidationResult(true);
            }
            return new ScriptValidationResult(false, "尺寸必须在1到8192之间");
        }
        return new ScriptValidationResult(true);
    }

    public override Dictionary<string, object> GetParameterData()
    {
        return new Dictionary<string, object>
        {
            [nameof(Width)] = Width,
            [nameof(Height)] = Height
        };
    }

    public override async Task SetParameterDataAsync(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(Width), out var width))
            Width = Convert.ToInt32(width);
        if (data.TryGetValue(nameof(Height), out var height))
            Height = Convert.ToInt32(height);
        await Task.CompletedTask;
    }

    public override async Task ResetToDefaultAsync()
    {
        Width = 512;
        Height = 512;
        await Task.CompletedTask;
    }

    public override void Dispose()
    {
        base.Dispose();
    }
}
