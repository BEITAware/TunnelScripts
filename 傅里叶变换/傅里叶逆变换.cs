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

/// <summary>
/// 重建质量枚举
/// </summary>
public enum ReconstructionQuality
{
    Low,
    Medium,
    High,
    Ultra
}

[RevivalScript(
    Name = "傅里叶逆变换",
    Author = "Revival Scripts",
    Description = "傅里叶逆变换 - 从幅度谱和相位谱重建原始图像，支持RGBA格式",
    Version = "1.0",
    Category = "频域处理",
    Color = "#FF6633"
)]
public class InverseFourierTransformScript : RevivalScriptBase
{
    [ScriptParameter(DisplayName = "自动检测参数", Description = "从元数据自动检测原始变换参数", Order = 0)]
    public bool AutoDetectParameters { get; set; } = true;

    [ScriptParameter(DisplayName = "应用了对数变换", Description = "输入的幅度谱是否应用了对数变换", Order = 1)]
    public bool WasLogTransformed { get; set; } = true;

    [ScriptParameter(DisplayName = "零频率已居中", Description = "输入的频谱是否已将零频率移到中心", Order = 2)]
    public bool WasCentered { get; set; } = true;

    [ScriptParameter(DisplayName = "应用了汉宁窗", Description = "原始变换是否应用了汉宁窗函数", Order = 3)]
    public bool WasWindowed { get; set; } = false;

    [ScriptParameter(DisplayName = "增强了相位谱对比度", Description = "输入的相位谱是否增强了对比度", Order = 4)]
    public bool WasPhaseEnhanced { get; set; } = true;

    [ScriptParameter(DisplayName = "变换了Alpha通道", Description = "原始变换是否对Alpha通道也进行了变换", Order = 5)]
    public bool WasAlphaTransformed { get; set; } = false;

    [ScriptParameter(DisplayName = "重建质量", Description = "重建图像的质量级别", Order = 6)]
    public ReconstructionQuality Quality { get; set; } = ReconstructionQuality.High;

    [ScriptParameter(DisplayName = "幅度缩放因子", Description = "手动调整幅度谱的缩放因子", Order = 7)]
    public double MagnitudeScale { get; set; } = 1.0;

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
            ["f32bmp"] = new PortDefinition("f32bmp", false, "重建的图像")
        };
    }

    public override Dictionary<string, object> Process(Dictionary<string, object> inputs, IScriptContext context)
    {
        // 检查输入是否有效
        if (!inputs.TryGetValue("f32bmp_magnitude", out var magnitudeObj) || magnitudeObj == null ||
            !inputs.TryGetValue("f32bmp_phase", out var phaseObj) || phaseObj == null)
        {
            return new Dictionary<string, object>
            {
                ["f32bmp"] = null
            };
        }

        if (!(magnitudeObj is Mat magnitudeMat) || magnitudeMat.Empty() ||
            !(phaseObj is Mat phaseMat) || phaseMat.Empty())
        {
            return new Dictionary<string, object>
            {
                ["f32bmp"] = null
            };
        }

        try
        {
            // 注意：元数据检测在ExtractMetadata方法中完成，这里不需要再次调用

            // 确保输入是RGBA格式
            Mat workingMagnitude = EnsureRGBAFormat(magnitudeMat);
            Mat workingPhase = EnsureRGBAFormat(phaseMat);

            // 分离RGBA通道
            Mat[] magnitudeChannels = Cv2.Split(workingMagnitude);
            Mat[] phaseChannels = Cv2.Split(workingPhase);

            // 分别对RGB通道进行逆傅里叶变换
            Mat[] reconstructedChannels = new Mat[4];

            // 处理RGB通道
            for (int i = 0; i < 3; i++)
            {
                reconstructedChannels[i] = PerformInverseFFT(magnitudeChannels[i], phaseChannels[i]);
            }

            // 处理Alpha通道
            if (WasAlphaTransformed)
            {
                reconstructedChannels[3] = PerformInverseFFT(magnitudeChannels[3], phaseChannels[3]);
            }
            else
            {
                // Alpha通道设为1.0（原始图像的Alpha通道）
                reconstructedChannels[3] = Mat.Ones(reconstructedChannels[0].Size(), MatType.CV_32F);
            }

            // 合并通道创建输出图像
            Mat result = new Mat();
            Cv2.Merge(reconstructedChannels, result);

            // 清理资源
            foreach (var ch in magnitudeChannels) ch.Dispose();
            foreach (var ch in phaseChannels) ch.Dispose();
            foreach (var ch in reconstructedChannels) ch.Dispose();
            workingMagnitude.Dispose();
            workingPhase.Dispose();

            return new Dictionary<string, object>
            {
                ["f32bmp"] = result
            };
        }
        catch (Exception ex)
        {
            throw new ApplicationException($"傅里叶逆变换节点处理失败: {ex.Message}", ex);
        }
    }

    // 存储从上游提取的元数据
    private Dictionary<string, object> _extractedMetadata = new Dictionary<string, object>();

    // 存储提取的重建数据
    private Dictionary<string, object> _reconstructionData = new Dictionary<string, object>();

    /// <summary>
    /// 从上游提取所需元数据
    /// </summary>
    public override void ExtractMetadata(Dictionary<string, object> upstreamMetadata)
    {
        // 调用基类方法
        base.ExtractMetadata(upstreamMetadata);

        // 存储元数据供后续使用
        _extractedMetadata = new Dictionary<string, object>(upstreamMetadata);

        System.Diagnostics.Debug.WriteLine($"[傅里叶逆变换] 接收到上游元数据，包含 {upstreamMetadata.Count} 个键");

        // 如果启用自动检测，立即应用元数据
        if (AutoDetectParameters)
        {
            DetectParametersFromMetadata();
        }
    }

    /// <summary>
    /// 从元数据自动检测参数
    /// </summary>
    private void DetectParametersFromMetadata()
    {
        try
        {
            bool parametersDetected = false;

            // 优先检测傅里叶重建参数（更准确）
            if (_extractedMetadata.TryGetValue("傅里叶重建参数", out var rebuildParams) &&
                rebuildParams is Dictionary<string, object> rebuildDict)
            {
                // 存储完整的重建数据
                _reconstructionData = new Dictionary<string, object>(rebuildDict);

                if (rebuildDict.TryGetValue("ApplyLogTransform", out var logTransform))
                {
                    WasLogTransformed = Convert.ToBoolean(logTransform);
                    parametersDetected = true;
                }
                if (rebuildDict.TryGetValue("CenterFFT", out var centerFFT))
                {
                    WasCentered = Convert.ToBoolean(centerFFT);
                    parametersDetected = true;
                }
                if (rebuildDict.TryGetValue("ApplyWindow", out var applyWindow))
                {
                    WasWindowed = Convert.ToBoolean(applyWindow);
                    parametersDetected = true;
                }
                if (rebuildDict.TryGetValue("TransformAlpha", out var transformAlpha))
                {
                    WasAlphaTransformed = Convert.ToBoolean(transformAlpha);
                    parametersDetected = true;
                }
                if (rebuildDict.TryGetValue("EnhancePhaseContrast", out var enhancePhase))
                {
                    WasPhaseEnhanced = Convert.ToBoolean(enhancePhase);
                    parametersDetected = true;
                }

                // 记录提取到的数值信息
                var originalMaxValue = rebuildDict.TryGetValue("OriginalMaxValue", out var origMax) ? origMax : "未知";
                var logMaxValue = rebuildDict.TryGetValue("LogMaxValue", out var logMax) ? logMax : "未知";
                var originalRows = rebuildDict.TryGetValue("OriginalRows", out var origRows) ? origRows : "未知";
                var originalCols = rebuildDict.TryGetValue("OriginalCols", out var origCols) ? origCols : "未知";

                System.Diagnostics.Debug.WriteLine($"[傅里叶逆变换] 从重建参数检测到: 对数变换={WasLogTransformed}, 居中={WasCentered}, 窗函数={WasWindowed}, Alpha变换={WasAlphaTransformed}, 相位增强={WasPhaseEnhanced}");
                System.Diagnostics.Debug.WriteLine($"[傅里叶逆变换] 数值信息: 原始最大值={originalMaxValue}, 对数最大值={logMaxValue}, 原始尺寸={originalRows}x{originalCols}");
            }
            // 如果没有重建参数，尝试从傅里叶变换参数中获取
            else if (_extractedMetadata.TryGetValue("傅里叶变换参数", out var fftParams) &&
                fftParams is Dictionary<string, object> fftDict)
            {
                if (fftDict.TryGetValue("ApplyLogTransform", out var logTransform))
                {
                    WasLogTransformed = Convert.ToBoolean(logTransform);
                    parametersDetected = true;
                }
                if (fftDict.TryGetValue("CenterFFT", out var centerFFT))
                {
                    WasCentered = Convert.ToBoolean(centerFFT);
                    parametersDetected = true;
                }
                if (fftDict.TryGetValue("ApplyWindow", out var applyWindow))
                {
                    WasWindowed = Convert.ToBoolean(applyWindow);
                    parametersDetected = true;
                }
                if (fftDict.TryGetValue("TransformAlpha", out var transformAlpha))
                {
                    WasAlphaTransformed = Convert.ToBoolean(transformAlpha);
                    parametersDetected = true;
                }
                if (fftDict.TryGetValue("EnhancePhaseContrast", out var enhancePhase))
                {
                    WasPhaseEnhanced = Convert.ToBoolean(enhancePhase);
                    parametersDetected = true;
                }

                System.Diagnostics.Debug.WriteLine($"[傅里叶逆变换] 从变换参数检测到: 对数变换={WasLogTransformed}, 居中={WasCentered}, 窗函数={WasWindowed}, Alpha变换={WasAlphaTransformed}, 相位增强={WasPhaseEnhanced}");
            }

            // 如果没有检测到参数，使用默认值
            if (!parametersDetected)
            {
                WasLogTransformed = true;  // 大多数情况下会应用对数变换
                WasCentered = true;        // 大多数情况下会居中
                WasWindowed = false;       // 窗函数不常用
                WasPhaseEnhanced = true;   // 相位谱通常会增强
                WasAlphaTransformed = false; // Alpha通道通常不变换

                System.Diagnostics.Debug.WriteLine($"[傅里叶逆变换] 未找到元数据，使用默认参数: 对数变换={WasLogTransformed}, 居中={WasCentered}, 窗函数={WasWindowed}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[傅里叶逆变换] 元数据检测失败: {ex.Message}");
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
    /// 执行逆傅里叶变换（单通道）
    /// </summary>
    private Mat PerformInverseFFT(Mat magnitudeChannel, Mat phaseChannel)
    {
        // 获取图像尺寸，优先使用重建数据中的原始尺寸
        int rows = magnitudeChannel.Rows;
        int cols = magnitudeChannel.Cols;
        int optimalRows = Cv2.GetOptimalDFTSize(rows);
        int optimalCols = Cv2.GetOptimalDFTSize(cols);

        // 如果有重建数据中的尺寸信息，使用它们
        if (_reconstructionData.TryGetValue("OriginalRows", out var origRowsObj) &&
            _reconstructionData.TryGetValue("OriginalCols", out var origColsObj) &&
            _reconstructionData.TryGetValue("OptimalRows", out var optRowsObj) &&
            _reconstructionData.TryGetValue("OptimalCols", out var optColsObj))
        {
            int originalRows = Convert.ToInt32(origRowsObj);
            int originalCols = Convert.ToInt32(origColsObj);
            int originalOptimalRows = Convert.ToInt32(optRowsObj);
            int originalOptimalCols = Convert.ToInt32(optColsObj);

            // 使用原始的尺寸信息
            rows = originalRows;
            cols = originalCols;
            optimalRows = originalOptimalRows;
            optimalCols = originalOptimalCols;

            System.Diagnostics.Debug.WriteLine($"[傅里叶逆变换] 使用重建数据中的尺寸: 原始={rows}x{cols}, 优化={optimalRows}x{optimalCols}");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[傅里叶逆变换] 使用当前图像尺寸: 原始={rows}x{cols}, 优化={optimalRows}x{optimalCols}");
        }

        // 确保是32位浮点格式
        Mat magnitude = new Mat();
        Mat phase = new Mat();

        if (magnitudeChannel.Type() != MatType.CV_32F)
        {
            magnitudeChannel.ConvertTo(magnitude, MatType.CV_32F);
        }
        else
        {
            magnitude = magnitudeChannel.Clone();
        }

        if (phaseChannel.Type() != MatType.CV_32F)
        {
            phaseChannel.ConvertTo(phase, MatType.CV_32F);
        }
        else
        {
            phase = phaseChannel.Clone();
        }

        // 应用幅度缩放因子
        if (Math.Abs(MagnitudeScale - 1.0) > 1e-6)
        {
            Cv2.Multiply(magnitude, Scalar.All(MagnitudeScale), magnitude);
        }

        // 检测输入数据的类型和范围
        double inputMinVal, inputMaxVal;
        Cv2.MinMaxLoc(magnitude, out inputMinVal, out inputMaxVal);
        bool isNormalized = (inputMaxVal <= 1.0 && inputMinVal >= 0.0);

        System.Diagnostics.Debug.WriteLine($"[傅里叶逆变换] 输入幅度谱范围: [{inputMinVal}, {inputMaxVal}], 是否归一化: {isNormalized}");

        // 逆对数变换（如果原始应用了对数变换）
        if (WasLogTransformed)
        {
            Mat tempMagnitude = new Mat();

            if (isNormalized)
            {
                // 输入是归一化的显示版本，需要先反归一化
                if (_reconstructionData.TryGetValue("LogMaxValue", out var logMaxObj))
                {
                    double logMaxValue = Convert.ToDouble(logMaxObj);

                    // 反归一化到对数变换后的范围
                    Cv2.Multiply(magnitude, Scalar.All(logMaxValue), tempMagnitude);

                    // 然后应用逆对数变换
                    Cv2.Exp(tempMagnitude, tempMagnitude);
                    Cv2.Subtract(tempMagnitude, Scalar.All(1), tempMagnitude);

                    System.Diagnostics.Debug.WriteLine($"[傅里叶逆变换] 处理归一化输入: 对数最大值={logMaxValue}");
                }
                else
                {
                    // 没有元数据，使用估算的逆变换
                    Cv2.Multiply(magnitude, Scalar.All(10.0), tempMagnitude); // 估算的缩放
                    Cv2.Exp(tempMagnitude, tempMagnitude);
                    Cv2.Subtract(tempMagnitude, Scalar.All(1), tempMagnitude);

                    System.Diagnostics.Debug.WriteLine($"[傅里叶逆变换] 处理归一化输入（无元数据）");
                }
            }
            else
            {
                // 输入是原始版本，直接应用逆对数变换
                Cv2.Exp(magnitude, tempMagnitude);
                Cv2.Subtract(tempMagnitude, Scalar.All(1), tempMagnitude);

                System.Diagnostics.Debug.WriteLine($"[傅里叶逆变换] 处理原始输入（未归一化）");
            }

            magnitude.Dispose();
            magnitude = tempMagnitude;
        }
        else if (isNormalized && _reconstructionData.TryGetValue("OriginalMaxValue", out var origMaxObj))
        {
            // 即使没有对数变换，如果输入是归一化的，也需要反归一化
            double originalMaxValue = Convert.ToDouble(origMaxObj);
            Mat tempMagnitude = new Mat();
            Cv2.Multiply(magnitude, Scalar.All(originalMaxValue), tempMagnitude);
            magnitude.Dispose();
            magnitude = tempMagnitude;

            System.Diagnostics.Debug.WriteLine($"[傅里叶逆变换] 反归一化到原始范围: 最大值={originalMaxValue}");
        }

        // 从归一化的相位谱恢复到-π到π范围
        Mat realPhase = new Mat();
        Cv2.Multiply(phase, Scalar.All(2 * Math.PI), realPhase);
        Cv2.Subtract(realPhase, Scalar.All(Math.PI), realPhase);

        // 逆相位谱对比度增强（如果原始增强了相位谱对比度）
        if (WasPhaseEnhanced && Quality >= ReconstructionQuality.High)
        {
            // 尝试逆CLAHE操作（近似）
            Mat enhancedPhase = new Mat();

            // 将相位重新归一化到0-1
            Mat tempPhase = new Mat();
            Cv2.Add(realPhase, Scalar.All(Math.PI), tempPhase);
            Cv2.Multiply(tempPhase, Scalar.All(1.0 / (2 * Math.PI)), tempPhase);

            // 应用轻微的高斯模糊来减少对比度增强的影响
            Cv2.GaussianBlur(tempPhase, enhancedPhase, new Size(3, 3), 0.5);

            // 重新转换回-π到π范围
            Cv2.Multiply(enhancedPhase, Scalar.All(2 * Math.PI), enhancedPhase);
            Cv2.Subtract(enhancedPhase, Scalar.All(Math.PI), enhancedPhase);

            realPhase.Dispose();
            realPhase = enhancedPhase;
            tempPhase.Dispose();
        }

        // 填充到最佳尺寸（如果需要）
        Mat paddedMagnitude = magnitude;
        Mat paddedPhase = realPhase;

        if (rows != optimalRows || cols != optimalCols)
        {
            paddedMagnitude = new Mat();
            paddedPhase = new Mat();
            Cv2.CopyMakeBorder(magnitude, paddedMagnitude, 0, optimalRows - rows, 0, optimalCols - cols,
                              BorderTypes.Constant, Scalar.All(0));
            Cv2.CopyMakeBorder(realPhase, paddedPhase, 0, optimalRows - rows, 0, optimalCols - cols,
                              BorderTypes.Constant, Scalar.All(0));
        }

        // 从幅度谱和相位谱重建复数频域表示
        Mat realPart = new Mat();
        Mat imagPart = new Mat();

        // real = magnitude * cos(phase)
        // imag = magnitude * sin(phase)
        // 使用OpenCV的数学函数来计算cos和sin
        Mat cosPhase = new Mat();
        Mat sinPhase = new Mat();

        // 使用OpenCV的数学运算
        // 创建临时矩阵来存储cos和sin值
        paddedPhase.CopyTo(cosPhase);
        paddedPhase.CopyTo(sinPhase);

        // 使用矩阵运算计算cos和sin（通过查找表或近似方法）
        // 这里使用一个简化的方法：通过Exp函数来计算复数
        // e^(i*phase) = cos(phase) + i*sin(phase)
        Mat negPhase = new Mat();
        Cv2.Multiply(paddedPhase, Scalar.All(-1), negPhase);

        // 使用欧拉公式的近似计算
        // cos(x) ≈ 1 - x²/2 + x⁴/24 (泰勒级数前几项)
        Mat phase2 = new Mat();
        Mat phase4 = new Mat();
        Cv2.Multiply(paddedPhase, paddedPhase, phase2);
        Cv2.Multiply(phase2, phase2, phase4);

        Mat ones = Mat.Ones(paddedPhase.Size(), MatType.CV_32F);
        Mat temp1 = new Mat();
        Mat temp2 = new Mat();

        // cos(x) ≈ 1 - x²/2 + x⁴/24
        Cv2.Multiply(phase2, Scalar.All(-0.5), temp1);
        Cv2.Multiply(phase4, Scalar.All(1.0/24.0), temp2);
        Cv2.Add(ones, temp1, cosPhase);
        Cv2.Add(cosPhase, temp2, cosPhase);

        // sin(x) ≈ x - x³/6 + x⁵/120
        Mat phase3 = new Mat();
        Cv2.Multiply(phase2, paddedPhase, phase3);
        Cv2.Multiply(phase3, Scalar.All(-1.0/6.0), temp1);
        Cv2.Add(paddedPhase, temp1, sinPhase);

        Cv2.Multiply(paddedMagnitude, cosPhase, realPart);
        Cv2.Multiply(paddedMagnitude, sinPhase, imagPart);

        // 清理临时矩阵
        negPhase.Dispose();
        phase2.Dispose();
        phase4.Dispose();
        phase3.Dispose();
        ones.Dispose();
        temp1.Dispose();
        temp2.Dispose();

        // 创建复数格式的频域数据
        Mat[] complexChannels = { realPart, imagPart };
        Mat complexSpectrum = new Mat();
        Cv2.Merge(complexChannels, complexSpectrum);

        // 逆零频率居中（如果原始居中了）
        if (WasCentered)
        {
            ShiftDFT(complexSpectrum);
        }

        // 执行逆傅里叶变换
        Mat reconstructed = new Mat();
        Cv2.Dft(complexSpectrum, reconstructed, DftFlags.Inverse | DftFlags.Scale);

        // 提取实部（重建的图像）
        Mat[] reconstructedChannels = Cv2.Split(reconstructed);
        Mat result = reconstructedChannels[0].Clone();

        // 裁剪回原始尺寸（如果需要）
        if (rows != result.Rows || cols != result.Cols)
        {
            Mat croppedResult = new Mat(result, new Rect(0, 0, cols, rows));
            Mat finalResult = croppedResult.Clone();
            result.Dispose();
            result = finalResult;
        }

        // 确保结果在有效范围内
        Cv2.Threshold(result, result, 1.0, 1.0, ThresholdTypes.Trunc);
        Cv2.Threshold(result, result, 0.0, 0.0, ThresholdTypes.Tozero);

        // 清理资源
        magnitude.Dispose();
        phase.Dispose();
        realPhase.Dispose();
        if (paddedMagnitude != magnitude) paddedMagnitude.Dispose();
        if (paddedPhase != realPhase) paddedPhase.Dispose();
        realPart.Dispose();
        imagPart.Dispose();
        cosPhase.Dispose();
        sinPhase.Dispose();
        complexSpectrum.Dispose();
        reconstructed.Dispose();
        foreach (var ch in reconstructedChannels) ch.Dispose();

        return result;
    }

    /// <summary>
    /// 移动DFT结果使零频率居中（与正变换相同的函数）
    /// </summary>
    private void ShiftDFT(Mat dft)
    {
        int cx = dft.Cols / 2;
        int cy = dft.Rows / 2;

        // 处理奇数尺寸的情况
        int cx2 = dft.Cols - cx;  // 右半部分的宽度
        int cy2 = dft.Rows - cy;  // 下半部分的高度

        Mat q0 = new Mat(dft, new Rect(0, 0, cx, cy));           // 左上
        Mat q1 = new Mat(dft, new Rect(cx, 0, cx2, cy));         // 右上
        Mat q2 = new Mat(dft, new Rect(0, cy, cx, cy2));         // 左下
        Mat q3 = new Mat(dft, new Rect(cx, cy, cx2, cy2));       // 右下

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
                ["AutoDetectParameters"] = AutoDetectParameters,
                ["WasLogTransformed"] = WasLogTransformed,
                ["WasCentered"] = WasCentered,
                ["WasWindowed"] = WasWindowed,
                ["WasPhaseEnhanced"] = WasPhaseEnhanced,
                ["WasAlphaTransformed"] = WasAlphaTransformed,
                ["Quality"] = Quality.ToString(),
                ["MagnitudeScale"] = MagnitudeScale
            };
        }

        // 注入重建信息
        if (!metadata.ContainsKey("傅里叶重建信息"))
        {
            metadata["傅里叶重建信息"] = new Dictionary<string, object>
            {
                ["NodeInstanceId"] = NodeInstanceId,
                ["ReconstructionTimestamp"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                ["Version"] = "1.0"
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

        // 应用Aero主题样式 - 使用interfacepanelbar的渐变背景
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
        var viewModel = CreateViewModel() as InverseFourierTransformViewModel;
        mainPanel.DataContext = viewModel;

        // 标题
        var titleLabel = new Label
        {
            Content = "傅里叶逆变换",
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial")
        };
        mainPanel.Children.Add(titleLabel);

        // 创建复选框控件
        mainPanel.Children.Add(CreateCheckBoxControl("自动检测参数", nameof(AutoDetectParameters), viewModel));
        mainPanel.Children.Add(CreateCheckBoxControl("应用了对数变换", nameof(WasLogTransformed), viewModel));
        mainPanel.Children.Add(CreateCheckBoxControl("零频率已居中", nameof(WasCentered), viewModel));
        mainPanel.Children.Add(CreateCheckBoxControl("应用了汉宁窗", nameof(WasWindowed), viewModel));
        mainPanel.Children.Add(CreateCheckBoxControl("增强了相位谱对比度", nameof(WasPhaseEnhanced), viewModel));
        mainPanel.Children.Add(CreateCheckBoxControl("变换了Alpha通道", nameof(WasAlphaTransformed), viewModel));

        // 重建质量选择
        var qualityPanel = new StackPanel { Margin = new Thickness(0, 5, 0, 5) };
        var qualityLabel = new Label
        {
            Content = "重建质量:",
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11,
            Margin = new Thickness(5, 0, 5, 0)
        };
        qualityPanel.Children.Add(qualityLabel);

        var qualityComboBox = new ComboBox
        {
            Margin = new Thickness(5, 0, 5, 0),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11
        };
        qualityComboBox.Items.Add("Low");
        qualityComboBox.Items.Add("Medium");
        qualityComboBox.Items.Add("High");
        qualityComboBox.Items.Add("Ultra");

        var qualityBinding = new Binding(nameof(Quality))
        {
            Source = viewModel,
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        };
        qualityComboBox.SetBinding(ComboBox.SelectedItemProperty, qualityBinding);
        qualityPanel.Children.Add(qualityComboBox);
        mainPanel.Children.Add(qualityPanel);

        // 幅度缩放因子
        var scalePanel = new StackPanel { Margin = new Thickness(0, 5, 0, 5) };
        var scaleLabel = new Label
        {
            Content = "幅度缩放因子:",
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11,
            Margin = new Thickness(5, 0, 5, 0)
        };
        scalePanel.Children.Add(scaleLabel);

        var scaleTextBox = new TextBox
        {
            Margin = new Thickness(5, 0, 5, 0),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11,
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF2D3748")),
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF4A5568"))
        };

        var scaleBinding = new Binding(nameof(MagnitudeScale))
        {
            Source = viewModel,
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        };
        scaleTextBox.SetBinding(TextBox.TextProperty, scaleBinding);
        scalePanel.Children.Add(scaleTextBox);
        mainPanel.Children.Add(scalePanel);

        // 自动检测按钮
        var detectButton = new Button
        {
            Content = "重新检测参数",
            Margin = new Thickness(2, 10, 2, 2),
            Padding = new Thickness(10, 5, 10, 5),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new GradientStop((Color)ColorConverter.ConvertFromString("#00FFFFFF"), 0),
                    new GradientStop((Color)ColorConverter.ConvertFromString("#1AFFFFFF"), 0.135436),
                    new GradientStop((Color)ColorConverter.ConvertFromString("#17FFFFFF"), 0.487941),
                    new GradientStop((Color)ColorConverter.ConvertFromString("#00000004"), 0.517625),
                    new GradientStop((Color)ColorConverter.ConvertFromString("#FF1F8EAD"), 0.729128)
                },
                new System.Windows.Point(0.5, -0.667875), new System.Windows.Point(0.5, 1.66787)
            ),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF000000")),
            BorderThickness = new Thickness(1)
        };

        detectButton.Click += (s, e) =>
        {
            viewModel?.ForceDetectParameters();
        };

        mainPanel.Children.Add(detectButton);

        // 重置按钮
        var resetButton = new Button
        {
            Content = "重置所有参数",
            Margin = new Thickness(2, 5, 2, 2),
            Padding = new Thickness(10, 5, 10, 5),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new GradientStop((Color)ColorConverter.ConvertFromString("#00FFFFFF"), 0),
                    new GradientStop((Color)ColorConverter.ConvertFromString("#1AFFFFFF"), 0.135436),
                    new GradientStop((Color)ColorConverter.ConvertFromString("#17FFFFFF"), 0.487941),
                    new GradientStop((Color)ColorConverter.ConvertFromString("#00000004"), 0.517625),
                    new GradientStop((Color)ColorConverter.ConvertFromString("#FF1F8EAD"), 0.729128)
                },
                new System.Windows.Point(0.5, -0.667875), new System.Windows.Point(0.5, 1.66787)
            ),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF000000")),
            BorderThickness = new Thickness(1)
        };

        resetButton.Click += (s, e) =>
        {
            viewModel?.ResetToDefault();
        };

        mainPanel.Children.Add(resetButton);

        return mainPanel;
    }

    private StackPanel CreateCheckBoxControl(string label, string propertyName, InverseFourierTransformViewModel viewModel)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 5, 0, 5) };

        var checkBox = new CheckBox
        {
            Content = label,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11,
            Margin = new Thickness(5, 0, 5, 0)
        };

        // 数据绑定
        var binding = new Binding(propertyName)
        {
            Source = viewModel,
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        };
        checkBox.SetBinding(CheckBox.IsCheckedProperty, binding);

        panel.Children.Add(checkBox);
        return panel;
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
            [nameof(AutoDetectParameters)] = AutoDetectParameters,
            [nameof(WasLogTransformed)] = WasLogTransformed,
            [nameof(WasCentered)] = WasCentered,
            [nameof(WasWindowed)] = WasWindowed,
            [nameof(WasPhaseEnhanced)] = WasPhaseEnhanced,
            [nameof(WasAlphaTransformed)] = WasAlphaTransformed,
            [nameof(Quality)] = Quality.ToString(),
            [nameof(MagnitudeScale)] = MagnitudeScale,
            ["NodeInstanceId"] = NodeInstanceId
        };
    }

    public override void DeserializeParameters(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(AutoDetectParameters), out var autoDetect))
            AutoDetectParameters = Convert.ToBoolean(autoDetect);
        if (data.TryGetValue(nameof(WasLogTransformed), out var wasLog))
            WasLogTransformed = Convert.ToBoolean(wasLog);
        if (data.TryGetValue(nameof(WasCentered), out var wasCentered))
            WasCentered = Convert.ToBoolean(wasCentered);
        if (data.TryGetValue(nameof(WasWindowed), out var wasWindowed))
            WasWindowed = Convert.ToBoolean(wasWindowed);
        if (data.TryGetValue(nameof(WasPhaseEnhanced), out var wasPhaseEnhanced))
            WasPhaseEnhanced = Convert.ToBoolean(wasPhaseEnhanced);
        if (data.TryGetValue(nameof(WasAlphaTransformed), out var wasAlphaTransformed))
            WasAlphaTransformed = Convert.ToBoolean(wasAlphaTransformed);
        if (data.TryGetValue(nameof(Quality), out var quality))
            Quality = Enum.Parse<ReconstructionQuality>(quality.ToString());
        if (data.TryGetValue(nameof(MagnitudeScale), out var magnitudeScale))
            MagnitudeScale = Convert.ToDouble(magnitudeScale);
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

    /// <summary>
    /// 强制重新检测参数（从已存储的元数据）
    /// </summary>
    public void ForceParameterDetection()
    {
        if (_extractedMetadata.Count > 0)
        {
            DetectParametersFromMetadata();
            System.Diagnostics.Debug.WriteLine($"[傅里叶逆变换] 强制重新检测参数完成");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[傅里叶逆变换] 没有可用的元数据进行参数检测");
        }
    }
}

public class InverseFourierTransformViewModel : ScriptViewModelBase
{
    private InverseFourierTransformScript InverseFourierTransformScript => (InverseFourierTransformScript)Script;

    public bool AutoDetectParameters
    {
        get => InverseFourierTransformScript.AutoDetectParameters;
        set
        {
            if (InverseFourierTransformScript.AutoDetectParameters != value)
            {
                InverseFourierTransformScript.AutoDetectParameters = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(AutoDetectParameters), value);
            }
        }
    }

    public bool WasLogTransformed
    {
        get => InverseFourierTransformScript.WasLogTransformed;
        set
        {
            if (InverseFourierTransformScript.WasLogTransformed != value)
            {
                InverseFourierTransformScript.WasLogTransformed = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(WasLogTransformed), value);
            }
        }
    }

    public bool WasCentered
    {
        get => InverseFourierTransformScript.WasCentered;
        set
        {
            if (InverseFourierTransformScript.WasCentered != value)
            {
                InverseFourierTransformScript.WasCentered = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(WasCentered), value);
            }
        }
    }

    public bool WasWindowed
    {
        get => InverseFourierTransformScript.WasWindowed;
        set
        {
            if (InverseFourierTransformScript.WasWindowed != value)
            {
                InverseFourierTransformScript.WasWindowed = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(WasWindowed), value);
            }
        }
    }

    public bool WasPhaseEnhanced
    {
        get => InverseFourierTransformScript.WasPhaseEnhanced;
        set
        {
            if (InverseFourierTransformScript.WasPhaseEnhanced != value)
            {
                InverseFourierTransformScript.WasPhaseEnhanced = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(WasPhaseEnhanced), value);
            }
        }
    }

    public bool WasAlphaTransformed
    {
        get => InverseFourierTransformScript.WasAlphaTransformed;
        set
        {
            if (InverseFourierTransformScript.WasAlphaTransformed != value)
            {
                InverseFourierTransformScript.WasAlphaTransformed = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(WasAlphaTransformed), value);
            }
        }
    }

    public ReconstructionQuality Quality
    {
        get => InverseFourierTransformScript.Quality;
        set
        {
            if (InverseFourierTransformScript.Quality != value)
            {
                InverseFourierTransformScript.Quality = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(Quality), value);
            }
        }
    }

    public double MagnitudeScale
    {
        get => InverseFourierTransformScript.MagnitudeScale;
        set
        {
            if (Math.Abs(InverseFourierTransformScript.MagnitudeScale - value) > 1e-6)
            {
                InverseFourierTransformScript.MagnitudeScale = value;
                OnPropertyChanged();
                NotifyParameterChanged(nameof(MagnitudeScale), value);
            }
        }
    }

    public InverseFourierTransformViewModel(InverseFourierTransformScript script) : base(script)
    {
    }

    private void NotifyParameterChanged(string parameterName, object value)
    {
        if (Script is RevivalScriptBase rsb)
        {
            rsb.OnParameterChanged(parameterName, value);
        }
    }

    public async Task ResetToDefault()
    {
        AutoDetectParameters = true;
        WasLogTransformed = true;
        WasCentered = true;
        WasWindowed = false;
        WasPhaseEnhanced = true;
        WasAlphaTransformed = false;
        Quality = ReconstructionQuality.High;
        MagnitudeScale = 1.0;
        await Task.CompletedTask;
    }

    public void ForceDetectParameters()
    {
        // 强制重新检测参数
        AutoDetectParameters = true;

        // 如果脚本实例可用，强制重新检测
        if (InverseFourierTransformScript != null)
        {
            InverseFourierTransformScript.ForceParameterDetection();
        }

        // 通知参数已更改
        OnPropertyChanged(nameof(AutoDetectParameters));
        OnPropertyChanged(nameof(WasLogTransformed));
        OnPropertyChanged(nameof(WasCentered));
        OnPropertyChanged(nameof(WasWindowed));
        OnPropertyChanged(nameof(WasPhaseEnhanced));
        OnPropertyChanged(nameof(WasAlphaTransformed));
    }

    public override async Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
    {
        await Task.CompletedTask;
    }

    public override ScriptValidationResult ValidateParameter(string parameterName, object value)
    {
        if (parameterName == nameof(MagnitudeScale))
        {
            if (value is double scale)
            {
                if (scale <= 0)
                {
                    return new ScriptValidationResult(false, "幅度缩放因子必须大于0");
                }
                if (scale > 100)
                {
                    return new ScriptValidationResult(false, "幅度缩放因子不应超过100");
                }
            }
        }

        return new ScriptValidationResult(true);
    }

    public override Dictionary<string, object> GetParameterData()
    {
        return new Dictionary<string, object>
        {
            [nameof(AutoDetectParameters)] = AutoDetectParameters,
            [nameof(WasLogTransformed)] = WasLogTransformed,
            [nameof(WasCentered)] = WasCentered,
            [nameof(WasWindowed)] = WasWindowed,
            [nameof(WasPhaseEnhanced)] = WasPhaseEnhanced,
            [nameof(WasAlphaTransformed)] = WasAlphaTransformed,
            [nameof(Quality)] = Quality,
            [nameof(MagnitudeScale)] = MagnitudeScale
        };
    }

    public override async Task SetParameterDataAsync(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(AutoDetectParameters), out var autoDetect))
            AutoDetectParameters = Convert.ToBoolean(autoDetect);
        if (data.TryGetValue(nameof(WasLogTransformed), out var wasLog))
            WasLogTransformed = Convert.ToBoolean(wasLog);
        if (data.TryGetValue(nameof(WasCentered), out var wasCentered))
            WasCentered = Convert.ToBoolean(wasCentered);
        if (data.TryGetValue(nameof(WasWindowed), out var wasWindowed))
            WasWindowed = Convert.ToBoolean(wasWindowed);
        if (data.TryGetValue(nameof(WasPhaseEnhanced), out var wasPhaseEnhanced))
            WasPhaseEnhanced = Convert.ToBoolean(wasPhaseEnhanced);
        if (data.TryGetValue(nameof(WasAlphaTransformed), out var wasAlphaTransformed))
            WasAlphaTransformed = Convert.ToBoolean(wasAlphaTransformed);
        if (data.TryGetValue(nameof(Quality), out var quality))
            Quality = (ReconstructionQuality)quality;
        if (data.TryGetValue(nameof(MagnitudeScale), out var magnitudeScale))
            MagnitudeScale = Convert.ToDouble(magnitudeScale);
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