using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Tunnel_Next.Services.Scripting;

namespace TNX_Scripts.ScriptPrototypes
{
    [RevivalScript(
        Name = "白噪声生成器",
        Author = "GeneratedByAI",
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
            var panel = new StackPanel { Margin = new Thickness(5) };
            var vm = CreateViewModel() as WhiteNoiseViewModel;
            panel.DataContext = vm;

            panel.Children.Add(new Label { Content = "白噪声生成器", FontWeight = FontWeights.Bold });

            // Noise type
            panel.Children.Add(new Label { Content = "噪声类型:" });
            var combo = new ComboBox { ItemsSource = Enum.GetValues(typeof(NoiseType)) };
            combo.SetBinding(ComboBox.SelectedItemProperty, new System.Windows.Data.Binding(nameof(WhiteNoiseViewModel.Type)) { Mode = System.Windows.Data.BindingMode.TwoWay });
            panel.Children.Add(combo);

            // Width
            panel.Children.Add(new Label { Content = "宽度:" });
            var tbWidth = new TextBox { Width = 60 };
            tbWidth.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding(nameof(WhiteNoiseViewModel.Width)) { Mode = System.Windows.Data.BindingMode.TwoWay });
            panel.Children.Add(tbWidth);

            // Height
            panel.Children.Add(new Label { Content = "高度:" });
            var tbHeight = new TextBox { Width = 60 };
            tbHeight.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding(nameof(WhiteNoiseViewModel.Height)) { Mode = System.Windows.Data.BindingMode.TwoWay });
            panel.Children.Add(tbHeight);

            // Mean
            panel.Children.Add(new Label { Content = "均值:" });
            var sliderMean = new Slider { Minimum = 0, Maximum = 1, TickFrequency = 0.01, Width = 120 };
            sliderMean.SetBinding(Slider.ValueProperty, new System.Windows.Data.Binding(nameof(WhiteNoiseViewModel.Mean)) { Mode = System.Windows.Data.BindingMode.TwoWay });
            panel.Children.Add(sliderMean);

            // StdDev
            panel.Children.Add(new Label { Content = "标准差:" });
            var sliderStd = new Slider { Minimum = 0, Maximum = 1, TickFrequency = 0.01, Width = 120 };
            sliderStd.SetBinding(Slider.ValueProperty, new System.Windows.Data.Binding(nameof(WhiteNoiseViewModel.StdDev)) { Mode = System.Windows.Data.BindingMode.TwoWay });
            panel.Children.Add(sliderStd);

            return panel;
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