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
    Name = "布尔取反",
    Author = "BEITAware",
    Description = "布尔取反：先将输入图像转灰度并二值化 (>0→1, ≤0→0)，然后将 1→0、0→1",
    Version = "1.0",
    Category = "数学",
    Color = "#AA33CC"
)]
public class BooleanInvertScript : TunnelExtensionScriptBase
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
            
            // 执行布尔取反操作
            Mat resultMat = PerformBooleanInvert(workingMat);

            return new Dictionary<string, object> { ["f32bmp"] = resultMat };
        }
        catch (Exception ex)
        {
            throw new ApplicationException($"布尔取反节点处理失败: {ex.Message}", ex);
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
    /// 执行布尔取反操作
    /// </summary>
    private Mat PerformBooleanInvert(Mat inputMat)
    {
        // 分离通道
        Mat[] channels = Cv2.Split(inputMat);
        
        // 转换为灰度（忽略Alpha通道）
        Mat grayMat = new Mat();
        using (var rgbMat = new Mat())
        {
            Cv2.Merge(new Mat[] { channels[0], channels[1], channels[2] }, rgbMat);
            Cv2.CvtColor(rgbMat, grayMat, ColorConversionCodes.RGB2GRAY);
        }

        // 二值化 (>0→1, ≤0→0)
        Mat binaryMat = new Mat();
        Cv2.Threshold(grayMat, binaryMat, 0, 1.0, ThresholdTypes.Binary);

        // 取反 (1→0, 0→1)
        Mat invertedMat = new Mat();
        Mat onesMat = Mat.Ones(binaryMat.Size(), binaryMat.Type());
        Cv2.Subtract(onesMat, binaryMat, invertedMat);

        // 创建输出RGBA图像
        Mat resultMat = new Mat(inputMat.Size(), MatType.CV_32FC4);
        Mat[] resultChannels = new Mat[4];
        
        // 将灰度结果复制到RGB通道
        resultChannels[0] = invertedMat.Clone();
        resultChannels[1] = invertedMat.Clone();
        resultChannels[2] = invertedMat.Clone();
        resultChannels[3] = Mat.Ones(invertedMat.Size(), invertedMat.Type()); // Alpha通道设为1

        Cv2.Merge(resultChannels, resultMat);

        // 清理资源
        foreach (var ch in channels) ch.Dispose();
        foreach (var ch in resultChannels) ch.Dispose();
        grayMat.Dispose();
        binaryMat.Dispose();
        invertedMat.Dispose();
        onesMat.Dispose();

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
            "/Tunnel-Next;component/Resources/ScriptsControls/TextBlockStyles.xaml"
        };
        foreach (var path in resourcePaths)
        {
            try { resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(path, UriKind.Relative) }); }
            catch { /* 静默处理 */ }
        }

        if (resources.Contains("MainPanelStyle")) mainPanel.Style = resources["MainPanelStyle"] as Style;

        // ViewModel
        var viewModel = CreateViewModel() as BooleanInvertViewModel;
        mainPanel.DataContext = viewModel;

        // 标题
        var titleLabel = new Label { Content = "布尔取反" };
        if (resources.Contains("TitleLabelStyle")) titleLabel.Style = resources["TitleLabelStyle"] as Style;
        mainPanel.Children.Add(titleLabel);

        // 说明文本
        var descriptionText = new TextBlock
        {
            Text = "将图像转为灰度并二值化，然后进行布尔取反操作",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 5, 0, 5)
        };
        if (resources.Contains("StatusTextBlockStyle")) descriptionText.Style = resources["StatusTextBlockStyle"] as Style;
        mainPanel.Children.Add(descriptionText);

        return mainPanel;
    }

    public override IScriptViewModel CreateViewModel()
    {
        return new BooleanInvertViewModel(this);
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

public class BooleanInvertViewModel : ScriptViewModelBase
{
    public BooleanInvertViewModel(BooleanInvertScript script) : base(script)
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
