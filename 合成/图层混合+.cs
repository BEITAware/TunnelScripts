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
public class GpuLayerBlendScript : DynamicUIRevivalScriptBase
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

        // 检测连接变化并更新UI
        var currentLinkNames = inputs.Keys.Where(k => k != "canvas").OrderBy(k => k).ToList();
        var newToken = string.Join(",", currentLinkNames);

        if (newToken != _currentUIToken)
        {
            _currentUIToken = newToken;
            _lastLinkNames = currentLinkNames;

            // 创建连接信息并触发UI更新
            var connectionInfo = new ScriptConnectionInfo
            {
                ChangeType = ScriptConnectionChangeType.Connected,
                InputConnections = inputs.Keys.ToDictionary(k => k, k => inputs.ContainsKey(k) && inputs[k] != null)
            };

            // 在UI线程中请求更新
            Application.Current?.Dispatcher.BeginInvoke(() => RequestUIRefresh());
        }

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
            [nameof(ReverseOrder)] = ReverseOrder,
            ["_UIToken"] = _currentUIToken,
            ["_LastLinkNames"] = string.Join("|", _lastLinkNames)
        };

        // 序列化所有图层参数
        foreach (var kv in _layerParams)
        {
            dict[$"Layer_{kv.Key}_OffsetX"] = kv.Value.OffsetX;
            dict[$"Layer_{kv.Key}_OffsetY"] = kv.Value.OffsetY;
            dict[$"Layer_{kv.Key}_ScaleX"] = kv.Value.ScaleX;
            dict[$"Layer_{kv.Key}_ScaleY"] = kv.Value.ScaleY;
        }

        System.Diagnostics.Debug.WriteLine($"[图层混合+] 序列化参数: {dict.Count} 项");
        return dict;
    }

    public override void DeserializeParameters(Dictionary<string, object> data)
    {
        System.Diagnostics.Debug.WriteLine($"[图层混合+] 反序列化参数: {data.Count} 项");

        // 恢复基础参数
        if (data.TryGetValue(nameof(GlobalOpacity), out var g) && float.TryParse(g.ToString(), out var gv))
            GlobalOpacity = Clamp(gv, 0f, 1f);
        if (data.TryGetValue(nameof(RespectAlpha), out var r) && bool.TryParse(r.ToString(), out var rv))
            RespectAlpha = rv;
        if (data.TryGetValue(nameof(EnableParallelProcessing), out var p) && bool.TryParse(p.ToString(), out var pv))
            EnableParallelProcessing = pv;
        if (data.TryGetValue(nameof(ReverseOrder), out var q) && bool.TryParse(q.ToString(), out var qv))
            ReverseOrder = qv;

        // 恢复UI状态
        if (data.TryGetValue("_UIToken", out var token))
            _currentUIToken = token?.ToString() ?? "";
        if (data.TryGetValue("_LastLinkNames", out var linkNames))
        {
            var names = linkNames?.ToString() ?? "";
            _lastLinkNames = string.IsNullOrEmpty(names) ? new List<string>() : names.Split('|').ToList();
        }

        // 恢复图层参数
        _layerParams.Clear();
        foreach (var kv in data)
        {
            if (!kv.Key.StartsWith("Layer_")) continue;

            var parts = kv.Key.Split('_');
            if (parts.Length != 3) continue; // Layer_LayerKey_Property

            var layerKey = parts[1];
            var prop = parts[2];

            if (!_layerParams.TryGetValue(layerKey, out var lt))
            {
                lt = new LayerTransform();
                _layerParams[layerKey] = lt;
            }

            if (!float.TryParse(kv.Value.ToString(), out var val)) continue;

            switch (prop)
            {
                case "OffsetX": lt.OffsetX = val; break;
                case "OffsetY": lt.OffsetY = val; break;
                case "ScaleX": lt.ScaleX = val; break;
                case "ScaleY": lt.ScaleY = val; break;
            }
        }

        System.Diagnostics.Debug.WriteLine($"[图层混合+] 恢复了 {_layerParams.Count} 个图层参数");
    }

    public override IScriptViewModel CreateViewModel() => new LayerBlendViewModel(this);

    public override FrameworkElement CreateParameterControl()
    {
        try
        {
            _mainPanel = new StackPanel { Margin = new Thickness(5) };

            // 标题
            _mainPanel.Children.Add(new TextBlock
            {
                Text = "GPU 图层混合+ 控制面板",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 6)
            });

            // 全局参数区域
            AddGlobalControls(_mainPanel);

            // 动态图层控件区域
            _layerControlsPanel = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            _mainPanel.Children.Add(_layerControlsPanel);

            // 初始化图层控件
            var targetKeys = _lastLinkNames.Any()
                ? _lastLinkNames
                : new List<string> { "Layer1" };

            foreach (var key in targetKeys)
                AddLayerControls(_layerControlsPanel, key);

            return _mainPanel;
        }
        catch (Exception ex)
        {
            // 若UI创建失败，退化为简单文本提示，避免脚本面板完全空白
            return new TextBlock { Text = $"参数面板加载失败: {ex.Message}" };
        }
    }

    private void AddGlobalControls(StackPanel parent)
    {
        // 全局不透明度
        var globalOpacityPanel = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
        globalOpacityPanel.Children.Add(new TextBlock { Text = "全局不透明度", FontSize = 11 });
        var globalOpacitySlider = new Slider
        {
            Minimum = 0,
            Maximum = 1,
            Value = GlobalOpacity,
            TickFrequency = 0.1,
            IsSnapToTickEnabled = false
        };
        globalOpacitySlider.ValueChanged += (_, e) =>
        {
            GlobalOpacity = (float)e.NewValue;
            OnParameterChanged(nameof(GlobalOpacity), e.NewValue);
        };
        globalOpacityPanel.Children.Add(globalOpacitySlider);
        parent.Children.Add(globalOpacityPanel);

        // 尊重Alpha
        var chkRespectAlpha = new CheckBox { Content = "尊重 Alpha", IsChecked = RespectAlpha };
        chkRespectAlpha.Checked += (_, __) => { RespectAlpha = true; OnParameterChanged(nameof(RespectAlpha), true); };
        chkRespectAlpha.Unchecked += (_, __) => { RespectAlpha = false; OnParameterChanged(nameof(RespectAlpha), false); };
        parent.Children.Add(chkRespectAlpha);

        // 并行处理
        var chkParallel = new CheckBox { Content = "并行处理", IsChecked = EnableParallelProcessing };
        chkParallel.Checked += (_, __) => { EnableParallelProcessing = true; OnParameterChanged(nameof(EnableParallelProcessing), true); };
        chkParallel.Unchecked += (_, __) => { EnableParallelProcessing = false; OnParameterChanged(nameof(EnableParallelProcessing), false); };
        parent.Children.Add(chkParallel);

        // 逆序混合
        var chkReverse = new CheckBox { Content = "逆序混合", IsChecked = ReverseOrder };
        chkReverse.Checked += (_, __) => { ReverseOrder = true; OnParameterChanged(nameof(ReverseOrder), true); };
        chkReverse.Unchecked += (_, __) => { ReverseOrder = false; OnParameterChanged(nameof(ReverseOrder), false); };
        parent.Children.Add(chkReverse);
    }

    private void AddLayerControls(StackPanel parent, string layerKey)
    {
        var expander = new Expander
        {
            Header = $"{layerKey} 参数",
            IsExpanded = layerKey == "Layer1",
            Margin = new Thickness(0, 4, 0, 4)
        };
        var inner = new StackPanel { Margin = new Thickness(4, 2, 0, 2) };

        var current = GetLayerTransform(layerKey);

        // 位置 X
        var offsetXPanel = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
        offsetXPanel.Children.Add(new TextBlock { Text = "位置 X", FontSize = 11 });
        var offsetXSlider = new Slider { Minimum = -1000, Maximum = 1000, Value = current.OffsetX };
        offsetXSlider.ValueChanged += (_, e) => SetLayerParam(layerKey, LayerParamType.OffsetX, (float)e.NewValue);
        offsetXPanel.Children.Add(offsetXSlider);
        inner.Children.Add(offsetXPanel);

        // 位置 Y
        var offsetYPanel = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
        offsetYPanel.Children.Add(new TextBlock { Text = "位置 Y", FontSize = 11 });
        var offsetYSlider = new Slider { Minimum = -1000, Maximum = 1000, Value = current.OffsetY };
        offsetYSlider.ValueChanged += (_, e) => SetLayerParam(layerKey, LayerParamType.OffsetY, (float)e.NewValue);
        offsetYPanel.Children.Add(offsetYSlider);
        inner.Children.Add(offsetYPanel);

        // 缩放 X
        var scaleXPanel = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
        scaleXPanel.Children.Add(new TextBlock { Text = "缩放 X", FontSize = 11 });
        var scaleXSlider = new Slider { Minimum = 0.1, Maximum = 3, Value = current.ScaleX };
        scaleXSlider.ValueChanged += (_, e) => SetLayerParam(layerKey, LayerParamType.ScaleX, (float)e.NewValue);
        scaleXPanel.Children.Add(scaleXSlider);
        inner.Children.Add(scaleXPanel);

        // 缩放 Y
        var scaleYPanel = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
        scaleYPanel.Children.Add(new TextBlock { Text = "缩放 Y", FontSize = 11 });
        var scaleYSlider = new Slider { Minimum = 0.1, Maximum = 3, Value = current.ScaleY };
        scaleYSlider.ValueChanged += (_, e) => SetLayerParam(layerKey, LayerParamType.ScaleY, (float)e.NewValue);
        scaleYPanel.Children.Add(scaleYSlider);
        inner.Children.Add(scaleYPanel);

        expander.Content = inner;
        parent.Children.Add(expander);
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

        var oldValue = type switch
        {
            LayerParamType.OffsetX => lt.OffsetX,
            LayerParamType.OffsetY => lt.OffsetY,
            LayerParamType.ScaleX => lt.ScaleX,
            LayerParamType.ScaleY => lt.ScaleY,
            _ => 0f
        };

        switch (type)
        {
            case LayerParamType.OffsetX: lt.OffsetX = value; break;
            case LayerParamType.OffsetY: lt.OffsetY = value; break;
            case LayerParamType.ScaleX: lt.ScaleX = value; break;
            case LayerParamType.ScaleY: lt.ScaleY = value; break;
        }

        // 同时写入基名，保证别名都能取到相同 transform
        var baseName = GetBaseLayerName(layerKey);
        if (baseName != layerKey)
        {
            _layerParams[baseName] = lt;
        }

        // 触发参数变化事件
        var paramName = $"Layer_{layerKey}_{type}";
        OnParameterChanged(paramName, value);

        System.Diagnostics.Debug.WriteLine($"[图层混合+] 设置参数 {paramName}: {oldValue} -> {value}");
    }

    public override Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue) => Task.CompletedTask;

    private class LayerBlendViewModel : ScriptViewModelBase
    {
        private GpuLayerBlendScript Script => (GpuLayerBlendScript)base.Script;

        public LayerBlendViewModel(GpuLayerBlendScript script) : base(script) { }

        public float GlobalOpacity
        {
            get => Script.GlobalOpacity;
            set
            {
                if (Math.Abs(Script.GlobalOpacity - value) > 0.001f)
                {
                    var oldValue = Script.GlobalOpacity;
                    Script.GlobalOpacity = value;
                    OnPropertyChanged();
                    _ = OnParameterChangedAsync(nameof(GlobalOpacity), oldValue, value);
                }
            }
        }

        public bool RespectAlpha
        {
            get => Script.RespectAlpha;
            set
            {
                if (Script.RespectAlpha != value)
                {
                    var oldValue = Script.RespectAlpha;
                    Script.RespectAlpha = value;
                    OnPropertyChanged();
                    _ = OnParameterChangedAsync(nameof(RespectAlpha), oldValue, value);
                }
            }
        }

        public bool EnableParallelProcessing
        {
            get => Script.EnableParallelProcessing;
            set
            {
                if (Script.EnableParallelProcessing != value)
                {
                    var oldValue = Script.EnableParallelProcessing;
                    Script.EnableParallelProcessing = value;
                    OnPropertyChanged();
                    _ = OnParameterChangedAsync(nameof(EnableParallelProcessing), oldValue, value);
                }
            }
        }

        public bool ReverseOrder
        {
            get => Script.ReverseOrder;
            set
            {
                if (Script.ReverseOrder != value)
                {
                    var oldValue = Script.ReverseOrder;
                    Script.ReverseOrder = value;
                    OnPropertyChanged();
                    _ = OnParameterChangedAsync(nameof(ReverseOrder), oldValue, value);
                }
            }
        }

        public override async Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
        {
            await Task.CompletedTask;
        }

        public override ScriptValidationResult ValidateParameter(string parameterName, object value)
        {
            return parameterName switch
            {
                nameof(GlobalOpacity) => new ScriptValidationResult(value is float f && f >= 0f && f <= 1f,
                    "全局不透明度必须在0-1之间"),
                _ => new ScriptValidationResult(true)
            };
        }

        public override Dictionary<string, object> GetParameterData()
        {
            return new Dictionary<string, object>
            {
                [nameof(GlobalOpacity)] = GlobalOpacity,
                [nameof(RespectAlpha)] = RespectAlpha,
                [nameof(EnableParallelProcessing)] = EnableParallelProcessing,
                [nameof(ReverseOrder)] = ReverseOrder
            };
        }

        public override async Task SetParameterDataAsync(Dictionary<string, object> data)
        {
            if (data.TryGetValue(nameof(GlobalOpacity), out var opacity))
                GlobalOpacity = Convert.ToSingle(opacity);
            if (data.TryGetValue(nameof(RespectAlpha), out var respectAlpha))
                RespectAlpha = Convert.ToBoolean(respectAlpha);
            if (data.TryGetValue(nameof(EnableParallelProcessing), out var parallel))
                EnableParallelProcessing = Convert.ToBoolean(parallel);
            if (data.TryGetValue(nameof(ReverseOrder), out var reverse))
                ReverseOrder = Convert.ToBoolean(reverse);
            await Task.CompletedTask;
        }

        public override async Task ResetToDefaultAsync()
        {
            GlobalOpacity = 1.0f;
            RespectAlpha = true;
            EnableParallelProcessing = true;
            ReverseOrder = false;
            await Task.CompletedTask;
        }
    }

    private static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);

    // 缓存最近一次 Process 收到的输入端口名称，用于动态生成参数UI
    private List<string> _lastLinkNames = new();

    // 动态UI支持
    private StackPanel? _mainPanel;
    private StackPanel? _layerControlsPanel;
    private string _currentUIToken = "";

    public override void OnConnectionChanged(ScriptConnectionInfo connectionInfo)
    {
        // 更新连接状态并生成UI标识符
        var connectedLayers = connectionInfo.InputConnections
            .Where(kvp => kvp.Key != "canvas" && kvp.Value)
            .Select(kvp => kvp.Key)
            .OrderBy(k => k)
            .ToList();

        var newToken = string.Join(",", connectedLayers);
        if (newToken != _currentUIToken)
        {
            _currentUIToken = newToken;
            _lastLinkNames = connectedLayers;
            RequestUIRefresh();
        }
    }

    public override string? GetUIUpdateToken()
    {
        return _currentUIToken;
    }

    public override bool TryUpdateUI(FrameworkElement existingControl, ScriptConnectionInfo changeInfo)
    {
        if (existingControl is StackPanel mainPanel && _layerControlsPanel != null)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                // 清除旧的图层控件
                _layerControlsPanel.Children.Clear();

                // 重新生成图层控件
                var connectedLayers = changeInfo.InputConnections
                    .Where(kvp => kvp.Key != "canvas" && kvp.Value)
                    .Select(kvp => kvp.Key)
                    .OrderBy(k => k)
                    .ToList();

                if (!connectedLayers.Any())
                {
                    connectedLayers.Add("Layer1"); // 默认显示Layer1
                }

                foreach (var layerKey in connectedLayers)
                {
                    AddLayerControls(_layerControlsPanel, layerKey);
                }
            });

            return true; // 增量更新成功
        }

        return false; // 需要重建整个UI
    }
} 