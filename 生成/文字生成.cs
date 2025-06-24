using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Tunnel_Next.Services.Scripting;
using System.Windows.Media;
using System.Globalization;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;
using System.Linq;

namespace TNX_Scripts.ScriptPrototypes
{
    [RevivalScript(
        Name = "文字生成器",
        Author = "BEITAware",
        Description = "生成指定文字",
        Version = "1.0",
        Category = "图像生成",
        Color = "#AABBCC")]
    public class TextImageGeneratorScript : RevivalScriptBase
    {
        [ScriptParameter(DisplayName = "文本内容", Description = "要渲染的文字内容", Order = 0)]
        public string Text { get; set; } = "Hello, Tunnel!";

        [ScriptParameter(DisplayName = "字体", Description = "系统字体名称", Order = 1)]
        public string FontName { get; set; } = "Arial";

        [ScriptParameter(DisplayName = "宽度", Description = "生成图像宽度 (像素)", Order = 2)]
        public int Width { get; set; } = 1024;

        [ScriptParameter(DisplayName = "高度", Description = "生成图像高度 (像素)", Order = 3)]
        public int Height { get; set; } = 512;

        [ScriptParameter(DisplayName = "字体大小", Description = "字体大小 (像素)", Order = 4)]
        public double FontScale { get; set; } = 3.0;

        [ScriptParameter(DisplayName = "粗细", Description = "文本线条粗细", Order = 5)]
        public int Thickness { get; set; } = 2;

        public override Dictionary<string, PortDefinition> GetInputPorts()
        {
            return new Dictionary<string, PortDefinition>();
        }

        public override Dictionary<string, PortDefinition> GetOutputPorts()
        {
            return new Dictionary<string, PortDefinition>
            {
                ["Output"] = new PortDefinition("F32bmp", false, "生成的文字图像")
            };
        }

        public override Dictionary<string, object> Process(Dictionary<string, object> inputs, IScriptContext context)
        {
            int w = Math.Max(1, Width);
            int h = Math.Max(1, Height);

            // 使用 WPF RenderTargetBitmap
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                // 黑底
                dc.DrawRectangle(Brushes.Black, null, new System.Windows.Rect(0, 0, w, h));

                // 文本
                var typeface = new Typeface(new FontFamily(FontName), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
                var ft = new FormattedText(
                    Text,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    FontScale, // Font size in px
                    Brushes.White,
                    1.0); // pixelsPerDip

                double x = Math.Max(0, (w - ft.Width) / 2);
                double y = Math.Max(0, (h - ft.Height) / 2);
                dc.DrawText(ft, new System.Windows.Point(x, y));
            }

            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);

            // BitmapSource -> byte[]
            int stride = w * 4;
            var pixels = new byte[h * stride];
            rtb.CopyPixels(pixels, stride, 0);

            // 创建 Mat 并复制像素，随后转换为 float32 BGR
            using var mat8 = new Mat(h, w, MatType.CV_8UC4);
            Marshal.Copy(pixels, 0, mat8.Data, pixels.Length);

            var mat8bgr = new Mat();
            Cv2.CvtColor(mat8, mat8bgr, ColorConversionCodes.BGRA2BGR);

            var mat32 = new Mat();
            mat8bgr.ConvertTo(mat32, MatType.CV_32FC3, 1.0 / 255.0);

            return new Dictionary<string, object>
            {
                ["Output"] = mat32
            };
        }

        public override FrameworkElement CreateParameterControl()
        {
            var mainPanel = new StackPanel { Margin = new Thickness(5) };

            // 资源加载
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
                catch { /* 忽略加载失败 */ }
            }

            if (resources.Contains("Layer_2"))
            {
                mainPanel.Background = resources["Layer_2"] as Brush;
            }

            var viewModel = CreateViewModel() as TextImageViewModel;
            mainPanel.DataContext = viewModel;

            var titleLabel = new Label { Content = "文字生成器" };
            if (resources.Contains("TitleLabelStyle"))
            {
                titleLabel.Style = resources["TitleLabelStyle"] as Style;
            }
            mainPanel.Children.Add(titleLabel);

            // 文本内容
            mainPanel.Children.Add(CreateLabel("文本内容:", resources));
            var tbText = new TextBox { Margin = new Thickness(0, 2, 0, 10) };
            if (resources.Contains("DefaultTextBoxStyle")) tbText.Style = resources["DefaultTextBoxStyle"] as Style;
            tbText.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding(nameof(TextImageViewModel.Text)) { Mode = System.Windows.Data.BindingMode.TwoWay, UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged });
            mainPanel.Children.Add(tbText);

            // Font
            mainPanel.Children.Add(CreateLabel("字体:", resources));
            var cbFont = new ComboBox { Margin = new Thickness(0, 2, 0, 10) };
            if (resources.Contains("DefaultComboBoxStyle")) cbFont.Style = resources["DefaultComboBoxStyle"] as Style;
            cbFont.ItemsSource = System.Windows.Media.Fonts.SystemFontFamilies.Select(f => f.Source);
            cbFont.SetBinding(ComboBox.SelectedItemProperty, new System.Windows.Data.Binding(nameof(TextImageViewModel.FontName)) { Mode = System.Windows.Data.BindingMode.TwoWay });
            mainPanel.Children.Add(cbFont);

            // Width
            mainPanel.Children.Add(CreateLabel("宽度:", resources));
            var tbWidth = new TextBox { Margin = new Thickness(0, 2, 0, 10) };
            if (resources.Contains("DefaultTextBoxStyle")) tbWidth.Style = resources["DefaultTextBoxStyle"] as Style;
            tbWidth.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding(nameof(TextImageViewModel.Width)) { Mode = System.Windows.Data.BindingMode.TwoWay, UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged });
            mainPanel.Children.Add(tbWidth);

            // Height
            mainPanel.Children.Add(CreateLabel("高度:", resources));
            var tbHeight = new TextBox { Margin = new Thickness(0, 2, 0, 10) };
            if (resources.Contains("DefaultTextBoxStyle")) tbHeight.Style = resources["DefaultTextBoxStyle"] as Style;
            tbHeight.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding(nameof(TextImageViewModel.Height)) { Mode = System.Windows.Data.BindingMode.TwoWay, UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged });
            mainPanel.Children.Add(tbHeight);

            // FontScale
            mainPanel.Children.Add(CreateLabel("字体大小:", resources));
            var sliderScale = new Slider { Minimum = 0.1, Maximum = 100, SmallChange = 0.5, Margin = new Thickness(0, 2, 0, 10) };
            if (resources.Contains("DefaultSliderStyle")) sliderScale.Style = resources["DefaultSliderStyle"] as Style;
            sliderScale.SetBinding(Slider.ValueProperty, new System.Windows.Data.Binding(nameof(TextImageViewModel.FontScale)) { Mode = System.Windows.Data.BindingMode.TwoWay });
            mainPanel.Children.Add(sliderScale);

            // Thickness
            mainPanel.Children.Add(CreateLabel("粗细:", resources));
            var sliderThick = new Slider { Minimum = 1, Maximum = 10, SmallChange = 1, TickFrequency = 1, Margin = new Thickness(0, 2, 0, 10) };
            if (resources.Contains("DefaultSliderStyle")) sliderThick.Style = resources["DefaultSliderStyle"] as Style;
            sliderThick.SetBinding(Slider.ValueProperty, new System.Windows.Data.Binding(nameof(TextImageViewModel.Thickness)) { Mode = System.Windows.Data.BindingMode.TwoWay });
            mainPanel.Children.Add(sliderThick);

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
            return new TextImageViewModel(this);
        }

        public override async Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
        {
            await Task.CompletedTask;
        }

        public override Dictionary<string, object> SerializeParameters()
        {
            return new Dictionary<string, object>
            {
                [nameof(Text)] = Text,
                [nameof(FontName)] = FontName,
                [nameof(Width)] = Width,
                [nameof(Height)] = Height,
                [nameof(FontScale)] = FontScale,
                [nameof(Thickness)] = Thickness
            };
        }

        public override void DeserializeParameters(Dictionary<string, object> data)
        {
            if (data.TryGetValue(nameof(Text), out var txt) && txt is string s) Text = s;
            if (data.TryGetValue(nameof(FontName), out var fn) && fn is string fns) FontName = fns;
            if (data.TryGetValue(nameof(Width), out var w) && w is int wi) Width = wi;
            if (data.TryGetValue(nameof(Height), out var h) && h is int hi) Height = hi;
            if (data.TryGetValue(nameof(FontScale), out var fs) && fs is double d) FontScale = d;
            if (data.TryGetValue(nameof(Thickness), out var th) && th is int ti) Thickness = ti;
        }
    }

    public class TextImageViewModel : ScriptViewModelBase
    {
        private TextImageGeneratorScript TxtScript => (TextImageGeneratorScript)Script;

        public TextImageViewModel(TextImageGeneratorScript script) : base(script) { }

        public string Text
        {
            get => TxtScript.Text;
            set
            {
                if (TxtScript.Text != value)
                {
                    var old = TxtScript.Text;
                    TxtScript.Text = value;
                    OnPropertyChanged();
                    NotifyParameterChanged(nameof(Text), value);
                }
            }
        }

        public string FontName
        {
            get => TxtScript.FontName;
            set
            {
                if (TxtScript.FontName != value)
                {
                    var old = TxtScript.FontName;
                    TxtScript.FontName = value;
                    OnPropertyChanged();
                    NotifyParameterChanged(nameof(FontName), value);
                }
            }
        }

        public int Width
        {
            get => TxtScript.Width;
            set
            {
                if (TxtScript.Width != value)
                {
                    var old = TxtScript.Width;
                    TxtScript.Width = Math.Max(1, value);
                    OnPropertyChanged();
                    NotifyParameterChanged(nameof(Width), value);
                }
            }
        }

        public int Height
        {
            get => TxtScript.Height;
            set
            {
                if (TxtScript.Height != value)
                {
                    var old = TxtScript.Height;
                    TxtScript.Height = Math.Max(1, value);
                    OnPropertyChanged();
                    NotifyParameterChanged(nameof(Height), value);
                }
            }
        }

        public double FontScale
        {
            get => TxtScript.FontScale;
            set
            {
                if (Math.Abs(TxtScript.FontScale - value) > 0.001)
                {
                    var old = TxtScript.FontScale;
                    TxtScript.FontScale = value;
                    OnPropertyChanged();
                    NotifyParameterChanged(nameof(FontScale), value);
                }
            }
        }

        public int Thickness
        {
            get => TxtScript.Thickness;
            set
            {
                if (TxtScript.Thickness != value)
                {
                    var old = TxtScript.Thickness;
                    TxtScript.Thickness = value;
                    OnPropertyChanged();
                    NotifyParameterChanged(nameof(Thickness), value);
                }
            }
        }

        private void NotifyParameterChanged(string parameterName, object value)
        {
            if (Script is RevivalScriptBase rsb)
            {
                rsb.OnParameterChanged(parameterName, value);
            }
        }

        public override async Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
        {
            await TxtScript.OnParameterChangedAsync(parameterName, oldValue, newValue);
        }

        public override ScriptValidationResult ValidateParameter(string parameterName, object value)
        {
            switch (parameterName)
            {
                case nameof(FontName):
                    if (value is string str && !string.IsNullOrWhiteSpace(str)) return new ScriptValidationResult(true);
                    return new ScriptValidationResult(false, "字体不能为空");
                case nameof(Width):
                case nameof(Height):
                    if (value is int i && i >= 1 && i <= 8192) return new ScriptValidationResult(true);
                    return new ScriptValidationResult(false, "尺寸范围 1-8192");
                case nameof(FontScale):
                    if (value is double d && d > 0 && d <= 20) return new ScriptValidationResult(true);
                    return new ScriptValidationResult(false, "范围 0-20");
                case nameof(Thickness):
                    if (value is int t && t >= 1 && t <= 20) return new ScriptValidationResult(true);
                    return new ScriptValidationResult(false, "范围 1-20");
            }
            return new ScriptValidationResult(true);
        }

        public override Dictionary<string, object> GetParameterData()
        {
            return new Dictionary<string, object>
            {
                [nameof(Text)] = Text,
                [nameof(FontName)] = FontName,
                [nameof(Width)] = Width,
                [nameof(Height)] = Height,
                [nameof(FontScale)] = FontScale,
                [nameof(Thickness)] = Thickness
            };
        }

        public override async Task SetParameterDataAsync(Dictionary<string, object> data)
        {
            await RunOnUIThreadAsync(() =>
            {
                if (data.TryGetValue(nameof(Text), out var txt) && txt is string s) Text = s;
                if (data.TryGetValue(nameof(FontName), out var fn) && fn is string fns) FontName = fns;
                if (data.TryGetValue(nameof(Width), out var w) && w is int wi) Width = wi;
                if (data.TryGetValue(nameof(Height), out var h) && h is int hi) Height = hi;
                if (data.TryGetValue(nameof(FontScale), out var fs) && fs is double d) FontScale = d;
                if (data.TryGetValue(nameof(Thickness), out var th) && th is int ti) Thickness = ti;
            });
        }

        public override async Task ResetToDefaultAsync()
        {
            await RunOnUIThreadAsync(() =>
            {
                Text = "Hello, Tunnel!";
                FontName = "Arial";
                Width = 1024;
                Height = 512;
                FontScale = 3.0;
                Thickness = 2;
            });
        }
    }
} 