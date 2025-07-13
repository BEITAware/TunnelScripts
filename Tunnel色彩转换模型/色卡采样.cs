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

[RevivalScript(
    Name = "色卡采样",
    Author = "BEITAware",
    Description = "从色卡图像中采样颜色数据，输出采样数值和可视化图像",
    Version = "1.0",
    Category = "Tunnel色彩转换模型",
    Color = "#FF6B35"
)]
public class ColorCardSamplingScript : RevivalScriptBase
{
    [ScriptParameter(DisplayName = "行数", Description = "色卡的行数", Order = 0)]
    public int Rows { get; set; } = 4;

    [ScriptParameter(DisplayName = "列数", Description = "色卡的列数", Order = 1)]
    public int Columns { get; set; } = 6;

    [ScriptParameter(DisplayName = "采样区域大小", Description = "每个色块的采样区域半径", Order = 2)]
    public int SampleRegionSize { get; set; } = 10;

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
            ["samples"] = new PortDefinition("f32bmp", false, "采样数据（每个像素为一个采样点）"),
            ["visualization"] = new PortDefinition("f32bmp", false, "可视化图像（原图叠加采样区域）")
        };
    }

    public override Dictionary<string, object> Process(Dictionary<string, object> inputs, IScriptContext context)
    {
        if (!inputs.TryGetValue("f32bmp", out var inputObj) || inputObj == null)
        {
            return new Dictionary<string, object> { ["samples"] = null, ["visualization"] = null };
        }

        if (!(inputObj is Mat inputMat) || inputMat.Empty())
        {
            return new Dictionary<string, object> { ["samples"] = null, ["visualization"] = null };
        }

        try
        {
            // 确保输入是RGBA格式
            Mat workingMat = EnsureRGBAFormat(inputMat);
            
            // 获取色卡角点
            var corners = GetColorCheckerCorners(workingMat);
            
            // 计算色块中心点
            var centers = GetColorBlockCenters(corners, Rows, Columns);
            
            // 采样颜色数据
            var sampledColors = SampleColorsFromRegions(workingMat, centers, SampleRegionSize);
            
            // 创建采样数据图像
            Mat samplesImage = CreateSamplesImage(sampledColors);
            
            // 创建可视化图像
            Mat visualizationImage = CreateVisualizationImage(workingMat, centers, SampleRegionSize);

            return new Dictionary<string, object>
            {
                ["samples"] = samplesImage,
                ["visualization"] = visualizationImage
            };
        }
        catch (Exception ex)
        {
            throw new ApplicationException($"色卡采样处理失败: {ex.Message}", ex);
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
    /// 获取色卡的四个角点（假设色卡占据整个图像）
    /// </summary>
    private Point2f[] GetColorCheckerCorners(Mat image)
    {
        int width = image.Width;
        int height = image.Height;
        
        return new Point2f[]
        {
            new Point2f(0, 0),                    // 左上
            new Point2f(width - 1, 0),           // 右上
            new Point2f(0, height - 1),          // 左下
            new Point2f(width - 1, height - 1)   // 右下
        };
    }

    /// <summary>
    /// 计算色块中心点
    /// </summary>
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

    /// <summary>
    /// 从指定区域采样颜色
    /// </summary>
    private Vec4f[] SampleColorsFromRegions(Mat image, Point2f[] centers, int regionSize)
    {
        var sampledColors = new List<Vec4f>();

        foreach (var center in centers)
        {
            int x = (int)Math.Round(center.X);
            int y = (int)Math.Round(center.Y);

            // 定义采样区域
            int x1 = Math.Max(0, x - regionSize);
            int y1 = Math.Max(0, y - regionSize);
            int x2 = Math.Min(image.Width - 1, x + regionSize);
            int y2 = Math.Min(image.Height - 1, y + regionSize);

            // 采样区域内的平均颜色
            using (var roi = new Mat(image, new OpenCvSharp.Rect(x1, y1, x2 - x1 + 1, y2 - y1 + 1)))
            {
                var mean = Cv2.Mean(roi);
                sampledColors.Add(new Vec4f((float)mean.Val0, (float)mean.Val1, (float)mean.Val2, (float)mean.Val3));
            }
        }

        return sampledColors.ToArray();
    }

    /// <summary>
    /// 创建采样数据图像（每个像素代表一个采样点）
    /// </summary>
    private Mat CreateSamplesImage(Vec4f[] sampledColors)
    {
        int totalSamples = sampledColors.Length;
        int width = Columns;
        int height = Rows;

        Mat samplesImage = new Mat(height, width, MatType.CV_32FC4);

        for (int i = 0; i < totalSamples; i++)
        {
            int row = i / Columns;
            int col = i % Columns;

            if (row < height && col < width)
            {
                samplesImage.Set(row, col, sampledColors[i]);
            }
        }

        return samplesImage;
    }

    /// <summary>
    /// 创建可视化图像（原图叠加采样区域标记）
    /// </summary>
    private Mat CreateVisualizationImage(Mat originalImage, Point2f[] centers, int regionSize)
    {
        Mat visualizationImage = originalImage.Clone();

        // 在每个采样中心绘制标记
        foreach (var center in centers)
        {
            OpenCvSharp.Point centerPoint = new OpenCvSharp.Point((int)Math.Round(center.X), (int)Math.Round(center.Y));

            // 绘制采样区域边框
            OpenCvSharp.Point topLeft = new OpenCvSharp.Point(centerPoint.X - regionSize, centerPoint.Y - regionSize);
            OpenCvSharp.Point bottomRight = new OpenCvSharp.Point(centerPoint.X + regionSize, centerPoint.Y + regionSize);

            Cv2.Rectangle(visualizationImage, topLeft, bottomRight, new Scalar(1.0, 0.0, 0.0, 1.0), 2);

            // 绘制中心点
            Cv2.Circle(visualizationImage, centerPoint, 3, new Scalar(0.0, 1.0, 0.0, 1.0), -1);
        }

        return visualizationImage;
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
        var viewModel = CreateViewModel() as ColorCardSamplingViewModel;
        mainPanel.DataContext = viewModel;

        // 标题
        var titleLabel = new Label { Content = "色卡采样设置" };
        if (resources.Contains("TitleLabelStyle")) titleLabel.Style = resources["TitleLabelStyle"] as Style;
        mainPanel.Children.Add(titleLabel);

        // 行数
        var rowsLabel = new Label { Content = "行数:" };
        if(resources.Contains("DefaultLabelStyle")) rowsLabel.Style = resources["DefaultLabelStyle"] as Style;
        mainPanel.Children.Add(rowsLabel);

        var rowsTextBox = new TextBox { Margin = new Thickness(0, 0, 0, 10) };
        if(resources.Contains("DefaultTextBoxStyle")) rowsTextBox.Style = resources["DefaultTextBoxStyle"] as Style;
        rowsTextBox.SetBinding(TextBox.TextProperty, new Binding(nameof(viewModel.Rows)) { Mode = BindingMode.TwoWay });
        mainPanel.Children.Add(rowsTextBox);

        // 列数
        var columnsLabel = new Label { Content = "列数:" };
        if(resources.Contains("DefaultLabelStyle")) columnsLabel.Style = resources["DefaultLabelStyle"] as Style;
        mainPanel.Children.Add(columnsLabel);

        var columnsTextBox = new TextBox { Margin = new Thickness(0, 0, 0, 10) };
        if(resources.Contains("DefaultTextBoxStyle")) columnsTextBox.Style = resources["DefaultTextBoxStyle"] as Style;
        columnsTextBox.SetBinding(TextBox.TextProperty, new Binding(nameof(viewModel.Columns)) { Mode = BindingMode.TwoWay });
        mainPanel.Children.Add(columnsTextBox);

        // 采样区域大小
        var sampleSizeLabel = new Label { Content = "采样区域大小:" };
        if(resources.Contains("DefaultLabelStyle")) sampleSizeLabel.Style = resources["DefaultLabelStyle"] as Style;
        mainPanel.Children.Add(sampleSizeLabel);

        var sampleSizeTextBox = new TextBox { Margin = new Thickness(0, 0, 0, 10) };
        if(resources.Contains("DefaultTextBoxStyle")) sampleSizeTextBox.Style = resources["DefaultTextBoxStyle"] as Style;
        sampleSizeTextBox.SetBinding(TextBox.TextProperty, new Binding(nameof(viewModel.SampleRegionSize)) { Mode = BindingMode.TwoWay });
        mainPanel.Children.Add(sampleSizeTextBox);

        return mainPanel;
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
            [nameof(SampleRegionSize)] = SampleRegionSize,
            ["NodeInstanceId"] = NodeInstanceId
        };
    }

    public override void DeserializeParameters(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(Rows), out var rows))
            Rows = Convert.ToInt32(rows);
        if (data.TryGetValue(nameof(Columns), out var columns))
            Columns = Convert.ToInt32(columns);
        if (data.TryGetValue(nameof(SampleRegionSize), out var sampleSize))
            SampleRegionSize = Convert.ToInt32(sampleSize);
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

    public int SampleRegionSize
    {
        get => ColorCardSamplingScript.SampleRegionSize;
        set
        {
            if (ColorCardSamplingScript.SampleRegionSize != value)
            {
                ColorCardSamplingScript.SampleRegionSize = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(SampleRegionSize), value);
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
            [nameof(SampleRegionSize)] = SampleRegionSize
        };
    }

    public override async Task SetParameterDataAsync(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(Rows), out var rows))
            Rows = Convert.ToInt32(rows);
        if (data.TryGetValue(nameof(Columns), out var columns))
            Columns = Convert.ToInt32(columns);
        if (data.TryGetValue(nameof(SampleRegionSize), out var sampleSize))
            SampleRegionSize = Convert.ToInt32(sampleSize);
        await Task.CompletedTask;
    }

    public override async Task ResetToDefaultAsync()
    {
        Rows = 4;
        Columns = 6;
        SampleRegionSize = 10;
        await Task.CompletedTask;
    }

    public override void Dispose()
    {
        base.Dispose();
    }
}
