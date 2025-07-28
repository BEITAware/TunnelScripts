using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Tunnel_Next.Services.Scripting;
using OpenCvSharp;
using System.Linq;
using System.Threading;
using System.Text.Json;

[TunnelExtensionScript(
    Name = "CTM应用",
    Author = "BEITAware",
    Description = "将CTM模型应用到图像上进行颜色转换",
    Version = "1.0",
    Category = "Tunnel色彩转换模型",
    Color = "#9B59B6"
)]
public class CTMApplicationScript : TunnelExtensionScriptBase
{
    [ScriptParameter(DisplayName = "并行处理", Description = "启用多线程并行处理", Order = 0)]
    public bool EnableParallelProcessing { get; set; } = true;

    public string NodeInstanceId { get; set; } = string.Empty;

    public override Dictionary<string, PortDefinition> GetInputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            ["f32bmp"] = new PortDefinition("f32bmp", false, "输入图像"),
            ["ctm"] = new PortDefinition("ColorTransferModel", false, "CTM模型")
        };
    }

    public override Dictionary<string, PortDefinition> GetOutputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            ["f32bmp"] = new PortDefinition("f32bmp", false, "转换后的图像")
        };
    }

    public override Dictionary<string, object> Process(Dictionary<string, object> inputs, IScriptContext context)
    {
        if (!inputs.TryGetValue("f32bmp", out var imageObj) || imageObj == null ||
            !inputs.TryGetValue("ctm", out var ctmObj) || ctmObj == null)
        {
            return new Dictionary<string, object> { ["f32bmp"] = null };
        }

        if (!(imageObj is Mat imageMat) || imageMat.Empty())
        {
            return new Dictionary<string, object> { ["f32bmp"] = null };
        }

        try
        {
            // 确保输入是RGBA格式
            Mat workingImage = EnsureRGBAFormat(imageMat);

            // 将CTM对象转换为ColorTransferModel
            ColorTransferModel ctmModel = ConvertToColorTransferModel(ctmObj);
            if (ctmModel == null)
            {
                return new Dictionary<string, object> { ["f32bmp"] = null };
            }

            // 应用CTM转换
            Mat transformedImage = ApplyColorTransform(workingImage, ctmModel);

            return new Dictionary<string, object>
            {
                ["f32bmp"] = transformedImage
            };
        }
        catch (Exception ex)
        {
            throw new ApplicationException($"CTM应用处理失败: {ex.Message}", ex);
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
    /// 将CTM对象转换为ColorTransferModel
    /// </summary>
    private ColorTransferModel ConvertToColorTransferModel(object ctmObj)
    {
        // 如果已经是ColorTransferModel类型，直接返回
        if (ctmObj is ColorTransferModel ctm)
        {
            return ctm;
        }

        // 尝试从其他CTM类型转换
        try
        {
            // 使用反射获取属性值
            var ctmType = ctmObj.GetType();
            var degreeProperty = ctmType.GetProperty("Degree");
            var rCoefficientsProperty = ctmType.GetProperty("RCoefficients");
            var gCoefficientsProperty = ctmType.GetProperty("GCoefficients");
            var bCoefficientsProperty = ctmType.GetProperty("BCoefficients");

            if (degreeProperty != null && rCoefficientsProperty != null &&
                gCoefficientsProperty != null && bCoefficientsProperty != null)
            {
                return new ColorTransferModel
                {
                    Degree = (int)degreeProperty.GetValue(ctmObj),
                    RCoefficients = (double[])rCoefficientsProperty.GetValue(ctmObj),
                    GCoefficients = (double[])gCoefficientsProperty.GetValue(ctmObj),
                    BCoefficients = (double[])bCoefficientsProperty.GetValue(ctmObj)
                };
            }
        }
        catch (Exception)
        {
            // 转换失败，返回null
        }

        return null;
    }

    /// <summary>
    /// 应用颜色转换
    /// </summary>
    private Mat ApplyColorTransform(Mat inputImage, ColorTransferModel ctmModel)
    {
        Mat outputImage = inputImage.Clone();
        int height = inputImage.Height;
        int width = inputImage.Width;

        if (EnableParallelProcessing)
        {
            // 并行处理
            Parallel.For(0, height, y =>
            {
                ProcessRow(inputImage, outputImage, ctmModel, y, width);
            });
        }
        else
        {
            // 串行处理
            for (int y = 0; y < height; y++)
            {
                ProcessRow(inputImage, outputImage, ctmModel, y, width);
            }
        }

        return outputImage;
    }

    /// <summary>
    /// 处理单行像素
    /// </summary>
    private void ProcessRow(Mat inputImage, Mat outputImage, ColorTransferModel ctmModel, int y, int width)
    {
        for (int x = 0; x < width; x++)
        {
            var pixel = inputImage.At<Vec4f>(y, x);
            var inputColor = new Vec3f(pixel.Item0, pixel.Item1, pixel.Item2);
            
            // 应用CTM转换
            var transformedColor = ctmModel.Transform(inputColor);
            
            // 保持Alpha通道不变
            var outputPixel = new Vec4f(transformedColor.Item0, transformedColor.Item1, transformedColor.Item2, pixel.Item3);
            outputImage.Set(y, x, outputPixel);
        }
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
            "/Tunnel-Next;component/Resources/ScriptsControls/CheckBoxStyles.xaml"
        };
        foreach (var path in resourcePaths)
        {
            try { resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(path, UriKind.Relative) }); }
            catch { /* 静默处理 */ }
        }

        if (resources.Contains("MainPanelStyle")) mainPanel.Style = resources["MainPanelStyle"] as Style;

        // ViewModel
        var viewModel = CreateViewModel() as CTMApplicationViewModel;
        mainPanel.DataContext = viewModel;

        // 标题
        var titleLabel = new Label { Content = "CTM应用设置" };
        if (resources.Contains("TitleLabelStyle")) titleLabel.Style = resources["TitleLabelStyle"] as Style;
        mainPanel.Children.Add(titleLabel);
        
        // 并行处理选项
        var parallelCheckBox = new CheckBox { Content = "启用并行处理", Margin = new Thickness(0, 5, 0, 10) };
        if(resources.Contains("DefaultCheckBoxStyle")) parallelCheckBox.Style = resources["DefaultCheckBoxStyle"] as Style;
        parallelCheckBox.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(viewModel.EnableParallelProcessing)) { Mode = BindingMode.TwoWay });
        mainPanel.Children.Add(parallelCheckBox);

        return mainPanel;
    }

    public override IScriptViewModel CreateViewModel()
    {
        return new CTMApplicationViewModel(this);
    }

    public override async Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
    {
        await Task.CompletedTask;
    }

    public override Dictionary<string, object> SerializeParameters()
    {
        return new Dictionary<string, object>
        {
            [nameof(EnableParallelProcessing)] = EnableParallelProcessing,
            ["NodeInstanceId"] = NodeInstanceId
        };
    }

    public override void DeserializeParameters(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(EnableParallelProcessing), out var enableParallel))
            EnableParallelProcessing = Convert.ToBoolean(enableParallel);
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

public class CTMApplicationViewModel : ScriptViewModelBase
{
    private CTMApplicationScript CTMApplicationScript => (CTMApplicationScript)Script;

    public bool EnableParallelProcessing
    {
        get => CTMApplicationScript.EnableParallelProcessing;
        set
        {
            if (CTMApplicationScript.EnableParallelProcessing != value)
            {
                CTMApplicationScript.EnableParallelProcessing = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(EnableParallelProcessing), value);
            }
        }
    }

    public CTMApplicationViewModel(CTMApplicationScript script) : base(script)
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
            [nameof(EnableParallelProcessing)] = EnableParallelProcessing
        };
    }

    public override async Task SetParameterDataAsync(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(EnableParallelProcessing), out var enableParallel))
            EnableParallelProcessing = Convert.ToBoolean(enableParallel);
        await Task.CompletedTask;
    }

    public override async Task ResetToDefaultAsync()
    {
        EnableParallelProcessing = true;
        await Task.CompletedTask;
    }

    public override void Dispose()
    {
        base.Dispose();
    }
}

/// <summary>
/// 颜色转换模型
/// </summary>
public class ColorTransferModel
{
    public int Degree { get; set; }
    public double[] RCoefficients { get; set; }
    public double[] GCoefficients { get; set; }
    public double[] BCoefficients { get; set; }

    /// <summary>
    /// 应用颜色转换
    /// </summary>
    public Vec3f Transform(Vec3f inputColor)
    {
        var features = GeneratePolynomialFeatures(inputColor, Degree);

        float r = (float)features.Zip(RCoefficients, (f, c) => f * c).Sum();
        float g = (float)features.Zip(GCoefficients, (f, c) => f * c).Sum();
        float b = (float)features.Zip(BCoefficients, (f, c) => f * c).Sum();

        // 限制在0-1范围内
        r = Math.Max(0, Math.Min(1, r));
        g = Math.Max(0, Math.Min(1, g));
        b = Math.Max(0, Math.Min(1, b));

        return new Vec3f(r, g, b);
    }

    /// <summary>
    /// 生成单个颜色的多项式特征
    /// </summary>
    private double[] GeneratePolynomialFeatures(Vec3f color, int degree)
    {
        var features = new List<double>();
        double r = color.Item0;
        double g = color.Item1;
        double b = color.Item2;

        // 常数项
        features.Add(1.0);

        // 生成各度数的项
        for (int d = 1; d <= degree; d++)
        {
            for (int i = 0; i <= d; i++)
            {
                for (int j = 0; j <= d - i; j++)
                {
                    int k = d - i - j;
                    if (k >= 0)
                    {
                        features.Add(Math.Pow(r, i) * Math.Pow(g, j) * Math.Pow(b, k));
                    }
                }
            }
        }

        return features.ToArray();
    }

    /// <summary>
    /// 序列化为JSON字符串
    /// </summary>
    public string ToJson()
    {
        return JsonSerializer.Serialize(this);
    }

    /// <summary>
    /// 从JSON字符串反序列化
    /// </summary>
    public static ColorTransferModel FromJson(string json)
    {
        return JsonSerializer.Deserialize<ColorTransferModel>(json);
    }
}
