using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Tunnel_Next.Services.Scripting;
using Microsoft.Win32;
using System.IO;
using System.Text;

[TunnelExtensionScript(
    Name = "写入文件",
    Author = "BEITAware",
    Description = "将任意数据写入到文件",
    Version = "1.0",
    Category = "输入输出",
    Color = "#E67E22"
)]
public class WriteFileScript : TunnelExtensionScriptBase
{
    [ScriptParameter(DisplayName = "文件路径", Description = "要保存的文件路径（包含扩展名）", Order = 0)]
    public string FilePath { get; set; } = string.Empty;

    public string NodeInstanceId { get; set; } = string.Empty;

    public override Dictionary<string, PortDefinition> GetInputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            ["data"] = new PortDefinition("Any", false, "要写入的数据")
        };
    }

    public override Dictionary<string, PortDefinition> GetOutputPorts()
    {
        // 写入文件节点没有输出端口
        return new Dictionary<string, PortDefinition>();
    }

    public override Dictionary<string, object> Process(Dictionary<string, object> inputs, IScriptContext context)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[写入文件] 开始处理，文件路径: {FilePath}");
            Console.WriteLine($"[写入文件] 开始处理，文件路径: {FilePath}");

            // 调试：显示所有输入键
            Console.WriteLine($"[写入文件] 输入键数量: {inputs.Count}");
            foreach (var kvp in inputs)
            {
                Console.WriteLine($"[写入文件] 输入键: '{kvp.Key}', 值类型: {kvp.Value?.GetType().Name ?? "null"}");
            }

            // 获取第一个（也是唯一的）输入数据，不依赖特定键名
            var inputData = inputs.Values.FirstOrDefault();
            if (inputData == null)
            {
                System.Diagnostics.Debug.WriteLine("[写入文件] 错误: 没有输入数据");
                Console.WriteLine("[写入文件] 错误: 没有输入数据");
                return new Dictionary<string, object>();
            }

            System.Diagnostics.Debug.WriteLine($"[写入文件] 输入数据类型: {inputData.GetType().Name}");
            Console.WriteLine($"[写入文件] 输入数据类型: {inputData.GetType().Name}");

            if (string.IsNullOrEmpty(FilePath))
            {
                System.Diagnostics.Debug.WriteLine("[写入文件] 错误: 文件路径为空");
                Console.WriteLine("[写入文件] 错误: 文件路径为空");
                return new Dictionary<string, object>();
            }

            WriteDataToFile(inputData, FilePath);

            System.Diagnostics.Debug.WriteLine("[写入文件] 处理完成");
            Console.WriteLine("[写入文件] 处理完成");

            return new Dictionary<string, object>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[写入文件] 处理异常: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[写入文件] 异常堆栈: {ex.StackTrace}");
            Console.WriteLine($"[写入文件] 处理异常: {ex.Message}");
            Console.WriteLine($"[写入文件] 异常堆栈: {ex.StackTrace}");
            throw;
        }
    }

    /// <summary>
    /// 将数据写入文件
    /// </summary>
    private void WriteDataToFile(object data, string path)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[写入文件] 准备写入文件: {path}");
            Console.WriteLine($"[写入文件] 准备写入文件: {path}");

            // 验证路径
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("文件路径不能为空");
            }

            // 确保目录存在
            var directory = Path.GetDirectoryName(path);
            System.Diagnostics.Debug.WriteLine($"[写入文件] 目标目录: {directory}");
            Console.WriteLine($"[写入文件] 目标目录: {directory}");

            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                System.Diagnostics.Debug.WriteLine($"[写入文件] 创建目录: {directory}");
                Console.WriteLine($"[写入文件] 创建目录: {directory}");
                Directory.CreateDirectory(directory);
            }

            // 检查数据类型并写入
            System.Diagnostics.Debug.WriteLine($"[写入文件] 数据类型: {data.GetType().FullName}");
            Console.WriteLine($"[写入文件] 数据类型: {data.GetType().FullName}");

            if (data is string stringData)
            {
                System.Diagnostics.Debug.WriteLine($"[写入文件] 写入字符串数据，长度: {stringData.Length}");
                Console.WriteLine($"[写入文件] 写入字符串数据，长度: {stringData.Length}");
                File.WriteAllText(path, stringData, Encoding.UTF8);
            }
            else if (data.GetType().Name == "Cube3DLut")
            {
                var stringContent = data.ToString();
                System.Diagnostics.Debug.WriteLine($"[写入文件] 写入Cube3DLut数据，转换后长度: {stringContent.Length}");
                Console.WriteLine($"[写入文件] 写入Cube3DLut数据，转换后长度: {stringContent.Length}");
                File.WriteAllText(path, stringContent, Encoding.UTF8);
            }
            else if (data is byte[] binaryData)
            {
                System.Diagnostics.Debug.WriteLine($"[写入文件] 写入二进制数据，长度: {binaryData.Length}");
                Console.WriteLine($"[写入文件] 写入二进制数据，长度: {binaryData.Length}");
                File.WriteAllBytes(path, binaryData);
            }
            else
            {
                var stringContent = data.ToString();
                System.Diagnostics.Debug.WriteLine($"[写入文件] 写入对象ToString数据，长度: {stringContent.Length}");
                Console.WriteLine($"[写入文件] 写入对象ToString数据，长度: {stringContent.Length}");
                File.WriteAllText(path, stringContent, Encoding.UTF8);
            }

            // 验证文件是否成功创建
            if (File.Exists(path))
            {
                var fileInfo = new FileInfo(path);
                System.Diagnostics.Debug.WriteLine($"[写入文件] 文件写入成功: {path}, 大小: {fileInfo.Length} 字节");
                Console.WriteLine($"[写入文件] 文件写入成功: {path}, 大小: {fileInfo.Length} 字节");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[写入文件] 警告: 文件写入后不存在: {path}");
                Console.WriteLine($"[写入文件] 警告: 文件写入后不存在: {path}");
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorMsg = $"文件访问权限不足: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[写入文件] 权限错误: {errorMsg}");
            Console.WriteLine($"[写入文件] 权限错误: {errorMsg}");
            throw new InvalidOperationException(errorMsg, ex);
        }
        catch (DirectoryNotFoundException ex)
        {
            var errorMsg = $"目录不存在: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[写入文件] 目录错误: {errorMsg}");
            Console.WriteLine($"[写入文件] 目录错误: {errorMsg}");
            throw new InvalidOperationException(errorMsg, ex);
        }
        catch (IOException ex)
        {
            var errorMsg = $"文件IO错误: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[写入文件] IO错误: {errorMsg}");
            Console.WriteLine($"[写入文件] IO错误: {errorMsg}");
            throw new InvalidOperationException(errorMsg, ex);
        }
        catch (Exception ex)
        {
            var errorMsg = $"写入文件失败: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[写入文件] 未知错误: {errorMsg}");
            System.Diagnostics.Debug.WriteLine($"[写入文件] 异常类型: {ex.GetType().Name}");
            System.Diagnostics.Debug.WriteLine($"[写入文件] 异常堆栈: {ex.StackTrace}");
            Console.WriteLine($"[写入文件] 未知错误: {errorMsg}");
            Console.WriteLine($"[写入文件] 异常类型: {ex.GetType().Name}");
            Console.WriteLine($"[写入文件] 异常堆栈: {ex.StackTrace}");
            throw new InvalidOperationException(errorMsg, ex);
        }
    }



    public override Dictionary<string, object> InjectMetadata(Dictionary<string, object> currentMetadata)
    {
        var metadata = new Dictionary<string, object>(currentMetadata);

        if (!string.IsNullOrEmpty(FilePath))
        {
            metadata["OutputPath"] = FilePath;
            metadata["OutputFileName"] = Path.GetFileName(FilePath);
            metadata["OutputDirectory"] = Path.GetDirectoryName(FilePath) ?? string.Empty;
            metadata["OutputExtension"] = Path.GetExtension(FilePath);
        }

        return metadata;
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
            "/Tunnel-Next;component/Resources/ScriptsControls/ScriptButtonStyles.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/CheckBoxStyles.xaml"
        };
        foreach (var path in resourcePaths)
        {
            try { resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(path, UriKind.Relative) }); }
            catch { /* 静默处理 */ }
        }

        if (resources.Contains("MainPanelStyle")) mainPanel.Style = resources["MainPanelStyle"] as Style;

        var viewModel = CreateViewModel() as WriteFileViewModel;
        mainPanel.DataContext = viewModel;

        // 标题
        var titleLabel = new Label { Content = "写入文件设置" };
        if (resources.Contains("TitleLabelStyle")) titleLabel.Style = resources["TitleLabelStyle"] as Style;
        mainPanel.Children.Add(titleLabel);

        CreateFilePathControls(mainPanel, viewModel, resources);

        return mainPanel;
    }

    /// <summary>
    /// 创建文件路径控件
    /// </summary>
    private void CreateFilePathControls(StackPanel mainPanel, WriteFileViewModel viewModel, ResourceDictionary resources)
    {
        var pathLabel = new Label { Content = "文件路径:" };
        if (resources.Contains("DefaultLabelStyle")) pathLabel.Style = resources["DefaultLabelStyle"] as Style;
        mainPanel.Children.Add(pathLabel);

        var pathTextBox = new TextBox { IsReadOnly = true, Margin = new Thickness(0, 0, 0, 5) };
        if (resources.Contains("DefaultTextBoxStyle")) pathTextBox.Style = resources["DefaultTextBoxStyle"] as Style;
        pathTextBox.SetBinding(TextBox.TextProperty, new Binding(nameof(viewModel.FilePath)) { Mode = BindingMode.OneWay });
        mainPanel.Children.Add(pathTextBox);

        var selectFileButton = new Button { Content = "选择保存路径", Margin = new Thickness(0, 0, 0, 10) };
        if (resources.Contains("SelectFileScriptButtonStyle")) selectFileButton.Style = resources["SelectFileScriptButtonStyle"] as Style;
        selectFileButton.Click += (s, e) =>
        {
            var saveDialog = new SaveFileDialog
            {
                Filter = "所有文件|*.*|文本文件|*.txt|CUBE LUT|*.cube",
                FileName = "output.txt"
            };

            if (saveDialog.ShowDialog() == true)
            {
                viewModel.FilePath = saveDialog.FileName;
            }
        };
        mainPanel.Children.Add(selectFileButton);
    }



    public override IScriptViewModel CreateViewModel()
    {
        return new WriteFileViewModel(this);
    }

    public override async Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
    {
        await Task.CompletedTask;
    }

    public override Dictionary<string, object> SerializeParameters()
    {
        return new Dictionary<string, object>
        {
            [nameof(FilePath)] = FilePath,
            ["NodeInstanceId"] = NodeInstanceId
        };
    }

    public override void DeserializeParameters(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(FilePath), out var filePath))
            FilePath = filePath?.ToString() ?? string.Empty;
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

public class WriteFileViewModel : ScriptViewModelBase
{
    private WriteFileScript WriteFileScript => (WriteFileScript)Script;

    public string FilePath
    {
        get => WriteFileScript.FilePath;
        set
        {
            if (WriteFileScript.FilePath != value)
            {
                WriteFileScript.FilePath = value ?? string.Empty;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(FilePath), value);
            }
        }
    }



    public WriteFileViewModel(WriteFileScript script) : base(script)
    {
    }

    private void NotifyParameterChanged(string parameterName, object value)
    {
        if (Script is TunnelExtensionScriptBase rsb)
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
        if (data.TryGetValue(nameof(FilePath), out var filePath))
            FilePath = filePath?.ToString() ?? string.Empty;
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
