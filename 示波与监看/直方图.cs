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
    [RevivalScript(
        Name = "直方图",
        Description = "生成输入图像的RGB直方图和统计信息，支持高位深度分析",
        Category = "示波与监看",
        Color = "#87CEEB"
    )]
    public class HistogramScript : RevivalScriptBase
    {
        private double _highlightClipTolerance = 0.01;
        private double _shadowClipTolerance = 0.01;
        private int _minPixelCount = 1;
        private int _analysisMode = 0; // 0=自动, 1=8位, 2=10位, 3=12位, 4=14位, 5=16位
        private bool _enableHighPrecision = true;

        [ScriptParameter(DisplayName = "亮部裁切容差", Order = 1)]
        public double HighlightClipTolerance
        {
            get => _highlightClipTolerance;
            set
            {
                var newValue = Math.Max(0.0, Math.Min(1.0, value));
                if (SetProperty(ref _highlightClipTolerance, newValue))
                {
                    OnParameterChanged(nameof(HighlightClipTolerance), newValue);
                }
            }
        }

        [ScriptParameter(DisplayName = "暗部裁切容差", Order = 2)]
        public double ShadowClipTolerance
        {
            get => _shadowClipTolerance;
            set
            {
                var newValue = Math.Max(0.0, Math.Min(1.0, value));
                if (SetProperty(ref _shadowClipTolerance, newValue))
                {
                    OnParameterChanged(nameof(ShadowClipTolerance), newValue);
                }
            }
        }

        [ScriptParameter(DisplayName = "最小像素数", Order = 3)]
        public int MinPixelCount
        {
            get => _minPixelCount;
            set
            {
                var newValue = Math.Max(1, value);
                if (SetProperty(ref _minPixelCount, newValue))
                {
                    OnParameterChanged(nameof(MinPixelCount), newValue);
                }
            }
        }

        [ScriptParameter(DisplayName = "分析模式", Order = 4)]
        public int AnalysisMode
        {
            get => _analysisMode;
            set
            {
                var newValue = Math.Max(0, Math.Min(5, value));
                if (SetProperty(ref _analysisMode, newValue))
                {
                    OnParameterChanged(nameof(AnalysisMode), newValue);
                }
            }
        }

        [ScriptParameter(DisplayName = "高精度分析", Order = 5)]
        public bool EnableHighPrecision
        {
            get => _enableHighPrecision;
            set
            {
                if (SetProperty(ref _enableHighPrecision, value))
                {
                    OnParameterChanged(nameof(EnableHighPrecision), value);
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
                ["F32Page"] = new PortDefinition("F32Page", false, "直方图统计页面")
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
                var result = GenerateHistogramImage(inputMat);
                return new Dictionary<string, object> { ["F32Page"] = result };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"直方图生成错误: {ex.Message}");
                return new Dictionary<string, object> { ["F32Page"] = new Mat() };
            }
        }

        private Mat GenerateHistogramImage(Mat inputMat)
        {
            const int outputWidth = 800;
            const int outputHeight = 600;
            const int histogramWidth = 600;
            const int histogramHeight = 300;
            const int histogramX = 100;
            const int histogramY = 50;

            // 动态计算bins数量和分析精度
            var analysisConfig = DetermineAnalysisConfiguration(inputMat);

            // 创建输出图像 (BGRA格式)
            var outputMat = new Mat(outputHeight, outputWidth, MatType.CV_32FC4, new Scalar(0.1, 0.1, 0.1, 1.0));

            // 确保输入是4通道BGRA格式
            Mat processedInput;
            if (inputMat.Channels() == 3)
            {
                processedInput = new Mat();
                Cv2.CvtColor(inputMat, processedInput, ColorConversionCodes.BGR2BGRA);
            }
            else if (inputMat.Channels() == 4)
            {
                processedInput = inputMat.Clone();
            }
            else
            {
                // 单通道转BGRA
                processedInput = new Mat();
                Cv2.CvtColor(inputMat, processedInput, ColorConversionCodes.GRAY2BGRA);
            }

            // 计算高精度直方图 (并行处理)
            var histogramData = CalculateHighPrecisionHistogram(processedInput, analysisConfig);
            var statistics = CalculateAdvancedStatistics(processedInput, histogramData, analysisConfig);

            // 绘制直方图框架
            DrawHistogramFrame(outputMat, histogramX, histogramY, histogramWidth, histogramHeight);

            // 绘制RGB直方图
            DrawHistograms(outputMat, histogramData, histogramX, histogramY, histogramWidth, histogramHeight);

            // 绘制统计信息
            DrawStatistics(outputMat, statistics, histogramY + histogramHeight + 30);

            if (processedInput != inputMat)
                processedInput.Dispose();

            return outputMat;
        }

        private class AnalysisConfiguration
        {
            public int Bins { get; set; }
            public int DisplayBins { get; set; } = 256; // 显示用的bins数量，固定256用于绘制
            public double BinScale { get; set; }
            public string ModeName { get; set; } = "";
            public int TargetBitDepth { get; set; }
        }

        private class HistogramData
        {
            public int[] RedHist { get; set; }
            public int[] GreenHist { get; set; }
            public int[] BlueHist { get; set; }
            public int[] DisplayRedHist { get; set; } = new int[256]; // 用于显示的256bins
            public int[] DisplayGreenHist { get; set; } = new int[256];
            public int[] DisplayBlueHist { get; set; } = new int[256];
            public int MaxValue { get; set; }
            public int DisplayMaxValue { get; set; }
            public int TotalBins { get; set; }
        }

        private class ImageStatistics
        {
            public double HighlightClipRatio { get; set; }
            public double ShadowClipRatio { get; set; }
            public double EffectiveBitDepth { get; set; }
            public double TrueEffectiveBitDepth { get; set; } // 基于实际数据分析的位深度
            public int UniqueValues { get; set; }
            public string AnalysisMode { get; set; } = "";
        }

        private AnalysisConfiguration DetermineAnalysisConfiguration(Mat inputMat)
        {
            var config = new AnalysisConfiguration();

            // 根据分析模式确定bins数量
            switch (AnalysisMode)
            {
                case 0: // 自动模式 - 基于数据分析
                    config = EnableHighPrecision ? AutoDetectBitDepth(inputMat) : new AnalysisConfiguration
                    {
                        Bins = 256,
                        BinScale = 255.0,
                        ModeName = "8-bit Compatible",
                        TargetBitDepth = 8
                    };
                    break;
                case 1: // 8位
                    config.Bins = 256;
                    config.BinScale = 255.0;
                    config.ModeName = "8-bit";
                    config.TargetBitDepth = 8;
                    break;
                case 2: // 10位
                    config.Bins = 1024;
                    config.BinScale = 1023.0;
                    config.ModeName = "10-bit";
                    config.TargetBitDepth = 10;
                    break;
                case 3: // 12位
                    config.Bins = 4096;
                    config.BinScale = 4095.0;
                    config.ModeName = "12-bit";
                    config.TargetBitDepth = 12;
                    break;
                case 4: // 14位
                    config.Bins = 16384;
                    config.BinScale = 16383.0;
                    config.ModeName = "14-bit";
                    config.TargetBitDepth = 14;
                    break;
                case 5: // 16位
                    config.Bins = 65536;
                    config.BinScale = 65535.0;
                    config.ModeName = "16-bit";
                    config.TargetBitDepth = 16;
                    break;
            }

            return config;
        }

        private AnalysisConfiguration AutoDetectBitDepth(Mat inputMat)
        {
            // 快速采样检测实际位深度，避免全图扫描影响性能
            const int sampleSize = 10000; // 采样像素数
            var width = inputMat.Width;
            var height = inputMat.Height;
            var totalPixels = width * height;
            var step = Math.Max(1, totalPixels / sampleSize);

            var uniqueValues = new HashSet<int>();
            var maxObservedValue = 0;

            unsafe
            {
                for (int y = 0; y < height; y += Math.Max(1, step / width))
                {
                    var ptr = (float*)inputMat.Ptr(y);
                    for (int x = 0; x < width; x += Math.Max(1, step % width + 1))
                    {
                        // 检查RGB三个通道
                        for (int c = 0; c < 3; c++)
                        {
                            var value = Math.Max(0, Math.Min(1.0f, ptr[x * 4 + c]));
                            var quantized = (int)(value * 65535); // 量化到16位进行检测
                            uniqueValues.Add(quantized);
                            maxObservedValue = Math.Max(maxObservedValue, quantized);
                        }

                        if (uniqueValues.Count > 20000) break; // 避免内存过度使用
                    }
                    if (uniqueValues.Count > 20000) break;
                }
            }

            // 基于唯一值数量推断位深度
            var uniqueCount = uniqueValues.Count;
            var config = new AnalysisConfiguration();

            if (uniqueCount <= 256)
            {
                config.Bins = 256;
                config.BinScale = 255.0;
                config.ModeName = "Detected 8-bit";
                config.TargetBitDepth = 8;
            }
            else if (uniqueCount <= 1024)
            {
                config.Bins = 1024;
                config.BinScale = 1023.0;
                config.ModeName = "Detected 10-bit";
                config.TargetBitDepth = 10;
            }
            else if (uniqueCount <= 4096)
            {
                config.Bins = 4096;
                config.BinScale = 4095.0;
                config.ModeName = "Detected 12-bit";
                config.TargetBitDepth = 12;
            }
            else if (uniqueCount <= 16384)
            {
                config.Bins = 16384;
                config.BinScale = 16383.0;
                config.ModeName = "Detected 14-bit";
                config.TargetBitDepth = 14;
            }
            else
            {
                config.Bins = 65536;
                config.BinScale = 65535.0;
                config.ModeName = "Detected 16-bit";
                config.TargetBitDepth = 16;
            }

            return config;
        }

        private HistogramData CalculateHighPrecisionHistogram(Mat inputMat, AnalysisConfiguration config)
        {
            var histData = new HistogramData
            {
                RedHist = new int[config.Bins],
                GreenHist = new int[config.Bins],
                BlueHist = new int[config.Bins],
                TotalBins = config.Bins
            };

            var width = inputMat.Width;
            var height = inputMat.Height;

            // 并行计算高精度直方图
            var lockObj = new object();

            Parallel.For(0, height, y =>
            {
                var localRedHist = new int[config.Bins];
                var localGreenHist = new int[config.Bins];
                var localBlueHist = new int[config.Bins];

                unsafe
                {
                    var ptr = (float*)inputMat.Ptr(y);
                    for (int x = 0; x < width; x++)
                    {
                        var b = Math.Max(0, Math.Min(1.0f, ptr[x * 4 + 0]));
                        var g = Math.Max(0, Math.Min(1.0f, ptr[x * 4 + 1]));
                        var r = Math.Max(0, Math.Min(1.0f, ptr[x * 4 + 2]));

                        // 使用配置的精度进行量化
                        var rBin = Math.Min((int)(r * config.BinScale), config.Bins - 1);
                        var gBin = Math.Min((int)(g * config.BinScale), config.Bins - 1);
                        var bBin = Math.Min((int)(b * config.BinScale), config.Bins - 1);

                        localRedHist[rBin]++;
                        localGreenHist[gBin]++;
                        localBlueHist[bBin]++;
                    }
                }

                lock (lockObj)
                {
                    for (int i = 0; i < config.Bins; i++)
                    {
                        histData.RedHist[i] += localRedHist[i];
                        histData.GreenHist[i] += localGreenHist[i];
                        histData.BlueHist[i] += localBlueHist[i];
                    }
                }
            });

            histData.MaxValue = Math.Max(histData.RedHist.Max(), Math.Max(histData.GreenHist.Max(), histData.BlueHist.Max()));

            // 生成用于显示的256bins直方图（性能优化）
            GenerateDisplayHistogram(histData, config);

            return histData;
        }

        private void GenerateDisplayHistogram(HistogramData histData, AnalysisConfiguration config)
        {
            // 将高精度直方图压缩到256bins用于显示
            if (config.Bins <= 256)
            {
                // 直接复制或填充
                for (int i = 0; i < 256; i++)
                {
                    var sourceIndex = (i * config.Bins) / 256;
                    histData.DisplayRedHist[i] = sourceIndex < config.Bins ? histData.RedHist[sourceIndex] : 0;
                    histData.DisplayGreenHist[i] = sourceIndex < config.Bins ? histData.GreenHist[sourceIndex] : 0;
                    histData.DisplayBlueHist[i] = sourceIndex < config.Bins ? histData.BlueHist[sourceIndex] : 0;
                }
            }
            else
            {
                // 合并多个bins到一个显示bin
                var binRatio = config.Bins / 256;
                for (int i = 0; i < 256; i++)
                {
                    var startBin = i * binRatio;
                    var endBin = Math.Min((i + 1) * binRatio, config.Bins);

                    for (int j = startBin; j < endBin; j++)
                    {
                        histData.DisplayRedHist[i] += histData.RedHist[j];
                        histData.DisplayGreenHist[i] += histData.GreenHist[j];
                        histData.DisplayBlueHist[i] += histData.BlueHist[j];
                    }
                }
            }

            histData.DisplayMaxValue = Math.Max(histData.DisplayRedHist.Max(),
                Math.Max(histData.DisplayGreenHist.Max(), histData.DisplayBlueHist.Max()));
        }

        private void DrawHistogramFrame(Mat outputMat, int x, int y, int width, int height)
        {
            // 绘制外框
            var frameColor = new Scalar(0.8, 0.8, 0.8, 1.0);
            Cv2.Rectangle(outputMat, new OpenCvSharp.Point(x - 2, y - 2), new OpenCvSharp.Point(x + width + 2, y + height + 2), frameColor, 2);

            // 绘制背景
            var bgColor = new Scalar(0.05, 0.05, 0.05, 1.0);
            Cv2.Rectangle(outputMat, new OpenCvSharp.Point(x, y), new OpenCvSharp.Point(x + width, y + height), bgColor, -1);

            // 绘制网格线
            var gridColor = new Scalar(0.3, 0.3, 0.3, 1.0);

            // 垂直网格线 (每64个bin一条线)
            for (int i = 64; i < 256; i += 64)
            {
                var gridX = x + (i * width) / 256;
                Cv2.Line(outputMat, new OpenCvSharp.Point(gridX, y), new OpenCvSharp.Point(gridX, y + height), gridColor, 1);
            }

            // 水平网格线
            for (int i = 1; i < 4; i++)
            {
                var gridY = y + (i * height) / 4;
                Cv2.Line(outputMat, new OpenCvSharp.Point(x, gridY), new OpenCvSharp.Point(x + width, gridY), gridColor, 1);
            }
        }

        private ImageStatistics CalculateAdvancedStatistics(Mat inputMat, HistogramData histData, AnalysisConfiguration config)
        {
            var stats = new ImageStatistics
            {
                AnalysisMode = config.ModeName
            };
            var totalPixels = inputMat.Width * inputMat.Height;

            // 计算亮部裁切比例（基于实际bins）
            var highlightThreshold = 1.0 - HighlightClipTolerance;
            var highlightBin = (int)(highlightThreshold * config.BinScale);
            var highlightPixels = 0;
            for (int i = highlightBin; i < config.Bins; i++)
            {
                highlightPixels += Math.Max(histData.RedHist[i], Math.Max(histData.GreenHist[i], histData.BlueHist[i]));
            }
            stats.HighlightClipRatio = (double)highlightPixels / (totalPixels * 3);

            // 计算暗部裁切比例（基于实际bins）
            var shadowBin = (int)(ShadowClipTolerance * config.BinScale);
            var shadowPixels = 0;
            for (int i = 0; i <= shadowBin; i++)
            {
                shadowPixels += Math.Max(histData.RedHist[i], Math.Max(histData.GreenHist[i], histData.BlueHist[i]));
            }
            stats.ShadowClipRatio = (double)shadowPixels / (totalPixels * 3);

            // 计算传统有效位深度（基于显示bins，向后兼容）
            var validDisplayBins = 0;
            for (int i = 0; i < 256; i++)
            {
                if (histData.DisplayRedHist[i] >= MinPixelCount ||
                    histData.DisplayGreenHist[i] >= MinPixelCount ||
                    histData.DisplayBlueHist[i] >= MinPixelCount)
                {
                    validDisplayBins++;
                }
            }
            stats.EffectiveBitDepth = validDisplayBins > 0 ? Math.Log2(validDisplayBins) : 0;

            // 计算真实有效位深度（基于实际高精度bins）
            var validTrueBins = 0;
            var uniqueValues = 0;
            for (int i = 0; i < config.Bins; i++)
            {
                if (histData.RedHist[i] >= MinPixelCount ||
                    histData.GreenHist[i] >= MinPixelCount ||
                    histData.BlueHist[i] >= MinPixelCount)
                {
                    validTrueBins++;
                }
                if (histData.RedHist[i] > 0 || histData.GreenHist[i] > 0 || histData.BlueHist[i] > 0)
                {
                    uniqueValues++;
                }
            }
            stats.TrueEffectiveBitDepth = validTrueBins > 0 ? Math.Log2(validTrueBins) : 0;
            stats.UniqueValues = uniqueValues;

            return stats;
        }

        private void DrawHistograms(Mat outputMat, HistogramData histData, int x, int y, int width, int height)
        {
            if (histData.DisplayMaxValue == 0) return;

            var redColor = new Scalar(0.2, 0.2, 1.0, 0.7);    // 红色 (BGR格式)
            var greenColor = new Scalar(0.2, 1.0, 0.2, 0.7);  // 绿色
            var blueColor = new Scalar(1.0, 0.2, 0.2, 0.7);   // 蓝色

            // 绘制直方图柱状图（使用显示用的256bins）
            for (int i = 0; i < 256; i++)
            {
                var binX = x + (i * width) / 256;
                var binWidth = Math.Max(1, width / 256);

                // 红色通道
                if (histData.DisplayRedHist[i] > 0)
                {
                    var redHeight = (histData.DisplayRedHist[i] * height) / histData.DisplayMaxValue;
                    var redY = y + height - redHeight;
                    Cv2.Rectangle(outputMat, new OpenCvSharp.Point(binX, redY), new OpenCvSharp.Point(binX + binWidth, y + height), redColor, -1);
                }

                // 绿色通道
                if (histData.DisplayGreenHist[i] > 0)
                {
                    var greenHeight = (histData.DisplayGreenHist[i] * height) / histData.DisplayMaxValue;
                    var greenY = y + height - greenHeight;
                    Cv2.Rectangle(outputMat, new OpenCvSharp.Point(binX, greenY), new OpenCvSharp.Point(binX + binWidth, y + height), greenColor, -1);
                }

                // 蓝色通道
                if (histData.DisplayBlueHist[i] > 0)
                {
                    var blueHeight = (histData.DisplayBlueHist[i] * height) / histData.DisplayMaxValue;
                    var blueY = y + height - blueHeight;
                    Cv2.Rectangle(outputMat, new OpenCvSharp.Point(binX, blueY), new OpenCvSharp.Point(binX + binWidth, y + height), blueColor, -1);
                }
            }
        }

        private void DrawStatistics(Mat outputMat, ImageStatistics stats, int startY)
        {
            var textColor = new Scalar(1.0, 1.0, 1.0, 1.0);
            var highlightColor = new Scalar(0.3, 1.0, 1.0, 1.0); // 高亮颜色用于重要信息
            var font = HersheyFonts.HersheySimplex;
            var fontSize = 0.6;
            var thickness = 1;
            var lineHeight = 25;

            // 使用英文标签以避免OpenCV Hershey字体的中文显示问题
            var texts = new[]
            {
                $"Analysis Mode: {stats.AnalysisMode}",
                $"Highlight Clip: {stats.HighlightClipRatio:P2}",
                $"Shadow Clip: {stats.ShadowClipRatio:P2}",
                $"Display Bit Depth: {stats.EffectiveBitDepth:F2} bits",
                $"True Bit Depth: {stats.TrueEffectiveBitDepth:F2} bits",
                $"Unique Values: {stats.UniqueValues:N0}"
            };

            for (int i = 0; i < texts.Length; i++)
            {
                var textY = startY + i * lineHeight;
                var color = (i == 4) ? highlightColor : textColor; // 高亮真实位深度
                Cv2.PutText(outputMat, texts[i], new OpenCvSharp.Point(50, textY), font, fontSize, color, thickness);
            }
        }



        public override Dictionary<string, object> SerializeParameters()
        {
            return new Dictionary<string, object>
            {
                [nameof(HighlightClipTolerance)] = HighlightClipTolerance,
                [nameof(ShadowClipTolerance)] = ShadowClipTolerance,
                [nameof(MinPixelCount)] = MinPixelCount,
                [nameof(AnalysisMode)] = AnalysisMode,
                [nameof(EnableHighPrecision)] = EnableHighPrecision
            };
        }

        public override void DeserializeParameters(Dictionary<string, object> data)
        {
            if (data.TryGetValue(nameof(HighlightClipTolerance), out var highlightTol))
                HighlightClipTolerance = Convert.ToDouble(highlightTol);

            if (data.TryGetValue(nameof(ShadowClipTolerance), out var shadowTol))
                ShadowClipTolerance = Convert.ToDouble(shadowTol);

            if (data.TryGetValue(nameof(MinPixelCount), out var minPixels))
                MinPixelCount = Convert.ToInt32(minPixels);

            if (data.TryGetValue(nameof(AnalysisMode), out var analysisMode))
                AnalysisMode = Convert.ToInt32(analysisMode);

            if (data.TryGetValue(nameof(EnableHighPrecision), out var enableHighPrecision))
                EnableHighPrecision = Convert.ToBoolean(enableHighPrecision);
        }

        public override FrameworkElement CreateParameterControl()
        {
            var stackPanel = new StackPanel { Margin = new Thickness(5) };

            // 分析模式选择
            var modeLabel = new TextBlock { Text = "分析模式", Margin = new Thickness(0, 5, 0, 2) };
            var modeComboBox = new ComboBox
            {
                ItemsSource = new[] { "自动检测", "8位", "10位", "12位", "14位", "16位" },
                SelectedIndex = AnalysisMode
            };
            modeComboBox.SelectionChanged += (s, e) => AnalysisMode = modeComboBox.SelectedIndex;

            // 高精度分析开关
            var precisionLabel = new TextBlock { Text = "高精度分析", Margin = new Thickness(0, 10, 0, 2) };
            var precisionCheckBox = new CheckBox
            {
                IsChecked = EnableHighPrecision,
                Content = "启用自动位深度检测"
            };
            precisionCheckBox.Checked += (s, e) => EnableHighPrecision = true;
            precisionCheckBox.Unchecked += (s, e) => EnableHighPrecision = false;

            // 亮部裁切容差滑块
            var highlightLabel = new TextBlock { Text = "亮部裁切容差", Margin = new Thickness(0, 10, 0, 2) };
            var highlightSlider = new Slider
            {
                Minimum = 0.0,
                Maximum = 1.0,
                Value = HighlightClipTolerance,
                TickFrequency = 0.01,
                IsSnapToTickEnabled = true
            };
            highlightSlider.ValueChanged += (s, e) => HighlightClipTolerance = e.NewValue;

            // 暗部裁切容差滑块
            var shadowLabel = new TextBlock { Text = "暗部裁切容差", Margin = new Thickness(0, 10, 0, 2) };
            var shadowSlider = new Slider
            {
                Minimum = 0.0,
                Maximum = 1.0,
                Value = ShadowClipTolerance,
                TickFrequency = 0.01,
                IsSnapToTickEnabled = true
            };
            shadowSlider.ValueChanged += (s, e) => ShadowClipTolerance = e.NewValue;

            // 最小像素数滑块
            var minPixelLabel = new TextBlock { Text = "最小像素数", Margin = new Thickness(0, 10, 0, 2) };
            var minPixelSlider = new Slider
            {
                Minimum = 1,
                Maximum = 100,
                Value = MinPixelCount,
                TickFrequency = 1,
                IsSnapToTickEnabled = true
            };
            minPixelSlider.ValueChanged += (s, e) => MinPixelCount = (int)e.NewValue;

            stackPanel.Children.Add(modeLabel);
            stackPanel.Children.Add(modeComboBox);
            stackPanel.Children.Add(precisionLabel);
            stackPanel.Children.Add(precisionCheckBox);
            stackPanel.Children.Add(highlightLabel);
            stackPanel.Children.Add(highlightSlider);
            stackPanel.Children.Add(shadowLabel);
            stackPanel.Children.Add(shadowSlider);
            stackPanel.Children.Add(minPixelLabel);
            stackPanel.Children.Add(minPixelSlider);

            return stackPanel;
        }

        public override IScriptViewModel CreateViewModel()
        {
            return new HistogramViewModel(this);
        }

        public override async Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
        {
            // 参数变化时的处理逻辑
            await Task.CompletedTask;
        }

        public class HistogramViewModel : ScriptViewModelBase
        {
            private HistogramScript HistogramScript => (HistogramScript)Script;

            public HistogramViewModel(HistogramScript script) : base(script) { }

            public double HighlightClipTolerance
            {
                get => HistogramScript.HighlightClipTolerance;
                set => HistogramScript.HighlightClipTolerance = value;
            }

            public double ShadowClipTolerance
            {
                get => HistogramScript.ShadowClipTolerance;
                set => HistogramScript.ShadowClipTolerance = value;
            }

            public int MinPixelCount
            {
                get => HistogramScript.MinPixelCount;
                set => HistogramScript.MinPixelCount = value;
            }

            public int AnalysisMode
            {
                get => HistogramScript.AnalysisMode;
                set => HistogramScript.AnalysisMode = value;
            }

            public bool EnableHighPrecision
            {
                get => HistogramScript.EnableHighPrecision;
                set => HistogramScript.EnableHighPrecision = value;
            }

            public override ScriptValidationResult ValidateParameter(string parameterName, object value)
            {
                switch (parameterName)
                {
                    case nameof(HighlightClipTolerance):
                    case nameof(ShadowClipTolerance):
                        if (value is double tolerance && (tolerance < 0.0 || tolerance > 1.0))
                            return new ScriptValidationResult(false, "容差值必须在0.0到1.0之间");
                        break;
                    case nameof(MinPixelCount):
                        if (value is int count && count < 1)
                            return new ScriptValidationResult(false, "最小像素数必须大于0");
                        break;
                    case nameof(AnalysisMode):
                        if (value is int mode && (mode < 0 || mode > 5))
                            return new ScriptValidationResult(false, "分析模式必须在0到5之间");
                        break;
                }
                return new ScriptValidationResult(true);
            }

            public override Dictionary<string, object> GetParameterData()
            {
                return new Dictionary<string, object>
                {
                    [nameof(HighlightClipTolerance)] = HighlightClipTolerance,
                    [nameof(ShadowClipTolerance)] = ShadowClipTolerance,
                    [nameof(MinPixelCount)] = MinPixelCount,
                    [nameof(AnalysisMode)] = AnalysisMode,
                    [nameof(EnableHighPrecision)] = EnableHighPrecision
                };
            }

            public override async Task SetParameterDataAsync(Dictionary<string, object> data)
            {
                if (data.TryGetValue(nameof(HighlightClipTolerance), out var highlightTol))
                    HighlightClipTolerance = Convert.ToDouble(highlightTol);

                if (data.TryGetValue(nameof(ShadowClipTolerance), out var shadowTol))
                    ShadowClipTolerance = Convert.ToDouble(shadowTol);

                if (data.TryGetValue(nameof(MinPixelCount), out var minPixels))
                    MinPixelCount = Convert.ToInt32(minPixels);

                if (data.TryGetValue(nameof(AnalysisMode), out var analysisMode))
                    AnalysisMode = Convert.ToInt32(analysisMode);

                if (data.TryGetValue(nameof(EnableHighPrecision), out var enableHighPrecision))
                    EnableHighPrecision = Convert.ToBoolean(enableHighPrecision);

                await Task.CompletedTask;
            }

            public override async Task ResetToDefaultAsync()
            {
                HighlightClipTolerance = 0.01;
                ShadowClipTolerance = 0.01;
                MinPixelCount = 1;
                AnalysisMode = 0;
                EnableHighPrecision = true;
                await Task.CompletedTask;
            }

            public override async Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
            {
                // ViewModel参数变化时触发脚本的参数变化处理
                await HistogramScript.OnParameterChangedAsync(parameterName, oldValue, newValue);
            }
        }
    }
}
