using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Tunnel_Next.Services.Scripting;
using OpenCvSharp;

[TunnelExtensionScript(
    Name = "卷积",
    Author = "BEITAware",
    Description = "利用内置或输入的卷积核执行卷积运算",
    Version = "1.0",
    Category = "卷积",
    Color = "#FFFF00"
)]
public class ConvolutionScript : TunnelExtensionScriptBase
{
    [ScriptParameter(DisplayName = "内置核类型", Description = "当没有外部核输入时使用的内置卷积核类型", Order = 0)]
    public string KernelType { get; set; } = "sharpen";

    [ScriptParameter(DisplayName = "自定义卷积核", Description = "自定义卷积核矩阵，格式：行;列，例如：0,0,0;0,1,0;0,0,0", Order = 1)]
    public string CustomKernel { get; set; } = "0,0,0;0,1,0;0,0,0";

    [ScriptParameter(DisplayName = "内置核强度", Description = "内置卷积核的强度系数 (0.1 到 5.0)", Order = 2)]
    public double Intensity { get; set; } = 1.0;

    [ScriptParameter(DisplayName = "归一化卷积核", Description = "是否对卷积核进行归一化处理", Order = 3)]
    public bool Normalize { get; set; } = false;

    [ScriptParameter(DisplayName = "偏移值", Description = "卷积后添加的偏移值 (-1000 到 1000)", Order = 4)]
    public double Bias { get; set; } = 0;

    [ScriptParameter(DisplayName = "边界处理", Description = "图像边界的处理方式", Order = 5)]
    public string BorderType { get; set; } = "reflect_101";

    public string NodeInstanceId { get; set; } = string.Empty;

    public override Dictionary<string, PortDefinition> GetInputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            ["f32bmp"] = new PortDefinition("f32bmp", false, "输入图像"),
            ["kernel"] = new PortDefinition("kernel", false, "卷积核（可选）")
        };
    }

    public override Dictionary<string, PortDefinition> GetOutputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            ["f32bmp"] = new PortDefinition("f32bmp", false, "输出图像")
        };
    }

    public override Dictionary<string, object> Process(Dictionary<string, object> inputs, IScriptContext context)
    {
        // 检查图像输入是否存在
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
            // 创建输入图像的副本以避免修改原始数据
            Mat workingMat = inputMat.Clone();

            // 确定卷积核 - 优先使用外部输入的kernel
            Mat kernel = null;
            bool useExternalKernel = false;

            // 检查是否有外部卷积核输入
            if (inputs.TryGetValue("kernel", out var kernelObj) && kernelObj is Mat kernelMat && !kernelMat.Empty())
            {
                // 使用外部卷积核（优先级最高）
                useExternalKernel = true;
                System.Diagnostics.Debug.WriteLine($"使用外部卷积核，原始类型: {kernelMat.Type()}, 大小: {kernelMat.Rows}x{kernelMat.Cols}");

                // 创建外部核的副本并确保是float32类型
                if (kernelMat.Type() == MatType.CV_32F)
                {
                    kernel = kernelMat.Clone();
                }
                else
                {
                    kernel = new Mat();
                    kernelMat.ConvertTo(kernel, MatType.CV_32F);
                }

                // 检查外部核的有效性
                if (kernel.Rows % 2 == 0 || kernel.Cols % 2 == 0)
                {
                    System.Diagnostics.Debug.WriteLine("外部卷积核大小必须为奇数，使用内置核");
                    kernel.Dispose();
                    kernel = CreateInternalKernel();
                    useExternalKernel = false;
                }
                else
                {
                    // 打印外部核内容用于调试
                    if (kernel.Rows <= 7 && kernel.Cols <= 7)
                    {
                        var kernelIndexer = kernel.GetGenericIndexer<float>();
                        System.Diagnostics.Debug.WriteLine("外部卷积核内容:");
                        for (int i = 0; i < kernel.Rows; i++)
                        {
                            var row = "";
                            for (int j = 0; j < kernel.Cols; j++)
                            {
                                row += $"{kernelIndexer[i, j]:F3} ";
                            }
                            System.Diagnostics.Debug.WriteLine($"  第{i}行: {row}");
                        }
                    }
                }
            }
            else
            {
                // 没有外部核输入，使用内置卷积核
                kernel = CreateInternalKernel();
                useExternalKernel = false;
                System.Diagnostics.Debug.WriteLine($"使用内置卷积核: {KernelType}");
            }

            if (kernel == null || kernel.Empty())
            {
                // 如果无法创建有效的核，返回原始图像
                System.Diagnostics.Debug.WriteLine("无法创建有效卷积核，返回原图");
                return new Dictionary<string, object> { ["f32bmp"] = workingMat };
            }

            // 对内置核应用强度因子和归一化
            if (!useExternalKernel)
            {
                // 应用强度因子
                ApplyIntensityToKernel(kernel);

                // 如果选择了归一化，对卷积核进行归一化处理
                if (Normalize)
                {
                    NormalizeKernel(kernel);
                }
            }

            // 选择边界类型
            BorderTypes cvBorderType = GetBorderType();

            // 调试信息：检查卷积核
            System.Diagnostics.Debug.WriteLine($"卷积核大小: {kernel.Rows}x{kernel.Cols}, 类型: {kernel.Type()}");
            System.Diagnostics.Debug.WriteLine($"输入图像大小: {workingMat.Rows}x{workingMat.Cols}, 类型: {workingMat.Type()}");

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

            // 应用卷积处理
            Mat resultMat = ApplyConvolution(workingMat, kernel, cvBorderType);

            // 清理资源
            kernel?.Dispose();
            workingMat.Dispose();

            return new Dictionary<string, object> { ["f32bmp"] = resultMat };
        }
        catch (Exception ex)
        {
            throw new ApplicationException($"卷积处理错误: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 创建内部卷积核
    /// </summary>
    private Mat CreateInternalKernel()
    {
        try
        {
            Mat kernel = null;

            switch (KernelType)
            {
                case "custom":
                    kernel = ParseCustomKernel();
                    break;
                case "sharpen":
                    kernel = CreateMatFromArray(new float[,]
                    {
                        { 0, -1, 0 },
                        { -1, 5, -1 },
                        { 0, -1, 0 }
                    });
                    break;
                case "edge_detect":
                    kernel = CreateMatFromArray(new float[,]
                    {
                        { -1, 0, 1 },
                        { -2, 0, 2 },
                        { -1, 0, 1 }
                    });
                    break;
                case "edge_enhance":
                    kernel = CreateMatFromArray(new float[,]
                    {
                        { 0, 0, 0 },
                        { -1, 1, 0 },
                        { 0, 0, 0 }
                    });
                    break;
                case "emboss":
                    kernel = CreateMatFromArray(new float[,]
                    {
                        { -2, -1, 0 },
                        { -1, 1, 1 },
                        { 0, 1, 2 }
                    });
                    break;
                case "blur":
                    var blurData = new float[3, 3];
                    for (int i = 0; i < 3; i++)
                        for (int j = 0; j < 3; j++)
                            blurData[i, j] = 1.0f / 9.0f;
                    kernel = CreateMatFromArray(blurData);
                    break;
                case "gaussian_blur":
                    kernel = CreateMatFromArray(new float[,]
                    {
                        { 1f/16f, 2f/16f, 1f/16f },
                        { 2f/16f, 4f/16f, 2f/16f },
                        { 1f/16f, 2f/16f, 1f/16f }
                    });
                    break;
                default:
                    // 默认单位核
                    kernel = CreateMatFromArray(new float[,]
                    {
                        { 0, 0, 0 },
                        { 0, 1, 0 },
                        { 0, 0, 0 }
                    });
                    break;
            }

            return kernel;
        }
        catch (Exception)
        {
            // 解析失败时使用默认单位核
            return CreateMatFromArray(new float[,]
            {
                { 0, 0, 0 },
                { 0, 1, 0 },
                { 0, 0, 0 }
            });
        }
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

        // 解析失败时返回默认单位核
        return CreateMatFromArray(new float[,]
        {
            { 0, 0, 0 },
            { 0, 1, 0 },
            { 0, 0, 0 }
        });
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
    /// 对内部核应用强度因子
    /// </summary>
    private void ApplyIntensityToKernel(Mat kernel)
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
                if (newSum.Val0 != 0)
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

    /// <summary>
    /// 归一化卷积核
    /// </summary>
    private void NormalizeKernel(Mat kernel)
    {
        Scalar kernelSum = Cv2.Sum(kernel);
        if (Math.Abs(kernelSum.Val0) > 0.001)
        {
            kernel.ConvertTo(kernel, -1, 1.0 / kernelSum.Val0);
        }
    }

    /// <summary>
    /// 获取边界类型
    /// </summary>
    private BorderTypes GetBorderType()
    {
        return BorderType switch
        {
            "constant" => BorderTypes.Constant,
            "replicate" => BorderTypes.Replicate,
            "reflect" => BorderTypes.Reflect,
            "reflect_101" => BorderTypes.Reflect101,
            "isolated" => BorderTypes.Isolated,
            _ => BorderTypes.Reflect101
        };
    }

    /// <summary>
    /// 应用卷积处理
    /// </summary>
    private Mat ApplyConvolution(Mat inputMat, Mat kernel, BorderTypes borderType)
    {
        Mat resultMat = new Mat();

        System.Diagnostics.Debug.WriteLine($"开始卷积处理，输入类型: {inputMat.Type()}, 通道数: {inputMat.Channels()}, 大小: {inputMat.Rows}x{inputMat.Cols}");
        System.Diagnostics.Debug.WriteLine($"卷积核类型: {kernel.Type()}, 大小: {kernel.Rows}x{kernel.Cols}");

        try
        {
            // 确保卷积核是正确的类型
            Mat workingKernel = kernel;
            if (kernel.Type() != MatType.CV_32F)
            {
                workingKernel = new Mat();
                kernel.ConvertTo(workingKernel, MatType.CV_32F);
                System.Diagnostics.Debug.WriteLine($"卷积核类型转换为: {workingKernel.Type()}");
            }

            // 检查输入图像是否为多通道（特别是RGBA）
            if (inputMat.Channels() == 4) // RGBA
            {
                System.Diagnostics.Debug.WriteLine("处理RGBA图像，分离Alpha通道");

                // 分离通道
                Mat[] channels = Cv2.Split(inputMat);
                Mat[] resultChannels = new Mat[4];

                // 对RGB通道应用卷积，保持Alpha通道不变
                for (int i = 0; i < 3; i++) // 只处理RGB通道
                {
                    resultChannels[i] = new Mat();
                    Cv2.Filter2D(channels[i], resultChannels[i], -1, workingKernel,
                        new OpenCvSharp.Point(-1, -1), Bias, borderType);
                }

                // Alpha通道保持不变
                resultChannels[3] = channels[3].Clone();

                // 合并通道
                Cv2.Merge(resultChannels, resultMat);

                // 清理资源
                foreach (var ch in channels) ch.Dispose();
                foreach (var ch in resultChannels) ch.Dispose();
            }
            else if (inputMat.Channels() == 3) // RGB
            {
                System.Diagnostics.Debug.WriteLine("处理RGB图像");
                Cv2.Filter2D(inputMat, resultMat, -1, workingKernel,
                    new OpenCvSharp.Point(-1, -1), Bias, borderType);
            }
            else // 单通道
            {
                System.Diagnostics.Debug.WriteLine("处理单通道图像");
                Cv2.Filter2D(inputMat, resultMat, -1, workingKernel,
                    new OpenCvSharp.Point(-1, -1), Bias, borderType);
            }

            System.Diagnostics.Debug.WriteLine($"卷积完成，输出类型: {resultMat.Type()}, 通道数: {resultMat.Channels()}, 大小: {resultMat.Rows}x{resultMat.Cols}");

            // 检查结果是否为空
            if (resultMat.Empty())
            {
                System.Diagnostics.Debug.WriteLine("警告：卷积结果为空，返回原图");
                resultMat = inputMat.Clone();
            }

            // 清理临时核
            if (workingKernel != kernel)
            {
                workingKernel.Dispose();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"卷积处理异常: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"异常堆栈: {ex.StackTrace}");
            // 如果出错，返回原图
            resultMat.Dispose();
            resultMat = inputMat.Clone();
        }

        return resultMat;
    }

    public override FrameworkElement CreateParameterControl()
    {
        var mainPanel = new StackPanel { Margin = new Thickness(5) };

        // 加载所有需要的资源字典
        var resources = new ResourceDictionary();
        var resourcePaths = new[]
        {
            "/Tunnel-Next;component/Resources/ScriptsControls/SharedBrushes.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/LabelStyles.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/CheckBoxStyles.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/ComboBoxStyles.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/SliderStyles.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/TextBoxIdleStyles.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/TextBlockStyles.xaml"
        };

        foreach (var path in resourcePaths)
        {
            try
            {
                resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(path, UriKind.Relative) });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"资源加载失败: {path} - {ex.Message}");
            }
        }

        if (resources.Contains("Layer_2"))
        {
            mainPanel.Background = resources["Layer_2"] as Brush;
        }

        var viewModel = CreateViewModel() as ConvolutionViewModel;
        mainPanel.DataContext = viewModel;

        var titleLabel = new Label
        {
            Content = "卷积设置",
        };
        if (resources.Contains("TitleLabelStyle"))
        {
            titleLabel.Style = resources["TitleLabelStyle"] as Style;
        }
        mainPanel.Children.Add(titleLabel);

        // 内置核类型
        mainPanel.Children.Add(CreateLabel("内置核类型:", resources));
        var kernelTypeComboBox = new ComboBox
        {
            ItemsSource = new[] { "custom", "sharpen", "edge_detect", "edge_enhance", "emboss", "blur", "gaussian_blur" },
            Margin = new Thickness(0, 2, 0, 10)
        };
        if (resources.Contains("DefaultComboBoxStyle"))
        {
            kernelTypeComboBox.Style = resources["DefaultComboBoxStyle"] as Style;
        }
        kernelTypeComboBox.SetBinding(ComboBox.SelectedItemProperty, new Binding("KernelType") { Source = viewModel, Mode = BindingMode.TwoWay });
        mainPanel.Children.Add(kernelTypeComboBox);

        // 自定义核 (仅在KernelType为custom时显示)
        var customKernelPanel = new StackPanel();
        customKernelPanel.SetBinding(StackPanel.VisibilityProperty, new Binding("KernelType")
        {
            Source = viewModel,
            Converter = new StringToVisibilityConverter(),
            ConverterParameter = "custom"
        });

        customKernelPanel.Children.Add(CreateLabel("自定义卷积核 (格式: 1,0,-1;1,0,-1;1,0,-1):", resources));
        var customKernelTextBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 60,
            Margin = new Thickness(0, 2, 0, 10)
        };
        if (resources.Contains("DefaultTextBoxStyle"))
        {
            customKernelTextBox.Style = resources["DefaultTextBoxStyle"] as Style;
        }
        customKernelTextBox.SetBinding(TextBox.TextProperty, new Binding("CustomKernel") { Source = viewModel, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        customKernelPanel.Children.Add(customKernelTextBox);
        mainPanel.Children.Add(customKernelPanel);
        
        // 内置核强度
        mainPanel.Children.Add(CreateLabel("内置核强度:", resources));
        var intensitySlider = new Slider
        {
            Minimum = 0.1,
            Maximum = 5.0,
            SmallChange = 0.1,
            Margin = new Thickness(0, 2, 0, 10)
        };
        if (resources.Contains("DefaultSliderStyle"))
        {
            intensitySlider.Style = resources["DefaultSliderStyle"] as Style;
        }
        intensitySlider.SetBinding(Slider.ValueProperty, new Binding("Intensity") { Source = viewModel, Mode = BindingMode.TwoWay });
        mainPanel.Children.Add(intensitySlider);

        // 归一化
        var normalizeCheckBox = new CheckBox
        {
            Content = "归一化卷积核",
            Margin = new Thickness(0, 5, 0, 10)
        };
        if (resources.Contains("DefaultCheckBoxStyle"))
        {
            normalizeCheckBox.Style = resources["DefaultCheckBoxStyle"] as Style;
        }
        normalizeCheckBox.SetBinding(CheckBox.IsCheckedProperty, new Binding("Normalize") { Source = viewModel, Mode = BindingMode.TwoWay });
        mainPanel.Children.Add(normalizeCheckBox);

        // 偏移值
        mainPanel.Children.Add(CreateLabel("偏移值:", resources));
        var biasSlider = new Slider
        {
            Minimum = -1000,
            Maximum = 1000,
            SmallChange = 1,
            Margin = new Thickness(0, 2, 0, 10)
        };
        if (resources.Contains("DefaultSliderStyle"))
        {
            biasSlider.Style = resources["DefaultSliderStyle"] as Style;
        }
        biasSlider.SetBinding(Slider.ValueProperty, new Binding("Bias") { Source = viewModel, Mode = BindingMode.TwoWay });
        mainPanel.Children.Add(biasSlider);

        // 边界处理
        mainPanel.Children.Add(CreateLabel("边界处理:", resources));
        var borderTypeComboBox = new ComboBox
        {
            ItemsSource = new[] { "replicate", "reflect", "reflect_101", "wrap", "constant" },
            Margin = new Thickness(0, 2, 0, 10)
        };
        if (resources.Contains("DefaultComboBoxStyle"))
        {
            borderTypeComboBox.Style = resources["DefaultComboBoxStyle"] as Style;
        }
        borderTypeComboBox.SetBinding(ComboBox.SelectedItemProperty, new Binding("BorderType") { Source = viewModel, Mode = BindingMode.TwoWay });
        mainPanel.Children.Add(borderTypeComboBox);


        return mainPanel;
    }

    private Label CreateLabel(string content, ResourceDictionary resources)
    {
        var label = new Label { Content = content };
        if (resources.Contains("DefaultLabelStyle"))
        {
            label.Style = resources["DefaultLabelStyle"] as Style;
        }
        return label;
    }

    public override IScriptViewModel CreateViewModel()
    {
        return new ConvolutionViewModel(this);
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
            [nameof(CustomKernel)] = CustomKernel,
            [nameof(Intensity)] = Intensity,
            [nameof(Normalize)] = Normalize,
            [nameof(Bias)] = Bias,
            [nameof(BorderType)] = BorderType,
            ["NodeInstanceId"] = NodeInstanceId
        };
    }

    public override void DeserializeParameters(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(KernelType), out var kernelType))
            KernelType = kernelType?.ToString() ?? "custom";
        if (data.TryGetValue(nameof(CustomKernel), out var customKernel))
            CustomKernel = customKernel?.ToString() ?? "0,0,0;0,1,0;0,0,0";
        if (data.TryGetValue(nameof(Intensity), out var intensity))
            Intensity = Convert.ToDouble(intensity);
        if (data.TryGetValue(nameof(Normalize), out var normalize))
            Normalize = Convert.ToBoolean(normalize);
        if (data.TryGetValue(nameof(Bias), out var bias))
            Bias = Convert.ToDouble(bias);
        if (data.TryGetValue(nameof(BorderType), out var borderType))
            BorderType = borderType?.ToString() ?? "reflect_101";
        if (data.TryGetValue("NodeInstanceId", out var nodeId))
            NodeInstanceId = nodeId?.ToString() ?? string.Empty;
    }

    public void InitializeNodeInstance(string nodeId)
    {
        NodeInstanceId = nodeId;
    }
}

public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        return value?.ToString() == parameter?.ToString() ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class ConvolutionViewModel : ScriptViewModelBase
{
    private ConvolutionScript ConvolutionScript => (ConvolutionScript)Script;

    public string KernelType
    {
        get => ConvolutionScript.KernelType;
        set
        {
            if (ConvolutionScript.KernelType != value)
            {
                ConvolutionScript.KernelType = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(KernelType), value);
            }
        }
    }

    public string CustomKernel
    {
        get => ConvolutionScript.CustomKernel;
        set
        {
            if (ConvolutionScript.CustomKernel != value)
            {
                ConvolutionScript.CustomKernel = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(CustomKernel), value);
            }
        }
    }

    public double Intensity
    {
        get => ConvolutionScript.Intensity;
        set
        {
            if (Math.Abs(ConvolutionScript.Intensity - value) > 0.001)
            {
                ConvolutionScript.Intensity = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(Intensity), value);
            }
        }
    }

    public bool Normalize
    {
        get => ConvolutionScript.Normalize;
        set
        {
            if (ConvolutionScript.Normalize != value)
            {
                ConvolutionScript.Normalize = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(Normalize), value);
            }
        }
    }

    public double Bias
    {
        get => ConvolutionScript.Bias;
        set
        {
            if (Math.Abs(ConvolutionScript.Bias - value) > 0.001)
            {
                ConvolutionScript.Bias = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(Bias), value);
            }
        }
    }

    public string BorderType
    {
        get => ConvolutionScript.BorderType;
        set
        {
            if (ConvolutionScript.BorderType != value)
            {
                ConvolutionScript.BorderType = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(BorderType), value);
            }
        }
    }

    public ConvolutionViewModel(ConvolutionScript script) : base(script)
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
            [nameof(CustomKernel)] = CustomKernel,
            [nameof(Intensity)] = Intensity,
            [nameof(Normalize)] = Normalize,
            [nameof(Bias)] = Bias,
            [nameof(BorderType)] = BorderType
        };
    }

    public override async Task SetParameterDataAsync(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(KernelType), out var kernelType))
            KernelType = kernelType?.ToString() ?? "custom";
        if (data.TryGetValue(nameof(CustomKernel), out var customKernel))
            CustomKernel = customKernel?.ToString() ?? "0,0,0;0,1,0;0,0,0";
        if (data.TryGetValue(nameof(Intensity), out var intensity))
            Intensity = Convert.ToDouble(intensity);
        if (data.TryGetValue(nameof(Normalize), out var normalize))
            Normalize = Convert.ToBoolean(normalize);
        if (data.TryGetValue(nameof(Bias), out var bias))
            Bias = Convert.ToDouble(bias);
        if (data.TryGetValue(nameof(BorderType), out var borderType))
            BorderType = borderType?.ToString() ?? "reflect_101";
        await Task.CompletedTask;
    }

    public override async Task ResetToDefaultAsync()
    {
        KernelType = "sharpen";
        CustomKernel = "0,0,0;0,1,0;0,0,0";
        Intensity = 1.0;
        Normalize = false;
        Bias = 0;
        BorderType = "reflect_101";
        await Task.CompletedTask;
    }

    public override void Dispose()
    {
        base.Dispose();
    }
}
