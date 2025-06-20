using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Tunnel_Next.Services.Scripting;

[RevivalScript(
    Name = "混淆图像-Revised",
    Author = "BEITAware",
    Description = "基于 16×16 分块、块/通道可逆打乱的图像加密脚本",
    Version = "1.0",
    Category = "Security",
    Color = "#8E44AD")]
public class BlockScrambleEncryptorScriptRV : RevivalScriptBase
{
    // --------------- 参数 -----------------
    [ScriptParameter(DisplayName = "Seed", Description = "随机种子，保证加密/解密一致", Order = 0)]
    public int Seed { get; set; } = 12345;

    [ScriptParameter(DisplayName = "Encrypt Mode", Description = "勾选=加密，取消=解密", Order = 1)]
    public bool Encrypt { get; set; } = true;

    [ScriptParameter(DisplayName = "块列数", Description = "图像宽方向块数量", Order = 2)]
    public int BlocksX { get; set; } = 32;

    [ScriptParameter(DisplayName = "块行数", Description = "图像高方向块数量", Order = 3)]
    public int BlocksY { get; set; } = 32;

    [ScriptParameter(DisplayName = "修复伪影", Description = "解密后对块边缘进行平滑处理以减少压缩伪影", Order = 4)]
    public bool SmoothArtifacts { get; set; } = true;

    [ScriptParameter(DisplayName = "修复强度", Description = "边缘平滑的宽度（像素 > 0）", Order = 5)]
    public int SmoothWidth { get; set; } = 2;

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

        // 1) 根据块数量计算块尺寸，并裁切到可整除尺寸
        int blockW = Math.Max(1, src.Width / BlocksX);
        int blockH = Math.Max(1, src.Height / BlocksY);

        int newW = blockW * BlocksX;
        int newH = blockH * BlocksY;
        var cropped = src.SubMat(0, newH, 0, newW).Clone(); // Clone 避免后续修改原图

        int blocksX = BlocksX;
        int blocksY = BlocksY;
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

                int tx = (targetIdx % blocksX) * blockW;
                int ty = (targetIdx / blocksX) * blockH;

                // 取 ROI
                var srcROI = cropped.SubMat(by * blockH, (by + 1) * blockH,
                                            bx * blockW, (bx + 1) * blockW);

                // 通道打乱（置换索引按目标块确定，以便解密时能正确逆置换）
                int permIdx = Encrypt ? channelPermIndices[targetIdx]   // 加密：由目标块（加密后位置）决定
                                      : channelPermIndices[idx];       // 解密：当前块即加密时的目标块

                var processed = ApplyChannelPermutation(srcROI, permIdx, Encrypt);

                // 写入目标位置
                var dstROI = dst.SubMat(ty, ty + blockH, tx, tx + blockW);
                processed.CopyTo(dstROI);
                processed.Dispose();
            }
        }

        // 如果解密，则选择性平滑块边界以减少压缩伪影
        if (!Encrypt && SmoothArtifacts && SmoothWidth > 0)
        {
            SmoothBlockBoundaries(dst, blockW, blockH, blocksX, blocksY, SmoothWidth);
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

    /// <summary>
    /// 在解密后平滑块边界以减少压缩伪影
    /// </summary>
    private static void SmoothBlockBoundaries(Mat image, int blockW, int blockH, int blocksX, int blocksY, int smoothWidth)
    {
        if (smoothWidth <= 0) return;

        // 内核大小必须为奇数。我们使用的内核大小约为平滑宽度的两倍，以确保效果。
        int ksize = (smoothWidth * 2) + 1;

        // 平滑水平边界
        for (int by = 1; by < blocksY; by++)
        {
            int y_seam = by * blockH;
            
            // 定义一个以接缝为中心、宽度为 smoothWidth * 2 的ROI
            int roiY = Math.Max(0, y_seam - smoothWidth);
            int roiHeight = Math.Min(image.Height - roiY, smoothWidth * 2);
            if (roiHeight <= ksize) continue; // ROI太小则跳过

            OpenCvSharp.Rect seamRoi = new OpenCvSharp.Rect(0, roiY, image.Width, roiHeight);
            using (Mat roiMat = new Mat(image, seamRoi))
            {
                // 仅在垂直方向上应用高斯模糊
                Cv2.GaussianBlur(roiMat, roiMat, new OpenCvSharp.Size(1, ksize), 0);
            }
        }

        // 平滑垂直边界
        for (int bx = 1; bx < blocksX; bx++)
        {
            int x_seam = bx * blockW;

            // 定义ROI
            int roiX = Math.Max(0, x_seam - smoothWidth);
            int roiWidth = Math.Min(image.Width - roiX, smoothWidth * 2);
            if (roiWidth <= ksize) continue; // ROI太小则跳过

            OpenCvSharp.Rect seamRoiV = new OpenCvSharp.Rect(roiX, 0, roiWidth, image.Height);
            using (Mat roiMat = new Mat(image, seamRoiV))
            {
                // 仅在水平方向上应用高斯模糊
                Cv2.GaussianBlur(roiMat, roiMat, new OpenCvSharp.Size(ksize, 1), 0);
            }
        }
    }

    // --------------- UI 控件 --------------
    public override FrameworkElement CreateParameterControl()
    {
        var mainPanel = new StackPanel { Margin = new Thickness(5) };

        // 加载资源
        var resources = new ResourceDictionary();
        var resourcePaths = new[]
        {
            "/Tunnel-Next;component/Resources/ScriptsControls/SharedBrushes.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/LabelStyles.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/CheckBoxStyles.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/TextBoxIdleStyles.xaml",
            "/Tunnel-Next;component/Resources/ScriptsControls/SliderStyles.xaml"
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

        var titleLabel = new Label { Content = "加密设置 (Revised)" };
        if (resources.Contains("TitleLabelStyle"))
        {
            titleLabel.Style = resources["TitleLabelStyle"] as Style;
        }
        mainPanel.Children.Add(titleLabel);

        // 基本设置
        mainPanel.Children.Add(CreateLabel("随机种子 (Seed):", resources));
        var seedBox = CreateBoundTextBox("Seed", viewModel, resources);
        mainPanel.Children.Add(seedBox);

        mainPanel.Children.Add(CreateBoundCheckBox("加密模式 (Encrypt Mode)", "Encrypt", viewModel, resources));

        mainPanel.Children.Add(CreateLabel("横向块数量 (Blocks X):", resources));
        var bxBox = CreateBoundTextBox("BlocksX", viewModel, resources);
        mainPanel.Children.Add(bxBox);
        
        mainPanel.Children.Add(CreateLabel("纵向块数量 (Blocks Y):", resources));
        var byBox = CreateBoundTextBox("BlocksY", viewModel, resources);
        mainPanel.Children.Add(byBox);

        // 修复伪影
        var smoothCheckBox = CreateBoundCheckBox("修复伪影 (Smooth Artifacts)", "SmoothArtifacts", viewModel, resources);
        mainPanel.Children.Add(smoothCheckBox);

        var smoothPanel = new StackPanel();
        smoothPanel.SetBinding(StackPanel.VisibilityProperty, new System.Windows.Data.Binding("SmoothArtifacts")
        {
            Source = viewModel,
            Converter = new BooleanToVisibilityConverter()
        });

        smoothPanel.Children.Add(CreateLabel("修复强度 (Smooth Width):", resources));
        var smoothSlider = new Slider
        {
            Minimum = 1,
            Maximum = 16,
            SmallChange = 1,
            IsSnapToTickEnabled = true,
            Margin = new Thickness(0, 2, 0, 10)
        };
        if (resources.Contains("DefaultSliderStyle"))
        {
            smoothSlider.Style = resources["DefaultSliderStyle"] as Style;
        }
        smoothSlider.SetBinding(Slider.ValueProperty, new System.Windows.Data.Binding("SmoothWidth") { Source = viewModel, Mode = System.Windows.Data.BindingMode.TwoWay });
        smoothPanel.Children.Add(smoothSlider);
        
        mainPanel.Children.Add(smoothPanel);
        
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
        textBox.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding(propertyName) { Source = viewModel, Mode = System.Windows.Data.BindingMode.TwoWay, UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged });
        return textBox;
    }

    private CheckBox CreateBoundCheckBox(string content, string propertyName, object viewModel, ResourceDictionary resources)
    {
        var checkBox = new CheckBox { Content = content, Margin = new Thickness(0, 5, 0, 10) };
        if (resources.Contains("DefaultCheckBoxStyle"))
        {
            checkBox.Style = resources["DefaultCheckBoxStyle"] as Style;
        }
        checkBox.SetBinding(CheckBox.IsCheckedProperty, new System.Windows.Data.Binding(propertyName) { Source = viewModel, Mode = System.Windows.Data.BindingMode.TwoWay });
        return checkBox;
    }

    // --------------- ViewModel ------------
    public override IScriptViewModel CreateViewModel() => new EncryptorViewModel(this);

    private class EncryptorViewModel : ScriptViewModelBase
    {
        private readonly BlockScrambleEncryptorScriptRV _s;
        public EncryptorViewModel(BlockScrambleEncryptorScriptRV s) : base(s) => _s = s;

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

        public int BlocksX
        {
            get => _s.BlocksX;
            set { if (_s.BlocksX != value) { var old = _s.BlocksX; _s.BlocksX = value; OnPropertyChanged(); _ = HandleParameterChangeAsync(nameof(BlocksX), old, value); } }
        }

        public int BlocksY
        {
            get => _s.BlocksY;
            set { if (_s.BlocksY != value) { var old = _s.BlocksY; _s.BlocksY = value; OnPropertyChanged(); _ = HandleParameterChangeAsync(nameof(BlocksY), old, value); } }
        }

        public bool SmoothArtifacts
        {
            get => _s.SmoothArtifacts;
            set { if (_s.SmoothArtifacts != value) { var old = _s.SmoothArtifacts; _s.SmoothArtifacts = value; OnPropertyChanged(); _ = HandleParameterChangeAsync(nameof(SmoothArtifacts), old, value); } }
        }

        public int SmoothWidth
        {
            get => _s.SmoothWidth;
            set { if (_s.SmoothWidth != value) { var old = _s.SmoothWidth; _s.SmoothWidth = value; OnPropertyChanged(); _ = HandleParameterChangeAsync(nameof(SmoothWidth), old, value); } }
        }

        public override async Task OnParameterChangedAsync(string n, object o, object v) => await _s.OnParameterChangedAsync(n, o, v);
        public override ScriptValidationResult ValidateParameter(string n, object v) => new(true);
        public override Dictionary<string, object> GetParameterData()
        {
            return new Dictionary<string, object>
            {
                [nameof(Seed)] = Seed,
                [nameof(Encrypt)] = Encrypt,
                [nameof(BlocksX)] = BlocksX,
                [nameof(BlocksY)] = BlocksY,
                [nameof(SmoothArtifacts)] = SmoothArtifacts,
                [nameof(SmoothWidth)] = SmoothWidth
            };
        }
        public override async Task SetParameterDataAsync(Dictionary<string, object> d) => await RunOnUIThreadAsync(() =>
        {
            if (d.TryGetValue(nameof(Seed), out var s) && s is int i) Seed = i;
            if (d.TryGetValue(nameof(Encrypt), out var b) && b is bool e) Encrypt = e;
            if (d.TryGetValue(nameof(BlocksX), out var bx) && bx is int bxInt) BlocksX = bxInt;
            if (d.TryGetValue(nameof(BlocksY), out var by) && by is int byInt) BlocksY = byInt;
            if (d.TryGetValue(nameof(SmoothArtifacts), out var sa) && sa is bool saBool) SmoothArtifacts = saBool;
            if (d.TryGetValue(nameof(SmoothWidth), out var sw) && sw is int swInt) SmoothWidth = swInt;
        });
        public override async Task ResetToDefaultAsync() => await RunOnUIThreadAsync(() => { Seed = 12345; Encrypt = true; BlocksX = 32; BlocksY = 32; SmoothArtifacts = true; SmoothWidth = 2; });
    }

    // ------------- 序列化支持 -------------
    public override Dictionary<string, object> SerializeParameters()
    {
        return new Dictionary<string, object>
        {
            [nameof(Seed)] = Seed,
            [nameof(Encrypt)] = Encrypt,
            [nameof(BlocksX)] = BlocksX,
            [nameof(BlocksY)] = BlocksY,
            [nameof(SmoothArtifacts)] = SmoothArtifacts,
            [nameof(SmoothWidth)] = SmoothWidth
        };
    }

    public override void DeserializeParameters(Dictionary<string, object> data)
    {
        if (data.TryGetValue(nameof(Seed), out var s) && s is int i) Seed = i;
        if (data.TryGetValue(nameof(Encrypt), out var b) && b is bool e) Encrypt = e;
        if (data.TryGetValue(nameof(BlocksX), out var bx) && bx is int bxInt) BlocksX = bxInt;
        if (data.TryGetValue(nameof(BlocksY), out var by) && by is int byInt) BlocksY = byInt;
        if (data.TryGetValue(nameof(SmoothArtifacts), out var sa) && sa is bool saBool) SmoothArtifacts = saBool;
        if (data.TryGetValue(nameof(SmoothWidth), out var sw) && sw is int swInt) SmoothWidth = swInt;
    }

    // ------------- 参数变化处理 -------------
    public override async Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
    {
        // 由于脚本内部无状态依赖，只需异步返回即可。
        await Task.CompletedTask;
    }
}