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

[RevivalScript(
    Name = "LUT应用",
    Author = "BEITAware",
    Description = "将3D LUT应用到图像上进行颜色映射",
    Version = "1.0",
    Category = "映射",
    Color = "#3498DB"
)]
public class LUTApplicationScript : RevivalScriptBase
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
            Mat workingImage = EnsureRGBAFormat(imageMat);
            
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
        int gridSize = 33; // 默认值
        var lutValues = new List<Vec3f>();

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            
            // 跳过注释和空行
            if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith("#"))
                continue;

            // 解析LUT_3D_SIZE
            if (trimmedLine.StartsWith("LUT_3D_SIZE"))
            {
                var parts = trimmedLine.Split(' ');
                if (parts.Length >= 2 && int.TryParse(parts[1], out int size))
                {
                    gridSize = size;
                }
                continue;
            }

            // 跳过其他头部信息
            if (trimmedLine.StartsWith("TITLE") || trimmedLine.StartsWith("DOMAIN_"))
                continue;

            // 解析LUT数据行
            var values = trimmedLine.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (values.Length >= 3)
            {
                if (float.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float r) &&
                    float.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float g) &&
                    float.TryParse(values[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float b))
                {
                    lutValues.Add(new Vec3f(r, g, b));
                }
            }
        }

        // 验证LUT数据完整性
        int expectedSize = gridSize * gridSize * gridSize;
        if (lutValues.Count != expectedSize)
        {
            throw new ArgumentException($"LUT数据不完整，期望 {expectedSize} 个值，实际 {lutValues.Count} 个");
        }

        return new Cube3DLut
        {
            GridSize = gridSize,
            Data = lutValues.ToArray()
        };
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
            var inputColor = new Vec3f(pixel.Item0, pixel.Item1, pixel.Item2);
            
            // 应用LUT变换
            var transformedColor = InterpolateLUT(lut, inputColor);
            
            // 保持Alpha通道不变
            var outputPixel = new Vec4f(transformedColor.Item0, transformedColor.Item1, transformedColor.Item2, pixel.Item3);
            outputImage.Set(y, x, outputPixel);
        }
    }

    /// <summary>
    /// LUT插值
    /// </summary>
    private Vec3f InterpolateLUT(Cube3DLut lut, Vec3f inputColor)
    {
        // 将输入颜色映射到LUT网格坐标
        float r = Math.Max(0, Math.Min(1, inputColor.Item0)) * (lut.GridSize - 1);
        float g = Math.Max(0, Math.Min(1, inputColor.Item1)) * (lut.GridSize - 1);
        float b = Math.Max(0, Math.Min(1, inputColor.Item2)) * (lut.GridSize - 1);

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
    public Vec3f[] Data { get; set; }
}
