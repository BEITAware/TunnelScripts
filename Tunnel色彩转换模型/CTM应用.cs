using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using OpenCvSharp;
using Microsoft.ML;
using Tunnel_Next.Services.Scripting;

[RevivalScript(
    Name = "CTM应用",
    Author = "BEITAware",
    Description = "使用色彩转换模型对图像进行颜色转换",
    Version = "1.0",
    Category = "色彩转换",
    Color = "#FF6B6B"
)]
public class CTMApplicationScript : RevivalScriptBase
{
    public string NodeInstanceId { get; set; } = string.Empty;

        public override Dictionary<string, PortDefinition> GetInputPorts()
        {
            return new Dictionary<string, PortDefinition>
            {
                ["f32bmp"] = new PortDefinition("f32bmp", false, "输入图像"),
                ["ColorTransferModel"] = new PortDefinition("ColorTransferModel", false, "色彩转换模型")
            };
        }

        public override Dictionary<string, PortDefinition> GetOutputPorts()
        {
            return new Dictionary<string, PortDefinition>
            {
                ["f32bmp"] = new PortDefinition("f32bmp", false, "转换后的图像")
            };
        }

        public override Dictionary<string, object> Process(Dictionary<string, object> inputs, IScriptContext context)
        {
            // 查找Mat输入（图像）
            Mat inputMat = null;
            foreach (var input in inputs.Values)
            {
                if (input is Mat mat && !mat.Empty())
                {
                    inputMat = mat;
                    break;
                }
            }

            if (inputMat == null)
            {
                return new Dictionary<string, object> { ["f32bmp"] = null };
            }

            // 查找模型输入 - 优先按键名查找
            object modelObj = null;

            // 方法1: 直接按键名获取
            if (inputs.TryGetValue("ColorTransferModel", out modelObj) && modelObj != null)
            {
                // 找到模型
            }
            // 方法2: 查找非Mat对象
            else
            {
                foreach (var input in inputs)
                {
                    if (input.Value != null && !(input.Value is Mat))
                    {
                        modelObj = input.Value;
                        break;
                    }
                }
            }

            if (modelObj == null)
            {
                return new Dictionary<string, object> { ["f32bmp"] = null };
            }

            try
            {
                // 转换为RGBA格式
                Mat workingMat = EnsureRGBAFormat(inputMat);

                // 应用颜色转换
                Mat resultMat = ApplyColorTransfer(workingMat, modelObj);

                // 清理工作图像
                if (workingMat != inputMat)
                {
                    workingMat.Dispose();
                }

                return new Dictionary<string, object> { ["f32bmp"] = resultMat };
            }
            catch (Exception ex)
            {
                return new Dictionary<string, object> { ["f32bmp"] = null };
            }
        }

        private Mat EnsureRGBAFormat(Mat inputMat)
        {
            // 如果已经是32位浮点RGBA，直接克隆
            if (inputMat.Type() == MatType.CV_32FC4)
            {
                return inputMat.Clone();
            }

            // 转换为32位浮点RGBA
            Mat floatMat = new Mat();
            inputMat.ConvertTo(floatMat, MatType.CV_32F, 1.0 / 255.0);

            if (floatMat.Channels() == 4)
            {
                return floatMat;
            }
            else if (floatMat.Channels() == 3)
            {
                Mat rgbaMat = new Mat();
                Cv2.CvtColor(floatMat, rgbaMat, ColorConversionCodes.RGB2RGBA);
                floatMat.Dispose();
                return rgbaMat;
            }
            else
            {
                floatMat.Dispose();
                throw new NotSupportedException($"不支持 {inputMat.Channels()} 通道的图像");
            }
        }

        private Mat ApplyColorTransfer(Mat inputMat, object modelObj)
        {
            Mat resultMat = new Mat(inputMat.Size(), inputMat.Type());

            try
            {
                // 使用反射访问模型属性
                var modelType = modelObj.GetType();
                var mlContextProp = modelType.GetProperty("MLContext");
                var rModelProp = modelType.GetProperty("RModel");
                var gModelProp = modelType.GetProperty("GModel");
                var bModelProp = modelType.GetProperty("BModel");

                if (mlContextProp == null || rModelProp == null || gModelProp == null || bModelProp == null)
                {
                    return inputMat.Clone();
                }

                var mlContext = mlContextProp.GetValue(modelObj) as MLContext;
                var rModel = rModelProp.GetValue(modelObj) as Microsoft.ML.ITransformer;
                var gModel = gModelProp.GetValue(modelObj) as Microsoft.ML.ITransformer;
                var bModel = bModelProp.GetValue(modelObj) as Microsoft.ML.ITransformer;

                if (mlContext == null || rModel == null || gModel == null || bModel == null)
                {
                    return inputMat.Clone();
                }

                // 创建预测引擎
                var rEngine = mlContext.Model.CreatePredictionEngine<ColorTransferData, ColorPrediction>(rModel);
                var gEngine = mlContext.Model.CreatePredictionEngine<ColorTransferData, ColorPrediction>(gModel);
                var bEngine = mlContext.Model.CreatePredictionEngine<ColorTransferData, ColorPrediction>(bModel);

            // 逐像素处理
            for (int y = 0; y < inputMat.Rows; y++)
            {
                for (int x = 0; x < inputMat.Cols; x++)
                {
                    var pixel = inputMat.Get<Vec4f>(y, x);

                    // 预测新的RGB值
                    var inputData = new ColorTransferData
                    {
                        SourceR = pixel.Item0,
                        SourceG = pixel.Item1,
                        SourceB = pixel.Item2
                    };

                    var newR = rEngine.Predict(inputData).Score;
                    var newG = gEngine.Predict(inputData).Score;
                    var newB = bEngine.Predict(inputData).Score;

                    // 设置新像素值，保持Alpha通道
                    var newPixel = new Vec4f(newR, newG, newB, pixel.Item3);
                    resultMat.Set<Vec4f>(y, x, newPixel);
                }

            }

                return resultMat;
            }
            catch (Exception ex)
            {
                resultMat?.Dispose();
                return inputMat.Clone();
            }
        }

        public override FrameworkElement CreateParameterControl()
        {
            return new TextBlock { Text = "无参数" };
        }

        public override IScriptViewModel CreateViewModel()
        {
            return new CTMApplicationViewModel(this);
        }

        public override Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
        {
            return Task.CompletedTask;
        }

        public override Dictionary<string, object> SerializeParameters()
        {
            return new Dictionary<string, object>();
        }

        public override void DeserializeParameters(Dictionary<string, object> parameters)
        {
        }
    }

public class CTMApplicationViewModel : ScriptViewModelBase
{
    public CTMApplicationViewModel(CTMApplicationScript script) : base(script)
    {
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

// 数据类定义（与CTM生成脚本共享）
public class ColorTransferData
{
    [Microsoft.ML.Data.LoadColumn(0)]
    public float SourceR { get; set; }

    [Microsoft.ML.Data.LoadColumn(1)]
    public float SourceG { get; set; }

    [Microsoft.ML.Data.LoadColumn(2)]
    public float SourceB { get; set; }

    [Microsoft.ML.Data.LoadColumn(3)]
    public float TargetR { get; set; }

    [Microsoft.ML.Data.LoadColumn(4)]
    public float TargetG { get; set; }

    [Microsoft.ML.Data.LoadColumn(5)]
    public float TargetB { get; set; }
}

public class ColorPrediction
{
    [Microsoft.ML.Data.ColumnName("Score")]
    public float Score { get; set; }
}

public class CTMModel
{
    public MLContext MLContext { get; set; }
    public Microsoft.ML.ITransformer RModel { get; set; }  // R通道模型
    public Microsoft.ML.ITransformer GModel { get; set; }  // G通道模型
    public Microsoft.ML.ITransformer BModel { get; set; }  // B通道模型
    public int Degree { get; set; }
    public OpenCvSharp.Vec3f[] SourceColors { get; set; }  // 源颜色样本
    public OpenCvSharp.Vec3f[] TargetColors { get; set; }  // 目标颜色样本
}