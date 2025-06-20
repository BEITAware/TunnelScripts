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
    [RevivalScript(
        Name = "白噪声生成器",
        Author = "BEITAware",
        Description = "生成随机白噪声图像，可选均匀分布/高斯分布",
        Version = "1.0",
        Category = "图像生成",
        Color = "#AAAAAA")]
    public class WhiteNoiseGeneratorScript : RevivalScriptBase
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
            int w = Math.Max(1, Width);
            int h = Math.Max(1, Height);

            var mat = new Mat(h, w, MatType.CV_32FC3);

            switch (Type)
            {
                case NoiseType.Uniform:
                    Cv2.Randu(mat, 0.0, 1.0);
                    break;
                case NoiseType.Gaussian:
                    Cv2.Randn(mat, Mean, StdDev);
                    Cv2.Min(mat, 1.0, mat);
                    Cv2.Max(mat, 0.0, mat);
                    break;
            }

            return new Dictionary<string, object>
            {
                ["Output"] = mat
            };
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
            if (data.TryGetValue(nameof(Type), out var t) && t is NoiseType nt)
                Type = nt;
            if (data.TryGetValue(nameof(Width), out var w) && w is int wi)
                Width = wi;
            if (data.TryGetValue(nameof(Height), out var h) && h is int hi)
                Height = hi;
            if (data.TryGetValue(nameof(Mean), out var m) && m is double md)
                Mean = md;
            if (data.TryGetValue(nameof(StdDev), out var sd) && sd is double sdd)
                StdDev = sdd;
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
                    NotifyChanged(nameof(Type), old, value);
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
                    GenScript.Width = Math.Max(1, value);
                    OnPropertyChanged();
                    NotifyChanged(nameof(Width), old, value);
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
                    GenScript.Height = Math.Max(1, value);
                    OnPropertyChanged();
                    NotifyChanged(nameof(Height), old, value);
                }
            }
        }

        public double Mean
        {
            get => GenScript.Mean;
            set
            {
                if (Math.Abs(GenScript.Mean - value) > 1e-6)
                {
                    var old = GenScript.Mean;
                    GenScript.Mean = Math.Clamp(value, 0, 1);
                    OnPropertyChanged();
                    NotifyChanged(nameof(Mean), old, value);
                }
            }
        }

        public double StdDev
        {
            get => GenScript.StdDev;
            set
            {
                if (Math.Abs(GenScript.StdDev - value) > 1e-6)
                {
                    var old = GenScript.StdDev;
                    GenScript.StdDev = Math.Clamp(value, 0, 1);
                    OnPropertyChanged();
                    NotifyChanged(nameof(StdDev), old, value);
                }
            }
        }

        private void NotifyChanged(string name, object oldVal, object newVal)
        {
            if (GenScript is RevivalScriptBase rsb)
            {
                _ = rsb.OnParameterChangedAsync(name, oldVal, newVal);
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
                if (data.TryGetValue(nameof(Type), out var t) && t is WhiteNoiseGeneratorScript.NoiseType nt) Type = nt;
                if (data.TryGetValue(nameof(Width), out var w) && w is int wi) Width = wi;
                if (data.TryGetValue(nameof(Height), out var h) && h is int hi) Height = hi;
                if (data.TryGetValue(nameof(Mean), out var m) && m is double md) Mean = md;
                if (data.TryGetValue(nameof(StdDev), out var sd) && sd is double sdd) StdDev = sdd;
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