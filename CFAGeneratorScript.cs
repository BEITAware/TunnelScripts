using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Tunnel_Next.Services.Scripting;

namespace TNX_Scripts.ScriptPrototypes
{
    // 新增脚本：用于生成彩色CFA示意图（Bayer / X-Trans 等）
    [RevivalScript(
        Name = "CFA 生成器",
        Author = "BEITAware",
        Description = "生成常见彩色 CFA（Bayer、X-Trans）图样",
        Version = "1.0",
        Category = "图像生成",
        Color = "#FFAA00")]
    public class CFAGeneratorScript : RevivalScriptBase
    {
        // CFA 类型
        public enum CFAPatternType
        {
            Bayer_RGGB,
            Bayer_BGGR,
            Bayer_GRBG,
            Bayer_GBRG,
            XTrans
        }

        [ScriptParameter(DisplayName = "CFA 类型", Description = "选择要生成的 CFA 图样", Order = 0)]
        public CFAPatternType Pattern { get; set; } = CFAPatternType.Bayer_RGGB;

        [ScriptParameter(DisplayName = "块大小(像素)", Description = "每个 CFA 单元块显示的像素大小", Order = 1)]
        public int BlockSize { get; set; } = 16;

        [ScriptParameter(DisplayName = "宽度(块)", Description = "水平方向重复的 CFA 单元数量", Order = 2)]
        public int BlocksX { get; set; } = 32;

        [ScriptParameter(DisplayName = "高度(块)", Description = "垂直方向重复的 CFA 单元数量", Order = 3)]
        public int BlocksY { get; set; } = 32;

        public override Dictionary<string, PortDefinition> GetInputPorts()
        {
            // 此脚本不依赖输入
            return new Dictionary<string, PortDefinition>();
        }

        public override Dictionary<string, PortDefinition> GetOutputPorts()
        {
            return new Dictionary<string, PortDefinition>
            {
                ["Output"] = new PortDefinition("F32bmp", false, "生成的 CFA 图像")
            };
        }

        public override Dictionary<string, object> Process(Dictionary<string, object> inputs, IScriptContext context)
        {
            var pixelWidth = BlocksX * BlockSize;
            var pixelHeight = BlocksY * BlockSize;

            var output = new Mat(pixelHeight, pixelWidth, MatType.CV_8UC3);

            // 根据 CFA 类型获取基元图案矩阵（0=R,1=G,2=B）
            int[,] basePattern = GetBasePattern(Pattern);
            int baseH = basePattern.GetLength(0);
            int baseW = basePattern.GetLength(1);

            for (int by = 0; by < BlocksY; by++)
            {
                for (int bx = 0; bx < BlocksX; bx++)
                {
                    // 计算色彩索引
                    int colorIdx = basePattern[by % baseH, bx % baseW];
                    var color = GetScalarByIndex(colorIdx);

                    int startX = bx * BlockSize;
                    int startY = by * BlockSize;
                    var rect = new OpenCvSharp.Rect(startX, startY, BlockSize, BlockSize);
                    Cv2.Rectangle(output, rect, color, -1);
                }
            }

            // 转换到 32F 格式并归一化到 [0,1]，以匹配 F32bmp
            var output32F = new Mat();
            output.ConvertTo(output32F, MatType.CV_32FC3, 1.0 / 255.0);

            return new Dictionary<string, object>
            {
                ["Output"] = output32F
            };
        }

        private static int[,] GetBasePattern(CFAPatternType type)
        {
            switch (type)
            {
                case CFAPatternType.Bayer_RGGB:
                    return new int[,]
                    {
                        {0,1},
                        {1,2}
                    };
                case CFAPatternType.Bayer_BGGR:
                    return new int[,]
                    {
                        {2,1},
                        {1,0}
                    };
                case CFAPatternType.Bayer_GRBG:
                    return new int[,]
                    {
                        {1,0},
                        {2,1}
                    };
                case CFAPatternType.Bayer_GBRG:
                    return new int[,]
                    {
                        {1,2},
                        {0,1}
                    };
                case CFAPatternType.XTrans:
                    // 6x6 Fuji X-Trans Pattern
                    return new int[,]
                    {
                        {1,0,1,1,0,1},
                        {2,1,2,0,1,2},
                        {1,0,1,1,0,1},
                        {1,0,1,1,0,1},
                        {2,1,2,0,1,2},
                        {1,0,1,1,0,1}
                    };
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }

        private static Scalar GetScalarByIndex(int index)
        {
            // OpenCV 默认 BGR 顺序
            return index switch
            {
                0 => new Scalar(0, 0, 255),   // R
                1 => new Scalar(0, 255, 0),   // G
                2 => new Scalar(255, 0, 0),   // B
                _ => new Scalar(0, 0, 0)
            };
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
                "/Tunnel-Next;component/Resources/ScriptsControls/PanelStyles.xaml",
                "/Tunnel-Next;component/Resources/ScriptsControls/ComboBoxStyles.xaml",
                "/Tunnel-Next;component/Resources/ScriptsControls/TextBoxIdleStyles.xaml",
                "/Tunnel-Next;component/Resources/ScriptsControls/SliderStyles.xaml",
            };

            foreach (var path in resourcePaths)
            {
                try
                {
                    resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(path, UriKind.Relative) });
                }
                catch (Exception)
                {
                    // 静默处理资源加载失败
                }
            }
            
            if (resources.Contains("MainPanelStyle"))
            {
                mainPanel.Style = resources["MainPanelStyle"] as Style;
            }

            // 绑定数据源
            var vm = CreateViewModel() as CFAGeneratorViewModel;
            mainPanel.DataContext = vm;
            
            var titleLabel = new Label { Content = "CFA 生成器" };
            if(resources.Contains("TitleLabelStyle")) titleLabel.Style = resources["TitleLabelStyle"] as Style;
            mainPanel.Children.Add(titleLabel);

            // Pattern 下拉框
            var patternLabel = new Label { Content = "CFA 类型:" };
            if(resources.Contains("DefaultLabelStyle")) patternLabel.Style = resources["DefaultLabelStyle"] as Style;
            mainPanel.Children.Add(patternLabel);

            var patternCombo = new ComboBox { ItemsSource = Enum.GetValues(typeof(CFAPatternType)), Margin = new Thickness(0,0,0,10) };
            if(resources.Contains("DefaultComboBoxStyle")) patternCombo.Style = resources["DefaultComboBoxStyle"] as Style;
            patternCombo.SetBinding(ComboBox.SelectedItemProperty, new System.Windows.Data.Binding(nameof(CFAGeneratorViewModel.Pattern)) { Mode = System.Windows.Data.BindingMode.TwoWay });
            mainPanel.Children.Add(patternCombo);

            // BlockSize Slider
            var blockSizeLabel = new Label { Content = "块大小:" };
            if(resources.Contains("DefaultLabelStyle")) blockSizeLabel.Style = resources["DefaultLabelStyle"] as Style;
            mainPanel.Children.Add(blockSizeLabel);

            var blockSlider = new Slider { Minimum = 1, Maximum = 64, Value = vm.BlockSize, Margin = new Thickness(0,0,0,10) };
            if(resources.Contains("DefaultSliderStyle")) blockSlider.Style = resources["DefaultSliderStyle"] as Style;
            blockSlider.SetBinding(Slider.ValueProperty, new System.Windows.Data.Binding(nameof(CFAGeneratorViewModel.BlockSize)) { Mode = System.Windows.Data.BindingMode.TwoWay });
            mainPanel.Children.Add(blockSlider);

            // BlocksX
            var blocksXLabel = new Label { Content = "宽度(块):" };
            if(resources.Contains("DefaultLabelStyle")) blocksXLabel.Style = resources["DefaultLabelStyle"] as Style;
            mainPanel.Children.Add(blocksXLabel);

            var tbX = new TextBox { Margin = new Thickness(0,0,0,10) };
            if(resources.Contains("DefaultTextBoxStyle")) tbX.Style = resources["DefaultTextBoxStyle"] as Style;
            tbX.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding(nameof(CFAGeneratorViewModel.BlocksX)) { Mode = System.Windows.Data.BindingMode.TwoWay });
            mainPanel.Children.Add(tbX);

            // BlocksY
            var blocksYLabel = new Label { Content = "高度(块):" };
            if(resources.Contains("DefaultLabelStyle")) blocksYLabel.Style = resources["DefaultLabelStyle"] as Style;
            mainPanel.Children.Add(blocksYLabel);
            
            var tbY = new TextBox { Margin = new Thickness(0,0,0,10) };
            if(resources.Contains("DefaultTextBoxStyle")) tbY.Style = resources["DefaultTextBoxStyle"] as Style;
            tbY.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding(nameof(CFAGeneratorViewModel.BlocksY)) { Mode = System.Windows.Data.BindingMode.TwoWay });
            mainPanel.Children.Add(tbY);

            return mainPanel;
        }

        public override IScriptViewModel CreateViewModel()
        {
            return new CFAGeneratorViewModel(this);
        }

        public override async Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
        {
            await Task.CompletedTask;
        }

        public override Dictionary<string, object> SerializeParameters()
        {
            return new Dictionary<string, object>
            {
                [nameof(Pattern)] = Pattern,
                [nameof(BlockSize)] = BlockSize,
                [nameof(BlocksX)] = BlocksX,
                [nameof(BlocksY)] = BlocksY
            };
        }

        public override void DeserializeParameters(Dictionary<string, object> data)
        {
            if (data.TryGetValue(nameof(Pattern), out var p) && p is CFAPatternType pattern)
                Pattern = pattern;
            if (data.TryGetValue(nameof(BlockSize), out var bs) && bs is int bsz)
                BlockSize = bsz;
            if (data.TryGetValue(nameof(BlocksX), out var bx) && bx is int bxv)
                BlocksX = bxv;
            if (data.TryGetValue(nameof(BlocksY), out var by) && by is int byv)
                BlocksY = byv;
        }
    }

    public class CFAGeneratorViewModel : ScriptViewModelBase
    {
        private CFAGeneratorScript GenScript => (CFAGeneratorScript)Script;

        public CFAGeneratorViewModel(CFAGeneratorScript script) : base(script)
        {
        }

        public CFAGeneratorScript.CFAPatternType Pattern
        {
            get => GenScript.Pattern;
            set
            {
                if (GenScript.Pattern != value)
                {
                    var oldVal = GenScript.Pattern;
                    GenScript.Pattern = value;
                    OnPropertyChanged();
                    NotifyParameterChanged(nameof(Pattern), oldVal, value);
                }
            }
        }

        public int BlockSize
        {
            get => GenScript.BlockSize;
            set
            {
                if (GenScript.BlockSize != value)
                {
                    var oldVal = GenScript.BlockSize;
                    GenScript.BlockSize = Math.Max(1, value);
                    OnPropertyChanged();
                    NotifyParameterChanged(nameof(BlockSize), oldVal, value);
                }
            }
        }

        public int BlocksX
        {
            get => GenScript.BlocksX;
            set
            {
                if (GenScript.BlocksX != value)
                {
                    var oldVal = GenScript.BlocksX;
                    GenScript.BlocksX = Math.Max(1, value);
                    OnPropertyChanged();
                    NotifyParameterChanged(nameof(BlocksX), oldVal, value);
                }
            }
        }

        public int BlocksY
        {
            get => GenScript.BlocksY;
            set
            {
                if (GenScript.BlocksY != value)
                {
                    var oldVal = GenScript.BlocksY;
                    GenScript.BlocksY = Math.Max(1, value);
                    OnPropertyChanged();
                    NotifyParameterChanged(nameof(BlocksY), oldVal, value);
                }
            }
        }

        private void NotifyParameterChanged(string paramName, object oldVal, object newVal)
        {
            if (GenScript is RevivalScriptBase rsb)
            {
                _ = rsb.OnParameterChangedAsync(paramName, oldVal, newVal);
            }
        }

        public override async Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
        {
            await GenScript.OnParameterChangedAsync(parameterName, oldValue, newValue);
        }

        public override ScriptValidationResult ValidateParameter(string parameterName, object value)
        {
            switch (parameterName)
            {
                case nameof(BlockSize):
                    if (value is int bs && bs >= 1 && bs <= 64) return new ScriptValidationResult(true);
                    return new ScriptValidationResult(false, "块大小应在1-64之间");
                case nameof(BlocksX):
                case nameof(BlocksY):
                    if (value is int v && v >= 1 && v <= 512) return new ScriptValidationResult(true);
                    return new ScriptValidationResult(false, "块数量应在1-512之间");
            }
            return new ScriptValidationResult(true);
        }

        public override Dictionary<string, object> GetParameterData()
        {
            return new Dictionary<string, object>
            {
                [nameof(Pattern)] = Pattern,
                [nameof(BlockSize)] = BlockSize,
                [nameof(BlocksX)] = BlocksX,
                [nameof(BlocksY)] = BlocksY
            };
        }

        public override async Task SetParameterDataAsync(Dictionary<string, object> data)
        {
            await RunOnUIThreadAsync(() =>
            {
                if (data.TryGetValue(nameof(Pattern), out var p) && p is CFAGeneratorScript.CFAPatternType pattern)
                    Pattern = pattern;
                if (data.TryGetValue(nameof(BlockSize), out var bs) && bs is int bsz)
                    BlockSize = bsz;
                if (data.TryGetValue(nameof(BlocksX), out var bx) && bx is int bxv)
                    BlocksX = bxv;
                if (data.TryGetValue(nameof(BlocksY), out var by) && by is int byv)
                    BlocksY = byv;
            });
        }

        public override async Task ResetToDefaultAsync()
        {
            await RunOnUIThreadAsync(() =>
            {
                Pattern = CFAGeneratorScript.CFAPatternType.Bayer_RGGB;
                BlockSize = 16;
                BlocksX = 32;
                BlocksY = 32;
            });
        }
    }
} 