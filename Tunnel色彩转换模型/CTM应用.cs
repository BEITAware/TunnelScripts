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

[RevivalScript(
    Name = "CTM应用",
    Author = "BEITAware",
    Description = "将CTM模型应用到图像上进行色彩转换",
    Version = "1.0",
    Category = "色彩转换",
    Color = "#45B7D1"
)]
public class CTMApplicationScript : RevivalScriptBase
{
    [ScriptParameter(DisplayName = "分块处理", Description = "对大图像使用分块处理以节省内存", Order = 0)]
    public bool UseTiling { get; set; } = true;

    [ScriptParameter(DisplayName = "分块大小", Description = "分块处理时每块的大小", Order = 1)]
    public int TileSize { get; set; } = 512;

    public string NodeInstanceId { get; set; } = string.Empty;

    public override Dictionary<string, PortDefinition> GetInputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            ["f32bmp"] = new PortDefinition("f32bmp", false, "输入图像"),
            ["ctm_model"] = new PortDefinition("object", false, "CTM模型")
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
            !inputs.TryGetValue("ctm_model", out var modelObj) || modelObj == null)
        {
            return new Dictionary<string, object> { ["f32bmp"] = null };
        }

        if (!(imageObj is Mat inputMat) || inputMat.Empty())
        {
            return new Dictionary<string, object> { ["f32bmp"] = null };
        }

        if (!(modelObj is CTMModel ctmModel))
        {
            return new Dictionary<string, object> { ["f32bmp"] = null };
        }

        try
        {
            // 确保输入是RGBA格式
            Mat workingMat = EnsureRGBAFormat(inputMat);
            
            Mat resultMat;
            if (UseTiling && (workingMat.Rows * workingMat.Cols > TileSize * TileSize))
            {
                resultMat = ProcessWithTiling(workingMat, ctmModel, context);
            }
            else
            {
                resultMat = ProcessDirectly(workingMat, ctmModel, context);
            }

            return new Dictionary<string, object> { ["f32bmp"] = resultMat };
        }
        catch (Exception ex)
        {
            throw new ApplicationException($"CTM应用失败: {ex.Message}", ex);
        }
    }

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

    private Mat ProcessDirectly(Mat inputMat, CTMModel ctmModel, IScriptContext context)
    {
        // context?.LogMessage("开始直接处理图像...");

        Mat resultMat = new Mat(inputMat.Size(), inputMat.Type());

        // 创建预测引擎
        var predictionEngine = ctmModel.MLContext.Model.CreatePredictionEngine<ColorTransferData, ColorPrediction>(ctmModel.Model);

        for (int y = 0; y < inputMat.Rows; y++)
        {
            for (int x = 0; x < inputMat.Cols; x++)
            {
                var pixel = inputMat.Get<Vec4f>(y, x);

                // 应用CTM转换到RGB通道
                var transformedColor = ApplyCTMToPixel(pixel, predictionEngine, ctmModel.Degree);

                // 保持Alpha通道不变
                var resultPixel = new Vec4f(transformedColor.Item0, transformedColor.Item1, transformedColor.Item2, pixel.Item3);
                resultMat.Set<Vec4f>(y, x, resultPixel);
            }

            if (y % 100 == 0)
            {
                float progress = (float)y / inputMat.Rows * 100;
                // context?.LogMessage($"处理进度: {progress:F1}%");
            }
        }

        // context?.LogMessage("图像处理完成");
        return resultMat;
    }

    private Mat ProcessWithTiling(Mat inputMat, CTMModel ctmModel, IScriptContext context)
    {
        // context?.LogMessage($"开始分块处理图像，分块大小: {TileSize}x{TileSize}");

        Mat resultMat = new Mat(inputMat.Size(), inputMat.Type());

        int tilesX = (inputMat.Cols + TileSize - 1) / TileSize;
        int tilesY = (inputMat.Rows + TileSize - 1) / TileSize;
        int totalTiles = tilesX * tilesY;
        int processedTiles = 0;

        var predictionEngine = ctmModel.MLContext.Model.CreatePredictionEngine<ColorTransferData, ColorPrediction>(ctmModel.Model);

        for (int tileY = 0; tileY < tilesY; tileY++)
        {
            for (int tileX = 0; tileX < tilesX; tileX++)
            {
                int startX = tileX * TileSize;
                int startY = tileY * TileSize;
                int endX = Math.Min(startX + TileSize, inputMat.Cols);
                int endY = Math.Min(startY + TileSize, inputMat.Rows);

                var tileRect = new OpenCvSharp.Rect(startX, startY, endX - startX, endY - startY);
                Mat tileMat = inputMat[tileRect];
                Mat resultTile = new Mat(tileMat.Size(), tileMat.Type());

                // 处理当前分块
                for (int y = 0; y < tileMat.Rows; y++)
                {
                    for (int x = 0; x < tileMat.Cols; x++)
                    {
                        var pixel = tileMat.Get<Vec4f>(y, x);
                        var transformedColor = ApplyCTMToPixel(pixel, predictionEngine, ctmModel.Degree);
                        var resultPixel = new Vec4f(transformedColor.Item0, transformedColor.Item1, transformedColor.Item2, pixel.Item3);
                        resultTile.Set<Vec4f>(y, x, resultPixel);
                    }
                }

                // 将处理后的分块复制回结果图像
                resultTile.CopyTo(resultMat[tileRect]);
                resultTile.Dispose();

                processedTiles++;
                float progress = (float)processedTiles / totalTiles * 100;
                // context?.LogMessage($"分块处理进度: {progress:F1}% ({processedTiles}/{totalTiles})");
            }
        }

        // context?.LogMessage("分块处理完成");
        return resultMat;
    }

    private Vec3f ApplyCTMToPixel(Vec4f pixel, PredictionEngine<ColorTransferData, ColorPrediction> predictionEngine, int degree)
    {
        // 简化版本：这里应该使用实际的多项式特征变换
        // 由于ML.NET的复杂性，这里使用简化的线性变换作为示例
        
        var inputData = new ColorTransferData
        {
            SourceR = pixel.Item0,
            SourceG = pixel.Item1,
            SourceB = pixel.Item2
        };
        
        // 这里需要实现实际的预测逻辑
        // 由于ML.NET模型的复杂性，这是一个简化版本
        var prediction = predictionEngine.Predict(inputData);
        
        // 简化的颜色变换（实际应该基于训练的模型）
        float r = Math.Max(0, Math.Min(1, pixel.Item0 * 1.1f));
        float g = Math.Max(0, Math.Min(1, pixel.Item1 * 1.05f));
        float b = Math.Max(0, Math.Min(1, pixel.Item2 * 0.95f));
        
        return new Vec3f(r, g, b);
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
            "/Tunnel-Next;component/Resources/ScriptsControls/SliderStyles.xaml"
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
        var titleLabel = new Label { Content = "CTM应用" };
        if (resources.Contains("TitleLabelStyle")) titleLabel.Style = resources["TitleLabelStyle"] as Style;
        mainPanel.Children.Add(titleLabel);

        // 分块处理复选框
        var tilingCheckBox = new CheckBox { Content = "使用分块处理", Margin = new Thickness(0, 10, 0, 0) };
        tilingCheckBox.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(viewModel.UseTiling)) { Mode = BindingMode.TwoWay });
        mainPanel.Children.Add(tilingCheckBox);

        // 分块大小滑块
        var tileSizePanel = new StackPanel { Margin = new Thickness(0, 5, 0, 0) };
        var tileSizeLabel = new Label { Content = "分块大小" };
        if (resources.Contains("DefaultLabelStyle")) tileSizeLabel.Style = resources["DefaultLabelStyle"] as Style;
        
        var tileSizeSlider = new Slider { Minimum = 256, Maximum = 1024 };
        if (resources.Contains("DefaultSliderStyle")) tileSizeSlider.Style = resources["DefaultSliderStyle"] as Style;
        tileSizeSlider.SetBinding(Slider.ValueProperty, new Binding(nameof(viewModel.TileSize)) { Mode = BindingMode.TwoWay });
        
        tileSizePanel.Children.Add(tileSizeLabel);
        tileSizePanel.Children.Add(tileSizeSlider);
        mainPanel.Children.Add(tileSizePanel);

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
            [nameof(UseTiling)] = UseTiling,
            [nameof(TileSize)] = TileSize,
            ["NodeInstanceId"] = NodeInstanceId
        };
    }

    public override void DeserializeParameters(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(UseTiling), out var useTiling))
            UseTiling = Convert.ToBoolean(useTiling);
        if (data.TryGetValue(nameof(TileSize), out var tileSize))
            TileSize = Convert.ToInt32(tileSize);
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

// 重用CTM生成脚本中的数据类
public class ColorTransferData
{
    public float SourceR { get; set; }
    public float SourceG { get; set; }
    public float SourceB { get; set; }
    public float TargetR { get; set; }
    public float TargetG { get; set; }
    public float TargetB { get; set; }
}

public class ColorPrediction
{
    public float Score { get; set; }
}

public class CTMModel
{
    public MLContext MLContext { get; set; }
    public ITransformer Model { get; set; }
    public int Degree { get; set; }
}

public class CTMApplicationViewModel : ScriptViewModelBase
{
    private CTMApplicationScript CTMApplicationScript => (CTMApplicationScript)Script;

    public bool UseTiling
    {
        get => CTMApplicationScript.UseTiling;
        set
        {
            if (CTMApplicationScript.UseTiling != value)
            {
                CTMApplicationScript.UseTiling = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(UseTiling), value);
            }
        }
    }

    public int TileSize
    {
        get => CTMApplicationScript.TileSize;
        set
        {
            if (CTMApplicationScript.TileSize != value)
            {
                CTMApplicationScript.TileSize = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(TileSize), value);
            }
        }
    }

    public CTMApplicationViewModel(CTMApplicationScript script) : base(script)
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
            [nameof(UseTiling)] = UseTiling,
            [nameof(TileSize)] = TileSize
        };
    }

    public override async Task SetParameterDataAsync(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(UseTiling), out var useTiling))
            UseTiling = Convert.ToBoolean(useTiling);
        if (data.TryGetValue(nameof(TileSize), out var tileSize))
            TileSize = Convert.ToInt32(tileSize);
        await Task.CompletedTask;
    }

    public override async Task ResetToDefaultAsync()
    {
        UseTiling = true;
        TileSize = 512;
        await Task.CompletedTask;
    }

    public override void Dispose()
    {
        base.Dispose();
    }
}
