using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using Tunnel_Next.Services.Scripting;
using OpenCvSharp;

/**
 * 对两幅图像按像素求均值的节点脚本
 * 尺寸不同时左上角对齐，输出与第一幅图像同维度结果
 */
[TunnelExtensionScript(
    Name = "平均",
    Author = "BEITAware",
    Description = "对输入的两个图像按像素求均值，尺寸不同时左上角对齐",
    Version = "1.0",
    Category = "数学",
    Color = "#3498DB"
)]
public class AverageScript : TunnelExtensionScriptBase
{
    public string NodeInstanceId { get; set; } = string.Empty;

    /**
     * 定义输入端口
     */
    public override Dictionary<string, PortDefinition> GetInputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            ["f32bmp1"] = new PortDefinition("F32bmp1", false, "输入图像1"),
            ["f32bmp2"] = new PortDefinition("F32bmp2", false, "输入图像2")
        };
    }

    /**
     * 定义输出端口
     */
    public override Dictionary<string, PortDefinition> GetOutputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            ["f32bmp"] = new PortDefinition("F32bmp", false, "输出均值图像")
        };
    }

    /**
     * 执行节点处理
     * @param inputs 包含"f32bmp1"和"f32bmp2"的Mat图像
     * @param context 脚本上下文
     * @returns 输出字典，key "f32bmp"对应结果Mat
     */
    public override Dictionary<string, object> Process(Dictionary<string, object> inputs, IScriptContext context)
    {
        if (!inputs.TryGetValue("f32bmp1", out var mat1Obj) || mat1Obj is not Mat mat1 || mat1.Empty() ||
            !inputs.TryGetValue("f32bmp2", out var mat2Obj) || mat2Obj is not Mat mat2 || mat2.Empty())
        {
            return new Dictionary<string, object> { ["f32bmp"] = null };
        }

        try
        {
            // 输出图像初始为第一幅图像的克隆
            Mat output = mat1.Clone();

            int width = Math.Min(mat1.Width, mat2.Width);
            int height = Math.Min(mat1.Height, mat2.Height);

            // 定义ROI区域
            var roi1 = new Mat(mat1, new OpenCvSharp.Rect(0, 0, width, height));
            var roi2 = new Mat(mat2, new OpenCvSharp.Rect(0, 0, width, height));
            var dstRoi = new Mat(output, new OpenCvSharp.Rect(0, 0, width, height));

            // 使用OpenCV的AddWeighted函数，内部利用多线程和SIMD优化
            Cv2.AddWeighted(roi1, 0.5, roi2, 0.5, 0, dstRoi);

            // 释放临时资源
            roi1.Dispose();
            roi2.Dispose();
            dstRoi.Dispose();

            return new Dictionary<string, object> { ["f32bmp"] = output };
        }
        catch (Exception ex)
        {
            throw new ApplicationException($"平均节点处理失败: {ex.Message}", ex);
        }
    }

    /**
     * 创建参数控件（无参数）
     */
    public override FrameworkElement CreateParameterControl() => null;

    /**
     * 创建脚本视图模型（无视图模型）
     */
    public override IScriptViewModel CreateViewModel() => null;

    /**
     * 参数变更时异步调用（无参数）
     */
    public override Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue) => Task.CompletedTask;

    /**
     * 序列化参数到字典（无参数）
     */
    public override Dictionary<string, object> SerializeParameters() => new Dictionary<string, object>();

    /**
     * 从字典反序列化参数（无参数）
     */
    public override void DeserializeParameters(Dictionary<string, object> data) { }
} 