using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Resources;
using Tunnel_Next.Services.Scripting;
using OpenCvSharp;

[TunnelExtensionScript(
    Name = "预览节点",
    Author = "BEITAware",
    Description = "将图像发送到主程序的预览系统",
    Version = "1.0",
    Category = "输入输出",
    Color = "#9B59B6"
)]
public class PreviewNodeScript : TunnelExtensionScriptBase
{
    [ScriptParameter(DisplayName = "启用预览", Description = "是否启用图像预览", Order = 0)]
    public bool EnablePreview { get; set; } = true;

    [ScriptParameter(DisplayName = "预览标题", Description = "预览窗口显示的标题", Order = 1)]
    public string PreviewTitle { get; set; } = "图像预览";

    [ScriptParameter(DisplayName = "自动缩放", Description = "自动调整图像大小以适应预览窗口", Order = 2)]
    public bool AutoScale { get; set; } = true;

    [ScriptParameter(DisplayName = "显示信息", Description = "在预览中显示图像信息", Order = 3)]
    public bool ShowInfo { get; set; } = false;

    [ScriptParameter(DisplayName = "缩放比例")]
    public double ZoomLevel { get; set; } = 1.0;

    public override Dictionary<string, PortDefinition> GetInputPorts()
    {
        // 预览节点需要一个图像输入端口
        return new Dictionary<string, PortDefinition>
        {
            ["f32bmp"] = new PortDefinition("f32bmp", false, "输入图像")
        };
    }

    public override Dictionary<string, PortDefinition> GetOutputPorts()
    {
        // 预览节点将输入图像直接传递到输出，以便其他节点可以继续处理
        return new Dictionary<string, PortDefinition>
        {
            ["f32bmp"] = new PortDefinition("f32bmp", false, "输出图像")
        };
    }

    public override Dictionary<string, object> Process(Dictionary<string, object> inputs, IScriptContext context)
    {
        // 记录方法开始执行

        // 尝试从输入中获取图像数据
        if (!inputs.TryGetValue("f32bmp", out var inputObj) || inputObj is not Mat inputMat)
        {
            // 如果没有有效输入图像，返回一个空的Mat对象或者不返回任何内容，取决于下游节点的期望
            return new Dictionary<string, object>
            {
                ["f32bmp"] = new Mat() // 返回空Mat以表示无有效图像
            };
        }

        // 对接收到的 inputMat 进行安全检查
        if (inputMat == null || inputMat.IsDisposed || inputMat.Empty())
        {
            return new Dictionary<string, object>
            {
                ["f32bmp"] = new Mat() // 返回空Mat
            };
        }

        // 缩放、标题与信息叠加由宿主预览窗口显示，其他节点继续接收原始像素。
        return new Dictionary<string, object> { ["f32bmp"] = inputMat.Clone() };
    }

    public override Dictionary<string, object> InjectMetadata(Dictionary<string, object> currentMetadata)
    {
        // 向元数据注入预览相关信息
        var metadata = new Dictionary<string, object>(currentMetadata);

        metadata["PreviewEnabled"] = EnablePreview;
        metadata["PreviewTitle"] = PreviewTitle;
        metadata["AutoScale"] = AutoScale;
        metadata["ZoomLevel"] = ZoomLevel;
        metadata["ShowInfo"] = ShowInfo;
        metadata["IsPreviewNode"] = true; // 标记这是预览节点

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
            "/Tunnel-Next;component/Resources/ScriptsControls/CheckBoxStyles.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/TextBoxIdleStyles.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/TextBlockStyles.xaml"
        };

        foreach (var path in resourcePaths)
        {
            try
            {
                resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(path, UriKind.Relative) });
            }
            catch (Exception)
            {
                // 如果资源加载失败，可以记录日志，但这里我们选择静默处理
            }
        }
        
        // 使用资源字典中的Layer_2画刷
        if (resources.Contains("Layer_2"))
        {
            mainPanel.Background = resources["Layer_2"] as Brush;
        }
        else
        {
            // 回退到默认样式
            mainPanel.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF1A1F28"));
        }

        // 创建并设置ViewModel作为DataContext
        var viewModel = CreateViewModel() as PreviewNodeViewModel;
        mainPanel.DataContext = viewModel;

        // 标题
        var titleLabel = new Label
        {
            Content = "预览设置",
        };
        if (resources.Contains("TitleLabelStyle"))
        {
            titleLabel.Style = resources["TitleLabelStyle"] as Style;
        }
        mainPanel.Children.Add(titleLabel);

        // 启用预览复选框
        var enableCheckBox = new CheckBox
        {
            Content = "启用预览",
            Margin = new Thickness(0, 5, 0, 10),
        };
        if (resources.Contains("DefaultCheckBoxStyle"))
        {
            enableCheckBox.Style = resources["DefaultCheckBoxStyle"] as Style;
        }

        // 使用数据绑定将CheckBox的IsChecked绑定到ViewModel的EnablePreview属性
        var enableBinding = new System.Windows.Data.Binding("EnablePreview")
        {
            Source = viewModel,
            Mode = System.Windows.Data.BindingMode.TwoWay,
            UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
        };
        enableCheckBox.SetBinding(CheckBox.IsCheckedProperty, enableBinding);

        mainPanel.Children.Add(enableCheckBox);

        // 预览标题设置
        var titleTextLabel = new Label
        {
            Content = "预览标题:",
        };
        if (resources.Contains("DefaultLabelStyle"))
        {
            titleTextLabel.Style = resources["DefaultLabelStyle"] as Style;
        }
        mainPanel.Children.Add(titleTextLabel);

        var titleTextBox = new TextBox
        {
            Margin = new Thickness(0, 0, 0, 10),
        };
        
        // 尝试应用主程序的TextBox资源
        if (resources.Contains("DefaultTextBoxStyle"))
        {
            titleTextBox.Style = resources["DefaultTextBoxStyle"] as Style;
        }
        else
        {
            // Fallback styles if resource fails to load
            titleTextBox.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF1A1F28"));
            titleTextBox.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF"));
            titleTextBox.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A1F28"));
            titleTextBox.BorderThickness = new Thickness(1);
            titleTextBox.Padding = new Thickness(6, 4, 6, 4);
        }

        // 使用数据绑定将TextBox的Text绑定到ViewModel的PreviewTitle属性
        var titleBinding = new System.Windows.Data.Binding("PreviewTitle")
        {
            Source = viewModel,
            Mode = System.Windows.Data.BindingMode.TwoWay,
            UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
        };
        titleTextBox.SetBinding(TextBox.TextProperty, titleBinding);

        mainPanel.Children.Add(titleTextBox);

        // 其他选项
        var optionsPanel = new StackPanel { Margin = new Thickness(0, 5, 0, 0) };

        var autoScaleCheckBox = new CheckBox
        {
            Content = "自动缩放",
            Margin = new Thickness(0, 0, 0, 5),
        };
        if (resources.Contains("DefaultCheckBoxStyle"))
        {
            autoScaleCheckBox.Style = resources["DefaultCheckBoxStyle"] as Style;
        }

        // 使用数据绑定将CheckBox的IsChecked绑定到ViewModel的AutoScale属性
        var autoScaleBinding = new System.Windows.Data.Binding("AutoScale")
        {
            Source = viewModel,
            Mode = System.Windows.Data.BindingMode.TwoWay,
            UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
        };
        autoScaleCheckBox.SetBinding(CheckBox.IsCheckedProperty, autoScaleBinding);

        var showInfoCheckBox = new CheckBox
        {
            Content = "显示图像信息",
            Margin = new Thickness(0, 0, 0, 5),
        };
        if (resources.Contains("DefaultCheckBoxStyle"))
        {
            showInfoCheckBox.Style = resources["DefaultCheckBoxStyle"] as Style;
        }

        // 使用数据绑定将CheckBox的IsChecked绑定到ViewModel的ShowInfo属性
        var showInfoBinding = new System.Windows.Data.Binding("ShowInfo")
        {
            Source = viewModel,
            Mode = System.Windows.Data.BindingMode.TwoWay,
            UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
        };
        showInfoCheckBox.SetBinding(CheckBox.IsCheckedProperty, showInfoBinding);

        optionsPanel.Children.Add(autoScaleCheckBox);
        optionsPanel.Children.Add(showInfoCheckBox);
        mainPanel.Children.Add(optionsPanel);

        // 状态信息
        var statusPanel = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Margin = new Thickness(0, 10, 0, 0),
            Padding = new Thickness(8)
        };
        
        // 尝试应用主程序的资源
        try
        {
            if (resources.Contains("Layer_2"))
            {
                statusPanel.Background = resources["Layer_2"] as Brush;
                statusPanel.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A1F28"));
            }
            else
            {
                statusPanel.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F0F8FF"));
                statusPanel.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A1F28"));
            }
        }
        catch
        {
            // 如果加载失败，使用默认样式
            statusPanel.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F0F8FF"));
            statusPanel.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A1F28"));
        }

        var statusText = new TextBlock
        {
            Text = "💡 此节点将图像发送到主程序的预览窗口。\n" +
                   "主程序会自动查找名为'预览节点'的节点并显示其输出。",
        };
        if (resources.Contains("StatusTextBlockStyle"))
        {
            statusText.Style = resources["StatusTextBlockStyle"] as Style;
        }

        statusPanel.Child = statusText;
        mainPanel.Children.Add(statusPanel);

        return mainPanel;
    }

    public override IScriptViewModel CreateViewModel()
    {
        return new PreviewNodeViewModel(this);
    }

    public override async Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
    {
        if (parameterName == nameof(EnablePreview))
        {
            if (bool.TryParse(newValue?.ToString(), out var enable))
                EnablePreview = enable;
        }
        else if (parameterName == nameof(PreviewTitle))
        {
            PreviewTitle = newValue?.ToString() ?? "图像预览";
        }
        else if (parameterName == nameof(AutoScale))
        {
            if (bool.TryParse(newValue?.ToString(), out var autoScale))
                AutoScale = autoScale;
        }
        else if (parameterName == nameof(ShowInfo))
        {
            if (bool.TryParse(newValue?.ToString(), out var showInfo))
                ShowInfo = showInfo;
        }
        else if (parameterName == nameof(ZoomLevel))
        {
            ZoomLevel = Convert.ToDouble(newValue);
        }
        await Task.CompletedTask;
    }

    /// <summary>
    /// 序列化参数
    /// </summary>
    /// <returns>参数字典</returns>
    public override Dictionary<string, object> SerializeParameters()
    {

        // 创建序列化数据字典
        var data = new Dictionary<string, object>
        {
            [nameof(EnablePreview)] = EnablePreview,
            [nameof(PreviewTitle)] = PreviewTitle,
            [nameof(AutoScale)] = AutoScale,
            [nameof(ShowInfo)] = ShowInfo,
            [nameof(ZoomLevel)] = ZoomLevel,
            // 可以添加其他需要保存的参数
        };

        foreach (var kvp in data)
        {
        }

        return data;
    }

    /// <summary>
    /// 反序列化参数
    /// </summary>
    /// <param name="data">参数字典</param>
    public override void DeserializeParameters(Dictionary<string, object> data)
    {

        foreach (var key in data.Keys)
        {
        }

        // 恢复参数值
        if (data.TryGetValue(nameof(EnablePreview), out var enablePreview))
        {
            if (enablePreview is bool boolValue)
            {
                EnablePreview = boolValue;
            }
            else if (bool.TryParse(enablePreview?.ToString(), out var parsedBool))
            {
                EnablePreview = parsedBool;
            }
        }

        if (data.TryGetValue(nameof(PreviewTitle), out var previewTitle))
        {
            PreviewTitle = previewTitle?.ToString() ?? "图像预览";
        }

        if (data.TryGetValue(nameof(AutoScale), out var autoScale))
        {
            if (autoScale is bool asBool)
            {
                AutoScale = asBool;
            }
            else if (bool.TryParse(autoScale?.ToString(), out var a))
            {
                AutoScale = a;
            }
        }

        if (data.TryGetValue(nameof(ShowInfo), out var showInfo))
        {
            if (showInfo is bool siBool)
            {
                ShowInfo = siBool;
            }
            else if (bool.TryParse(showInfo?.ToString(), out var s))
            {
                ShowInfo = s;
            }
        }

        if (data.TryGetValue(nameof(ZoomLevel), out var zoomLevel))
        {
            ZoomLevel = Convert.ToDouble(zoomLevel);
        }
    }
}

public class PreviewNodeViewModel : ScriptViewModelBase
{
    private PreviewNodeScript PreviewNodeScript => (PreviewNodeScript)Script;

    public bool EnablePreview
    {
        get => PreviewNodeScript.EnablePreview;
        set
        {
            if (PreviewNodeScript.EnablePreview != value)
            {
                var oldValue = PreviewNodeScript.EnablePreview;
                PreviewNodeScript.EnablePreview = value;
                OnPropertyChanged();

                // 确保使用TunnelExtensionScriptBase的OnParameterChanged通知主程序
                if (Script is TunnelExtensionScriptBase rsb)
                {
                    rsb.OnParameterChanged(nameof(EnablePreview), value);
                }
                else
                {
                    _ = OnParameterChangedAsync(nameof(EnablePreview), oldValue, value);
                }
            }
        }
    }

    public string PreviewTitle
    {
        get => PreviewNodeScript.PreviewTitle;
        set
        {
            if (PreviewNodeScript.PreviewTitle != value)
            {
                var oldValue = PreviewNodeScript.PreviewTitle;
                PreviewNodeScript.PreviewTitle = value;
                OnPropertyChanged();

                // 确保使用TunnelExtensionScriptBase的OnParameterChanged通知主程序
                if (Script is TunnelExtensionScriptBase rsb)
                {
                    rsb.OnParameterChanged(nameof(PreviewTitle), value);
                }
                else
                {
                    _ = OnParameterChangedAsync(nameof(PreviewTitle), oldValue, value);
                }
            }
        }
    }

    public bool AutoScale
    {
        get => PreviewNodeScript.AutoScale;
        set
        {
            if (PreviewNodeScript.AutoScale != value)
            {
                var oldValue = PreviewNodeScript.AutoScale;
                PreviewNodeScript.AutoScale = value;
                OnPropertyChanged();

                // 确保使用TunnelExtensionScriptBase的OnParameterChanged通知主程序
                if (Script is TunnelExtensionScriptBase rsb)
                {
                    rsb.OnParameterChanged(nameof(AutoScale), value);
                }
                else
                {
                    _ = OnParameterChangedAsync(nameof(AutoScale), oldValue, value);
                }
            }
        }
    }

    public bool ShowInfo
    {
        get => PreviewNodeScript.ShowInfo;
        set
        {
            if (PreviewNodeScript.ShowInfo != value)
            {
                var oldValue = PreviewNodeScript.ShowInfo;
                PreviewNodeScript.ShowInfo = value;
                OnPropertyChanged();

                // 确保使用TunnelExtensionScriptBase的OnParameterChanged通知主程序
                if (Script is TunnelExtensionScriptBase rsb)
                {
                    rsb.OnParameterChanged(nameof(ShowInfo), value);
                }
                else
                {
                    _ = OnParameterChangedAsync(nameof(ShowInfo), oldValue, value);
                }
            }
        }
    }

    public double ZoomLevel
    {
        get => PreviewNodeScript.ZoomLevel;
        set
        {
            if (PreviewNodeScript.ZoomLevel != value)
            {
                var oldValue = PreviewNodeScript.ZoomLevel;
                PreviewNodeScript.ZoomLevel = value;
                OnPropertyChanged();

                // 确保使用TunnelExtensionScriptBase的OnParameterChanged通知主程序
                if (Script is TunnelExtensionScriptBase rsb)
                {
                    rsb.OnParameterChanged(nameof(ZoomLevel), value);
                }
                else
                {
                    _ = OnParameterChangedAsync(nameof(ZoomLevel), oldValue, value);
                }
            }
        }
    }

    public PreviewNodeViewModel(PreviewNodeScript script) : base(script) { }

    public override async Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
    {
        await Task.CompletedTask;
    }

    public override ScriptValidationResult ValidateParameter(string parameterName, object value)
    {
        // 所有参数都是有效的
        return new ScriptValidationResult(true);
    }

    public override Dictionary<string, object> GetParameterData()
    {
        return new Dictionary<string, object>
        {
            [nameof(EnablePreview)] = EnablePreview,
            [nameof(PreviewTitle)] = PreviewTitle,
            [nameof(AutoScale)] = AutoScale,
            [nameof(ShowInfo)] = ShowInfo,
            [nameof(ZoomLevel)] = ZoomLevel
        };
    }

    public override async Task SetParameterDataAsync(Dictionary<string, object> data)
    {
        await RunOnUIThreadAsync(() =>
        {
            if (data.TryGetValue(nameof(EnablePreview), out var enable) && bool.TryParse(enable?.ToString(), out var e))
                EnablePreview = e;

            if (data.TryGetValue(nameof(PreviewTitle), out var title))
                PreviewTitle = title?.ToString() ?? "图像预览";

            if (data.TryGetValue(nameof(AutoScale), out var autoScale) && bool.TryParse(autoScale?.ToString(), out var a))
                AutoScale = a;

            if (data.TryGetValue(nameof(ShowInfo), out var showInfo) && bool.TryParse(showInfo?.ToString(), out var s))
                ShowInfo = s;

            if (data.TryGetValue(nameof(ZoomLevel), out var zoomLevel) && double.TryParse(zoomLevel?.ToString(), out var z))
                ZoomLevel = z;
        });
    }

    public override async Task ResetToDefaultAsync()
    {
        await RunOnUIThreadAsync(() =>
        {
            EnablePreview = true;
            PreviewTitle = "图像预览";
            AutoScale = true;
            ShowInfo = false;
            ZoomLevel = 1.0;
        });
    }
}
