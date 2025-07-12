using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Tunnel_Next.Services.Scripting;
using OpenCvSharp;
using Microsoft.ML;
using Microsoft.ML.Data;
using System.IO;
using System.Linq;

[RevivalScript(
    Name = "CTM生成",
    Author = "BEITAware", 
    Description = "基于两个F32bmp采样数据生成色彩转换模型（CTM），使用ML.NET多项式回归",
    Version = "1.0",
    Category = "色彩转换",
    Color = "#4ECDC4"
)]
public class CTMGenerationScript : RevivalScriptBase
{
    [ScriptParameter(DisplayName = "多项式度数", Description = "多项式回归的度数（1-3）", Order = 0)]
    public int PolynomialDegree { get; set; } = 2;

    public string NodeInstanceId { get; set; } = string.Empty;

    public override Dictionary<string, PortDefinition> GetInputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            ["source_f32bmp"] = new PortDefinition("f32bmp", false, "源色卡采样数据"),
            ["target_f32bmp"] = new PortDefinition("f32bmp", false, "目标色卡采样数据")
        };
    }

    public override Dictionary<string, PortDefinition> GetOutputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            ["ColorTransferModelForward"] = new PortDefinition("ColorTransferModel", false, "正向CTM模型（源→目标）"),
            ["ColorTransferModelReversed"] = new PortDefinition("ColorTransferModel", false, "反向CTM模型（目标→源）")
        };
    }

    public override Dictionary<string, object> Process(Dictionary<string, object> inputs, IScriptContext context)
    {
        if (!inputs.TryGetValue("source_f32bmp", out var sourceObj) || sourceObj == null ||
            !inputs.TryGetValue("target_f32bmp", out var targetObj) || targetObj == null)
        {
            return new Dictionary<string, object> 
            { 
                ["ctm_forward"] = null,
                ["ctm_reverse"] = null
            };
        }

        if (!(sourceObj is Mat sourceMat) || sourceMat.Empty() ||
            !(targetObj is Mat targetMat) || targetMat.Empty())
        {
            return new Dictionary<string, object>
            {
                ["ColorTransferModelForward"] = null,
                ["ColorTransferModelReversed"] = null
            };
        }

        // 检查Mat对象是否已被释放
        try
        {
            var sourceSize = sourceMat.Size();
            var targetSize = targetMat.Size();
        }
        catch (Exception ex)
        {
            return new Dictionary<string, object>
            {
                ["ColorTransferModelForward"] = null,
                ["ColorTransferModelReversed"] = null
            };
        }

        try
        {
            // 确保两个输入具有相同的尺寸
            if (sourceMat.Size() != targetMat.Size())
            {
                throw new ArgumentException("源和目标采样数据尺寸必须相同");
            }

            // 提取颜色数据
            var sourceColors = ExtractColorData(sourceMat);
            var targetColors = ExtractColorData(targetMat);

            // 创建ML.NET上下文
            var mlContext = new MLContext(seed: 0);

            // 训练正向模型（源→目标）
            var forwardModel = TrainColorTransferModel(mlContext, sourceColors, targetColors, PolynomialDegree);

            // 训练反向模型（目标→源）
            var reverseModel = TrainColorTransferModel(mlContext, targetColors, sourceColors, PolynomialDegree);

            return new Dictionary<string, object>
            {
                ["ColorTransferModelForward"] = forwardModel,
                ["ColorTransferModelReversed"] = reverseModel
            };
        }
        catch (Exception ex)
        {
            throw new ApplicationException($"CTM生成失败: {ex.Message}", ex);
        }
    }

    private Vec3f[] ExtractColorData(Mat mat)
    {
        var colors = new List<Vec3f>();

        for (int y = 0; y < mat.Rows; y++)
        {
            for (int x = 0; x < mat.Cols; x++)
            {
                var pixel = mat.Get<Vec4f>(y, x);
                // 只使用RGB通道，忽略Alpha
                colors.Add(new Vec3f(pixel.Item0, pixel.Item1, pixel.Item2));
            }
        }

        return colors.ToArray();
    }

    private CTMModel TrainColorTransferModel(MLContext mlContext, Vec3f[] sourceColors, Vec3f[] targetColors, int degree)
    {
        // 简化的模型训练实现
        // 准备训练数据
        var trainingData = new List<ColorTransferData>();

        for (int i = 0; i < sourceColors.Length; i++)
        {
            trainingData.Add(new ColorTransferData
            {
                SourceR = sourceColors[i].Item0,
                SourceG = sourceColors[i].Item1,
                SourceB = sourceColors[i].Item2,
                TargetR = targetColors[i].Item0,
                TargetG = targetColors[i].Item1,
                TargetB = targetColors[i].Item2
            });
        }

        var dataView = mlContext.Data.LoadFromEnumerable(trainingData);

        // 创建基础特征管道
        var featurePipeline = mlContext.Transforms.Concatenate("Features", "SourceR", "SourceG", "SourceB");

        // 训练R通道模型
        var rPipeline = featurePipeline.Append(mlContext.Regression.Trainers.FastTree(labelColumnName: "TargetR"));
        var rModel = rPipeline.Fit(dataView);

        // 训练G通道模型
        var gPipeline = featurePipeline.Append(mlContext.Regression.Trainers.FastTree(labelColumnName: "TargetG"));
        var gModel = gPipeline.Fit(dataView);

        // 训练B通道模型
        var bPipeline = featurePipeline.Append(mlContext.Regression.Trainers.FastTree(labelColumnName: "TargetB"));
        var bModel = bPipeline.Fit(dataView);

        // 返回完整的CTM模型
        return new CTMModel
        {
            MLContext = mlContext,
            RModel = rModel,
            GModel = gModel,
            BModel = bModel,
            Degree = degree,
            SourceColors = sourceColors,
            TargetColors = targetColors
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
            "/Tunnel-Next;component/Resources/ScriptsControls/SliderStyles.xaml",
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
        var titleLabel = new Label { Content = "CTM生成" };
        if (resources.Contains("TitleLabelStyle")) titleLabel.Style = resources["TitleLabelStyle"] as Style;
        mainPanel.Children.Add(titleLabel);

        // 多项式度数滑块
        var degreePanel = new StackPanel { Margin = new Thickness(0, 5, 0, 0) };
        var degreeLabel = new Label { Content = "多项式度数" };
        if (resources.Contains("DefaultLabelStyle")) degreeLabel.Style = resources["DefaultLabelStyle"] as Style;

        var degreeSlider = new Slider { Minimum = 1, Maximum = 3 };
        if (resources.Contains("DefaultSliderStyle")) degreeSlider.Style = resources["DefaultSliderStyle"] as Style;
        degreeSlider.SetBinding(Slider.ValueProperty, new Binding(nameof(viewModel.PolynomialDegree)) { Mode = BindingMode.TwoWay });

        degreePanel.Children.Add(degreeLabel);
        degreePanel.Children.Add(degreeSlider);
        mainPanel.Children.Add(degreePanel);

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

// 数据类定义
public class ColorTransferData
{
    [LoadColumn(0)]
    public float SourceR { get; set; }

    [LoadColumn(1)]
    public float SourceG { get; set; }

    [LoadColumn(2)]
    public float SourceB { get; set; }

    [LoadColumn(3)]
    public float TargetR { get; set; }

    [LoadColumn(4)]
    public float TargetG { get; set; }

    [LoadColumn(5)]
    public float TargetB { get; set; }
}



public class ColorPrediction
{
    [ColumnName("Score")]
    public float Score { get; set; }
}

public class CTMModel
{
    public MLContext MLContext { get; set; }
    public ITransformer RModel { get; set; }  // R通道模型
    public ITransformer GModel { get; set; }  // G通道模型
    public ITransformer BModel { get; set; }  // B通道模型
    public int Degree { get; set; }
    public Vec3f[] SourceColors { get; set; }  // 源颜色样本
    public Vec3f[] TargetColors { get; set; }  // 目标颜色样本
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
        if (Script is RevivalScriptBase rsb)
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
