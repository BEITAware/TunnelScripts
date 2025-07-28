using System;
using System.Collections.Generic;
using Tunnel_Next.Services.Scripting;
using OpenCvSharp;
using System.Threading.Tasks;
using System.Windows;

/**
 * Alpha应用节点脚本
 */
[TunnelExtensionScript(
    Name = "Alpha应用",
    Author = "BEITAware",
    Description = "将第二输入作为Alpha通道应用到第一输入上，如果第二输入是多通道图像，则取RGB平均值或直接取R通道",
    Version = "1.0",
    Category = "通道",
    Color = "#2ECC71"
)]
public class AlphaApplyScript : TunnelExtensionScriptBase
{
    public string NodeInstanceId { get; set; } = string.Empty;

    /**
     * 定义输入端口
     */
    public override Dictionary<string, PortDefinition> GetInputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            ["f32bmp"] = new PortDefinition("f32bmp", false, "输入图像"),
            ["alpha"] = new PortDefinition("alpha", false, "Alpha通道图像")
        };
    }

    /**
     * 定义输出端口
     */
    public override Dictionary<string, PortDefinition> GetOutputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            ["f32bmp"] = new PortDefinition("f32bmp", false, "输出应用新Alpha的图像")
        };
    }

    /**
     * 执行节点处理
     * @param inputs 输入数据字典，包含"f32bmp"和"alpha"的Mat图像
     * @param context 脚本上下文
     * @returns 输出数据字典，key "f32bmp"对应处理结果Mat
     */
    public override Dictionary<string, object> Process(Dictionary<string, object> inputs, IScriptContext context)
    {
        if (!inputs.TryGetValue("f32bmp", out var baseObj) || baseObj is not Mat baseMat || baseMat.Empty() ||
            !inputs.TryGetValue("alpha", out var alphaObj) || alphaObj is not Mat alphaMat || alphaMat.Empty())
        {
            return new Dictionary<string, object> { ["f32bmp"] = null };
        }

        try
        {
            // 尺寸一致性检查
            if (baseMat.Width != alphaMat.Width || baseMat.Height != alphaMat.Height)
            {
                throw new ArgumentException("输入图像尺寸不一致");
            }

            // 生成单通道AlphaMat
            Mat alphaSingle;
            if (alphaMat.Channels() == 1)
            {
                alphaSingle = alphaMat.Clone();
            }
            else
            {
                // 提取RGB平均或R通道
                Mat[] channels = new Mat[alphaMat.Channels()];
                Cv2.Split(alphaMat, out channels);

                // 计算RGB平均
                Mat sum = new Mat();
                Cv2.Add(channels[0], channels[1], sum);
                Cv2.Add(sum, channels[2], sum);
                Cv2.Multiply(sum, new Scalar(1.0/3.0), sum);
                alphaSingle = sum;

                // 释放多余通道
                foreach (var c in channels) c.Dispose();
            }

            // 确保基础图像为RGBA
            Mat baseRgba = (baseMat.Channels() == 4 && baseMat.Type() == MatType.CV_32FC4)
                ? baseMat.Clone()
                : ConvertToRGBA(baseMat);

            // 分离并替换Alpha通道
            Mat[] bgra = new Mat[4];
            Cv2.Split(baseRgba, out bgra);
            bgra[3].Dispose(); // 释放旧Alpha
            bgra[3] = alphaSingle;
            Mat result = new Mat(baseRgba.Size(), MatType.CV_32FC4);
            Cv2.Merge(bgra, result);

            // 释放临时资源
            baseRgba.Dispose();
            foreach (var c in bgra) c.Dispose();

            return new Dictionary<string, object> { ["f32bmp"] = result };
        }
        catch (Exception ex)
        {
            throw new ApplicationException($"Alpha应用节点处理失败: {ex.Message}", ex);
        }
    }

    /**
     * 将单通道或三通道图像转换为RGBA格式
     * @param mat 输入Mat
     * @returns RGBA格式Mat
     */
    private Mat ConvertToRGBA(Mat mat)
    {
        Mat rgba = new Mat();
        if (mat.Channels() == 3)
        {
            Cv2.CvtColor(mat, rgba, ColorConversionCodes.BGR2RGBA);
        }
        else
        {
            // 其他情况：先转换为灰度，再到RGBA
            Mat gray = new Mat();
            Cv2.CvtColor(mat, gray, ColorConversionCodes.BGR2GRAY);
            Cv2.CvtColor(gray, rgba, ColorConversionCodes.GRAY2RGBA);
            gray.Dispose();
        }
        // 确保是32位浮点且4通道
        if (rgba.Type() != MatType.CV_32FC4)
        {
            Mat temp = new Mat();
            rgba.ConvertTo(temp, MatType.CV_32FC4);
            rgba.Dispose();
            rgba = temp;
        }
        return rgba;
    }

    /**
     * 创建参数控件
     * @returns 参数控件或 null
     */
    public override FrameworkElement CreateParameterControl()
    {
        return null;
    }

    /**
     * 创建脚本视图模型
     * @returns IScriptViewModel
     */
    public override IScriptViewModel CreateViewModel()
    {
        return new AlphaApplyViewModel(this);
    }

    /**
     * 当参数更改时异步调用
     */
    public override Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
    {
        return Task.CompletedTask;
    }

    public override Dictionary<string, object> SerializeParameters()
    {
        return new Dictionary<string, object>
        {
            ["NodeInstanceId"] = NodeInstanceId
        };
    }

    public override void DeserializeParameters(Dictionary<string, object> data)
    {
        if (data.TryGetValue("NodeInstanceId", out var nodeId))
            NodeInstanceId = nodeId?.ToString() ?? string.Empty;
    }

    /**
     * AlphaApplyScript 对应的简易 ViewModel
     */
    private class AlphaApplyViewModel : ScriptViewModelBase
    {
        public AlphaApplyViewModel(AlphaApplyScript script) : base(script) { }

        public override async Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
        {
            await Task.CompletedTask; // 当前脚本无可变参数
        }

        public override ScriptValidationResult ValidateParameter(string parameterName, object value)
        {
            return new ScriptValidationResult(true);
        }

        public override Dictionary<string, object> GetParameterData()
        {
            return new Dictionary<string, object>();
        }

        public override async Task SetParameterDataAsync(Dictionary<string, object> data)
        {
            await Task.CompletedTask;
        }

        public override async Task ResetToDefaultAsync()
        {
            await Task.CompletedTask;
        }
    }
}