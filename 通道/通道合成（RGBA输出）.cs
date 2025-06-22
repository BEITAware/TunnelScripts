using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Tunnel_Next.Services.Scripting;
using OpenCvSharp;

[RevivalScript(
    Name = "通道合成（RGBA输出）",
    Author = "BEITAware",
    Description = "RGBA 通道合成：从四路输入图像分别提取 R/G/B/A 通道并合成一张 RGBA 图像",
    Version = "1.0",
    Category = "通道",
    Color = "#FF9933"
)]
public class ChannelCompositionRGBAScript : RevivalScriptBase
{
    public string NodeInstanceId { get; set; } = string.Empty;

    public override Dictionary<string, PortDefinition> GetInputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            ["channelR"] = new PortDefinition("ChannelR", false, "R通道（单通道）"),
            ["channelG"] = new PortDefinition("ChannelG", false, "G通道（单通道）"),
            ["channelB"] = new PortDefinition("ChannelB", false, "B通道（单通道）"),
            ["channelA"] = new PortDefinition("ChannelA", false, "A通道（单通道）")
        };
    }

    public override Dictionary<string, PortDefinition> GetOutputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            ["f32bmp"] = new PortDefinition("f32bmp", false, "合成的RGBA图像")
        };
    }

    public override Dictionary<string, object> Process(Dictionary<string, object> inputs, IScriptContext context)
    {
        // 检查所有输入是否有效
        if (!inputs.TryGetValue("channelR", out var inputR) || inputR == null ||
            !inputs.TryGetValue("channelG", out var inputG) || inputG == null ||
            !inputs.TryGetValue("channelB", out var inputB) || inputB == null ||
            !inputs.TryGetValue("channelA", out var inputA) || inputA == null)
        {
            return new Dictionary<string, object> { ["f32bmp"] = null };
        }

        if (!(inputR is Mat matR) || matR.Empty() ||
            !(inputG is Mat matG) || matG.Empty() ||
            !(inputB is Mat matB) || matB.Empty() ||
            !(inputA is Mat matA) || matA.Empty())
        {
            return new Dictionary<string, object> { ["f32bmp"] = null };
        }

        try
        {
            // 检查尺寸一致性
            if (!CheckSizeConsistency(matR, matG, matB, matA))
            {
                throw new ArgumentException("输入图像尺寸不一致");
            }

            // 执行通道合成操作
            Mat resultMat = PerformChannelComposition(matR, matG, matB, matA);

            return new Dictionary<string, object> { ["f32bmp"] = resultMat };
        }
        catch (Exception ex)
        {
            throw new ApplicationException($"通道合成（RGBA输出）节点处理失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 检查输入图像尺寸一致性
    /// </summary>
    private bool CheckSizeConsistency(Mat matR, Mat matG, Mat matB, Mat matA)
    {
        int height = matR.Height;
        int width = matR.Width;

        return matG.Height == height && matG.Width == width &&
               matB.Height == height && matB.Width == width &&
               matA.Height == height && matA.Width == width;
    }

    /// <summary>
    /// 执行通道合成操作
    /// </summary>
    private Mat PerformChannelComposition(Mat matR, Mat matG, Mat matB, Mat matA)
    {
        // 提取每个输入的单通道数据
        Mat channelR = ExtractSingleChannel(matR);
        Mat channelG = ExtractSingleChannel(matG);
        Mat channelB = ExtractSingleChannel(matB);
        Mat channelA = ExtractSingleChannel(matA);

        // 创建输出RGBA图像
        Mat resultMat = new Mat(matR.Size(), MatType.CV_32FC4);
        
        // 合并通道
        Mat[] channels = { channelR, channelG, channelB, channelA };
        Cv2.Merge(channels, resultMat);

        // 清理临时通道
        channelR.Dispose();
        channelG.Dispose();
        channelB.Dispose();
        channelA.Dispose();

        return resultMat;
    }

    /// <summary>
    /// 从输入图像提取单通道数据
    /// </summary>
    private Mat ExtractSingleChannel(Mat inputMat)
    {
        Mat singleChannel = new Mat();

        if (inputMat.Channels() == 1)
        {
            // 已经是单通道，直接转换为32位浮点
            inputMat.ConvertTo(singleChannel, MatType.CV_32F);
        }
        else
        {
            // 多通道图像，提取第一个通道
            Mat[] channels = Cv2.Split(inputMat);
            channels[0].ConvertTo(singleChannel, MatType.CV_32F);
            
            // 清理临时通道
            foreach (var ch in channels)
            {
                ch.Dispose();
            }
        }

        return singleChannel;
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
        var viewModel = CreateViewModel() as ChannelCompositionRGBAViewModel;
        mainPanel.DataContext = viewModel;

        // 标题
        var titleLabel = new Label { Content = "通道合成（RGBA输出）" };
        if (resources.Contains("TitleLabelStyle")) titleLabel.Style = resources["TitleLabelStyle"] as Style;
        mainPanel.Children.Add(titleLabel);

        // 说明文本
        var descriptionText = new TextBlock
        {
            Text = "将四路输入图像分别映射到R/G/B/A通道，合成RGBA图像。\n" +
                   "• 需要四路输入：channelR, channelG, channelB, channelA\n" +
                   "• 所有输入图像必须尺寸一致\n" +
                   "• 自动提取每个输入的第一个通道",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 5, 0, 5)
        };
        if (resources.Contains("StatusTextBlockStyle")) descriptionText.Style = resources["StatusTextBlockStyle"] as Style;
        mainPanel.Children.Add(descriptionText);

        return mainPanel;
    }

    public override IScriptViewModel CreateViewModel()
    {
        return new ChannelCompositionRGBAViewModel(this);
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

public class ChannelCompositionRGBAViewModel : ScriptViewModelBase
{
    public ChannelCompositionRGBAViewModel(ChannelCompositionRGBAScript script) : base(script)
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
