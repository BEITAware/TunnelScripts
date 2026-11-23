using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using Tunnel_Next.Services.Scripting;
using System.IO;
using Microsoft.Win32;
using OpenCvSharp;
using System.Text;
using Tunnel_Next.Services;

[TunnelExtensionScript(
    Name = "静态节点",
    Author = "BEITAware",
    Description = "加载.tsn静态节点文件并恢复其输出内容",
    Version = "1.0",
    Category = "输入输出",
    Color = "#7A6EFF"
)]
public class StaticNodeScript : TunnelExtensionScriptBase
{
    [ScriptParameter(DisplayName = "静态节点文件", Description = "要加载的.tsn静态节点文件路径", Order = 0)]
    public string FilePath { get; set; } = string.Empty;

    // 原始节点名称（从文件中读取）
    private string _originalNodeName = string.Empty;
    // 原始端口名称（从文件中读取）
    private string _originalPortName = string.Empty;
    // 原始数据类型（从文件中读取）
    private string _originalDataType = string.Empty;

    // 节点实例标识
    public string NodeInstanceId { get; set; } = string.Empty;

    public override Dictionary<string, PortDefinition> GetInputPorts()
    {
        // 静态节点不需要输入端口
        return new Dictionary<string, PortDefinition>();
    }

    public override Dictionary<string, PortDefinition> GetOutputPorts()
    {
        // 使用Any类型的输出端口，可以输出任何类型的数据
        return new Dictionary<string, PortDefinition>
        {
            ["output"] = new PortDefinition("any", false, "静态节点输出")
        };
    }

    public override Dictionary<string, object> Process(Dictionary<string, object> inputs, IScriptContext context)
    {
        if (string.IsNullOrEmpty(FilePath) || !File.Exists(FilePath))
        {
            throw new ArgumentException("请选择有效的静态节点文件(.tsn)");
        }

        try
        {
            // 加载静态节点文件
            var data = LoadStaticNodeFile(FilePath);
            
            // 返回解析出的数据
            return new Dictionary<string, object>
            {
                ["output"] = data
            };
        }
        catch (Exception ex)
        {
            throw new ApplicationException($"处理静态节点文件时发生错误: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 加载静态节点文件并解析内容
    /// </summary>
    private object LoadStaticNodeFile(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var reader = new BinaryReader(stream);
        var header = ReadField(reader);
        if (Encoding.ASCII.GetString(header) == "TSN1")
        {
            _originalNodeName = Encoding.UTF8.GetString(ReadField(reader));
            _originalPortName = Encoding.UTF8.GetString(ReadField(reader));
            _originalDataType = Encoding.UTF8.GetString(ReadField(reader));
            return LoadTSN1Format(reader);
        }

        var metadata = Encoding.UTF8.GetString(header).Split('\n');
        if (metadata.Length < 3)
            throw new InvalidDataException("无法解析静态节点元数据");
        _originalNodeName = metadata[0];
        _originalPortName = metadata[1];
        _originalDataType = metadata[2];
        return ReadMat(reader, hasChannelCount: false);
    }

    internal static byte[] ReadField(BinaryReader reader)
    {
        int length = reader.ReadInt32();
        if (length < 0 || length > reader.BaseStream.Length - reader.BaseStream.Position)
            throw new InvalidDataException("静态节点字段长度与文件不符");
        var data = reader.ReadBytes(length);
        if (data.Length != length)
            throw new EndOfStreamException();
        return data;
    }

    private static object LoadTSN1Format(BinaryReader reader)
    {
        return reader.ReadString() switch
        {
            "MAT" => ReadMat(reader, hasChannelCount: true),
            "BYTES" => ReadField(reader),
            "DOUBLE" => reader.ReadDouble(),
            "FLOAT" => reader.ReadSingle(),
            "INT" => reader.ReadInt32(),
            "BOOL" => reader.ReadBoolean(),
            "STRING" or "UNKNOWN" => Encoding.UTF8.GetString(ReadField(reader)),
            var tag => throw new InvalidDataException($"未知静态节点类型：{tag}")
        };
    }

    private static Mat ReadMat(BinaryReader reader, bool hasChannelCount)
    {
        int type = reader.ReadInt32();
        int rows = reader.ReadInt32();
        int cols = reader.ReadInt32();
        var matType = (MatType)type;
        int channels = hasChannelCount ? reader.ReadInt32() : matType.Channels;
        if (type < 0 || type > 4095 || channels != matType.Channels || rows <= 0 || cols <= 0)
            throw new InvalidDataException("静态节点图像的类型或尺寸无效");

        // 使用一个像素查询该 OpenCV 类型的实际字节数，再校验整个图像。
        using var sample = new Mat(1, 1, matType);
        long byteCount = checked((long)rows * cols * sample.ElemSize());
        var length = reader.ReadInt32();
        if (length != byteCount || length > reader.BaseStream.Length - reader.BaseStream.Position)
            throw new InvalidDataException("静态节点图像长度与尺寸不符");
        var bytes = reader.ReadBytes(length);
        if (bytes.Length != length)
            throw new EndOfStreamException();
        using var pixels = Mat.FromPixelData(rows, cols, matType, bytes);
        return pixels.Clone();
    }

    public override Dictionary<string, object> InjectMetadata(Dictionary<string, object> currentMetadata)
    {
        // 注入静态节点的元数据
        var metadata = new Dictionary<string, object>(currentMetadata);

        if (!string.IsNullOrEmpty(FilePath))
        {
            metadata["静态节点文件"] = FilePath;
        }

        if (!string.IsNullOrEmpty(_originalNodeName))
        {
            metadata["原始节点名称"] = _originalNodeName;
        }

        if (!string.IsNullOrEmpty(_originalPortName))
        {
            metadata["原始端口名称"] = _originalPortName;
        }

        if (!string.IsNullOrEmpty(_originalDataType))
        {
            metadata["原始数据类型"] = _originalDataType;
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
        var viewModel = CreateViewModel() as StaticNodeViewModel;
        mainPanel.DataContext = viewModel;

        // 标题
        var titleLabel = new Label { Content = "静态节点文件选择" };
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
        var pathBinding = new Binding("FilePath") { Mode = BindingMode.OneWay };
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
                Filter = "静态节点文件|*.tsn|所有文件|*.*"
            };
            
            // 设置初始目录为工作文件夹下的StaticNodes目录
            string initialDir = Path.Combine(viewModel.GetWorkFolder(), "Resources", "StaticNodes");
            if (Directory.Exists(initialDir))
            {
                openFileDialog.InitialDirectory = initialDir;
            }
            
            if (openFileDialog.ShowDialog() == true)
            {
                viewModel.FilePath = openFileDialog.FileName;
            }
        };
        mainPanel.Children.Add(selectFileButton);

        // 静态节点信息
        var infoLabel = new Label { Content = "静态节点信息:" };
        if (resources.Contains("DefaultLabelStyle"))
        {
            infoLabel.Style = resources["DefaultLabelStyle"] as Style;
        }
        mainPanel.Children.Add(infoLabel);

        var infoTextBlock = new TextBlock();
        var infoBinding = new Binding("StaticNodeInfo") { Mode = BindingMode.OneWay };
        infoTextBlock.SetBinding(TextBlock.TextProperty, infoBinding);
        mainPanel.Children.Add(infoTextBlock);

        return mainPanel;
    }

    public override IScriptViewModel CreateViewModel()
    {
        return new StaticNodeViewModel(this);
    }

    public override async Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
    {
        if (parameterName == nameof(FilePath))
        {
            FilePath = newValue?.ToString() ?? string.Empty;
            
            // 当文件路径更改时，尝试读取文件元数据
            if (!string.IsNullOrEmpty(FilePath) && File.Exists(FilePath))
            {
                try
                {
                    // 只读取元数据，不加载完整内容
                    ReadStaticNodeMetadata(FilePath);
                }
                catch (Exception)
                {
                    // 忽略读取错误
                }
            }
        }
        
        await Task.CompletedTask;
    }

    /// <summary>
    /// 只读取静态节点文件的元数据，不加载数据内容
    /// </summary>
    private void ReadStaticNodeMetadata(string filePath)
    {
        try
        {
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            using (var br = new BinaryReader(fs))
            {
                // 尝试读取文件标识符
                int headerLength = br.ReadInt32();
                byte[] headerBytes = br.ReadBytes(headerLength);
                string header = Encoding.ASCII.GetString(headerBytes);

                if (header == "TSN1")
                {
                    // 新版本格式
                    int nodeNameLength = br.ReadInt32();
                    byte[] nodeNameBytes = br.ReadBytes(nodeNameLength);
                    _originalNodeName = Encoding.UTF8.GetString(nodeNameBytes);

                    int portNameLength = br.ReadInt32();
                    byte[] portNameBytes = br.ReadBytes(portNameLength);
                    _originalPortName = Encoding.UTF8.GetString(portNameBytes);

                    int dataTypeLength = br.ReadInt32();
                    byte[] dataTypeBytes = br.ReadBytes(dataTypeLength);
                    _originalDataType = Encoding.UTF8.GetString(dataTypeBytes);
                }
                else
                {
                    // 旧版本格式
                    fs.Seek(0, SeekOrigin.Begin);
                    
                    byte[] lengthBytes = new byte[4];
                    fs.Read(lengthBytes, 0, 4);
                    int metadataLength = BitConverter.ToInt32(lengthBytes, 0);

                    byte[] metadataBytes = new byte[metadataLength];
                    fs.Read(metadataBytes, 0, metadataLength);
                    string metadata = Encoding.UTF8.GetString(metadataBytes);

                    string[] parts = metadata.Split('\n');
                    if (parts.Length >= 3)
                    {
                        _originalNodeName = parts[0];
                        _originalPortName = parts[1];
                        _originalDataType = parts[2];
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"读取静态节点元数据失败: {ex.Message}");
            _originalNodeName = "未知节点";
            _originalPortName = "未知端口";
            _originalDataType = "未知类型";
        }
    }

    /// <summary>
    /// 序列化参数
    /// </summary>
    public override Dictionary<string, object> SerializeParameters()
    {
        return new Dictionary<string, object>
        {
            [nameof(FilePath)] = FilePath,
            ["NodeInstanceId"] = NodeInstanceId,
            ["OriginalNodeName"] = _originalNodeName,
            ["OriginalPortName"] = _originalPortName,
            ["OriginalDataType"] = _originalDataType
        };
    }

    /// <summary>
    /// 反序列化参数
    /// </summary>
    public override void DeserializeParameters(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(FilePath), out var path))
        {
            FilePath = path?.ToString() ?? string.Empty;
        }

        if (data.TryGetValue("NodeInstanceId", out var nodeId))
        {
            NodeInstanceId = nodeId?.ToString() ?? string.Empty;
        }

        if (data.TryGetValue("OriginalNodeName", out var nodeName))
        {
            _originalNodeName = nodeName?.ToString() ?? string.Empty;
        }

        if (data.TryGetValue("OriginalPortName", out var portName))
        {
            _originalPortName = portName?.ToString() ?? string.Empty;
        }

        if (data.TryGetValue("OriginalDataType", out var dataType))
        {
            _originalDataType = dataType?.ToString() ?? string.Empty;
        }
    }

    /// <summary>
    /// 初始化节点实例ID
    /// </summary>
    public void InitializeNodeInstance(string nodeId)
    {
        if (!string.IsNullOrEmpty(nodeId))
        {
            NodeInstanceId = nodeId;
        }
    }
}

public class StaticNodeViewModel : ScriptViewModelBase
{
    private StaticNodeScript StaticNodeScript => (StaticNodeScript)Script;
    private string _staticNodeInfo = "未选择静态节点文件";
    private IScriptContext _context;

    public string FilePath
    {
        get => StaticNodeScript.FilePath;
        set
        {
            if (StaticNodeScript.FilePath != value)
            {
                var oldValue = StaticNodeScript.FilePath; // 保存旧值
                StaticNodeScript.FilePath = value;
                OnPropertyChanged(); // 通知UI绑定的属性已更新

                // 更新静态节点信息
                UpdateStaticNodeInfo(value);

                // 使用TunnelExtensionScriptBase的OnParameterChanged通知主程序参数变化
                if (Script is TunnelExtensionScriptBase rsb)
                {
                    rsb.OnParameterChanged(nameof(FilePath), value);
                }
                else
                {
                    // 备选方案
                    _ = Script.OnParameterChangedAsync(nameof(FilePath), oldValue, value);
                }
            }
        }
    }

    public string StaticNodeInfo
    {
        get => _staticNodeInfo;
        private set
        {
            if (_staticNodeInfo != value)
            {
                _staticNodeInfo = value;
                OnPropertyChanged();
            }
        }
    }

    public string NodeInstanceId => StaticNodeScript.NodeInstanceId;

    public StaticNodeViewModel(StaticNodeScript script) : base(script)
    {
        // 获取上下文
        _context = CreateDefaultContext();
        
        // 初始化静态节点信息
        UpdateStaticNodeInfo(script.FilePath);
    }

    /// <summary>
    /// 创建默认的脚本上下文
    /// </summary>
    private IScriptContext CreateDefaultContext()
    {
        return new DefaultScriptContext();
    }

    /// <summary>
    /// 获取工作文件夹路径
    /// </summary>
    public string GetWorkFolder()
    {
        return _context?.WorkFolder ?? Environment.CurrentDirectory;
    }

    /// <summary>
    /// 更新静态节点信息
    /// </summary>
    private void UpdateStaticNodeInfo(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            StaticNodeInfo = "未选择静态节点文件";
            return;
        }

        try
        {
            var fileInfo = new FileInfo(filePath);
            var info = $"文件名: {fileInfo.Name}\n";
            info += $"大小: {FormatFileSize(fileInfo.Length)}\n";
            info += $"修改时间: {fileInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}\n";

            // 尝试读取元数据
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            using (var br = new BinaryReader(fs))
            {
                try
                {
                    // 尝试读取文件标识符
                    var header = global::StaticNodeScript.ReadField(br);
                    if (Encoding.ASCII.GetString(header) == "TSN1")
                    {
                        string nodeName = Encoding.UTF8.GetString(global::StaticNodeScript.ReadField(br));
                        string portName = Encoding.UTF8.GetString(global::StaticNodeScript.ReadField(br));
                        string dataType = Encoding.UTF8.GetString(global::StaticNodeScript.ReadField(br));
                        info += $"原始节点: {nodeName}\n";
                        info += $"原始端口: {portName}\n";
                        info += $"数据类型: {dataType}";
                    }
                    else
                    {
                        string metadata = Encoding.UTF8.GetString(header);
                        string[] parts = metadata.Split('\n');
                        if (parts.Length >= 3)
                        {
                            info += $"原始节点: {parts[0]}\n";
                            info += $"原始端口: {parts[1]}\n";
                            info += $"数据类型: {parts[2]}";
                        }
                        else
                        {
                            info += "无法解析文件元数据";
                        }
                    }
                }
                catch (Exception)
                {
                    info += "无法读取文件元数据，可能是旧版格式或文件已损坏";
                }
            }

            StaticNodeInfo = info;
        }
        catch (Exception ex)
        {
            StaticNodeInfo = $"读取静态节点信息失败: {ex.Message}";
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
        // 参数变化已在属性setter中处理
        await Task.CompletedTask;
    }

    public override ScriptValidationResult ValidateParameter(string parameterName, object value)
    {
        // 简单验证
        return new ScriptValidationResult(true);
    }

    public override Dictionary<string, object> GetParameterData()
    {
        return new Dictionary<string, object>
        {
            [nameof(FilePath)] = FilePath
        };
    }

    public override async Task SetParameterDataAsync(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(FilePath), out var path))
        {
            FilePath = path?.ToString() ?? string.Empty;
        }
        await Task.CompletedTask;
    }

    public override async Task ResetToDefaultAsync()
    {
        FilePath = string.Empty;
        await Task.CompletedTask;
    }

    public override void Dispose()
    {
        base.Dispose();
    }
}

/// <summary>
/// 简单的默认脚本上下文实现
/// </summary>
internal class DefaultScriptContext : IScriptContext
{
    public string WorkFolder => Environment.CurrentDirectory;
    public string TempFolder => Path.Combine(Environment.CurrentDirectory, "temp");
    public string ScriptsFolder => Path.Combine(Environment.CurrentDirectory, "Scripts");
    public string? CurrentImagePath => null;
    public double ZoomLevel => 1.0;
    public double PreviewScrollX => 0;
    public double PreviewScrollY => 0;

    public Dictionary<string, object> GetNodeInputs(int nodeId) => new Dictionary<string, object>();
    public void SetNodeOutputs(int nodeId, Dictionary<string, object> outputs) { }
    public T? GetService<T>() where T : class => null;
    public void ShowMessage(string message, string title = "信息") { }
    public string? ShowFileDialog(string filter = "所有文件 (*.*)|*.*", string title = "选择文件") => null;
    public string? ShowSaveDialog(string filter = "所有文件 (*.*)|*.*", string title = "保存文件") => null;
    public void RequestPreviewRelease() { }
    public void RequestPreviewReattach() { }
} 