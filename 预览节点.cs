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
    Name = "预览节点",
    Author = "Revival Scripts",
    Description = "将图像发送到主程序的预览系统",
    Version = "1.0",
    Category = "输入输出",
    Color = "#9B59B6"
)]
public class PreviewNodeScript : RevivalScriptBase
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

        try
        {
            // 如果启用预览，图像会被主程序的预览系统自动显示
            // 因为主程序会查找名为"预览节点"的节点并显示其f32bmp输出

            if (EnablePreview)
            {
                // 在访问Width/Height前再次确认Mat有效
                if (!inputMat.IsDisposed && !inputMat.Empty())
                {
                }
                else
                {
                }

                // 记录处理前的图像信息
                var beforeClone = DateTime.Now;

                // 返回输入图像的克隆（不添加信息叠加）
                // 克隆很重要，这样预览节点不会意外释放上游节点可能仍在使用的Mat
                var outputMat = inputMat.Clone();

                // 记录处理后的图像信息
                var afterClone = DateTime.Now;

                // 确保输出图像有效
                if (outputMat == null || outputMat.Empty())
                {
                    // 紧急措施 - 再次尝试克隆
                    if (!inputMat.IsDisposed && !inputMat.Empty())
                    {
                        outputMat = inputMat.Clone();
                    }
                }

                // 可选添加图像信息叠加
                if (ShowInfo && outputMat != null && !outputMat.Empty())
                {
                    AddImageInfo(outputMat);
                }

                return new Dictionary<string, object>
                {
                    ["f32bmp"] = outputMat
                };
            }
            else
            {
                // 如果禁用预览，直接传递图像的克隆

                // 即使禁用预览，我们也需要克隆，以避免内存管理问题
                var disabledOutput = inputMat.Clone();

                return new Dictionary<string, object>
                {
                    ["f32bmp"] = disabledOutput
                };
            }
        }
        catch (Exception ex)
        {
            // 发生异常时尝试返回原始图像的安全副本
            return new Dictionary<string, object>
            {
                ["f32bmp"] = inputMat?.Clone() ?? new Mat()
            };
        }
    }

    private void AddImageInfo(Mat image)
    {
        try
        {
            // 在访问任何Mat属性之前，进行严格检查
            if (image == null || image.IsDisposed || image.Empty())
            {
                return;
            }

            // 准备信息文本 - 增强RGBA支持
            var channels = image.Channels();
            var channelInfo = channels switch
            {
                1 => "Gray",
                3 => "RGB",
                4 => "RGBA",
                _ => $"{channels}Ch"
            };

            var info = $"Size: {image.Width}x{image.Height}, {channelInfo}, Type: {image.Type()}";

            // 如果是RGBA图像，显示Alpha通道信息
            if (channels == 4)
            {
                try
                {
                    // 分离Alpha通道并计算统计信息
                    var alphaChannels = new Mat[4];
                    Cv2.Split(image, out alphaChannels);
                    var alphaChannel = alphaChannels[3];

                    Cv2.MinMaxLoc(alphaChannel, out double minAlpha, out double maxAlpha);
                    info += $", Alpha: [{minAlpha:F2}-{maxAlpha:F2}]";

                    // 清理资源
                    foreach (var ch in alphaChannels) ch.Dispose();
                }
                catch
                {
                    info += ", Alpha: [Info N/A]";
                }
            }

            if (!string.IsNullOrEmpty(PreviewTitle))
            {
                info = $"{PreviewTitle} - {info}";
            }

            // 在图像左上角添加文本
            // 确保图像尺寸足够大以放置文本，避免异常
            if (image.Width < 50 || image.Height < 50) // 示例阈值，可调整
            {
                return;
            }

            var fontScale = Math.Max(0.5, Math.Min(image.Width, image.Height) / 1000.0);
            var thickness = Math.Max(1, (int)(fontScale * 2));

            // 添加黑色背景的白色文字
            Cv2.PutText(image, info, new OpenCvSharp.Point(10, 30),
                       HersheyFonts.HersheySimplex, fontScale,
                       Scalar.Black, thickness + 2); // 黑色边框
            Cv2.PutText(image, info, new OpenCvSharp.Point(10, 30),
                       HersheyFonts.HersheySimplex, fontScale,
                       Scalar.White, thickness); // 白色文字
        }
        catch (Exception ex)
        {
        }
    }

    public override Dictionary<string, object> InjectMetadata(Dictionary<string, object> currentMetadata)
    {
        // 向元数据注入预览相关信息
        var metadata = new Dictionary<string, object>(currentMetadata);

        metadata["PreviewEnabled"] = EnablePreview;
        metadata["PreviewTitle"] = PreviewTitle;
        metadata["AutoScale"] = AutoScale;
        metadata["ShowInfo"] = ShowInfo;
        metadata["IsPreviewNode"] = true; // 标记这是预览节点

        return metadata;
    }

    public override FrameworkElement CreateParameterControl()
    {
        var mainPanel = new StackPanel { Margin = new Thickness(5) };

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

        // 创建并设置ViewModel作为DataContext
        var viewModel = CreateViewModel() as PreviewNodeViewModel;
        mainPanel.DataContext = viewModel;

        // 标题
        var titleLabel = new Label
        {
            Content = "预览设置",
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial")
        };
        mainPanel.Children.Add(titleLabel);

        // 启用预览复选框
        var enableCheckBox = new CheckBox
        {
            Content = "启用预览",
            Margin = new Thickness(0, 5, 0, 10),
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11
        };

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
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11
        };
        mainPanel.Children.Add(titleTextLabel);

        var titleTextBox = new TextBox
        {
            Margin = new Thickness(0, 0, 0, 10),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F0F0F0")),
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#333333")),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A1F28")),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 4, 6, 4),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11
        };

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
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11
        };

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
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11
        };

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
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F0F8FF")),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A1F28")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Margin = new Thickness(0, 10, 0, 0),
            Padding = new Thickness(8)
        };

        var statusText = new TextBlock
        {
            Text = "💡 此节点将图像发送到主程序的预览窗口。\n" +
                   "主程序会自动查找名为'预览节点'的节点并显示其输出。",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#777777")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial")
        };

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

                // 确保使用RevivalScriptBase的OnParameterChanged通知主程序
                if (Script is RevivalScriptBase rsb)
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

                // 确保使用RevivalScriptBase的OnParameterChanged通知主程序
                if (Script is RevivalScriptBase rsb)
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

                // 确保使用RevivalScriptBase的OnParameterChanged通知主程序
                if (Script is RevivalScriptBase rsb)
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

                // 确保使用RevivalScriptBase的OnParameterChanged通知主程序
                if (Script is RevivalScriptBase rsb)
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

                // 确保使用RevivalScriptBase的OnParameterChanged通知主程序
                if (Script is RevivalScriptBase rsb)
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
