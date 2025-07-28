using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Tunnel_Next.Services.Scripting;
using OpenCvSharp;

public enum KernelPreset
{
    custom,
    sharpen,
    edge_detect,
    edge_detect_y,
    edge_enhance,
    emboss,
    blur,
    gaussian_blur,
    gradient_x,
    gradient_y,
    laplacian
}

[TunnelExtensionScript(
    Name = "卷积核节点",
    Author = "BEITAware",
    Description = "生成卷积核",
    Version = "1.0",
    Category = "卷积",
    Color = "#FF9900"
)]
public class ConvolutionKernelScript : TunnelExtensionScriptBase
{
    [ScriptParameter(DisplayName = "核类型", Description = "卷积核的类型", Order = 0)]
    public KernelPreset KernelType { get; set; } = KernelPreset.sharpen;

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
            case KernelPreset.custom:
                kernel = ParseCustomKernel();
                break;
            case KernelPreset.sharpen:
                kernel = CreateSharpenKernel(size);
                break;
            case KernelPreset.edge_detect:
                kernel = CreateEdgeDetectKernel(size);
                break;
            case KernelPreset.edge_detect_y:
                kernel = CreateEdgeDetectYKernel(size);
                break;
            case KernelPreset.edge_enhance:
                kernel = CreateEdgeEnhanceKernel(size);
                break;
            case KernelPreset.emboss:
                kernel = CreateEmbossKernel(size);
                break;
            case KernelPreset.blur:
                kernel = CreateBlurKernel(size);
                break;
            case KernelPreset.gaussian_blur:
                kernel = CreateGaussianBlurKernel(size);
                break;
            case KernelPreset.gradient_x:
                kernel = CreateGradientXKernel(size);
                break;
            case KernelPreset.gradient_y:
                kernel = CreateGradientYKernel(size);
                break;
            case KernelPreset.laplacian:
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
            if (KernelType == KernelPreset.blur || KernelType == KernelPreset.gaussian_blur)
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

        // 加载资源
        var resources = new ResourceDictionary();
        var resourcePaths = new[]
        {
            "/Tunnel-Next;component/Resources/ScriptsControls/SharedBrushes.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/LabelStyles.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/PanelStyles.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/ComboBoxStyles.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/TextBoxStyles.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/TextBlockStyles.xaml"
        };
        foreach (var path in resourcePaths)
        {
            try { resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(path, UriKind.Relative) }); }
            catch { /* 静默处理 */ }
        }

        if (resources.Contains("MainPanelStyle")) mainPanel.Style = resources["MainPanelStyle"] as Style;

        var viewModel = CreateViewModel() as ConvolutionKernelViewModel;
        mainPanel.DataContext = viewModel;

        // 标题
        var titleLabel = new Label { Content = "卷积核设置" };
        if (resources.Contains("TitleLabelStyle")) titleLabel.Style = resources["TitleLabelStyle"] as Style;
        mainPanel.Children.Add(titleLabel);

        // 预设核心
        var presetLabel = new Label { Content = "预设核心:", Margin = new Thickness(0, 5, 0, 0) };
        if(resources.Contains("DefaultLabelStyle")) presetLabel.Style = resources["DefaultLabelStyle"] as Style;
        mainPanel.Children.Add(presetLabel);

        var presetComboBox = new ComboBox
        {
            Margin = new Thickness(0, 0, 0, 10),
            ItemsSource = Enum.GetValues(typeof(KernelPreset)),
            SelectedItem = viewModel.KernelType
        };
        if(resources.Contains("DefaultComboBoxStyle")) presetComboBox.Style = resources["DefaultComboBoxStyle"] as Style;
        presetComboBox.SetBinding(ComboBox.SelectedItemProperty, new Binding("KernelType") { Mode = BindingMode.TwoWay });
        mainPanel.Children.Add(presetComboBox);

        // 自定义核心
        var customKernelLabel = new Label { Content = "自定义核心 (例如: 1,0,-1;2,0,-2;1,0,-1):" };
        if(resources.Contains("DefaultLabelStyle")) customKernelLabel.Style = resources["DefaultLabelStyle"] as Style;
        mainPanel.Children.Add(customKernelLabel);

        var customKernelTextBox = new TextBox
        {
            Margin = new Thickness(0, 0, 0, 10),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 80
        };
        if(resources.Contains("DefaultTextBoxStyle")) customKernelTextBox.Style = resources["DefaultTextBoxStyle"] as Style;
        customKernelTextBox.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding("CustomKernelString") { Mode = System.Windows.Data.BindingMode.TwoWay, UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged });
        mainPanel.Children.Add(customKernelTextBox);

        return mainPanel;
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
        {
            if (Enum.TryParse<KernelPreset>(kernelType.ToString(), out var preset))
            {
                KernelType = preset;
            }
        }
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

    public KernelPreset KernelType
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
            [nameof(KernelType)] = KernelType,
            [nameof(Size)] = Size,
            [nameof(Intensity)] = Intensity,
            [nameof(CustomKernel)] = CustomKernel
        };
    }

    public override async Task SetParameterDataAsync(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(KernelType), out var kernelType))
        {
            if (Enum.TryParse<KernelPreset>(kernelType.ToString(), out var preset))
            {
                ConvolutionKernelScript.KernelType = preset;
            }
        }
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
        await RunOnUIThreadAsync(() =>
        {
            KernelType = KernelPreset.sharpen;
            NotifyParameterChanged(nameof(KernelType), KernelType);

            Size = 3;
            NotifyParameterChanged(nameof(Size), Size);

            Intensity = 1.0;
            NotifyParameterChanged(nameof(Intensity), Intensity);

            CustomKernel = "0,0,0;0,1,0;0,0,0";
            NotifyParameterChanged(nameof(CustomKernel), CustomKernel);
        });
    }

    public override void Dispose()
    {
        base.Dispose();
    }
}
