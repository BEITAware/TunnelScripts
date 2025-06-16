using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Tunnel_Next.Services.Scripting;

[RevivalScript(
    Name = "混淆图像",
    Author = "Your Name",
    Description = "基于 16×16 分块、块/通道可逆打乱的图像加密脚本",
    Version = "1.0",
    Category = "Security",
    Color = "#8E44AD")]
public class BlockScrambleEncryptorScript : RevivalScriptBase
{
    // --------------- 参数 -----------------
    [ScriptParameter(DisplayName = "Seed", Description = "随机种子，保证加密/解密一致", Order = 0)]
    public int Seed { get; set; } = 12345;

    [ScriptParameter(DisplayName = "Encrypt Mode", Description = "勾选=加密，取消=解密", Order = 1)]
    public bool Encrypt { get; set; } = true;

    private const int BlockSize = 16;     // 固定 16×16

    // --------------- 端口定义 --------------
    public override Dictionary<string, PortDefinition> GetInputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            ["f32bmp"] = new PortDefinition("f32bmp", false, "输入图像")
        };
    }

    public override Dictionary<string, PortDefinition> GetOutputPorts()
    {
        return new Dictionary<string, PortDefinition>
        {
            ["f32bmp"] = new PortDefinition("f32bmp", false, "输出图像")
        };
    }

    // --------------- 核心处理 --------------
    public override Dictionary<string, object> Process(Dictionary<string, object> inputs, IScriptContext ctx)
    {
        var outputs = new Dictionary<string, object>();

        if (!inputs.TryGetValue("f32bmp", out var inputObj) || inputObj is not Mat src)
            return outputs;

        // 1) 裁切到最接近且不超过的 16 的倍数
        int newW = (src.Width / BlockSize) * BlockSize;
        int newH = (src.Height / BlockSize) * BlockSize;
        var cropped = src.SubMat(0, newH, 0, newW).Clone(); // Clone 避免后续修改原图

        int blocksX = newW / BlockSize;
        int blocksY = newH / BlockSize;
        int totalBlocks = blocksX * blocksY;

        // 2) 生成可复现的块映射与通道打乱方案
        var rnd = new Random(Seed);
        int[] forwardMap = Enumerable.Range(0, totalBlocks).OrderBy(_ => rnd.Next()).ToArray(); // i -> destIdx
        int[] inverseMap = new int[totalBlocks];                         // destIdx -> srcIdx
        for (int i = 0; i < totalBlocks; i++) inverseMap[forwardMap[i]] = i;

        // 每块的 RGB 置换（共有 6 种），索引 0~5
        int[] channelPermIndices = new int[totalBlocks];
        for (int i = 0; i < totalBlocks; i++) channelPermIndices[i] = rnd.Next(6);

        // 3) 创建输出 Mat
        var dst = new Mat(newH, newW, src.Type());

        // 4) 遍历并处理每个块
        for (int by = 0; by < blocksY; by++)
        {
            for (int bx = 0; bx < blocksX; bx++)
            {
                int idx = by * blocksX + bx;                // 当前块序号
                int targetIdx = Encrypt ? forwardMap[idx]   // 加密：映射到 forwardMap
                                         : inverseMap[idx]; // 解密：映射到 inverseMap

                int tx = (targetIdx % blocksX) * BlockSize;
                int ty = (targetIdx / blocksX) * BlockSize;

                // 取 ROI
                var srcROI = cropped.SubMat(by * BlockSize, (by + 1) * BlockSize,
                                            bx * BlockSize, (bx + 1) * BlockSize);

                // 通道打乱（置换索引按目标块确定，以便解密时能正确逆置换）
                int permIdx = Encrypt ? channelPermIndices[targetIdx]   // 加密：由目标块（加密后位置）决定
                                      : channelPermIndices[idx];       // 解密：当前块即加密时的目标块

                var processed = ApplyChannelPermutation(srcROI, permIdx, Encrypt);

                // 写入目标位置
                var dstROI = dst.SubMat(ty, ty + BlockSize, tx, tx + BlockSize);
                processed.CopyTo(dstROI);
                processed.Dispose();
            }
        }

        outputs["f32bmp"] = dst;
        cropped.Dispose();
        return outputs;
    }

    // 根据索引对 ROI 进行 RGB 通道打乱/还原
    private static Mat ApplyChannelPermutation(Mat roi, int permIdx, bool encrypt)
    {
        // Split ROI into individual channels (支持3或4通道，保持Alpha不变)
        var channels = roi.Split();

        if (channels.Length < 3)
        {
            // 少于三个颜色通道，直接返回克隆
            var clone = roi.Clone();
            foreach (var c in channels) c.Dispose();
            return clone;
        }

        // 定义RGB三通道的6种排列
        int[][] perms =
        {
            new[]{0,1,2}, new[]{0,2,1},
            new[]{1,0,2}, new[]{1,2,0},
            new[]{2,0,1}, new[]{2,1,0}
        };

        int[] perm = perms[permIdx];

        // 计算逆置换
        int[] inverse = new int[3];
        for (int i = 0; i < 3; i++) inverse[perm[i]] = i;

        int[] order = encrypt ? perm : inverse;

        // 构造结果通道列表
        var resultChannels = new List<Mat>(channels.Length);
        resultChannels.Add(channels[order[0]]);
        resultChannels.Add(channels[order[1]]);
        resultChannels.Add(channels[order[2]]);

        // 其余通道（例如Alpha）保持不变
        for (int i = 3; i < channels.Length; i++)
        {
            resultChannels.Add(channels[i]);
        }

        var merged = new Mat();
        Cv2.Merge(resultChannels.ToArray(), merged);

        // 释放临时通道 Mat
        foreach (var c in channels) c.Dispose();

        return merged;
    }

    // --------------- UI 控件 --------------
    public override FrameworkElement CreateParameterControl()
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical };

        var seedLabel = new Label { Content = "Seed：" };
        var seedBox = new TextBox { Text = Seed.ToString(), Width = 120 };
        seedBox.TextChanged += (s, e) =>
        {
            if (int.TryParse(seedBox.Text, out int v))
            {
                var old = Seed; Seed = v;
                OnParameterChanged(nameof(Seed), v);
            }
        };

        var encryptCheck = new CheckBox { Content = "Encrypt Mode (勾选加密 / 取消解密)", IsChecked = Encrypt };
        encryptCheck.Checked += (s, e) => { Encrypt = true; OnParameterChanged(nameof(Encrypt), Encrypt); };
        encryptCheck.Unchecked += (s, e) => { Encrypt = false; OnParameterChanged(nameof(Encrypt), Encrypt); };

        panel.Children.Add(seedLabel);
        panel.Children.Add(seedBox);
        panel.Children.Add(encryptCheck);
        return panel;
    }

    // --------------- ViewModel ------------
    public override IScriptViewModel CreateViewModel() => new EncryptorViewModel(this);

    private class EncryptorViewModel : ScriptViewModelBase
    {
        private readonly BlockScrambleEncryptorScript _s;
        public EncryptorViewModel(BlockScrambleEncryptorScript s) : base(s) => _s = s;

        public int Seed
        {
            get => _s.Seed;
            set { if (_s.Seed != value) { var old = _s.Seed; _s.Seed = value; OnPropertyChanged(); _ = HandleParameterChangeAsync(nameof(Seed), old, value); } }
        }

        public bool Encrypt
        {
            get => _s.Encrypt;
            set { if (_s.Encrypt != value) { var old = _s.Encrypt; _s.Encrypt = value; OnPropertyChanged(); _ = HandleParameterChangeAsync(nameof(Encrypt), old, value); } }
        }

        public override async Task OnParameterChangedAsync(string n, object o, object v) => await _s.OnParameterChangedAsync(n, o, v);
        public override ScriptValidationResult ValidateParameter(string n, object v) => new(true);
        public override Dictionary<string, object> GetParameterData()
        {
            return new Dictionary<string, object>
            {
                [nameof(Seed)] = Seed,
                [nameof(Encrypt)] = Encrypt
            };
        }
        public override async Task SetParameterDataAsync(Dictionary<string, object> d) => await RunOnUIThreadAsync(() =>
        {
            if (d.TryGetValue(nameof(Seed), out var s) && s is int i) Seed = i;
            if (d.TryGetValue(nameof(Encrypt), out var b) && b is bool e) Encrypt = e;
        });
        public override async Task ResetToDefaultAsync() => await RunOnUIThreadAsync(() => { Seed = 12345; Encrypt = true; });
    }

    // ------------- 序列化支持 -------------
    public override Dictionary<string, object> SerializeParameters()
    {
        return new Dictionary<string, object>
        {
            [nameof(Seed)] = Seed,
            [nameof(Encrypt)] = Encrypt
        };
    }

    public override void DeserializeParameters(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(Seed), out var s) && s is int i) Seed = i;
        if (data.TryGetValue(nameof(Encrypt), out var b) && b is bool e) Encrypt = e;
    }

    // ------------- 参数变化处理 -------------
    public override async Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
    {
        // 由于脚本内部无状态依赖，只需异步返回即可。
        await Task.CompletedTask;
    }
}