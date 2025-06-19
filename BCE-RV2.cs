using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;
using Tunnel_Next.Services.Scripting;

[RevivalScript(
    Name = "混淆图像 Revised 2",
    Author = "Your Name",
    Description = "基于块/通道可逆打乱的图像加密脚本，支持移位输入以优化边界。",
    Version = "1.2",
    Category = "Security",
    Color = "#8E44AD")]
public class BlockScrambleEncryptorScript : RevivalScriptBase
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
        int[][] perms = { new[]{0,1,2}, new[]{0,2,1}, new[]{1,0,2}, new[]{1,2,0}, new[]{2,0,1}, new[]{2,1,0} };
        int[] perm = perms[permIdx];
        int[] inverse = new int[3];
        for (int i = 0; i < 3; i++) inverse[perm[i]] = i;
        int[] order = encrypt ? perm : inverse;
        var resultChannels = new List<Mat> { channels[order[0]], channels[order[1]], channels[order[2]] };
        if(channels.Length > 3) resultChannels.Add(channels[3]); // Keep Alpha
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
        if (shiftX == 0 && shiftY == 0) return src;
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

        // 应用手动偏移
        if (maskOffsetX != 0 || maskOffsetY != 0)
        {
            var shiftedWeight = ShiftWrap(weight, maskOffsetX, maskOffsetY);
            weight.Dispose(); // 释放旧的
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
                Cv2.Add(pA, pB, fusedBGR[i] = new Mat());
                pA.Dispose(); pB.Dispose();
            }
            var alpha = aCh[3].Clone();
            Cv2.Merge(new[] { fusedBGR[0], fusedBGR[1], fusedBGR[2], alpha }, fused);
            Array.ForEach(aCh, m=>m.Dispose()); Array.ForEach(bCh, m=>m.Dispose()); Array.ForEach(fusedBGR, m=>m.Dispose());
            invWeight.Dispose(); alpha.Dispose();
        }
        else
        {
            var w3 = new Mat(); Cv2.Merge(new[] { weight, weight, weight }, w3);
            var iw3 = new Mat(); Cv2.Subtract(new Scalar(1.0), w3, iw3);
            var pA = new Mat(); var pB = new Mat();
            Cv2.Multiply(imgA, iw3, pA);
            Cv2.Multiply(imgB, w3, pB);
            Cv2.Add(pA, pB, fused);
            w3.Dispose(); iw3.Dispose(); pA.Dispose(); pB.Dispose();
        }
        weight.Dispose();
        return fused;
    }

    public override FrameworkElement CreateParameterControl()
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(5) };
        var seedLabel = new Label { Content = "Seed：" };
        var seedBox = new TextBox { Text = Seed.ToString(), Width = 120 };
        seedBox.TextChanged += (s, e) => { if (int.TryParse(seedBox.Text, out int v)) { Seed = v; OnParameterChanged(nameof(Seed), v); } };
        var encryptCheck = new CheckBox { Content = "Encrypt Mode", IsChecked = Encrypt };
        encryptCheck.Checked += (s, e) => { Encrypt = true; OnParameterChanged(nameof(Encrypt), Encrypt); };
        encryptCheck.Unchecked += (s, e) => { Encrypt = false; OnParameterChanged(nameof(Encrypt), Encrypt); };
        var bxLabel = new Label { Content = "Blocks X:" };
        var bxBox = new TextBox { Text = BlocksX.ToString(), Width = 80 };
        bxBox.TextChanged += (s, e) => { if (int.TryParse(bxBox.Text, out int v) && v > 0) { BlocksX = v; OnParameterChanged(nameof(BlocksX), v); } };
        var byLabel = new Label { Content = "Blocks Y:" };
        var byBox = new TextBox { Text = BlocksY.ToString(), Width = 80 };
        byBox.TextChanged += (s, e) => { if (int.TryParse(byBox.Text, out int v) && v > 0) { BlocksY = v; OnParameterChanged(nameof(BlocksY), v); } };
        var debugCheck = new CheckBox { Content = "Debug Mode", IsChecked = DebugMode };
        debugCheck.Checked += (s, e) => { DebugMode = true; OnParameterChanged(nameof(DebugMode), DebugMode); };
        debugCheck.Unchecked += (s, e) => { DebugMode = false; OnParameterChanged(nameof(DebugMode), DebugMode); };
        panel.Children.Add(seedLabel);
        panel.Children.Add(seedBox);
        panel.Children.Add(encryptCheck);
        panel.Children.Add(bxLabel);
        panel.Children.Add(bxBox);
        panel.Children.Add(byLabel);
        panel.Children.Add(byBox);
        panel.Children.Add(debugCheck);
        return panel;
    }

    public override IScriptViewModel CreateViewModel() => new EncryptorViewModel(this);
    private class EncryptorViewModel : ScriptViewModelBase
    {
        private readonly BlockScrambleEncryptorScript _s;
        public EncryptorViewModel(BlockScrambleEncryptorScript s) : base(s) => _s = s;
        public int Seed { get => _s.Seed; set { if (_s.Seed != value) { _s.Seed = value; OnPropertyChanged(); _ = HandleParameterChangeAsync(nameof(Seed), _s.Seed, value); } } }
        public bool Encrypt { get => _s.Encrypt; set { if (_s.Encrypt != value) { _s.Encrypt = value; OnPropertyChanged(); _ = HandleParameterChangeAsync(nameof(Encrypt), _s.Encrypt, value); } } }
        public int BlocksX { get => _s.BlocksX; set { if (_s.BlocksX != value) { _s.BlocksX = value; OnPropertyChanged(); _ = HandleParameterChangeAsync(nameof(BlocksX), _s.BlocksX, value); } } }
        public int BlocksY { get => _s.BlocksY; set { if (_s.BlocksY != value) { _s.BlocksY = value; OnPropertyChanged(); _ = HandleParameterChangeAsync(nameof(BlocksY), _s.BlocksY, value); } } }
        public bool DebugMode { get => _s.DebugMode; set { if (_s.DebugMode != value) { _s.DebugMode = value; OnPropertyChanged(); _ = HandleParameterChangeAsync(nameof(DebugMode), _s.DebugMode, value); } } }
        public override async Task OnParameterChangedAsync(string n, object o, object v) => await _s.OnParameterChangedAsync(n, o, v);
        public override ScriptValidationResult ValidateParameter(string n, object v) => new(true);
        public override Dictionary<string, object> GetParameterData() => new() { [nameof(Seed)] = Seed, [nameof(Encrypt)] = Encrypt, [nameof(BlocksX)] = BlocksX, [nameof(BlocksY)] = BlocksY, [nameof(DebugMode)] = DebugMode };
        public override async Task SetParameterDataAsync(Dictionary<string, object> d) => await RunOnUIThreadAsync(() => { if (d.TryGetValue(nameof(Seed), out var s) && s is int i) Seed = i; if (d.TryGetValue(nameof(Encrypt), out var b) && b is bool e) Encrypt = e; if (d.TryGetValue(nameof(BlocksX), out var bx) && bx is int bxInt) BlocksX = bxInt; if (d.TryGetValue(nameof(BlocksY), out var by) && by is int byInt) BlocksY = byInt; if (d.TryGetValue(nameof(DebugMode), out var dm) && dm is bool dbgFlag) DebugMode = dbgFlag; });
        public override async Task ResetToDefaultAsync() => await RunOnUIThreadAsync(() => { Seed = 12345; Encrypt = true; BlocksX = 32; BlocksY = 32; DebugMode = false; });
    }
    public override Dictionary<string, object> SerializeParameters() => new() { [nameof(Seed)] = Seed, [nameof(Encrypt)] = Encrypt, [nameof(BlocksX)] = BlocksX, [nameof(BlocksY)] = BlocksY, [nameof(DebugMode)] = DebugMode };
    public override void DeserializeParameters(Dictionary<string, object> data) { if (data.TryGetValue(nameof(Seed), out var s) && s is int i) Seed = i; if (data.TryGetValue(nameof(Encrypt), out var b) && b is bool e) Encrypt = e; if (data.TryGetValue(nameof(BlocksX), out var bx) && bx is int bxInt) BlocksX = bxInt; if (data.TryGetValue(nameof(BlocksY), out var by) && by is int byInt) BlocksY = byInt; if (data.TryGetValue(nameof(DebugMode), out var dm) && dm is bool dbgFlag) DebugMode = dbgFlag; }
    public override async Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue) { await Task.CompletedTask; }
}