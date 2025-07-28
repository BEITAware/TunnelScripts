using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Tunnel_Next.Services.Scripting;
using OpenCvSharp;
using Rect = OpenCvSharp.Rect;

[TunnelExtensionScript(
    Name = "傅里叶逆变换A",
    Author = "BEITAware",
    Description = "傅里叶逆变换 - 从幅度谱和相位谱重建图像，支持RGBA格式",
    Version = "1.0",
    Category = "频域处理",
    Color = "#FF6600"
)]
public class InverseFourierTransformScript : TunnelExtensionScriptBase
{
    [ScriptParameter(DisplayName = "逆对数变换", Description = "对幅度谱应用逆对数变换", Order = 0)]
    public bool InverseLogTransform { get; set; } = true;

    [ScriptParameter(DisplayName = "零频率居中", Description = "零频率位于频谱中心", Order = 1)]
    public bool CenterFFT { get; set; } = true;

    [ScriptParameter(DisplayName = "保持原始尺寸", Description = "裁剪结果图像到原始尺寸", Order = 2)]
    public bool PreserveOriginalSize { get; set; } = true;

    [ScriptParameter(DisplayName = "归一化输出", Description = "归一化输出图像到0-1范围", Order = 3)]
    public bool NormalizeOutput { get; set; } = true;

    public string NodeInstanceId { get; set; } = string.Empty;

    public override Dictionary<string, PortDefinition> GetInputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            ["f32bmp_magnitude"] = new PortDefinition("f32bmp", false, "幅度谱图像"),
            ["f32bmp_phase"] = new PortDefinition("f32bmp", false, "相位谱图像")
        };
    }

    public override Dictionary<string, PortDefinition> GetOutputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            ["f32bmp"] = new PortDefinition("f32bmp", false, "重建图像")
        };
    }

    public override Dictionary<string, object> Process(Dictionary<string, object> inputs, IScriptContext context)
    {
        // 检查输入是否有效
        if (!inputs.TryGetValue("f32bmp_magnitude", out var magnitudeObj) || magnitudeObj == null ||
            !inputs.TryGetValue("f32bmp_phase", out var phaseObj) || phaseObj == null)
        {
            return new Dictionary<string, object> { ["f32bmp"] = null };
        }

        if (!(magnitudeObj is Mat magnitudeMat) || magnitudeMat.Empty() ||
            !(phaseObj is Mat phaseMat) || phaseMat.Empty())
        {
            return new Dictionary<string, object> { ["f32bmp"] = null };
        }

        try
        {
            // 确保输入是RGBA格式
            Mat magnitudeRGBA = EnsureRGBAFormat(magnitudeMat);
            Mat phaseRGBA = EnsureRGBAFormat(phaseMat);

            // 分离RGBA通道
            Mat[] magnitudeChannels = Cv2.Split(magnitudeRGBA);
            Mat[] phaseChannels = Cv2.Split(phaseRGBA);

            // 分别对每个通道进行逆傅里叶变换
            Mat[] reconstructedChannels = new Mat[4];

            // 处理RGB通道（0, 1, 2）
            for (int i = 0; i < 3; i++)
            {
                reconstructedChannels[i] = PerformIFFT(magnitudeChannels[i], phaseChannels[i]);
            }

            // 特殊处理Alpha通道
            // 检查Alpha通道是否是常数（全1）
            double minVal, maxVal;
            Cv2.MinMaxLoc(magnitudeChannels[3], out minVal, out maxVal);

            if (Math.Abs(maxVal - minVal) < 0.01) // Alpha通道是常数
            {
                // 如果Alpha通道是常数，直接设为1.0（不透明）
                reconstructedChannels[3] = Mat.Ones(reconstructedChannels[0].Size(), reconstructedChannels[0].Type());
                System.Diagnostics.Debug.WriteLine("[傅里叶逆变换] Alpha通道检测为常数，设为1.0");
            }
            else
            {
                // 如果Alpha通道不是常数，进行正常的逆FFT
                reconstructedChannels[3] = PerformIFFT(magnitudeChannels[3], phaseChannels[3]);
                System.Diagnostics.Debug.WriteLine("[傅里叶逆变换] Alpha通道进行逆FFT处理");
            }

            // 确保所有通道具有相同的尺寸
            var targetSize = reconstructedChannels[0].Size();
            var targetType = reconstructedChannels[0].Type();

            for (int i = 1; i < 4; i++)
            {
                if (reconstructedChannels[i].Size() != targetSize || reconstructedChannels[i].Type() != targetType)
                {
                    Mat resizedChannel = new Mat();
                    Cv2.Resize(reconstructedChannels[i], resizedChannel, targetSize);

                    // 确保类型一致
                    if (resizedChannel.Type() != targetType)
                    {
                        Mat convertedChannel = new Mat();
                        resizedChannel.ConvertTo(convertedChannel, targetType);
                        resizedChannel.Dispose();
                        resizedChannel = convertedChannel;
                    }

                    reconstructedChannels[i].Dispose();
                    reconstructedChannels[i] = resizedChannel;
                }
            }

            // 合并通道创建最终结果
            Mat resultMat = new Mat();
            Cv2.Merge(reconstructedChannels, resultMat);

            // 清理资源
            magnitudeRGBA.Dispose();
            phaseRGBA.Dispose();
            foreach (var ch in magnitudeChannels) ch.Dispose();
            foreach (var ch in phaseChannels) ch.Dispose();
            foreach (var ch in reconstructedChannels) ch.Dispose();

            return new Dictionary<string, object> { ["f32bmp"] = resultMat };
        }
        catch (Exception ex)
        {
            throw new ApplicationException($"傅里叶逆变换节点处理失败: {ex.Message}", ex);
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
    /// 执行傅里叶逆变换（单通道）
    /// </summary>
    private Mat PerformIFFT(Mat magnitudeChannel, Mat phaseChannel)
    {
        // 复制输入数据
        Mat magnitude = magnitudeChannel.Clone();
        Mat phase = phaseChannel.Clone();

        // 确保是32位浮点格式
        if (magnitude.Type() != MatType.CV_32F)
        {
            Mat tempMag = new Mat();
            magnitude.ConvertTo(tempMag, MatType.CV_32F);
            magnitude.Dispose();
            magnitude = tempMag;
        }

        if (phase.Type() != MatType.CV_32F)
        {
            Mat tempPhase = new Mat();
            phase.ConvertTo(tempPhase, MatType.CV_32F);
            phase.Dispose();
            phase = tempPhase;
        }

        // 使用参数控制重建过程，不再从图像中提取元数据
        bool logTransformApplied = InverseLogTransform;
        bool centerFFTApplied = CenterFFT;
        int originalRows = magnitude.Rows;
        int originalCols = magnitude.Cols;

        System.Diagnostics.Debug.WriteLine($"[傅里叶逆变换] 使用参数: logTransform={logTransformApplied}, centerFFT={centerFFTApplied}, rows={originalRows}, cols={originalCols}");

        // 如果图像被填充过，需要先填充到相同尺寸
        Mat workingMagnitude = magnitude.Clone();
        Mat workingPhase = phase.Clone();

        // 计算最佳FFT尺寸（与正向变换保持一致）
        int optimalRows = Cv2.GetOptimalDFTSize(originalRows);
        int optimalCols = Cv2.GetOptimalDFTSize(originalCols);

        // 如果当前尺寸小于最佳尺寸，需要填充
        if (workingMagnitude.Rows < optimalRows || workingMagnitude.Cols < optimalCols)
        {
            Mat paddedMagnitude = new Mat();
            Mat paddedPhase = new Mat();

            Cv2.CopyMakeBorder(workingMagnitude, paddedMagnitude,
                              0, optimalRows - workingMagnitude.Rows,
                              0, optimalCols - workingMagnitude.Cols,
                              BorderTypes.Constant, Scalar.All(0));

            Cv2.CopyMakeBorder(workingPhase, paddedPhase,
                              0, optimalRows - workingPhase.Rows,
                              0, optimalCols - workingPhase.Cols,
                              BorderTypes.Constant, Scalar.All(0));

            workingMagnitude.Dispose();
            workingPhase.Dispose();
            workingMagnitude = paddedMagnitude;
            workingPhase = paddedPhase;
        }

        // 重建原始幅度谱
        Mat reconstructedMagnitude = workingMagnitude.Clone();

        // 自适应的重建逻辑
        // 检查输入数据的特征来决定如何处理
        double minVal, maxVal;
        Cv2.MinMaxLoc(reconstructedMagnitude, out minVal, out maxVal);
        System.Diagnostics.Debug.WriteLine($"[傅里叶逆变换] 输入幅度谱范围: [{minVal}, {maxVal}]");

        // 如果启用逆对数变换
        if (InverseLogTransform)
        {
            // 检查数据是否看起来像对数变换后的数据
            if (maxVal <= 1.1 && minVal >= -0.1) // 看起来像归一化的数据
            {
                // 假设这是归一化的对数变换数据，需要先反归一化再逆对数变换
                // 大幅提高系数以解决图像过暗问题
                double estimatedLogMax = 25.0; // 原来是10.0，现在提高到25.0
                Cv2.Multiply(reconstructedMagnitude, Scalar.All(estimatedLogMax), reconstructedMagnitude);

                // 应用逆对数变换
                Cv2.Exp(reconstructedMagnitude, reconstructedMagnitude);
                Cv2.Subtract(reconstructedMagnitude, Scalar.All(1), reconstructedMagnitude);

                System.Diagnostics.Debug.WriteLine($"[傅里叶逆变换] 应用逆对数变换，估算logMax={estimatedLogMax}");
            }
            else
            {
                // 数据范围不像归一化数据，直接应用逆对数变换，但也提高系数
                // 先提高幅度值
                Cv2.Multiply(reconstructedMagnitude, Scalar.All(2.5), reconstructedMagnitude);
                // 然后执行逆对数变换
                Cv2.Exp(reconstructedMagnitude, reconstructedMagnitude);
                Cv2.Subtract(reconstructedMagnitude, Scalar.All(1), reconstructedMagnitude);

                System.Diagnostics.Debug.WriteLine("[傅里叶逆变换] 直接应用逆对数变换，并提高幅度系数");
            }
        }
        else
        {
            // 不应用逆对数变换，但增强缩放系数
            if (maxVal <= 1.1 && minVal >= -0.1) // 看起来像归一化的数据
            {
                // 假设原始幅度谱的合理范围，进行强化缩放
                double estimatedMax = 2500.0; // 原来是1000.0，提高到2500.0
                Cv2.Multiply(reconstructedMagnitude, Scalar.All(estimatedMax), reconstructedMagnitude);

                System.Diagnostics.Debug.WriteLine($"[傅里叶逆变换] 强化缩放归一化数据，估算max={estimatedMax}");
            }
            else
            {
                // 即使是非归一化数据，也进行适度提升
                Cv2.Multiply(reconstructedMagnitude, Scalar.All(2.5), reconstructedMagnitude);
                System.Diagnostics.Debug.WriteLine("[傅里叶逆变换] 直接增强输入数据亮度");
            }
        }

        // 相位谱从0-1范围映射回-π到π
        Mat denormalizedPhase = new Mat();
        Cv2.Multiply(workingPhase, Scalar.All(2 * Math.PI), denormalizedPhase);
        Cv2.Subtract(denormalizedPhase, Scalar.All(Math.PI), denormalizedPhase);

        // 处理相位谱增强对比度后的情况
        // 如果相位谱看起来经过了直方图均衡化等增强，尝试逆转这种增强
        // 这里添加简单检测并修正相位谱数据，避免过度处理
        double phaseMin, phaseMax;
        Cv2.MinMaxLoc(denormalizedPhase, out phaseMin, out phaseMax);
        if (Math.Abs(phaseMax - phaseMin) < 6.0) // 相位范围不足，可能需要修正
        {
            System.Diagnostics.Debug.WriteLine("[傅里叶逆变换] 检测到相位谱范围异常，尝试修正");
            // 尝试恢复相位谱的正常范围
            Cv2.Normalize(denormalizedPhase, denormalizedPhase, -Math.PI, Math.PI, NormTypes.MinMax);
        }

        // 根据幅度谱和相位谱创建复数数组
        Mat real = new Mat();
        Mat imaginary = new Mat();

        Cv2.PolarToCart(reconstructedMagnitude, denormalizedPhase, real, imaginary);

        // 合并实部和虚部
        Mat complexArray = new Mat();
        Cv2.Merge(new Mat[] { real, imaginary }, complexArray);

        // 执行逆傅里叶变换
        // 首先进行逆移动使零频率回到原位置（如果在FFT中进行了中心化）
        if (centerFFTApplied && CenterFFT)
        {
            ShiftDFT(complexArray);
        }

        // 逆傅里叶变换
        Mat idft = new Mat();
        Cv2.Dft(complexArray, idft, DftFlags.Inverse | DftFlags.Scale);

        // 提取实部作为结果图像
        Mat[] idftChannels = Cv2.Split(idft);
        Mat reconstructed = idftChannels[0].Clone();

        // 裁剪回原始尺寸（如果需要）
        if (PreserveOriginalSize && originalRows > 0 && originalCols > 0 &&
            originalRows <= reconstructed.Rows && originalCols <= reconstructed.Cols)
        {
            try
            {
                Mat croppedMat = new Mat(reconstructed, new Rect(0, 0, originalCols, originalRows));
                Mat result = croppedMat.Clone();
                croppedMat.Dispose();
                reconstructed.Dispose();
                reconstructed = result;
            }
            catch
            {
                // 如果裁剪失败，使用整个图像
            }
        }

        // 检查重建后的图像亮度
        double reMinVal, reMaxVal;
        Cv2.MinMaxLoc(reconstructed, out reMinVal, out reMaxVal);
        System.Diagnostics.Debug.WriteLine($"[傅里叶逆变换] 重建前图像范围: [{reMinVal}, {reMaxVal}]");
        
        // 如果图像亮度过低，进行增强
        if (reMaxVal < 0.5)
        {
            double brightnessFactor = 1.0 / reMaxVal * 0.8; // 将最大值提升到0.8左右
            if (brightnessFactor > 10.0) brightnessFactor = 10.0; // 限制增强幅度
            
            System.Diagnostics.Debug.WriteLine($"[傅里叶逆变换] 图像亮度过低，进行增强，系数={brightnessFactor}");
            Cv2.Multiply(reconstructed, Scalar.All(brightnessFactor), reconstructed);
        }

        // 归一化输出
        if (NormalizeOutput)
        {
            Cv2.Normalize(reconstructed, reconstructed, 0, 1, NormTypes.MinMax);
        }
        else
        {
            // 即使不启用归一化，也要确保值在合理范围内
            // 检查数值范围
            double finalMinVal, finalMaxVal;
            Cv2.MinMaxLoc(reconstructed, out finalMinVal, out finalMaxVal);
            System.Diagnostics.Debug.WriteLine($"[傅里叶逆变换] 重建图像范围: [{finalMinVal}, {finalMaxVal}]");

            // 如果数值范围异常，进行修正
            if (finalMaxVal > 10.0 || finalMinVal < -1.0)
            {
                System.Diagnostics.Debug.WriteLine("[傅里叶逆变换] 检测到异常数值范围，进行归一化修正");
                Cv2.Normalize(reconstructed, reconstructed, 0, 1, NormTypes.MinMax);
            }
            // 如果图像最大值很小，可能导致图像过暗，进行适度提升
            else if (finalMaxVal < 0.3)
            {
                System.Diagnostics.Debug.WriteLine("[傅里叶逆变换] 检测到图像可能过暗，进行亮度提升");
                Cv2.Multiply(reconstructed, Scalar.All(3.0), reconstructed);
            }
        }

        // 清理资源
        magnitude.Dispose();
        phase.Dispose();
        workingMagnitude.Dispose();
        workingPhase.Dispose();
        reconstructedMagnitude.Dispose();
        denormalizedPhase.Dispose();
        real.Dispose();
        imaginary.Dispose();
        complexArray.Dispose();
        idft.Dispose();
        foreach (var ch in idftChannels) ch.Dispose();

        return reconstructed;
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

    /// <summary>
    /// 注入元数据到下游
    /// </summary>
    public override Dictionary<string, object> InjectMetadata(Dictionary<string, object> currentMetadata)
    {
        // 调用基类方法
        var metadata = base.InjectMetadata(currentMetadata);

        // 注入傅里叶逆变换相关的元数据
        if (!metadata.ContainsKey("傅里叶逆变换参数"))
        {
            metadata["傅里叶逆变换参数"] = new Dictionary<string, object>
            {
                ["InverseLogTransform"] = InverseLogTransform,
                ["CenterFFT"] = CenterFFT,
                ["PreserveOriginalSize"] = PreserveOriginalSize,
                ["NormalizeOutput"] = NormalizeOutput
            };
        }

        if (!metadata.ContainsKey("处理历史"))
        {
            metadata["处理历史"] = "傅里叶逆变换";
        }
        else
        {
            metadata["处理历史"] = metadata["处理历史"] + " → 傅里叶逆变换";
        }

        return metadata;
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
                // 可以记录资源加载失败的日志
                System.Diagnostics.Debug.WriteLine($"资源加载失败: {path} - {ex.Message}");
            }
        }

        if (resources.Contains("Layer_2"))
        {
            mainPanel.Background = resources["Layer_2"] as Brush;
        }

        var viewModel = CreateViewModel() as InverseFourierTransformViewModel;
        mainPanel.DataContext = viewModel;

        var titleLabel = new Label
        {
            Content = "傅里叶逆变换设置",
        };
        if (resources.Contains("TitleLabelStyle"))
        {
            titleLabel.Style = resources["TitleLabelStyle"] as Style;
        }
        mainPanel.Children.Add(titleLabel);

        // 创建复选框
        mainPanel.Children.Add(CreateBindingCheckBox("逆对数变换", "InverseLogTransform", viewModel, resources));
        mainPanel.Children.Add(CreateBindingCheckBox("零频率居中", "CenterFFT", viewModel, resources));
        mainPanel.Children.Add(CreateBindingCheckBox("保持原始尺寸", "PreserveOriginalSize", viewModel, resources));
        mainPanel.Children.Add(CreateBindingCheckBox("归一化输出", "NormalizeOutput", viewModel, resources));

        return mainPanel;
    }

    private CheckBox CreateBindingCheckBox(string content, string propertyName, object viewModel, ResourceDictionary resources)
    {
        var checkBox = new CheckBox
        {
            Content = content,
            Margin = new Thickness(0, 5, 0, 5),
        };

        if (resources.Contains("DefaultCheckBoxStyle"))
        {
            checkBox.Style = resources["DefaultCheckBoxStyle"] as Style;
        }

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
        return new InverseFourierTransformViewModel(this);
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
            [nameof(InverseLogTransform)] = InverseLogTransform,
            [nameof(CenterFFT)] = CenterFFT,
            [nameof(PreserveOriginalSize)] = PreserveOriginalSize,
            [nameof(NormalizeOutput)] = NormalizeOutput,
            ["NodeInstanceId"] = NodeInstanceId
        };
    }

    public override void DeserializeParameters(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(InverseLogTransform), out var inverseLog))
            InverseLogTransform = Convert.ToBoolean(inverseLog);
        if (data.TryGetValue(nameof(CenterFFT), out var centerFFT))
            CenterFFT = Convert.ToBoolean(centerFFT);
        if (data.TryGetValue(nameof(PreserveOriginalSize), out var preserveSize))
            PreserveOriginalSize = Convert.ToBoolean(preserveSize);
        if (data.TryGetValue(nameof(NormalizeOutput), out var normalizeOutput))
            NormalizeOutput = Convert.ToBoolean(normalizeOutput);
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

public class InverseFourierTransformViewModel : ScriptViewModelBase
{
    private InverseFourierTransformScript InverseFourierTransformScript => (InverseFourierTransformScript)Script;

    public bool InverseLogTransform
    {
        get => InverseFourierTransformScript.InverseLogTransform;
        set
        {
            if (InverseFourierTransformScript.InverseLogTransform != value)
            {
                InverseFourierTransformScript.InverseLogTransform = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(InverseLogTransform), value);
            }
        }
    }

    public bool CenterFFT
    {
        get => InverseFourierTransformScript.CenterFFT;
        set
        {
            if (InverseFourierTransformScript.CenterFFT != value)
            {
                InverseFourierTransformScript.CenterFFT = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(CenterFFT), value);
            }
        }
    }

    public bool PreserveOriginalSize
    {
        get => InverseFourierTransformScript.PreserveOriginalSize;
        set
        {
            if (InverseFourierTransformScript.PreserveOriginalSize != value)
            {
                InverseFourierTransformScript.PreserveOriginalSize = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(PreserveOriginalSize), value);
            }
        }
    }

    public bool NormalizeOutput
    {
        get => InverseFourierTransformScript.NormalizeOutput;
        set
        {
            if (InverseFourierTransformScript.NormalizeOutput != value)
            {
                InverseFourierTransformScript.NormalizeOutput = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(NormalizeOutput), value);
            }
        }
    }

    public InverseFourierTransformViewModel(InverseFourierTransformScript script) : base(script)
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
        InverseLogTransform = true;
        CenterFFT = true;
        PreserveOriginalSize = true;
        NormalizeOutput = true;
        await Task.CompletedTask;
    }

    public void ApplyFrequencyFilter()
    {
        // 这里可以实现频率域滤波的逻辑
        // 目前只是一个占位符功能
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
            [nameof(InverseLogTransform)] = InverseLogTransform,
            [nameof(CenterFFT)] = CenterFFT,
            [nameof(PreserveOriginalSize)] = PreserveOriginalSize,
            [nameof(NormalizeOutput)] = NormalizeOutput
        };
    }

    public override async Task SetParameterDataAsync(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(InverseLogTransform), out var inverseLog))
            InverseLogTransform = Convert.ToBoolean(inverseLog);
        if (data.TryGetValue(nameof(CenterFFT), out var centerFFT))
            CenterFFT = Convert.ToBoolean(centerFFT);
        if (data.TryGetValue(nameof(PreserveOriginalSize), out var preserveSize))
            PreserveOriginalSize = Convert.ToBoolean(preserveSize);
        if (data.TryGetValue(nameof(NormalizeOutput), out var normalizeOutput))
            NormalizeOutput = Convert.ToBoolean(normalizeOutput);
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
