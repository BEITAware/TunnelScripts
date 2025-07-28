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

[TunnelExtensionScript(
    Name = "导出四象限",
    Author = "BEITAware",
    Description = "将输入图像分割为四个象限并导出，支持预览和自定义导出设置",
    Version = "1.0.0",
    Category = "导出",
    Color = "#4287F5"
)]
public class ExportQuadrantsScript : TunnelExtensionScriptBase
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
        
        var resources = new ResourceDictionary();
        var resourcePaths = new[]
        {
            "/Tunnel-Next;component/Resources/ScriptsControls/SharedBrushes.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/LabelStyles.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/PanelStyles.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/TextBoxIdleStyles.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/ComboBoxStyles.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/SliderStyles.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/CheckBoxStyles.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/ScriptButtonStyles.xaml"
        };
        foreach (var path in resourcePaths)
        {
            try { resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(path, UriKind.Relative) }); }
            catch { /* 静默处理 */ }
        }

        if (resources.Contains("MainPanelStyle")) mainPanel.Style = resources["MainPanelStyle"] as Style;

        var viewModel = CreateViewModel() as ExportQuadrantsViewModel;
        mainPanel.DataContext = viewModel;

        var titleLabel = new Label { Content = "四象限导出设置" };
        if (resources.Contains("TitleLabelStyle")) titleLabel.Style = resources["TitleLabelStyle"] as Style;
        mainPanel.Children.Add(titleLabel);

        mainPanel.Children.Add(CreateDirectoryPanel(viewModel, resources));
        mainPanel.Children.Add(CreatePrefixPanel(viewModel, resources));
        mainPanel.Children.Add(CreateGridPanel(viewModel, resources));
        mainPanel.Children.Add(CreateButtonPanel(viewModel, resources));

        return mainPanel;
    }

    private FrameworkElement CreateDirectoryPanel(ExportQuadrantsViewModel viewModel, ResourceDictionary resources)
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 10, 0, 0) };

        var label = new Label { Content = "导出目录:" };
        if (resources.Contains("DefaultLabelStyle")) label.Style = resources["DefaultLabelStyle"] as Style;
        panel.Children.Add(label);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var textBox = new TextBox { Margin = new Thickness(0, 0, 5, 0) };
        if (resources.Contains("DefaultTextBoxStyle")) textBox.Style = resources["DefaultTextBoxStyle"] as Style;
        textBox.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding(nameof(viewModel.ExportDirectory)) { Mode = System.Windows.Data.BindingMode.TwoWay });
        Grid.SetColumn(textBox, 0);
        grid.Children.Add(textBox);

        var browseButton = new Button { Content = "浏览..." };
        if (resources.Contains("SelectFileScriptButtonStyle")) browseButton.Style = resources["SelectFileScriptButtonStyle"] as Style;
        browseButton.Click += (s, e) => BrowseDirectory(textBox, viewModel);
        Grid.SetColumn(browseButton, 1);
        grid.Children.Add(browseButton);
        
        panel.Children.Add(grid);
        return panel;
    }

    private FrameworkElement CreatePrefixPanel(ExportQuadrantsViewModel viewModel, ResourceDictionary resources)
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 10, 0, 0) };

        var label = new Label { Content = "文件名前缀:" };
        if (resources.Contains("DefaultLabelStyle")) label.Style = resources["DefaultLabelStyle"] as Style;
        panel.Children.Add(label);

        var textBox = new TextBox();
        if (resources.Contains("DefaultTextBoxStyle")) textBox.Style = resources["DefaultTextBoxStyle"] as Style;
        textBox.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding(nameof(viewModel.FilenamePrefix)) { Mode = System.Windows.Data.BindingMode.TwoWay });
        panel.Children.Add(textBox);

        return panel;
    }

    private FrameworkElement CreateGridPanel(ExportQuadrantsViewModel viewModel, ResourceDictionary resources)
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 10, 0, 0) };

        var showGridCheckBox = new CheckBox { Content = "显示网格线" };
        if(resources.Contains("DefaultCheckBoxStyle")) showGridCheckBox.Style = resources["DefaultCheckBoxStyle"] as Style;
        showGridCheckBox.SetBinding(CheckBox.IsCheckedProperty, new System.Windows.Data.Binding(nameof(viewModel.ShowGrid)) { Mode = System.Windows.Data.BindingMode.TwoWay });
        panel.Children.Add(showGridCheckBox);

        var thicknessLabel = new Label { Content = "网格线粗细:" };
        if (resources.Contains("DefaultLabelStyle")) thicknessLabel.Style = resources["DefaultLabelStyle"] as Style;
        panel.Children.Add(thicknessLabel);

        var thicknessSlider = new Slider { Minimum = 1, Maximum = 10 };
        if(resources.Contains("DefaultSliderStyle")) thicknessSlider.Style = resources["DefaultSliderStyle"] as Style;
        thicknessSlider.SetBinding(Slider.ValueProperty, new System.Windows.Data.Binding(nameof(viewModel.GridThickness)) { Mode = System.Windows.Data.BindingMode.TwoWay });
        panel.Children.Add(thicknessSlider);

        var colorLabel = new Label { Content = "网格线颜色:" };
        if (resources.Contains("DefaultLabelStyle")) colorLabel.Style = resources["DefaultLabelStyle"] as Style;
        panel.Children.Add(colorLabel);

        var colorComboBox = new ComboBox { ItemsSource = _colorMap.Keys };
        if(resources.Contains("DefaultComboBoxStyle")) colorComboBox.Style = resources["DefaultComboBoxStyle"] as Style;
        colorComboBox.SetBinding(ComboBox.SelectedItemProperty, new System.Windows.Data.Binding(nameof(viewModel.GridColor)) { Mode = System.Windows.Data.BindingMode.TwoWay });
        panel.Children.Add(colorComboBox);

        return panel;
    }

    private FrameworkElement CreateButtonPanel(ExportQuadrantsViewModel viewModel, ResourceDictionary resources)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 15, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };

        var openFolderButton = new Button { Content = "打开文件夹", Margin = new Thickness(0, 0, 10, 0) };
        if (resources.Contains("SelectFileScriptButtonStyle")) openFolderButton.Style = resources["SelectFileScriptButtonStyle"] as Style;
        openFolderButton.Click += (s, e) => OpenExportFolder();
        panel.Children.Add(openFolderButton);

        var exportButton = new Button { Content = "导出四象限" };
        if (resources.Contains("SelectFileScriptButtonStyle")) exportButton.Style = resources["SelectFileScriptButtonStyle"] as Style;
        exportButton.Click += (s, e) => ExportQuadrants(viewModel);
        panel.Children.Add(exportButton);

        return panel;
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