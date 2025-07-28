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
using System.Runtime.CompilerServices;

namespace Tunnel_Next.Scripts
{
    [TunnelExtensionScript(Name = "3D 投影", Author = "AI", Description = "3D 旋转/平移+透视投影", Version = "1.1", Category = "投影与混合", Color = "#4169E1")]
    public class ProjectionScript : TunnelExtensionScriptBase
    {
        static ProjectionScript()
        {
            try
            {
                Cv2.SetNumThreads(Environment.ProcessorCount);
                Cv2.SetUseOptimized(true);
            }
            catch { /* 若 OpenCV 不支持，则忽略 */ }
        }

        #region 参数 (与 Python 原型一致)

        [ScriptParameter(DisplayName = "X轴旋转 (俯仰)", Description = "-45~45°", Order = 0)]
        public double ThetaX { get; set; } = 0;
        [ScriptParameter(DisplayName = "Y轴旋转 (偏航)", Description = "-45~45°", Order = 1)]
        public double ThetaY { get; set; } = 0;
        [ScriptParameter(DisplayName = "Z轴旋转 (翻滚)", Description = "-180~180°", Order = 2)]
        public double ThetaZ { get; set; } = 0;
        [ScriptParameter(DisplayName = "X轴平移", Description = "-300~300", Order = 3)]
        public double Tx { get; set; } = 0;
        [ScriptParameter(DisplayName = "Y轴平移", Description = "-300~300", Order = 4)]
        public double Ty { get; set; } = 0;
        [ScriptParameter(DisplayName = "摄像机距离", Description = "500~3000", Order = 5)]
        public double Distance { get; set; } = 1000;
        [ScriptParameter(DisplayName = "缩放 (%)", Description = "50~150", Order = 6)]
        public double ScalePercent { get; set; } = 100;

        #endregion

        #region 端口
        public override Dictionary<string, PortDefinition> GetInputPorts() => new() { ["f32bmp"] = new("f32bmp", false, "输入图像") };
        public override Dictionary<string, PortDefinition> GetOutputPorts() => new() { ["f32bmp"] = new("f32bmp", false, "输出图像") };
        #endregion

        #region 处理核心
        public override Dictionary<string, object> Process(Dictionary<string, object> inputs, IScriptContext ctx)
        {
            if (!inputs.TryGetValue("f32bmp", out var obj) || obj is not Mat src || src.Empty())
                return new();

            if (IsDefault())
                return new() { ["f32bmp"] = src.Clone() };

            try
            {
                return new() { ["f32bmp"] = Project(src) };
            }
            catch
            {
                return new() { ["f32bmp"] = src.Clone() };
            }
        }

        private bool IsDefault() => Math.Abs(ThetaX) < 1e-6 && Math.Abs(ThetaY) < 1e-6 && Math.Abs(ThetaZ) < 1e-6 &&
                                     Math.Abs(Tx) < 1e-6 && Math.Abs(Ty) < 1e-6 &&
                                     Math.Abs(Distance - 1000) < 1e-6 && Math.Abs(ScalePercent - 100) < 1e-6;

        private Mat? _lastH;

        private Mat Project(Mat input)
        {
            int w = input.Width;
            int h = input.Height;
            double scale = ScalePercent / 100.0;
            double d = Distance;

            // 旋转矩阵 (Z->Y->X)
            var R = ComposeRotation(Deg2Rad(ThetaX), Deg2Rad(ThetaY), Deg2Rad(ThetaZ));

            // 四角 3D 坐标
            double halfW = w * scale * 0.5;
            double halfH = h * scale * 0.5;
            var corners = new[]
            {
                new[] { -halfW, -halfH, d },
                new[] {  halfW, -halfH, d },
                new[] {  halfW,  halfH, d },
                new[] { -halfW,  halfH, d }
            };

            var dst2D = new Point2f[4];
            for (int i = 0; i < 4; i++)
            {
                var c = corners[i];
                var centered = new[] { c[0], c[1], 0 }; // 减去 d 再加回来可省略
                var rotated = Mul(R, centered);
                double x3 = rotated[0] + Tx;
                double y3 = rotated[1] + Ty;
                double z3 = rotated[2] + d; // z 位移

                // 若 z<=0, 采用近似投影到远处，避免 NaN
                if (z3 <= 1)
                {
                    double signX = x3 >= 0 ? 1 : -1;
                    double signY = y3 >= 0 ? 1 : -1;
                    dst2D[i] = new Point2f((float)(w * (1.5 + signX)), (float)(h * (1.5 + signY)));
                }
                else
                {
                    float x2 = (float)(d * x3 / z3 + w / 2.0);
                    float y2 = (float)(d * y3 / z3 + h / 2.0);
                    dst2D[i] = new Point2f(x2, y2);
                }
            }

            if (!ValidateQuad(dst2D, w, h))
            {
                if (_lastH != null && !_lastH.Empty())
                {
                    var fallback = new Mat();
                    Cv2.WarpPerspective(input, fallback, _lastH, new OpenCvSharp.Size(w, h), InterpolationFlags.Linear, BorderTypes.Replicate);
                    return fallback;
                }
            }

            var srcPts = new[] { new Point2f(0, 0), new Point2f(w - 1, 0), new Point2f(w - 1, h - 1), new Point2f(0, h - 1) };
            using var M = Cv2.GetPerspectiveTransform(srcPts, dst2D);
            _lastH?.Dispose();
            _lastH = M.Clone();

            var outMat = new Mat();
            Cv2.WarpPerspective(input, outMat, M, new OpenCvSharp.Size(w, h), InterpolationFlags.Linear, BorderTypes.Replicate);

            if (outMat.Depth() == MatType.CV_32F || outMat.Depth() == MatType.CV_64F)
            {
                Cv2.Min(outMat, 1.0, outMat);
                Cv2.Max(outMat, 0.0, outMat);
            }
            return outMat;
        }

        private static bool ValidateQuad(Point2f[] pts, int w, int h)
        {
            // 计算四边长度
            double minEdge = double.MaxValue;
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                double len = Math.Sqrt(Math.Pow(pts[i].X - pts[j].X, 2) + Math.Pow(pts[i].Y - pts[j].Y, 2));
                minEdge = Math.Min(minEdge, len);
            }
            if (minEdge < 0.5) return false; // 接近点

            // 面积 (Shoelace)
            double area = 0.5 * Math.Abs(
                pts[0].X * (pts[1].Y - pts[3].Y) +
                pts[1].X * (pts[2].Y - pts[0].Y) +
                pts[2].X * (pts[3].Y - pts[1].Y) +
                pts[3].X * (pts[0].Y - pts[2].Y));

            return area > 10; // 仅确保不为零
        }

        #endregion

        #region 数学辅助
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double Deg2Rad(double d) => d * Math.PI / 180.0;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double[][] ComposeRotation(double ax, double ay, double az)
        {
            double cx = Math.Cos(ax), sx = Math.Sin(ax);
            double cy = Math.Cos(ay), sy = Math.Sin(ay);
            double cz = Math.Cos(az), sz = Math.Sin(az);
            var Rx = new[] { new[] {1.0,0,0}, new[]{0,cx,-sx}, new[]{0,sx,cx} };
            var Ry = new[] { new[] {cy,0,sy}, new[]{0,1.0,0}, new[]{-sy,0,cy} };
            var Rz = new[] { new[] {cz,-sz,0}, new[]{sz,cz,0}, new[]{0,0,1.0} };
            return Mul(Rz, Mul(Ry, Rx));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double[][] Mul(double[][] A, double[][] B)
        {
            var r = new double[3][] { new double[3], new double[3], new double[3] };
            for(int i=0;i<3;i++)
                for(int j=0;j<3;j++)
                    r[i][j] = A[i][0]*B[0][j] + A[i][1]*B[1][j] + A[i][2]*B[2][j];
            return r;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double[] Mul(double[][] M, double[] v) => new[]{ M[0][0]*v[0] + M[0][1]*v[1] + M[0][2]*v[2],
                                                                      M[1][0]*v[0] + M[1][1]*v[1] + M[1][2]*v[2],
                                                                      M[2][0]*v[0] + M[2][1]*v[1] + M[2][2]*v[2] };
        #endregion

        #region UI/VM (简化版本，按钮样式引用保证一致)

        public override FrameworkElement CreateParameterControl()
        {
            var panel = new StackPanel { Margin = new Thickness(5) };
            var res = new ResourceDictionary();
            var paths = new[]
            {
                "/Tunnel-Next;component/Resources/ScriptsControls/SharedBrushes.xaml",
                "/Tunnel-Next;component/Resources/ScriptsControls/LabelStyles.xaml",
                "/Tunnel-Next;component/Resources/ScriptsControls/SliderStyles.xaml",
                "/Tunnel-Next;component/Resources/ScriptsControls/ScriptButtonStyles.xaml"
            };
            foreach (var p in paths) { try { res.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(p, UriKind.Relative) }); } catch { } }
            panel.Resources = res;

            var vm = new ProjectionVM(this);
            panel.DataContext = vm;

            var title = new Label { Content = "3D 投影" };
            if (res.Contains("TitleLabelStyle")) title.Style = res["TitleLabelStyle"] as Style;
            panel.Children.Add(title);

            // 滑块生成器
            void AddSlider(string name,string prop,double min,double max)
            {
                var sp = new StackPanel { Margin = new Thickness(0,5,0,0) };
                var lbl = new Label { Content = name };
                if (res.Contains("DefaultLabelStyle")) lbl.Style = res["DefaultLabelStyle"] as Style;
                var sld = new Slider { Minimum = min, Maximum = max };
                if (res.Contains("DefaultSliderStyle")) sld.Style = res["DefaultSliderStyle"] as Style;
                sld.SetBinding(Slider.ValueProperty, new Binding(prop){Mode=BindingMode.TwoWay});
                sp.Children.Add(lbl);
                sp.Children.Add(sld);
                panel.Children.Add(sp);
            }

            AddSlider("X轴旋转", nameof(vm.ThetaX), -80, 80);
            AddSlider("Y轴旋转", nameof(vm.ThetaY), -80, 80);
            AddSlider("Z轴旋转", nameof(vm.ThetaZ), -180, 180);
            AddSlider("X轴平移", nameof(vm.Tx), -300, 300);
            AddSlider("Y轴平移", nameof(vm.Ty), -300, 300);
            AddSlider("摄像机距离", nameof(vm.Distance), 500, 3000);
            AddSlider("缩放 (%)", nameof(vm.ScalePercent), 50, 150);

            var resetBtn = new Button { Content="重置", Margin = new Thickness(0,10,0,0) };
            if(res.Contains("SelectFileScriptButtonStyle")) resetBtn.Style = res["SelectFileScriptButtonStyle"] as Style;
            resetBtn.Click += async (_,__) => await vm.Reset();
            panel.Children.Add(resetBtn);
            return panel;
        }

        public override IScriptViewModel CreateViewModel() => new ProjectionVM(this);
        public override Task OnParameterChangedAsync(string p, object o, object n) => Task.CompletedTask;
        public override Dictionary<string, object> SerializeParameters() => new(){{nameof(ThetaX),ThetaX},{nameof(ThetaY),ThetaY},{nameof(ThetaZ),ThetaZ},{nameof(Tx),Tx},{nameof(Ty),Ty},{nameof(Distance),Distance},{nameof(ScalePercent),ScalePercent}};
        public override void DeserializeParameters(Dictionary<string, object> d){if(d.TryGetValue(nameof(ThetaX),out var v0))ThetaX=Convert.ToDouble(v0);if(d.TryGetValue(nameof(ThetaY),out var v1))ThetaY=Convert.ToDouble(v1);if(d.TryGetValue(nameof(ThetaZ),out var v2))ThetaZ=Convert.ToDouble(v2);if(d.TryGetValue(nameof(Tx),out var v3))Tx=Convert.ToDouble(v3);if(d.TryGetValue(nameof(Ty),out var v4))Ty=Convert.ToDouble(v4);if(d.TryGetValue(nameof(Distance),out var v5))Distance=Convert.ToDouble(v5);if(d.TryGetValue(nameof(ScalePercent),out var v6))ScalePercent=Convert.ToDouble(v6);}        

        public class ProjectionVM : ScriptViewModelBase
        {
            private ProjectionScript S => (ProjectionScript)Script;
            public ProjectionVM(ProjectionScript s):base(s){}
            public double ThetaX{get=>S.ThetaX;set=>Set(nameof(ThetaX),value,v=>S.ThetaX=v);}        public double ThetaY{get=>S.ThetaY;set=>Set(nameof(ThetaY),value,v=>S.ThetaY=v);}        public double ThetaZ{get=>S.ThetaZ;set=>Set(nameof(ThetaZ),value,v=>S.ThetaZ=v);}        public double Tx{get=>S.Tx;set=>Set(nameof(Tx),value,v=>S.Tx=v);}        public double Ty{get=>S.Ty;set=>Set(nameof(Ty),value,v=>S.Ty=v);}        public double Distance{get=>S.Distance;set=>Set(nameof(Distance),value,v=>S.Distance=v);}        public double ScalePercent{get=>S.ScalePercent;set=>Set(nameof(ScalePercent),value,v=>S.ScalePercent=v);}            
            private void Set(string n,double val,Action<double> assign){ if(Math.Abs(val-Convert.ToDouble(GetType().GetProperty(n)?.GetValue(this)))<1e-4) return; var old=GetType().GetProperty(n)?.GetValue(this); assign(val); OnPropertyChanged(n); NotifyParameterChanged(n,val); }
            private void NotifyParameterChanged(string p,object v){ if(S is TunnelExtensionScriptBase rsb) rsb.OnParameterChanged(p,v);}            
            public async Task Reset(){ThetaX=ThetaY=ThetaZ=Tx=Ty=0;Distance=1000;ScalePercent=100;await Task.CompletedTask;}
            public override Task OnParameterChangedAsync(string p, object o, object n)=>Task.CompletedTask;            public override ScriptValidationResult ValidateParameter(string p, object v)=>new(true);            public override Dictionary<string, object> GetParameterData()=>new(){[nameof(ThetaX)]=ThetaX,[nameof(ThetaY)]=ThetaY,[nameof(ThetaZ)]=ThetaZ,[nameof(Tx)]=Tx,[nameof(Ty)]=Ty,[nameof(Distance)]=Distance,[nameof(ScalePercent)]=ScalePercent};            public override Task SetParameterDataAsync(Dictionary<string, object> d)=>Task.CompletedTask;            public override Task ResetToDefaultAsync()=>Reset();
        }
        #endregion
    }
}