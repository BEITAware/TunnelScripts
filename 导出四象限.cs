using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenCvSharp;
using Tunnel_Next.Services.Scripting;

[RevivalScript(
    Name = "导出四象限",
    Author = "Tunnel Team",
    Description = "将输入图像分割为四个象限并导出，支持预览和自定义导出设置",
    Version = "1.0.0",
    Category = "导出",
    Color = "#4287F5"
)]
public class ExportQuadrantsScript : RevivalScriptBase
{
    #region 参数定义

    [ScriptParameter(DisplayName = "导出目录", Description = "选择导出文件夹（留空使用工作目录）", Order = 0)]
    public string ExportDirectory { get; set; } = "";

    [ScriptParameter(DisplayName = "文件名前缀", Description = "导出文件的前缀名称", Order = 1)]
    public string FilenamePrefix { get; set; } = "quadrant";

    [ScriptParameter(DisplayName = "显示网格线", Description = "在预览中显示四象限分割线", Order = 2)]
    public bool ShowGrid { get; set; } = true;

    [ScriptParameter(DisplayName = "网格线粗细", Description = "分割线的粗细程度", Order = 3)]
    public int GridThickness { get; set; } = 2;

    [ScriptParameter(DisplayName = "网格线颜色", Description = "分割线的颜色", Order = 4)]
    public string GridColor { get; set; } = "红色";

    #endregion

    #region 私有字段

    private Mat? _inputImage;
    private readonly Dictionary<string, Scalar> _colorMap = new()
    {
        { "白色", new Scalar(255, 255, 255, 255) },
        { "黑色", new Scalar(0, 0, 0, 255) },
        { "红色", new Scalar(0, 0, 255, 255) },
        { "绿色", new Scalar(0, 255, 0, 255) },
        { "蓝色", new Scalar(255, 0, 0, 255) },
        { "黄色", new Scalar(0, 255, 255, 255) }
    };

    #endregion

    #region 端口定义

    public override Dictionary<string, PortDefinition> GetInputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            ["f32bmp"] = new PortDefinition("f32bmp", false, "图像输入")
        };
    }

    public override Dictionary<string, PortDefinition> GetOutputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            ["f32bmp"] = new PortDefinition("f32bmp", false, "预览输出")
        };
    }

    #endregion

    #region 核心处理

    public override Dictionary<string, object> Process(Dictionary<string, object> inputs, IScriptContext context)
    {
        var outputs = new Dictionary<string, object>();

        try
        {
            // 获取输入图像
            if (inputs.TryGetValue("f32bmp", out var inputObj) && inputObj is Mat inputMat)
            {
                _inputImage = inputMat.Clone();

                // 生成预览图像
                var previewImage = GeneratePreviewImage(_inputImage);
                outputs["f32bmp"] = previewImage;
            }
            else
            {
                // 没有输入时返回空
                outputs["f32bmp"] = new Mat();
            }
        }
        catch (Exception ex)
        {
            // 返回空图像
            outputs["f32bmp"] = new Mat();
        }

        return outputs;
    }

    #endregion

    #region 私有方法

    private Mat GeneratePreviewImage(Mat inputImage)
    {
        var result = inputImage.Clone();

        if (ShowGrid && !inputImage.Empty())
        {
            // 获取图像尺寸
            int height = inputImage.Height;
            int width = inputImage.Width;

            // 计算中心点
            int centerX = width / 2;
            int centerY = height / 2;

            // 获取网格线颜色
            var gridColor = _colorMap.TryGetValue(GridColor, out var color) ? color : _colorMap["红色"];

            // 绘制水平线
            Cv2.Line(result,
                new OpenCvSharp.Point(0, centerY),
                new OpenCvSharp.Point(width, centerY),
                gridColor,
                GridThickness);

            // 绘制垂直线
            Cv2.Line(result,
                new OpenCvSharp.Point(centerX, 0),
                new OpenCvSharp.Point(centerX, height),
                gridColor,
                GridThickness);
        }

        return result;
    }

    private Mat CreateQuadrant(Mat sourceImage, int x, int y, int width, int height)
    {
        try
        {
            // 创建ROI区域
            var roi = new OpenCvSharp.Rect(x, y, width, height);

            // 提取象限图像
            var quadrant = sourceImage[roi].Clone();

            // 根据Python原型，需要处理数据类型转换
            Mat processedQuadrant;

            if (quadrant.Depth() == MatType.CV_32F)
            {
                // float32 类型，需要转换为 uint8 (乘以255)
                processedQuadrant = new Mat();
                quadrant.ConvertTo(processedQuadrant, MatType.CV_8U, 255.0, 0.0);
                quadrant.Dispose();
            }
            else if (quadrant.Depth() == MatType.CV_16U)
            {
                // uint16 类型，需要转换为 uint8 (除以256)
                processedQuadrant = new Mat();
                quadrant.ConvertTo(processedQuadrant, MatType.CV_8U, 1.0/256.0, 0.0);
                quadrant.Dispose();
            }
            else
            {
                // 已经是 uint8 或其他类型，确保转换为 uint8
                processedQuadrant = new Mat();
                quadrant.ConvertTo(processedQuadrant, MatType.CV_8U);
                quadrant.Dispose();
            }

            return processedQuadrant;
        }
        catch (Exception ex)
        {
            // 如果分割失败，返回空图像
            return new Mat();
        }
    }

    #endregion

    #region UI和ViewModel

    public override FrameworkElement CreateParameterControl()
    {
        var mainPanel = new StackPanel { Margin = new Thickness(5, 5, 5, 5) };

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

        // 创建并设置ViewModel作为DataContext
        var viewModel = CreateViewModel() as ExportQuadrantsViewModel;
        mainPanel.DataContext = viewModel;

        // 标题
        var titleLabel = new Label
        {
            Content = "四象限导出设置",
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial")
        };
        mainPanel.Children.Add(titleLabel);

        // 导出目录设置
        var dirPanel = CreateDirectoryPanel(viewModel);
        mainPanel.Children.Add(dirPanel);

        // 文件名前缀设置
        var prefixPanel = CreatePrefixPanel(viewModel);
        mainPanel.Children.Add(prefixPanel);

        // 网格设置
        var gridPanel = CreateGridPanel(viewModel);
        mainPanel.Children.Add(gridPanel);

        // 操作按钮
        var buttonPanel = CreateButtonPanel(viewModel);
        mainPanel.Children.Add(buttonPanel);

        return mainPanel;
    }

    private StackPanel CreateDirectoryPanel(ExportQuadrantsViewModel viewModel)
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 10, 0, 0) };

        var label = new Label
        {
            Content = "导出目录:",
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11
        };
        panel.Children.Add(label);

        var dirPanel = new StackPanel { Orientation = Orientation.Horizontal };

        // 使用designs资源样式的文本框 - 基于TextBoxBasic设计
        var textBox = new TextBox
        {
            Width = 200,
            Margin = new Thickness(0, 0, 5, 0),
            Text = ExportDirectory,
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new GradientStop((Color)ColorConverter.ConvertFromString("#FFFFFFFF"), 0),
                    new GradientStop((Color)ColorConverter.ConvertFromString("#FFB3BBBC"), 1)
                },
                new System.Windows.Point(0.5, 1.72875), new System.Windows.Point(0.5, -0.728735)
            ),
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF2E5C8A")),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF000000")),
            BorderThickness = new Thickness(1, 1, 1, 1),
            Padding = new Thickness(6, 4, 6, 4),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11
        };
        textBox.TextChanged += (s, e) => viewModel.ExportDirectory = textBox.Text;

        var browseButton = CreateStyledButton("浏览...", 60);
        browseButton.Click += (s, e) => BrowseDirectory(textBox, viewModel);

        dirPanel.Children.Add(textBox);
        dirPanel.Children.Add(browseButton);
        panel.Children.Add(dirPanel);

        return panel;
    }

    private StackPanel CreatePrefixPanel(ExportQuadrantsViewModel viewModel)
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 10, 0, 0) };

        var label = new Label
        {
            Content = "文件名前缀:",
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11
        };
        panel.Children.Add(label);

        // 使用designs资源样式的文本框 - 基于TextBoxBasic设计
        var textBox = new TextBox
        {
            Width = 200,
            Text = FilenamePrefix,
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new GradientStop((Color)ColorConverter.ConvertFromString("#FFFFFFFF"), 0),
                    new GradientStop((Color)ColorConverter.ConvertFromString("#FFB3BBBC"), 1)
                },
                new System.Windows.Point(0.5, 1.72875), new System.Windows.Point(0.5, -0.728735)
            ),
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF2E5C8A")),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF000000")),
            BorderThickness = new Thickness(1, 1, 1, 1),
            Padding = new Thickness(6, 4, 6, 4),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        textBox.TextChanged += (s, e) => viewModel.FilenamePrefix = textBox.Text;

        panel.Children.Add(textBox);
        return panel;
    }

    private StackPanel CreateGridPanel(ExportQuadrantsViewModel viewModel)
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 10, 0, 0) };

        var titleLabel = new Label
        {
            Content = "网格设置:",
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold
        };
        panel.Children.Add(titleLabel);

        // 显示网格线复选框
        var showGridCheckBox = new CheckBox
        {
            Content = "显示网格线",
            IsChecked = ShowGrid,
            Margin = new Thickness(0, 5, 0, 0),
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11
        };
        showGridCheckBox.Checked += (s, e) => { viewModel.ShowGrid = true; };
        showGridCheckBox.Unchecked += (s, e) => { viewModel.ShowGrid = false; };

        panel.Children.Add(showGridCheckBox);
        return panel;
    }

    private StackPanel CreateButtonPanel(ExportQuadrantsViewModel viewModel)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 15, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };

        // 导出按钮
        var exportButton = CreateStyledButton("导出四象限", 100);
        exportButton.Click += (s, e) => ExportQuadrants(viewModel);

        // 打开文件夹按钮
        var openFolderButton = CreateStyledButton("打开文件夹", 100);
        openFolderButton.Click += (s, e) => OpenExportFolder();

        panel.Children.Add(exportButton);
        panel.Children.Add(openFolderButton);

        return panel;
    }

    private Button CreateStyledButton(string content, double width)
    {
        return new Button
        {
            Content = content,
            Width = width,
            Margin = new Thickness(2, 2, 2, 2),
            Padding = new Thickness(10, 5, 10, 5),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new GradientStop((Color)ColorConverter.ConvertFromString("#00FFFFFF"), 0),
                    new GradientStop((Color)ColorConverter.ConvertFromString("#1AFFFFFF"), 0.135436),
                    new GradientStop((Color)ColorConverter.ConvertFromString("#17FFFFFF"), 0.487941),
                    new GradientStop((Color)ColorConverter.ConvertFromString("#00000004"), 0.517625),
                    new GradientStop((Color)ColorConverter.ConvertFromString("#FF1F8EAD"), 0.729128)
                },
                new System.Windows.Point(0.5, -0.667875), new System.Windows.Point(0.5, 1.66787)
            ),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF000000")),
            BorderThickness = new Thickness(1, 1, 1, 1)
        };
    }

    private void BrowseDirectory(TextBox textBox, ExportQuadrantsViewModel viewModel)
    {
        try
        {
            // 使用标准的文件夹选择对话框
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "选择导出目录";
                dialog.ShowNewFolderButton = true;

                if (!string.IsNullOrEmpty(ExportDirectory))
                {
                    dialog.SelectedPath = ExportDirectory;
                }

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    viewModel.ExportDirectory = dialog.SelectedPath;
                    textBox.Text = dialog.SelectedPath;
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"选择目录时发生错误: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public override IScriptViewModel CreateViewModel()
    {
        return new ExportQuadrantsViewModel(this);
    }

    #endregion

    #region 导出功能

    private void ExportQuadrants(ExportQuadrantsViewModel viewModel)
    {
        try
        {
            if (_inputImage == null || _inputImage.Empty())
            {
                MessageBox.Show("没有可用的图像数据", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 显示输入图像信息用于调试
            var imageInfo = $"输入图像信息:\n" +
                           $"尺寸: {_inputImage.Width} x {_inputImage.Height}\n" +
                           $"通道数: {_inputImage.Channels()}\n" +
                           $"深度: {_inputImage.Depth()}\n" +
                           $"类型: {_inputImage.Type()}";

            var debugResult = MessageBox.Show($"{imageInfo}\n\n是否继续导出？", "图像信息", MessageBoxButton.YesNo, MessageBoxImage.Information);

            if (debugResult != MessageBoxResult.Yes)
                return;

            var result = ExportQuadrantsToFiles();

            if (result.Success)
            {
                MessageBox.Show(result.Message, "导出成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(result.ErrorMessage, "导出失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导出时发生错误: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenExportFolder()
    {
        try
        {
            var targetDir = string.IsNullOrEmpty(ExportDirectory) ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop) : ExportDirectory;

            // 确保目录存在
            Directory.CreateDirectory(targetDir);

            // 尝试直接打开文件夹
            try
            {
                // 使用Windows Shell命令打开文件夹
                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{targetDir}\"",
                    UseShellExecute = true,
                    CreateNoWindow = true
                };
                System.Diagnostics.Process.Start(processInfo);
            }
            catch
            {
                // 如果直接打开失败，显示路径并提供复制功能
                var result = MessageBox.Show($"导出目录: {targetDir}\n\n无法自动打开文件夹。\n\n点击\"是\"复制路径到剪贴板", "导出目录", MessageBoxButton.YesNo, MessageBoxImage.Information);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        Clipboard.SetText(targetDir);
                        MessageBox.Show("路径已复制到剪贴板，请手动打开文件管理器并粘贴路径", "复制成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch
                    {
                        // 忽略剪贴板错误
                        MessageBox.Show($"路径: {targetDir}\n\n请手动复制此路径并在文件管理器中打开", "导出目录", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"获取导出目录时发生错误: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private (bool Success, string Message, string ErrorMessage) ExportQuadrantsToFiles()
    {
        try
        {
            if (_inputImage == null || _inputImage.Empty())
            {
                return (false, "", "没有可用的图像数据");
            }

            // 确定导出目录
            var exportDir = string.IsNullOrEmpty(ExportDirectory) ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop) : ExportDirectory;

            // 确保导出目录存在
            Directory.CreateDirectory(exportDir);

            // 获取图像尺寸
            int height = _inputImage.Height;
            int width = _inputImage.Width;

            // 计算中心点
            int centerX = width / 2;
            int centerY = height / 2;

            // 生成时间戳
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            // 分割四个象限 - 按照Python原型的正确算法
            // Python: quadrant1 = img[0:center_y, center_x:width]      # q1_右上
            // Python: quadrant2 = img[0:center_y, 0:center_x]          # q2_左上
            // Python: quadrant3 = img[center_y:height, 0:center_x]     # q3_左下
            // Python: quadrant4 = img[center_y:height, center_x:width] # q4_右下
            var quadrants = new[]
            {
                (CreateQuadrant(_inputImage, centerX, 0, width - centerX, centerY), "q1_右上"),
                (CreateQuadrant(_inputImage, 0, 0, centerX, centerY), "q2_左上"),
                (CreateQuadrant(_inputImage, 0, centerY, centerX, height - centerY), "q3_左下"),
                (CreateQuadrant(_inputImage, centerX, centerY, width - centerX, height - centerY), "q4_右下")
            };

            var savedFiles = new List<string>();

            foreach (var (quadrant, suffix) in quadrants)
            {
                try
                {
                    string filename = $"{FilenamePrefix}_{suffix}_{timestamp}.png";
                    string filepath = Path.Combine(exportDir, filename);

                    // 确保Alpha通道正确保存
                    if (quadrant.Channels() == 4)
                    {
                        // 对于RGBA图像，使用PNG格式保存以保留Alpha通道
                        var saveParams = new int[] { (int)ImwriteFlags.PngCompression, 9 };
                        Cv2.ImWrite(filepath, quadrant, saveParams);
                    }
                    else
                    {
                        // 对于RGB图像，正常保存
                        Cv2.ImWrite(filepath, quadrant);
                    }

                    savedFiles.Add(Path.GetFileName(filepath));
                    quadrant.Dispose();
                }
                catch (Exception ex)
                {
                    return (false, "", $"保存象限 {suffix} 时出错: {ex.Message}");
                }
            }

            var message = $"已成功导出四个象限图像到: {exportDir}\n" +
                         string.Join("\n", savedFiles.Select(f => $"- {f}"));

            return (true, message, "");
        }
        catch (Exception ex)
        {
            return (false, "", $"导出四象限时出错: {ex.Message}");
        }
    }

    #endregion

    #region 必须实现的抽象方法

    public override async Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
    {
        // 参数变化处理
        await Task.CompletedTask;
    }

    public override Dictionary<string, object> SerializeParameters()
    {
        return new Dictionary<string, object>
        {
            { nameof(ExportDirectory), ExportDirectory },
            { nameof(FilenamePrefix), FilenamePrefix },
            { nameof(ShowGrid), ShowGrid },
            { nameof(GridThickness), GridThickness },
            { nameof(GridColor), GridColor }
        };
    }

    public override void DeserializeParameters(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(ExportDirectory), out var exportDir))
            ExportDirectory = exportDir?.ToString() ?? "";

        if (data.TryGetValue(nameof(FilenamePrefix), out var prefix))
            FilenamePrefix = prefix?.ToString() ?? "quadrant";

        if (data.TryGetValue(nameof(ShowGrid), out var showGrid))
            ShowGrid = Convert.ToBoolean(showGrid);

        if (data.TryGetValue(nameof(GridThickness), out var thickness))
            GridThickness = Convert.ToInt32(thickness);

        if (data.TryGetValue(nameof(GridColor), out var color))
            GridColor = color?.ToString() ?? "红色";
    }

    #endregion
}

public class ExportQuadrantsViewModel : ScriptViewModelBase
{
    private ExportQuadrantsScript ExportScript => (ExportQuadrantsScript)Script;

    public string ExportDirectory
    {
        get => ExportScript.ExportDirectory;
        set
        {
            if (ExportScript.ExportDirectory != value)
            {
                ExportScript.ExportDirectory = value;
                OnPropertyChanged();
            }
        }
    }

    public string FilenamePrefix
    {
        get => ExportScript.FilenamePrefix;
        set
        {
            if (ExportScript.FilenamePrefix != value)
            {
                ExportScript.FilenamePrefix = value;
                OnPropertyChanged();
            }
        }
    }

    public bool ShowGrid
    {
        get => ExportScript.ShowGrid;
        set
        {
            if (ExportScript.ShowGrid != value)
            {
                ExportScript.ShowGrid = value;
                OnPropertyChanged();
            }
        }
    }

    public int GridThickness
    {
        get => ExportScript.GridThickness;
        set
        {
            if (ExportScript.GridThickness != value)
            {
                ExportScript.GridThickness = value;
                OnPropertyChanged();
            }
        }
    }

    public string GridColor
    {
        get => ExportScript.GridColor;
        set
        {
            if (ExportScript.GridColor != value)
            {
                ExportScript.GridColor = value;
                OnPropertyChanged();
            }
        }
    }

    public ExportQuadrantsViewModel(ExportQuadrantsScript script) : base(script)
    {
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
        return new Dictionary<string, object>
        {
            [nameof(ExportDirectory)] = ExportDirectory,
            [nameof(FilenamePrefix)] = FilenamePrefix,
            [nameof(ShowGrid)] = ShowGrid,
            [nameof(GridThickness)] = GridThickness,
            [nameof(GridColor)] = GridColor
        };
    }

    public override async Task SetParameterDataAsync(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(ExportDirectory), out var exportDir))
            ExportDirectory = exportDir?.ToString() ?? "";

        if (data.TryGetValue(nameof(FilenamePrefix), out var prefix))
            FilenamePrefix = prefix?.ToString() ?? "quadrant";

        if (data.TryGetValue(nameof(ShowGrid), out var showGrid))
            ShowGrid = Convert.ToBoolean(showGrid);

        if (data.TryGetValue(nameof(GridThickness), out var thickness))
            GridThickness = Convert.ToInt32(thickness);

        if (data.TryGetValue(nameof(GridColor), out var color))
            GridColor = color?.ToString() ?? "红色";

        await Task.CompletedTask;
    }

    public override async Task ResetToDefaultAsync()
    {
        ExportDirectory = "";
        FilenamePrefix = "quadrant";
        ShowGrid = true;
        GridThickness = 2;
        GridColor = "红色";
        await Task.CompletedTask;
    }
}