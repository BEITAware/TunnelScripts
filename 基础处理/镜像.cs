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
    Name = "镜像",
    Author = "BEITAware",
    Description = "左右镜像图像，支持Alpha通道，可选自适应边界裁切",
    Version = "1.0",
    Category = "几何",
    Color = "#4A90E2"
)]
public class MirrorScript : TunnelExtensionScriptBase
{
    [ScriptParameter(DisplayName = "镜像方向", Description = "选择镜像方向", Order = 0)]
    public string Direction { get; set; } = "水平";

    [ScriptParameter(DisplayName = "自适应边界", Description = "自动清理透明的Alpha多余边界", Order = 1)]
    public bool AdaptiveBoundary { get; set; } = false;

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
            ["f32bmp"] = new PortDefinition("f32bmp", false, "输出图像 (普通)") ,
            ["f32bmp_mirror"] = new PortDefinition("f32bmp_mirror", false, "仅镜像部分")
        };
    }

    public override Dictionary<string, object> Process(Dictionary<string, object> inputs, IScriptContext context)
    {
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
            Mat workingMat = EnsureRGBAFormat(inputMat);

            // 根据方向选择镜像方式
            FlipMode flipMode = Direction switch
            {
                "水平" => FlipMode.Y,
                "垂直" => FlipMode.X,
                "水平+垂直" => FlipMode.XY,
                _ => FlipMode.Y
            };

            Mat mirroredMat = new Mat();
            Cv2.Flip(workingMat, mirroredMat, flipMode);
            workingMat.Dispose();

            // 生成仅镜像部分输出
            Mat mirrorOnly = new Mat(mirroredMat.Size(), mirroredMat.Type(), new Scalar(0, 0, 0, 0));
            int halfW = mirroredMat.Cols / 2;
            int halfH = mirroredMat.Rows / 2;
            switch (Direction)
            {
                case "水平":
                    {
                        OpenCvSharp.Rect rightRect = new OpenCvSharp.Rect(halfW, 0, mirroredMat.Cols - halfW, mirroredMat.Rows);
                        using var src = mirroredMat.SubMat(rightRect);
                        using var dst = mirrorOnly.SubMat(rightRect);
                        src.CopyTo(dst);
                        break;
                    }
                case "垂直":
                    {
                        OpenCvSharp.Rect bottomRect = new OpenCvSharp.Rect(0, halfH, mirroredMat.Cols, mirroredMat.Rows - halfH);
                        using var src = mirroredMat.SubMat(bottomRect);
                        using var dst = mirrorOnly.SubMat(bottomRect);
                        src.CopyTo(dst);
                        break;
                    }
                case "水平+垂直":
                    {
                        OpenCvSharp.Rect brRect = new OpenCvSharp.Rect(halfW, halfH, mirroredMat.Cols - halfW, mirroredMat.Rows - halfH);
                        using var src = mirroredMat.SubMat(brRect);
                        using var dst = mirrorOnly.SubMat(brRect);
                        src.CopyTo(dst);
                        break;
                    }
            }

            if (AdaptiveBoundary)
            {
                Mat trimmedFull = TrimTransparentBorders(mirroredMat);
                mirroredMat.Dispose();
                mirroredMat = trimmedFull;

                Mat trimmedMirror = TrimTransparentBorders(mirrorOnly);
                mirrorOnly.Dispose();
                mirrorOnly = trimmedMirror;
            }

            return new Dictionary<string, object>
            {
                ["f32bmp"] = mirroredMat,
                ["f32bmp_mirror"] = mirrorOnly
            };
        }
        catch (Exception ex)
        {
            throw new ApplicationException($"镜像节点处理失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 确保图像为 RGBA 32F 格式
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
            Cv2.CvtColor(inputMat, rgbaMat, ColorConversionCodes.RGB2RGBA);
        }
        else if (inputMat.Channels() == 1)
        {
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
    /// 裁切掉四周完全透明的区域
    /// </summary>
    private Mat TrimTransparentBorders(Mat rgbaMat)
    {
        Mat[] channels = Cv2.Split(rgbaMat);
        Mat alpha = channels[3];

        // 将Alpha通道转换为8位，用于阈值和查找非零像素
        Mat alpha8U = new Mat();
        alpha.ConvertTo(alpha8U, MatType.CV_8UC1, 255.0);

        Mat thresh = new Mat();
        Cv2.Threshold(alpha8U, thresh, 0, 255, ThresholdTypes.Binary);

        using var nonZeroMat = new Mat();
        Cv2.FindNonZero(thresh, nonZeroMat);

        // 释放临时资源
        alpha8U.Dispose();
        thresh.Dispose();
        foreach (var ch in channels) ch.Dispose();

        if (nonZeroMat.Empty())
        {
            // 图像完全透明，直接返回原图
            return rgbaMat;
        }

        OpenCvSharp.Rect bounding = Cv2.BoundingRect(nonZeroMat);
        Mat cropped = new Mat(rgbaMat, bounding).Clone();

        return cropped;
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
            "/Tunnel-Next;component/Resources/ScriptsControls/ComboBoxStyles.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/TextBlockStyles.xaml"
        };
        foreach (var path in resourcePaths)
        {
            try { resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(path, UriKind.Relative) }); }
            catch { /* 静默处理 */ }
        }

        if (resources.Contains("MainPanelStyle")) mainPanel.Style = resources["MainPanelStyle"] as Style;

        var viewModel = CreateViewModel() as MirrorViewModel;
        mainPanel.DataContext = viewModel;

        var titleLabel = new Label { Content = "镜像" };
        if (resources.Contains("TitleLabelStyle")) titleLabel.Style = resources["TitleLabelStyle"] as Style;
        mainPanel.Children.Add(titleLabel);

        // 方向选择
        var directionLabel = new Label { Content = "镜像方向:" };
        if (resources.Contains("DefaultLabelStyle")) directionLabel.Style = resources["DefaultLabelStyle"] as Style;
        mainPanel.Children.Add(directionLabel);

        var directionComboBox = new ComboBox { Margin = new Thickness(0, 0, 0, 10) };
        if (resources.Contains("DefaultComboBoxStyle")) directionComboBox.Style = resources["DefaultComboBoxStyle"] as Style;
        directionComboBox.Items.Add("水平");
        directionComboBox.Items.Add("垂直");
        directionComboBox.Items.Add("水平+垂直");
        directionComboBox.SetBinding(ComboBox.SelectedItemProperty, new Binding(nameof(viewModel.Direction)) { Mode = BindingMode.TwoWay });
        mainPanel.Children.Add(directionComboBox);

        var adaptiveCheckBox = new CheckBox
        {
            Content = "自适应边界",
            Margin = new Thickness(0, 10, 0, 5)
        };
        if (resources.Contains("DefaultCheckBoxStyle")) adaptiveCheckBox.Style = resources["DefaultCheckBoxStyle"] as Style;
        adaptiveCheckBox.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(viewModel.AdaptiveBoundary)) { Mode = BindingMode.TwoWay });
        mainPanel.Children.Add(adaptiveCheckBox);

        var descriptionText = new TextBlock
        {
            Text = "勾选后将自动裁切掉四周完全透明的区域",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 5)
        };
        if (resources.Contains("StatusTextBlockStyle")) descriptionText.Style = resources["StatusTextBlockStyle"] as Style;
        mainPanel.Children.Add(descriptionText);

        return mainPanel;
    }

    public override IScriptViewModel CreateViewModel()
    {
        return new MirrorViewModel(this);
    }

    public override async Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
    {
        await Task.CompletedTask;
    }

    public override Dictionary<string, object> SerializeParameters()
    {
        return new Dictionary<string, object>
        {
            [nameof(Direction)] = Direction,
            [nameof(AdaptiveBoundary)] = AdaptiveBoundary,
            ["NodeInstanceId"] = NodeInstanceId
        };
    }

    public override void DeserializeParameters(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(Direction), out var dir))
            Direction = dir?.ToString() ?? "水平";
        if (data.TryGetValue(nameof(AdaptiveBoundary), out var adaptive))
            AdaptiveBoundary = Convert.ToBoolean(adaptive);
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

public class MirrorViewModel : ScriptViewModelBase
{
    private MirrorScript MirrorScript => (MirrorScript)Script;

    public bool AdaptiveBoundary
    {
        get => MirrorScript.AdaptiveBoundary;
        set
        {
            if (MirrorScript.AdaptiveBoundary != value)
            {
                MirrorScript.AdaptiveBoundary = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(AdaptiveBoundary), value);
            }
        }
    }

    public string Direction
    {
        get => MirrorScript.Direction;
        set
        {
            if (MirrorScript.Direction != value)
            {
                MirrorScript.Direction = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(Direction), value);
            }
        }
    }

    public MirrorViewModel(MirrorScript script) : base(script) { }

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
            [nameof(AdaptiveBoundary)] = AdaptiveBoundary,
            [nameof(Direction)] = Direction
        };
    }

    public override async Task SetParameterDataAsync(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(AdaptiveBoundary), out var adaptive))
            AdaptiveBoundary = Convert.ToBoolean(adaptive);
        if (data.TryGetValue(nameof(Direction), out var direction))
            Direction = direction?.ToString() ?? "水平";
        await Task.CompletedTask;
    }

    public override async Task ResetToDefaultAsync()
    {
        AdaptiveBoundary = false;
        Direction = "水平";
        await Task.CompletedTask;
    }

    public override void Dispose()
    {
        base.Dispose();
    }
} 