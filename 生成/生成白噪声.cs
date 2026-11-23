using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Tunnel_Next.Services.Scripting;
using System.Windows.Media;
using System.Windows.Data;

namespace TNX_Scripts.ScriptPrototypes
{
    [TunnelExtensionScript(
        Name = "白噪声生成器",
        Author = "BEITAware",
        Description = "生成随机白噪声图像，可选均匀分布/高斯分布",
        Version = "1.0",
        Category = "图像生成",
        Color = "#AAAAAA")]
    public class WhiteNoiseGeneratorScript : TunnelExtensionScriptBase
    {
        public enum NoiseType
        {
            Uniform,
            Gaussian
        }

        [ScriptParameter(DisplayName = "噪声类型", Description = "选择噪声分布类型", Order = 0)]
        public NoiseType Type { get; set; } = NoiseType.Uniform;

        [ScriptParameter(DisplayName = "宽度", Description = "生成图像宽度 (像素)", Order = 1)]
        public int Width { get; set; } = 512;

        [ScriptParameter(DisplayName = "高度", Description = "生成图像高度 (像素)", Order = 2)]
        public int Height { get; set; } = 512;

        [ScriptParameter(DisplayName = "均值", Description = "高斯噪声均值 (0-1)", Order = 3)]
        public double Mean { get; set; } = 0.5;

        [ScriptParameter(DisplayName = "标准差", Description = "高斯噪声标准差 (0-1)", Order = 4)]
        public double StdDev { get; set; } = 0.2;

        public override Dictionary<string, PortDefinition> GetInputPorts()
        {
            return new Dictionary<string, PortDefinition>();
        }

        public override Dictionary<string, PortDefinition> GetOutputPorts()
        {
            return new Dictionary<string, PortDefinition>
            {
                ["Output"] = new PortDefinition("F32bmp", false, "生成的白噪声图像")
            };
        }

        public override Dictionary<string, object> Process(Dictionary<string, object> inputs, IScriptContext context)
        {
            int width = Math.Clamp(Width, 1, 4096);
            int height = Math.Clamp(Height, 1, 4096);
            using var colors = new Mat(height, width, MatType.CV_32FC3);
            if (Type == NoiseType.Gaussian)
            {
                Cv2.Randn(colors, Scalar.All(Math.Clamp(Mean, 0, 1)), Scalar.All(Math.Clamp(StdDev, 0, 1)));
                Cv2.Min(colors, 1, colors);
                Cv2.Max(colors, 0, colors);
            }
            else
            {
                Cv2.Randu(colors, Scalar.All(0), Scalar.All(1));
            }

            var output = new Mat();
            Cv2.CvtColor(colors, output, ColorConversionCodes.BGR2BGRA);
            return new Dictionary<string, object> { ["Output"] = output };
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
                "/Tunnel-Next;component/Resources/ScriptsControls/ComboBoxStyles.xaml",
                "/Tunnel-Next;component/Resources/ScriptsControls/TextBoxIdleStyles.xaml",
                "/Tunnel-Next;component/Resources/ScriptsControls/SliderStyles.xaml"
            };
            foreach (var path in resourcePaths)
            {
                try { resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(path, UriKind.Relative) }); }
                catch { /* 静默处理 */ }
            }

            if (resources.Contains("Layer_2"))
            {
                mainPanel.Background = resources["Layer_2"] as Brush;
            }

            var viewModel = CreateViewModel() as WhiteNoiseViewModel;
            mainPanel.DataContext = viewModel;

            var titleLabel = new Label { Content = "白噪声生成器" };
            if (resources.Contains("TitleLabelStyle"))
            {
                titleLabel.Style = resources["TitleLabelStyle"] as Style;
            }
            mainPanel.Children.Add(titleLabel);

            // Noise type
            mainPanel.Children.Add(CreateLabel("噪声类型:", resources));
            var combo = new ComboBox { ItemsSource = Enum.GetValues(typeof(NoiseType)), Margin = new Thickness(0, 2, 0, 10) };
            if (resources.Contains("DefaultComboBoxStyle")) combo.Style = resources["DefaultComboBoxStyle"] as Style;
            combo.SetBinding(ComboBox.SelectedItemProperty, new System.Windows.Data.Binding(nameof(WhiteNoiseViewModel.Type)) { Mode = System.Windows.Data.BindingMode.TwoWay });
            mainPanel.Children.Add(combo);

            // Width
            mainPanel.Children.Add(CreateLabel("宽度:", resources));
            var tbWidth = new TextBox { Margin = new Thickness(0, 2, 0, 10) };
            if (resources.Contains("DefaultTextBoxStyle")) tbWidth.Style = resources["DefaultTextBoxStyle"] as Style;
            tbWidth.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding(nameof(WhiteNoiseViewModel.Width)) { Mode = System.Windows.Data.BindingMode.TwoWay, UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged });
            mainPanel.Children.Add(tbWidth);

            // Height
            mainPanel.Children.Add(CreateLabel("高度:", resources));
            var tbHeight = new TextBox { Margin = new Thickness(0, 2, 0, 10) };
            if (resources.Contains("DefaultTextBoxStyle")) tbHeight.Style = resources["DefaultTextBoxStyle"] as Style;
            tbHeight.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding(nameof(WhiteNoiseViewModel.Height)) { Mode = System.Windows.Data.BindingMode.TwoWay, UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged });
            mainPanel.Children.Add(tbHeight);

            // Gaussian-specific controls
            var gaussianPanel = new StackPanel();
            var visibilityBinding = new System.Windows.Data.Binding(nameof(WhiteNoiseViewModel.Type))
            {
                Converter = new EnumToVisibilityConverter(),
                ConverterParameter = NoiseType.Gaussian
            };
            gaussianPanel.SetBinding(UIElement.VisibilityProperty, visibilityBinding);
            mainPanel.Children.Add(gaussianPanel);
            
            // Mean
            gaussianPanel.Children.Add(CreateLabel("均值:", resources));
            var sliderMean = new Slider { Minimum = 0, Maximum = 1, SmallChange = 0.01, Margin = new Thickness(0, 2, 0, 10) };
            if (resources.Contains("DefaultSliderStyle")) sliderMean.Style = resources["DefaultSliderStyle"] as Style;
            sliderMean.SetBinding(Slider.ValueProperty, new System.Windows.Data.Binding(nameof(WhiteNoiseViewModel.Mean)) { Mode = System.Windows.Data.BindingMode.TwoWay });
            gaussianPanel.Children.Add(sliderMean);

            // StdDev
            gaussianPanel.Children.Add(CreateLabel("标准差:", resources));
            var sliderStd = new Slider { Minimum = 0, Maximum = 1, SmallChange = 0.01, Margin = new Thickness(0, 2, 0, 10) };
            if (resources.Contains("DefaultSliderStyle")) sliderStd.Style = resources["DefaultSliderStyle"] as Style;
            sliderStd.SetBinding(Slider.ValueProperty, new System.Windows.Data.Binding(nameof(WhiteNoiseViewModel.StdDev)) { Mode = System.Windows.Data.BindingMode.TwoWay });
            gaussianPanel.Children.Add(sliderStd);

            return mainPanel;
        }

        private Label CreateLabel(string content, ResourceDictionary resources)
        {
            var label = new Label { Content = content, Margin = new Thickness(0, 10, 0, 2) };
            if (resources.Contains("DefaultLabelStyle"))
            {
                label.Style = resources["DefaultLabelStyle"] as Style;
            }
            return label;
        }

        public override IScriptViewModel CreateViewModel()
        {
            return new WhiteNoiseViewModel(this);
        }

        public override async Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
        {
            await Task.CompletedTask;
        }

        public override Dictionary<string, object> SerializeParameters()
        {
            return new Dictionary<string, object>
            {
                [nameof(Type)] = Type,
                [nameof(Width)] = Width,
                [nameof(Height)] = Height,
                [nameof(Mean)] = Mean,
                [nameof(StdDev)] = StdDev
            };
        }

        public override void DeserializeParameters(Dictionary<string, object> data)
        {
            Type = ReadType(data, Type);
            Width = ReadDimension(data, nameof(Width), Width);
            Height = ReadDimension(data, nameof(Height), Height);
            Mean = ReadUnit(data, nameof(Mean), Mean);
            StdDev = ReadUnit(data, nameof(StdDev), StdDev);
        }

        internal static NoiseType ReadType(Dictionary<string, object> data, NoiseType fallback)
        {
            return data.TryGetValue(nameof(Type), out var raw) &&
                   Enum.TryParse<NoiseType>(raw?.ToString(), true, out var parsed) && Enum.IsDefined(parsed)
                ? parsed : fallback;
        }

        internal static int ReadDimension(Dictionary<string, object> data, string key, int fallback)
        {
            return data.TryGetValue(key, out var raw) && int.TryParse(raw?.ToString(), out var value)
                ? Math.Clamp(value, 1, 4096) : fallback;
        }

        internal static double ReadUnit(Dictionary<string, object> data, string key, double fallback)
        {
            if (!data.TryGetValue(key, out var raw)) return fallback;
            double value;
            if (raw is System.Text.Json.JsonElement element && element.ValueKind == System.Text.Json.JsonValueKind.Number)
            {
                if (!element.TryGetDouble(out value)) return fallback;
            }
            else if (!double.TryParse(raw?.ToString(), out value))
                return fallback;
            return double.IsFinite(value) ? Math.Clamp(value, 0, 1) : fallback;
        }
    }

    public class EnumToVisibilityConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value == null || parameter == null)
                return Visibility.Collapsed;
            
            return value.Equals(parameter) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class WhiteNoiseViewModel : ScriptViewModelBase
    {
        private WhiteNoiseGeneratorScript GenScript => (WhiteNoiseGeneratorScript)Script;

        public WhiteNoiseViewModel(WhiteNoiseGeneratorScript script) : base(script) { }

        public WhiteNoiseGeneratorScript.NoiseType Type
        {
            get => GenScript.Type;
            set
            {
                if (GenScript.Type != value)
                {
                    var old = GenScript.Type;
                    GenScript.Type = value;
                    OnPropertyChanged();
                    NotifyParameterChanged(nameof(Type), value);
                }
            }
        }

        public int Width
        {
            get => GenScript.Width;
            set
            {
                if (GenScript.Width != value)
                {
                    var old = GenScript.Width;
                    GenScript.Width = Math.Clamp(value, 1, 4096);
                    OnPropertyChanged();
                    NotifyParameterChanged(nameof(Width), GenScript.Width);
                }
            }
        }

        public int Height
        {
            get => GenScript.Height;
            set
            {
                if (GenScript.Height != value)
                {
                    var old = GenScript.Height;
                    GenScript.Height = Math.Clamp(value, 1, 4096);
                    OnPropertyChanged();
                    NotifyParameterChanged(nameof(Height), GenScript.Height);
                }
            }
        }

        public double Mean
        {
            get => GenScript.Mean;
            set
            {
                if (Math.Abs(GenScript.Mean - value) > 0.001)
                {
                    var old = GenScript.Mean;
                    GenScript.Mean = value;
                    OnPropertyChanged();
                    NotifyParameterChanged(nameof(Mean), value);
                }
            }
        }

        public double StdDev
        {
            get => GenScript.StdDev;
            set
            {
                if (Math.Abs(GenScript.StdDev - value) > 0.001)
                {
                    var old = GenScript.StdDev;
                    GenScript.StdDev = value;
                    OnPropertyChanged();
                    NotifyParameterChanged(nameof(StdDev), value);
                }
            }
        }

        private void NotifyParameterChanged(string parameterName, object value)
        {
            if (Script is TunnelExtensionScriptBase rsb)
            {
                rsb.OnParameterChanged(parameterName, value);
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
                case nameof(Width):
                case nameof(Height):
                    if (value is int i && i >= 1 && i <= 4096) return new ScriptValidationResult(true);
                    return new ScriptValidationResult(false, "尺寸范围 1-4096");
                case nameof(Mean):
                case nameof(StdDev):
                    if (value is double d && d >= 0 && d <= 1) return new ScriptValidationResult(true);
                    return new ScriptValidationResult(false, "数值范围 0-1");
            }
            return new ScriptValidationResult(true);
        }

        public override Dictionary<string, object> GetParameterData()
        {
            return new Dictionary<string, object>
            {
                [nameof(Type)] = Type,
                [nameof(Width)] = Width,
                [nameof(Height)] = Height,
                [nameof(Mean)] = Mean,
                [nameof(StdDev)] = StdDev
            };
        }

        public override async Task SetParameterDataAsync(Dictionary<string, object> data)
        {
            await RunOnUIThreadAsync(() =>
            {
                Type = WhiteNoiseGeneratorScript.ReadType(data, Type);
                Width = WhiteNoiseGeneratorScript.ReadDimension(data, nameof(Width), Width);
                Height = WhiteNoiseGeneratorScript.ReadDimension(data, nameof(Height), Height);
                Mean = WhiteNoiseGeneratorScript.ReadUnit(data, nameof(Mean), Mean);
                StdDev = WhiteNoiseGeneratorScript.ReadUnit(data, nameof(StdDev), StdDev);
            });
        }

        public override async Task ResetToDefaultAsync()
        {
            await RunOnUIThreadAsync(() =>
            {
                Type = WhiteNoiseGeneratorScript.NoiseType.Uniform;
                Width = 512;
                Height = 512;
                Mean = 0.5;
                StdDev = 0.2;
            });
        }
    }
} 