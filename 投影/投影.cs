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

            return new() { ["f32bmp"] = Project(src) };
        }

        private bool IsDefault() => Math.Abs(ThetaX) < 1e-6 && Math.Abs(ThetaY) < 1e-6 && Math.Abs(ThetaZ) < 1e-6 &&
                                     Math.Abs(Tx) < 1e-6 && Math.Abs(Ty) < 1e-6 &&
                                     Math.Abs(Distance - 1000) < 1e-6 && Math.Abs(ScalePercent - 100) < 1e-6;

        private Mat Project(Mat src)
        {
            double scale = ScalePercent / 100.0;
            double distance = Distance;
            if (!double.IsFinite(distance) || !double.IsFinite(scale) || distance <= 0 || scale <= 0)
                throw new ArgumentOutOfRangeException("Distance / ScalePercent");

            // Map pixel centers through the rotated plane, then divide by camera depth.
            var r = ComposeRotation(Deg2Rad(ThetaX), Deg2Rad(ThetaY), Deg2Rad(ThetaZ));
            double cx = (src.Width - 1) * 0.5, cy = (src.Height - 1) * 0.5;
            double destX = (src.Width - 1) * 0.5, destY = (src.Height - 1) * 0.5;
            double zx = scale * r[2][0], zy = scale * r[2][1];
            double xx = distance * scale * r[0][0] + destX * zx;
            double xy = distance * scale * r[0][1] + destX * zy;
            double yx = distance * scale * r[1][0] + destY * zx;
            double yy = distance * scale * r[1][1] + destY * zy;
            using var matrix = new Mat(3, 3, MatType.CV_64F);
            matrix.SetArray(new double[] {
                xx, xy, distance * (Tx + destX) - xx * cx - xy * cy,
                yx, yy, distance * (Ty + destY) - yx * cx - yy * cy,
                zx, zy, distance - zx * cx - zy * cy
            });
            double determinant = Cv2.Determinant(matrix);
            var size = new OpenCvSharp.Size(src.Width, src.Height);
            if (!double.IsFinite(determinant))
                throw new ArgumentException("投影参数必须为有限数值。");
            if (Math.Abs(determinant) <= 1e-10 * distance * distance * distance * scale * scale)
                return new Mat(size, src.Type(), Scalar.All(0));

            var output = new Mat();
            Cv2.WarpPerspective(src, output, matrix, size, InterpolationFlags.Linear, BorderTypes.Replicate);
            return output;
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
        public override void DeserializeParameters(Dictionary<string, object> data)
        {
            ThetaX = ReadNumber(data, nameof(ThetaX), ThetaX);
            ThetaY = ReadNumber(data, nameof(ThetaY), ThetaY);
            ThetaZ = ReadNumber(data, nameof(ThetaZ), ThetaZ);
            Tx = ReadNumber(data, nameof(Tx), Tx);
            Ty = ReadNumber(data, nameof(Ty), Ty);
            Distance = ReadNumber(data, nameof(Distance), Distance);
            ScalePercent = ReadNumber(data, nameof(ScalePercent), ScalePercent);
        }
        private static double ReadNumber(Dictionary<string, object> values, string key, double fallback)
        {
            if (!values.TryGetValue(key, out var raw) || raw == null) return fallback;
            if (raw is System.Text.Json.JsonElement element)
                return element.ValueKind == System.Text.Json.JsonValueKind.Number && element.TryGetDouble(out var value) && double.IsFinite(value) ? value : fallback;
            return double.TryParse(raw.ToString(), out var parsed) && double.IsFinite(parsed) ? parsed : fallback;
        }


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