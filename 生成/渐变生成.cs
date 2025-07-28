using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Data;
using Tunnel_Next.Services.Scripting;
using System.Collections.ObjectModel;
using TNX_Scripts.Controls;
using System.ComponentModel;
using System.Collections.Specialized;
using System.Text.Json;

namespace TNX_Scripts.ScriptPrototypes
{
    [TunnelExtensionScript(
        Name = "渐变生成器",
        Author = "BEITAware",
        Description = "生成线性/径向渐变图像，支持自定义渐变档位 (HSL)",
        Version = "1.0",
        Category = "图像生成",
        Color = "#77AAFF")]
    public class GradientGeneratorScript : TunnelExtensionScriptBase
    {
        // Use shared enum
        public enum GradientType { Linear = 0, Radial = 1 }

        public class GradientStopData : IGradientStopData, System.ComponentModel.INotifyPropertyChanged
        {
            private double _offset, _h, _s, _l;
            public double Offset { get => _offset; set { _offset = value; OnPropertyChanged(nameof(Offset)); } }
            public double H { get => _h; set { _h = value; OnPropertyChanged(nameof(H)); } }
            public double S { get => _s; set { _s = value; OnPropertyChanged(nameof(S)); } }
            public double L { get => _l; set { _l = value; OnPropertyChanged(nameof(L)); } }
            public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
            private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
        }

        [ScriptParameter(DisplayName = "渐变类型", Description = "线性或径向", Order = 0)]
        public GradientType Type { get; set; } = GradientType.Linear;

        [ScriptParameter(DisplayName = "宽度", Description = "生成图像宽度 (像素)", Order = 1)]
        public int Width { get; set; } = 1024;

        [ScriptParameter(DisplayName = "高度", Description = "生成图像高度 (像素)", Order = 2)]
        public int Height { get; set; } = 512;

        private ObservableCollection<IGradientStopData> _stops;
        public ObservableCollection<IGradientStopData> Stops
        {
            get => _stops;
            set
            {
                if (_stops != value)
                {
                    if (_stops != null)
                    {
                        _stops.CollectionChanged -= Stops_CollectionChanged;
                        foreach (var stop in _stops)
                            stop.PropertyChanged -= Stop_PropertyChanged;
                    }

                    _stops = value;

                    if (_stops != null)
                    {
                        _stops.CollectionChanged += Stops_CollectionChanged;
                        foreach (var stop in _stops)
                            stop.PropertyChanged += Stop_PropertyChanged;
                    }
                    OnParameterChanged(nameof(Stops), _stops);
                }
            }
        }

        public GradientGeneratorScript()
        {
            Stops = new ObservableCollection<IGradientStopData>
            {
                new GradientStopData { Offset = 0, H = 0,   S = 1, L = 0.5},
                new GradientStopData { Offset = 1, H = 240, S = 1, L = 0.5}
            };
        }

        private void Stop_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            OnParameterChanged(nameof(Stops), Stops);
        }

        private void Stops_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (IGradientStopData item in e.OldItems)
                    item.PropertyChanged -= Stop_PropertyChanged;
            }
            if (e.NewItems != null)
            {
                foreach (IGradientStopData item in e.NewItems)
                    item.PropertyChanged += Stop_PropertyChanged;
            }
            OnParameterChanged(nameof(Stops), sender);
        }

        public override Dictionary<string, PortDefinition> GetInputPorts() => new();

        public override Dictionary<string, PortDefinition> GetOutputPorts() => new()
        {
            ["Output"] = new PortDefinition("F32bmp", false, "渐变图像")
        };

        public override Dictionary<string, object> Process(Dictionary<string, object> inputs, IScriptContext context)
        {
            int w = Math.Max(1, Width);
            int h = Math.Max(1, Height);

            // 构建 WPF Brush
            GradientBrush brush;
            var gradientStops = new GradientStopCollection();
            foreach (var gs in Stops.OrderBy(s => s.Offset))
            {
                gradientStops.Add(new GradientStop(HslToColor(gs.H, gs.S, gs.L), gs.Offset));
            }

            if (Type == GradientType.Linear)
            {
                var lin = new LinearGradientBrush(gradientStops)
                {
                    StartPoint = new System.Windows.Point(0, 0),
                    EndPoint = new System.Windows.Point(1, 0)
                };
                brush = lin;
            }
            else
            {
                brush = new RadialGradientBrush(gradientStops)
                {
                    RadiusX = 0.5,
                    RadiusY = 0.5,
                    Center = new System.Windows.Point(0.5, 0.5),
                    GradientOrigin = new System.Windows.Point(0.5, 0.5)
                };
            }
            brush.Freeze();

            // Render
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(brush, null, new System.Windows.Rect(0, 0, w, h));
            }
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);

            int stride = w * 4;
            var pixels = new byte[h * stride];
            rtb.CopyPixels(pixels, stride, 0);

            using var mat8 = new Mat(h, w, MatType.CV_8UC4);
            Marshal.Copy(pixels, 0, mat8.Data, pixels.Length);
            using var mat8bgr = new Mat();
            Cv2.CvtColor(mat8, mat8bgr, ColorConversionCodes.BGRA2BGR);
            var mat32 = new Mat();
            mat8bgr.ConvertTo(mat32, MatType.CV_32FC3, 1.0 / 255.0);

            return new Dictionary<string, object>
            {
                ["Output"] = mat32
            };
        }

        private Color HslToColor(double h, double s, double l)
        {
            h = h / 360.0;
            double r = l, g = l, b = l;
            if (s != 0)
            {
                double q = l < 0.5 ? l * (1 + s) : (l + s - l * s);
                double p = 2 * l - q;
                r = HueToRGB(p, q, h + 1.0 / 3);
                g = HueToRGB(p, q, h);
                b = HueToRGB(p, q, h - 1.0 / 3);
            }
            return Color.FromScRgb(1f, (float)r, (float)g, (float)b);
        }

        private double HueToRGB(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 1.0 / 2) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }

        public override FrameworkElement CreateParameterControl()
        {
            var mainPanel = new StackPanel { Margin = new Thickness(5) };

            // 加载资源与背景
            var resources = new ResourceDictionary();
            var resourcePaths = new[]
            {
                "/Tunnel-Next;component/Resources/ScriptsControls/SharedBrushes.xaml",
                "/Tunnel-Next;component/Resources/ScriptsControls/LabelStyles.xaml",
                "/Tunnel-Next;component/Resources/ScriptsControls/ComboBoxStyles.xaml",
                "/Tunnel-Next;component/Resources/ScriptsControls/DropdownStyles.xaml",
                "/Tunnel-Next;component/Resources/ScriptsControls/TextBoxIdleStyles.xaml",
                "/Tunnel-Next;component/Resources/ScriptsControls/SliderStyles.xaml"
            };
            foreach (var path in resourcePaths)
            {
                try { resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(path, UriKind.Relative) }); } catch { }
            }
            if (resources.Contains("Layer_2")) mainPanel.Background = resources["Layer_2"] as Brush;

            var vm = CreateViewModel() as GradientViewModel;
            mainPanel.DataContext = vm;

            var title = new Label { Content = "渐变生成器" };
            if (resources.Contains("TitleLabelStyle")) title.Style = resources["TitleLabelStyle"] as Style;
            mainPanel.Children.Add(title);

            // Type
            mainPanel.Children.Add(CreateLabel("渐变类型:", resources));
            var cbType = new ComboBox { ItemsSource = Enum.GetValues(typeof(TNX_Scripts.Controls.GradientType)), Margin = new Thickness(0, 2, 0, 10) };
            if (resources.Contains("DefaultComboBoxStyle")) cbType.Style = resources["DefaultComboBoxStyle"] as Style;
            cbType.SetBinding(ComboBox.SelectedItemProperty, new Binding(nameof(GradientViewModel.Type)) { Mode = BindingMode.TwoWay });
            mainPanel.Children.Add(cbType);

            // Width / Height
            mainPanel.Children.Add(CreateLabel("宽度:", resources));
            var tbW = new TextBox { Margin = new Thickness(0, 2, 0, 10) };
            if (resources.Contains("DefaultTextBoxStyle")) tbW.Style = resources["DefaultTextBoxStyle"] as Style;
            tbW.SetBinding(TextBox.TextProperty, new Binding(nameof(GradientViewModel.Width)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            mainPanel.Children.Add(tbW);

            mainPanel.Children.Add(CreateLabel("高度:", resources));
            var tbH = new TextBox { Margin = new Thickness(0, 2, 0, 10) };
            if (resources.Contains("DefaultTextBoxStyle")) tbH.Style = resources["DefaultTextBoxStyle"] as Style;
            tbH.SetBinding(TextBox.TextProperty, new Binding(nameof(GradientViewModel.Height)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            mainPanel.Children.Add(tbH);

            // Gradient editor control
            mainPanel.Children.Add(CreateLabel("渐变档位:", resources));
            
            var editorContainer = new Grid();
            var ge = new GradientEditor 
            { 
                Margin = new Thickness(0,2,0,10),
                HorizontalAlignment = HorizontalAlignment.Stretch // Stretch to fill container
            };
            ge.SetBinding(GradientEditor.StopsProperty, new Binding(nameof(GradientViewModel.Stops)) { Mode = BindingMode.OneWay });
            ge.SetBinding(GradientEditor.GradientTypeProperty, new Binding(nameof(GradientViewModel.Type)) { Mode = BindingMode.TwoWay });
            editorContainer.Children.Add(ge);

            mainPanel.Children.Add(editorContainer);

            return mainPanel;
        }

        private DataTemplate BuildStopTemplate(ResourceDictionary resources)
        {
            // Create template in code: each item row contains: Slider Offset + Hue slider + Sat slider + Light slider + Delete button
            var dt = new DataTemplate(typeof(GradientStopData));
            var spFactory = new FrameworkElementFactory(typeof(StackPanel));
            spFactory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

            void AddSlider(string bindingPath, double min, double max, double width)
            {
                var sliderFactory = new FrameworkElementFactory(typeof(Slider));
                sliderFactory.SetValue(Slider.MinimumProperty, min);
                sliderFactory.SetValue(Slider.MaximumProperty, max);
                sliderFactory.SetValue(Slider.WidthProperty, width);
                sliderFactory.SetValue(Slider.MarginProperty, new Thickness(2));
                sliderFactory.SetBinding(Slider.ValueProperty, new Binding(bindingPath) { Mode = BindingMode.TwoWay });
                spFactory.AppendChild(sliderFactory);
            }

            // Offset slider 0-1
            AddSlider(nameof(GradientStopData.Offset), 0, 1, 80);
            // H slider 0-360
            AddSlider(nameof(GradientStopData.H), 0, 360, 100);
            // S slider 0-1
            AddSlider(nameof(GradientStopData.S), 0, 1, 80);
            // L slider 0-1
            AddSlider(nameof(GradientStopData.L), 0, 1, 80);

            // Delete button
            var btnFactory = new FrameworkElementFactory(typeof(Button));
            btnFactory.SetValue(Button.ContentProperty, "×");
            btnFactory.SetValue(Button.WidthProperty, 25.0);
            btnFactory.SetValue(Button.MarginProperty, new Thickness(2));
            btnFactory.AddHandler(Button.ClickEvent, new RoutedEventHandler((s, e) =>
            {
                if (s is FrameworkElement fe && fe.DataContext is GradientStopData gsd && fe.TemplatedParent is FrameworkElement tf && tf.DataContext is GradientViewModel gvm)
                {
                    gvm.RemoveStop(gsd);
                }
            }));
            spFactory.AppendChild(btnFactory);

            dt.VisualTree = spFactory;
            return dt;
        }

        private Label CreateLabel(string content, ResourceDictionary resources)
        {
            var label = new Label { Content = content, Margin = new Thickness(0, 10, 0, 2) };
            if (resources.Contains("DefaultLabelStyle")) label.Style = resources["DefaultLabelStyle"] as Style;
            return label;
        }

        public override IScriptViewModel CreateViewModel() => new GradientViewModel(this);

        public override async Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
        {
            await Task.CompletedTask;
        }

        public override Dictionary<string, object> SerializeParameters()
        {
            // 使用纯数值数组 [offset, h, s, l] 以确保外部简化序列化保持数据
            var stopsList = Stops.Select(s => new object[] { s.Offset, s.H, s.S, s.L }).ToList();

            var result = new Dictionary<string, object>(4, StringComparer.OrdinalIgnoreCase)
            {
                [nameof(Type)]   = (int)Type,
                [nameof(Width)]  = Width,
                [nameof(Height)] = Height,
                ["Stops"]       = stopsList
            };

            Console.WriteLine($"[GradientGenerator] SerializeParameters => {JsonSerializer.Serialize(result)}");
            return result;
        }

        public override void DeserializeParameters(Dictionary<string, object> data)
        {
            Console.WriteLine($"[GradientGenerator] DeserializeParameters <= {JsonSerializer.Serialize(data)}");
            if (data is null) return;

            // 1. 渐变类型
            if (data.TryGetValue(nameof(Type), out var tRaw) &&
                double.TryParse(tRaw?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var tVal))
            {
                var old = Type;
                Type = Enum.IsDefined(typeof(GradientType), (int)tVal) ? (GradientType)(int)tVal : old;
                if (Type != old) OnParameterChanged(nameof(Type), Type);
            }

            // 2. 尺寸
            if (data.TryGetValue(nameof(Width), out var wRaw) &&
                double.TryParse(wRaw?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var wVal))
            {
                int newW = (int)Math.Max(1, wVal);
                if (newW != Width)
                {
                    Width = newW;
                    OnParameterChanged(nameof(Width), Width);
                }
            }
            if (data.TryGetValue(nameof(Height), out var hRaw) &&
                double.TryParse(hRaw?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var hVal))
            {
                int newH = (int)Math.Max(1, hVal);
                if (newH != Height)
                {
                    Height = newH;
                    OnParameterChanged(nameof(Height), Height);
                }
            }

            // 3. Stops
            if (data.TryGetValue("Stops", out var stopsRaw) && stopsRaw is System.Collections.IEnumerable enumerable)
            {
                Stops.Clear();

                foreach (var obj in enumerable)
                {
                    // 1. 假设是纯数值数组
                    if (obj is System.Collections.IEnumerable numArr && !(obj is string))
                    {
                        var vals = numArr.Cast<object>().Select(o => Convert.ToDouble(o, CultureInfo.InvariantCulture)).ToList();
                        if (vals.Count >= 4)
                        {
                            Stops.Add(new GradientStopData { Offset = vals[0], H = vals[1], S = vals[2], L = vals[3] });
                            continue;
                        }
                    }
                    // 2. 字典或其它结构
                    if (obj is Dictionary<string, object> dict)
                    {
                        TryAddStopFromDict(dict);
                        continue;
                    }

                    var converted = ConvertUnknownStop(obj);
                    if (converted != null)
                        TryAddStopFromDict(converted);
                }

                Console.WriteLine($"[GradientGenerator] Stops count after deserialize: {Stops.Count}");
                OnParameterChanged(nameof(Stops), Stops);
            }

            // --- helper local functions ---
            Dictionary<string, object>? ConvertUnknownStop(object stopObj)
            {
                try
                {
                    // Case: JsonElement
                    if (stopObj is JsonElement je)
                    {
                        if (je.ValueKind == JsonValueKind.Object)
                        {
                            var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(je.GetRawText());
                            return dict;
                        }
                        else if (je.ValueKind == JsonValueKind.Array)
                        {
                            // Fallthrough to generic IEnumerable handling below
                            stopObj = je.EnumerateArray().Select(el => (object)el).ToList();
                        }
                    }

                    if (stopObj is System.Collections.IDictionary idic)
                    {
                        var d = new Dictionary<string, object>();
                        foreach (System.Collections.DictionaryEntry de in idic)
                        {
                            d[de.Key.ToString()!] = de.Value!;
                        }
                        return d;
                    }

                    if (stopObj is System.Collections.IEnumerable ie)
                    {
                        var d = new Dictionary<string, object>(4, StringComparer.OrdinalIgnoreCase);
                        foreach (var item in ie)
                        {
                            if (item == null) continue;
                            string? key = null; object? val = null;

                            // Possible KeyValuePair-like
                            var kvType = item.GetType();
                            if (kvType.GetProperty("Key") != null && kvType.GetProperty("Value") != null)
                            {
                                key = kvType.GetProperty("Key")?.GetValue(item)?.ToString();
                                val = kvType.GetProperty("Value")?.GetValue(item);
                            }
                            else if (item is System.Collections.IEnumerable kvEnum)
                            {
                                var arr = kvEnum.Cast<object?>().ToArray();
                                if (arr.Length >= 2)
                                {
                                    key = arr[0]?.ToString();
                                    val = arr[1];
                                }
                            }

                            if (!string.IsNullOrEmpty(key) && val != null)
                                d[key] = val;
                        }
                        if (d.Count > 0) return d;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GradientGenerator] ConvertUnknownStop failed: {ex.Message}");
                }
                return null;
            }

            void TryAddStopFromDict(Dictionary<string, object> d)
            {
                try
                {
                    double off = Convert.ToDouble(d["Offset"], CultureInfo.InvariantCulture);
                    double h   = Convert.ToDouble(d["H"],      CultureInfo.InvariantCulture);
                    double s   = Convert.ToDouble(d["S"],      CultureInfo.InvariantCulture);
                    double l   = Convert.ToDouble(d["L"],      CultureInfo.InvariantCulture);

                    Stops.Add(new GradientStopData { Offset = off, H = h, S = s, L = l });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GradientGenerator] Invalid stop entry: {ex.Message}");
                }
            }
        }
    }

    public class GradientViewModel : ScriptViewModelBase
    {
        private GradientGeneratorScript Grad => (GradientGeneratorScript)Script;

        public GradientViewModel(GradientGeneratorScript script) : base(script)
        {
        }

        public TNX_Scripts.Controls.GradientType Type
        {
            get => (TNX_Scripts.Controls.GradientType)Grad.Type;
            set
            {
                if (Grad.Type != (GradientGeneratorScript.GradientType)value)
                {
                    var old = Grad.Type;
                    Grad.Type = (GradientGeneratorScript.GradientType)value;
                    OnPropertyChanged();
                    NotifyParameterChanged(nameof(Type), value);
                }
            }
        }

        public int Width
        {
            get => Grad.Width;
            set
            {
                if (Grad.Width != value)
                {
                    Grad.Width = Math.Max(1, value);
                    OnPropertyChanged();
                    NotifyParameterChanged(nameof(Width), value);
                }
            }
        }

        public int Height
        {
            get => Grad.Height;
            set
            {
                if (Grad.Height != value)
                {
                    Grad.Height = Math.Max(1, value);
                    OnPropertyChanged();
                    NotifyParameterChanged(nameof(Height), value);
                }
            }
        }

        public ObservableCollection<IGradientStopData> Stops => Grad.Stops;

        public void AddStop()
        {
            var nd = new GradientGeneratorScript.GradientStopData
            {
                Offset = 0.5,
                H = 120,
                S = 1,
                L = 0.5
            };
            Stops.Add(nd);
            OnPropertyChanged(nameof(Stops));
            NotifyParameterChanged("Stops", Stops);
        }

        public void RemoveStop(IGradientStopData stop)
        {
            if (Stops.Contains(stop))
            {
                Stops.Remove(stop);
                OnPropertyChanged(nameof(Stops));
                NotifyParameterChanged("Stops", Stops);
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
            await Grad.OnParameterChangedAsync(parameterName, oldValue, newValue);
        }

        public override ScriptValidationResult ValidateParameter(string parameterName, object value)
        {
            switch (parameterName)
            {
                case nameof(Width):
                case nameof(Height):
                    if (value is int i && i >= 1 && i <= 8192) return new ScriptValidationResult(true);
                    return new ScriptValidationResult(false, "尺寸范围 1-8192");
            }
            return new ScriptValidationResult(true);
        }

        public override Dictionary<string, object> GetParameterData()
        {
            return new Dictionary<string, object>
            {
                [nameof(Type)] = (int)Type,
                [nameof(Width)] = Width,
                [nameof(Height)] = Height,
                ["Stops"] = Stops
            };
        }

        public override async Task SetParameterDataAsync(Dictionary<string, object> data)
        {
            await RunOnUIThreadAsync(() =>
            {
                if (data.TryGetValue(nameof(Type), out var t))
                {
                    try { Type = (TNX_Scripts.Controls.GradientType)Convert.ToInt32(t, CultureInfo.InvariantCulture); }
                    catch { }
                }
                if (data.TryGetValue(nameof(Width), out var w))
                {
                    try { Width = Math.Max(1, Convert.ToInt32(w, CultureInfo.InvariantCulture)); }
                    catch { }
                }
                if (data.TryGetValue(nameof(Height), out var h))
                {
                    try { Height = Math.Max(1, Convert.ToInt32(h, CultureInfo.InvariantCulture)); }
                    catch { }
                }
                if (data.TryGetValue("Stops", out var st) && st is System.Collections.IEnumerable lst)
                {
                    Stops.Clear();
                    foreach (var obj in lst)
                    {
                        if (obj is IGradientStopData gsd)
                        {
                            Stops.Add(gsd);
                        }
                        else if (obj is Dictionary<string, object> d) // From deserialization
                        {
                            Stops.Add(new GradientGeneratorScript.GradientStopData
                            {
                                Offset = Convert.ToDouble(d["Offset"], CultureInfo.InvariantCulture),
                                H      = Convert.ToDouble(d["H"],      CultureInfo.InvariantCulture),
                                S      = Convert.ToDouble(d["S"],      CultureInfo.InvariantCulture),
                                L      = Convert.ToDouble(d["L"],      CultureInfo.InvariantCulture)
                            });
                        }
                    }
                    OnPropertyChanged(nameof(Stops));
                }
            });
        }

        public override async Task ResetToDefaultAsync()
        {
            await RunOnUIThreadAsync(() =>
            {
                Type = (TNX_Scripts.Controls.GradientType)GradientGeneratorScript.GradientType.Linear;
                Width = 1024;
                Height = 512;
                Stops.Clear();
                Stops.Add(new GradientGeneratorScript.GradientStopData { Offset = 0, H = 0, S = 1, L = 0.5 });
                Stops.Add(new GradientGeneratorScript.GradientStopData { Offset = 1, H = 240, S = 1, L = 0.5 });
                OnPropertyChanged(nameof(Stops));
            });
        }
    }
} 