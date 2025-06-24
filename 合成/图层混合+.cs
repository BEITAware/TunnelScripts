using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Tunnel_Next.Services.Scripting;
using OpenCvSharp;
using CvSize = OpenCvSharp.Size;
using CvRect = OpenCvSharp.Rect;

// GPU-Style recursive layer blending script (Normal mode only)
[RevivalScript(
    Name = "图层混合+",
    Author = "BEITAware",
    Description = "基准画布 + 递归 Normal 混合",
    Version = "2.0",
    Category = "图像处理",
    Color = "#191970")]
public class GpuLayerBlendScript : RevivalScriptBase
{
    // ---------------- Parameters ----------------
    [ScriptParameter(DisplayName = "全局不透明度", Description = "所有图层的全局不透明度 0-1")]
    public float GlobalOpacity { get; set; } = 1.0f;

    [ScriptParameter(DisplayName = "尊重 Alpha", Description = "是否考虑输入图像的 Alpha 通道")]
    public bool RespectAlpha { get; set; } = true;

    [ScriptParameter(DisplayName = "并行处理", Description = "对大图像使用多线程")]
    public bool EnableParallelProcessing { get; set; } = true;

    [ScriptParameter(DisplayName = "逆序混合", Description = "是否反转图层顺序")]
    public bool ReverseOrder { get; set; } = false;

    // ---------------- Port definitions ----------------
    public override Dictionary<string, PortDefinition> GetInputPorts() => new()
    {
        ["canvas"] = new("f32bmp", false, "基准画布"),
        ["Layer1"] = new("f32bmp", true, "图像图层 (自动扩展)")
    };

    public override Dictionary<string, PortDefinition> GetOutputPorts() => new()
    {
        ["Output"] = new("f32bmp", false, "混合输出")
    };

    // ---------------- Processing ----------------
    public override Dictionary<string, object> Process(Dictionary<string, object> inputs, IScriptContext context)
    {
        var result = new Dictionary<string, object>();

        // 动态输入统计
        int linkCount = inputs.Count;                          // 连接端口数量
        var linkNames = inputs.Keys.ToList();                  // 连接端口名称列表

        System.Diagnostics.Debug.WriteLine($"GpuLayerBlendScript links ({linkCount}): {string.Join(",", linkNames)}");

        if (!inputs.TryGetValue("canvas", out var canvasObj) || canvasObj is not Mat canvas || canvas.Empty())
            return result; // 没有画布则返回空

        var working = To32FC4(canvas);

        // 收集并排序所有图层（排除 canvas）
        var layers = inputs.Where(k => k.Key != "canvas" && k.Value is Mat m && !m.Empty())
                            .OrderBy(k => k.Key)
                            .Select(k => (Key: k.Key, Mat: To32FC4((Mat)k.Value)))
                            .ToList();

        // 记录端口名称以便 UI 动态生成
        _lastLinkNames = inputs.Keys.Where(k => k != "canvas").OrderBy(k => k).ToList();

        if (!layers.Any()) { result["Output"] = working; return result; }
        if (ReverseOrder) layers.Reverse();

        var final = BlendRecursive(working, layers, 0);
        result["Output"] = final;
        return result;
    }

    private Mat BlendRecursive(Mat baseCanvas, IList<(string Key, Mat Mat)> layers, int idx)
    {
        if (idx >= layers.Count) return ApplyOpacity(baseCanvas);

        var lt = GetLayerTransform(layers[idx].Key);
        using var transformed = TransformLayer(layers[idx].Mat, baseCanvas.Size(), lt);
        using var blended = BlendNormal(baseCanvas, transformed);
        baseCanvas.Dispose();
        return BlendRecursive(blended.Clone(), layers, idx + 1);
    }

    private static Mat TransformLayer(Mat src, CvSize canvasSize, LayerTransform lt)
    {
        // 缩放
        float sx = lt.ScaleX <= 0 ? 1f : lt.ScaleX;
        float sy = lt.ScaleY <= 0 ? 1f : lt.ScaleY;
        int newW = Math.Max(1, (int)Math.Round(src.Width * sx));
        int newH = Math.Max(1, (int)Math.Round(src.Height * sy));
        using var scaled = new Mat();
        Cv2.Resize(src, scaled, new CvSize(newW, newH), 0, 0, InterpolationFlags.Linear);

        // 创建目标画布
        var dst = new Mat(canvasSize, MatType.CV_32FC4, Scalar.All(0));

        int offsetX = (int)Math.Round(lt.OffsetX);
        int offsetY = (int)Math.Round(lt.OffsetY);

        // 处理负偏移，计算 ROI
        int srcX = Math.Max(0, -offsetX);
        int srcY = Math.Max(0, -offsetY);
        int dstX = Math.Max(0, offsetX);
        int dstY = Math.Max(0, offsetY);

        int copyW = Math.Min(scaled.Width - srcX, canvasSize.Width - dstX);
        int copyH = Math.Min(scaled.Height - srcY, canvasSize.Height - dstY);

        if (copyW <= 0 || copyH <= 0)
        {
            // 完全在画布外
            return dst;
        }

        var roiSrc = new CvRect(srcX, srcY, copyW, copyH);
        var roiDst = new CvRect(dstX, dstY, copyW, copyH);
        scaled[roiSrc].CopyTo(dst[roiDst]);
        return dst;
    }

    private static string GetBaseLayerName(string key)
    {
        if (!key.StartsWith("Layer")) return key;
        int i = 5;
        while (i < key.Length && char.IsDigit(key[i])) i++;
        return key.Substring(0, i);
    }

    private LayerTransform GetLayerTransform(string key)
    {
        if (_layerParams.TryGetValue(key, out var lt)) return lt;

        var baseName = GetBaseLayerName(key);
        if (_layerParams.TryGetValue(baseName, out lt)) return lt;

        // 兼容 "Layer2_0" 等带后缀的动态端口名称
        foreach (var kv in _layerParams)
        {
            if (key.StartsWith(kv.Key, StringComparison.Ordinal))
                return kv.Value;
        }

        // 尝试将 key 解析成层序号
        if (key.StartsWith("Layer", StringComparison.Ordinal))
        {
            var s = key.Substring(5);
            var parts = s.Split('_');
            if (int.TryParse(parts[0], out var idx) && idx >= 1)
            {
                // UI 组命名与实际端口可能错位，尝试不同别名
                string alt1 = $"Layer{idx}";
                if (_layerParams.TryGetValue(alt1, out lt)) return lt;
                if (idx > 1)
                {
                    string alt2 = $"Layer1_{idx-1}";
                    if (_layerParams.TryGetValue(alt2, out lt)) return lt;
                }
            }
        }
        return new LayerTransform();
    }

    // ---------------- Helpers ----------------
    private static Mat To32FC4(Mat src)
    {
        if (src.Type() == MatType.CV_32FC4) return src.Clone();
        var tmp = new Mat();
        if (src.Channels() == 3)
            Cv2.CvtColor(src, tmp, ColorConversionCodes.BGR2BGRA);
        else
            src.CopyTo(tmp);
        var dst = new Mat();
        tmp.ConvertTo(dst, MatType.CV_32FC4, src.Depth() == MatType.CV_8U ? 1.0 / 255.0 : 1.0);
        tmp.Dispose();
        return dst;
    }

    private static Mat AdjustToCanvas(Mat src, CvSize canvasSize)
    {
        var dst = new Mat(canvasSize, MatType.CV_32FC4, Scalar.All(0));
        var copyW = Math.Min(src.Width, canvasSize.Width);
        var copyH = Math.Min(src.Height, canvasSize.Height);
        var roiSrc = new CvRect(0, 0, copyW, copyH);
        var roiDst = new CvRect(0, 0, copyW, copyH);
        src[roiSrc].CopyTo(dst[roiDst]);
        return dst;
    }

    private Mat BlendNormal(Mat dst, Mat src)
    {
        var outMat = new Mat(dst.Size(), dst.Type());
        dst = EnsureContinuous(dst);
        src = EnsureContinuous(src);
        outMat = EnsureContinuous(outMat);
        unsafe
        {
            var dstPtr = (float*)dst.DataPointer;
            var srcPtr = (float*)src.DataPointer;
            var outPtr = (float*)outMat.DataPointer;
            int pixels = dst.Rows * dst.Cols;

            void BlendPixel(int i)
            {
                int idx = i * 4;
                float dstA = dstPtr[idx + 3];
                float srcA = srcPtr[idx + 3];
                float effA = (RespectAlpha ? srcA : 1f) * GlobalOpacity;
                effA = Clamp(effA, 0f, 1f);
                if (effA < 1e-6f)
                {
                    for (int c = 0; c < 4; c++) outPtr[idx + c] = dstPtr[idx + c];
                    return;
                }
                float outA = effA + dstA * (1 - effA);
                float inv = 1f / outA;
                for (int c = 0; c < 3; c++)
                {
                    float srcC = srcPtr[idx + c];
                    float dstC = dstPtr[idx + c];
                    outPtr[idx + c] = (srcC * effA + dstC * dstA * (1 - effA)) * inv;
                }
                outPtr[idx + 3] = outA;
            }

            if (EnableParallelProcessing && pixels > 10000)
                Parallel.For(0, pixels, BlendPixel);
            else
                for (int i = 0; i < pixels; i++) BlendPixel(i);
        }
        return outMat;
    }

    private static Mat EnsureContinuous(Mat m) => m.IsContinuous() ? m : m.Clone();

    private Mat ApplyOpacity(Mat input)
    {
        if (Math.Abs(GlobalOpacity - 1f) < 1e-6f) return input;
        var tmp = new Mat();
        input.ConvertTo(tmp, input.Type(), GlobalOpacity);
        input.Dispose();
        return tmp;
    }

    // ---------------- Misc overrides ----------------
    public override Dictionary<string, object> SerializeParameters()
    {
        var dict = new Dictionary<string, object>
        {
            [nameof(GlobalOpacity)] = GlobalOpacity,
            [nameof(RespectAlpha)] = RespectAlpha,
            [nameof(EnableParallelProcessing)] = EnableParallelProcessing,
            [nameof(ReverseOrder)] = ReverseOrder
        };

        foreach (var kv in _layerParams)
        {
            dict[$"{kv.Key}_OffsetX"] = kv.Value.OffsetX;
            dict[$"{kv.Key}_OffsetY"] = kv.Value.OffsetY;
            dict[$"{kv.Key}_ScaleX"] = kv.Value.ScaleX;
            dict[$"{kv.Key}_ScaleY"] = kv.Value.ScaleY;
        }
        return dict;
    }

    public override void DeserializeParameters(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(GlobalOpacity), out var g) && float.TryParse(g.ToString(), out var gv))
            GlobalOpacity = Clamp(gv, 0f, 1f);
        if (data.TryGetValue(nameof(RespectAlpha), out var r) && bool.TryParse(r.ToString(), out var rv))
            RespectAlpha = rv;
        if (data.TryGetValue(nameof(EnableParallelProcessing), out var p) && bool.TryParse(p.ToString(), out var pv))
            EnableParallelProcessing = pv;
        if (data.TryGetValue(nameof(ReverseOrder), out var q) && bool.TryParse(q.ToString(), out var qv))
            ReverseOrder = qv;

        foreach (var kv in data)
        {
            if (!kv.Key.StartsWith("Layer")) continue;

            var parts = kv.Key.Split('_');
            if (parts.Length != 2) continue;

            var layerKey = parts[0];
            var prop = parts[1];

            if (!_layerParams.TryGetValue(layerKey, out var lt))
            {
                lt = new LayerTransform();
                _layerParams[layerKey] = lt;
            }

            float val = 0f;
            if (!float.TryParse(kv.Value.ToString(), out val)) continue;

            switch (prop)
            {
                case "OffsetX": lt.OffsetX = val; break;
                case "OffsetY": lt.OffsetY = val; break;
                case "ScaleX": lt.ScaleX = val; break;
                case "ScaleY": lt.ScaleY = val; break;
            }
        }
    }

    public override IScriptViewModel CreateViewModel() => new DummyVm(this);

    public override FrameworkElement CreateParameterControl()
    {
        try
        {
            var panel = new StackPanel { Margin = new Thickness(5) };

            // 标题
            panel.Children.Add(new TextBlock
            {
                Text = "GPU 图层混合+ 控制面板",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 6)
            });

            // 逆序混合
            var chkReverse = new CheckBox { Content = "逆序混合", IsChecked = ReverseOrder };
            chkReverse.Checked += (_, __) => { ReverseOrder = true; OnParameterChanged(nameof(ReverseOrder), true); };
            chkReverse.Unchecked += (_, __) => { ReverseOrder = false; OnParameterChanged(nameof(ReverseOrder), false); };
            panel.Children.Add(chkReverse);

            // 层级控制生成器
            void AddLayerControls(string layerKey)
            {
                var expander = new Expander { Header = $"{layerKey} 参数", IsExpanded = layerKey == "Layer1", Margin = new Thickness(0, 4, 0, 4) };
                var inner = new StackPanel { Margin = new Thickness(4, 2, 0, 2) };

                Slider MakeSlider(string name, double min, double max, double initial, Action<double> act)
                {
                    var sp = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
                    sp.Children.Add(new TextBlock { Text = name, FontSize = 11 });
                    var sld = new Slider { Minimum = min, Maximum = max, Value = initial };
                    sld.ValueChanged += (_, e) => act(e.NewValue);
                    sp.Children.Add(sld);
                    inner.Children.Add(sp);
                    return sld;
                }

                var current = GetLayerTransform(layerKey);
                MakeSlider("位置 X", -1000, 1000, current.OffsetX, v => SetLayerParam(layerKey, LayerParamType.OffsetX, (float)v));
                MakeSlider("位置 Y", -1000, 1000, current.OffsetY, v => SetLayerParam(layerKey, LayerParamType.OffsetY, (float)v));
                MakeSlider("缩放 X", 0.1, 3, current.ScaleX, v => SetLayerParam(layerKey, LayerParamType.ScaleX, (float)v));
                MakeSlider("缩放 Y", 0.1, 3, current.ScaleY, v => SetLayerParam(layerKey, LayerParamType.ScaleY, (float)v));

                expander.Content = inner;
                panel.Children.Add(expander);
            }

            // 根据最近一次处理得到的端口名称动态生成 UI
            var targetKeys = _lastLinkNames.Any()
                ? _lastLinkNames
                : new List<string> { "Layer1" };

            foreach (var key in targetKeys)
                AddLayerControls(key);

            return panel;
        }
        catch (Exception ex)
        {
            // 若UI创建失败，退化为简单文本提示，避免脚本面板完全空白
            return new TextBlock { Text = $"参数面板加载失败: {ex.Message}" };
        }
    }

    // ---------------- Layer 参数存储 ----------------
    private enum LayerParamType { OffsetX, OffsetY, ScaleX, ScaleY }

    private class LayerTransform
    {
        public float OffsetX;
        public float OffsetY;
        public float ScaleX = 1f;
        public float ScaleY = 1f;
    }

    private readonly Dictionary<string, LayerTransform> _layerParams = new();

    private void SetLayerParam(string layerKey, LayerParamType type, float value)
    {
        if (!_layerParams.TryGetValue(layerKey, out var lt))
        {
            lt = new LayerTransform();
            _layerParams[layerKey] = lt;
        }

        switch (type)
        {
            case LayerParamType.OffsetX: lt.OffsetX = value; break;
            case LayerParamType.OffsetY: lt.OffsetY = value; break;
            case LayerParamType.ScaleX: lt.ScaleX = value; break;
            case LayerParamType.ScaleY: lt.ScaleY = value; break;
        }

        // 同时写入基名，保证别名都能取到相同 transform
        var baseName = GetBaseLayerName(layerKey);
        _layerParams[baseName] = lt;

        OnParameterChanged($"{layerKey}_{type}", value);
    }

    public override Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue) => Task.CompletedTask;

    private class DummyVm : ScriptViewModelBase
    {
        public DummyVm(IRevivalScript script) : base(script) { }

        public override Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue) => Task.CompletedTask;
        public override ScriptValidationResult ValidateParameter(string parameterName, object value) => new ScriptValidationResult(true);
        public override Dictionary<string, object> GetParameterData() => new();
        public override Task SetParameterDataAsync(Dictionary<string, object> data) => Task.CompletedTask;
        public override Task ResetToDefaultAsync() => Task.CompletedTask;
    }

    private static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);

    // 缓存最近一次 Process 收到的输入端口名称，用于动态生成参数UI
    private List<string> _lastLinkNames = new();
} 