using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Tunnel_Next.Services.Scripting;

[RevivalScript(
    Name = "混淆图像 Revised 2",
    Author = "BEITAware",
    Description = "基于块/通道可逆打乱的图像加密脚本，支持移位输入以优化边界。",
    Version = "1.2",
    Category = "Security",
    Color = "#8E44AD")]
public class BlockScrambleEncryptorScriptRV2 : RevivalScriptBase
{
    // --------------- 参数 -----------------
    [ScriptParameter(DisplayName = "Seed", Description = "随机种子", Order = 0)]
    public int Seed { get; set; } = 12345;

    [ScriptParameter(DisplayName = "Encrypt Mode", Description = "勾选加密/取消解密", Order = 1)]
    public bool Encrypt { get; set; } = true;

    [ScriptParameter(DisplayName = "块列数", Description = "宽度块数量", Order = 2)]
    public int BlocksX { get; set; } = 32;

    [ScriptParameter(DisplayName = "块行数", Description = "高度块数量", Order = 3)]
    public int BlocksY { get; set; } = 32;

    [ScriptParameter(DisplayName = "Debug Mode", Description = "输出调试日志", Order = 4)]
    public bool DebugMode { get; set; } = true;

    [ScriptParameter(DisplayName = "遮罩水平偏移", Description = "手动调整遮罩X位置", Order = 5)]
    public int MaskOffsetX { get; set; } = 0;

    [ScriptParameter(DisplayName = "遮罩垂直偏移", Description = "手动调整遮罩Y位置", Order = 6)]
    public int MaskOffsetY { get; set; } = 0;

    // --------------- 端口定义 --------------
    public override Dictionary<string, PortDefinition> GetInputPorts() => new()
    {
        ["f32bmp"] = new PortDefinition("f32bmp", false, "主输入"),
        ["f32bmp_shifted_in"] = new PortDefinition("f32bmp", false, "可选：移位输入")
    };

    public override Dictionary<string, PortDefinition> GetOutputPorts() => new()
    {
        ["f32bmp"] = new PortDefinition("f32bmp", false, "主输出"),
        ["f32bmp_shifted"] = new PortDefinition("f32bmp", false, "移位输出")
    };

    // --------------- 核心处理 --------------
    public override Dictionary<string, object> Process(Dictionary<string, object> inputs, IScriptContext ctx)
    {
        var outputs = new Dictionary<string, object>();
        Action<string> dbg = m => { if (DebugMode) Debug.WriteLine($"[BCE-RV2] {m}"); };

        if (!inputs.TryGetValue("f32bmp", out var inputObj) || inputObj is not Mat src || src.Empty())
        {
            dbg("主输入 'f32bmp' 未找到或为空，操作中止。");
            return outputs;
        }

        Mat? cropped = null;
        try
        {
            dbg("处理开始。");
            int blockW = Math.Max(1, src.Width / BlocksX);
            int blockH = Math.Max(1, src.Height / BlocksY);
            dbg($"块尺寸: {blockW}x{blockH}");

            int newW = blockW * BlocksX;
            int newH = blockH * BlocksY;
            cropped = src.SubMat(0, newH, 0, newW).Clone();

            int totalBlocks = BlocksX * BlocksY;
            var rnd = new Random(Seed);
            int[] forwardMap = Enumerable.Range(0, totalBlocks).OrderBy(_ => rnd.Next()).ToArray();
            int[] inverseMap = new int[totalBlocks];
            for (int i = 0; i < totalBlocks; i++) inverseMap[forwardMap[i]] = i; 
            int[] channelPermIndices = new int[totalBlocks];
            for (int i = 0; i < totalBlocks; i++) channelPermIndices[i] = rnd.Next(6);
            dbg("映射及置换方案已生成。");

            if (Encrypt)
            {
                dbg("进入加密模式。");
                var result = new Mat(newH, newW, src.Type());
                for (int y = 0; y < BlocksY; y++)
                {
                    for (int x = 0; x < BlocksX; x++)
                    {
                        int srcIdx = y * BlocksX + x;
                        int dstIdx = forwardMap[srcIdx];

                        var srcRoi = cropped.SubMat(y * blockH, (y + 1) * blockH, x * blockW, (x + 1) * blockW);
                        var dstRoi = result.SubMat((dstIdx / BlocksX) * blockH, ((dstIdx / BlocksX) + 1) * blockH, (dstIdx % BlocksX) * blockW, ((dstIdx % BlocksX) + 1) * blockW);
                        
                        int permKey = channelPermIndices[dstIdx];
                        var processed = ApplyChannelPermutation(srcRoi, permKey, true);
                        
                        processed.CopyTo(dstRoi);
                        processed.Dispose();
                    }
                }
                dbg("加密完成。");
                outputs["f32bmp"] = result;
                outputs["f32bmp_shifted"] = ShiftWrap(result.Clone(), -blockW / 2, blockH / 2);
            }
            else // Decrypt
            {
                dbg("进入解密模式。");
                Mat baseDecoded = DecodeImage(cropped, inverseMap, channelPermIndices, BlocksX, BlocksY, blockW, blockH);
                dbg("主输入解密完成。");

                inputs.TryGetValue("f32bmp_shifted_in", out var shiftedInObj);
                if (shiftedInObj is Mat shiftedEnc && !shiftedEnc.Empty())
                {
                    dbg("接收到第二（移位）输入，开始处理。");
                    int shiftX = blockW / 2;
                    int shiftY = blockH / 2;
                    Mat alignedEnc = ShiftWrap(shiftedEnc, shiftX, -shiftY);
                    Mat alignedCrop = alignedEnc.SubMat(0, newH, 0, newW);

                    Mat secondDecoded = DecodeImage(alignedCrop, inverseMap, channelPermIndices, BlocksX, BlocksY, blockW, blockH);
                    dbg("第二输入解密完成，开始融合。");
                    
                    Mat fused = BlendWithBoundary(baseDecoded, secondDecoded, blockW, blockH, MaskOffsetX, MaskOffsetY);
                    dbg("融合完成。");
                    
                    outputs["f32bmp"] = fused;
                    outputs["f32bmp_shifted"] = ShiftWrap(fused.Clone(), -shiftX, shiftY);

                    alignedEnc.Dispose();
                    alignedCrop.Dispose();
                    baseDecoded.Dispose();
                    secondDecoded.Dispose();
                }
                else
                {
                    dbg("未提供第二输入，跳过融合。");
                    outputs["f32bmp"] = baseDecoded;
                    outputs["f32bmp_shifted"] = ShiftWrap(baseDecoded.Clone(), -blockW / 2, blockH / 2);
                }
            }
            dbg("处理流程结束。");
        }
        catch (Exception ex)
        {
            dbg($"!!! 异常: {ex}");
        }
        finally
        {
            cropped?.Dispose();
        }
        return outputs;
    }

    private static Mat DecodeImage(Mat scrambledImg, int[] inverseMap, int[] channelPermIndices, int blocksX, int blocksY, int blockW, int blockH)
    {
        var decoded = new Mat(scrambledImg.Size(), scrambledImg.Type());
        for (int y = 0; y < blocksY; y++)
        {
            for (int x = 0; x < blocksX; x++)
            {
                int scrambledIdx = y * blocksX + x;
                int originalIdx = inverseMap[scrambledIdx];

                var srcRoi = scrambledImg.SubMat(y * blockH, (y + 1) * blockH, x * blockW, (x + 1) * blockW);
                var dstRoi = decoded.SubMat((originalIdx / blocksX) * blockH, ((originalIdx / blocksX) + 1) * blockH, (originalIdx % blocksX) * blockW, ((originalIdx % blocksX) + 1) * blockW);

                int permKey = channelPermIndices[scrambledIdx];
                var processed = ApplyChannelPermutation(srcRoi, permKey, false);

                processed.CopyTo(dstRoi);
                processed.Dispose();
            }
        }
        return decoded;
    }

    private static Mat ApplyChannelPermutation(Mat roi, int permIdx, bool encrypt)
    {
        var channels = roi.Split();
        if (channels.Length < 3) { var c = roi.Clone(); Array.ForEach(channels, m => m.Dispose()); return c; }
        int[][] perms = { new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 }, new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 } };
        int[] perm = perms[permIdx];
        int[] inverse = new int[3];
        for (int i = 0; i < 3; i++) inverse[perm[i]] = i;
        int[] order = encrypt ? perm : inverse;
        var resultChannels = new List<Mat> { channels[order[0]], channels[order[1]], channels[order[2]] };
        if (channels.Length > 3) resultChannels.Add(channels[3]); // Keep Alpha
        var merged = new Mat();
        Cv2.Merge(resultChannels.ToArray(), merged);
        Array.ForEach(channels, m => m.Dispose());
        return merged;
    }

    private static Mat ShiftWrap(Mat src, int shiftX, int shiftY)
    {
        int w = src.Width, h = src.Height;
        shiftX = ((shiftX % w) + w) % w;
        shiftY = ((shiftY % h) + h) % h;
        if (shiftX == 0 && shiftY == 0) return src.Clone();
        var dst = new Mat(src.Size(), src.Type());
        src.SubMat(0, h - shiftY, 0, w - shiftX).CopyTo(dst.SubMat(shiftY, h, shiftX, w));
        src.SubMat(0, h - shiftY, w - shiftX, w).CopyTo(dst.SubMat(shiftY, h, 0, shiftX));
        src.SubMat(h - shiftY, h, 0, w - shiftX).CopyTo(dst.SubMat(0, shiftY, shiftX, w));
        src.SubMat(h - shiftY, h, w - shiftX, w).CopyTo(dst.SubMat(0, shiftY, 0, shiftX));
        return dst;
    }

    private static Mat BlendWithBoundary(Mat imgA, Mat imgB, int blockW, int blockH, int maskOffsetX, int maskOffsetY)
    {
        int w = imgA.Width, h = imgA.Height;
        int edgeW = Math.Max(1, Math.Min(blockW, blockH) / 4);
        var weight = new Mat(h, w, MatType.CV_32FC1);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int dy = Math.Min(y % blockH, blockH - 1 - (y % blockH));
                int dx = Math.Min(x % blockW, blockW - 1 - (x % blockW));
                float dist = Math.Min(dx, dy);
                float ratio = dist / edgeW;
                weight.Set(y, x, ratio < 1f ? 1f - ratio * ratio : 0f);
            }
        }

        if (maskOffsetX != 0 || maskOffsetY != 0)
        {
            var shiftedWeight = ShiftWrap(weight, maskOffsetX, maskOffsetY);
            weight.Dispose();
            weight = shiftedWeight;
        }

        var fused = new Mat();
        if (imgA.Channels() == 4)
        {
            var aCh = imgA.Split(); var bCh = imgB.Split();
            var invWeight = new Mat(); Cv2.Subtract(new Scalar(1.0), weight, invWeight);
            var fusedBGR = new Mat[3];
            for (int i = 0; i < 3; i++)
            {
                var pA = new Mat(); var pB = new Mat();
                Cv2.Multiply(aCh[i], invWeight, pA);
                Cv2.Multiply(bCh[i], weight, pB);
                fusedBGR[i] = new Mat();
                Cv2.Add(pA, pB, fusedBGR[i]);
                pA.Dispose(); pB.Dispose();
            }
            var channels = new List<Mat>(fusedBGR) { aCh[3].Clone() };
            Cv2.Merge(channels.ToArray(), fused);

            Array.ForEach(aCh, m => m.Dispose());
            Array.ForEach(bCh, m => m.Dispose());
            Array.ForEach(fusedBGR, m => m.Dispose());
            invWeight.Dispose();
        }
        else
        {
            var invWeight = new Mat();
            Cv2.Subtract(new Scalar(1.0), weight, invWeight);
            Cv2.Multiply(imgA, invWeight, imgA);
            Cv2.Multiply(imgB, weight, imgB);
            Cv2.Add(imgA, imgB, fused);
            invWeight.Dispose();
        }

        weight.Dispose();
        return fused;
    }

    public override FrameworkElement CreateParameterControl()
    {
        var mainPanel = new StackPanel { Margin = new Thickness(5) };

        var resources = new ResourceDictionary();
        var resourcePaths = new[]
        {
            "/Tunnel-Next;component/Resources/ScriptsControls/SharedBrushes.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/LabelStyles.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/CheckBoxStyles.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/TextBoxIdleStyles.xaml"
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

        var viewModel = CreateViewModel() as EncryptorViewModel;
        mainPanel.DataContext = viewModel;

        var titleLabel = new Label { Content = "加密设置 (Revised 2)" };
        if (resources.Contains("TitleLabelStyle"))
        {
            titleLabel.Style = resources["TitleLabelStyle"] as Style;
        }
        mainPanel.Children.Add(titleLabel);

        mainPanel.Children.Add(CreateLabel("随机种子 (Seed):", resources));
        mainPanel.Children.Add(CreateBoundTextBox("Seed", viewModel, resources));

        mainPanel.Children.Add(CreateBoundCheckBox("加密模式 (Encrypt)", "Encrypt", viewModel, resources));

        mainPanel.Children.Add(CreateLabel("横向块数量 (Blocks X):", resources));
        mainPanel.Children.Add(CreateBoundTextBox("BlocksX", viewModel, resources));

        mainPanel.Children.Add(CreateLabel("纵向块数量 (Blocks Y):", resources));
        mainPanel.Children.Add(CreateBoundTextBox("BlocksY", viewModel, resources));

        mainPanel.Children.Add(CreateLabel("遮罩水平偏移:", resources));
        mainPanel.Children.Add(CreateBoundTextBox("MaskOffsetX", viewModel, resources));

        mainPanel.Children.Add(CreateLabel("遮罩垂直偏移:", resources));
        mainPanel.Children.Add(CreateBoundTextBox("MaskOffsetY", viewModel, resources));

        mainPanel.Children.Add(CreateBoundCheckBox("调试模式 (Debug Mode)", "DebugMode", viewModel, resources));

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

    private TextBox CreateBoundTextBox(string propertyName, object viewModel, ResourceDictionary resources)
    {
        var textBox = new TextBox { Margin = new Thickness(0, 2, 0, 10) };
        if (resources.Contains("DefaultTextBoxStyle"))
        {
            textBox.Style = resources["DefaultTextBoxStyle"] as Style;
        }
        textBox.SetBinding(TextBox.TextProperty, new Binding(propertyName) { Source = viewModel, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        return textBox;
    }

    private CheckBox CreateBoundCheckBox(string content, string propertyName, object viewModel, ResourceDictionary resources)
    {
        var checkBox = new CheckBox { Content = content, Margin = new Thickness(0, 5, 0, 10) };
        if (resources.Contains("DefaultCheckBoxStyle"))
        {
            checkBox.Style = resources["DefaultCheckBoxStyle"] as Style;
        }
        checkBox.SetBinding(CheckBox.IsCheckedProperty, new Binding(propertyName) { Source = viewModel, Mode = BindingMode.TwoWay });
        return checkBox;
    }

    public override IScriptViewModel CreateViewModel() => new EncryptorViewModel(this);
    private class EncryptorViewModel : ScriptViewModelBase
    {
        private readonly BlockScrambleEncryptorScriptRV2 _s;
        public EncryptorViewModel(BlockScrambleEncryptorScriptRV2 s) : base(s) => _s = s;
        public int Seed { get => _s.Seed; set { if (_s.Seed != value) { _s.Seed = value; OnPropertyChanged(); _ = HandleParameterChangeAsync(nameof(Seed), _s.Seed, value); } } }
        public bool Encrypt { get => _s.Encrypt; set { if (_s.Encrypt != value) { _s.Encrypt = value; OnPropertyChanged(); _ = HandleParameterChangeAsync(nameof(Encrypt), _s.Encrypt, value); } } }
        public int BlocksX { get => _s.BlocksX; set { if (_s.BlocksX != value) { _s.BlocksX = value; OnPropertyChanged(); _ = HandleParameterChangeAsync(nameof(BlocksX), _s.BlocksX, value); } } }
        public int BlocksY { get => _s.BlocksY; set { if (_s.BlocksY != value) { _s.BlocksY = value; OnPropertyChanged(); _ = HandleParameterChangeAsync(nameof(BlocksY), _s.BlocksY, value); } } }
        public bool DebugMode { get => _s.DebugMode; set { if (_s.DebugMode != value) { _s.DebugMode = value; OnPropertyChanged(); _ = HandleParameterChangeAsync(nameof(DebugMode), _s.DebugMode, value); } } }
        public int MaskOffsetX { get => _s.MaskOffsetX; set { if (_s.MaskOffsetX != value) { _s.MaskOffsetX = value; OnPropertyChanged(); _ = HandleParameterChangeAsync(nameof(MaskOffsetX), _s.MaskOffsetX, value); } } }
        public int MaskOffsetY { get => _s.MaskOffsetY; set { if (_s.MaskOffsetY != value) { _s.MaskOffsetY = value; OnPropertyChanged(); _ = HandleParameterChangeAsync(nameof(MaskOffsetY), _s.MaskOffsetY, value); } } }
        public override async Task OnParameterChangedAsync(string n, object o, object v) => await _s.OnParameterChangedAsync(n, o, v);
        public override ScriptValidationResult ValidateParameter(string n, object v) => new(true);
        public override Dictionary<string, object> GetParameterData() => new() { [nameof(Seed)] = Seed, [nameof(Encrypt)] = Encrypt, [nameof(BlocksX)] = BlocksX, [nameof(BlocksY)] = BlocksY, [nameof(DebugMode)] = DebugMode, [nameof(MaskOffsetX)] = MaskOffsetX, [nameof(MaskOffsetY)] = MaskOffsetY };
        public override async Task SetParameterDataAsync(Dictionary<string, object> d) => await RunOnUIThreadAsync(() => { if (d.TryGetValue(nameof(Seed), out var s) && int.TryParse(s?.ToString(), out int i)) Seed = i; if (d.TryGetValue(nameof(Encrypt), out var b) && bool.TryParse(b?.ToString(), out bool e)) Encrypt = e; if (d.TryGetValue(nameof(BlocksX), out var bx) && int.TryParse(bx?.ToString(), out int bxInt)) BlocksX = bxInt; if (d.TryGetValue(nameof(BlocksY), out var by) && int.TryParse(by?.ToString(), out int byInt)) BlocksY = byInt; if (d.TryGetValue(nameof(DebugMode), out var dm) && bool.TryParse(dm?.ToString(), out bool dbgFlag)) DebugMode = dbgFlag; if (d.TryGetValue(nameof(MaskOffsetX), out var mox) && int.TryParse(mox?.ToString(), out int moxInt)) MaskOffsetX = moxInt; if (d.TryGetValue(nameof(MaskOffsetY), out var moy) && int.TryParse(moy?.ToString(), out int moyInt)) MaskOffsetY = moyInt; });
        public override async Task ResetToDefaultAsync() => await RunOnUIThreadAsync(() => { Seed = 12345; Encrypt = true; BlocksX = 32; BlocksY = 32; DebugMode = false; MaskOffsetX = 0; MaskOffsetY = 0; });
    }
    public override Dictionary<string, object> SerializeParameters() => new() { [nameof(Seed)] = Seed, [nameof(Encrypt)] = Encrypt, [nameof(BlocksX)] = BlocksX, [nameof(BlocksY)] = BlocksY, [nameof(DebugMode)] = DebugMode, [nameof(MaskOffsetX)] = MaskOffsetX, [nameof(MaskOffsetY)] = MaskOffsetY };
    public override void DeserializeParameters(Dictionary<string, object> data) { if (data.TryGetValue(nameof(Seed), out var s) && int.TryParse(s?.ToString(), out int i)) Seed = i; if (data.TryGetValue(nameof(Encrypt), out var b) && bool.TryParse(b?.ToString(), out bool e)) Encrypt = e; if (data.TryGetValue(nameof(BlocksX), out var bx) && int.TryParse(bx?.ToString(), out int bxInt)) BlocksX = bxInt; if (data.TryGetValue(nameof(BlocksY), out var by) && int.TryParse(by?.ToString(), out int byInt)) BlocksY = byInt; if (data.TryGetValue(nameof(DebugMode), out var dm) && bool.TryParse(dm?.ToString(), out bool dbgFlag)) DebugMode = dbgFlag; if (data.TryGetValue(nameof(MaskOffsetX), out var mox) && int.TryParse(mox?.ToString(), out int moxInt)) MaskOffsetX = moxInt; if (data.TryGetValue(nameof(MaskOffsetY), out var moy) && int.TryParse(moy?.ToString(), out int moyInt)) MaskOffsetY = moyInt; }
    public override async Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue) { await Task.CompletedTask; }
}