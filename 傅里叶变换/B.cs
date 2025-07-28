using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Tunnel_Next.Services.Scripting;
using OpenCvSharp;
using Size = OpenCvSharp.Size;
using Rect = OpenCvSharp.Rect;

[TunnelExtensionScript(
    Name = "傅里叶变换A",
    Author = "BEITAware",
    Description = "傅里叶变换 - 生成幅度谱和相位谱，支持RGBA格式",
    Version = "1.0",
    Category = "频域处理",
    Color = "#3399FF"
)]
public class FourierTransformScript : TunnelExtensionScriptBase
{
    [ScriptParameter(DisplayName = "应用对数变换", Description = "对幅度谱应用对数变换以增强可视化效果", Order = 0)]
    public bool ApplyLogTransform { get; set; } = true;

    [ScriptParameter(DisplayName = "零频率居中", Description = "将零频率移动到频谱中心", Order = 1)]
    public bool CenterFFT { get; set; } = true;

    [ScriptParameter(DisplayName = "存储元数据", Description = "在幅度谱中存储原始图像信息", Order = 2)]
    public bool StoreMetadata { get; set; } = true;

    [ScriptParameter(DisplayName = "应用汉宁窗", Description = "应用汉宁窗函数减少频谱泄漏", Order = 3)]
    public bool ApplyWindow { get; set; } = false;

    [ScriptParameter(DisplayName = "增强预览效果", Description = "增强幅度谱的预览效果", Order = 4)]
    public bool EnhancePreview { get; set; } = true;

    [ScriptParameter(DisplayName = "变换Alpha通道", Description = "是否对Alpha通道也进行傅里叶变换", Order = 5)]
    public bool TransformAlpha { get; set; } = false;

    [ScriptParameter(DisplayName = "增强相位谱对比度", Description = "增强相位谱的对比度以改善可视化效果", Order = 6)]
    public bool EnhancePhaseContrast { get; set; } = true;

    public string NodeInstanceId { get; set; } = string.Empty;

    public override Dictionary<string, PortDefinition> GetInputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            ["f32bmp"] = new PortDefinition("f32bmp", false, "输入图像")
        };
    }

    public override Dictionary<string, PortDefinition> GetOutputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            ["f32bmp_magnitude"] = new PortDefinition("f32bmp", false, "幅度谱图像"),
            ["f32bmp_phase"] = new PortDefinition("f32bmp", false, "相位谱图像")
        };
    }

    public override Dictionary<string, object> Process(Dictionary<string, object> inputs, IScriptContext context)
    {
        // 检查输入是否有效
        if (!inputs.TryGetValue("f32bmp", out var inputObj) || inputObj == null)
        {
            return new Dictionary<string, object>
            {
                ["f32bmp_magnitude"] = null,
                ["f32bmp_phase"] = null
            };
        }

        if (!(inputObj is Mat inputMat) || inputMat.Empty())
        {
            return new Dictionary<string, object>
            {
                ["f32bmp_magnitude"] = null,
                ["f32bmp_phase"] = null
            };
        }

        try
        {
            // 确保输入是RGBA格式
            Mat workingMat = EnsureRGBAFormat(inputMat);

            // 分离RGBA通道
            Mat[] channels = Cv2.Split(workingMat);

            // 分别对RGB通道进行傅里叶变换
            Mat[] magnitudeChannels = new Mat[4];
            Mat[] phaseChannels = new Mat[4];

            // 处理RGB通道
            for (int i = 0; i < 3; i++)
            {
                var (magnitude, phase) = PerformFFT(channels[i]);
                magnitudeChannels[i] = magnitude;
                phaseChannels[i] = phase;
            }

            // 处理Alpha通道
            if (TransformAlpha)
            {
                var (magnitude, phase) = PerformFFT(channels[3]);
                magnitudeChannels[3] = magnitude;
                phaseChannels[3] = phase;
            }
            else
            {
                // Alpha通道设为1.0（不变换）
                // 对于幅度谱和相位谱的Alpha通道都设为1.0，确保图像可见
                magnitudeChannels[3] = Mat.Ones(magnitudeChannels[0].Size(), MatType.CV_32F);
                phaseChannels[3] = Mat.Ones(phaseChannels[0].Size(), MatType.CV_32F);
            }

            // 合并通道创建输出图像
            Mat magnitudeResult = new Mat();
            Mat phaseResult = new Mat();
            Cv2.Merge(magnitudeChannels, magnitudeResult);
            Cv2.Merge(phaseChannels, phaseResult);

            // 清理资源
            foreach (var ch in channels) ch.Dispose();
            foreach (var ch in magnitudeChannels) ch.Dispose();
            foreach (var ch in phaseChannels) ch.Dispose();
            workingMat.Dispose();

            return new Dictionary<string, object>
            {
                ["f32bmp_magnitude"] = magnitudeResult,
                ["f32bmp_phase"] = phaseResult
            };
        }
        catch (Exception ex)
        {
            throw new ApplicationException($"傅里叶变换节点处理失败: {ex.Message}", ex);
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
            // RGB -> RGBA
            Cv2.CvtColor(inputMat, rgbaMat, ColorConversionCodes.RGB2RGBA);
        }
        else if (inputMat.Channels() == 1)
        {
            // 灰度 -> RGB -> RGBA
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

        // 确保是32位浮点格式
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
    /// 执行傅里叶变换（单通道）
    /// </summary>
    private (Mat magnitude, Mat phase) PerformFFT(Mat channelMat)
    {
        // 确保图像尺寸适合FFT（加速计算）
        int rows = channelMat.Rows;
        int cols = channelMat.Cols;
        int optimalRows = Cv2.GetOptimalDFTSize(rows);
        int optimalCols = Cv2.GetOptimalDFTSize(cols);

        // 如果需要，填充图像到最佳尺寸
        Mat paddedMat = new Mat();
        Cv2.CopyMakeBorder(channelMat, paddedMat, 0, optimalRows - rows, 0, optimalCols - cols,
                          BorderTypes.Constant, Scalar.All(0));

        // 确保是32位浮点格式
        Mat floatMat = new Mat();
        if (paddedMat.Type() != MatType.CV_32F)
        {
            paddedMat.ConvertTo(floatMat, MatType.CV_32F);
        }
        else
        {
            floatMat = paddedMat.Clone();
        }

        // 应用窗函数减少频谱泄漏（如果启用）
        if (ApplyWindow)
        {
            Mat window = new Mat();
            Cv2.CreateHanningWindow(window, new Size(floatMat.Cols, floatMat.Rows), MatType.CV_32F);
            Cv2.Multiply(floatMat, window, floatMat);
            window.Dispose();
        }

        // 执行傅里叶变换
        Mat dft = new Mat();
        Cv2.Dft(floatMat, dft, DftFlags.ComplexOutput);

        // 移动零频率到中心（如果需要）
        if (CenterFFT)
        {
            ShiftDFT(dft);
        }

        // 分离实部和虚部
        Mat[] dftChannels = Cv2.Split(dft);
        Mat realPart = dftChannels[0];
        Mat imagPart = dftChannels[1];

        // 计算幅度谱和相位谱
        Mat magnitudeSpectrum = new Mat();
        Mat phaseSpectrum = new Mat();

        Cv2.Magnitude(realPart, imagPart, magnitudeSpectrum);
        Cv2.Phase(realPart, imagPart, phaseSpectrum);

        // 保存原始幅度谱的最大值用于重建
        double minVal, maxVal;
        Cv2.MinMaxLoc(magnitudeSpectrum, out minVal, out maxVal);

        // 创建用于显示的幅度谱副本
        Mat displayMagnitude = magnitudeSpectrum.Clone();

        // 保存对数变换后的最大值（用于重建）
        double logMaxVal = maxVal; // 默认使用原始最大值

        // 对显示用的幅度谱应用对数变换以增强可视化效果（如果需要）
        if (ApplyLogTransform)
        {
            Cv2.Add(displayMagnitude, Scalar.All(1), displayMagnitude);
            Cv2.Log(displayMagnitude, displayMagnitude);

            // 记录对数变换后的实际最大值
            double logMinVal;
            Cv2.MinMaxLoc(displayMagnitude, out logMinVal, out logMaxVal);
            System.Diagnostics.Debug.WriteLine($"[傅里叶变换] 对数变换后的最大值: {logMaxVal}");
        }
        else
        {
            // 如果没有对数变换，logMaxVal就是原始最大值
            System.Diagnostics.Debug.WriteLine($"[傅里叶变换] 未应用对数变换，使用原始最大值: {logMaxVal}");
        }

        // 归一化显示用的幅度谱到0-1范围
        Cv2.Normalize(displayMagnitude, displayMagnitude, 0, 1, NormTypes.MinMax);

        // 相位谱从-π到π映射到0-1范围
        Mat normalizedPhase = new Mat();
        Cv2.Add(phaseSpectrum, Scalar.All(Math.PI), normalizedPhase);
        Cv2.Multiply(normalizedPhase, Scalar.All(1.0 / (2 * Math.PI)), normalizedPhase);

        // 增强相位谱对比度（如果启用）
        if (EnhancePhaseContrast)
        {
            // 使用直方图均衡化增强对比度
            Mat enhancedPhase = new Mat();
            normalizedPhase.ConvertTo(enhancedPhase, MatType.CV_8U, 255.0);
            Cv2.EqualizeHist(enhancedPhase, enhancedPhase);
            enhancedPhase.ConvertTo(normalizedPhase, MatType.CV_32F, 1.0 / 255.0);
            enhancedPhase.Dispose();
        }

        // 确保相位谱的值在有效范围内
        Cv2.Threshold(normalizedPhase, normalizedPhase, 1.0, 1.0, ThresholdTypes.Trunc);
        Cv2.Threshold(normalizedPhase, normalizedPhase, 0.0, 0.0, ThresholdTypes.Tozero);

        // 裁剪回原始尺寸（如果需要）
        if (rows != displayMagnitude.Rows || cols != displayMagnitude.Cols)
        {
            Mat croppedMagnitude = new Mat(displayMagnitude, new Rect(0, 0, cols, rows));
            Mat croppedPhase = new Mat(normalizedPhase, new Rect(0, 0, cols, rows));

            Mat finalMagnitude = croppedMagnitude.Clone();
            Mat finalPhase = croppedPhase.Clone();

            displayMagnitude.Dispose();
            normalizedPhase.Dispose();

            displayMagnitude = finalMagnitude;
            normalizedPhase = finalPhase;
        }

        // 元数据通过InjectMetadata系统传递，不需要在图像中存储

        // 清理资源
        paddedMat.Dispose();
        floatMat.Dispose();
        dft.Dispose();
        realPart.Dispose();
        imagPart.Dispose();
        phaseSpectrum.Dispose();
        magnitudeSpectrum.Dispose();  // 清理原始幅度谱

        return (displayMagnitude, normalizedPhase);
    }

    /// <summary>
    /// 注入元数据到下游
    /// </summary>
    public override Dictionary<string, object> InjectMetadata(Dictionary<string, object> currentMetadata)
    {
        // 调用基类方法
        var metadata = base.InjectMetadata(currentMetadata);

        // 注入傅里叶变换相关的元数据
        if (!metadata.ContainsKey("傅里叶变换参数"))
        {
            metadata["傅里叶变换参数"] = new Dictionary<string, object>
            {
                ["ApplyLogTransform"] = ApplyLogTransform,
                ["CenterFFT"] = CenterFFT,
                ["StoreMetadata"] = StoreMetadata,
                ["ApplyWindow"] = ApplyWindow,
                ["EnhancePreview"] = EnhancePreview,
                ["TransformAlpha"] = TransformAlpha,
                ["EnhancePhaseContrast"] = EnhancePhaseContrast
            };
        }

        // 重建信息将在处理时动态更新到元数据中

        if (!metadata.ContainsKey("处理历史"))
        {
            metadata["处理历史"] = "傅里叶变换";
        }
        else
        {
            metadata["处理历史"] = metadata["处理历史"] + " → 傅里叶变换";
        }

        return metadata;
    }

    /// <summary>
    /// 移动DFT结果使零频率居中
    /// </summary>
    private void ShiftDFT(Mat dft)
    {
        int cx = dft.Cols / 2;
        int cy = dft.Rows / 2;

        Mat q0 = new Mat(dft, new Rect(0, 0, cx, cy));
        Mat q1 = new Mat(dft, new Rect(cx, 0, cx, cy));
        Mat q2 = new Mat(dft, new Rect(0, cy, cx, cy));
        Mat q3 = new Mat(dft, new Rect(cx, cy, cx, cy));

        Mat tmp = new Mat();

        q0.CopyTo(tmp);
        q3.CopyTo(q0);
        tmp.CopyTo(q3);

        q1.CopyTo(tmp);
        q2.CopyTo(q1);
        tmp.CopyTo(q2);

        tmp.Dispose();
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
            "/Tunnel-Next;component/Resources/ScriptsControls/CheckBoxStyles.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/TextBlockStyles.xaml"
        };
        foreach (var path in resourcePaths)
        {
            try { resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(path, UriKind.Relative) }); }
            catch { /* 静默处理 */ }
        }

        if (resources.Contains("MainPanelStyle")) mainPanel.Style = resources["MainPanelStyle"] as Style;

        var viewModel = CreateViewModel() as FourierTransformViewModel;
        mainPanel.DataContext = viewModel;

        var titleLabel = new Label { Content = "傅里叶变换设置" };
        if (resources.Contains("TitleLabelStyle")) titleLabel.Style = resources["TitleLabelStyle"] as Style;
        mainPanel.Children.Add(titleLabel);

        mainPanel.Children.Add(CreateCheckBoxControl("应用对数变换", nameof(viewModel.ApplyLogTransform), viewModel, resources));
        mainPanel.Children.Add(CreateCheckBoxControl("零频率居中", nameof(viewModel.CenterFFT), viewModel, resources));
        mainPanel.Children.Add(CreateCheckBoxControl("存储元数据", nameof(viewModel.StoreMetadata), viewModel, resources));
        mainPanel.Children.Add(CreateCheckBoxControl("应用汉宁窗", nameof(viewModel.ApplyWindow), viewModel, resources));
        mainPanel.Children.Add(CreateCheckBoxControl("增强预览效果", nameof(viewModel.EnhancePreview), viewModel, resources));
        mainPanel.Children.Add(CreateCheckBoxControl("变换Alpha通道", nameof(viewModel.TransformAlpha), viewModel, resources));
        mainPanel.Children.Add(CreateCheckBoxControl("增强相位谱对比度", nameof(viewModel.EnhancePhaseContrast), viewModel, resources));

        return mainPanel;
    }

    private CheckBox CreateCheckBoxControl(string content, string propertyName, FourierTransformViewModel viewModel, ResourceDictionary resources)
    {
        var checkBox = new CheckBox
        {
            Content = content,
            Margin = new Thickness(0, 5, 0, 5)
        };
        if (resources.Contains("DefaultCheckBoxStyle")) checkBox.Style = resources["DefaultCheckBoxStyle"] as Style;

        var binding = new Binding(propertyName)
        {
            Source = viewModel,
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        };
        checkBox.SetBinding(CheckBox.IsCheckedProperty, binding);

        return checkBox;
    }

    public override IScriptViewModel CreateViewModel()
    {
        return new FourierTransformViewModel(this);
    }

    public override async Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
    {
        // 参数变化处理
        await Task.CompletedTask;
    }

    public override Dictionary<string, object> SerializeParameters()
    {
        return new Dictionary<string, object>
        {
            [nameof(ApplyLogTransform)] = ApplyLogTransform,
            [nameof(CenterFFT)] = CenterFFT,
            [nameof(StoreMetadata)] = StoreMetadata,
            [nameof(ApplyWindow)] = ApplyWindow,
            [nameof(EnhancePreview)] = EnhancePreview,
            [nameof(TransformAlpha)] = TransformAlpha,
            [nameof(EnhancePhaseContrast)] = EnhancePhaseContrast,
            ["NodeInstanceId"] = NodeInstanceId
        };
    }

    public override void DeserializeParameters(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(ApplyLogTransform), out var applyLog))
            ApplyLogTransform = Convert.ToBoolean(applyLog);
        if (data.TryGetValue(nameof(CenterFFT), out var centerFFT))
            CenterFFT = Convert.ToBoolean(centerFFT);
        if (data.TryGetValue(nameof(StoreMetadata), out var storeMeta))
            StoreMetadata = Convert.ToBoolean(storeMeta);
        if (data.TryGetValue(nameof(ApplyWindow), out var applyWindow))
            ApplyWindow = Convert.ToBoolean(applyWindow);
        if (data.TryGetValue(nameof(EnhancePreview), out var enhancePreview))
            EnhancePreview = Convert.ToBoolean(enhancePreview);
        if (data.TryGetValue(nameof(TransformAlpha), out var transformAlpha))
            TransformAlpha = Convert.ToBoolean(transformAlpha);
        if (data.TryGetValue(nameof(EnhancePhaseContrast), out var enhancePhase))
            EnhancePhaseContrast = Convert.ToBoolean(enhancePhase);
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

public class FourierTransformViewModel : ScriptViewModelBase
{
    private FourierTransformScript FourierTransformScript => (FourierTransformScript)Script;

    public bool ApplyLogTransform
    {
        get => FourierTransformScript.ApplyLogTransform;
        set
        {
            if (FourierTransformScript.ApplyLogTransform != value)
            {
                FourierTransformScript.ApplyLogTransform = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(ApplyLogTransform), value);
            }
        }
    }

    public bool CenterFFT
    {
        get => FourierTransformScript.CenterFFT;
        set
        {
            if (FourierTransformScript.CenterFFT != value)
            {
                FourierTransformScript.CenterFFT = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(CenterFFT), value);
            }
        }
    }

    public bool StoreMetadata
    {
        get => FourierTransformScript.StoreMetadata;
        set
        {
            if (FourierTransformScript.StoreMetadata != value)
            {
                FourierTransformScript.StoreMetadata = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(StoreMetadata), value);
            }
        }
    }

    public bool ApplyWindow
    {
        get => FourierTransformScript.ApplyWindow;
        set
        {
            if (FourierTransformScript.ApplyWindow != value)
            {
                FourierTransformScript.ApplyWindow = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(ApplyWindow), value);
            }
        }
    }

    public bool EnhancePreview
    {
        get => FourierTransformScript.EnhancePreview;
        set
        {
            if (FourierTransformScript.EnhancePreview != value)
            {
                FourierTransformScript.EnhancePreview = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(EnhancePreview), value);
            }
        }
    }

    public bool TransformAlpha
    {
        get => FourierTransformScript.TransformAlpha;
        set
        {
            if (FourierTransformScript.TransformAlpha != value)
            {
                FourierTransformScript.TransformAlpha = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(TransformAlpha), value);
            }
        }
    }

    public bool EnhancePhaseContrast
    {
        get => FourierTransformScript.EnhancePhaseContrast;
        set
        {
            if (FourierTransformScript.EnhancePhaseContrast != value)
            {
                FourierTransformScript.EnhancePhaseContrast = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(EnhancePhaseContrast), value);
            }
        }
    }

    public FourierTransformViewModel(FourierTransformScript script) : base(script)
    {
    }

    private void NotifyParameterChanged(string parameterName, object value)
    {
        if (Script is TunnelExtensionScriptBase rsb)
        {
            rsb.OnParameterChanged(parameterName, value);
        }
    }

    public async Task ResetToDefault()
    {
        ApplyLogTransform = true;
        CenterFFT = true;
        StoreMetadata = true;
        ApplyWindow = false;
        EnhancePreview = true;
        TransformAlpha = false;
        EnhancePhaseContrast = true;
        await Task.CompletedTask;
    }

    public void EnhanceMagnitudePreview()
    {
        ApplyLogTransform = true;
        CenterFFT = true;
        EnhancePreview = true;
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
            [nameof(ApplyLogTransform)] = ApplyLogTransform,
            [nameof(CenterFFT)] = CenterFFT,
            [nameof(StoreMetadata)] = StoreMetadata,
            [nameof(ApplyWindow)] = ApplyWindow,
            [nameof(EnhancePreview)] = EnhancePreview,
            [nameof(TransformAlpha)] = TransformAlpha,
            [nameof(EnhancePhaseContrast)] = EnhancePhaseContrast
        };
    }

    public override async Task SetParameterDataAsync(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(ApplyLogTransform), out var applyLog))
            ApplyLogTransform = Convert.ToBoolean(applyLog);
        if (data.TryGetValue(nameof(CenterFFT), out var centerFFT))
            CenterFFT = Convert.ToBoolean(centerFFT);
        if (data.TryGetValue(nameof(StoreMetadata), out var storeMeta))
            StoreMetadata = Convert.ToBoolean(storeMeta);
        if (data.TryGetValue(nameof(ApplyWindow), out var applyWindow))
            ApplyWindow = Convert.ToBoolean(applyWindow);
        if (data.TryGetValue(nameof(EnhancePreview), out var enhancePreview))
            EnhancePreview = Convert.ToBoolean(enhancePreview);
        if (data.TryGetValue(nameof(TransformAlpha), out var transformAlpha))
            TransformAlpha = Convert.ToBoolean(transformAlpha);
        if (data.TryGetValue(nameof(EnhancePhaseContrast), out var enhancePhase))
            EnhancePhaseContrast = Convert.ToBoolean(enhancePhase);
        await Task.CompletedTask;
    }

    public override async Task ResetToDefaultAsync()
    {
        await ResetToDefault();
    }

    public override void Dispose()
    {
        base.Dispose();
    }
}
