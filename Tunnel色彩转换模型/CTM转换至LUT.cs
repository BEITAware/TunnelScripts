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
using System.Text;
using System.Threading;

[RevivalScript(
    Name = "CTM转换至LUT",
    Author = "BEITAware",
    Description = "将CTM模型转换为3D LUT文件（.cube格式）",
    Version = "1.0",
    Category = "色彩转换",
    Color = "#9B59B6"
)]
public class CTMToLUTScript : RevivalScriptBase
{
    [ScriptParameter(DisplayName = "LUT精度", Description = "LUT的网格精度（33或65点）", Order = 0)]
    public int LUTSize { get; set; } = 33;

    [ScriptParameter(DisplayName = "并行处理", Description = "使用多线程加速LUT生成", Order = 1)]
    public bool UseParallel { get; set; } = true;

    public string NodeInstanceId { get; set; } = string.Empty;

    public override Dictionary<string, PortDefinition> GetInputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            ["ctm_model"] = new PortDefinition("object", false, "CTM模型输入")
        };
    }

    public override Dictionary<string, PortDefinition> GetOutputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            ["lut_data"] = new PortDefinition("object", false, "生成的LUT数据"),
            ["lut_text"] = new PortDefinition("string", false, "LUT的.cube格式文本")
        };
    }

    public override Dictionary<string, object> Process(Dictionary<string, object> inputs, IScriptContext context)
    {
        if (!inputs.TryGetValue("ctm_model", out var modelObj) || modelObj == null)
        {
            return new Dictionary<string, object>
            {
                ["lut_data"] = null,
                ["lut_text"] = null
            };
        }

        if (!(modelObj is CTMModel ctmModel))
        {
            return new Dictionary<string, object>
            {
                ["lut_data"] = null,
                ["lut_text"] = null
            };
        }

        try
        {
            // 生成LUT数据
            var lutData = GenerateLUTData(ctmModel, LUTSize, context);

            // 生成.cube格式文本
            var lutText = GenerateCubeText(lutData, LUTSize);

            return new Dictionary<string, object>
            {
                ["lut_data"] = lutData,
                ["lut_text"] = lutText
            };
        }
        catch (Exception ex)
        {
            throw new ApplicationException($"CTM转换至LUT失败: {ex.Message}", ex);
        }
    }

    private Vec3f[,,] GenerateLUTData(CTMModel ctmModel, int lutSize, IScriptContext context)
    {
        var lutData = new Vec3f[lutSize, lutSize, lutSize];
        // 创建三个预测引擎
        var rEngine = ctmModel.MLContext.Model.CreatePredictionEngine<ColorTransferData, ColorPrediction>(ctmModel.RModel);
        var gEngine = ctmModel.MLContext.Model.CreatePredictionEngine<ColorTransferData, ColorPrediction>(ctmModel.GModel);
        var bEngine = ctmModel.MLContext.Model.CreatePredictionEngine<ColorTransferData, ColorPrediction>(ctmModel.BModel);
        
        int totalSteps = lutSize * lutSize * lutSize;
        int processedSteps = 0;
        
        if (UseParallel)
        {
            // context?.LogMessage("使用并行处理生成LUT数据...");

            Parallel.For(0, lutSize, r =>
            {
                for (int g = 0; g < lutSize; g++)
                {
                    for (int b = 0; b < lutSize; b++)
                    {
                        // 将网格坐标转换为0-1范围的颜色值
                        float rValue = (float)r / (lutSize - 1);
                        float gValue = (float)g / (lutSize - 1);
                        float bValue = (float)b / (lutSize - 1);

                        // 应用CTM变换
                        var transformedColor = ApplyCTMToColor(rValue, gValue, bValue, rEngine, gEngine, bEngine);

                        // 确保颜色值在有效范围内
                        transformedColor = new Vec3f(
                            Math.Max(0, Math.Min(1, transformedColor.Item0)),
                            Math.Max(0, Math.Min(1, transformedColor.Item1)),
                            Math.Max(0, Math.Min(1, transformedColor.Item2))
                        );

                        lutData[r, g, b] = transformedColor;

                        Interlocked.Increment(ref processedSteps);

                        if (processedSteps % 1000 == 0)
                        {
                            float progress = (float)processedSteps / totalSteps * 100;
                            // context?.LogMessage($"LUT生成进度: {progress:F1}% ({processedSteps}/{totalSteps})");
                        }
                    }
                }
            });
        }
        else
        {
            // context?.LogMessage("使用单线程生成LUT数据...");

            for (int r = 0; r < lutSize; r++)
            {
                for (int g = 0; g < lutSize; g++)
                {
                    for (int b = 0; b < lutSize; b++)
                    {
                        float rValue = (float)r / (lutSize - 1);
                        float gValue = (float)g / (lutSize - 1);
                        float bValue = (float)b / (lutSize - 1);

                        var transformedColor = ApplyCTMToColor(rValue, gValue, bValue, rEngine, gEngine, bEngine);

                        transformedColor = new Vec3f(
                            Math.Max(0, Math.Min(1, transformedColor.Item0)),
                            Math.Max(0, Math.Min(1, transformedColor.Item1)),
                            Math.Max(0, Math.Min(1, transformedColor.Item2))
                        );

                        lutData[r, g, b] = transformedColor;

                        processedSteps++;
                        if (processedSteps % 1000 == 0)
                        {
                            float progress = (float)processedSteps / totalSteps * 100;
                            // context?.LogMessage($"LUT生成进度: {progress:F1}% ({processedSteps}/{totalSteps})");
                        }
                    }
                }
            }
        }

        // context?.LogMessage("LUT数据生成完成");
        return lutData;
    }

    private Vec3f ApplyCTMToColor(float r, float g, float b,
        PredictionEngine<ColorTransferData, ColorPrediction> rEngine,
        PredictionEngine<ColorTransferData, ColorPrediction> gEngine,
        PredictionEngine<ColorTransferData, ColorPrediction> bEngine)
    {
        var inputData = new ColorTransferData
        {
            SourceR = r,
            SourceG = g,
            SourceB = b
        };

        // 使用三个独立的模型预测每个通道
        var rPrediction = rEngine.Predict(inputData);
        var gPrediction = gEngine.Predict(inputData);
        var bPrediction = bEngine.Predict(inputData);

        float transformedR = Math.Max(0, Math.Min(1, rPrediction.Score));
        float transformedG = Math.Max(0, Math.Min(1, gPrediction.Score));
        float transformedB = Math.Max(0, Math.Min(1, bPrediction.Score));

        return new Vec3f(transformedR, transformedG, transformedB);
    }

    private string GenerateCubeText(Vec3f[,,] lutData, int lutSize)
    {
        var sb = new StringBuilder();

        // 写入.cube文件头
        sb.AppendLine("TITLE \"Generated LUT from CTM\"");
        sb.AppendLine($"LUT_3D_SIZE {lutSize}");
        sb.AppendLine("DOMAIN_MIN 0.0 0.0 0.0");
        sb.AppendLine("DOMAIN_MAX 1.0 1.0 1.0");
        sb.AppendLine();

        // 写入LUT数据
        // .cube格式要求按照B->G->R的顺序
        for (int b = 0; b < lutSize; b++)
        {
            for (int g = 0; g < lutSize; g++)
            {
                for (int r = 0; r < lutSize; r++)
                {
                    var color = lutData[r, g, b];
                    sb.AppendLine($"{color.Item0:F6} {color.Item1:F6} {color.Item2:F6}");
                }
            }
        }

        return sb.ToString();
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
            "/Tunnel-Next;component/Resources/ScriptsControls/TextBoxIdleStyles.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/ScriptButtonStyles.xaml"
        };
        foreach (var path in resourcePaths)
        {
            try { resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(path, UriKind.Relative) }); }
            catch { /* 静默处理 */ }
        }
        
        if (resources.Contains("MainPanelStyle")) mainPanel.Style = resources["MainPanelStyle"] as Style;

        // ViewModel
        var viewModel = CreateViewModel() as CTMToLUTViewModel;
        mainPanel.DataContext = viewModel;

        // 标题
        var titleLabel = new Label { Content = "CTM转换至LUT" };
        if (resources.Contains("TitleLabelStyle")) titleLabel.Style = resources["TitleLabelStyle"] as Style;
        mainPanel.Children.Add(titleLabel);

        // LUT精度选择
        var lutSizePanel = new StackPanel { Margin = new Thickness(0, 5, 0, 0) };
        var lutSizeLabel = new Label { Content = "LUT精度" };
        if (resources.Contains("DefaultLabelStyle")) lutSizeLabel.Style = resources["DefaultLabelStyle"] as Style;
        
        var lutSizeComboBox = new ComboBox();
        lutSizeComboBox.Items.Add(33);
        lutSizeComboBox.Items.Add(65);
        lutSizeComboBox.SetBinding(ComboBox.SelectedItemProperty, new Binding(nameof(viewModel.LUTSize)) { Mode = BindingMode.TwoWay });
        
        lutSizePanel.Children.Add(lutSizeLabel);
        lutSizePanel.Children.Add(lutSizeComboBox);
        mainPanel.Children.Add(lutSizePanel);

        // 并行处理复选框
        var parallelCheckBox = new CheckBox { Content = "使用并行处理", Margin = new Thickness(0, 10, 0, 0) };
        parallelCheckBox.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(viewModel.UseParallel)) { Mode = BindingMode.TwoWay });
        mainPanel.Children.Add(parallelCheckBox);

        return mainPanel;
    }

    public override IScriptViewModel CreateViewModel()
    {
        return new CTMToLUTViewModel(this);
    }

    public override async Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
    {
        await Task.CompletedTask;
    }

    public override Dictionary<string, object> SerializeParameters()
    {
        return new Dictionary<string, object>
        {
            [nameof(LUTSize)] = LUTSize,
            [nameof(UseParallel)] = UseParallel,
            ["NodeInstanceId"] = NodeInstanceId
        };
    }

    public override void DeserializeParameters(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(LUTSize), out var lutSize))
            LUTSize = Convert.ToInt32(lutSize);
        if (data.TryGetValue(nameof(UseParallel), out var useParallel))
            UseParallel = Convert.ToBoolean(useParallel);
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

// 重用数据类
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

public class CTMToLUTViewModel : ScriptViewModelBase
{
    private CTMToLUTScript CTMToLUTScript => (CTMToLUTScript)Script;

    public int LUTSize
    {
        get => CTMToLUTScript.LUTSize;
        set
        {
            if (CTMToLUTScript.LUTSize != value)
            {
                CTMToLUTScript.LUTSize = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(LUTSize), value);
            }
        }
    }



    public bool UseParallel
    {
        get => CTMToLUTScript.UseParallel;
        set
        {
            if (CTMToLUTScript.UseParallel != value)
            {
                CTMToLUTScript.UseParallel = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(UseParallel), value);
            }
        }
    }

    public CTMToLUTViewModel(CTMToLUTScript script) : base(script)
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
            [nameof(LUTSize)] = LUTSize,
            [nameof(UseParallel)] = UseParallel
        };
    }

    public override async Task SetParameterDataAsync(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(LUTSize), out var lutSize))
            LUTSize = Convert.ToInt32(lutSize);
        if (data.TryGetValue(nameof(UseParallel), out var useParallel))
            UseParallel = Convert.ToBoolean(useParallel);
        await Task.CompletedTask;
    }

    public override async Task ResetToDefaultAsync()
    {
        LUTSize = 33;
        UseParallel = true;
        await Task.CompletedTask;
    }

    public override void Dispose()
    {
        base.Dispose();
    }
}
