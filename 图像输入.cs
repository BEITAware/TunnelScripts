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
using Microsoft.Win32;

[RevivalScript(
    Name = "图像输入",
    Author = "Revival Scripts",
    Description = "从文件加载图像并向下游输出",
    Version = "1.0",
    Category = "输入输出",
    Color = "#4ECDC4"
)]
public class ImageInputScript : RevivalScriptBase
{
    [ScriptParameter(DisplayName = "图像路径", Description = "要加载的图像文件路径", Order = 0)]
    public string ImagePath { get; set; } = string.Empty;

    // 处理节点实例标识
    public string NodeInstanceId { get; set; } = string.Empty;

    public override Dictionary<string, PortDefinition> GetInputPorts()
    {
        // 图像输入节点不需要输入端口
        return new Dictionary<string, PortDefinition>();
    }

    public override Dictionary<string, PortDefinition> GetOutputPorts()
    {
        // 只需要一个图像输出端口
        return new Dictionary<string, PortDefinition>
        {
            ["f32bmp"] = new PortDefinition("f32bmp", false, "输出图像")
        };
    }

    public override Dictionary<string, object> Process(Dictionary<string, object> inputs, IScriptContext context)
    {

        if (string.IsNullOrEmpty(ImagePath) || !System.IO.File.Exists(ImagePath))
        {
            throw new ArgumentException("请选择有效的图像文件");
        }

        try
        {
            var startRead = DateTime.Now;

            // 首先尝试读取包含Alpha通道的图像
            using (var mat = Cv2.ImRead(ImagePath, ImreadModes.Unchanged))
            {
                var endRead = DateTime.Now;

                if (mat.Empty())
                {
                    throw new ArgumentException("无法加载图像文件");
                }


                // 转换为32位浮点RGBA格式 - RGBA是一等公民
                var startConversion = DateTime.Now;

                var outputMat = ConvertToRGBA32F(mat);

                var endConversion = DateTime.Now;

                // 确认输出图像有效
                if (outputMat.Empty())
                {

                    // 第二次尝试
                    outputMat = ConvertToRGBA32F(mat);

                    if (outputMat.Empty())
                    {
                        throw new InvalidOperationException("转换图像为RGBA浮点格式失败");
                    }
                }

                return new Dictionary<string, object>
                {
                    ["f32bmp"] = outputMat
                };
            }
        }
        catch (Exception ex)
        {
            throw new ApplicationException($"处理图像时发生错误: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 将任意通道数的图像转换为32位浮点RGBA格式
    /// RGBA是一等公民 - 所有图像都统一转换为RGBA处理
    /// </summary>
    private Mat ConvertToRGBA32F(Mat inputMat)
    {
        var channels = inputMat.Channels();
        var outputMat = new Mat();


        switch (channels)
        {
            case 1: // 灰度图像
                // 灰度 -> RGB -> RGBA
                var rgbMat = new Mat();
                Cv2.CvtColor(inputMat, rgbMat, ColorConversionCodes.GRAY2RGB);

                // RGB -> RGBA (添加Alpha通道)
                var rgbaMat = new Mat();
                Cv2.CvtColor(rgbMat, rgbaMat, ColorConversionCodes.RGB2RGBA);

                // 转换为32位浮点
                rgbaMat.ConvertTo(outputMat, MatType.CV_32FC4, 1.0 / 255.0);

                rgbMat.Dispose();
                rgbaMat.Dispose();
                break;

            case 3: // RGB图像
                // RGB -> RGBA (添加Alpha通道)
                var rgbaFromRgb = new Mat();
                Cv2.CvtColor(inputMat, rgbaFromRgb, ColorConversionCodes.RGB2RGBA);

                // 转换为32位浮点
                rgbaFromRgb.ConvertTo(outputMat, MatType.CV_32FC4, 1.0 / 255.0);

                rgbaFromRgb.Dispose();
                break;

            case 4: // RGBA图像
                // 直接转换为32位浮点
                inputMat.ConvertTo(outputMat, MatType.CV_32FC4, 1.0 / 255.0);
                break;

            default:
                throw new NotSupportedException($"不支持 {channels} 通道的图像");
        }

        return outputMat;
    }

    public override Dictionary<string, object> InjectMetadata(Dictionary<string, object> currentMetadata)
    {
        // 只注入图像源地址
        var metadata = new Dictionary<string, object>(currentMetadata);

        if (!string.IsNullOrEmpty(ImagePath))
        {
            metadata["图像源地址"] = ImagePath;
        }

        return metadata;
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
        var viewModel = CreateViewModel() as ImageInputViewModel;
        mainPanel.DataContext = viewModel;

        // 标题
        var titleLabel = new Label
        {
            Content = "图像文件选择",
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial")
        };
        mainPanel.Children.Add(titleLabel);

        // 当前路径显示
        var pathLabel = new Label
        {
            Content = "当前路径:",
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11
        };
        mainPanel.Children.Add(pathLabel);

        var pathTextBox = new TextBox
        {
            IsReadOnly = true,
            Background = textBoxIdleBrush,
            Foreground = new SolidColorBrush(Colors.White),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF000000")),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 10),
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 40,
            Padding = new Thickness(6, 4, 6, 4),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11
        };
        
        // 创建TextBox样式
        var textBoxStyle = new Style(typeof(TextBox));
        
        // 添加触发器：获得焦点时改变背景
        var focusedTrigger = new Trigger { Property = TextBox.IsFocusedProperty, Value = true };
        focusedTrigger.Setters.Add(new Setter(TextBox.BackgroundProperty, textBoxActivatedBrush));
        textBoxStyle.Triggers.Add(focusedTrigger);
        
        pathTextBox.Style = textBoxStyle;

        // 使用数据绑定将TextBox的Text属性绑定到ViewModel的ImagePath属性
        var pathBinding = new System.Windows.Data.Binding("ImagePath")
        {
            Source = viewModel,
            Mode = System.Windows.Data.BindingMode.OneWay,
            UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
        };
        pathTextBox.SetBinding(TextBox.TextProperty, pathBinding);

        mainPanel.Children.Add(pathTextBox);

        // 选择文件按钮 - 使用设计资源
        var selectButton = new Button
        {
            Content = "选择图像文件",
            Margin = new Thickness(2),
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
        
        selectButton.Style = buttonStyle;

        // 重新处理按钮 - 使用设计资源
        var reprocessButton = new Button
        {
            Content = "重新处理节点",
            Margin = new Thickness(2),
            Padding = new Thickness(10, 5, 10, 5),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF000000")),
            BorderThickness = new Thickness(1),
            Style = buttonStyle
        };

        // 图像信息显示部分
        TextBlock infoTextBlock = new TextBlock
        {
            Margin = new Thickness(10, 5, 10, 10),
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11
        };

        // 使用事件驱动的手动UI更新
        infoTextBlock.Text = viewModel.ImageInfo;

        // 订阅ViewModel的属性变化事件来更新图像信息显示
        viewModel.PropertyChanged += (sender, e) =>
        {
            if (e.PropertyName == nameof(viewModel.ImageInfo))
            {
                infoTextBlock.Text = viewModel.ImageInfo;
            }
        };

        // 调试信息
        TextBlock debugTextBlock = new TextBlock
        {
            Margin = new Thickness(10, 5, 10, 5),
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#777777")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 10
        };
        debugTextBlock.Text = $"节点ID: {NodeInstanceId}";

        selectButton.Click += (s, e) =>
        {

            var dialog = new OpenFileDialog
            {
                Filter = "图像文件|*.jpg;*.jpeg;*.png;*.bmp;*.tiff;*.tif;*.gif;*.webp|" +
                        "JPEG文件|*.jpg;*.jpeg|" +
                        "PNG文件|*.png|" +
                        "BMP文件|*.bmp|" +
                        "TIFF文件|*.tiff;*.tif|" +
                        "所有文件|*.*",
                Title = "选择图像文件",
                CheckFileExists = true,
                CheckPathExists = true
            };

            // 获取当前路径（优先从ViewModel获取，如果有的话）
            var currentPath = GetCurrentImagePath(mainPanel);
            if (!string.IsNullOrEmpty(currentPath) && System.IO.File.Exists(currentPath))
            {
                dialog.InitialDirectory = System.IO.Path.GetDirectoryName(currentPath);
                dialog.FileName = System.IO.Path.GetFileName(currentPath);
            }

            if (dialog.ShowDialog() == true)
            {
                // 优先通过ViewModel设置，这样会自动触发OnParameterChanged
                SetImagePath(mainPanel, dialog.FileName);
            }
        };

        reprocessButton.Click += (s, e) =>
        {
            // 直接触发重新处理
            if (this is RevivalScriptBase rsb)
            {
                // 使用RevivalScriptBase的OnParameterChanged方法触发重新处理
                rsb.OnParameterChanged(nameof(ImagePath), ImagePath);
            }
            else
            {
                // 回退到异步方法
                _ = OnParameterChangedAsync(nameof(ImagePath), null, ImagePath);
            }
        };

        mainPanel.Children.Add(selectButton);
        mainPanel.Children.Add(reprocessButton);

        // 图像信息显示
        var infoPanel = CreateImageInfoPanel(infoTextBlock, textBoxIdleBrush);
        mainPanel.Children.Add(infoPanel);

        // 调试信息显示
        mainPanel.Children.Add(debugTextBlock);

        return mainPanel;
    }

    private StackPanel CreateImageInfoPanel(TextBlock infoTextBlock, Brush backgroundBrush)
    {
        var infoPanel = new StackPanel
        {
            Margin = new Thickness(0, 10, 0, 0),
            Background = backgroundBrush
        };

        var infoLabel = new Label
        {
            Content = "图像信息:",
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11
        };
        infoPanel.Children.Add(infoLabel);

        infoPanel.Children.Add(infoTextBlock);
        return infoPanel;
    }

    private void UpdateImageInfo(TextBlock infoTextBlock, string imagePath)
    {

        if (string.IsNullOrEmpty(imagePath) || !System.IO.File.Exists(imagePath))
        {
            infoTextBlock.Text = "未选择图像文件";
            return;
        }

        try
        {
            var fileInfo = new System.IO.FileInfo(imagePath);
            var info = $"文件名: {fileInfo.Name}\n";
            info += $"大小: {FormatFileSize(fileInfo.Length)}\n";
            info += $"修改时间: {fileInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}\n";

            // 获取图像尺寸
            using var mat = Cv2.ImRead(imagePath, ImreadModes.Color);
            if (!mat.Empty())
            {
                info += $"尺寸: {mat.Width} × {mat.Height}\n";
                info += $"通道数: {mat.Channels()}";
            }

            infoTextBlock.Text = info;
        }
        catch (Exception ex)
        {
            infoTextBlock.Text = $"读取图像信息失败: {ex.Message}";
        }
    }

    private string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    /// <summary>
    /// 获取当前图像路径（优先从ViewModel获取）
    /// </summary>
    private string GetCurrentImagePath(FrameworkElement control)
    {
        try
        {
            // 尝试从DataContext获取ViewModel
            if (control.DataContext is ImageInputViewModel viewModel)
            {
                return viewModel.ImagePath;
            }

            // 回退到Script实例的属性
            return ImagePath;
        }
        catch (Exception ex)
        {
            return ImagePath;
        }
    }

    /// <summary>
    /// 设置图像路径（优先设置到ViewModel）
    /// </summary>
    private void SetImagePath(FrameworkElement control, string newPath)
    {
        try
        {
            // 尝试设置到DataContext的ViewModel
            if (control.DataContext is ImageInputViewModel viewModel)
            {
                viewModel.ImagePath = newPath;
                return;
            }

            // 回退到直接设置Script实例的属性，并手动触发参数变化事件
            ImagePath = newPath;
            // 手动触发参数变化事件，确保使用单一链路
            if (this is RevivalScriptBase rsb)
            {
                rsb.OnParameterChanged(nameof(ImagePath), newPath);
            }
        }
        catch (Exception ex)
        {
            // 最后的回退，也要触发参数变化事件
            ImagePath = newPath;
            if (this is RevivalScriptBase rsb)
            {
                rsb.OnParameterChanged(nameof(ImagePath), newPath);
            }
        }
    }

    public override IScriptViewModel CreateViewModel()
    {
        return new ImageInputViewModel(this);
    }

    public override async Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
    {
        if (parameterName == nameof(ImagePath))
        {
            ImagePath = newValue?.ToString() ?? string.Empty;
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
            [nameof(ImagePath)] = ImagePath,
            ["NodeInstanceId"] = NodeInstanceId // 保存节点实例ID
        };


        return data;
    }

    /// <summary>
    /// 反序列化参数
    /// </summary>
    /// <param name="data">参数字典</param>
    public override void DeserializeParameters(Dictionary<string, object> data)
    {

        // 恢复参数值
        if (data.TryGetValue(nameof(ImagePath), out var path))
        {
            ImagePath = path?.ToString() ?? string.Empty;
        }

        // 恢复节点实例ID
        if (data.TryGetValue("NodeInstanceId", out var nodeId))
        {
            NodeInstanceId = nodeId?.ToString() ?? string.Empty;
        }
    }

    /// <summary>
    /// 初始化节点实例ID
    /// </summary>
    /// <param name="nodeId">节点ID</param>
    public void InitializeNodeInstance(string nodeId)
    {
        if (!string.IsNullOrEmpty(nodeId))
        {
            NodeInstanceId = nodeId;
        }
    }
}

public class ImageInputViewModel : ScriptViewModelBase
{
    private ImageInputScript ImageInputScript => (ImageInputScript)Script;
    private string _imageInfo = "未选择图像文件";

    public string ImagePath
    {
        get => ImageInputScript.ImagePath;
        set
        {
            if (ImageInputScript.ImagePath != value)
            {
                var oldValue = ImageInputScript.ImagePath; // Capture old value for potential use
                ImageInputScript.ImagePath = value;
                OnPropertyChanged(); // Notify UI bound to this ViewModel property

                // 更新图像信息
                UpdateImageInfo(value);

                // 确保使用RevivalScriptBase的OnParameterChanged通知主程序参数变化
                // 这将触发整个图像处理流水线的重新计算
                if (Script is RevivalScriptBase rsb)
                {
                    rsb.OnParameterChanged(nameof(ImagePath), value);
                }
                else
                {
                    // 仅作为备选方案，如果Script不是RevivalScriptBase类型
                    _ = Script.OnParameterChangedAsync(nameof(ImagePath), oldValue, value);
                }
            }
        }
    }

    public string ImageInfo
    {
        get => _imageInfo;
        private set
        {
            if (_imageInfo != value)
            {
                _imageInfo = value;
                OnPropertyChanged();
            }
        }
    }

    public string NodeInstanceId => ImageInputScript.NodeInstanceId;

    public ImageInputViewModel(ImageInputScript script) : base(script)
    {
        // 初始化图像信息
        UpdateImageInfo(script.ImagePath);
    }

    private void UpdateImageInfo(string imagePath)
    {

        if (string.IsNullOrEmpty(imagePath) || !System.IO.File.Exists(imagePath))
        {
            ImageInfo = "未选择图像文件";
            return;
        }

        try
        {
            var fileInfo = new System.IO.FileInfo(imagePath);
            var info = $"文件名: {fileInfo.Name}\n";
            info += $"大小: {FormatFileSize(fileInfo.Length)}\n";
            info += $"修改时间: {fileInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}\n";

            // 获取图像尺寸
            using var mat = Cv2.ImRead(imagePath, ImreadModes.Color);
            if (!mat.Empty())
            {
                info += $"尺寸: {mat.Width} × {mat.Height}\n";
                info += $"通道数: {mat.Channels()}";
            }

            ImageInfo = info;
        }
        catch (Exception ex)
        {
            ImageInfo = $"读取图像信息失败: {ex.Message}";
        }
    }

    private string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    public override async Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
    {
        // 参数变化已经在属性setter中处理，这里不需要额外处理
        await Task.CompletedTask;
    }

    public override ScriptValidationResult ValidateParameter(string parameterName, object value)
    {
        // 简单验证，可以根据需要扩展
        return new ScriptValidationResult(true);
    }

    public override Dictionary<string, object> GetParameterData()
    {
        return new Dictionary<string, object>
        {
            [nameof(ImagePath)] = ImagePath
        };
    }

    public override async Task SetParameterDataAsync(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(ImagePath), out var path))
        {
            ImagePath = path?.ToString() ?? string.Empty;
        }
        await Task.CompletedTask;
    }

    public override async Task ResetToDefaultAsync()
    {
        ImagePath = string.Empty;
        await Task.CompletedTask;
    }

    public override void Dispose()
    {
        base.Dispose();
    }
}
