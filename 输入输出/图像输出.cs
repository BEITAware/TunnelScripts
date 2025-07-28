using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Tunnel_Next.Services.Scripting;
using OpenCvSharp;
using Microsoft.Win32;
using System.Text.RegularExpressions; // add for placeholder replacement

[TunnelExtensionScript(
    Name = "图像输出",
    Author = "BEITAware",
    Description = "将图像保存到文件",
    Version = "1.0",
    Category = "输入输出",
    Color = "#FF6B6B"
)]
public class ImageOutputScript : TunnelExtensionScriptBase
{
    [ScriptParameter(DisplayName = "保存路径")]
    public string SavePath { get; set; } = string.Empty;

    // 处理节点实例标识
    public string NodeInstanceId { get; set; } = string.Empty;

    // 新增字段: 用于存储节点图名称（来自全局元数据）
    private string _nodeGraphName = string.Empty;
    // 新增字段: 用于存储序号
    private string _index = string.Empty;

    // 保存最后一次处理上下文的引用
    private IScriptContext _lastContext;

    [ScriptParameter(DisplayName = "图像质量", Description = "JPEG图像质量 (1-100)", Order = 1)]
    public int Quality { get; set; } = 95;

    [ScriptParameter(DisplayName = "自动保存", Description = "处理时自动保存图像", Order = 2)]
    public bool AutoSave { get; set; } = true;

    [ScriptParameter(DisplayName = "保存Alpha通道", Description = "是否保存透明度信息（仅PNG和TIFF支持）", Order = 3)]
    public bool SaveAlpha { get; set; } = true;

    public override Dictionary<string, PortDefinition> GetInputPorts()
    {
        // 图像输出节点只需要一个图像输入端口
        return new Dictionary<string, PortDefinition>
        {
            ["f32bmp"] = new PortDefinition("f32bmp", false, "输入图像")
        };
    }

    public override Dictionary<string, PortDefinition> GetOutputPorts()
    {
        // 图像输出节点不需要输出端口
        return new Dictionary<string, PortDefinition>();
    }

    public override Dictionary<string, object> Process(Dictionary<string, object> inputs, IScriptContext context)
    {
        if (!inputs.TryGetValue("f32bmp", out var inputObj) || inputObj is not Mat inputMat)
        {
            throw new ArgumentException("需要输入图像");
        }

        // 保存上下文引用以供后续方法使用
        _lastContext = context;

        // 解析占位符（如 {NodegraphName}）
        var resolvedPath = ReplacePlaceholders(SavePath);

        if (AutoSave && !string.IsNullOrEmpty(resolvedPath))
        {
            SaveImage(inputMat, resolvedPath);
        }

        // 图像输出节点不返回任何输出
        return new Dictionary<string, object>();
    }

    // 新增：替换保存路径中的通配符，支持所有环境字典key
    private string ReplacePlaceholders(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        // 支持ProcessorEnvironment.EnvironmentDictionary
        if (_lastContext != null)
        {
            var processorEnv = _lastContext.GetType().GetProperty("Environment")?.GetValue(_lastContext) as Tunnel_Next.Services.ImageProcessing.ProcessorEnvironment;
            if (processorEnv?.EnvironmentDictionary != null)
            {
                foreach (var kv in processorEnv.EnvironmentDictionary)
                {
                    var key = kv.Key;
                    var value = kv.Value?.ToString() ?? string.Empty;
                    path = System.Text.RegularExpressions.Regex.Replace(path, $"\\{{{System.Text.RegularExpressions.Regex.Escape(key)}\\}}", value, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                }
            }
        }
        // 兼容旧有字段
        if (!string.IsNullOrEmpty(_nodeGraphName))
        {
            path = System.Text.RegularExpressions.Regex.Replace(path, "\\{NodegraphName\\}", _nodeGraphName, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        if (!string.IsNullOrEmpty(_index))
        {
            path = System.Text.RegularExpressions.Regex.Replace(path, "\\{Index\\}", _index, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        return path;
    }

    /// <summary>
    /// 提取上游元数据，获取节点图名称
    /// </summary>
    public override void ExtractMetadata(Dictionary<string, object> upstreamMetadata)
    {
        if (upstreamMetadata != null)
        {
            if (upstreamMetadata.TryGetValue("节点图名称", out var nameObj))
            {
                _nodeGraphName = nameObj?.ToString() ?? string.Empty;
            }

            if (upstreamMetadata.TryGetValue("序号", out var indexObj) || upstreamMetadata.TryGetValue("Index", out indexObj))
            {
                _index = indexObj?.ToString() ?? string.Empty;
            }
        }
    }

    private void SaveImage(Mat inputMat, string path)
    {
        try
        {
            // 确保目录存在
            var directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }

            // 根据文件扩展名和Alpha支持情况转换图像
            var extension = System.IO.Path.GetExtension(path).ToLower();
            var supportsAlpha = extension == ".png" || extension == ".tiff" || extension == ".tif";


            var outputMat = ConvertFromRGBA32F(inputMat, supportsAlpha && SaveAlpha);

            // 根据文件扩展名设置保存参数
            var saveParams = new int[0];

            if (extension == ".jpg" || extension == ".jpeg")
            {
                saveParams = new int[] { (int)ImwriteFlags.JpegQuality, Quality };
            }
            else if (extension == ".png")
            {
                saveParams = new int[] { (int)ImwriteFlags.PngCompression, 9 };
            }

            Cv2.ImWrite(path, outputMat, saveParams);


            outputMat.Dispose();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"保存图像失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 将32位浮点RGBA格式转换为适合保存的格式
    /// 根据目标格式决定是否保留Alpha通道
    /// </summary>
    private Mat ConvertFromRGBA32F(Mat inputMat, bool preserveAlpha)
    {
        var outputMat = new Mat();


        if (preserveAlpha && inputMat.Channels() == 4)
        {
            // 保留Alpha通道，转换为8位RGBA
            inputMat.ConvertTo(outputMat, MatType.CV_8UC4, 255.0);
        }
        else
        {
            // 不保留Alpha通道或输入不是4通道，转换为8位RGB

            if (inputMat.Channels() == 4)
            {
                // 从RGBA提取RGB
                var rgbMat = new Mat();
                Cv2.CvtColor(inputMat, rgbMat, ColorConversionCodes.RGBA2RGB);
                rgbMat.ConvertTo(outputMat, MatType.CV_8UC3, 255.0);
                rgbMat.Dispose();
            }
            else
            {
                // 直接转换（虽然理论上输入应该总是4通道）
                inputMat.ConvertTo(outputMat, MatType.CV_8UC3, 255.0);
            }
        }

        return outputMat;
    }

    public override Dictionary<string, object> InjectMetadata(Dictionary<string, object> currentMetadata)
    {
        // 向元数据注入输出路径信息
        var metadata = new Dictionary<string, object>(currentMetadata);

        if (!string.IsNullOrEmpty(SavePath))
        {
            metadata["OutputPath"] = SavePath;
            metadata["OutputFileName"] = System.IO.Path.GetFileName(SavePath);
            metadata["OutputDirectory"] = System.IO.Path.GetDirectoryName(SavePath) ?? string.Empty;
            metadata["OutputExtension"] = System.IO.Path.GetExtension(SavePath);
            metadata["Quality"] = Quality;
            metadata["AutoSave"] = AutoSave;
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
            "/Tunnel-Next;component/Resources/ScriptsControls/SliderStyles.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/CheckBoxStyles.xaml"
        };
        foreach (var path in resourcePaths)
        {
            try { resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(path, UriKind.Relative) }); }
            catch { /* 静默处理 */ }
        }

        if (resources.Contains("MainPanelStyle")) mainPanel.Style = resources["MainPanelStyle"] as Style;

        var viewModel = CreateViewModel() as ImageOutputViewModel;
        mainPanel.DataContext = viewModel;

        var titleLabel = new Label { Content = "图像输出设置" };
        if (resources.Contains("TitleLabelStyle")) titleLabel.Style = resources["TitleLabelStyle"] as Style;
        mainPanel.Children.Add(titleLabel);

        CreateSavePathControls(mainPanel, viewModel, resources);
        CreateQualityControls(mainPanel, viewModel, resources);
        CreateAutoSaveControls(mainPanel, viewModel, resources);
        CreateSaveAlphaControls(mainPanel, viewModel, resources);

        return mainPanel;
    }

    private void CreateSavePathControls(StackPanel parent, ImageOutputViewModel viewModel, ResourceDictionary resources)
    {
        var pathLabel = new Label { Content = "保存路径:" };
        if (resources.Contains("DefaultLabelStyle")) pathLabel.Style = resources["DefaultLabelStyle"] as Style;
        parent.Children.Add(pathLabel);

        var pathPanel = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };

        var selectButton = new Button { Content = "选择...", Width = 60, Margin = new Thickness(5, 0, 0, 0) };
        if (resources.Contains("SelectFileScriptButtonStyle")) selectButton.Style = resources["SelectFileScriptButtonStyle"] as Style;
        
        selectButton.Click += (s, e) =>
        {
            var saveFileDialog = new SaveFileDialog
            {
                Filter = "PNG Image|*.png|JPEG Image|*.jpg|TIFF Image|*.tiff|All files|*.*",
                FileName = viewModel.SavePath
            };
            if (saveFileDialog.ShowDialog() == true)
            {
                viewModel.SavePath = saveFileDialog.FileName;
            }
        };
        DockPanel.SetDock(selectButton, Dock.Right);

        var pathTextBox = new TextBox { Margin = new Thickness(0) };
        if (resources.Contains("DefaultTextBoxStyle")) pathTextBox.Style = resources["DefaultTextBoxStyle"] as Style;
        pathTextBox.SetBinding(TextBox.TextProperty, new Binding(nameof(viewModel.SavePath)) { Mode = BindingMode.TwoWay });
        
        pathPanel.Children.Add(selectButton);
        pathPanel.Children.Add(pathTextBox);
        parent.Children.Add(pathPanel);
    }

    private void CreateQualityControls(StackPanel parent, ImageOutputViewModel viewModel, ResourceDictionary resources)
    {
        var qualityLabel = new Label();
        if (resources.Contains("DefaultLabelStyle")) qualityLabel.Style = resources["DefaultLabelStyle"] as Style;
        
        var qualityBinding = new Binding(nameof(viewModel.QualityText)) { Source = viewModel };
        qualityLabel.SetBinding(ContentControl.ContentProperty, qualityBinding);
        parent.Children.Add(qualityLabel);

        var qualitySlider = new Slider
        {
            Minimum = 1,
            Maximum = 100,
            Margin = new Thickness(0, 0, 0, 10)
        };
        if (resources.Contains("DefaultSliderStyle")) qualitySlider.Style = resources["DefaultSliderStyle"] as Style;
        qualitySlider.SetBinding(Slider.ValueProperty, new Binding(nameof(viewModel.Quality)) { Mode = BindingMode.TwoWay });
        parent.Children.Add(qualitySlider);
    }

    private void CreateAutoSaveControls(StackPanel parent, ImageOutputViewModel viewModel, ResourceDictionary resources)
    {
        var autoSaveCheckBox = new CheckBox
        {
            Content = "自动保存",
            Margin = new Thickness(0, 5, 0, 10)
        };
        if (resources.Contains("DefaultCheckBoxStyle")) autoSaveCheckBox.Style = resources["DefaultCheckBoxStyle"] as Style;
        autoSaveCheckBox.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(viewModel.AutoSave)) { Mode = BindingMode.TwoWay });
        parent.Children.Add(autoSaveCheckBox);
    }

    private void CreateSaveAlphaControls(StackPanel parent, ImageOutputViewModel viewModel, ResourceDictionary resources)
    {
        var saveAlphaCheckBox = new CheckBox
        {
            Content = "保存Alpha通道 (PNG/TIFF)",
            Margin = new Thickness(0, 0, 0, 10)
        };
        if (resources.Contains("DefaultCheckBoxStyle")) saveAlphaCheckBox.Style = resources["DefaultCheckBoxStyle"] as Style;
        saveAlphaCheckBox.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(viewModel.SaveAlpha)) { Mode = BindingMode.TwoWay });
        parent.Children.Add(saveAlphaCheckBox);
    }

    public override IScriptViewModel CreateViewModel()
    {
        return new ImageOutputViewModel(this);
    }

    public override async Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
    {
        if (parameterName == nameof(SavePath))
        {
            SavePath = newValue?.ToString() ?? string.Empty;
        }
        else if (parameterName == nameof(Quality))
        {
            if (int.TryParse(newValue?.ToString(), out var quality))
                Quality = Math.Clamp(quality, 1, 100);
        }
        else if (parameterName == nameof(AutoSave))
        {
            if (bool.TryParse(newValue?.ToString(), out var autoSave))
                AutoSave = autoSave;
        }
        else if (parameterName == nameof(SaveAlpha))
        {
            if (bool.TryParse(newValue?.ToString(), out var saveAlpha))
                SaveAlpha = saveAlpha;
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
            [nameof(SavePath)] = SavePath,
            [nameof(Quality)] = Quality,
            [nameof(AutoSave)] = AutoSave,
            [nameof(SaveAlpha)] = SaveAlpha
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
        if (data.TryGetValue(nameof(SavePath), out var path))
        {
            SavePath = path?.ToString() ?? string.Empty;
        }

        if (data.TryGetValue(nameof(Quality), out var quality) && int.TryParse(quality?.ToString(), out var q))
        {
            Quality = Math.Clamp(q, 1, 100);
        }

        if (data.TryGetValue(nameof(AutoSave), out var autoSave) && bool.TryParse(autoSave?.ToString(), out var a))
        {
            AutoSave = a;
        }

        if (data.TryGetValue(nameof(SaveAlpha), out var saveAlpha) && bool.TryParse(saveAlpha?.ToString(), out var sa))
        {
            SaveAlpha = sa;
        }
    }
}

public class ImageOutputViewModel : ScriptViewModelBase
{
    private ImageOutputScript ImageOutputScript => (ImageOutputScript)Script;

    public string SavePath
    {
        get => ImageOutputScript.SavePath;
        set
        {
            if (ImageOutputScript.SavePath != value)
            {
                ImageOutputScript.SavePath = value;
                OnPropertyChanged();

                // 直接触发参数变化
                if (Script is TunnelExtensionScriptBase rsb)
                {
                    rsb.OnParameterChanged(nameof(SavePath), value);
                }
            }
        }
    }

    public int Quality
    {
        get => ImageOutputScript.Quality;
        set
        {
            var clampedValue = Math.Clamp(value, 1, 100);
            if (ImageOutputScript.Quality != clampedValue)
            {
                ImageOutputScript.Quality = clampedValue;
                OnPropertyChanged();
                OnPropertyChanged(nameof(QualityText)); // 同时更新QualityText

                // 直接触发参数变化
                if (Script is TunnelExtensionScriptBase rsb)
                {
                    rsb.OnParameterChanged(nameof(Quality), clampedValue);
                }
            }
        }
    }

    public string QualityText => $"图像质量: {Quality}";

    public bool AutoSave
    {
        get => ImageOutputScript.AutoSave;
        set
        {
            if (ImageOutputScript.AutoSave != value)
            {
                ImageOutputScript.AutoSave = value;
                OnPropertyChanged();

                // 直接触发参数变化
                if (Script is TunnelExtensionScriptBase rsb)
                {
                    rsb.OnParameterChanged(nameof(AutoSave), value);
                }
            }
        }
    }

    public bool SaveAlpha
    {
        get => ImageOutputScript.SaveAlpha;
        set
        {
            if (ImageOutputScript.SaveAlpha != value)
            {
                ImageOutputScript.SaveAlpha = value;
                OnPropertyChanged();

                // 直接触发参数变化
                if (Script is TunnelExtensionScriptBase rsb)
                {
                    rsb.OnParameterChanged(nameof(SaveAlpha), value);
                }
            }
        }
    }

    public ImageOutputViewModel(ImageOutputScript script) : base(script) { }

    public override async Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
    {
        // 参数变化已经在属性setter中处理，这里不需要额外处理
        await Task.CompletedTask;
    }

    public override ScriptValidationResult ValidateParameter(string parameterName, object value)
    {
        switch (parameterName)
        {
            case nameof(SavePath):
                var path = value?.ToString();
                if (string.IsNullOrEmpty(path))
                    return new ScriptValidationResult(false, "请指定保存路径");
                break;
            case nameof(Quality):
                if (!int.TryParse(value?.ToString(), out var quality) || quality < 1 || quality > 100)
                    return new ScriptValidationResult(false, "质量值必须在1-100之间");
                break;
        }
        return new ScriptValidationResult(true);
    }

    public override Dictionary<string, object> GetParameterData()
    {
        return new Dictionary<string, object>
        {
            [nameof(SavePath)] = SavePath,
            [nameof(Quality)] = Quality,
            [nameof(AutoSave)] = AutoSave,
            [nameof(SaveAlpha)] = SaveAlpha
        };
    }

    public override async Task SetParameterDataAsync(Dictionary<string, object> data)
    {
        await RunOnUIThreadAsync(() =>
        {
            if (data.TryGetValue(nameof(SavePath), out var path))
                SavePath = path?.ToString() ?? string.Empty;

            if (data.TryGetValue(nameof(Quality), out var quality) && int.TryParse(quality?.ToString(), out var q))
                Quality = q;

            if (data.TryGetValue(nameof(AutoSave), out var autoSave) && bool.TryParse(autoSave?.ToString(), out var a))
                AutoSave = a;

            if (data.TryGetValue(nameof(SaveAlpha), out var saveAlpha) && bool.TryParse(saveAlpha?.ToString(), out var sa))
                SaveAlpha = sa;
        });
    }

    public override async Task ResetToDefaultAsync()
    {
        await RunOnUIThreadAsync(() =>
        {
            SavePath = string.Empty;
            Quality = 95;
            AutoSave = true;
            SaveAlpha = true;
        });
    }
}
