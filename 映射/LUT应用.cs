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
using System.Globalization;

[TunnelExtensionScript(
    Name = "LUT应用",
    Author = "BEITAware",
    Description = "将3D LUT应用到图像上进行颜色映射",
    Version = "1.0",
    Category = "映射",
    Color = "#3498DB"
)]
public class LUTApplicationScript : TunnelExtensionScriptBase
{
    [ScriptParameter(DisplayName = "插值方式", Description = "LUT插值方式", Order = 0)]
    public string InterpolationMethod { get; set; } = "三线性插值";

    [ScriptParameter(DisplayName = "并行处理", Description = "启用多线程并行处理", Order = 1)]
    public bool EnableParallelProcessing { get; set; } = true;

    public string NodeInstanceId { get; set; } = string.Empty;

    public override Dictionary<string, PortDefinition> GetInputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            ["f32bmp"] = new PortDefinition("f32bmp", false, "输入图像"),
            ["lut"] = new PortDefinition("Cube3DLut", false, "3D LUT数据")
        };
    }

    public override Dictionary<string, PortDefinition> GetOutputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            ["f32bmp"] = new PortDefinition("f32bmp", false, "应用LUT后的图像")
        };
    }

    public override Dictionary<string, object> Process(Dictionary<string, object> inputs, IScriptContext context)
    {
        if (!inputs.TryGetValue("f32bmp", out var imageObj) || imageObj == null ||
            !inputs.TryGetValue("lut", out var lutObj) || lutObj == null)
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
            using var workingImage = EnsureRGBAFormat(imageMat);
            
            // 解析LUT数据
            Cube3DLut lut = ParseLUTData(lutObj);
            if (lut == null)
            {
                return new Dictionary<string, object> { ["f32bmp"] = null };
            }
            
            // 应用LUT
            Mat transformedImage = ApplyLUT(workingImage, lut);

            return new Dictionary<string, object>
            {
                ["f32bmp"] = transformedImage
            };
        }
        catch (Exception ex)
        {
            throw new ApplicationException($"LUT应用处理失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 确保图像是RGBA格式
    /// </summary>
    private Mat EnsureRGBAFormat(Mat inputMat)
    {
        double scale = inputMat.Depth() switch
        {
            MatType.CV_8U => 1.0 / 255.0,
            MatType.CV_16U => 1.0 / 65535.0,
            MatType.CV_32F or MatType.CV_64F => 1.0,
            _ => throw new NotSupportedException($"不支持的图像深度：{inputMat.Depth()}")
        };
        using var normalized = new Mat();
        inputMat.ConvertTo(normalized, MatType.CV_32F, scale);
        return normalized.Channels() switch
        {
            1 => normalized.CvtColor(ColorConversionCodes.GRAY2BGRA),
            3 => normalized.CvtColor(ColorConversionCodes.BGR2BGRA),
            4 => normalized.Clone(),
            _ => throw new NotSupportedException($"不支持 {normalized.Channels()} 通道的图像")
        };
    }

    /// <summary>
    /// 解析LUT数据
    /// </summary>
    private Cube3DLut ParseLUTData(object lutObj)
    {
        // 如果已经是Cube3DLut类型，直接返回
        if (lutObj is Cube3DLut lut)
        {
            return lut;
        }

        // 如果是字符串，解析CUBE格式
        if (lutObj is string lutString)
        {
            return ParseCubeFormat(lutString);
        }

        return null;
    }

    /// <summary>
    /// 解析CUBE格式的LUT数据
    /// </summary>
    private Cube3DLut ParseCubeFormat(string cubeData)
    {
        var lines = cubeData.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        int gridSize = 33; // 兼容原节点接受的无标题 33 点网格
        var domainMin = new Vec3f(0, 0, 0);
        var domainMax = new Vec3f(1, 1, 1);
        var values = new List<Vec3f>();
        foreach (var line in lines)
        {
            var content = line.Split('#', 2)[0].Trim();
            if (content.Length == 0) continue;
            var parts = content.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            switch (parts[0])
            {
                case "TITLE":
                    break;
                case "LUT_3D_SIZE":
                    if (parts.Length != 2 || !int.TryParse(parts[1], out gridSize) || gridSize < 2)
                        throw new FormatException("LUT_3D_SIZE 必须是大于 1 的整数");
                    break;
                case "LUT_3D_INPUT_RANGE":
                    if (parts.Length != 3)
                        throw new FormatException("LUT_3D_INPUT_RANGE 需要最小值和最大值");
                    var min = ParseValue(parts[1]);
                    var max = ParseValue(parts[2]);
                    domainMin = new Vec3f(min, min, min);
                    domainMax = new Vec3f(max, max, max);
                    break;
                case "DOMAIN_MIN":
                    domainMin = ParseTriple(parts, 1);
                    break;
                case "DOMAIN_MAX":
                    domainMax = ParseTriple(parts, 1);
                    break;
                default:
                    // 部分导出器会附带额外的命名头字段，沿用原解析器的兼容行为。
                    if (char.IsLetter(parts[0][0]) &&
                        !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                        break;
                    values.Add(ParseTriple(parts, 0));
                    break;
            }
        }
        if (checked((long)gridSize * gridSize * gridSize) != values.Count)
            throw new FormatException($"LUT 网格与数据行数不符：{gridSize}，{values.Count} 行");
        if (domainMax.Item0 <= domainMin.Item0 || domainMax.Item1 <= domainMin.Item1 ||
            domainMax.Item2 <= domainMin.Item2)
            throw new FormatException("每个通道的 DOMAIN_MAX 必须大于 DOMAIN_MIN");

        return new Cube3DLut { GridSize = gridSize, DomainMin = domainMin, DomainMax = domainMax, Data = values.ToArray() };
    }

    private static Vec3f ParseTriple(string[] parts, int start)
    {
        if (parts.Length != start + 3)
            throw new FormatException("LUT 数据行需要三个数值");
        return new Vec3f(ParseValue(parts[start]), ParseValue(parts[start + 1]), ParseValue(parts[start + 2]));
    }

    private static float ParseValue(string text)
    {
        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
            !float.IsFinite(value))
            throw new FormatException($"无效的 LUT 数值：{text}");
        return value;
    }

    /// <summary>
    /// 应用LUT到图像
    /// </summary>
    private Mat ApplyLUT(Mat inputImage, Cube3DLut lut)
    {
        Mat outputImage = inputImage.Clone();
        int height = inputImage.Height;
        int width = inputImage.Width;

        if (EnableParallelProcessing)
        {
            // 并行处理
            Parallel.For(0, height, y =>
            {
                ProcessRowWithLUT(inputImage, outputImage, lut, y, width);
            });
        }
        else
        {
            // 串行处理
            for (int y = 0; y < height; y++)
            {
                ProcessRowWithLUT(inputImage, outputImage, lut, y, width);
            }
        }

        return outputImage;
    }

    /// <summary>
    /// 处理单行像素
    /// </summary>
    private void ProcessRowWithLUT(Mat inputImage, Mat outputImage, Cube3DLut lut, int y, int width)
    {
        for (int x = 0; x < width; x++)
        {
            var pixel = inputImage.At<Vec4f>(y, x);
            var inputColor = new Vec3f(pixel.Item2, pixel.Item1, pixel.Item0);
            
            // 应用LUT变换
            var transformedColor = InterpolateLUT(lut, inputColor);
            
            // 保持Alpha通道不变
            var outputPixel = new Vec4f(transformedColor.Item2, transformedColor.Item1, transformedColor.Item0, pixel.Item3);
            outputImage.Set(y, x, outputPixel);
        }
    }

    /// <summary>
    /// LUT插值
    /// </summary>
    private Vec3f InterpolateLUT(Cube3DLut lut, Vec3f inputColor)
    {
        // 将输入颜色映射到LUT网格坐标
        float GridPosition(float value, float min, float max) =>
            (float)(Math.Clamp(((double)value - min) / ((double)max - min), 0.0, 1.0) * (lut.GridSize - 1));
        float r = GridPosition(inputColor.Item0, lut.DomainMin.Item0, lut.DomainMax.Item0);
        float g = GridPosition(inputColor.Item1, lut.DomainMin.Item1, lut.DomainMax.Item1);
        float b = GridPosition(inputColor.Item2, lut.DomainMin.Item2, lut.DomainMax.Item2);

        if (InterpolationMethod == "最近邻")
        {
            return NearestNeighborInterpolation(lut, r, g, b);
        }
        else
        {
            return TrilinearInterpolation(lut, r, g, b);
        }
    }

    /// <summary>
    /// 最近邻插值
    /// </summary>
    private Vec3f NearestNeighborInterpolation(Cube3DLut lut, float r, float g, float b)
    {
        int ri = (int)Math.Round(r);
        int gi = (int)Math.Round(g);
        int bi = (int)Math.Round(b);
        
        ri = Math.Max(0, Math.Min(lut.GridSize - 1, ri));
        gi = Math.Max(0, Math.Min(lut.GridSize - 1, gi));
        bi = Math.Max(0, Math.Min(lut.GridSize - 1, bi));
        
        int index = bi * lut.GridSize * lut.GridSize + gi * lut.GridSize + ri;
        return lut.Data[index];
    }

    /// <summary>
    /// 三线性插值
    /// </summary>
    private Vec3f TrilinearInterpolation(Cube3DLut lut, float r, float g, float b)
    {
        int r0 = (int)Math.Floor(r);
        int g0 = (int)Math.Floor(g);
        int b0 = (int)Math.Floor(b);
        
        int r1 = Math.Min(r0 + 1, lut.GridSize - 1);
        int g1 = Math.Min(g0 + 1, lut.GridSize - 1);
        int b1 = Math.Min(b0 + 1, lut.GridSize - 1);
        
        float dr = r - r0;
        float dg = g - g0;
        float db = b - b0;
        
        // 获取8个角点的值
        var c000 = GetLUTValue(lut, r0, g0, b0);
        var c001 = GetLUTValue(lut, r0, g0, b1);
        var c010 = GetLUTValue(lut, r0, g1, b0);
        var c011 = GetLUTValue(lut, r0, g1, b1);
        var c100 = GetLUTValue(lut, r1, g0, b0);
        var c101 = GetLUTValue(lut, r1, g0, b1);
        var c110 = GetLUTValue(lut, r1, g1, b0);
        var c111 = GetLUTValue(lut, r1, g1, b1);
        
        // 三线性插值
        var c00 = Lerp(c000, c001, db);
        var c01 = Lerp(c010, c011, db);
        var c10 = Lerp(c100, c101, db);
        var c11 = Lerp(c110, c111, db);
        
        var c0 = Lerp(c00, c01, dg);
        var c1 = Lerp(c10, c11, dg);
        
        return Lerp(c0, c1, dr);
    }

    /// <summary>
    /// 获取LUT值
    /// </summary>
    private Vec3f GetLUTValue(Cube3DLut lut, int r, int g, int b)
    {
        int index = b * lut.GridSize * lut.GridSize + g * lut.GridSize + r;
        return lut.Data[index];
    }

    /// <summary>
    /// 线性插值
    /// </summary>
    private Vec3f Lerp(Vec3f a, Vec3f b, float t)
    {
        return new Vec3f(
            a.Item0 + (b.Item0 - a.Item0) * t,
            a.Item1 + (b.Item1 - a.Item1) * t,
            a.Item2 + (b.Item2 - a.Item2) * t
        );
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
        var viewModel = CreateViewModel() as LUTApplicationViewModel;
        mainPanel.DataContext = viewModel;

        // 标题
        var titleLabel = new Label { Content = "LUT应用设置" };
        if (resources.Contains("TitleLabelStyle")) titleLabel.Style = resources["TitleLabelStyle"] as Style;
        mainPanel.Children.Add(titleLabel);

        // 插值方式
        var interpolationLabel = new Label { Content = "插值方式:" };
        if(resources.Contains("DefaultLabelStyle")) interpolationLabel.Style = resources["DefaultLabelStyle"] as Style;
        mainPanel.Children.Add(interpolationLabel);

        var interpolationComboBox = new ComboBox { Margin = new Thickness(0, 0, 0, 10) };
        interpolationComboBox.Items.Add("三线性插值");
        interpolationComboBox.Items.Add("最近邻");
        interpolationComboBox.SetBinding(ComboBox.SelectedItemProperty, new Binding(nameof(viewModel.InterpolationMethod)) { Mode = BindingMode.TwoWay });
        mainPanel.Children.Add(interpolationComboBox);

        // 并行处理选项
        var parallelCheckBox = new CheckBox { Content = "启用并行处理", Margin = new Thickness(0, 5, 0, 10) };
        if(resources.Contains("DefaultCheckBoxStyle")) parallelCheckBox.Style = resources["DefaultCheckBoxStyle"] as Style;
        parallelCheckBox.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(viewModel.EnableParallelProcessing)) { Mode = BindingMode.TwoWay });
        mainPanel.Children.Add(parallelCheckBox);

        return mainPanel;
    }

    public override IScriptViewModel CreateViewModel()
    {
        return new LUTApplicationViewModel(this);
    }

    public override async Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
    {
        await Task.CompletedTask;
    }

    public override Dictionary<string, object> SerializeParameters()
    {
        return new Dictionary<string, object>
        {
            [nameof(InterpolationMethod)] = InterpolationMethod,
            [nameof(EnableParallelProcessing)] = EnableParallelProcessing,
            ["NodeInstanceId"] = NodeInstanceId
        };
    }

    public override void DeserializeParameters(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(InterpolationMethod), out var interpolation))
            InterpolationMethod = interpolation?.ToString() ?? "三线性插值";
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

public class LUTApplicationViewModel : ScriptViewModelBase
{
    private LUTApplicationScript LUTApplicationScript => (LUTApplicationScript)Script;

    public string InterpolationMethod
    {
        get => LUTApplicationScript.InterpolationMethod;
        set
        {
            if (LUTApplicationScript.InterpolationMethod != value)
            {
                LUTApplicationScript.InterpolationMethod = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(InterpolationMethod), value);
            }
        }
    }

    public bool EnableParallelProcessing
    {
        get => LUTApplicationScript.EnableParallelProcessing;
        set
        {
            if (LUTApplicationScript.EnableParallelProcessing != value)
            {
                LUTApplicationScript.EnableParallelProcessing = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(EnableParallelProcessing), value);
            }
        }
    }

    public LUTApplicationViewModel(LUTApplicationScript script) : base(script)
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
            [nameof(InterpolationMethod)] = InterpolationMethod,
            [nameof(EnableParallelProcessing)] = EnableParallelProcessing
        };
    }

    public override async Task SetParameterDataAsync(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(InterpolationMethod), out var interpolation))
            InterpolationMethod = interpolation?.ToString() ?? "三线性插值";
        if (data.TryGetValue(nameof(EnableParallelProcessing), out var enableParallel))
            EnableParallelProcessing = Convert.ToBoolean(enableParallel);
        await Task.CompletedTask;
    }

    public override async Task ResetToDefaultAsync()
    {
        InterpolationMethod = "三线性插值";
        EnableParallelProcessing = true;
        await Task.CompletedTask;
    }

    public override void Dispose()
    {
        base.Dispose();
    }
}

/// <summary>
/// 3D LUT数据结构
/// </summary>
public class Cube3DLut
{
    public int GridSize { get; set; }
    public Vec3f DomainMin { get; set; } = new Vec3f(0, 0, 0);
    public Vec3f DomainMax { get; set; } = new Vec3f(1, 1, 1);
    public Vec3f[] Data { get; set; }
}
