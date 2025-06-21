using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using OpenCvSharp;
using Tunnel_Next.Models;
using Tunnel_Next.Services.Scripting;
using Tunnel_Next.Services.UI;
using WRect = System.Windows.Rect;

namespace Tunnel_Next.Scripts
{
    [RevivalScript(Name = "裁切脚本", Author = "AI", Description = "支持交互式裁切", Category = "图像", Color = "#FF7043")]
    public class CropCutScript : RevivalScriptBase, IScriptPreviewProvider
    {
        private bool _croppingMode = false;
        private WRect _cropRectNorm = new(0, 0, 1, 1);
        private Mat? _latestInput;

        public override Dictionary<string, PortDefinition> GetInputPorts() => new() { ["f32bmp"] = new("f32bmp") };
        public override Dictionary<string, PortDefinition> GetOutputPorts() => new() { ["f32bmp"] = new("f32bmp") };

        public override Dictionary<string, object> Process(Dictionary<string, object> inputs, IScriptContext context)
        {
            if (!inputs.TryGetValue("f32bmp", out var obj) || obj is not Mat mat || mat.Empty())
            {
                return new Dictionary<string, object>();
            }

            _latestInput = mat;

            var x = (int)Math.Clamp(_cropRectNorm.X * mat.Width, 0, mat.Width);
            var y = (int)Math.Clamp(_cropRectNorm.Y * mat.Height, 0, mat.Height);
            var w = (int)Math.Clamp(_cropRectNorm.Width * mat.Width, 0, mat.Width - x);
            var h = (int)Math.Clamp(_cropRectNorm.Height * mat.Height, 0, mat.Height - y);

            if (w <= 0 || h <= 0)
            {
                return new Dictionary<string, object>();
            }
            
            var roi = new OpenCvSharp.Rect(x, y, w, h);
            using var subMat = new Mat(mat, roi);
            return new Dictionary<string, object> { ["f32bmp"] = subMat.Clone() };
        }

        public override FrameworkElement CreateParameterControl()
        {
            var panel = new StackPanel { Margin = new Thickness(4) };

            // 手动加载资源
            var resources = new ResourceDictionary();
            try
            {
                resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Tunnel-Next;component/Resources/ScriptsControls/ScriptButtonStyles.xaml", UriKind.RelativeOrAbsolute) });
            }
            catch { /* 资源加载失败，按钮将使用默认样式 */ }
            panel.Resources = resources;
            
            var btn = new Button { 
                Content = "进入裁切模式", 
                Style = panel.TryFindResource("SelectFileScriptButtonStyle") as Style 
            };
            btn.Click += (_, __) => StartCropping();
            panel.Children.Add(btn);
            return panel;
        }

        public bool WantsPreview(PreviewTrigger trigger) => false;

        public FrameworkElement? CreatePreviewControl(PreviewTrigger trigger, IScriptContext ctx)
        {
            if (!_croppingMode) return null;
            return new CroppingPreviewControl(_latestInput, _cropRectNorm, ctx.ZoomLevel, ctx.PreviewScrollX, ctx.PreviewScrollY,
                confirmRect =>
                {
                    if (confirmRect.HasValue && _cropRectNorm != confirmRect.Value)
                    {
                        _cropRectNorm = confirmRect.Value;
                        OnParameterChanged("CropRect", _cropRectNorm);
                    }
                    _croppingMode = false;
                    ctx.RequestPreviewRelease();
                },
                () =>
                {
                    _croppingMode = false;
                    ctx.RequestPreviewRelease();
                },
                () =>
                {
                    var newRect = new WRect(0, 0, 1, 1);
                    if (_cropRectNorm != newRect)
                    {
                        _cropRectNorm = newRect;
                        OnParameterChanged("CropRect", _cropRectNorm);
                    }
                });
        }

        public void OnPreviewReleased() { _croppingMode = false; }

        private void StartCropping()
        {
            _croppingMode = true;
            var ctx = new ScriptContext("", "", "", () => null!, _ => { }, _ => null!, (a, b, c) => { }) { ZoomLevel = PreviewState.Zoom };
            var ctrl = CreatePreviewControl(PreviewTrigger.ParameterWindow, ctx);
            if (ctrl != null) PreviewManager.Instance.RequestTakeover(this, ctrl, PreviewTrigger.ParameterWindow);
        }

        public override IScriptViewModel CreateViewModel() => new CropCutScriptViewModel(this);
        public override Task OnParameterChangedAsync(string name, object old, object @new) => Task.CompletedTask;
        public override Dictionary<string, object> SerializeParameters() => new() { ["CropRect"] = _cropRectNorm.ToString() };
        public override void DeserializeParameters(Dictionary<string, object> data)
        {
            if (data.TryGetValue("CropRect", out var obj) && obj is string s)
            {
                try { _cropRectNorm = WRect.Parse(s); } catch { /* ignore */ }
            }
        }

        private class CroppingPreviewControl : UserControl
        {
            private readonly Canvas _rootCanvas = new();
            private readonly Image _image = new();
            private readonly Rectangle _cropRect = new();
            private readonly List<Thumb> _handles = new();
            private readonly Thumb _dragThumb = new();
            private readonly ScrollViewer _scrollViewer = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = new SolidColorBrush(Color.FromRgb(20, 20, 22)) };
            private readonly ScaleTransform _scaleTransform = new();
            private System.Windows.Point? _lastPanPoint;
            private WRect _rectNorm;
            private readonly double _imgWidth, _imgHeight;
            private readonly Action<WRect?> _onConfirm;
            private readonly Action _onCancel;
            private readonly Action _onReset;

            public CroppingPreviewControl(Mat? image, WRect rectNorm, double zoom, double scrollX, double scrollY, Action<WRect?> onConfirm, Action onCancel, Action onReset)
            {
                _rectNorm = rectNorm;
                _onConfirm = onConfirm;
                _onCancel = onCancel;
                _onReset = onReset;

                // 手动加载资源
                try
                {
                    Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Tunnel-Next;component/Resources/ScriptsControls/ScriptButtonStyles.xaml", UriKind.RelativeOrAbsolute) });
                    Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Tunnel-Next;component/Resources/ScriptsControls/SliderHandleStyles.xaml", UriKind.RelativeOrAbsolute) });
                }
                catch { /* 资源加载失败，控件将使用默认样式 */ }

                var root = new Grid();
                var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(10) };
                var btnOk = MakeButton("确认");
                btnOk.Click += (_,__) => _onConfirm(_rectNorm);
                var btnCancel = MakeButton("取消");
                btnCancel.Click += (_,__) => _onCancel();
                var btnReset = MakeButton("重置");
                btnReset.Click += (_,__) => ResetRect();

                btnPanel.Children.Add(btnOk);
                btnPanel.Children.Add(btnCancel);
                btnPanel.Children.Add(btnReset);

                _rootCanvas.LayoutTransform = _scaleTransform;
                _rootCanvas.Children.Add(_image);
                _scrollViewer.Content = _rootCanvas;
                _scrollViewer.MouseWheel += OnMouseWheel;
                _scrollViewer.MouseRightButtonDown += OnPanStart;
                _scrollViewer.MouseRightButtonUp += OnPanEnd;
                _scrollViewer.MouseMove += OnPanMove;

                root.Children.Add(_scrollViewer);
                root.Children.Add(btnPanel);
                Content = root;

                if (image != null && !image.Empty())
                {
                    _imgWidth = image.Width;
                    _imgHeight = image.Height;
                    _image.Source = ConvertF32MatToBitmapSource(image);
                    _rootCanvas.Width = _imgWidth;
                    _rootCanvas.Height = _imgHeight;
                }
                
                BuildOverlay();
                Loaded += (_, __) => SetInitialView(zoom, scrollX, scrollY);
            }

            private void SetInitialView(double zoom, double scrollX, double scrollY)
            {
                _scaleTransform.ScaleX = zoom;
                _scaleTransform.ScaleY = zoom;
                Dispatcher.BeginInvoke(new Action(() => {
                    _scrollViewer.ScrollToHorizontalOffset(scrollX);
                    _scrollViewer.ScrollToVerticalOffset(scrollY);
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }

            private void OnMouseWheel(object s, MouseWheelEventArgs e)
            {
                e.Handled = true;
                var point = e.GetPosition(_scrollViewer);
                var zoomFactor = e.Delta > 0 ? 1.2 : 1 / 1.2;
                var newScale = Math.Clamp(_scaleTransform.ScaleX * zoomFactor, 0.1, 10);
                var oldScale = _scaleTransform.ScaleX;
                if (Math.Abs(oldScale - newScale) < 0.001) return;

                _scaleTransform.ScaleX = newScale;
                _scaleTransform.ScaleY = newScale;

                var newScrollX = (point.X + _scrollViewer.HorizontalOffset) * (newScale / oldScale) - point.X;
                var newScrollY = (point.Y + _scrollViewer.VerticalOffset) * (newScale / oldScale) - point.Y;
                _scrollViewer.ScrollToHorizontalOffset(newScrollX);
                _scrollViewer.ScrollToVerticalOffset(newScrollY);
            }

            private void OnPanStart(object s, MouseButtonEventArgs e)
            {
                if (e.ChangedButton == MouseButton.Right)
                {
                    _lastPanPoint = e.GetPosition(_scrollViewer);
                    _scrollViewer.Cursor = Cursors.Hand;
                    _scrollViewer.CaptureMouse();
                }
            }

            private void OnPanEnd(object s, MouseButtonEventArgs e)
            {
                if (e.ChangedButton == MouseButton.Right)
                {
                    _scrollViewer.ReleaseMouseCapture();
                    _scrollViewer.Cursor = Cursors.Arrow;
                    _lastPanPoint = null;
                }
            }

            private void OnPanMove(object s, MouseEventArgs e)
            {
                if (_lastPanPoint.HasValue)
                {
                    var currentPoint = e.GetPosition(_scrollViewer);
                    var delta = (System.Windows.Point)(currentPoint - _lastPanPoint.Value);
                    _scrollViewer.ScrollToHorizontalOffset(_scrollViewer.HorizontalOffset - delta.X);
                    _scrollViewer.ScrollToVerticalOffset(_scrollViewer.VerticalOffset - delta.Y);
                    _lastPanPoint = currentPoint;
                }
            }

            private void BuildOverlay()
            {
                _rootCanvas.Background = Brushes.Transparent;

                // 定义新的样式
                var highlightBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF0078D7")); // AeroHighlightColor
                highlightBrush.Freeze();
                var fillBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#330078D7")); // AeroHighlightColor with ~20% opacity
                fillBrush.Freeze();
                var handleStrokeBrush = new SolidColorBrush(Colors.White) { Opacity = 0.75 };
                handleStrokeBrush.Freeze();
                
                // 裁切框
                _cropRect.Stroke = highlightBrush;
                _cropRect.StrokeThickness = 1;
                _cropRect.StrokeDashArray = new DoubleCollection { 4, 2 };
                _cropRect.Fill = fillBrush;
                _rootCanvas.Children.Add(_cropRect);

                // 控制点模板
                var handleTemplate = new ControlTemplate(typeof(Thumb));
                var ellipseFactory = new FrameworkElementFactory(typeof(Ellipse));
                ellipseFactory.SetValue(Shape.FillProperty, new SolidColorBrush(Colors.White) { Opacity = 0.5 });
                ellipseFactory.SetValue(Shape.StrokeProperty, highlightBrush);
                ellipseFactory.SetValue(Shape.StrokeThicknessProperty, 1.5);
                handleTemplate.VisualTree = ellipseFactory;

                for (int i = 0; i < 8; i++)
                {
                    var thumb = new Thumb 
                    { 
                        Width = 10, 
                        Height = 10, 
                        Template = handleTemplate
                    };
                    thumb.DragDelta += HandleThumbDrag;
                    _handles.Add(thumb);
                    _rootCanvas.Children.Add(thumb);
                }

                _dragThumb.Cursor = Cursors.SizeAll;
                _dragThumb.Opacity = 0.01;
                _dragThumb.DragDelta += HandleDragWhole;
                _rootCanvas.Children.Add(_dragThumb);

                UpdateCropVisual();
            }

            private void HandleThumbDrag(object s, DragDeltaEventArgs e)
            {
                var thumb = s as Thumb;
                if (thumb == null || (_imgWidth <= 0 || _imgHeight <= 0)) return;
                
                var dx = e.HorizontalChange / _scaleTransform.ScaleX;
                var dy = e.VerticalChange / _scaleTransform.ScaleY;
                var pxRect = GetPixelRect();

                var left = pxRect.Left;
                var top = pxRect.Top;
                var right = pxRect.Right;
                var bottom = pxRect.Bottom;

                switch (_handles.IndexOf(thumb))
                {
                    case 0: left += dx; top += dy; break;       // Top-Left
                    case 1: top += dy; break;                   // Top-Center
                    case 2: right += dx; top += dy; break;      // Top-Right
                    case 3: left += dx; break;                  // Middle-Left
                    case 4: right += dx; break;                 // Middle-Right
                    case 5: left += dx; bottom += dy; break;    // Bottom-Left
                    case 6: bottom += dy; break;                // Bottom-Center
                    case 7: right += dx; bottom += dy; break;   // Bottom-Right
                }

                // 处理反向拖动（翻转）
                if (left > right) { var temp = left; left = right; right = temp; }
                if (top > bottom) { var temp = top; top = bottom; bottom = temp; }

                // 限制在图像边界内
                left = Math.Clamp(left, 0, _imgWidth);
                right = Math.Clamp(right, 0, _imgWidth);
                top = Math.Clamp(top, 0, _imgHeight);
                bottom = Math.Clamp(bottom, 0, _imgHeight);
                
                var newPxRect = new WRect(left, top, right - left, bottom - top);
                
                _rectNorm = new WRect(newPxRect.X / _imgWidth, newPxRect.Y / _imgHeight, newPxRect.Width / _imgWidth, newPxRect.Height / _imgHeight);
                UpdateCropVisual();
            }

            private void HandleDragWhole(object s, DragDeltaEventArgs e)
            {
                if (_imgWidth <= 0 || _imgHeight <= 0) return;
                
                var dx = e.HorizontalChange / _scaleTransform.ScaleX;
                var dy = e.VerticalChange / _scaleTransform.ScaleY;
                var pxRect = GetPixelRect();

                pxRect.X = Math.Clamp(pxRect.X + dx, 0, _imgWidth - pxRect.Width);
                pxRect.Y = Math.Clamp(pxRect.Y + dy, 0, _imgHeight - pxRect.Height);
                
                _rectNorm = new WRect(pxRect.X / _imgWidth, pxRect.Y / _imgHeight, pxRect.Width / _imgWidth, pxRect.Height / _imgHeight);
                UpdateCropVisual();
            }

            private WRect GetPixelRect() => new(_rectNorm.X * _imgWidth, _rectNorm.Y * _imgHeight, _rectNorm.Width * _imgWidth, _rectNorm.Height * _imgHeight);

            private void UpdateCropVisual()
            {
                var pxRect = GetPixelRect();
                Canvas.SetLeft(_cropRect, pxRect.X);
                Canvas.SetTop(_cropRect, pxRect.Y);
                _cropRect.Width = pxRect.Width;
                _cropRect.Height = pxRect.Height;

                var positions = new[] {
                    new System.Windows.Point(pxRect.Left, pxRect.Top), new System.Windows.Point(pxRect.Left + pxRect.Width / 2, pxRect.Top), new System.Windows.Point(pxRect.Right, pxRect.Top),
                    new System.Windows.Point(pxRect.Left, pxRect.Top + pxRect.Height / 2), new System.Windows.Point(pxRect.Right, pxRect.Top + pxRect.Height / 2),
                    new System.Windows.Point(pxRect.Left, pxRect.Bottom), new System.Windows.Point(pxRect.Left + pxRect.Width / 2, pxRect.Bottom), new System.Windows.Point(pxRect.Right, pxRect.Bottom)
                };
                for (int i = 0; i < _handles.Count; i++)
                {
                    Canvas.SetLeft(_handles[i], positions[i].X - _handles[i].Width / 2);
                    Canvas.SetTop(_handles[i], positions[i].Y - _handles[i].Height / 2);
                }

                Canvas.SetLeft(_dragThumb, pxRect.X);
                Canvas.SetTop(_dragThumb, pxRect.Y);
                _dragThumb.Width = pxRect.Width;
                _dragThumb.Height = pxRect.Height;
            }

            private Button MakeButton(string text)
            {
                return new Button { 
                    Content = text, 
                    Margin = new Thickness(4, 0, 0, 0), 
                    Padding = new Thickness(12, 4, 12, 4),
                    Style = TryFindResource("SelectFileScriptButtonStyle") as Style
                };
            }

            private void ResetRect()
            {
                _rectNorm = new WRect(0, 0, 1, 1);
                UpdateCropVisual();
                _onReset();
            }

            private BitmapSource ConvertF32MatToBitmapSource(Mat mat)
            {
                int width = mat.Width;
                int height = mat.Height;
                int channels = mat.Channels();
                var buffer = new byte[width * height * 4];

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int i = (y * width + x) * 4;
                        if (channels == 1)
                        {
                            var val = (byte)(Math.Clamp(mat.At<float>(y, x), 0f, 1f) * 255);
                            buffer[i] = buffer[i + 1] = buffer[i + 2] = val; buffer[i + 3] = 255;
                        }
                        else if (channels == 3)
                        {
                            var p = mat.At<Vec3f>(y, x);
                            buffer[i] = (byte)(Math.Clamp(p.Item0, 0f, 1f) * 255);
                            buffer[i + 1] = (byte)(Math.Clamp(p.Item1, 0f, 1f) * 255);
                            buffer[i + 2] = (byte)(Math.Clamp(p.Item2, 0f, 1f) * 255);
                            buffer[i + 3] = 255;
                        }
                        else if (channels == 4)
                        {
                            var p = mat.At<Vec4f>(y, x);
                            buffer[i] = (byte)(Math.Clamp(p.Item0, 0f, 1f) * 255);
                            buffer[i + 1] = (byte)(Math.Clamp(p.Item1, 0f, 1f) * 255);
                            buffer[i + 2] = (byte)(Math.Clamp(p.Item2, 0f, 1f) * 255);
                            buffer[i + 3] = (byte)(Math.Clamp(p.Item3, 0f, 1f) * 255);
                        }
                    }
                }
                var bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, buffer, width * 4);
                bmp.Freeze();
                return bmp;
            }
        }

        private class CropCutScriptViewModel : ScriptViewModelBase
        {
            public CropCutScriptViewModel(IRevivalScript script) : base(script) { }
            public override Task OnParameterChangedAsync(string name, object old, object @new) => Task.CompletedTask;
            public override ScriptValidationResult ValidateParameter(string name, object val) => new(true);
            public override Dictionary<string, object> GetParameterData() => new();
            public override Task SetParameterDataAsync(Dictionary<string, object> data) => Task.CompletedTask;
            public override Task ResetToDefaultAsync() => Task.CompletedTask;
        }
    }
}