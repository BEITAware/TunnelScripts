using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Tunnel_Next.Services.Scripting;
using OpenCvSharp;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearRegression;
using System.Linq;
using System.Text.Json;

[TunnelExtensionScript(
    Name = "CTM生成",
    Author = "BEITAware", 
    Description = "生成颜色转换模型（CTM），使用多项式回归建立颜色映射关系",
    Version = "1.0",
    Category = "Tunnel色彩转换模型",
    Color = "#4ECDC4"
)]
public class CTMGenerationScript : TunnelExtensionScriptBase
{
    [ScriptParameter(DisplayName = "多项式度数", Description = "多项式回归的度数", Order = 0)]
    public int PolynomialDegree { get; set; } = 2;

    public string NodeInstanceId { get; set; } = string.Empty;

    public override Dictionary<string, PortDefinition> GetInputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            ["samples1"] = new PortDefinition("f32bmp", false, "第一组采样数据"),
            ["samples2"] = new PortDefinition("f32bmp", false, "第二组采样数据")
        };
    }

    public override Dictionary<string, PortDefinition> GetOutputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            ["ctm_forward"] = new PortDefinition("ColorTransferModel", false, "正向CTM模型（1→2）"),
            ["ctm_reverse"] = new PortDefinition("ColorTransferModel", false, "反向CTM模型（2→1）")
        };
    }

    public override Dictionary<string, object> Process(Dictionary<string, object> inputs, IScriptContext context)
    {
        if (!inputs.TryGetValue("samples1", out var samples1Obj) || samples1Obj == null ||
            !inputs.TryGetValue("samples2", out var samples2Obj) || samples2Obj == null)
        {
            return new Dictionary<string, object> { ["ctm_forward"] = null, ["ctm_reverse"] = null };
        }

        if (!(samples1Obj is Mat samples1Mat) || samples1Mat.Empty() ||
            !(samples2Obj is Mat samples2Mat) || samples2Mat.Empty())
        {
            return new Dictionary<string, object> { ["ctm_forward"] = null, ["ctm_reverse"] = null };
        }

        try
        {
            // 确保输入是RGBA格式
            Mat workingSamples1 = EnsureRGBAFormat(samples1Mat);
            Mat workingSamples2 = EnsureRGBAFormat(samples2Mat);
            
            // 提取颜色数据
            var colors1 = ExtractColorData(workingSamples1);
            var colors2 = ExtractColorData(workingSamples2);
            
            if (colors1.Length != colors2.Length)
            {
                throw new ArgumentException("两组采样数据的样本数量必须相同");
            }

            // 生成正向CTM模型（1→2）
            var forwardModel = GeneratePolynomialModel(colors1, colors2, PolynomialDegree);
            
            // 生成反向CTM模型（2→1）
            var reverseModel = GeneratePolynomialModel(colors2, colors1, PolynomialDegree);

            return new Dictionary<string, object>
            {
                ["ctm_forward"] = forwardModel,
                ["ctm_reverse"] = reverseModel
            };
        }
        catch (Exception ex)
        {
            throw new ApplicationException($"CTM生成处理失败: {ex.Message}", ex);
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
    /// 从采样图像中提取颜色数据
    /// </summary>
    private Vec3f[] ExtractColorData(Mat samplesImage)
    {
        var colors = new List<Vec3f>();
        
        for (int y = 0; y < samplesImage.Height; y++)
        {
            for (int x = 0; x < samplesImage.Width; x++)
            {
                var pixel = samplesImage.At<Vec4f>(y, x);
                // 只使用RGB通道，忽略Alpha通道
                colors.Add(new Vec3f(pixel.Item0, pixel.Item1, pixel.Item2));
            }
        }
        
        return colors.ToArray();
    }

    /// <summary>
    /// 生成多项式回归模型
    /// </summary>
    private ColorTransferModelForGeneration GeneratePolynomialModel(Vec3f[] inputColors, Vec3f[] outputColors, int degree)
    {
        int numSamples = inputColors.Length;
        
        // 创建多项式特征矩阵
        var featureMatrix = CreatePolynomialFeatures(inputColors, degree);
        
        // 分别为R、G、B通道训练模型
        var rCoefficients = SolveLinearRegression(featureMatrix, outputColors.Select(c => (double)c.Item0).ToArray());
        var gCoefficients = SolveLinearRegression(featureMatrix, outputColors.Select(c => (double)c.Item1).ToArray());
        var bCoefficients = SolveLinearRegression(featureMatrix, outputColors.Select(c => (double)c.Item2).ToArray());
        
        return new ColorTransferModelForGeneration
        {
            Degree = degree,
            RCoefficients = rCoefficients,
            GCoefficients = gCoefficients,
            BCoefficients = bCoefficients
        };
    }

    /// <summary>
    /// 创建多项式特征矩阵
    /// </summary>
    private Matrix<double> CreatePolynomialFeatures(Vec3f[] colors, int degree)
    {
        int numSamples = colors.Length;
        int numFeatures = GetPolynomialFeatureCount(degree);
        
        var matrix = Matrix<double>.Build.Dense(numSamples, numFeatures);
        
        for (int i = 0; i < numSamples; i++)
        {
            var features = GeneratePolynomialFeatures(colors[i], degree);
            for (int j = 0; j < features.Length; j++)
            {
                matrix[i, j] = features[j];
            }
        }
        
        return matrix;
    }

    /// <summary>
    /// 计算多项式特征数量
    /// </summary>
    private int GetPolynomialFeatureCount(int degree)
    {
        // 对于3个变量(R,G,B)的多项式，特征数量为 (degree+3)! / (3! * degree!)
        int count = 1; // 常数项
        
        for (int d = 1; d <= degree; d++)
        {
            // 计算度数为d的项数
            count += (d + 2) * (d + 1) / 2;
        }
        
        return count;
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
    /// 求解线性回归
    /// </summary>
    private double[] SolveLinearRegression(Matrix<double> X, double[] y)
    {
        var yVector = Vector<double>.Build.DenseOfArray(y);
        
        // 使用正规方程求解: θ = (X^T * X)^(-1) * X^T * y
        var XTranspose = X.Transpose();
        var XTX = XTranspose * X;
        var XTy = XTranspose * yVector;
        
        var coefficients = XTX.Solve(XTy);
        return coefficients.ToArray();
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
            "/Tunnel-Next;component/Resources/ScriptsControls/TextBoxIdleStyles.xaml"
        };
        foreach (var path in resourcePaths)
        {
            try { resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(path, UriKind.Relative) }); }
            catch { /* 静默处理 */ }
        }

        if (resources.Contains("MainPanelStyle")) mainPanel.Style = resources["MainPanelStyle"] as Style;

        // ViewModel
        var viewModel = CreateViewModel() as CTMGenerationViewModel;
        mainPanel.DataContext = viewModel;

        // 标题
        var titleLabel = new Label { Content = "CTM生成设置" };
        if (resources.Contains("TitleLabelStyle")) titleLabel.Style = resources["TitleLabelStyle"] as Style;
        mainPanel.Children.Add(titleLabel);

        // 多项式度数
        var degreeLabel = new Label { Content = "多项式度数:" };
        if(resources.Contains("DefaultLabelStyle")) degreeLabel.Style = resources["DefaultLabelStyle"] as Style;
        mainPanel.Children.Add(degreeLabel);

        var degreeTextBox = new TextBox { Margin = new Thickness(0, 0, 0, 10) };
        if(resources.Contains("DefaultTextBoxStyle")) degreeTextBox.Style = resources["DefaultTextBoxStyle"] as Style;
        degreeTextBox.SetBinding(TextBox.TextProperty, new Binding(nameof(viewModel.PolynomialDegree)) { Mode = BindingMode.TwoWay });
        mainPanel.Children.Add(degreeTextBox);

        return mainPanel;
    }

    public override IScriptViewModel CreateViewModel()
    {
        return new CTMGenerationViewModel(this);
    }

    public override async Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
    {
        await Task.CompletedTask;
    }

    public override Dictionary<string, object> SerializeParameters()
    {
        return new Dictionary<string, object>
        {
            [nameof(PolynomialDegree)] = PolynomialDegree,
            ["NodeInstanceId"] = NodeInstanceId
        };
    }

    public override void DeserializeParameters(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(PolynomialDegree), out var degree))
            PolynomialDegree = Convert.ToInt32(degree);
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

public class CTMGenerationViewModel : ScriptViewModelBase
{
    private CTMGenerationScript CTMGenerationScript => (CTMGenerationScript)Script;

    public int PolynomialDegree
    {
        get => CTMGenerationScript.PolynomialDegree;
        set
        {
            if (CTMGenerationScript.PolynomialDegree != value)
            {
                CTMGenerationScript.PolynomialDegree = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(PolynomialDegree), value);
            }
        }
    }

    public CTMGenerationViewModel(CTMGenerationScript script) : base(script)
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
        if (parameterName == nameof(PolynomialDegree))
        {
            if (value is int degree && (degree < 1 || degree > 5))
            {
                return new ScriptValidationResult(false, "多项式度数必须在1-5之间");
            }
        }
        return new ScriptValidationResult(true);
    }

    public override Dictionary<string, object> GetParameterData()
    {
        return new Dictionary<string, object>
        {
            [nameof(PolynomialDegree)] = PolynomialDegree
        };
    }

    public override async Task SetParameterDataAsync(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(PolynomialDegree), out var degree))
            PolynomialDegree = Convert.ToInt32(degree);
        await Task.CompletedTask;
    }

    public override async Task ResetToDefaultAsync()
    {
        PolynomialDegree = 2;
        await Task.CompletedTask;
    }

    public override void Dispose()
    {
        base.Dispose();
    }
}

/// <summary>
/// 颜色转换模型（用于生成）
/// </summary>
public class ColorTransferModelForGeneration
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
    public static ColorTransferModelForGeneration FromJson(string json)
    {
        return JsonSerializer.Deserialize<ColorTransferModelForGeneration>(json);
    }
}
