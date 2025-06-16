using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Tunnel_Next.Services.Scripting;
using OpenCvSharp;
using Microsoft.Win32;

[RevivalScript(
    Name = "图像输出",
    Author = "Revival Scripts",
    Description = "将图像保存到文件",
    Version = "1.0",
    Category = "输入输出",
    Color = "#FF6B6B"
)]
public class ImageOutputScript : RevivalScriptBase
{
    [ScriptParameter(DisplayName = "保存路径")]
    public string SavePath { get; set; } = string.Empty;

    // 处理节点实例标识
    public string NodeInstanceId { get; set; } = string.Empty;

    [ScriptParameter(DisplayName = "图像质量", Description = "JPEG图像质量 (1-100)", Order = 1)]
    public int Quality { get; set; } = 95;

    [ScriptParameter(DisplayName = "自动保存", Description = "处理时自动保存图像", Order = 2)]
    public bool AutoSave { get; set; } = true;

    [ScriptParameter(DisplayName = "保存Alpha通道", Description = "是否保存透明度信息（仅PNG和TIFF支持）", Order = 3)]
    public bool SaveAlpha { get; set; } = true;

    public override Dictionary<string, PortDefinition> GetInputPorts()
    {
        // 图像输出节点只需要一个图像输入端口
        return new Dictionary<string, PortDefinition>
        {
            ["f32bmp"] = new PortDefinition("f32bmp", false, "输入图像")
        };
    }

    public override Dictionary<string, PortDefinition> GetOutputPorts()
    {
        // 图像输出节点不需要输出端口
        return new Dictionary<string, PortDefinition>();
    }

    public override Dictionary<string, object> Process(Dictionary<string, object> inputs, IScriptContext context)
    {
        if (!inputs.TryGetValue("f32bmp", out var inputObj) || inputObj is not Mat inputMat)
        {
            throw new ArgumentException("需要输入图像");
        }

        if (AutoSave && !string.IsNullOrEmpty(SavePath))
        {
            SaveImage(inputMat, SavePath);
        }

        // 图像输出节点不返回任何输出
        return new Dictionary<string, object>();
    }

    private void SaveImage(Mat inputMat, string path)
    {
        try
        {
            // 确保目录存在
            var directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }

            // 根据文件扩展名和Alpha支持情况转换图像
            var extension = System.IO.Path.GetExtension(path).ToLower();
            var supportsAlpha = extension == ".png" || extension == ".tiff" || extension == ".tif";


            var outputMat = ConvertFromRGBA32F(inputMat, supportsAlpha && SaveAlpha);

            // 根据文件扩展名设置保存参数
            var saveParams = new int[0];

            if (extension == ".jpg" || extension == ".jpeg")
            {
                saveParams = new int[] { (int)ImwriteFlags.JpegQuality, Quality };
            }
            else if (extension == ".png")
            {
                saveParams = new int[] { (int)ImwriteFlags.PngCompression, 9 };
            }

            Cv2.ImWrite(path, outputMat, saveParams);


            outputMat.Dispose();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"保存图像失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 将32位浮点RGBA格式转换为适合保存的格式
    /// 根据目标格式决定是否保留Alpha通道
    /// </summary>
    private Mat ConvertFromRGBA32F(Mat inputMat, bool preserveAlpha)
    {
        var outputMat = new Mat();


        if (preserveAlpha && inputMat.Channels() == 4)
        {
            // 保留Alpha通道，转换为8位RGBA
            inputMat.ConvertTo(outputMat, MatType.CV_8UC4, 255.0);
        }
        else
        {
            // 不保留Alpha通道或输入不是4通道，转换为8位RGB

            if (inputMat.Channels() == 4)
            {
                // 从RGBA提取RGB
                var rgbMat = new Mat();
                Cv2.CvtColor(inputMat, rgbMat, ColorConversionCodes.RGBA2RGB);
                rgbMat.ConvertTo(outputMat, MatType.CV_8UC3, 255.0);
                rgbMat.Dispose();
            }
            else
            {
                // 直接转换（虽然理论上输入应该总是4通道）
                inputMat.ConvertTo(outputMat, MatType.CV_8UC3, 255.0);
            }
        }

        return outputMat;
    }

    public override Dictionary<string, object> InjectMetadata(Dictionary<string, object> currentMetadata)
    {
        // 向元数据注入输出路径信息
        var metadata = new Dictionary<string, object>(currentMetadata);

        if (!string.IsNullOrEmpty(SavePath))
        {
            metadata["OutputPath"] = SavePath;
            metadata["OutputFileName"] = System.IO.Path.GetFileName(SavePath);
            metadata["OutputDirectory"] = System.IO.Path.GetDirectoryName(SavePath) ?? string.Empty;
            metadata["OutputExtension"] = System.IO.Path.GetExtension(SavePath);
            metadata["Quality"] = Quality;
            metadata["AutoSave"] = AutoSave;
        }

        return metadata;
    }

    public override FrameworkElement CreateParameterControl()
    {
        var mainPanel = new StackPanel { Margin = new Thickness(5) };

        // 应用Aero主题样式 - 使用interfacepanelbar的渐变背景
        mainPanel.Background = new LinearGradientBrush(
            new GradientStopCollection
            {
                new GradientStop((Color)ColorConverter.ConvertFromString("#FF1A1F28"), 0),
                new GradientStop((Color)ColorConverter.ConvertFromString("#FF1C2432"), 0.510204),
                new GradientStop((Color)ColorConverter.ConvertFromString("#FE1C2533"), 0.562152),
                new GradientStop((Color)ColorConverter.ConvertFromString("#FE30445F"), 0.87013),
                new GradientStop((Color)ColorConverter.ConvertFromString("#FE384F6C"), 0.918367),
                new GradientStop((Color)ColorConverter.ConvertFromString("#FF405671"), 0.974026)
            },
            new System.Windows.Point(0.499999, 0), new System.Windows.Point(0.499999, 1)
        );

        // 创建并设置ViewModel作为DataContext
        var viewModel = CreateViewModel() as ImageOutputViewModel;
        mainPanel.DataContext = viewModel;

        // 标题
        var titleLabel = new Label
        {
            Content = "图像输出设置",
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial")
        };
        mainPanel.Children.Add(titleLabel);

        // 输出路径设置
        CreateSavePathControls(mainPanel, viewModel);

        // 质量设置
        CreateQualityControls(mainPanel, viewModel);

        // 自动保存设置
        CreateAutoSaveControls(mainPanel, viewModel);

        // Alpha通道保存设置
        CreateSaveAlphaControls(mainPanel, viewModel);

        return mainPanel;
    }

    private void CreateSavePathControls(StackPanel parent, ImageOutputViewModel viewModel)
    {
        var pathLabel = new Label
        {
            Content = "保存路径:",
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11
        };
        parent.Children.Add(pathLabel);

        var pathPanel = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };

        var selectButton = new Button
        {
            Content = "选择...",
            Width = 60,
            Margin = new Thickness(5, 0, 0, 0),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new GradientStop((Color)ColorConverter.ConvertFromString("#00FFFFFF"), 0),
                    new GradientStop((Color)ColorConverter.ConvertFromString("#1AFFFFFF"), 0.135436),
                    new GradientStop((Color)ColorConverter.ConvertFromString("#17FFFFFF"), 0.487941),
                    new GradientStop((Color)ColorConverter.ConvertFromString("#00000004"), 0.517625),
                    new GradientStop((Color)ColorConverter.ConvertFromString("#FF1F8EAD"), 0.729128)
                },
                new System.Windows.Point(0.5, -0.667875), new System.Windows.Point(0.5, 1.66787)
            ),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF000000")),
            BorderThickness = new Thickness(1)
        };
        DockPanel.SetDock(selectButton, Dock.Right);

        var pathTextBox = new TextBox
        {
            Margin = new Thickness(0),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F0F0F0")),
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#333333")),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A1F28")),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 4, 6, 4),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11
        };

        // 使用数据绑定将TextBox的Text属性绑定到ViewModel的SavePath属性
        var pathBinding = new System.Windows.Data.Binding("SavePath")
        {
            Source = viewModel,
            Mode = System.Windows.Data.BindingMode.OneWay,
            UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
        };
        pathTextBox.SetBinding(TextBox.TextProperty, pathBinding);

        selectButton.Click += (s, e) =>
        {
            var dialog = new SaveFileDialog
            {
                Filter = "JPEG文件|*.jpg|PNG文件|*.png|BMP文件|*.bmp|TIFF文件|*.tiff|所有文件|*.*",
                Title = "选择保存路径",
                DefaultExt = ".jpg"
            };

            if (!string.IsNullOrEmpty(viewModel.SavePath))
            {
                dialog.InitialDirectory = System.IO.Path.GetDirectoryName(viewModel.SavePath);
                dialog.FileName = System.IO.Path.GetFileName(viewModel.SavePath);
            }

            if (dialog.ShowDialog() == true)
            {
                // 通过ViewModel设置，会自动触发UI更新和参数变化事件
                viewModel.SavePath = dialog.FileName;
            }
        };

        pathPanel.Children.Add(pathTextBox);
        pathPanel.Children.Add(selectButton);
        parent.Children.Add(pathPanel);
    }

    private void CreateQualityControls(StackPanel parent, ImageOutputViewModel viewModel)
    {
        var qualityLabel = new Label
        {
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11
        };

        // 使用数据绑定将Label的Content绑定到ViewModel的QualityText属性
        var labelBinding = new System.Windows.Data.Binding("QualityText")
        {
            Source = viewModel,
            Mode = System.Windows.Data.BindingMode.OneWay,
            UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
        };
        qualityLabel.SetBinding(Label.ContentProperty, labelBinding);

        parent.Children.Add(qualityLabel);

        var qualitySlider = new Slider
        {
            Minimum = 1,
            Maximum = 100,
            Margin = new Thickness(0, 0, 0, 10),
            TickFrequency = 10,
            TickPlacement = System.Windows.Controls.Primitives.TickPlacement.BottomRight,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF"))
        };

        // 使用数据绑定将Slider的Value绑定到ViewModel的Quality属性
        var sliderBinding = new System.Windows.Data.Binding("Quality")
        {
            Source = viewModel,
            Mode = System.Windows.Data.BindingMode.TwoWay,
            UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
        };
        qualitySlider.SetBinding(Slider.ValueProperty, sliderBinding);

        parent.Children.Add(qualitySlider);
    }

    private void CreateAutoSaveControls(StackPanel parent, ImageOutputViewModel viewModel)
    {
        var autoSaveCheckBox = new CheckBox
        {
            Content = "处理时自动保存",
            Margin = new Thickness(0, 0, 0, 10),
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11
        };

        // 使用数据绑定将CheckBox的IsChecked绑定到ViewModel的AutoSave属性
        var checkBinding = new System.Windows.Data.Binding("AutoSave")
        {
            Source = viewModel,
            Mode = System.Windows.Data.BindingMode.TwoWay,
            UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
        };
        autoSaveCheckBox.SetBinding(CheckBox.IsCheckedProperty, checkBinding);

        parent.Children.Add(autoSaveCheckBox);
    }

    private void CreateSaveAlphaControls(StackPanel parent, ImageOutputViewModel viewModel)
    {
        var saveAlphaCheckBox = new CheckBox
        {
            Content = "保存透明度通道（PNG/TIFF）",
            Margin = new Thickness(0, 0, 0, 10),
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Arial"),
            FontSize = 11
        };

        // 使用数据绑定将CheckBox的IsChecked绑定到ViewModel的SaveAlpha属性
        var checkBinding = new System.Windows.Data.Binding("SaveAlpha")
        {
            Source = viewModel,
            Mode = System.Windows.Data.BindingMode.TwoWay,
            UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
        };
        saveAlphaCheckBox.SetBinding(CheckBox.IsCheckedProperty, checkBinding);

        parent.Children.Add(saveAlphaCheckBox);
    }

    public override IScriptViewModel CreateViewModel()
    {
        return new ImageOutputViewModel(this);
    }

    public override async Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
    {
        if (parameterName == nameof(SavePath))
        {
            SavePath = newValue?.ToString() ?? string.Empty;
        }
        else if (parameterName == nameof(Quality))
        {
            if (int.TryParse(newValue?.ToString(), out var quality))
                Quality = Math.Clamp(quality, 1, 100);
        }
        else if (parameterName == nameof(AutoSave))
        {
            if (bool.TryParse(newValue?.ToString(), out var autoSave))
                AutoSave = autoSave;
        }
        else if (parameterName == nameof(SaveAlpha))
        {
            if (bool.TryParse(newValue?.ToString(), out var saveAlpha))
                SaveAlpha = saveAlpha;
        }
        await Task.CompletedTask;
    }

    /// <summary>
    /// 序列化参数
    /// </summary>
    /// <returns>参数字典</returns>
    public override Dictionary<string, object> SerializeParameters()
    {

        // 创建序列化数据字典
        var data = new Dictionary<string, object>
        {
            [nameof(SavePath)] = SavePath,
            [nameof(Quality)] = Quality,
            [nameof(AutoSave)] = AutoSave,
            [nameof(SaveAlpha)] = SaveAlpha
        };


        return data;
    }

    /// <summary>
    /// 反序列化参数
    /// </summary>
    /// <param name="data">参数字典</param>
    public override void DeserializeParameters(Dictionary<string, object> data)
    {

        // 恢复参数值
        if (data.TryGetValue(nameof(SavePath), out var path))
        {
            SavePath = path?.ToString() ?? string.Empty;
        }

        if (data.TryGetValue(nameof(Quality), out var quality) && int.TryParse(quality?.ToString(), out var q))
        {
            Quality = Math.Clamp(q, 1, 100);
        }

        if (data.TryGetValue(nameof(AutoSave), out var autoSave) && bool.TryParse(autoSave?.ToString(), out var a))
        {
            AutoSave = a;
        }

        if (data.TryGetValue(nameof(SaveAlpha), out var saveAlpha) && bool.TryParse(saveAlpha?.ToString(), out var sa))
        {
            SaveAlpha = sa;
        }
    }
}

public class ImageOutputViewModel : ScriptViewModelBase
{
    private ImageOutputScript ImageOutputScript => (ImageOutputScript)Script;

    public string SavePath
    {
        get => ImageOutputScript.SavePath;
        set
        {
            if (ImageOutputScript.SavePath != value)
            {
                ImageOutputScript.SavePath = value;
                OnPropertyChanged();

                // 直接触发参数变化
                if (Script is RevivalScriptBase rsb)
                {
                    rsb.OnParameterChanged(nameof(SavePath), value);
                }
            }
        }
    }

    public int Quality
    {
        get => ImageOutputScript.Quality;
        set
        {
            var clampedValue = Math.Clamp(value, 1, 100);
            if (ImageOutputScript.Quality != clampedValue)
            {
                ImageOutputScript.Quality = clampedValue;
                OnPropertyChanged();
                OnPropertyChanged(nameof(QualityText)); // 同时更新QualityText

                // 直接触发参数变化
                if (Script is RevivalScriptBase rsb)
                {
                    rsb.OnParameterChanged(nameof(Quality), clampedValue);
                }
            }
        }
    }

    public string QualityText => $"图像质量: {Quality}";

    public bool AutoSave
    {
        get => ImageOutputScript.AutoSave;
        set
        {
            if (ImageOutputScript.AutoSave != value)
            {
                ImageOutputScript.AutoSave = value;
                OnPropertyChanged();

                // 直接触发参数变化
                if (Script is RevivalScriptBase rsb)
                {
                    rsb.OnParameterChanged(nameof(AutoSave), value);
                }
            }
        }
    }

    public bool SaveAlpha
    {
        get => ImageOutputScript.SaveAlpha;
        set
        {
            if (ImageOutputScript.SaveAlpha != value)
            {
                ImageOutputScript.SaveAlpha = value;
                OnPropertyChanged();

                // 直接触发参数变化
                if (Script is RevivalScriptBase rsb)
                {
                    rsb.OnParameterChanged(nameof(SaveAlpha), value);
                }
            }
        }
    }

    public ImageOutputViewModel(ImageOutputScript script) : base(script) { }

    public override async Task OnParameterChangedAsync(string parameterName, object oldValue, object newValue)
    {
        // 参数变化已经在属性setter中处理，这里不需要额外处理
        await Task.CompletedTask;
    }

    public override ScriptValidationResult ValidateParameter(string parameterName, object value)
    {
        switch (parameterName)
        {
            case nameof(SavePath):
                var path = value?.ToString();
                if (string.IsNullOrEmpty(path))
                    return new ScriptValidationResult(false, "请指定保存路径");
                break;
            case nameof(Quality):
                if (!int.TryParse(value?.ToString(), out var quality) || quality < 1 || quality > 100)
                    return new ScriptValidationResult(false, "质量值必须在1-100之间");
                break;
        }
        return new ScriptValidationResult(true);
    }

    public override Dictionary<string, object> GetParameterData()
    {
        return new Dictionary<string, object>
        {
            [nameof(SavePath)] = SavePath,
            [nameof(Quality)] = Quality,
            [nameof(AutoSave)] = AutoSave,
            [nameof(SaveAlpha)] = SaveAlpha
        };
    }

    public override async Task SetParameterDataAsync(Dictionary<string, object> data)
    {
        await RunOnUIThreadAsync(() =>
        {
            if (data.TryGetValue(nameof(SavePath), out var path))
                SavePath = path?.ToString() ?? string.Empty;

            if (data.TryGetValue(nameof(Quality), out var quality) && int.TryParse(quality?.ToString(), out var q))
                Quality = q;

            if (data.TryGetValue(nameof(AutoSave), out var autoSave) && bool.TryParse(autoSave?.ToString(), out var a))
                AutoSave = a;

            if (data.TryGetValue(nameof(SaveAlpha), out var saveAlpha) && bool.TryParse(saveAlpha?.ToString(), out var sa))
                SaveAlpha = sa;
        });
    }

    public override async Task ResetToDefaultAsync()
    {
        await RunOnUIThreadAsync(() =>
        {
            SavePath = string.Empty;
            Quality = 95;
            AutoSave = true;
            SaveAlpha = true;
        });
    }
}
