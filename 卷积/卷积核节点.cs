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
    Name = "卷积核节点",
    Author = "Revival Scripts",
    Description = "生成卷积核",
    Version = "1.0",
    Category = "卷积",
    Color = "#FF9900"
)]
public class ConvolutionKernelScript : RevivalScriptBase
{
    [ScriptParameter(DisplayName = "核类型", Description = "卷积核的类型", Order = 0)]
    public string KernelType { get; set; } = "sharpen";

    [ScriptParameter(DisplayName = "核大小", Description = "卷积核的大小（必须为奇数）", Order = 1)]
    public int Size { get; set; } = 3;

    [ScriptParameter(DisplayName = "强度", Description = "卷积核的强度系数 (0.1 到 5.0)", Order = 2)]
    public double Intensity { get; set; } = 1.0;

    [ScriptParameter(DisplayName = "自定义核", Description = "自定义卷积核矩阵，格式：行;列，例如：0,0,0;0,1,0;0,0,0", Order = 3)]
    public string CustomKernel { get; set; } = "0,0,0;0,1,0;0,0,0";

    public string NodeInstanceId { get; set; } = string.Empty;

    public override Dictionary<string, PortDefinition> GetInputPorts()
    {
        return new Dictionary<string, PortDefinition>();
    }

    public override Dictionary<string, PortDefinition> GetOutputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            ["kernel"] = new PortDefinition("kernel", false, "生成的卷积核")
        };
    }

    public override Dictionary<string, object> Process(Dictionary<string, object> inputs, IScriptContext context)
    {
        try
        {
            // 确保size为奇数
            int kernelSize = Size;
            if (kernelSize % 2 == 0)
            {
                kernelSize += 1;
            }

            // 创建卷积核
            Mat kernel = CreateKernel(kernelSize);

            if (kernel == null || kernel.Empty())
            {
                // 如果没能生成有效的核，返回单位核
                kernel = CreateDefaultKernel();
            }

            // 应用强度因子
            ApplyIntensity(kernel);

            // 调试信息
            System.Diagnostics.Debug.WriteLine($"生成卷积核: 类型={KernelType}, 大小={kernel.Rows}x{kernel.Cols}, 强度={Intensity}");

            // 打印卷积核内容（仅对小核）
            if (kernel.Rows <= 5 && kernel.Cols <= 5)
            {
                var kernelIndexer = kernel.GetGenericIndexer<float>();
                for (int i = 0; i < kernel.Rows; i++)
                {
                    var row = "";
                    for (int j = 0; j < kernel.Cols; j++)
                    {
                        row += $"{kernelIndexer[i, j]:F3} ";
                    }
                    System.Diagnostics.Debug.WriteLine($"核第{i}行: {row}");
                }
            }

            return new Dictionary<string, object> { ["kernel"] = kernel };
        }
        catch (Exception ex)
        {
            throw new ApplicationException($"卷积核生成错误: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 创建卷积核
    /// </summary>
    private Mat CreateKernel(int size)
    {
        Mat kernel = null;

        switch (KernelType)
        {
            case "custom":
                kernel = ParseCustomKernel();
                break;
            case "sharpen":
                kernel = CreateSharpenKernel(size);
                break;
            case "edge_detect":
                kernel = CreateEdgeDetectKernel(size);
                break;
            case "edge_detect_y":
                kernel = CreateEdgeDetectYKernel(size);
                break;
            case "edge_enhance":
                kernel = CreateEdgeEnhanceKernel(size);
                break;
            case "emboss":
                kernel = CreateEmbossKernel(size);
                break;
            case "blur":
                kernel = CreateBlurKernel(size);
                break;
            case "gaussian_blur":
                kernel = CreateGaussianBlurKernel(size);
                break;
            case "gradient_x":
                kernel = CreateGradientXKernel(size);
                break;
            case "gradient_y":
                kernel = CreateGradientYKernel(size);
                break;
            case "laplacian":
                kernel = CreateLaplacianKernel(size);
                break;
            default:
                kernel = CreateDefaultKernel();
                break;
        }

        return kernel;
    }

    /// <summary>
    /// 解析自定义卷积核字符串
    /// </summary>
    private Mat ParseCustomKernel()
    {
        try
        {
            var rows = CustomKernel.Trim().Split(';');
            var kernelData = new List<List<float>>();
            
            foreach (var row in rows)
            {
                var rowData = new List<float>();
                var values = row.Trim().Split(',');
                foreach (var value in values)
                {
                    if (float.TryParse(value.Trim(), out float val))
                    {
                        rowData.Add(val);
                    }
                }
                if (rowData.Count > 0)
                {
                    kernelData.Add(rowData);
                }
            }

            if (kernelData.Count > 0 && kernelData[0].Count > 0)
            {
                int rows_count = kernelData.Count;
                int cols_count = kernelData[0].Count;
                
                // 检查形状是否规则
                if (rows_count != cols_count || rows_count % 2 == 0)
                {
                    return CreateDefaultKernel();
                }
                
                var kernel2D = new float[rows_count, cols_count];
                for (int i = 0; i < rows_count; i++)
                {
                    for (int j = 0; j < Math.Min(cols_count, kernelData[i].Count); j++)
                    {
                        kernel2D[i, j] = kernelData[i][j];
                    }
                }
                
                return CreateMatFromArray(kernel2D);
            }
        }
        catch (Exception)
        {
            // 解析失败，使用默认核
        }

        return CreateDefaultKernel();
    }

    /// <summary>
    /// 从二维数组创建Mat
    /// </summary>
    private Mat CreateMatFromArray(float[,] data)
    {
        int rows = data.GetLength(0);
        int cols = data.GetLength(1);

        var mat = new Mat(rows, cols, MatType.CV_32F);
        var indexer = mat.GetGenericIndexer<float>();

        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < cols; j++)
            {
                indexer[i, j] = data[i, j];
            }
        }

        return mat;
    }

    /// <summary>
    /// 创建锐化核
    /// </summary>
    private Mat CreateSharpenKernel(int size)
    {
        if (size == 3)
        {
            return CreateMatFromArray(new float[,]
            {
                { 0, -1, 0 },
                { -1, 5, -1 },
                { 0, -1, 0 }
            });
        }
        else
        {
            // 对于更大尺寸，生成扩展的锐化核
            var kernelData = new float[size, size];
            int center = size / 2;

            for (int i = 0; i < size; i++)
            {
                for (int j = 0; j < size; j++)
                {
                    int dist = Math.Abs(i - center) + Math.Abs(j - center);
                    if (i == center && j == center)
                    {
                        kernelData[i, j] = 1.0f + 4.0f * (size / 2);
                    }
                    else if (dist <= size / 2)
                    {
                        kernelData[i, j] = -1.0f;
                    }
                }
            }

            return CreateMatFromArray(kernelData);
        }
    }

    /// <summary>
    /// 创建边缘检测核 (Sobel X)
    /// </summary>
    private Mat CreateEdgeDetectKernel(int size)
    {
        if (size == 3)
        {
            return CreateMatFromArray(new float[,]
            {
                { -1, 0, 1 },
                { -2, 0, 2 },
                { -1, 0, 1 }
            });
        }
        else
        {
            var kernelData = new float[size, size];
            int center = size / 2;
            
            for (int i = 0; i < size; i++)
            {
                for (int j = 0; j < size; j++)
                {
                    if (j < center)
                    {
                        kernelData[i, j] = -1.0f - (center - j);
                    }
                    else if (j > center)
                    {
                        kernelData[i, j] = 1.0f + (j - center);
                    }
                }
            }
            
            return CreateMatFromArray(kernelData);
        }
    }

    /// <summary>
    /// 创建垂直边缘检测核 (Sobel Y)
    /// </summary>
    private Mat CreateEdgeDetectYKernel(int size)
    {
        if (size == 3)
        {
            return CreateMatFromArray(new float[,]
            {
                { -1, -2, -1 },
                { 0, 0, 0 },
                { 1, 2, 1 }
            });
        }
        else
        {
            var kernelData = new float[size, size];
            int center = size / 2;
            
            for (int i = 0; i < size; i++)
            {
                for (int j = 0; j < size; j++)
                {
                    if (i < center)
                    {
                        kernelData[i, j] = -1.0f - (center - i);
                    }
                    else if (i > center)
                    {
                        kernelData[i, j] = 1.0f + (i - center);
                    }
                }
            }
            
            return CreateMatFromArray(kernelData);
        }
    }

    /// <summary>
    /// 创建边缘增强核
    /// </summary>
    private Mat CreateEdgeEnhanceKernel(int size)
    {
        if (size == 3)
        {
            return CreateMatFromArray(new float[,]
            {
                { 0, 0, 0 },
                { -1, 1, 0 },
                { 0, 0, 0 }
            });
        }
        else
        {
            var kernelData = new float[size, size];
            int center = size / 2;
            kernelData[center, center] = 1.0f;
            kernelData[center, center - 1] = -0.5f;
            kernelData[center - 1, center] = -0.5f;

            return CreateMatFromArray(kernelData);
        }
    }

    /// <summary>
    /// 创建浮雕效果核
    /// </summary>
    private Mat CreateEmbossKernel(int size)
    {
        if (size == 3)
        {
            return CreateMatFromArray(new float[,]
            {
                { -2, -1, 0 },
                { -1, 1, 1 },
                { 0, 1, 2 }
            });
        }
        else
        {
            var kernelData = new float[size, size];
            int center = size / 2;

            for (int i = 0; i < size; i++)
            {
                for (int j = 0; j < size; j++)
                {
                    int dist_i = i - center;
                    int dist_j = j - center;
                    if (dist_i < 0 && dist_j < 0)
                    {
                        kernelData[i, j] = -1.0f - Math.Min(Math.Abs(dist_i), Math.Abs(dist_j));
                    }
                    else if (dist_i > 0 && dist_j > 0)
                    {
                        kernelData[i, j] = 1.0f + Math.Min(dist_i, dist_j);
                    }
                    else if (i == center && j == center)
                    {
                        kernelData[i, j] = 1.0f;
                    }
                }
            }

            return CreateMatFromArray(kernelData);
        }
    }

    /// <summary>
    /// 创建均值模糊核 (Box blur)
    /// </summary>
    private Mat CreateBlurKernel(int size)
    {
        var kernelData = new float[size, size];
        float value = 1.0f / (size * size);

        for (int i = 0; i < size; i++)
        {
            for (int j = 0; j < size; j++)
            {
                kernelData[i, j] = value;
            }
        }

        return CreateMatFromArray(kernelData);
    }

    /// <summary>
    /// 创建高斯模糊核
    /// </summary>
    private Mat CreateGaussianBlurKernel(int size)
    {
        if (size == 3)
        {
            return CreateMatFromArray(new float[,]
            {
                { 1f/16f, 2f/16f, 1f/16f },
                { 2f/16f, 4f/16f, 2f/16f },
                { 1f/16f, 2f/16f, 1f/16f }
            });
        }
        else
        {
            var kernelData = new float[size, size];
            int center = size / 2;
            double sigma = 0.3 * ((size - 1) * 0.5 - 1) + 0.8; // 动态计算sigma

            double sum = 0.0;
            for (int i = 0; i < size; i++)
            {
                for (int j = 0; j < size; j++)
                {
                    int x = i - center;
                    int y = j - center;
                    kernelData[i, j] = (float)Math.Exp(-(x * x + y * y) / (2 * sigma * sigma));
                    sum += kernelData[i, j];
                }
            }

            // 归一化
            if (sum != 0)
            {
                for (int i = 0; i < size; i++)
                {
                    for (int j = 0; j < size; j++)
                    {
                        kernelData[i, j] /= (float)sum;
                    }
                }
            }

            return CreateMatFromArray(kernelData);
        }
    }

    /// <summary>
    /// 创建X方向梯度核 (Prewitt)
    /// </summary>
    private Mat CreateGradientXKernel(int size)
    {
        var kernelData = new float[size, size];
        int center = size / 2;

        for (int i = 0; i < size; i++)
        {
            for (int j = 0; j < size; j++)
            {
                if (j < center)
                {
                    kernelData[i, j] = -1.0f;
                }
                else if (j > center)
                {
                    kernelData[i, j] = 1.0f;
                }
            }
        }

        return CreateMatFromArray(kernelData);
    }

    /// <summary>
    /// 创建Y方向梯度核 (Prewitt)
    /// </summary>
    private Mat CreateGradientYKernel(int size)
    {
        var kernelData = new float[size, size];
        int center = size / 2;

        for (int i = 0; i < size; i++)
        {
            for (int j = 0; j < size; j++)
            {
                if (i < center)
                {
                    kernelData[i, j] = -1.0f;
                }
                else if (i > center)
                {
                    kernelData[i, j] = 1.0f;
                }
            }
        }

        return CreateMatFromArray(kernelData);
    }

    /// <summary>
    /// 创建拉普拉斯算子 (用于边缘检测)
    /// </summary>
    private Mat CreateLaplacianKernel(int size)
    {
        if (size == 3)
        {
            return CreateMatFromArray(new float[,]
            {
                { 0, 1, 0 },
                { 1, -4, 1 },
                { 0, 1, 0 }
            });
        }
        else
        {
            var kernelData = new float[size, size];
            int center = size / 2;

            for (int i = 0; i < size; i++)
            {
                for (int j = 0; j < size; j++)
                {
                    if ((i == center && Math.Abs(j - center) == 1) ||
                        (j == center && Math.Abs(i - center) == 1))
                    {
                        kernelData[i, j] = 1.0f;
                    }
                }
            }
            kernelData[center, center] = -4.0f; // 中心值等于相邻单元格数量的负数

            return CreateMatFromArray(kernelData);
        }
    }

    /// <summary>
    /// 创建默认单位核
    /// </summary>
    private Mat CreateDefaultKernel()
    {
        return CreateMatFromArray(new float[,]
        {
            { 0, 0, 0 },
            { 0, 1, 0 },
            { 0, 0, 0 }
        });
    }

    /// <summary>
    /// 应用强度因子
    /// </summary>
    private void ApplyIntensity(Mat kernel)
    {
        if (Math.Abs(Intensity - 1.0) > 0.001)
        {
            // 对于某些滤波器（如模糊），应该保持核的和为1
            if (KernelType == "blur" || KernelType == "gaussian_blur")
            {
                // 重新归一化核以保持和为1
                Scalar kernelSum = Cv2.Sum(kernel);
                kernel.ConvertTo(kernel, -1, Intensity);
                Scalar newSum = Cv2.Sum(kernel);
                if (Math.Abs(newSum.Val0) > 0.001)
                {
                    kernel.ConvertTo(kernel, -1, kernelSum.Val0 / newSum.Val0);
                }
            }
            else
            {
                // 对于其他滤波器，直接应用强度
                kernel.ConvertTo(kernel, -1, Intensity);
            }
        }
    }

    public override FrameworkElement CreateParameterControl()
    {
        var mainPanel = new StackPanel { Margin = new Thickness(5) };

        // 应用Aero主题样式
        mainPanel.Background = new LinearGradientBrush(
            new GradientStopCollection
            {
                new GradientStop((Color)ColorConverter.ConvertFromString("#FF1A1F28"), 0),
                new GradientStop((Color)ColorConverter.ConvertFromString("#FF1C2432"), 0.510204),
                new GradientStop((Color)ColorConverter.ConvertFromString("#FE1C2533"), 0.562152),
                new GradientStop((Color)ColorConverter.ConvertFromString("#FE30445F"), 0.87013),
                new GradientStop((Color)ColorConverter.ConvertFromString("#FE384F6C"), 0.918367),
                new GradientStop((Color)ColorConverter.ConvertFromString("#FF405671"), 0.974026)
            },
            new System.Windows.Point(0.499999, 0), new System.Windows.Point(0.499999, 1)
        );

        // 创建ViewModel
        var viewModel = CreateViewModel() as ConvolutionKernelViewModel;
        mainPanel.DataContext = viewModel;

        // 标题
        var titleLabel = new Label
        {
            Content = "卷积核生成",
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial")
        };
        mainPanel.Children.Add(titleLabel);

        // 核类型下拉框
        var kernelTypePanel = new StackPanel { Margin = new Thickness(0, 5, 0, 5) };
        var kernelTypeLabel = new Label
        {
            Content = "核类型:",
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11
        };
        kernelTypePanel.Children.Add(kernelTypeLabel);

        var kernelTypeCombo = new ComboBox
        {
            Margin = new Thickness(5, 0, 5, 0),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11
        };
        kernelTypeCombo.Items.Add("custom");
        kernelTypeCombo.Items.Add("sharpen");
        kernelTypeCombo.Items.Add("edge_detect");
        kernelTypeCombo.Items.Add("edge_detect_y");
        kernelTypeCombo.Items.Add("edge_enhance");
        kernelTypeCombo.Items.Add("emboss");
        kernelTypeCombo.Items.Add("blur");
        kernelTypeCombo.Items.Add("gaussian_blur");
        kernelTypeCombo.Items.Add("gradient_x");
        kernelTypeCombo.Items.Add("gradient_y");
        kernelTypeCombo.Items.Add("laplacian");

        var kernelTypeBinding = new Binding(nameof(KernelType))
        {
            Source = viewModel,
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        };
        kernelTypeCombo.SetBinding(ComboBox.SelectedValueProperty, kernelTypeBinding);
        kernelTypePanel.Children.Add(kernelTypeCombo);
        mainPanel.Children.Add(kernelTypePanel);

        // 核大小下拉框
        var sizePanel = new StackPanel { Margin = new Thickness(0, 5, 0, 5) };
        var sizeLabel = new Label
        {
            Content = "核大小:",
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11
        };
        sizePanel.Children.Add(sizeLabel);

        var sizeCombo = new ComboBox
        {
            Margin = new Thickness(5, 0, 5, 0),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11
        };
        sizeCombo.Items.Add(3);
        sizeCombo.Items.Add(5);
        sizeCombo.Items.Add(7);
        sizeCombo.Items.Add(9);

        var sizeBinding = new Binding(nameof(Size))
        {
            Source = viewModel,
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        };
        sizeCombo.SetBinding(ComboBox.SelectedValueProperty, sizeBinding);
        sizePanel.Children.Add(sizeCombo);
        mainPanel.Children.Add(sizePanel);

        // 强度滑块
        mainPanel.Children.Add(CreateSliderControl("强度", nameof(Intensity), 0.1, 5.0, viewModel));

        // 自定义核文本框
        var customKernelPanel = new StackPanel { Margin = new Thickness(0, 5, 0, 5) };
        var customKernelLabel = new Label
        {
            Content = "自定义核:",
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11
        };
        customKernelPanel.Children.Add(customKernelLabel);

        var customKernelTextBox = new TextBox
        {
            Margin = new Thickness(5, 0, 5, 0),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11,
            Height = 60,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true
        };

        var customKernelBinding = new Binding(nameof(CustomKernel))
        {
            Source = viewModel,
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        };
        customKernelTextBox.SetBinding(TextBox.TextProperty, customKernelBinding);
        customKernelPanel.Children.Add(customKernelTextBox);
        mainPanel.Children.Add(customKernelPanel);

        return mainPanel;
    }

    private StackPanel CreateSliderControl(string label, string propertyName, double min, double max, ConvolutionKernelViewModel viewModel)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 5, 0, 5) };

        // 标签和值显示
        var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };

        var labelControl = new Label
        {
            Content = label + ":",
            Width = 80,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11
        };

        var valueLabel = new Label
        {
            Width = 60,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11,
            HorizontalContentAlignment = HorizontalAlignment.Right
        };

        headerPanel.Children.Add(labelControl);
        headerPanel.Children.Add(valueLabel);
        panel.Children.Add(headerPanel);

        // 滑块
        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            TickFrequency = (max - min) / 20,
            IsSnapToTickEnabled = false,
            Margin = new Thickness(5, 0, 5, 0)
        };

        // 数据绑定
        var binding = new Binding(propertyName)
        {
            Source = viewModel,
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        };
        slider.SetBinding(Slider.ValueProperty, binding);

        var valueBinding = new Binding(propertyName)
        {
            Source = viewModel,
            Mode = BindingMode.OneWay,
            StringFormat = "F2"
        };
        valueLabel.SetBinding(Label.ContentProperty, valueBinding);

        panel.Children.Add(slider);
        return panel;
    }

    public override IScriptViewModel CreateViewModel()
    {
        return new ConvolutionKernelViewModel(this);
    }

    public override async Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
    {
        await Task.CompletedTask;
    }

    public override Dictionary<string, object> SerializeParameters()
    {
        return new Dictionary<string, object>
        {
            [nameof(KernelType)] = KernelType,
            [nameof(Size)] = Size,
            [nameof(Intensity)] = Intensity,
            [nameof(CustomKernel)] = CustomKernel,
            ["NodeInstanceId"] = NodeInstanceId
        };
    }

    public override void DeserializeParameters(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(KernelType), out var kernelType))
            KernelType = kernelType?.ToString() ?? "custom";
        if (data.TryGetValue(nameof(Size), out var size))
            Size = Convert.ToInt32(size);
        if (data.TryGetValue(nameof(Intensity), out var intensity))
            Intensity = Convert.ToDouble(intensity);
        if (data.TryGetValue(nameof(CustomKernel), out var customKernel))
            CustomKernel = customKernel?.ToString() ?? "0,0,0;0,1,0;0,0,0";
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

public class ConvolutionKernelViewModel : ScriptViewModelBase
{
    private ConvolutionKernelScript ConvolutionKernelScript => (ConvolutionKernelScript)Script;

    public string KernelType
    {
        get => ConvolutionKernelScript.KernelType;
        set
        {
            if (ConvolutionKernelScript.KernelType != value)
            {
                ConvolutionKernelScript.KernelType = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(KernelType), value);
            }
        }
    }

    public int Size
    {
        get => ConvolutionKernelScript.Size;
        set
        {
            if (ConvolutionKernelScript.Size != value)
            {
                ConvolutionKernelScript.Size = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(Size), value);
            }
        }
    }

    public double Intensity
    {
        get => ConvolutionKernelScript.Intensity;
        set
        {
            if (Math.Abs(ConvolutionKernelScript.Intensity - value) > 0.001)
            {
                ConvolutionKernelScript.Intensity = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(Intensity), value);
            }
        }
    }

    public string CustomKernel
    {
        get => ConvolutionKernelScript.CustomKernel;
        set
        {
            if (ConvolutionKernelScript.CustomKernel != value)
            {
                ConvolutionKernelScript.CustomKernel = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(CustomKernel), value);
            }
        }
    }

    public ConvolutionKernelViewModel(ConvolutionKernelScript script) : base(script)
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
            [nameof(KernelType)] = KernelType,
            [nameof(Size)] = Size,
            [nameof(Intensity)] = Intensity,
            [nameof(CustomKernel)] = CustomKernel
        };
    }

    public override async Task SetParameterDataAsync(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(KernelType), out var kernelType))
            KernelType = kernelType?.ToString() ?? "custom";
        if (data.TryGetValue(nameof(Size), out var size))
            Size = Convert.ToInt32(size);
        if (data.TryGetValue(nameof(Intensity), out var intensity))
            Intensity = Convert.ToDouble(intensity);
        if (data.TryGetValue(nameof(CustomKernel), out var customKernel))
            CustomKernel = customKernel?.ToString() ?? "0,0,0;0,1,0;0,0,0";
        await Task.CompletedTask;
    }

    public override async Task ResetToDefaultAsync()
    {
        KernelType = "sharpen";
        Size = 3;
        Intensity = 1.0;
        CustomKernel = "0,0,0;0,1,0;0,0,0";
        await Task.CompletedTask;
    }

    public override void Dispose()
    {
        base.Dispose();
    }
}
