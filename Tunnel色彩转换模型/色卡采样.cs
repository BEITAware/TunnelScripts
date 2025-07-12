using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Tunnel_Next.Services.Scripting;
using OpenCvSharp;

[RevivalScript(
    Name = "色卡采样",
    Author = "BEITAware",
    Description = "从色卡图像中采样颜色数据，输出F32bmp格式的采样数据",
    Version = "1.0",
    Category = "色彩转换",
    Color = "#FF6B6B"
)]
public class ColorCardSamplingScript : RevivalScriptBase
{
    [ScriptParameter(DisplayName = "行数", Description = "色卡行数", Order = 0)]
    public int Rows { get; set; } = 4;

    [ScriptParameter(DisplayName = "列数", Description = "色卡列数", Order = 1)]
    public int Columns { get; set; } = 6;

    [ScriptParameter(DisplayName = "采样区域大小", Description = "每个色块的采样区域大小（像素）", Order = 2)]
    public int RegionSize { get; set; } = 10;

    public string NodeInstanceId { get; set; } = string.Empty;

    public override Dictionary<string, PortDefinition> GetInputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            ["f32bmp"] = new PortDefinition("f32bmp", false, "输入色卡图像")
        };
    }

    public override Dictionary<string, PortDefinition> GetOutputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            ["f32bmp"] = new PortDefinition("f32bmp", false, "采样数据输出")
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
            // 确保输入是RGBA格式
            Mat workingMat = EnsureRGBAFormat(inputMat);
            
            // 获取色卡四个角点
            var corners = GetColorCheckerCorners(workingMat);
            
            // 计算色块中心点
            var centers = GetColorBlockCenters(corners, Rows, Columns);
            
            // 采样颜色数据
            var sampledColors = SampleColorsFromRegions(workingMat, centers, RegionSize);
            
            // 创建输出图像：每个采样点作为一个像素
            Mat outputMat = CreateSampledDataImage(sampledColors, Rows, Columns);
            
            workingMat.Dispose();
            
            return new Dictionary<string, object> { ["f32bmp"] = outputMat };
        }
        catch (Exception ex)
        {
            throw new ApplicationException($"色卡采样失败: {ex.Message}", ex);
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

    private Point2f[] GetColorCheckerCorners(Mat img)
    {
        int height = img.Rows;
        int width = img.Cols;
        
        return new Point2f[]
        {
            new Point2f(0, 0),                    // 左上角
            new Point2f(width - 1, 0),           // 右上角
            new Point2f(0, height - 1),          // 左下角
            new Point2f(width - 1, height - 1)   // 右下角
        };
    }

    private Point2f[] GetColorBlockCenters(Point2f[] corners, int rows, int cols)
    {
        var centers = new List<Point2f>();
        
        float xStep = (corners[1].X - corners[0].X) / cols;
        float yStep = (corners[2].Y - corners[0].Y) / rows;
        
        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < cols; j++)
            {
                float x = corners[0].X + (j + 0.5f) * xStep;
                float y = corners[0].Y + (i + 0.5f) * yStep;
                centers.Add(new Point2f(x, y));
            }
        }
        
        return centers.ToArray();
    }

    private Vec4f[] SampleColorsFromRegions(Mat img, Point2f[] centers, int regionSize)
    {
        var sampledColors = new Vec4f[centers.Length];
        
        for (int i = 0; i < centers.Length; i++)
        {
            var center = centers[i];
            int x = (int)center.X;
            int y = (int)center.Y;
            
            // 定义采样区域
            int x1 = Math.Max(0, x - regionSize);
            int y1 = Math.Max(0, y - regionSize);
            int x2 = Math.Min(img.Cols, x + regionSize);
            int y2 = Math.Min(img.Rows, y + regionSize);
            
            var roi = new OpenCvSharp.Rect(x1, y1, x2 - x1, y2 - y1);
            Mat region = img[roi];
            
            // 计算区域平均颜色
            Scalar meanColor = Cv2.Mean(region);
            sampledColors[i] = new Vec4f((float)meanColor[0], (float)meanColor[1], 
                                       (float)meanColor[2], (float)meanColor[3]);
        }
        
        return sampledColors;
    }

    private Mat CreateSampledDataImage(Vec4f[] sampledColors, int rows, int cols)
    {
        // 创建输出图像：rows x cols 的图像，每个像素存储一个采样颜色
        Mat outputMat = new Mat(rows, cols, MatType.CV_32FC4);
        
        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < cols; j++)
            {
                int index = i * cols + j;
                if (index < sampledColors.Length)
                {
                    outputMat.Set<Vec4f>(i, j, sampledColors[index]);
                }
            }
        }
        
        return outputMat;
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
        var viewModel = CreateViewModel() as ColorCardSamplingViewModel;
        mainPanel.DataContext = viewModel;

        // 标题
        var titleLabel = new Label { Content = "色卡采样" };
        if (resources.Contains("TitleLabelStyle")) titleLabel.Style = resources["TitleLabelStyle"] as Style;
        mainPanel.Children.Add(titleLabel);

        // 参数控件
        mainPanel.Children.Add(CreateSliderControl("行数", nameof(viewModel.Rows), 1, 10, viewModel, resources));
        mainPanel.Children.Add(CreateSliderControl("列数", nameof(viewModel.Columns), 1, 12, viewModel, resources));
        mainPanel.Children.Add(CreateSliderControl("采样区域大小", nameof(viewModel.RegionSize), 5, 50, viewModel, resources));

        return mainPanel;
    }

    private FrameworkElement CreateSliderControl(string label, string propertyName, double min, double max, ColorCardSamplingViewModel viewModel, ResourceDictionary resources)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 5, 0, 0) };

        var labelControl = new Label { Content = label };
        if (resources.Contains("DefaultLabelStyle")) labelControl.Style = resources["DefaultLabelStyle"] as Style;
        
        var slider = new Slider { Minimum = min, Maximum = max };
        if (resources.Contains("DefaultSliderStyle")) slider.Style = resources["DefaultSliderStyle"] as Style;
        slider.SetBinding(Slider.ValueProperty, new Binding(propertyName) { Mode = BindingMode.TwoWay });
        
        panel.Children.Add(labelControl);
        panel.Children.Add(slider);
        
        return panel;
    }

    public override IScriptViewModel CreateViewModel()
    {
        return new ColorCardSamplingViewModel(this);
    }

    public override async Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
    {
        await Task.CompletedTask;
    }

    public override Dictionary<string, object> SerializeParameters()
    {
        return new Dictionary<string, object>
        {
            [nameof(Rows)] = Rows,
            [nameof(Columns)] = Columns,
            [nameof(RegionSize)] = RegionSize,
            ["NodeInstanceId"] = NodeInstanceId
        };
    }

    public override void DeserializeParameters(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(Rows), out var rows))
            Rows = Convert.ToInt32(rows);
        if (data.TryGetValue(nameof(Columns), out var columns))
            Columns = Convert.ToInt32(columns);
        if (data.TryGetValue(nameof(RegionSize), out var regionSize))
            RegionSize = Convert.ToInt32(regionSize);
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

public class ColorCardSamplingViewModel : ScriptViewModelBase
{
    private ColorCardSamplingScript ColorCardSamplingScript => (ColorCardSamplingScript)Script;

    public int Rows
    {
        get => ColorCardSamplingScript.Rows;
        set
        {
            if (ColorCardSamplingScript.Rows != value)
            {
                ColorCardSamplingScript.Rows = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(Rows), value);
            }
        }
    }

    public int Columns
    {
        get => ColorCardSamplingScript.Columns;
        set
        {
            if (ColorCardSamplingScript.Columns != value)
            {
                ColorCardSamplingScript.Columns = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(Columns), value);
            }
        }
    }

    public int RegionSize
    {
        get => ColorCardSamplingScript.RegionSize;
        set
        {
            if (ColorCardSamplingScript.RegionSize != value)
            {
                ColorCardSamplingScript.RegionSize = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(RegionSize), value);
            }
        }
    }

    public ColorCardSamplingViewModel(ColorCardSamplingScript script) : base(script)
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
            [nameof(Rows)] = Rows,
            [nameof(Columns)] = Columns,
            [nameof(RegionSize)] = RegionSize
        };
    }

    public override async Task SetParameterDataAsync(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(Rows), out var rows))
            Rows = Convert.ToInt32(rows);
        if (data.TryGetValue(nameof(Columns), out var columns))
            Columns = Convert.ToInt32(columns);
        if (data.TryGetValue(nameof(RegionSize), out var regionSize))
            RegionSize = Convert.ToInt32(regionSize);
        await Task.CompletedTask;
    }

    public override async Task ResetToDefaultAsync()
    {
        Rows = 4;
        Columns = 6;
        RegionSize = 10;
        await Task.CompletedTask;
    }

    public override void Dispose()
    {
        base.Dispose();
    }
}
