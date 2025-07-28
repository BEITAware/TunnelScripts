using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using OpenCvSharp;
using Tunnel_Next.Services.Scripting;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Scripts.示波与监看
{
    [TunnelExtensionScript(
        Name = "波形图",
        Description = "生成输入图像的波形图，显示每列像素的亮度分布",
        Category = "示波与监看",
        Color = "#32CD32"
    )]
    public class WaveformScript : TunnelExtensionScriptBase
    {
        private double _gain = 1.0;
        private int _luminanceBins = 256;
        private bool _enableRGBChannels = true; // 默认启用RGB通道显示
        private double _gammaCorrection = 1.0;

        [ScriptParameter(DisplayName = "波形增益", Order = 1)]
        public double Gain
        {
            get => _gain;
            set
            {
                var newValue = Math.Max(0.1, Math.Min(10.0, value));
                if (SetProperty(ref _gain, newValue))
                {
                    OnParameterChanged(nameof(Gain), newValue);
                }
            }
        }

        [ScriptParameter(DisplayName = "亮度分级数", Order = 2)]
        public int LuminanceBins
        {
            get => _luminanceBins;
            set
            {
                var newValue = Math.Max(64, Math.Min(1024, value));
                if (SetProperty(ref _luminanceBins, newValue))
                {
                    OnParameterChanged(nameof(LuminanceBins), newValue);
                }
            }
        }

        [ScriptParameter(DisplayName = "显示RGB通道", Order = 3)]
        public bool EnableRGBChannels
        {
            get => _enableRGBChannels;
            set
            {
                if (SetProperty(ref _enableRGBChannels, value))
                {
                    OnParameterChanged(nameof(EnableRGBChannels), value);
                }
            }
        }

        [ScriptParameter(DisplayName = "Gamma校正", Order = 4)]
        public double GammaCorrection
        {
            get => _gammaCorrection;
            set
            {
                var newValue = Math.Max(0.1, Math.Min(3.0, value));
                if (SetProperty(ref _gammaCorrection, newValue))
                {
                    OnParameterChanged(nameof(GammaCorrection), newValue);
                }
            }
        }

        public override Dictionary<string, PortDefinition> GetInputPorts()
        {
            return new Dictionary<string, PortDefinition>
            {
                ["f32bmp"] = new PortDefinition("f32bmp", false, "输入F32BMP图像")
            };
        }

        public override Dictionary<string, PortDefinition> GetOutputPorts()
        {
            return new Dictionary<string, PortDefinition>
            {
                ["F32Page"] = new PortDefinition("F32Page", false, "波形图页面")
            };
        }

        public override Dictionary<string, object> Process(Dictionary<string, object> inputs, IScriptContext context)
        {
            if (!inputs.TryGetValue("f32bmp", out var inputObj) || inputObj is not Mat inputMat || inputMat.Empty())
            {
                return new Dictionary<string, object> { ["F32Page"] = new Mat() };
            }

            try
            {
                var result = GenerateWaveformImage(inputMat);
                return new Dictionary<string, object> { ["F32Page"] = result };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"波形图生成错误: {ex.Message}");
                return new Dictionary<string, object> { ["F32Page"] = new Mat() };
            }
        }

        private Mat GenerateWaveformImage(Mat inputMat)
        {
            const int outputWidth = 800;
            const int outputHeight = 600;
            const int waveformWidth = 600;
            const int waveformHeight = 400;
            const int waveformX = 100;
            const int waveformY = 50;

            // 创建输出图像 (BGRA格式)
            var outputMat = new Mat(outputHeight, outputWidth, MatType.CV_32FC4, new Scalar(0.1, 0.1, 0.1, 1.0));

            // 确保输入是4通道BGRA格式
            Mat processedInput = EnsureBGRAFormat(inputMat);

            // 计算波形数据
            var waveformData = CalculateWaveformData(processedInput);

            // 绘制波形图框架
            DrawWaveformFrame(outputMat, waveformX, waveformY, waveformWidth, waveformHeight);

            // 绘制波形图
            DrawWaveform(outputMat, waveformData, waveformX, waveformY, waveformWidth, waveformHeight);

            // 绘制统计信息
            DrawWaveformStatistics(outputMat, waveformData, waveformY + waveformHeight + 30);

            if (processedInput != inputMat)
                processedInput.Dispose();

            return outputMat;
        }

        private Mat EnsureBGRAFormat(Mat inputMat)
        {
            if (inputMat.Channels() == 4)
            {
                return inputMat.Clone();
            }
            else if (inputMat.Channels() == 3)
            {
                var bgraMat = new Mat();
                Cv2.CvtColor(inputMat, bgraMat, ColorConversionCodes.BGR2BGRA);
                return bgraMat;
            }
            else if (inputMat.Channels() == 1)
            {
                var bgraMat = new Mat();
                Cv2.CvtColor(inputMat, bgraMat, ColorConversionCodes.GRAY2BGRA);
                return bgraMat;
            }
            else
            {
                throw new NotSupportedException($"不支持 {inputMat.Channels()} 通道的图像");
            }
        }

        private class WaveformData
        {
            public float[,] LuminanceWaveform { get; set; } // [column, luminance_bin]
            public float[,]? RedWaveform { get; set; }
            public float[,]? GreenWaveform { get; set; }
            public float[,]? BlueWaveform { get; set; }
            public int ImageWidth { get; set; }
            public int ImageHeight { get; set; }
            public float MaxValue { get; set; }
            public double AverageLuminance { get; set; }
            public double LuminanceRange { get; set; }
        }

        private WaveformData CalculateWaveformData(Mat inputMat)
        {
            var width = inputMat.Width;
            var height = inputMat.Height;
            var bins = LuminanceBins;

            var waveformData = new WaveformData
            {
                LuminanceWaveform = new float[width, bins],
                ImageWidth = width,
                ImageHeight = height
            };

            if (EnableRGBChannels)
            {
                waveformData.RedWaveform = new float[width, bins];
                waveformData.GreenWaveform = new float[width, bins];
                waveformData.BlueWaveform = new float[width, bins];
            }

            var totalLuminance = 0.0;
            var minLuminance = 1.0;
            var maxLuminance = 0.0;

            // 并行处理每一列
            Parallel.For(0, width, x =>
            {
                var localLuminanceColumn = new float[bins];
                var localRedColumn = EnableRGBChannels ? new float[bins] : null;
                var localGreenColumn = EnableRGBChannels ? new float[bins] : null;
                var localBlueColumn = EnableRGBChannels ? new float[bins] : null;

                unsafe
                {
                    for (int y = 0; y < height; y++)
                    {
                        var ptr = (float*)inputMat.Ptr(y);
                        var pixelIndex = x * 4;

                        // 只处理RGB，忽略Alpha通道
                        var b = Math.Max(0, Math.Min(1.0f, ptr[pixelIndex + 0]));
                        var g = Math.Max(0, Math.Min(1.0f, ptr[pixelIndex + 1]));
                        var r = Math.Max(0, Math.Min(1.0f, ptr[pixelIndex + 2]));
                        // Alpha通道 ptr[pixelIndex + 3] 被忽略

                        // 计算亮度 (ITU-R BT.709)
                        var luminance = 0.2126f * r + 0.7152f * g + 0.0722f * b;

                        // 应用Gamma校正
                        if (GammaCorrection != 1.0)
                        {
                            luminance = (float)Math.Pow(luminance, 1.0 / GammaCorrection);
                            if (EnableRGBChannels)
                            {
                                r = (float)Math.Pow(r, 1.0 / GammaCorrection);
                                g = (float)Math.Pow(g, 1.0 / GammaCorrection);
                                b = (float)Math.Pow(b, 1.0 / GammaCorrection);
                            }
                        }

                        // 量化到bins
                        var lumBin = Math.Min((int)(luminance * (bins - 1)), bins - 1);
                        localLuminanceColumn[lumBin] += 1.0f;

                        if (EnableRGBChannels)
                        {
                            var rBin = Math.Min((int)(r * (bins - 1)), bins - 1);
                            var gBin = Math.Min((int)(g * (bins - 1)), bins - 1);
                            var bBin = Math.Min((int)(b * (bins - 1)), bins - 1);

                            localRedColumn![rBin] += 1.0f;
                            localGreenColumn![gBin] += 1.0f;
                            localBlueColumn![bBin] += 1.0f;
                        }

                        lock (waveformData)
                        {
                            totalLuminance += luminance;
                            minLuminance = Math.Min(minLuminance, luminance);
                            maxLuminance = Math.Max(maxLuminance, luminance);
                        }
                    }
                }

                // 复制到主数据结构
                for (int bin = 0; bin < bins; bin++)
                {
                    waveformData.LuminanceWaveform[x, bin] = localLuminanceColumn[bin];
                    if (EnableRGBChannels)
                    {
                        waveformData.RedWaveform![x, bin] = localRedColumn![bin];
                        waveformData.GreenWaveform![x, bin] = localGreenColumn![bin];
                        waveformData.BlueWaveform![x, bin] = localBlueColumn![bin];
                    }
                }
            });

            // 计算统计信息
            waveformData.AverageLuminance = totalLuminance / (width * height);
            waveformData.LuminanceRange = maxLuminance - minLuminance;

            // 找到最大值用于归一化
            var maxValue = 0.0f;
            for (int x = 0; x < width; x++)
            {
                for (int bin = 0; bin < bins; bin++)
                {
                    maxValue = Math.Max(maxValue, waveformData.LuminanceWaveform[x, bin]);
                    if (EnableRGBChannels)
                    {
                        maxValue = Math.Max(maxValue, waveformData.RedWaveform![x, bin]);
                        maxValue = Math.Max(maxValue, waveformData.GreenWaveform![x, bin]);
                        maxValue = Math.Max(maxValue, waveformData.BlueWaveform![x, bin]);
                    }
                }
            }
            waveformData.MaxValue = maxValue;

            return waveformData;
        }

        private void DrawWaveformFrame(Mat outputMat, int x, int y, int width, int height)
        {
            // 绘制外框
            var frameColor = new Scalar(0.8, 0.8, 0.8, 1.0);
            Cv2.Rectangle(outputMat, new OpenCvSharp.Point(x - 2, y - 2), new OpenCvSharp.Point(x + width + 2, y + height + 2), frameColor, 2);

            // 绘制背景
            var bgColor = new Scalar(0.05, 0.05, 0.05, 1.0);
            Cv2.Rectangle(outputMat, new OpenCvSharp.Point(x, y), new OpenCvSharp.Point(x + width, y + height), bgColor, -1);

            // 绘制网格线
            var gridColor = new Scalar(0.3, 0.3, 0.3, 1.0);

            // 水平网格线 (亮度级别)
            for (int i = 1; i < 4; i++)
            {
                var gridY = y + (i * height) / 4;
                Cv2.Line(outputMat, new OpenCvSharp.Point(x, gridY), new OpenCvSharp.Point(x + width, gridY), gridColor, 1);
            }

            // 垂直网格线 (图像列)
            for (int i = 1; i < 8; i++)
            {
                var gridX = x + (i * width) / 8;
                Cv2.Line(outputMat, new OpenCvSharp.Point(gridX, y), new OpenCvSharp.Point(gridX, y + height), gridColor, 1);
            }

            // 绘制刻度标签
            var textColor = new Scalar(0.7, 0.7, 0.7, 1.0);
            var font = HersheyFonts.HersheySimplex;
            var fontSize = 0.4;
            var thickness = 1;

            // Y轴标签 (亮度)
            for (int i = 0; i <= 4; i++)
            {
                var labelY = y + height - (i * height) / 4;
                var luminanceValue = (i * 100) / 4; // 0-100%
                Cv2.PutText(outputMat, $"{luminanceValue}%", new OpenCvSharp.Point(x - 40, labelY + 5), font, fontSize, textColor, thickness);
            }
        }

        private void DrawWaveform(Mat outputMat, WaveformData waveformData, int x, int y, int width, int height)
        {
            if (waveformData.MaxValue == 0) return;

            var imageWidth = waveformData.ImageWidth;
            var bins = LuminanceBins;

            // 应用增益
            var effectiveMaxValue = waveformData.MaxValue / (float)Gain;

            if (EnableRGBChannels)
            {
                // 使用类似DaVinci Resolve的RGB叠加方式
                DrawRGBWaveformResolveStyle(outputMat, waveformData, x, y, width, height, imageWidth, bins, effectiveMaxValue);
            }
            else
            {
                // 绘制亮度波形
                DrawChannelWaveform(outputMat, waveformData.LuminanceWaveform, x, y, width, height, imageWidth, bins, effectiveMaxValue, new Scalar(0.8, 0.8, 0.8, 0.8)); // 白色
            }
        }

        private void DrawRGBWaveformResolveStyle(Mat outputMat, WaveformData waveformData, int x, int y, int width, int height, int imageWidth, int bins, float maxValue)
        {
            // 简洁的RGB波形显示，无任何限制和过滤

            for (int col = 0; col < imageWidth; col++)
            {
                var screenX = x + (col * width) / imageWidth;

                for (int bin = 0; bin < bins; bin++)
                {
                    var rValue = waveformData.RedWaveform![col, bin];
                    var gValue = waveformData.GreenWaveform![col, bin];
                    var bValue = waveformData.BlueWaveform![col, bin];

                    // 只要有任何通道有值就绘制，无最小值限制
                    if (rValue > 0 || gValue > 0 || bValue > 0)
                    {
                        // 计算Y位置
                        var screenY = y + height - (bin * height) / bins;

                        // 直接使用原始强度，无归一化限制
                        var rIntensity = Math.Min(1.0f, rValue / maxValue);
                        var gIntensity = Math.Min(1.0f, gValue / maxValue);
                        var bIntensity = Math.Min(1.0f, bValue / maxValue);

                        // 直接RGB混合，无额外处理
                        var pixelColor = new Scalar(
                            bIntensity * 255,  // OpenCV使用BGR顺序
                            gIntensity * 255,
                            rIntensity * 255,
                            255
                        );

                        // 直接绘制像素点，无线段处理
                        outputMat.Set(screenY, screenX, pixelColor);
                    }
                }
            }
        }

        private void DrawChannelWaveform(Mat outputMat, float[,] waveformData, int x, int y, int width, int height, int imageWidth, int bins, float maxValue, Scalar color)
        {
            for (int col = 0; col < imageWidth; col++)
            {
                var screenX = x + (col * width) / imageWidth;

                for (int bin = 0; bin < bins; bin++)
                {
                    var pixelCount = waveformData[col, bin];
                    if (pixelCount <= 0) continue;

                    // 计算亮度强度 (基于像素数量)
                    var intensity = Math.Min(1.0f, pixelCount / maxValue);

                    // 计算Y位置 (bin对应亮度级别)
                    var screenY = y + height - (bin * height) / bins;

                    // 绘制像素点，亮度反映像素数量
                    var pixelColor = new Scalar(
                        color.Val0 * intensity,
                        color.Val1 * intensity,
                        color.Val2 * intensity,
                        color.Val3
                    );

                    // 直接绘制像素点，无最小值限制
                    outputMat.Set(screenY, screenX, pixelColor);
                }
            }
        }

        private void DrawWaveformStatistics(Mat outputMat, WaveformData waveformData, int startY)
        {
            var textColor = new Scalar(1.0, 1.0, 1.0, 1.0);
            var highlightColor = new Scalar(0.3, 1.0, 1.0, 1.0);
            var font = HersheyFonts.HersheySimplex;
            var fontSize = 0.6;
            var thickness = 1;
            var lineHeight = 25;

            var texts = new[]
            {
                $"Image Size: {waveformData.ImageWidth} x {waveformData.ImageHeight}",
                $"Average Luminance: {waveformData.AverageLuminance:P1}",
                $"Luminance Range: {waveformData.LuminanceRange:P1}",
                $"Waveform Gain: {Gain:F1}x",
                $"Luminance Bins: {LuminanceBins}",
                $"RGB Channels: {(EnableRGBChannels ? "Enabled" : "Disabled")}"
            };

            for (int i = 0; i < texts.Length; i++)
            {
                var textY = startY + i * lineHeight;
                var color = (i == 3) ? highlightColor : textColor; // 高亮增益信息
                Cv2.PutText(outputMat, texts[i], new OpenCvSharp.Point(50, textY), font, fontSize, color, thickness);
            }
        }
        public override Dictionary<string, object> SerializeParameters()
        {
            return new Dictionary<string, object>
            {
                [nameof(Gain)] = Gain,
                [nameof(LuminanceBins)] = LuminanceBins,
                [nameof(EnableRGBChannels)] = EnableRGBChannels,
                [nameof(GammaCorrection)] = GammaCorrection
            };
        }

        public override void DeserializeParameters(Dictionary<string, object> data)
        {
            if (data.TryGetValue(nameof(Gain), out var gain))
                Gain = Convert.ToDouble(gain);

            if (data.TryGetValue(nameof(LuminanceBins), out var bins))
                LuminanceBins = Convert.ToInt32(bins);

            if (data.TryGetValue(nameof(EnableRGBChannels), out var enableRGB))
                EnableRGBChannels = Convert.ToBoolean(enableRGB);

            if (data.TryGetValue(nameof(GammaCorrection), out var gamma))
                GammaCorrection = Convert.ToDouble(gamma);
        }

        public override ScriptViewModelBase CreateViewModel()
        {
            return new WaveformViewModel(this);
        }

        public override async Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
        {
            // 参数变化时的内部处理逻辑
            System.Diagnostics.Debug.WriteLine($"波形图参数变化: {parameterName} = {newValue}");

            // 这里可以添加参数变化时的特殊处理逻辑
            // 注意：不要在这里调用OnParameterChanged，会产生递归！
            // OnParameterChanged已经在属性setter中调用，并且会自动调用这个方法

            await Task.CompletedTask;
        }

        public override FrameworkElement CreateParameterControl()
        {
            var stackPanel = new StackPanel { Margin = new Thickness(5) };

            // 波形增益滑块
            var gainLabel = new TextBlock { Text = "波形增益", Margin = new Thickness(0, 5, 0, 2) };
            var gainSlider = new Slider
            {
                Minimum = 0.1,
                Maximum = 10.0,
                Value = Gain,
                TickFrequency = 0.1,
                IsSnapToTickEnabled = true
            };
            gainSlider.ValueChanged += (s, e) => Gain = e.NewValue;

            // 亮度分级数滑块
            var binsLabel = new TextBlock { Text = "亮度分级数", Margin = new Thickness(0, 10, 0, 2) };
            var binsSlider = new Slider
            {
                Minimum = 64,
                Maximum = 1024,
                Value = LuminanceBins,
                TickFrequency = 32,
                IsSnapToTickEnabled = true
            };
            binsSlider.ValueChanged += (s, e) => LuminanceBins = (int)e.NewValue;

            // RGB通道显示开关
            var rgbLabel = new TextBlock { Text = "显示RGB通道", Margin = new Thickness(0, 10, 0, 2) };
            var rgbCheckBox = new CheckBox
            {
                IsChecked = EnableRGBChannels,
                Content = "启用RGB通道分析"
            };
            rgbCheckBox.Checked += (s, e) => EnableRGBChannels = true;
            rgbCheckBox.Unchecked += (s, e) => EnableRGBChannels = false;

            // Gamma校正滑块
            var gammaLabel = new TextBlock { Text = "Gamma校正", Margin = new Thickness(0, 10, 0, 2) };
            var gammaSlider = new Slider
            {
                Minimum = 0.1,
                Maximum = 3.0,
                Value = GammaCorrection,
                TickFrequency = 0.1,
                IsSnapToTickEnabled = true
            };
            gammaSlider.ValueChanged += (s, e) => GammaCorrection = e.NewValue;

            stackPanel.Children.Add(gainLabel);
            stackPanel.Children.Add(gainSlider);
            stackPanel.Children.Add(binsLabel);
            stackPanel.Children.Add(binsSlider);
            stackPanel.Children.Add(rgbLabel);
            stackPanel.Children.Add(rgbCheckBox);
            stackPanel.Children.Add(gammaLabel);
            stackPanel.Children.Add(gammaSlider);

            return stackPanel;
        }

        public class WaveformViewModel : ScriptViewModelBase
        {
            private WaveformScript WaveformScript => (WaveformScript)Script;

            public WaveformViewModel(WaveformScript script) : base(script) { }

            public double Gain
            {
                get => WaveformScript.Gain;
                set => WaveformScript.Gain = value;
            }

            public int LuminanceBins
            {
                get => WaveformScript.LuminanceBins;
                set => WaveformScript.LuminanceBins = value;
            }

            public bool EnableRGBChannels
            {
                get => WaveformScript.EnableRGBChannels;
                set => WaveformScript.EnableRGBChannels = value;
            }

            public double GammaCorrection
            {
                get => WaveformScript.GammaCorrection;
                set => WaveformScript.GammaCorrection = value;
            }

            public override ScriptValidationResult ValidateParameter(string parameterName, object value)
            {
                switch (parameterName)
                {
                    case nameof(Gain):
                        if (value is double gain && (gain < 0.1 || gain > 10.0))
                            return new ScriptValidationResult(false, "增益值必须在0.1到10.0之间");
                        break;
                    case nameof(LuminanceBins):
                        if (value is int bins && (bins < 64 || bins > 1024))
                            return new ScriptValidationResult(false, "亮度分级数必须在64到1024之间");
                        break;
                    case nameof(GammaCorrection):
                        if (value is double gamma && (gamma < 0.1 || gamma > 3.0))
                            return new ScriptValidationResult(false, "Gamma校正值必须在0.1到3.0之间");
                        break;
                }
                return new ScriptValidationResult(true);
            }

            public override Dictionary<string, object> GetParameterData()
            {
                return new Dictionary<string, object>
                {
                    [nameof(Gain)] = Gain,
                    [nameof(LuminanceBins)] = LuminanceBins,
                    [nameof(EnableRGBChannels)] = EnableRGBChannels,
                    [nameof(GammaCorrection)] = GammaCorrection
                };
            }

            public override async Task SetParameterDataAsync(Dictionary<string, object> data)
            {
                if (data.TryGetValue(nameof(Gain), out var gain))
                    Gain = Convert.ToDouble(gain);

                if (data.TryGetValue(nameof(LuminanceBins), out var bins))
                    LuminanceBins = Convert.ToInt32(bins);

                if (data.TryGetValue(nameof(EnableRGBChannels), out var enableRGB))
                    EnableRGBChannels = Convert.ToBoolean(enableRGB);

                if (data.TryGetValue(nameof(GammaCorrection), out var gamma))
                    GammaCorrection = Convert.ToDouble(gamma);

                await Task.CompletedTask;
            }

            public override async Task ResetToDefaultAsync()
            {
                Gain = 1.0;
                LuminanceBins = 256;
                EnableRGBChannels = true; // 默认启用RGB通道显示
                GammaCorrection = 1.0;
                await Task.CompletedTask;
            }

            public override async Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
            {
                // ViewModel参数变化时触发脚本的参数变化处理
                await WaveformScript.OnParameterChangedAsync(parameterName, oldValue, newValue);
            }
        }
    }
}