using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Tunnel_Next.Services.Scripting;
using OpenCvSharp;
using System.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks;

[TunnelExtensionScript(
    Name = "图层混合",
    Author = "BEITAware",
    Description = "多图层混合工具",
    Version = "2.0",
    Category = "图像处理",
    Color = "#FF6B35"
)]
public class FlexibleImageBlendScript : TunnelExtensionScriptBase
{
    public enum BlendMode
    {
        // 基础混合模式
        Normal,         // 正常
        Average,        // 平均混合
        Add,            // 相加混合
        Max,            // 最大值
        Min,            // 最小值

        // 变暗混合模式
        Darken,         // 变暗
        Multiply,       // 正片叠底
        ColorBurn,      // 颜色加深
        LinearBurn,     // 线性加深

        // 变亮混合模式
        Lighten,        // 变亮
        Screen,         // 滤色
        ColorDodge,     // 颜色减淡
        LinearDodge,    // 线性减淡(添加)

        // 对比混合模式
        Overlay,        // 叠加
        SoftLight,      // 柔光
        HardLight,      // 强光
        VividLight,     // 亮光
        LinearLight,    // 线性光
        PinLight,       // 点光
        HardMix,        // 实色混合

        // 差异混合模式
        Difference,     // 差值
        Exclusion,      // 排除
        Subtract,       // 减去
        Divide          // 划分
    }

    [ScriptParameter(DisplayName = "混合模式", Description = "选择图像混合模式")]
    public BlendMode MixMode { get; set; } = BlendMode.Normal;

    [ScriptParameter(DisplayName = "输出强度", Description = "输出图像强度调节 (0.1-2.0)")]
    public float OutputIntensity { get; set; } = 1.0f;

    [ScriptParameter(DisplayName = "全局不透明度", Description = "所有图层的全局不透明度 (0.0-1.0)")]
    public float GlobalOpacity { get; set; } = 1.0f;

    [ScriptParameter(DisplayName = "尊重Alpha通道", Description = "是否考虑输入图像的Alpha通道")]
    public bool RespectAlpha { get; set; } = true;

    [ScriptParameter(DisplayName = "启用并行处理", Description = "对大图像启用多线程并行处理")]
    public bool EnableParallelProcessing { get; set; } = true;

    [ScriptParameter(DisplayName = "逆序混合", Description = "是否反转图层混合顺序")]
    public bool ReverseOrder { get; set; } = false;

    public override Dictionary<string, PortDefinition> GetInputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            // 第一个输入端口是固定的
            ["Input1"] = new PortDefinition("f32bmp", false, "第一个图像输入"),

            // 第二个输入端口是灵活的，连接后会自动添加更多端口
            ["Input2"] = new PortDefinition("f32bmp", true, "灵活图像输入 - 连接后会自动添加更多端口")
        };
    }

    public override Dictionary<string, PortDefinition> GetOutputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            ["Output"] = new PortDefinition("f32bmp", false, "混合后的图像输出")
        };
    }

    public override Dictionary<string, object> Process(Dictionary<string, object> inputs, IScriptContext context)
    {
        try
        {
            var result = new Dictionary<string, object>();

            // 收集所有输入图像，按端口名称排序以确保一致的处理顺序
            var inputImages = new List<Mat>();
            var sortedInputs = inputs.OrderBy(kvp => kvp.Key).ToList();

            foreach (var kvp in sortedInputs)
            {
                if (kvp.Value is Mat inputMat && !inputMat.Empty())
                {
                    // 确保图像是32位浮点RGBA格式
                    var processedMat = new Mat();
                    if (inputMat.Type() != MatType.CV_32FC4)
                    {
                        // 转换为32位浮点RGBA格式，并将8bit值归一化到0-1
                        if (inputMat.Channels() == 3)
                        {
                            // RGB转RGBA，添加Alpha通道
                            var rgbaChannels = new Mat[4];
                            var rgbChannels = inputMat.Split();
                            rgbaChannels[0] = rgbChannels[0]; // R
                            rgbaChannels[1] = rgbChannels[1]; // G
                            rgbaChannels[2] = rgbChannels[2]; // B
                            rgbaChannels[3] = new Mat(inputMat.Size(), MatType.CV_8U, Scalar.All(255)); // A = 255

                            using (var merged = new Mat())
                            {
                                Cv2.Merge(rgbaChannels, merged);
                                merged.ConvertTo(processedMat, MatType.CV_32FC4, 1.0 / 255.0);
                            }

                            // 清理临时对象
                            foreach (var ch in rgbaChannels) ch.Dispose();
                            foreach (var ch in rgbChannels) ch.Dispose();
                        }
                        else
                        {
                            double scale = inputMat.Depth() == MatType.CV_8U ? 1.0 / 255.0 : 1.0; // 若已是float则无需缩放
                            inputMat.ConvertTo(processedMat, MatType.CV_32FC4, scale);
                        }
                    }
                    else
                    {
                        processedMat = inputMat.Clone();
                    }
                    inputImages.Add(processedMat);
                }
            }

            if (inputImages.Count == 0)
            {
                return result;
            }

            if (inputImages.Count == 1)
            {
                // 只有一个输入，应用全局不透明度和强度调节
                var singleOutput = ApplyGlobalEffects(inputImages[0].Clone());
                result["Output"] = singleOutput;
                return result;
            }

            // 执行图像混合
            var blendedImage = BlendImages(inputImages);
            result["Output"] = blendedImage;

            return result;
        }
        catch (Exception ex)
        {
            // 将错误信息输出到调试控制台，并返回一个包含空Mat的结果，以避免下游节点出错
            System.Diagnostics.Debug.WriteLine($"图层混合脚本处理失败: {ex.ToString()}");
            var errorResult = new Mat();
            return new Dictionary<string, object>
            {
                ["Output"] = errorResult
            };
        }
    }

    /// <summary>
    /// 混合多个图像
    /// </summary>
    private Mat BlendImages(List<Mat> images)
    {
        if (images.Count == 0)
            throw new ArgumentException("图像列表不能为空");

        if (images.Count == 1)
            return ApplyGlobalEffects(images[0].Clone());

        // 根据 ReverseOrder 决定层叠顺序
        // ReverseOrder == false : 输入顺序视为"上 → 下"，因此需要反向遍历列表（最后一个作为底图）
        // ReverseOrder == true  : 输入顺序视为"下 → 上"，保持原来的正向遍历

        IEnumerable<Mat> iterate;
        if (ReverseOrder)
        {
            iterate = images; // 0 -> n-1 （下 → 上）
        }
        else
        {
            iterate = images.AsEnumerable().Reverse(); // n-1 -> 0 （下 → 上）
        }

        // 初始化 result 为第一张（底图）
        var enumerator = iterate.GetEnumerator();
        enumerator.MoveNext();
        var first = new Mat();
        enumerator.Current.ConvertTo(first, MatType.CV_32FC4);
        var result = first;

        // 依次把更"上层"的图像叠加到 result 上
        while (enumerator.MoveNext())
        {
            var currentImage = new Mat();
            enumerator.Current.ConvertTo(currentImage, MatType.CV_32FC4);

            if (result.Size() != currentImage.Size())
            {
                Cv2.Resize(currentImage, currentImage, result.Size());
            }

            var blended = BlendTwoImages(result, currentImage, MixMode, GlobalOpacity);
            result.Dispose();
            result = blended;

            currentImage.Dispose();
        }

        // 应用最终效果
        return ApplyGlobalEffects(result);
    }

    /// <summary>
    /// 混合两个图像
    /// </summary>
    private Mat BlendTwoImages(Mat baseImage, Mat blendImage, BlendMode mode, float opacity)
    {
        var result = new Mat();

        // 确保两个图像尺寸一致
        if (baseImage.Size() != blendImage.Size())
        {
            Cv2.Resize(blendImage, blendImage, baseImage.Size());
        }

        // 根据混合模式进行处理
        switch (mode)
        {
            case BlendMode.Normal:
                result = BlendNormal(baseImage, blendImage, opacity);
                break;

            case BlendMode.Average:
                // 累积平均混合
                Cv2.AddWeighted(baseImage, 0.5, blendImage, 0.5, 0, result);
                break;

            case BlendMode.Add:
                Cv2.Add(baseImage, blendImage, result);
                break;

            case BlendMode.Max:
                Cv2.Max(baseImage, blendImage, result);
                break;

            case BlendMode.Min:
                Cv2.Min(baseImage, blendImage, result);
                break;

            case BlendMode.Multiply:
                result = BlendMultiply(baseImage, blendImage, opacity);
                break;

            case BlendMode.Screen:
                result = BlendScreen(baseImage, blendImage, opacity);
                break;

            case BlendMode.Overlay:
                result = BlendOverlay(baseImage, blendImage, opacity);
                break;

            case BlendMode.Difference:
                result = BlendDifference(baseImage, blendImage, opacity);
                break;

            case BlendMode.Darken:
                result = BlendDarken(baseImage, blendImage, opacity);
                break;

            case BlendMode.Lighten:
                result = BlendLighten(baseImage, blendImage, opacity);
                break;

            case BlendMode.ColorBurn:
                result = BlendColorBurn(baseImage, blendImage, opacity);
                break;

            case BlendMode.LinearBurn:
                result = BlendLinearBurn(baseImage, blendImage, opacity);
                break;

            case BlendMode.ColorDodge:
                result = BlendColorDodge(baseImage, blendImage, opacity);
                break;

            case BlendMode.LinearDodge:
                result = BlendLinearDodge(baseImage, blendImage, opacity);
                break;

            case BlendMode.SoftLight:
                result = BlendSoftLight(baseImage, blendImage, opacity);
                break;

            case BlendMode.HardLight:
                result = BlendHardLight(baseImage, blendImage, opacity);
                break;

            case BlendMode.Exclusion:
                result = BlendExclusion(baseImage, blendImage, opacity);
                break;

            case BlendMode.Subtract:
                result = BlendSubtract(baseImage, blendImage, opacity);
                break;

            default:
                // 默认使用正常混合
                Cv2.AddWeighted(baseImage, 1.0 - opacity, blendImage, opacity, 0, result);
                break;
        }

        return result;
    }

    /// <summary>
    /// 应用全局效果（强度和不透明度）
    /// </summary>
    private Mat ApplyGlobalEffects(Mat input)
    {
        var result = input.Clone();

        // 应用输出强度调节
        if (Math.Abs(OutputIntensity - 1.0f) > 0.001f)
        {
            var temp = new Mat();
            result.ConvertTo(temp, result.Type(), OutputIntensity);
            result.Dispose();
            result = temp;
        }

        // 应用全局不透明度（如果不是1.0）
        if (Math.Abs(GlobalOpacity - 1.0f) > 0.001f)
        {
            var temp = new Mat();
            result.ConvertTo(temp, result.Type(), GlobalOpacity);
            result.Dispose();
            result = temp;
        }

        return result;
    }

    #region 混合模式实现

    /// <summary>
    /// 保证 Mat 的数据在内存中连续（IsContinuous），若不连续则克隆一份连续副本
    /// </summary>
    private Mat EnsureContinuous(Mat mat)
    {
        return mat.IsContinuous() ? mat : mat.Clone();
    }

    /// <summary>
    /// 普通混合，采用标准 Source-Over 公式，该公式正确处理Alpha通道
    /// </summary>
    private Mat BlendNormal(Mat baseImage, Mat blendImage, float opacity)
    {
        var result = new Mat(baseImage.Size(), baseImage.Type());

        // 确保图像为32位浮点RGBA格式，并保证内存连续
        Mat base32f;
        if (baseImage.Type() != MatType.CV_32FC4)
        {
            base32f = new Mat();
            baseImage.ConvertTo(base32f, MatType.CV_32FC4);
        }
        else
        {
            base32f = baseImage.Clone();
        }
        base32f = EnsureContinuous(base32f);

        Mat blend32f;
        if (blendImage.Type() != MatType.CV_32FC4)
        {
            blend32f = new Mat();
            blendImage.ConvertTo(blend32f, MatType.CV_32FC4);
        }
        else
        {
            blend32f = blendImage.Clone();
        }
        blend32f = EnsureContinuous(blend32f);

        result = EnsureContinuous(result);

        unsafe
        {
            var basePtr = (float*)base32f.DataPointer;
            var blendPtr = (float*)blend32f.DataPointer;
            var resultPtr = (float*)result.DataPointer;

            int totalPixels = base32f.Rows * base32f.Cols;

            // 根据是否启用并行处理来选择执行路径
            if (EnableParallelProcessing && totalPixels > 10000) // 仅对大图像启用并行
            {
                Parallel.For(0, totalPixels, i =>
                {
                    ProcessPixel(i, basePtr, blendPtr, resultPtr, opacity);
                });
            }
            else
            {
                for (int i = 0; i < totalPixels; i++)
                {
                    ProcessPixel(i, basePtr, blendPtr, resultPtr, opacity);
                }
            }
        }

        base32f.Dispose();
        blend32f.Dispose();
        return result;
    }

    private unsafe void ProcessPixel(int i, float* basePtr, float* blendPtr, float* resultPtr, float opacity)
    {
        int idx = i * 4;

        // 读取底图(dst)和顶图(src)的Alpha通道
        float dstA = basePtr[idx + 3];
        float srcA = blendPtr[idx + 3];

        // 计算顶图的有效Alpha，它受图层自身Alpha和全局不透明度的共同影响
        float effectiveSrcA = (RespectAlpha ? srcA : 1.0f) * opacity;
        effectiveSrcA = Math.Max(0.0f, Math.Min(1.0f, effectiveSrcA));

        // 如果顶图完全透明，则结果就是底图，无需计算
        if (effectiveSrcA < 1e-6f)
        {
            for (int c = 0; c < 4; c++)
            {
                resultPtr[idx + c] = basePtr[idx + c];
            }
            return;
        }

        // 标准的 "Source-Over" Alpha 合成公式
        float outA = effectiveSrcA + dstA * (1.0f - effectiveSrcA);
        
        // 如果输出的alpha极小（几乎完全透明），为避免除零错误，直接输出透明像素
        if (outA < 1e-6f)
        {
            resultPtr[idx] = 0;
            resultPtr[idx + 1] = 0;
            resultPtr[idx + 2] = 0;
            resultPtr[idx + 3] = 0;
            return;
        }

        float invOutA = 1.0f / outA;

        // 标准的 "Source-Over" 颜色合成公式（针对非预乘Alpha）
        for (int c = 0; c < 3; c++)
        {
            float srcC = blendPtr[idx + c];
            float dstC = basePtr[idx + c];
            resultPtr[idx + c] = (srcC * effectiveSrcA + dstC * dstA * (1.0f - effectiveSrcA)) * invOutA;
        }
        resultPtr[idx + 3] = outA;
    }

    /// <summary>
    /// 正片叠底混合
    /// </summary>
    private Mat BlendMultiply(Mat baseImage, Mat blendImage, float opacity)
    {
        var result = new Mat(baseImage.Size(), baseImage.Type());

        // 确保图像是32位浮点格式
        var base32f = new Mat();
        var blend32f = new Mat();
        baseImage.ConvertTo(base32f, MatType.CV_32FC4);
        blendImage.ConvertTo(blend32f, MatType.CV_32FC4);

        unsafe
        {
            var basePtr = (float*)base32f.DataPointer;
            var blendPtr = (float*)blend32f.DataPointer;
            var resultPtr = (float*)result.DataPointer;

            int totalPixels = base32f.Rows * base32f.Cols;

            for (int i = 0; i < totalPixels; i++)
            {
                int idx = i * 4; // RGBA 4个通道

                // 读取Alpha通道并计算有效不透明度（权重）
                float baseAlpha = basePtr[idx + 3];
                float blendAlpha = blendPtr[idx + 3];
                float effectiveOpacity = RespectAlpha ? opacity * blendAlpha : opacity;

                // RGB通道进行正片叠底并使用有效不透明度
                for (int c = 0; c < 3; c++)
                {
                    float baseVal = basePtr[idx + c];
                    float blendVal = blendPtr[idx + c];
                    float multiplied = baseVal * blendVal;

                    resultPtr[idx + c] = baseVal * (1.0f - effectiveOpacity) + multiplied * effectiveOpacity;
                }

                // Alpha通道混合
                resultPtr[idx + 3] = baseAlpha * (1.0f - effectiveOpacity) + blendAlpha * effectiveOpacity;
            }
        }

        base32f.Dispose();
        blend32f.Dispose();
        return result;
    }

    /// <summary>
    /// 滤色混合
    /// </summary>
    private Mat BlendScreen(Mat baseImage, Mat blendImage, float opacity)
    {
        var result = new Mat(baseImage.Size(), baseImage.Type());

        // 确保图像是32位浮点格式
        var base32f = new Mat();
        var blend32f = new Mat();
        baseImage.ConvertTo(base32f, MatType.CV_32FC4);
        blendImage.ConvertTo(blend32f, MatType.CV_32FC4);

        unsafe
        {
            var basePtr = (float*)base32f.DataPointer;
            var blendPtr = (float*)blend32f.DataPointer;
            var resultPtr = (float*)result.DataPointer;

            int totalPixels = base32f.Rows * base32f.Cols;

            for (int i = 0; i < totalPixels; i++)
            {
                int idx = i * 4; // RGBA 4个通道

                // 读取Alpha通道并计算有效不透明度（权重）
                float baseAlpha = basePtr[idx + 3];
                float blendAlpha = blendPtr[idx + 3];
                float effectiveOpacity = RespectAlpha ? opacity * blendAlpha : opacity;

                // RGB通道进行滤色混合: 1 - (1-base) * (1-blend)
                for (int c = 0; c < 3; c++)
                {
                    float baseVal = basePtr[idx + c];
                    float blendVal = blendPtr[idx + c];
                    float screened = 1.0f - (1.0f - baseVal) * (1.0f - blendVal);

                    resultPtr[idx + c] = baseVal * (1.0f - effectiveOpacity) + screened * effectiveOpacity;
                }

                // Alpha通道混合
                resultPtr[idx + 3] = baseAlpha * (1.0f - effectiveOpacity) + blendAlpha * effectiveOpacity;
            }
        }

        base32f.Dispose();
        blend32f.Dispose();
        return result;
    }

    /// <summary>
    /// 叠加混合
    /// </summary>
    private Mat BlendOverlay(Mat baseImage, Mat blendImage, float opacity)
    {
        var result = new Mat(baseImage.Size(), baseImage.Type());

        // 确保图像是32位浮点格式
        var base32f = new Mat();
        var blend32f = new Mat();
        baseImage.ConvertTo(base32f, MatType.CV_32FC4);
        blendImage.ConvertTo(blend32f, MatType.CV_32FC4);

        unsafe
        {
            var basePtr = (float*)base32f.DataPointer;
            var blendPtr = (float*)blend32f.DataPointer;
            var resultPtr = (float*)result.DataPointer;

            int totalPixels = base32f.Rows * base32f.Cols;

            for (int i = 0; i < totalPixels; i++)
            {
                int idx = i * 4; // RGBA 4个通道

                // 读取Alpha通道并计算有效不透明度
                float baseAlpha = basePtr[idx + 3];
                float blendAlpha = blendPtr[idx + 3];
                float effectiveOpacity = RespectAlpha ? opacity * blendAlpha : opacity;

                // RGB通道进行叠加混合
                for (int c = 0; c < 3; c++)
                {
                    float baseVal = basePtr[idx + c];
                    float blendVal = blendPtr[idx + c];
                    float overlayed;

                    if (baseVal < 0.5f)
                    {
                        overlayed = 2.0f * baseVal * blendVal;
                    }
                    else
                    {
                        overlayed = 1.0f - 2.0f * (1.0f - baseVal) * (1.0f - blendVal);
                    }

                    resultPtr[idx + c] = baseVal * (1.0f - effectiveOpacity) + overlayed * effectiveOpacity;
                }

                // Alpha通道混合
                resultPtr[idx + 3] = baseAlpha * (1.0f - effectiveOpacity) + blendAlpha * effectiveOpacity;
            }
        }

        base32f.Dispose();
        blend32f.Dispose();
        return result;
    }

    /// <summary>
    /// 差值混合
    /// </summary>
    private Mat BlendDifference(Mat baseImage, Mat blendImage, float opacity)
    {
        var result = new Mat(baseImage.Size(), baseImage.Type());

        // 确保图像是32位浮点格式
        var base32f = new Mat();
        var blend32f = new Mat();
        baseImage.ConvertTo(base32f, MatType.CV_32FC4);
        blendImage.ConvertTo(blend32f, MatType.CV_32FC4);

        unsafe
        {
            var basePtr = (float*)base32f.DataPointer;
            var blendPtr = (float*)blend32f.DataPointer;
            var resultPtr = (float*)result.DataPointer;

            int totalPixels = base32f.Rows * base32f.Cols;

            for (int i = 0; i < totalPixels; i++)
            {
                int idx = i * 4; // RGBA 4个通道

                // 读取Alpha并计算有效不透明度
                float baseAlpha = basePtr[idx + 3];
                float blendAlpha = blendPtr[idx + 3];
                float effectiveOpacity = RespectAlpha ? opacity * blendAlpha : opacity;

                // RGB通道进行差值混合
                for (int c = 0; c < 3; c++)
                {
                    float baseVal = basePtr[idx + c];
                    float blendVal = blendPtr[idx + c];
                    float difference = Math.Abs(baseVal - blendVal);

                    resultPtr[idx + c] = baseVal * (1.0f - effectiveOpacity) + difference * effectiveOpacity;
                }

                // Alpha通道混合
                resultPtr[idx + 3] = baseAlpha * (1.0f - effectiveOpacity) + blendAlpha * effectiveOpacity;
            }
        }

        base32f.Dispose();
        blend32f.Dispose();
        return result;
    }

    /// <summary>
    /// 变暗混合
    /// </summary>
    private Mat BlendDarken(Mat baseImage, Mat blendImage, float opacity)
    {
        var result = new Mat(baseImage.Size(), baseImage.Type());

        // 确保图像是32位浮点格式
        var base32f = new Mat();
        var blend32f = new Mat();
        baseImage.ConvertTo(base32f, MatType.CV_32FC4);
        blendImage.ConvertTo(blend32f, MatType.CV_32FC4);

        unsafe
        {
            var basePtr = (float*)base32f.DataPointer;
            var blendPtr = (float*)blend32f.DataPointer;
            var resultPtr = (float*)result.DataPointer;

            int totalPixels = base32f.Rows * base32f.Cols;

            for (int i = 0; i < totalPixels; i++)
            {
                int idx = i * 4; // RGBA 4个通道

                // 读取Alpha并计算有效不透明度
                float baseAlpha = basePtr[idx + 3];
                float blendAlpha = blendPtr[idx + 3];
                float effectiveOpacity = RespectAlpha ? opacity * blendAlpha : opacity;

                // RGB通道进行变暗混合
                for (int c = 0; c < 3; c++)
                {
                    float baseVal = basePtr[idx + c];
                    float blendVal = blendPtr[idx + c];
                    float darkened = Math.Min(baseVal, blendVal);

                    resultPtr[idx + c] = baseVal * (1.0f - effectiveOpacity) + darkened * effectiveOpacity;
                }

                // Alpha通道混合
                resultPtr[idx + 3] = baseAlpha * (1.0f - effectiveOpacity) + blendAlpha * effectiveOpacity;
            }
        }

        base32f.Dispose();
        blend32f.Dispose();
        return result;
    }

    /// <summary>
    /// 变亮混合
    /// </summary>
    private Mat BlendLighten(Mat baseImage, Mat blendImage, float opacity)
    {
        var result = new Mat(baseImage.Size(), baseImage.Type());

        // 确保图像是32位浮点格式
        var base32f = new Mat();
        var blend32f = new Mat();
        baseImage.ConvertTo(base32f, MatType.CV_32FC4);
        blendImage.ConvertTo(blend32f, MatType.CV_32FC4);

        unsafe
        {
            var basePtr = (float*)base32f.DataPointer;
            var blendPtr = (float*)blend32f.DataPointer;
            var resultPtr = (float*)result.DataPointer;

            int totalPixels = base32f.Rows * base32f.Cols;

            for (int i = 0; i < totalPixels; i++)
            {
                int idx = i * 4; // RGBA 4个通道

                // 读取Alpha并计算有效不透明度
                float baseAlpha = basePtr[idx + 3];
                float blendAlpha = blendPtr[idx + 3];
                float effectiveOpacity = RespectAlpha ? opacity * blendAlpha : opacity;

                // RGB通道进行变亮混合
                for (int c = 0; c < 3; c++)
                {
                    float baseVal = basePtr[idx + c];
                    float blendVal = blendPtr[idx + c];
                    float lightened = Math.Max(baseVal, blendVal);

                    resultPtr[idx + c] = baseVal * (1.0f - effectiveOpacity) + lightened * effectiveOpacity;
                }

                // Alpha通道混合
                resultPtr[idx + 3] = baseAlpha * (1.0f - effectiveOpacity) + blendAlpha * effectiveOpacity;
            }
        }

        base32f.Dispose();
        blend32f.Dispose();
        return result;
    }

    /// <summary>
    /// 颜色加深混合
    /// </summary>
    private Mat BlendColorBurn(Mat baseImage, Mat blendImage, float opacity)
    {
        var result = new Mat(baseImage.Size(), baseImage.Type());

        // 确保图像是32位浮点格式
        var base32f = new Mat();
        var blend32f = new Mat();
        baseImage.ConvertTo(base32f, MatType.CV_32FC4);
        blendImage.ConvertTo(blend32f, MatType.CV_32FC4);

        unsafe
        {
            var basePtr = (float*)base32f.DataPointer;
            var blendPtr = (float*)blend32f.DataPointer;
            var resultPtr = (float*)result.DataPointer;

            int totalPixels = base32f.Rows * base32f.Cols;

            for (int i = 0; i < totalPixels; i++)
            {
                int idx = i * 4; // RGBA 4个通道

                // 读取Alpha并计算有效不透明度
                float baseAlpha = basePtr[idx + 3];
                float blendAlpha = blendPtr[idx + 3];
                float effectiveOpacity = RespectAlpha ? opacity * blendAlpha : opacity;

                // RGB通道进行颜色加深混合
                for (int c = 0; c < 3; c++)
                {
                    float baseVal = basePtr[idx + c];
                    float blendVal = blendPtr[idx + c];
                    float burned;

                    if (blendVal == 0.0f)
                    {
                        burned = 0.0f;
                    }
                    else
                    {
                        burned = Math.Max(0.0f, 1.0f - (1.0f - baseVal) / blendVal);
                    }

                    resultPtr[idx + c] = baseVal * (1.0f - effectiveOpacity) + burned * effectiveOpacity;
                }

                // Alpha通道混合
                resultPtr[idx + 3] = baseAlpha * (1.0f - effectiveOpacity) + blendAlpha * effectiveOpacity;
            }
        }

        base32f.Dispose();
        blend32f.Dispose();
        return result;
    }

    /// <summary>
    /// 线性加深混合
    /// </summary>
    private Mat BlendLinearBurn(Mat baseImage, Mat blendImage, float opacity)
    {
        var result = new Mat(baseImage.Size(), baseImage.Type());

        // 确保图像是32位浮点格式
        var base32f = new Mat();
        var blend32f = new Mat();
        baseImage.ConvertTo(base32f, MatType.CV_32FC4);
        blendImage.ConvertTo(blend32f, MatType.CV_32FC4);

        unsafe
        {
            var basePtr = (float*)base32f.DataPointer;
            var blendPtr = (float*)blend32f.DataPointer;
            var resultPtr = (float*)result.DataPointer;

            int totalPixels = base32f.Rows * base32f.Cols;

            for (int i = 0; i < totalPixels; i++)
            {
                int idx = i * 4; // RGBA 4个通道

                // 读取Alpha并计算有效不透明度
                float baseAlpha = basePtr[idx + 3];
                float blendAlpha = blendPtr[idx + 3];
                float effectiveOpacity = RespectAlpha ? opacity * blendAlpha : opacity;

                // RGB通道进行线性加深混合: base + blend - 1
                for (int c = 0; c < 3; c++)
                {
                    float baseVal = basePtr[idx + c];
                    float blendVal = blendPtr[idx + c];
                    float linearBurned = Math.Max(0.0f, baseVal + blendVal - 1.0f);

                    resultPtr[idx + c] = baseVal * (1.0f - effectiveOpacity) + linearBurned * effectiveOpacity;
                }

                // Alpha通道混合
                resultPtr[idx + 3] = baseAlpha * (1.0f - effectiveOpacity) + blendAlpha * effectiveOpacity;
            }
        }

        base32f.Dispose();
        blend32f.Dispose();
        return result;
    }

    /// <summary>
    /// 颜色减淡混合
    /// </summary>
    private Mat BlendColorDodge(Mat baseImage, Mat blendImage, float opacity)
    {
        var result = new Mat(baseImage.Size(), baseImage.Type());

        // 确保图像是32位浮点格式
        var base32f = new Mat();
        var blend32f = new Mat();
        baseImage.ConvertTo(base32f, MatType.CV_32FC4);
        blendImage.ConvertTo(blend32f, MatType.CV_32FC4);

        unsafe
        {
            var basePtr = (float*)base32f.DataPointer;
            var blendPtr = (float*)blend32f.DataPointer;
            var resultPtr = (float*)result.DataPointer;

            int totalPixels = base32f.Rows * base32f.Cols;

            for (int i = 0; i < totalPixels; i++)
            {
                int idx = i * 4; // RGBA 4个通道

                // 读取Alpha并计算有效不透明度
                float baseAlpha = basePtr[idx + 3];
                float blendAlpha = blendPtr[idx + 3];
                float effectiveOpacity = RespectAlpha ? opacity * blendAlpha : opacity;

                // RGB通道进行颜色减淡混合
                for (int c = 0; c < 3; c++)
                {
                    float baseVal = basePtr[idx + c];
                    float blendVal = blendPtr[idx + c];
                    float dodged;

                    if (blendVal >= 1.0f)
                    {
                        dodged = 1.0f;
                    }
                    else
                    {
                        dodged = Math.Min(1.0f, baseVal / (1.0f - blendVal));
                    }

                    resultPtr[idx + c] = baseVal * (1.0f - effectiveOpacity) + dodged * effectiveOpacity;
                }

                // Alpha通道混合
                resultPtr[idx + 3] = baseAlpha * (1.0f - effectiveOpacity) + blendAlpha * effectiveOpacity;
            }
        }

        base32f.Dispose();
        blend32f.Dispose();
        return result;
    }

    /// <summary>
    /// 线性减淡混合
    /// </summary>
    private Mat BlendLinearDodge(Mat baseImage, Mat blendImage, float opacity)
    {
        var result = new Mat(baseImage.Size(), baseImage.Type());

        // 确保图像是32位浮点格式
        var base32f = new Mat();
        var blend32f = new Mat();
        baseImage.ConvertTo(base32f, MatType.CV_32FC4);
        blendImage.ConvertTo(blend32f, MatType.CV_32FC4);

        unsafe
        {
            var basePtr = (float*)base32f.DataPointer;
            var blendPtr = (float*)blend32f.DataPointer;
            var resultPtr = (float*)result.DataPointer;

            int totalPixels = base32f.Rows * base32f.Cols;

            for (int i = 0; i < totalPixels; i++)
            {
                int idx = i * 4; // RGBA 4个通道

                // 读取Alpha并计算有效不透明度
                float baseAlpha = basePtr[idx + 3];
                float blendAlpha = blendPtr[idx + 3];
                float effectiveOpacity = RespectAlpha ? opacity * blendAlpha : opacity;

                // RGB通道进行线性减淡混合: base + blend
                for (int c = 0; c < 3; c++)
                {
                    float baseVal = basePtr[idx + c];
                    float blendVal = blendPtr[idx + c];
                    float linearDodged = Math.Min(1.0f, baseVal + blendVal);

                    resultPtr[idx + c] = baseVal * (1.0f - effectiveOpacity) + linearDodged * effectiveOpacity;
                }

                // Alpha通道混合
                resultPtr[idx + 3] = baseAlpha * (1.0f - effectiveOpacity) + blendAlpha * effectiveOpacity;
            }
        }

        base32f.Dispose();
        blend32f.Dispose();
        return result;
    }

    /// <summary>
    /// 柔光混合
    /// </summary>
    private Mat BlendSoftLight(Mat baseImage, Mat blendImage, float opacity)
    {
        var result = new Mat(baseImage.Size(), baseImage.Type());

        // 确保图像是32位浮点格式
        var base32f = new Mat();
        var blend32f = new Mat();
        baseImage.ConvertTo(base32f, MatType.CV_32FC4);
        blendImage.ConvertTo(blend32f, MatType.CV_32FC4);

        unsafe
        {
            var basePtr = (float*)base32f.DataPointer;
            var blendPtr = (float*)blend32f.DataPointer;
            var resultPtr = (float*)result.DataPointer;

            int totalPixels = base32f.Rows * base32f.Cols;

            for (int i = 0; i < totalPixels; i++)
            {
                int idx = i * 4; // RGBA 4个通道

                // 读取Alpha并计算有效不透明度
                float baseAlpha = basePtr[idx + 3];
                float blendAlpha = blendPtr[idx + 3];
                float effectiveOpacity = RespectAlpha ? opacity * blendAlpha : opacity;

                // RGB通道进行柔光混合
                for (int c = 0; c < 3; c++)
                {
                    float baseVal = basePtr[idx + c];
                    float blendVal = blendPtr[idx + c];
                    float softLight;

                    if (blendVal < 0.5f)
                    {
                        softLight = 2.0f * baseVal * blendVal + baseVal * baseVal * (1.0f - 2.0f * blendVal);
                    }
                    else
                    {
                        softLight = 2.0f * baseVal * (1.0f - blendVal) + (float)Math.Sqrt(baseVal) * (2.0f * blendVal - 1.0f);
                    }

                    resultPtr[idx + c] = baseVal * (1.0f - effectiveOpacity) + softLight * effectiveOpacity;
                }

                // Alpha通道混合
                resultPtr[idx + 3] = baseAlpha * (1.0f - effectiveOpacity) + blendAlpha * effectiveOpacity;
            }
        }

        base32f.Dispose();
        blend32f.Dispose();
        return result;
    }

    /// <summary>
    /// 强光混合
    /// </summary>
    private Mat BlendHardLight(Mat baseImage, Mat blendImage, float opacity)
    {
        var result = new Mat(baseImage.Size(), baseImage.Type());

        // 确保图像是32位浮点格式
        var base32f = new Mat();
        var blend32f = new Mat();
        baseImage.ConvertTo(base32f, MatType.CV_32FC4);
        blendImage.ConvertTo(blend32f, MatType.CV_32FC4);

        unsafe
        {
            var basePtr = (float*)base32f.DataPointer;
            var blendPtr = (float*)blend32f.DataPointer;
            var resultPtr = (float*)result.DataPointer;

            int totalPixels = base32f.Rows * base32f.Cols;

            for (int i = 0; i < totalPixels; i++)
            {
                int idx = i * 4; // RGBA 4个通道

                // 读取Alpha并计算有效不透明度
                float baseAlpha = basePtr[idx + 3];
                float blendAlpha = blendPtr[idx + 3];
                float effectiveOpacity = RespectAlpha ? opacity * blendAlpha : opacity;

                // RGB通道进行强光混合
                for (int c = 0; c < 3; c++)
                {
                    float baseVal = basePtr[idx + c];
                    float blendVal = blendPtr[idx + c];
                    float hardLight;

                    if (blendVal < 0.5f)
                    {
                        hardLight = 2.0f * baseVal * blendVal;
                    }
                    else
                    {
                        hardLight = 1.0f - 2.0f * (1.0f - baseVal) * (1.0f - blendVal);
                    }

                    resultPtr[idx + c] = baseVal * (1.0f - effectiveOpacity) + hardLight * effectiveOpacity;
                }

                // Alpha通道混合
                resultPtr[idx + 3] = baseAlpha * (1.0f - effectiveOpacity) + blendAlpha * effectiveOpacity;
            }
        }

        base32f.Dispose();
        blend32f.Dispose();
        return result;
    }

    /// <summary>
    /// 排除混合
    /// </summary>
    private Mat BlendExclusion(Mat baseImage, Mat blendImage, float opacity)
    {
        var result = new Mat(baseImage.Size(), baseImage.Type());

        // 确保图像是32位浮点格式
        var base32f = new Mat();
        var blend32f = new Mat();
        baseImage.ConvertTo(base32f, MatType.CV_32FC4);
        blendImage.ConvertTo(blend32f, MatType.CV_32FC4);

        unsafe
        {
            var basePtr = (float*)base32f.DataPointer;
            var blendPtr = (float*)blend32f.DataPointer;
            var resultPtr = (float*)result.DataPointer;

            int totalPixels = base32f.Rows * base32f.Cols;

            for (int i = 0; i < totalPixels; i++)
            {
                int idx = i * 4; // RGBA 4个通道

                // 读取Alpha并计算有效不透明度
                float baseAlpha = basePtr[idx + 3];
                float blendAlpha = blendPtr[idx + 3];
                float effectiveOpacity = RespectAlpha ? opacity * blendAlpha : opacity;

                // RGB通道进行排除混合
                for (int c = 0; c < 3; c++)
                {
                    float baseVal = basePtr[idx + c];
                    float blendVal = blendPtr[idx + c];
                    float excluded = baseVal + blendVal - 2.0f * baseVal * blendVal;

                    resultPtr[idx + c] = baseVal * (1.0f - effectiveOpacity) + excluded * effectiveOpacity;
                }

                // Alpha通道混合
                resultPtr[idx + 3] = baseAlpha * (1.0f - effectiveOpacity) + blendAlpha * effectiveOpacity;
            }
        }

        base32f.Dispose();
        blend32f.Dispose();
        return result;
    }

    /// <summary>
    /// 减去混合
    /// </summary>
    private Mat BlendSubtract(Mat baseImage, Mat blendImage, float opacity)
    {
        var result = new Mat(baseImage.Size(), baseImage.Type());

        // 确保图像是32位浮点格式
        var base32f = new Mat();
        var blend32f = new Mat();
        baseImage.ConvertTo(base32f, MatType.CV_32FC4);
        blendImage.ConvertTo(blend32f, MatType.CV_32FC4);

        unsafe
        {
            var basePtr = (float*)base32f.DataPointer;
            var blendPtr = (float*)blend32f.DataPointer;
            var resultPtr = (float*)result.DataPointer;

            int totalPixels = base32f.Rows * base32f.Cols;

            for (int i = 0; i < totalPixels; i++)
            {
                int idx = i * 4; // RGBA 4个通道

                // 读取Alpha并计算有效不透明度
                float baseAlpha = basePtr[idx + 3];
                float blendAlpha = blendPtr[idx + 3];
                float effectiveOpacity = RespectAlpha ? opacity * blendAlpha : opacity;

                // RGB通道进行减去混合
                for (int c = 0; c < 3; c++)
                {
                    float baseVal = basePtr[idx + c];
                    float blendVal = blendPtr[idx + c];
                    float subtracted = Math.Max(0.0f, baseVal - blendVal);

                    resultPtr[idx + c] = baseVal * (1.0f - effectiveOpacity) + subtracted * effectiveOpacity;
                }

                // Alpha通道混合
                resultPtr[idx + 3] = baseAlpha * (1.0f - effectiveOpacity) + blendAlpha * effectiveOpacity;
            }
        }

        base32f.Dispose();
        blend32f.Dispose();
        return result;
    }

    #endregion

    public override FrameworkElement CreateParameterControl()
    {
        var panel = new StackPanel { Margin = new Thickness(5) };

        // 应用Aero主题样式 - 使用interfacepanelbar的渐变背景
        panel.Background = new LinearGradientBrush(
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

        // 标题
        var title = new TextBlock
        {
            Text = "灵活端口图像混合",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 10),
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial")
        };
        panel.Children.Add(title);

        // 混合模式选择
        var modeLabel = new Label
        {
            Content = "混合模式:",
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11
        };
        var modeCombo = new ComboBox
        {
            Margin = new Thickness(0, 0, 0, 10),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F0F0F0")),
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#333333")),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A1F28")),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 4, 6, 4),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11
        };

        // 创建混合模式的友好显示名称
        var blendModeDisplayNames = new Dictionary<BlendMode, string>
        {
            { BlendMode.Normal, "正常" },
            { BlendMode.Average, "平均" },
            { BlendMode.Add, "相加" },
            { BlendMode.Max, "最大值" },
            { BlendMode.Min, "最小值" },
            { BlendMode.Darken, "变暗" },
            { BlendMode.Multiply, "正片叠底" },
            { BlendMode.ColorBurn, "颜色加深" },
            { BlendMode.LinearBurn, "线性加深" },
            { BlendMode.Lighten, "变亮" },
            { BlendMode.Screen, "滤色" },
            { BlendMode.ColorDodge, "颜色减淡" },
            { BlendMode.LinearDodge, "线性减淡" },
            { BlendMode.Overlay, "叠加" },
            { BlendMode.SoftLight, "柔光" },
            { BlendMode.HardLight, "强光" },
            { BlendMode.VividLight, "亮光" },
            { BlendMode.LinearLight, "线性光" },
            { BlendMode.PinLight, "点光" },
            { BlendMode.HardMix, "实色混合" },
            { BlendMode.Difference, "差值" },
            { BlendMode.Exclusion, "排除" },
            { BlendMode.Subtract, "减去" },
            { BlendMode.Divide, "划分" }
        };

        modeCombo.ItemsSource = blendModeDisplayNames;
        modeCombo.DisplayMemberPath = "Value";
        modeCombo.SelectedValuePath = "Key";
        modeCombo.SelectedValue = MixMode;
        modeCombo.SelectionChanged += (s, e) =>
        {
            if (modeCombo.SelectedValue is BlendMode newMode)
            {
                var oldValue = MixMode;
                MixMode = newMode;
                OnParameterChanged(nameof(MixMode), newMode);
            }
        };

        // 全局不透明度滑块
        var globalOpacityLabel = new Label
        {
            Content = $"全局不透明度: {GlobalOpacity:F2}",
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11
        };
        var globalOpacitySlider = new Slider
        {
            Minimum = 0.0,
            Maximum = 1.0,
            Value = GlobalOpacity,
            TickFrequency = 0.01,
            IsSnapToTickEnabled = false,
            Margin = new Thickness(0, 0, 0, 10),
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF"))
        };
        globalOpacitySlider.ValueChanged += (s, e) =>
        {
            var oldValue = GlobalOpacity;
            GlobalOpacity = (float)e.NewValue;
            globalOpacityLabel.Content = $"全局不透明度: {GlobalOpacity:F2}";
            OnParameterChanged(nameof(GlobalOpacity), GlobalOpacity);
        };

        // 输出强度滑块
        var intensityLabel = new Label
        {
            Content = $"输出强度: {OutputIntensity:F2}",
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11
        };
        var intensitySlider = new Slider
        {
            Minimum = 0.1,
            Maximum = 2.0,
            Value = OutputIntensity,
            TickFrequency = 0.1,
            IsSnapToTickEnabled = false,
            Margin = new Thickness(0, 0, 0, 10),
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF"))
        };
        intensitySlider.ValueChanged += (s, e) =>
        {
            var oldValue = OutputIntensity;
            OutputIntensity = (float)e.NewValue;
            intensityLabel.Content = $"输出强度: {OutputIntensity:F2}";
            OnParameterChanged(nameof(OutputIntensity), OutputIntensity);
        };

        // Alpha通道处理复选框
        var alphaCheckBox = new CheckBox
        {
            Content = "尊重Alpha通道",
            IsChecked = RespectAlpha,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 5)
        };
        alphaCheckBox.Checked += (s, e) =>
        {
            var oldValue = RespectAlpha;
            RespectAlpha = true;
            OnParameterChanged(nameof(RespectAlpha), RespectAlpha);
        };
        alphaCheckBox.Unchecked += (s, e) =>
        {
            var oldValue = RespectAlpha;
            RespectAlpha = false;
            OnParameterChanged(nameof(RespectAlpha), RespectAlpha);
        };

        // 并行处理复选框
        var parallelCheckBox = new CheckBox
        {
            Content = "启用并行处理",
            IsChecked = EnableParallelProcessing,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 10)
        };
        parallelCheckBox.Checked += (s, e) =>
        {
            var oldValue = EnableParallelProcessing;
            EnableParallelProcessing = true;
            OnParameterChanged(nameof(EnableParallelProcessing), EnableParallelProcessing);
        };
        parallelCheckBox.Unchecked += (s, e) =>
        {
            var oldValue = EnableParallelProcessing;
            EnableParallelProcessing = false;
            OnParameterChanged(nameof(EnableParallelProcessing), EnableParallelProcessing);
        };

        // 逆序混合复选框
        var reverseCheckBox = new CheckBox
        {
            Content = "逆序混合",
            IsChecked = ReverseOrder,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 10)
        };
        reverseCheckBox.Checked += (s, e) =>
        {
            var oldValue = ReverseOrder;
            ReverseOrder = true;
            OnParameterChanged(nameof(ReverseOrder), ReverseOrder);
        };
        reverseCheckBox.Unchecked += (s, e) =>
        {
            var oldValue = ReverseOrder;
            ReverseOrder = false;
            OnParameterChanged(nameof(ReverseOrder), ReverseOrder);
        };

        // 说明文本
        var description = new TextBlock
        {
            Text = "专业的多图层混合工具，支持24种混合模式。连接多个图像到灵活端口，系统会自动添加更多输入端口。支持不同尺寸的图像自动调整和Alpha通道处理。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#777777")),
            FontSize = 11,
            Margin = new Thickness(0, 5, 0, 0),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial")
        };

        panel.Children.Add(modeLabel);
        panel.Children.Add(modeCombo);
        panel.Children.Add(globalOpacityLabel);
        panel.Children.Add(globalOpacitySlider);
        panel.Children.Add(intensityLabel);
        panel.Children.Add(intensitySlider);
        panel.Children.Add(alphaCheckBox);
        panel.Children.Add(parallelCheckBox);
        panel.Children.Add(reverseCheckBox);
        panel.Children.Add(description);

        return panel;
    }

    public override IScriptViewModel CreateViewModel()
    {
        return new FlexibleImageBlendViewModel(this);
    }

    public override Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
    {
        switch (parameterName)
        {
            case nameof(MixMode):
                if (newValue is BlendMode mode)
                    MixMode = mode;
                break;
            case nameof(OutputIntensity):
                if (newValue is float intensity)
                    OutputIntensity = Math.Max(0.1f, Math.Min(2.0f, intensity));
                break;
            case nameof(GlobalOpacity):
                if (newValue is float opacity)
                    GlobalOpacity = Math.Max(0.0f, Math.Min(1.0f, opacity));
                break;
            case nameof(RespectAlpha):
                if (newValue is bool respectAlpha)
                    RespectAlpha = respectAlpha;
                break;
            case nameof(EnableParallelProcessing):
                if (newValue is bool enableParallel)
                    EnableParallelProcessing = enableParallel;
                break;
            case nameof(ReverseOrder):
                if (newValue is bool revOrder)
                    ReverseOrder = revOrder;
                break;
        }
        return Task.CompletedTask;
    }

    public override Dictionary<string, object> SerializeParameters()
    {
        return new Dictionary<string, object>
        {
            [nameof(MixMode)] = MixMode.ToString(),
            [nameof(OutputIntensity)] = OutputIntensity,
            [nameof(GlobalOpacity)] = GlobalOpacity,
            [nameof(RespectAlpha)] = RespectAlpha,
            [nameof(EnableParallelProcessing)] = EnableParallelProcessing,
            [nameof(ReverseOrder)] = ReverseOrder
        };
    }

    public override void DeserializeParameters(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(MixMode), out var modeValue) &&
            Enum.TryParse<BlendMode>(modeValue?.ToString(), out var mode))
        {
            MixMode = mode;
        }

        if (data.TryGetValue(nameof(OutputIntensity), out var intensityValue) &&
            float.TryParse(intensityValue?.ToString(), out var intensity))
        {
            OutputIntensity = Math.Max(0.1f, Math.Min(2.0f, intensity));
        }

        if (data.TryGetValue(nameof(GlobalOpacity), out var opacityValue) &&
            float.TryParse(opacityValue?.ToString(), out var opacity))
        {
            GlobalOpacity = Math.Max(0.0f, Math.Min(1.0f, opacity));
        }

        if (data.TryGetValue(nameof(RespectAlpha), out var respectAlphaValue) &&
            bool.TryParse(respectAlphaValue?.ToString(), out var respectAlpha))
        {
            RespectAlpha = respectAlpha;
        }

        if (data.TryGetValue(nameof(EnableParallelProcessing), out var enableParallelValue) &&
            bool.TryParse(enableParallelValue?.ToString(), out var enableParallel))
        {
            EnableParallelProcessing = enableParallel;
        }

        if (data.TryGetValue(nameof(ReverseOrder), out var revOrderValue) &&
            bool.TryParse(revOrderValue?.ToString(), out var revOrder))
        {
            ReverseOrder = revOrder;
        }
    }
}

public class FlexibleImageBlendViewModel : ScriptViewModelBase
{
    private readonly FlexibleImageBlendScript _script;

    public FlexibleImageBlendViewModel(FlexibleImageBlendScript script) : base(script)
    {
        _script = script;
    }

    public override Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
    {
        return _script.OnParameterChangedAsync(parameterName, oldValue, newValue);
    }

    public override ScriptValidationResult ValidateParameter(string parameterName, object value)
    {
        switch (parameterName)
        {
            case nameof(FlexibleImageBlendScript.OutputIntensity):
                if (value is float intensityVal && (intensityVal < 0.1f || intensityVal > 2.0f))
                    return new ScriptValidationResult(false, "输出强度必须在0.1到2.0之间");
                break;
            case nameof(FlexibleImageBlendScript.GlobalOpacity):
                if (value is float opacityVal && (opacityVal < 0.0f || opacityVal > 1.0f))
                    return new ScriptValidationResult(false, "全局不透明度必须在0.0到1.0之间");
                break;
            case nameof(FlexibleImageBlendScript.EnableParallelProcessing):
                if (value is bool enableParallel)
                    return new ScriptValidationResult(true);
                break;
            case nameof(FlexibleImageBlendScript.ReverseOrder):
                // 布尔值无需额外验证
                return new ScriptValidationResult(true);
        }
        return new ScriptValidationResult(true);
    }

    public override Dictionary<string, object> GetParameterData()
    {
        return _script.SerializeParameters();
    }

    public override Task SetParameterDataAsync(Dictionary<string, object> data)
    {
        _script.DeserializeParameters(data);
        return Task.CompletedTask;
    }

    public override Task ResetToDefaultAsync()
    {
        _script.MixMode = FlexibleImageBlendScript.BlendMode.Normal;
        _script.OutputIntensity = 1.0f;
        _script.GlobalOpacity = 1.0f;
        _script.RespectAlpha = true;
        _script.EnableParallelProcessing = true;
        _script.ReverseOrder = false;
        return Task.CompletedTask;
    }
}
