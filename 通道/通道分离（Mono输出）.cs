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
    Name = "通道分离（Mono输出）",
    Author = "BEITAware",
    Description = "将输入图像分离为四个独立单通道输出",
    Version = "1.0",
    Category = "通道",
    Color = "#4682B4"
)]
public class ChannelSeparationMonoScript : TunnelExtensionScriptBase
{
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
            ["ChannelR"] = new PortDefinition("ChannelR", false, "R通道（单通道）"),
            ["ChannelG"] = new PortDefinition("ChannelG", false, "G通道（单通道）"),
            ["ChannelB"] = new PortDefinition("ChannelB", false, "B通道（单通道）"),
            ["ChannelA"] = new PortDefinition("ChannelA", false, "A通道（单通道）")
        };
    }

    public override Dictionary<string, object> Process(Dictionary<string, object> inputs, IScriptContext context)
    {
        // 检查输入是否有效
        if (!inputs.TryGetValue("f32bmp", out var inputObj) || inputObj == null)
        {
            return new Dictionary<string, object> 
            { 
                ["ChannelR"] = null, 
                ["ChannelG"] = null, 
                ["ChannelB"] = null, 
                ["ChannelA"] = null 
            };
        }

        if (!(inputObj is Mat inputMat) || inputMat.Empty())
        {
            return new Dictionary<string, object> 
            { 
                ["ChannelR"] = null, 
                ["ChannelG"] = null, 
                ["ChannelB"] = null, 
                ["ChannelA"] = null 
            };
        }

        try
        {
            // 确保输入是RGBA格式
            Mat workingMat = EnsureRGBAFormat(inputMat);
            
            // 执行通道分离操作
            var channelResults = PerformChannelSeparation(workingMat);

            workingMat.Dispose();

            return channelResults;
        }
        catch (Exception ex)
        {
            throw new ApplicationException($"通道分离（Mono输出）节点处理失败: {ex.Message}", ex);
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
    /// 执行通道分离操作
    /// </summary>
    private Dictionary<string, object> PerformChannelSeparation(Mat inputMat)
    {
        // 分离通道
        Mat[] channels = Cv2.Split(inputMat);
        
        // 创建单通道输出（保持为单通道格式）
        Mat channelR = new Mat();
        Mat channelG = new Mat();
        Mat channelB = new Mat();
        Mat channelA = new Mat();

        // 转换为单通道32位浮点格式
        channels[0].ConvertTo(channelR, MatType.CV_32F);
        channels[1].ConvertTo(channelG, MatType.CV_32F);
        channels[2].ConvertTo(channelB, MatType.CV_32F);
        channels[3].ConvertTo(channelA, MatType.CV_32F);

        // 清理原始通道
        foreach (var ch in channels)
        {
            ch.Dispose();
        }

        return new Dictionary<string, object>
        {
            ["ChannelR"] = channelR,
            ["ChannelG"] = channelG,
            ["ChannelB"] = channelB,
            ["ChannelA"] = channelA
        };
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
            "/Tunnel-Next;component/Resources/ScriptsControls/TextBlockStyles.xaml"
        };
        foreach (var path in resourcePaths)
        {
            try { resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(path, UriKind.Relative) }); }
            catch { /* 静默处理 */ }
        }

        if (resources.Contains("MainPanelStyle")) mainPanel.Style = resources["MainPanelStyle"] as Style;

        // ViewModel
        var viewModel = CreateViewModel() as ChannelSeparationMonoViewModel;
        mainPanel.DataContext = viewModel;

        // 标题
        var titleLabel = new Label { Content = "通道分离（Mono输出）" };
        if (resources.Contains("TitleLabelStyle")) titleLabel.Style = resources["TitleLabelStyle"] as Style;
        mainPanel.Children.Add(titleLabel);

        // 说明文本
        var descriptionText = new TextBlock
        {
            Text = "将RGBA图像分离为4个独立的单通道输出",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 5, 0, 5)
        };
        if (resources.Contains("StatusTextBlockStyle")) descriptionText.Style = resources["StatusTextBlockStyle"] as Style;
        mainPanel.Children.Add(descriptionText);

        return mainPanel;
    }

    public override IScriptViewModel CreateViewModel()
    {
        return new ChannelSeparationMonoViewModel(this);
    }

    public override async Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
    {
        await Task.CompletedTask;
    }

    public override Dictionary<string, object> SerializeParameters()
    {
        return new Dictionary<string, object>
        {
            ["NodeInstanceId"] = NodeInstanceId
        };
    }

    public override void DeserializeParameters(Dictionary<string, object> data)
    {
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

public class ChannelSeparationMonoViewModel : ScriptViewModelBase
{
    public ChannelSeparationMonoViewModel(ChannelSeparationMonoScript script) : base(script)
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
        return new Dictionary<string, object>();
    }

    public override async Task SetParameterDataAsync(Dictionary<string, object> data)
    {
        await Task.CompletedTask;
    }

    public override async Task ResetToDefaultAsync()
    {
        await Task.CompletedTask;
    }

    public override void Dispose()
    {
        base.Dispose();
    }
}
