using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Tunnel_Next.Services.Scripting;
using OpenCvSharp;
using System.IO;
using System.Text;
using Microsoft.Win32;
using System.Linq;
using System.Text.Json;

[RevivalScript(
    Name = "CTM转LUT",
    Author = "BEITAware",
    Description = "将CTM模型转换为3D LUT格式文件",
    Version = "1.0",
    Category = "Tunnel色彩转换模型",
    Color = "#E74C3C"
)]
public class CTMToLUTScript : RevivalScriptBase
{
    [ScriptParameter(DisplayName = "LUT网格大小", Description = "3D LUT的网格大小（如33表示33x33x33）", Order = 0)]
    public int GridSize { get; set; } = 33;

    public string NodeInstanceId { get; set; } = string.Empty;

    public override Dictionary<string, PortDefinition> GetInputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            ["ctm"] = new PortDefinition("ColorTransferModel", false, "CTM模型")
        };
    }

    public override Dictionary<string, PortDefinition> GetOutputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            ["lut_data"] = new PortDefinition("Cube3DLut", false, "LUT数据（CUBE格式文本）")
        };
    }

    public override Dictionary<string, object> Process(Dictionary<string, object> inputs, IScriptContext context)
    {
        if (!inputs.TryGetValue("ctm", out var ctmObj) || ctmObj == null)
        {
            return new Dictionary<string, object> { ["lut_data"] = null };
        }

        try
        {
            // 将CTM对象转换为ColorTransferModel
            ColorTransferModel ctmModel = ConvertToColorTransferModel(ctmObj);
            if (ctmModel == null)
            {
                return new Dictionary<string, object> { ["lut_data"] = null };
            }

            // 生成3D LUT数据
            string lutData = Generate3DLUTData(ctmModel, GridSize);

            return new Dictionary<string, object>
            {
                ["lut_data"] = lutData
            };
        }
        catch (Exception ex)
        {
            throw new ApplicationException($"CTM转LUT处理失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 生成3D LUT数据
    /// </summary>
    private string Generate3DLUTData(ColorTransferModel ctmModel, int gridSize)
    {
        var lutBuilder = new StringBuilder();

        // 写入CUBE文件头
        lutBuilder.AppendLine("TITLE \"Generated from CTM\"");
        lutBuilder.AppendLine($"LUT_3D_SIZE {gridSize}");
        lutBuilder.AppendLine("DOMAIN_MIN 0.0 0.0 0.0");
        lutBuilder.AppendLine("DOMAIN_MAX 1.0 1.0 1.0");
        lutBuilder.AppendLine();

        // 生成LUT数据
        for (int b = 0; b < gridSize; b++)
        {
            for (int g = 0; g < gridSize; g++)
            {
                for (int r = 0; r < gridSize; r++)
                {
                    // 计算输入颜色（0-1范围）
                    float inputR = r / (float)(gridSize - 1);
                    float inputG = g / (float)(gridSize - 1);
                    float inputB = b / (float)(gridSize - 1);

                    var inputColor = new Vec3f(inputR, inputG, inputB);

                    // 应用CTM转换
                    var outputColor = ctmModel.Transform(inputColor);

                    // 添加LUT条目
                    lutBuilder.AppendLine($"{outputColor.Item0:F6} {outputColor.Item1:F6} {outputColor.Item2:F6}");
                }
            }
        }

        return lutBuilder.ToString();
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
        var titleLabel = new Label { Content = "CTM转LUT设置" };
        if (resources.Contains("TitleLabelStyle")) titleLabel.Style = resources["TitleLabelStyle"] as Style;
        mainPanel.Children.Add(titleLabel);
        
        // 网格大小
        var gridSizeLabel = new Label { Content = "LUT网格大小:" };
        if(resources.Contains("DefaultLabelStyle")) gridSizeLabel.Style = resources["DefaultLabelStyle"] as Style;
        mainPanel.Children.Add(gridSizeLabel);

        var gridSizeTextBox = new TextBox { Margin = new Thickness(0, 0, 0, 10) };
        if(resources.Contains("DefaultTextBoxStyle")) gridSizeTextBox.Style = resources["DefaultTextBoxStyle"] as Style;
        gridSizeTextBox.SetBinding(TextBox.TextProperty, new Binding(nameof(viewModel.GridSize)) { Mode = BindingMode.TwoWay });
        mainPanel.Children.Add(gridSizeTextBox);

        // 说明文本
        var infoLabel = new Label { Content = "LUT数据将作为文本输出，可连接到其他节点进行处理。" };
        if(resources.Contains("DefaultLabelStyle")) infoLabel.Style = resources["DefaultLabelStyle"] as Style;
        mainPanel.Children.Add(infoLabel);

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
            [nameof(GridSize)] = GridSize,
            ["NodeInstanceId"] = NodeInstanceId
        };
    }

    public override void DeserializeParameters(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(GridSize), out var gridSize))
            GridSize = Convert.ToInt32(gridSize);
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

public class CTMToLUTViewModel : ScriptViewModelBase
{
    private CTMToLUTScript CTMToLUTScript => (CTMToLUTScript)Script;

    public int GridSize
    {
        get => CTMToLUTScript.GridSize;
        set
        {
            if (CTMToLUTScript.GridSize != value)
            {
                CTMToLUTScript.GridSize = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(GridSize), value);
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
        if (parameterName == nameof(GridSize))
        {
            if (value is int gridSize && (gridSize < 2 || gridSize > 256))
            {
                return new ScriptValidationResult(false, "网格大小必须在2-256之间");
            }
        }
        return new ScriptValidationResult(true);
    }

    public override Dictionary<string, object> GetParameterData()
    {
        return new Dictionary<string, object>
        {
            [nameof(GridSize)] = GridSize
        };
    }

    public override async Task SetParameterDataAsync(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(GridSize), out var gridSize))
            GridSize = Convert.ToInt32(gridSize);
        await Task.CompletedTask;
    }

    public override async Task ResetToDefaultAsync()
    {
        GridSize = 33;
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
