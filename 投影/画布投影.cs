using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using OpenCvSharp;
using Tunnel_Next.Services.Scripting;

namespace Tunnel_Next.Scripts
{
    [TunnelExtensionScript(Name = "画布投影", Author = "AI", Description = "将多个投影图像映射到参考画布", Category = "投影与混合", Color = "#9370DB", Version = "1.0")]
    public class CanvasProjectionScript : TunnelExtensionScriptBase
    {
        private ProjectionParam _param = new ProjectionParam();

        #region 端口
        public override Dictionary<string, PortDefinition> GetInputPorts() => new()
        {
            ["canvas"] = new PortDefinition("f32bmp", false, "基础画布"),
            ["f32bmp"] = new PortDefinition("f32bmp", false, "投影图像")
        };

        public override Dictionary<string, PortDefinition> GetOutputPorts() => new()
        {
            ["f32bmp"] = new PortDefinition("f32bmp", false, "合成结果")
        };
        #endregion

        #region 处理
        public override Dictionary<string, object> Process(Dictionary<string, object> inputs, IScriptContext ctx)
        {
            if (!inputs.TryGetValue("canvas", out var baseObj) || baseObj is not Mat canvas || canvas.Empty())
                return new();

            if (!inputs.TryGetValue("f32bmp", out var imgObj) || imgObj is not Mat src || src.Empty())
                return new() { ["f32bmp"] = canvas.Clone() };

            using var layer = To32FC4(src);
            Premultiply(layer);
            using var warped = WarpToCanvas(layer, canvas.Width, canvas.Height, _param);
            using var blended = To32FC4(canvas);
            BlendToCanvas(blended, warped);
            using var converted = new Mat();
            if (canvas.Channels() == 1)
                Cv2.CvtColor(blended, converted, ColorConversionCodes.BGRA2GRAY);
            else if (canvas.Channels() == 3)
                Cv2.CvtColor(blended, converted, ColorConversionCodes.BGRA2BGR);

            var result = new Mat();
            double range = canvas.Depth() == MatType.CV_8U ? 255 : canvas.Depth() == MatType.CV_16U ? 65535 : 1;
            (canvas.Channels() == 4 ? blended : converted).ConvertTo(result, canvas.Type(), range);
            return new() { ["f32bmp"] = result };
        }

        private static Mat WarpToCanvas(Mat src, int canvasW, int canvasH, ProjectionParam p)
        {
            double scale = p.ScalePercent / 100.0;
            double distance = p.Distance;
            if (!double.IsFinite(distance) || !double.IsFinite(scale) || distance <= 0 || scale <= 0)
                throw new ArgumentOutOfRangeException("Distance / ScalePercent");

            // Map pixel centers through the rotated plane, then divide by camera depth.
            var r = ComposeRotation(Deg2Rad(p.ThetaX), Deg2Rad(p.ThetaY), Deg2Rad(p.ThetaZ));
            double cx = (src.Width - 1) * 0.5, cy = (src.Height - 1) * 0.5;
            double destX = (canvasW - 1) * 0.5, destY = (canvasH - 1) * 0.5;
            double zx = scale * r[2][0], zy = scale * r[2][1];
            double xx = distance * scale * r[0][0] + destX * zx;
            double xy = distance * scale * r[0][1] + destX * zy;
            double yx = distance * scale * r[1][0] + destY * zx;
            double yy = distance * scale * r[1][1] + destY * zy;
            using var matrix = new Mat(3, 3, MatType.CV_64F);
            matrix.SetArray(new double[] {
                xx, xy, distance * (p.Tx + destX) - xx * cx - xy * cy,
                yx, yy, distance * (p.Ty + destY) - yx * cx - yy * cy,
                zx, zy, distance - zx * cx - zy * cy
            });
            double determinant = Cv2.Determinant(matrix);
            var size = new OpenCvSharp.Size(canvasW, canvasH);
            if (!double.IsFinite(determinant))
                throw new ArgumentException("投影参数必须为有限数值。");
            if (Math.Abs(determinant) <= 1e-10 * distance * distance * distance * scale * scale)
                return new Mat(size, src.Type(), Scalar.All(0));

            var output = new Mat();
            Cv2.WarpPerspective(src, output, matrix, size, InterpolationFlags.Linear, BorderTypes.Constant);
            return output;
        }

        private static Mat To32FC4(Mat src)
        {
            double scale = src.Depth() == MatType.CV_8U ? 1.0 / 255 :
                           src.Depth() == MatType.CV_16U ? 1.0 / 65535 : 1.0;
            if (src.Channels() == 4)
            {
                var result = new Mat();
                src.ConvertTo(result, MatType.CV_32F, scale);
                return result;
            }
            var conversion = src.Channels() switch
            {
                1 => ColorConversionCodes.GRAY2BGRA,
                3 => ColorConversionCodes.BGR2BGRA,
                _ => throw new ArgumentException("仅支持灰度、BGR 和 BGRA 图像。")
            };
            using var floats = new Mat();
            src.ConvertTo(floats, MatType.CV_32F, scale);
            var bgra = new Mat();
            Cv2.CvtColor(floats, bgra, conversion);
            return bgra;
        }

        private static void Premultiply(Mat image)
        {
            unsafe
            {
                for (int y = 0; y < image.Rows; y++)
                {
                    var row = (float*)image.Ptr(y);
                    for (int x = 0; x < image.Cols; x++, row += 4)
                    {
                        float alpha = Math.Clamp(row[3], 0f, 1f);
                        row[3] = alpha;
                        for (int c = 0; c < 3; c++) row[c] *= alpha;
                    }
                }
            }
        }

        // Warp premultiplied colors, then return the host's straight-alpha pixels.
        private static unsafe void BlendToCanvas(Mat canvas, Mat overlay)
        {
            for (int y = 0; y < canvas.Rows; y++)
            {
                var dst = (float*)canvas.Ptr(y);
                var src = (float*)overlay.Ptr(y);
                for (int x = 0; x < canvas.Cols; x++, dst += 4, src += 4)
                {
                    float alpha = src[3];
                    if (alpha <= 0) continue;
                    float retained = Math.Clamp(dst[3], 0f, 1f) * (1 - alpha);
                    float outputAlpha = alpha + retained;
                    for (int c = 0; c < 3; c++) dst[c] = (src[c] + dst[c] * retained) / outputAlpha;
                    dst[3] = outputAlpha;
                }
            }
        }
        #endregion

        #region UI / ViewModel
        public override FrameworkElement CreateParameterControl()
        {
            var root = new StackPanel { Margin = new Thickness(5) };
            var res = new ResourceDictionary();
            var paths = new[]
            {
                "/Tunnel-Next;component/Resources/ScriptsControls/SharedBrushes.xaml",
                "/Tunnel-Next;component/Resources/ScriptsControls/LabelStyles.xaml",
                "/Tunnel-Next;component/Resources/ScriptsControls/SliderStyles.xaml",
                "/Tunnel-Next;component/Resources/ScriptsControls/ScriptButtonStyles.xaml"
            };
            foreach (var p in paths) { try { res.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(p, UriKind.Relative) }); } catch { } }
            root.Resources = res;

            var vm = new CanvasProjectionViewModel(this);
            root.DataContext = vm;

            var title = new Label { Content = "画布投影" };
            if (res.Contains("TitleLabelStyle")) title.Style = res["TitleLabelStyle"] as Style;
            root.Children.Add(title);

            var paramPanel = new StackPanel { Margin=new Thickness(0,5,0,0)};
            paramPanel.DataContext = vm.ParamVM;
            root.Children.Add(paramPanel);

            void AddSlider(string label,string prop,double min,double max)
            {
                var sp=new StackPanel();
                var lbl=new Label{Content=label};
                if(res.Contains("DefaultLabelStyle")) lbl.Style=res["DefaultLabelStyle"] as Style;
                var sld=new Slider{Minimum=min,Maximum=max};
                if(res.Contains("DefaultSliderStyle")) sld.Style=res["DefaultSliderStyle"] as Style;
                sld.SetBinding(Slider.ValueProperty,new Binding(prop){Mode=BindingMode.TwoWay});
                sp.Children.Add(lbl);
                sp.Children.Add(sld);
                paramPanel.Children.Add(sp);
            }

            AddSlider("X轴旋转", nameof(ProjectionParamVM.ThetaX), -180, 180);
            AddSlider("Y轴旋转", nameof(ProjectionParamVM.ThetaY), -180, 180);
            AddSlider("Z轴旋转", nameof(ProjectionParamVM.ThetaZ), -180, 180);
            AddSlider("X轴平移", nameof(ProjectionParamVM.Tx), -300, 300);
            AddSlider("Y轴平移", nameof(ProjectionParamVM.Ty), -300, 300);
            AddSlider("距离", nameof(ProjectionParamVM.Distance), 500, 3000);
            AddSlider("缩放%", nameof(ProjectionParamVM.ScalePercent), 50, 150);

            return root;
        }

        private DataTemplate BuildParamTemplate(ResourceDictionary res)
        {
            // Build in code
            var stackFactory = new FrameworkElementFactory(typeof(StackPanel));
            stackFactory.SetValue(StackPanel.MarginProperty, new Thickness(0,5,0,5));

            void AddSlider(string label, string bind)
            {
                var lbl = new FrameworkElementFactory(typeof(Label));
                lbl.SetValue(Label.ContentProperty, label);
                if (res.Contains("DefaultLabelStyle")) lbl.SetValue(Label.StyleProperty, res["DefaultLabelStyle"]);
                stackFactory.AppendChild(lbl);

                var sld = new FrameworkElementFactory(typeof(Slider));
                sld.SetValue(Slider.MinimumProperty, -180.0);
                sld.SetValue(Slider.MaximumProperty, 180.0);
                if (res.Contains("DefaultSliderStyle")) sld.SetValue(Slider.StyleProperty, res["DefaultSliderStyle"]);
                sld.SetBinding(Slider.ValueProperty, new Binding(bind){Mode=BindingMode.TwoWay});
                stackFactory.AppendChild(sld);
            }
            AddSlider("X轴旋转", "ThetaX");
            AddSlider("Y轴旋转", "ThetaY");
            AddSlider("Z轴旋转", "ThetaZ");
            AddSlider("X轴平移", "Tx");
            AddSlider("Y轴平移", "Ty");
            AddSlider("距离", "Distance");
            AddSlider("缩放%", "ScalePercent");

            var dt = new DataTemplate { VisualTree = stackFactory };
            return dt;
        }

        public override IScriptViewModel CreateViewModel() => new CanvasProjectionViewModel(this);
        public override Task OnParameterChangedAsync(string n, object o, object v) => Task.CompletedTask;
        public override Dictionary<string, object> SerializeParameters()
        {
            return _param.ToDict();
        }
        public override void DeserializeParameters(Dictionary<string, object> data)
        {
            _param = ProjectionParam.FromDict(data);
        }
        #endregion

        #region Math helpers
        private static double Deg2Rad(double d) => d * Math.PI / 180.0;
        private static double[][] ComposeRotation(double ax, double ay, double az)
        {
            double cx = Math.Cos(ax), sx = Math.Sin(ax);
            double cy = Math.Cos(ay), sy = Math.Sin(ay);
            double cz = Math.Cos(az), sz = Math.Sin(az);
            double[][] Rx = { new[]{1.0,0,0}, new[]{0,cx,-sx}, new[]{0,sx,cx} };
            double[][] Ry = { new[]{cy,0,sy}, new[]{0,1.0,0}, new[]{-sy,0,cy} };
            double[][] Rz = { new[]{cz,-sz,0}, new[]{sz,cz,0}, new[]{0,0,1.0} };
            return Mul(Rz, Mul(Ry, Rx));
        }
        private static double[][] Mul(double[][] A, double[][] B)
        {
            var r = new double[3][] { new double[3], new double[3], new double[3] };
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    r[i][j] = A[i][0] * B[0][j] + A[i][1] * B[1][j] + A[i][2] * B[2][j];
            return r;
        }
        #endregion

        #region 内部类型
        private static double ReadNumber(Dictionary<string, object> values, string key, double fallback)
        {
            if (!values.TryGetValue(key, out var raw) || raw == null) return fallback;
            if (raw is System.Text.Json.JsonElement element)
                return element.ValueKind == System.Text.Json.JsonValueKind.Number && element.TryGetDouble(out var value) && double.IsFinite(value) ? value : fallback;
            return double.TryParse(raw.ToString(), out var parsed) && double.IsFinite(parsed) ? parsed : fallback;
        }

        private class ProjectionParam
        {
            public double ThetaX = 0, ThetaY = 0, ThetaZ = 0;
            public double Tx = 0, Ty = 0;
            public double Distance = 1000;
            public double ScalePercent = 100;

            public Dictionary<string, object> ToDict() => new()
            {
                [nameof(ThetaX)] = ThetaX, [nameof(ThetaY)] = ThetaY, [nameof(ThetaZ)] = ThetaZ,
                [nameof(Tx)] = Tx, [nameof(Ty)] = Ty, [nameof(Distance)] = Distance, [nameof(ScalePercent)] = ScalePercent
            };
            public static ProjectionParam FromDict(Dictionary<string, object> d)
            {
                var p = new ProjectionParam();
                p.ThetaX = ReadNumber(d, nameof(ThetaX), p.ThetaX);
                p.ThetaY = ReadNumber(d, nameof(ThetaY), p.ThetaY);
                p.ThetaZ = ReadNumber(d, nameof(ThetaZ), p.ThetaZ);
                p.Tx = ReadNumber(d, nameof(Tx), p.Tx);
                p.Ty = ReadNumber(d, nameof(Ty), p.Ty);
                p.Distance = ReadNumber(d, nameof(Distance), p.Distance);
                p.ScalePercent = ReadNumber(d, nameof(ScalePercent), p.ScalePercent);
                return p;
            }
        }

        private class ProjectionParamVM : ScriptViewModelBase
        {
            private readonly ProjectionParam _p;
            public ProjectionParamVM(CanvasProjectionScript script, ProjectionParam p):base(script){_p=p;}
            private void Set(string name, ref double field, double val)
            {
                if (Math.Abs(field - val) < 1e-4) return;
                var old = field;
                field = val;
                OnPropertyChanged(name);
                if (Script is TunnelExtensionScriptBase rsb)
                    rsb.OnParameterChanged(name, val);
            }
            public double ThetaX { get => _p.ThetaX; set => Set(nameof(ThetaX), ref _p.ThetaX, value); }
            public double ThetaY { get => _p.ThetaY; set => Set(nameof(ThetaY), ref _p.ThetaY, value); }
            public double ThetaZ { get => _p.ThetaZ; set => Set(nameof(ThetaZ), ref _p.ThetaZ, value); }
            public double Tx { get => _p.Tx; set => Set(nameof(Tx), ref _p.Tx, value); }
            public double Ty { get => _p.Ty; set => Set(nameof(Ty), ref _p.Ty, value); }
            public double Distance { get => _p.Distance; set => Set(nameof(Distance), ref _p.Distance, value); }
            public double ScalePercent { get => _p.ScalePercent; set => Set(nameof(ScalePercent), ref _p.ScalePercent, value); }
            public override Task OnParameterChangedAsync(string p, object o, object n)=>Task.CompletedTask;
            public override ScriptValidationResult ValidateParameter(string p, object v)=> new(true);
            public override Dictionary<string, object> GetParameterData() => _p.ToDict();
            public override Task SetParameterDataAsync(Dictionary<string, object> d) { return Task.CompletedTask; }
            public override Task ResetToDefaultAsync() { return Task.CompletedTask; }
        }

        private class CanvasProjectionViewModel : ScriptViewModelBase
        {
            private readonly CanvasProjectionScript _script;
            public CanvasProjectionViewModel(CanvasProjectionScript s):base(s){_script=s; ParamVM = new ProjectionParamVM(_script,_script._param);}            
            public ProjectionParamVM ParamVM { get; }
            public override Task OnParameterChangedAsync(string p, object o, object n)=>Task.CompletedTask;
            public override ScriptValidationResult ValidateParameter(string p, object v)=> new(true);
            public override Dictionary<string, object> GetParameterData()=>ParamVM.GetParameterData();
            public override Task SetParameterDataAsync(Dictionary<string, object> d)=>Task.CompletedTask;
            public override Task ResetToDefaultAsync()=>Task.CompletedTask;
        }
        #endregion
    }
}
