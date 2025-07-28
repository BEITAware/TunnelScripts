using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Tunnel_Next.Services.Scripting;
using OpenCvSharp;

[TunnelExtensionScript(
    Name = "反相",
    Author = "BEITAware",
    Description = "反相图像",
    Version = "1.0",
    Category = "数学",
    Color = "#FF00FF"
)]
public class InvertScript : TunnelExtensionScriptBase
{
    [ScriptParameter(DisplayName = "包含Alpha通道", Description = "是否对Alpha通道也进行取反操作", Order = 0)]
    public bool IncludeAlpha { get; set; } = false;

    public string NodeInstanceId { get; set; } = string.Empty;

    public override Dictionary<string, PortDefinition> GetInputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            ["f32bmp"] = new PortDefinition("f32bmp", false, "输入图像")
        };
    }

    public override Dictionary<string, PortDefinition> GetOutputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            ["f32bmp"] = new PortDefinition("f32bmp", false, "输出图像")
        };
    }

    public override Dictionary<string, object> Process(Dictionary<string, object> inputs, IScriptContext context)
    {
        // 检查输入是否有效
        if (!inputs.TryGetValue("f32bmp", out var inputObj) || inputObj == null)
        {
            return new Dictionary<string, object> { ["f32bmp"] = null };
        }

        if (!(inputObj is Mat inputMat) || inputMat.Empty())
        {
            return new Dictionary<string, object> { ["f32bmp"] = null };
        }

        try
        {
            // 确保输入是RGBA格式
            Mat workingMat = EnsureRGBAFormat(inputMat);
            
            // 执行取反操作
            Mat resultMat = PerformInvert(workingMat);

            return new Dictionary<string, object> { ["f32bmp"] = resultMat };
        }
        catch (Exception ex)
        {
            throw new ApplicationException($"取反节点处理失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 确保图像是RGBA格式
    /// </summary>
    private Mat EnsureRGBAFormat(Mat inputMat)
    {
        if (inputMat.Channels() == 4 && inputMat.Type() == MatType.CV_32FC4)
        {
            return inputMat.Clone();
        }

        Mat rgbaMat = new Mat();
        
        if (inputMat.Channels() == 3)
        {
            // RGB -> RGBA
            Cv2.CvtColor(inputMat, rgbaMat, ColorConversionCodes.RGB2RGBA);
        }
        else if (inputMat.Channels() == 1)
        {
            // 灰度 -> RGB -> RGBA
            Mat rgbMat = new Mat();
            Cv2.CvtColor(inputMat, rgbMat, ColorConversionCodes.GRAY2RGB);
            Cv2.CvtColor(rgbMat, rgbaMat, ColorConversionCodes.RGB2RGBA);
            rgbMat.Dispose();
        }
        else if (inputMat.Channels() == 4)
        {
            rgbaMat = inputMat.Clone();
        }
        else
        {
            throw new NotSupportedException($"不支持 {inputMat.Channels()} 通道的图像");
        }

        // 确保是32位浮点格式
        if (rgbaMat.Type() != MatType.CV_32FC4)
        {
            Mat floatMat = new Mat();
            rgbaMat.ConvertTo(floatMat, MatType.CV_32FC4, 1.0 / 255.0);
            rgbaMat.Dispose();
            return floatMat;
        }

        return rgbaMat;
    }

    /// <summary>
    /// 执行取反操作
    /// </summary>
    private Mat PerformInvert(Mat inputMat)
    {
        Mat resultMat = new Mat();
        
        if (IncludeAlpha)
        {
            // 对所有通道（包括Alpha）进行取反
            // 创建全1的矩阵
            Mat onesMat = Mat.Ones(inputMat.Size(), inputMat.Type());
            
            // 执行取反：result = 1.0 - input
            Cv2.Subtract(onesMat, inputMat, resultMat);
            
            onesMat.Dispose();
        }
        else
        {
            // 只对RGB通道取反，保留Alpha通道
            Mat[] channels = Cv2.Split(inputMat);
            
            // 对RGB通道取反
            for (int i = 0; i < 3; i++)
            {
                Mat onesMat = Mat.Ones(channels[i].Size(), channels[i].Type());
                Mat invertedChannel = new Mat();
                Cv2.Subtract(onesMat, channels[i], invertedChannel);
                channels[i].Dispose();
                channels[i] = invertedChannel;
                onesMat.Dispose();
            }
            
            // 重新合并通道（Alpha通道保持不变）
            Cv2.Merge(channels, resultMat);
            
            // 清理资源
            foreach (var ch in channels)
            {
                ch.Dispose();
            }
        }

        return resultMat;
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
            "/Tunnel-Next;component/Resources/ScriptsControls/CheckBoxStyles.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/TextBlockStyles.xaml"
        };
        foreach (var path in resourcePaths)
        {
            try { resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(path, UriKind.Relative) }); }
            catch { /* 静默处理 */ }
        }

        if (resources.Contains("MainPanelStyle")) mainPanel.Style = resources["MainPanelStyle"] as Style;

        // ViewModel
        var viewModel = CreateViewModel() as InvertViewModel;
        mainPanel.DataContext = viewModel;

        // 标题
        var titleLabel = new Label { Content = "像素取反" };
        if (resources.Contains("TitleLabelStyle")) titleLabel.Style = resources["TitleLabelStyle"] as Style;
        mainPanel.Children.Add(titleLabel);

        // Alpha通道选项
        var alphaCheckBox = new CheckBox
        {
            Content = "包含Alpha通道",
            Margin = new Thickness(0, 10, 0, 5),
        };
        if(resources.Contains("DefaultCheckBoxStyle")) alphaCheckBox.Style = resources["DefaultCheckBoxStyle"] as Style;
        alphaCheckBox.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(viewModel.IncludeAlpha)) { Mode = BindingMode.TwoWay });
        mainPanel.Children.Add(alphaCheckBox);

        // 说明文本
        var descriptionText = new TextBlock
        {
            Text = "勾选后将对Alpha通道也进行取反操作",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 5)
        };
        if (resources.Contains("StatusTextBlockStyle")) descriptionText.Style = resources["StatusTextBlockStyle"] as Style;
        mainPanel.Children.Add(descriptionText);

        return mainPanel;
    }

    public override IScriptViewModel CreateViewModel()
    {
        return new InvertViewModel(this);
    }

    public override async Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
    {
        // 参数变化处理
        await Task.CompletedTask;
    }

    public override Dictionary<string, object> SerializeParameters()
    {
        return new Dictionary<string, object>
        {
            [nameof(IncludeAlpha)] = IncludeAlpha,
            ["NodeInstanceId"] = NodeInstanceId
        };
    }

    public override void DeserializeParameters(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(IncludeAlpha), out var includeAlpha))
            IncludeAlpha = Convert.ToBoolean(includeAlpha);
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

public class InvertViewModel : ScriptViewModelBase
{
    private InvertScript InvertScript => (InvertScript)Script;

    public bool IncludeAlpha
    {
        get => InvertScript.IncludeAlpha;
        set
        {
            if (InvertScript.IncludeAlpha != value)
            {
                InvertScript.IncludeAlpha = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(IncludeAlpha), value);
            }
        }
    }

    public InvertViewModel(InvertScript script) : base(script)
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
            [nameof(IncludeAlpha)] = IncludeAlpha
        };
    }

    public override async Task SetParameterDataAsync(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(IncludeAlpha), out var includeAlpha))
            IncludeAlpha = Convert.ToBoolean(includeAlpha);
        await Task.CompletedTask;
    }

    public override async Task ResetToDefaultAsync()
    {
        IncludeAlpha = false;
        await Task.CompletedTask;
    }

    public override void Dispose()
    {
        base.Dispose();
    }
}
