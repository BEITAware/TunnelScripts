using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using Tunnel_Next.Services.Scripting;
using Microsoft.Win32;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using Vortice.Direct2D1;
using Vortice.WIC;
using Vortice.DXGI;
using Vortice.Direct3D11;
using Vortice.Direct3D;
using System.IO;

[TunnelExtensionScript(
    Name = "图像输入+",
    Author = "BEITAware",
    Description = "从文件加载图像并转换为DXGI_FORMAT_R32G32B32A32_FLOAT格式输出",
    Version = "1.0",
    Category = "输入输出",
    Color = "#4ECDC4"
)]
public class ImageInputPlusScript : TunnelExtensionScriptBase
{
    [ScriptParameter(DisplayName = "图像路径", Description = "要加载的图像文件路径", Order = 0)]
    public string ImagePath { get; set; } = string.Empty;

    // 处理节点实例标识
    public string NodeInstanceId { get; set; } = string.Empty;

    // 保存处理上下文的引用
    private IScriptContext _lastContext;

    // D3D11设备和上下文
    private ID3D11Device _device;
    private ID3D11DeviceContext _deviceContext;
    private IWICImagingFactory _wicFactory;

    public override Dictionary<string, PortDefinition> GetInputPorts()
    {
        // 图像输入节点不需要输入端口
        return new Dictionary<string, PortDefinition>();
    }

    public override Dictionary<string, PortDefinition> GetOutputPorts()
    {
        // 输出F32texture格式（即ReadWriteTexture2D<float4>）
        return new Dictionary<string, PortDefinition>
        {
            ["F32texture"] = new PortDefinition("F32texture", false, "输出F32texture")
        };
    }

    public override Dictionary<string, object> Process(Dictionary<string, object> inputs, IScriptContext context)
    {
        // 保存上下文引用以供其他方法使用
        _lastContext = context;

        // 支持路径占位符替换
        var resolvedPath = ReplacePlaceholders(ImagePath, context);
        if (string.IsNullOrEmpty(resolvedPath) || !System.IO.File.Exists(resolvedPath))
        {
            throw new ArgumentException("请选择有效的图像文件");
        }

        try
        {
            // 初始化D3D11设备和WIC工厂
            InitializeDeviceAndWIC();

            // 使用Vortice WIC加载图像并转换为DXGI_FORMAT_R32G32B32A32_FLOAT
            var texture = LoadImageAsFloat32Texture(resolvedPath);

            return new Dictionary<string, object>
            {
                ["F32texture"] = texture
            };
        }
        catch (Exception ex)
        {
            throw new ApplicationException($"处理图像时发生错误: {ex.Message}", ex);
        }
    }

    private void InitializeDeviceAndWIC()
    {
        if (_device == null)
        {
            // 创建D3D11设备
            D3D11.D3D11CreateDevice(
                null,
                DriverType.Hardware,
                DeviceCreationFlags.None,
                null,
                out _device,
                out _deviceContext);
        }

        if (_wicFactory == null)
        {
            // 创建WIC工厂
            _wicFactory = new IWICImagingFactory2();
        }
    }

    private ID3D11Texture2D LoadImageAsFloat32Texture(string imagePath)
    {
        // 使用标准.NET方法加载图像，然后转换为D3D11纹理
        using var fileStream = new FileStream(imagePath, FileMode.Open, FileAccess.Read);
        using var decoder = _wicFactory.CreateDecoderFromStream(fileStream);
        using var frame = decoder.GetFrame(0);

        // 获取图像尺寸
        frame.GetSize(out var width, out var height);

        // 创建格式转换器，转换为RGBA32F格式
        using var converter = _wicFactory.CreateFormatConverter();
        converter.Initialize(frame, Vortice.WIC.PixelFormat.Format128bppRGBAFloat, BitmapDitherType.None, null, 0.0, BitmapPaletteType.Custom);

        // 计算步长
        var stride = (uint)(width * 16); // 4 channels * 4 bytes per channel
        var bufferSize = stride * height;

        // 创建缓冲区并复制像素数据
        var buffer = new byte[bufferSize];
        unsafe
        {
            fixed (byte* bufferPtr = buffer)
            {
                converter.CopyPixels(0u, stride, (nint)bufferPtr);
            }
        }

        // 创建D3D11纹理
        var textureDesc = new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.R32G32B32A32_Float,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource | BindFlags.UnorderedAccess,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = ResourceOptionFlags.None
        };

        unsafe
        {
            fixed (byte* bufferPtr = buffer)
            {
                var subresourceData = new SubresourceData
                {
                    DataPointer = (nint)bufferPtr,
                    RowPitch = (uint)stride,
                    SlicePitch = (uint)bufferSize
                };

                return _device.CreateTexture2D(textureDesc, new[] { subresourceData });
            }
        }
    }

    // 新增：支持{key}通配环境字典
    private string ReplacePlaceholders(string path, IScriptContext context)
    {
        if (string.IsNullOrEmpty(path) || context == null) return path;
        
        // 获取环境字典
        var processorEnv = context.GetType().GetProperty("Environment")?.GetValue(context) as Tunnel_Next.Services.ImageProcessing.ProcessorEnvironment;
        if (processorEnv?.EnvironmentDictionary != null)
        {
            foreach (var kv in processorEnv.EnvironmentDictionary)
            {
                var key = kv.Key;
                var value = kv.Value?.ToString() ?? string.Empty;
                path = Regex.Replace(path, $"\\{{{Regex.Escape(key)}\\}}", value, RegexOptions.IgnoreCase);
            }
            // 兼容常用占位符
            if (processorEnv.EnvironmentDictionary.TryGetValue("NodeGraphName", out var nodeGraphName))
                path = Regex.Replace(path, "\\{NodeGraphName\\}", nodeGraphName?.ToString() ?? string.Empty, RegexOptions.IgnoreCase);
            if (processorEnv.EnvironmentDictionary.TryGetValue("Index", out var index))
                path = Regex.Replace(path, "\\{Index\\}", index?.ToString() ?? string.Empty, RegexOptions.IgnoreCase);
        }
        return path;
    }



    public override Dictionary<string, object> InjectMetadata(Dictionary<string, object> currentMetadata)
    {
        // 注入所有环境字典内容为元数据
        var metadata = new Dictionary<string, object>(currentMetadata);
        if (!string.IsNullOrEmpty(ImagePath))
        {
            metadata["图像源地址"] = ImagePath;
        }
        // 补全与图像输出节点一致的占位符
        if (_lastContext != null)
        {
            var processorEnv = _lastContext.GetType().GetProperty("Environment")?.GetValue(_lastContext) as Tunnel_Next.Services.ImageProcessing.ProcessorEnvironment;
            if (processorEnv?.EnvironmentDictionary != null)
            {
                foreach (var kv in processorEnv.EnvironmentDictionary)
                {
                    metadata[kv.Key] = kv.Value;
                }
            }
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
            "/Tunnel-Next;component/Resources/ScriptsControls/TextBoxActivatedStyles.xaml"
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
        var viewModel = CreateViewModel() as ImageInputPlusViewModel;
        mainPanel.DataContext = viewModel;

        // 标题
        var titleLabel = new Label { Content = "图像输入+ (F32texture)" };
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
                Filter = "图像文件|*.bmp;*.jpg;*.jpeg;*.png;*.tiff|所有文件|*.*"
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

            // 使用WIC获取图像尺寸
            try
            {
                InitializeDeviceAndWIC();
                using var fileStream = new FileStream(imagePath, FileMode.Open, FileAccess.Read);
                using var decoder = _wicFactory.CreateDecoderFromStream(fileStream);
                using var frame = decoder.GetFrame(0);
                frame.GetSize(out var width, out var height);
                var pixelFormat = frame.PixelFormat;

                info += $"尺寸: {width} × {height}\n";
                info += $"像素格式: {GetPixelFormatName(pixelFormat)}";
            }
            catch (Exception wicEx)
            {
                info += $"无法读取图像信息: {wicEx.Message}";
            }

            infoTextBlock.Text = info;
        }
        catch (Exception ex)
        {
            infoTextBlock.Text = $"读取图像信息失败: {ex.Message}";
        }
    }

    private string GetPixelFormatName(Guid pixelFormat)
    {
        if (pixelFormat == Vortice.WIC.PixelFormat.Format32bppBGRA) return "BGRA32";
        if (pixelFormat == Vortice.WIC.PixelFormat.Format32bppRGBA) return "RGBA32";
        if (pixelFormat == Vortice.WIC.PixelFormat.Format24bppBGR) return "BGR24";
        if (pixelFormat == Vortice.WIC.PixelFormat.Format24bppRGB) return "RGB24";
        if (pixelFormat == Vortice.WIC.PixelFormat.Format128bppRGBAFloat) return "RGBA128F";
        return "未知格式";
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
            if (control.DataContext is ImageInputPlusViewModel viewModel)
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
            if (control.DataContext is ImageInputPlusViewModel viewModel)
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
        return new ImageInputPlusViewModel(this);
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

    /// <summary>
    /// 清理资源
    /// </summary>
    public void Dispose()
    {
        _deviceContext?.Dispose();
        _device?.Dispose();
        _wicFactory?.Dispose();
    }
}

public class ImageInputPlusViewModel : ScriptViewModelBase
{
    private ImageInputPlusScript ImageInputPlusScript => (ImageInputPlusScript)Script;
    private string _imageInfo = "未选择图像文件";

    public string ImagePath
    {
        get => ImageInputPlusScript.ImagePath;
        set
        {
            if (ImageInputPlusScript.ImagePath != value)
            {
                var oldValue = ImageInputPlusScript.ImagePath; // Capture old value for potential use
                ImageInputPlusScript.ImagePath = value;
                OnPropertyChanged(); // Notify UI bound to this ViewModel property

                // 更新图像信息
                UpdateImageInfo(value);

                // 确保使用TunnelExtensionScriptBase的OnParameterChanged通知主程序参数变化
                // 这将触发整个图像处理流水线的重新计算
                if (Script is TunnelExtensionScriptBase rsb)
                {
                    rsb.OnParameterChanged(nameof(ImagePath), value);
                }
                else
                {
                    // 仅作为备选方案，如果Script不是TunnelExtensionScriptBase类型
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

    public string NodeInstanceId => ImageInputPlusScript.NodeInstanceId;

    public ImageInputPlusViewModel(ImageInputPlusScript script) : base(script)
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

            // 使用WIC获取图像尺寸
            try
            {
                using var wicFactory = new IWICImagingFactory();
                using var fileStream = new FileStream(imagePath, FileMode.Open, FileAccess.Read);
                using var decoder = wicFactory.CreateDecoderFromStream(fileStream);
                using var frame = decoder.GetFrame(0);
                frame.GetSize(out var width, out var height);
                var pixelFormat = frame.PixelFormat;

                info += $"尺寸: {width} × {height}\n";
                info += $"像素格式: {GetPixelFormatName(pixelFormat)}";
            }
            catch (Exception wicEx)
            {
                info += $"无法读取图像信息: {wicEx.Message}";
            }

            ImageInfo = info;
        }
        catch (Exception ex)
        {
            ImageInfo = $"读取图像信息失败: {ex.Message}";
        }
    }

    private string GetPixelFormatName(Guid pixelFormat)
    {
        if (pixelFormat == Vortice.WIC.PixelFormat.Format32bppBGRA) return "BGRA32";
        if (pixelFormat == Vortice.WIC.PixelFormat.Format32bppRGBA) return "RGBA32";
        if (pixelFormat == Vortice.WIC.PixelFormat.Format24bppBGR) return "BGR24";
        if (pixelFormat == Vortice.WIC.PixelFormat.Format24bppRGB) return "RGB24";
        if (pixelFormat == Vortice.WIC.PixelFormat.Format128bppRGBAFloat) return "RGBA128F";
        return "未知格式";
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
