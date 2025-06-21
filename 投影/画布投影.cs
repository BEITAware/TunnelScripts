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
    [RevivalScript(Name = "画布投影", Author = "AI", Description = "将多个投影图像映射到参考画布", Category = "投影与混合", Color = "#9370DB", Version = "1.0")]
    public class CanvasProjectionScript : RevivalScriptBase
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

            int w = canvas.Width;
            int h = canvas.Height;
            Mat result = canvas.Clone();
            using var warped = WarpToCanvas(src, w, h, _param);
            BlendToCanvas(result, warped);
            return new() { ["f32bmp"] = result };
        }

        private static Mat WarpToCanvas(Mat src, int canvasW, int canvasH, ProjectionParam p)
        {
            double scale = p.ScalePercent / 100.0;
            double d = p.Distance;
            double halfW = src.Width * scale / 2.0;
            double halfH = src.Height * scale / 2.0;

            // Rotation matrices
            double rx = Deg2Rad(p.ThetaX), ry = Deg2Rad(p.ThetaY), rz = Deg2Rad(p.ThetaZ);
            var R = ComposeRotation(rx, ry, rz);

            double[][] corners3 = new[]
            {
                new[]{ -halfW, -halfH, d },
                new[]{  halfW, -halfH, d },
                new[]{  halfW,  halfH, d },
                new[]{ -halfW,  halfH, d }
            };

            Point2f[] dst2D = new Point2f[4];
            for (int i = 0; i < 4; i++)
            {
                var c = corners3[i];
                var v = new[] { c[0], c[1], 0 }; // relative
                var rot = Mul(R, v);
                double x3 = rot[0] + p.Tx;
                double y3 = rot[1] + p.Ty;
                double z3 = rot[2] + d;
                if (z3 <= 1) z3 = 1; // clamp
                dst2D[i] = new Point2f((float)(d * x3 / z3 + canvasW / 2.0), (float)(d * y3 / z3 + canvasH / 2.0));
            }
            Point2f[] srcPts = {
                new Point2f(0,0), new Point2f(src.Width-1,0), new Point2f(src.Width-1,src.Height-1), new Point2f(0,src.Height-1)
            };
            using var M = Cv2.GetPerspectiveTransform(srcPts, dst2D);
            var warped = new Mat();
            Cv2.WarpPerspective(src, warped, M, new OpenCvSharp.Size(canvasW, canvasH), InterpolationFlags.Linear, BorderTypes.Constant);
            return warped;
        }

        private static void BlendToCanvas(Mat canvas, Mat overlay)
        {
            bool srcHasAlpha = overlay.Channels() == 4;
            bool dstHasAlpha = canvas.Channels() == 4;

            if (!srcHasAlpha)
            {
                // 无 Alpha 简单覆盖非零像素
                using var gray = new Mat();
                Cv2.CvtColor(overlay, gray, ColorConversionCodes.BGR2GRAY);
                using var mask = new Mat();
                Cv2.Threshold(gray, mask, 0, 1.0, ThresholdTypes.Binary);
                overlay.CopyTo(canvas, mask);
                return;
            }

            // 分离通道
            Cv2.Split(overlay, out Mat[] srcCh); // 0,1,2,3
            Cv2.Split(canvas, out Mat[] dstCh); // size 3 or 4

            var alpha = srcCh[3]; // CV_32F 0-1
            var invAlpha = new Mat();
            Cv2.Subtract(new Scalar(1.0), alpha, invAlpha); // 1-alpha

            for (int c = 0; c < 3; c++)
            {
                var blended = new Mat();
                Cv2.Multiply(srcCh[c], alpha, blended);
                var dstPart = new Mat();
                Cv2.Multiply(dstCh[c], invAlpha, dstPart);
                Cv2.Add(blended, dstPart, dstCh[c]);
                blended.Dispose(); dstPart.Dispose();
            }

            if (dstHasAlpha)
            {
                var newA = new Mat();
                Cv2.Multiply(dstCh[3], invAlpha, dstCh[3]); // dstA * (1-a)
                Cv2.Add(dstCh[3], alpha, dstCh[3]); // + a
                newA.Dispose();
            }

            using var merged = new Mat();
            Cv2.Merge(dstCh, merged);
            merged.CopyTo(canvas);

            foreach (var m in srcCh) m.Dispose();
            foreach (var m in dstCh) m.Dispose();
            alpha.Dispose(); invAlpha.Dispose();
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
        private static double[] Mul(double[][] M, double[] v) => new[]
        {
            M[0][0]*v[0] + M[0][1]*v[1] + M[0][2]*v[2],
            M[1][0]*v[0] + M[1][1]*v[1] + M[1][2]*v[2],
            M[2][0]*v[0] + M[2][1]*v[1] + M[2][2]*v[2]
        };
        #endregion

        #region 内部类型
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
                if (d.TryGetValue(nameof(ThetaX), out var v)) p.ThetaX = Convert.ToDouble(v);
                if (d.TryGetValue(nameof(ThetaY), out var v2)) p.ThetaY = Convert.ToDouble(v2);
                if (d.TryGetValue(nameof(ThetaZ), out var v3)) p.ThetaZ = Convert.ToDouble(v3);
                if (d.TryGetValue(nameof(Tx), out var v4)) p.Tx = Convert.ToDouble(v4);
                if (d.TryGetValue(nameof(Ty), out var v5)) p.Ty = Convert.ToDouble(v5);
                if (d.TryGetValue(nameof(Distance), out var v6)) p.Distance = Convert.ToDouble(v6);
                if (d.TryGetValue(nameof(ScalePercent), out var v7)) p.ScalePercent = Convert.ToDouble(v7);
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
                if (Script is RevivalScriptBase rsb)
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
