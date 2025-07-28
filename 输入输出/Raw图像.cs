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
using Sdcb.LibRaw;

[TunnelExtensionScript(
    Name = "Raw图像输入",
    Author = "BEITAware",
    Description = "从Raw文件加载图像并向下游输出",
    Version = "1.0",
    Category = "输入输出",
    Color = "#B0C4DE"
)]
public class ImageInputScript : TunnelExtensionScriptBase
{
    [ScriptParameter(DisplayName = "图像路径", Description = "要加载的图像文件路径", Order = 0)]
    public string ImagePath { get; set; } = string.Empty;

    [ScriptParameter(DisplayName = "半尺寸解码", Description = "是否以HalfSize(1/4)分辨率解码", Order = 1)]
    public bool HalfSize { get; set; } = false;

    [ScriptParameter(DisplayName = "使用相机白平衡", Description = "使用相机内置白平衡而非手动", Order = 2)]
    public bool UseCameraWb { get; set; } = true;

    [ScriptParameter(DisplayName = "输出TIFF", Description = "输出TIFF格式而非Bitmap", Order = 3)]
    public bool OutputTiff { get; set; } = false;

    [ScriptParameter(DisplayName = "Gamma指数", Description = "Gamma曲线指数 (0-1)", Order = 4)]
    public double GammaExponent { get; set; } = 0.35;

    [ScriptParameter(DisplayName = "Gamma斜率", Description = "Gamma曲线斜率 (1-5)", Order = 5)]
    public double GammaSlope { get; set; } = 3.5;

    [ScriptParameter(DisplayName = "亮度", Description = "亮度 (0-5)", Order = 6)]
    public double RawBrightness { get; set; } = 2.2;

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
            ["f32bmp"] = new PortDefinition("f32bmp", false, "输出图像"),
            ["raw"] = new PortDefinition("raw", false, "输出裸Raw数据")
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
            // 利用 LibRaw 加载 RAW 图像并转换为 OpenCV BGRA 32F
            var outputMat = LoadRawToRGBA32F(ImagePath);

            if (outputMat == null || outputMat.Empty())
            {
                throw new InvalidOperationException("RAW 图像加载或转换失败");
            }
            
            var outputs = new Dictionary<string, object>
            {
                ["f32bmp"] = outputMat
            };

            // 输出裸Raw数据
            var rawMat = LoadRawData(ImagePath);
            if(rawMat != null && !rawMat.Empty())
            {
                outputs["raw"] = rawMat;
            }

            return outputs;
        }
        catch (Exception ex)
        {
            throw new ApplicationException($"处理RAW图像时发生错误: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 使用 Sdcb.LibRaw 将 RAW 文件转换为 32 位浮点 BGRA Mat。
    /// </summary>
    private Mat LoadRawToRGBA32F(string rawPath)
    {
        using RawContext ctx = RawContext.OpenFile(rawPath);
        ctx.Unpack();
        ctx.DcrawProcess(c =>
        {
            c.HalfSize = HalfSize;
            c.UseCameraWb = UseCameraWb;
            c.Gamma[0] = GammaExponent;
            c.Gamma[1] = GammaSlope;
            c.Brightness = (float)RawBrightness;
            c.Interpolation = true; // 总是进行插值
            c.OutputBps = 16;
            c.OutputTiff = OutputTiff;
        });
        using ProcessedImage img = ctx.MakeDcrawMemoryImage();

        // img.SwapRGB(); // Bypass LibRaw's swap, let OpenCV handle color conversion robustly.

        int width = img.Width;
        int height = img.Height;

        Mat matRgb32 = new Mat();

        using (Mat matRgb16 = Mat.FromPixelData(height, width, MatType.CV_16UC3, img.DataPointer, width * 3 * 2))
        {
            matRgb16.ConvertTo(matRgb32, MatType.CV_32FC3, 1.0 / 65535.0);
        }

        // Normalize the image to the [0.0, 1.0] range.
        // LibRaw processing with brightness adjustments can push values beyond 1.0.
        Cv2.Threshold(matRgb32, matRgb32, 1.0, 1.0, ThresholdTypes.Trunc);

        // LibRaw outputs RGB, convert to BGRA for the pipeline.
        // The CvtColor call was causing issues with the alpha channel, so we build it manually.
        Mat[] rgbChannels = Cv2.Split(matRgb32);
        Mat alphaChannel = new Mat(matRgb32.Size(), MatType.CV_32FC1, new Scalar(1.0));

        Mat matBgra32 = new Mat();
        // The source data is RGB, so the split channels are R, G, B.
        // To create a BGRA Mat, we merge them in the order: B, G, R, Alpha.
        Cv2.Merge(new Mat[] { rgbChannels[2], rgbChannels[1], rgbChannels[0], alphaChannel }, matBgra32);
        
        foreach (var ch in rgbChannels) ch.Dispose();
        alphaChannel.Dispose();
        matRgb32.Dispose();
        return matBgra32;
    }

    private Mat LoadRawData(string rawPath)
    {
        using RawContext ctx = RawContext.OpenFile(rawPath);
        ctx.Unpack();
        ctx.DcrawProcess(c =>
        {
            c.HalfSize = HalfSize;
            c.UseCameraWb = UseCameraWb;
            c.Gamma[0] = 1.0;
            c.Gamma[1] = 1.0;
            c.Brightness = 1.0f;
            c.Interpolation = false; // 不进行插值
            c.OutputBps = 16;
            c.OutputTiff = false;
            c.NoAutoBright = true;
        });
        using ProcessedImage img = ctx.MakeDcrawMemoryImage();

        int width = img.Width;
        int height = img.Height;

        Mat matRgb32 = new Mat();

        using (Mat matRgb16 = Mat.FromPixelData(height, width, MatType.CV_16UC3, img.DataPointer, width * 3 * 2))
        {
            matRgb16.ConvertTo(matRgb32, MatType.CV_32FC3, 1.0 / 65535.0);
        }

        // Normalize the image to the [0.0, 1.0] range to ensure it's a standard f32bmp.
        Cv2.Threshold(matRgb32, matRgb32, 1.0, 1.0, ThresholdTypes.Trunc);

        // LibRaw outputs RGB data. To create a BGRA Mat for the pipeline,
        // we need to swap the R and B channels and add a full alpha channel.
        Mat[] rgbChannels = Cv2.Split(matRgb32);
        Mat alphaChannel = new Mat(matRgb32.Size(), MatType.CV_32FC1, new Scalar(1.0));

        Mat matBgra32 = new Mat();
        // The source data is RGB, so the split channels are R, G, B.
        // To create a BGRA Mat, we merge them in the order: B, G, R, Alpha.
        Cv2.Merge(new Mat[] { rgbChannels[2], rgbChannels[1], rgbChannels[0], alphaChannel }, matBgra32);

        foreach (var ch in rgbChannels) ch.Dispose();
        alphaChannel.Dispose();
        matRgb32.Dispose();
        return matBgra32;
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

        // 加载所有需要的资源字典
        var resources = new ResourceDictionary();
        var resourcePaths = new[]
        {
            "/Tunnel-Next;component/Resources/ScriptsControls/SharedBrushes.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/LabelStyles.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/PanelStyles.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/ScriptButtonStyles.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/TextBoxIdleStyles.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/TextBoxActivatedStyles.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/SliderStyles.xaml"
        };

        foreach (var path in resourcePaths)
        {
            try
            {
                resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(path, UriKind.Relative) });
            }
            catch (Exception)
            {
                // 静默处理资源加载失败
            }
        }

        // 应用主面板样式
        if (resources.Contains("MainPanelStyle"))
        {
            mainPanel.Style = resources["MainPanelStyle"] as Style;
        }

        // 创建并设置ViewModel作为DataContext
        var viewModel = CreateViewModel() as ImageInputViewModel;
        mainPanel.DataContext = viewModel;

        // 标题
        var titleLabel = new Label { Content = "图像文件选择" };
        if (resources.Contains("TitleLabelStyle"))
        {
            titleLabel.Style = resources["TitleLabelStyle"] as Style;
        }
        mainPanel.Children.Add(titleLabel);

        // 当前路径显示
        var pathLabel = new Label { Content = "当前路径:" };
        if (resources.Contains("DefaultLabelStyle"))
        {
            pathLabel.Style = resources["DefaultLabelStyle"] as Style;
        }
        mainPanel.Children.Add(pathLabel);

        var pathTextBox = new TextBox { IsReadOnly = true };
        if (resources.Contains("DefaultTextBoxStyle"))
        {
            pathTextBox.Style = resources["DefaultTextBoxStyle"] as Style;
        }
        var pathBinding = new Binding("ImagePath") { Mode = BindingMode.OneWay };
        pathTextBox.SetBinding(TextBox.TextProperty, pathBinding);
        mainPanel.Children.Add(pathTextBox);

        // 选择文件按钮
        var selectFileButton = new Button { Content = "选择文件", Margin = new Thickness(0,5,0,10) };
        if (resources.Contains("SelectFileScriptButtonStyle"))
        {
            selectFileButton.Style = resources["SelectFileScriptButtonStyle"] as Style;
        }
        selectFileButton.Click += (s, e) =>
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Raw图像|*.dng;*.arw;*.cr2;*.cr3;*.nef;*.rw2;*.raw;*.orf;*.pef;*.srw;*.raf|所有文件|*.*"
            };
            if (openFileDialog.ShowDialog() == true)
            {
                viewModel.ImagePath = openFileDialog.FileName;
            }
        };
        mainPanel.Children.Add(selectFileButton);

        // 图像信息
        var infoLabel = new Label { Content = "图像信息:" };
        if (resources.Contains("DefaultLabelStyle"))
        {
            infoLabel.Style = resources["DefaultLabelStyle"] as Style;
        }
        mainPanel.Children.Add(infoLabel);

        var infoTextBlock = new TextBlock();
        var infoBinding = new Binding("ImageInfo") { Mode = BindingMode.OneWay };
        infoTextBlock.SetBinding(TextBlock.TextProperty, infoBinding);
        mainPanel.Children.Add(infoTextBlock);

        // --- 新增RAW解码参数控件 ---
        mainPanel.Children.Add(new Separator { Margin = new Thickness(0,10,0,10) });

        // 复选框控件方法
        FrameworkElement CreateCheck(string label, string prop)
        {
            var cb = new CheckBox { Content = label, Margin = new Thickness(0,5,0,0) };
            cb.SetBinding(CheckBox.IsCheckedProperty, new Binding(prop) { Mode = BindingMode.TwoWay });
            if (resources.Contains("DefaultCheckBoxStyle"))
                cb.Style = resources["DefaultCheckBoxStyle"] as Style;
            return cb;
        }

        // 滑块控件方法
        FrameworkElement CreateSlider(string label, string prop, double min, double max)
        {
            var panel = new StackPanel { Margin = new Thickness(0,5,0,0) };
            var lbl = new Label { Content = label };
            if (resources.Contains("DefaultLabelStyle")) lbl.Style = resources["DefaultLabelStyle"] as Style;
            var slider = new Slider { Minimum = min, Maximum = max };
            if (resources.Contains("DefaultSliderStyle")) slider.Style = resources["DefaultSliderStyle"] as Style;
            slider.SetBinding(Slider.ValueProperty, new Binding(prop) { Mode = BindingMode.TwoWay });
            panel.Children.Add(lbl);
            panel.Children.Add(slider);
            return panel;
        }

        mainPanel.Children.Add(CreateCheck("半尺寸解码", nameof(HalfSize)));
        mainPanel.Children.Add(CreateCheck("使用相机白平衡", nameof(UseCameraWb)));
        mainPanel.Children.Add(CreateCheck("输出TIFF", nameof(OutputTiff)));
        mainPanel.Children.Add(CreateSlider("Gamma指数", nameof(GammaExponent), 0.0, 1.0));
        mainPanel.Children.Add(CreateSlider("Gamma斜率", nameof(GammaSlope), 1.0, 5.0));
        mainPanel.Children.Add(CreateSlider("亮度", nameof(RawBrightness), 0.0, 5.0));

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
            if (this is TunnelExtensionScriptBase rsb)
            {
                rsb.OnParameterChanged(nameof(ImagePath), newPath);
            }
        }
        catch (Exception ex)
        {
            // 最后的回退，也要触发参数变化事件
            ImagePath = newPath;
            if (this is TunnelExtensionScriptBase rsb)
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
        switch (parameterName)
        {
            case nameof(ImagePath):
                ImagePath = newValue?.ToString() ?? string.Empty;
                break;
            case nameof(HalfSize):
                if (newValue is bool hs) HalfSize = hs;
                break;
            case nameof(UseCameraWb):
                if (newValue is bool uwb) UseCameraWb = uwb;
                break;
            case nameof(OutputTiff):
                if (newValue is bool ot) OutputTiff = ot;
                break;
            case nameof(GammaExponent):
                if (newValue is double ge) GammaExponent = ge;
                break;
            case nameof(GammaSlope):
                if (newValue is double gs) GammaSlope = gs;
                break;
            case nameof(RawBrightness):
                if (newValue is double rb) RawBrightness = rb;
                break;
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
            [nameof(HalfSize)] = HalfSize,
            [nameof(UseCameraWb)] = UseCameraWb,
            [nameof(OutputTiff)] = OutputTiff,
            [nameof(GammaExponent)] = GammaExponent,
            [nameof(GammaSlope)] = GammaSlope,
            [nameof(RawBrightness)] = RawBrightness,
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

        if (data.TryGetValue(nameof(HalfSize), out var h)) HalfSize = Convert.ToBoolean(h);
        if (data.TryGetValue(nameof(UseCameraWb), out var u)) UseCameraWb = Convert.ToBoolean(u);
        if (data.TryGetValue(nameof(OutputTiff), out var t)) OutputTiff = Convert.ToBoolean(t);
        if (data.TryGetValue(nameof(GammaExponent), out var g1)) GammaExponent = Convert.ToDouble(g1);
        if (data.TryGetValue(nameof(GammaSlope), out var g2)) GammaSlope = Convert.ToDouble(g2);
        if (data.TryGetValue(nameof(RawBrightness), out var rb)) RawBrightness = Convert.ToDouble(rb);

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

                Notify(nameof(ImagePath), value);
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

    public bool HalfSize
    {
        get => ImageInputScript.HalfSize;
        set { if (ImageInputScript.HalfSize != value) { ImageInputScript.HalfSize = value; OnPropertyChanged(); Notify(nameof(HalfSize), value); } }
    }

    public bool UseCameraWb
    {
        get => ImageInputScript.UseCameraWb;
        set { if (ImageInputScript.UseCameraWb != value) { ImageInputScript.UseCameraWb = value; OnPropertyChanged(); Notify(nameof(UseCameraWb), value); } }
    }

    public bool OutputTiff
    {
        get => ImageInputScript.OutputTiff;
        set { if (ImageInputScript.OutputTiff != value) { ImageInputScript.OutputTiff = value; OnPropertyChanged(); Notify(nameof(OutputTiff), value); } }
    }

    public double GammaExponent
    {
        get => ImageInputScript.GammaExponent;
        set { if (Math.Abs(ImageInputScript.GammaExponent - value) > 0.0001) { ImageInputScript.GammaExponent = value; OnPropertyChanged(); Notify(nameof(GammaExponent), value); } }
    }

    public double GammaSlope
    {
        get => ImageInputScript.GammaSlope;
        set { if (Math.Abs(ImageInputScript.GammaSlope - value) > 0.0001) { ImageInputScript.GammaSlope = value; OnPropertyChanged(); Notify(nameof(GammaSlope), value); } }
    }

    public double RawBrightness
    {
        get => ImageInputScript.RawBrightness;
        set { if (Math.Abs(ImageInputScript.RawBrightness - value) > 0.0001) { ImageInputScript.RawBrightness = value; OnPropertyChanged(); Notify(nameof(RawBrightness), value); } }
    }

    private void Notify(string param, object val)
    {
        if (Script is TunnelExtensionScriptBase rsb) rsb.OnParameterChanged(param, val);
    }

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
        // Manually trigger property changed to update UI, as the change comes from the script.
        OnPropertyChanged(parameterName);
        if (parameterName == nameof(ImagePath))
        {
            UpdateImageInfo(newValue as string);
        }
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
            [nameof(ImagePath)] = ImagePath,
            [nameof(HalfSize)] = HalfSize,
            [nameof(UseCameraWb)] = UseCameraWb,
            [nameof(OutputTiff)] = OutputTiff,
            [nameof(GammaExponent)] = GammaExponent,
            [nameof(GammaSlope)] = GammaSlope,
            [nameof(RawBrightness)] = RawBrightness
        };
    }

    public override async Task SetParameterDataAsync(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(ImagePath), out var path))
        {
            ImagePath = path?.ToString() ?? string.Empty;
        }
        if (data.TryGetValue(nameof(HalfSize), out var h)) HalfSize = Convert.ToBoolean(h);
        if (data.TryGetValue(nameof(UseCameraWb), out var u)) UseCameraWb = Convert.ToBoolean(u);
        if (data.TryGetValue(nameof(OutputTiff), out var t)) OutputTiff = Convert.ToBoolean(t);
        if (data.TryGetValue(nameof(GammaExponent), out var g1)) GammaExponent = Convert.ToDouble(g1);
        if (data.TryGetValue(nameof(GammaSlope), out var g2)) GammaSlope = Convert.ToDouble(g2);
        if (data.TryGetValue(nameof(RawBrightness), out var rb)) RawBrightness = Convert.ToDouble(rb);
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
