using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LbpArchiveToolkit.ViewModels;

namespace LbpArchiveToolkit
{
    public partial class ImageCropDialog : Window
    {
        public string? CroppedImagePath => _viewModel.CroppedImagePath;

        private readonly ImageCropDialogViewModel _viewModel;
        private Point _startPoint;
        private double _originX;
        private double _originY;
        private bool _isDragging = false;
        private bool _isInitializing = false;

        public ImageCropDialog(string imagePath)
        {
            InitializeComponent();
            _viewModel = new ImageCropDialogViewModel();
            
            _viewModel.RequestClose += (result) => { DialogResult = result; Close(); };
            
            DataContext = _viewModel;
            LoadImage(imagePath);
        }

        private void LoadImage(string imagePath)
        {
            _isInitializing = true;
            try
            {
                var bmp = LbpArchiveToolkit.Utils.TextureDecoder.LoadBitmapImage(imagePath);
                _viewModel.ImageSource = bmp;

                double imgW = bmp.PixelWidth;
                double imgH = bmp.PixelHeight;

                imgSource.Width = imgW;
                imgSource.Height = imgH;

                double minScale = Math.Max(320.0 / imgW, 176.0 / imgH);

                sliderZoom.Minimum = 0.001;
                sliderZoom.Maximum = 10000.0;
                sliderZoom.Value = minScale;

                sliderZoom.Minimum = minScale * 0.5;
                sliderZoom.Maximum = Math.Max(minScale * 6.0, 4.0);

                double scaledW = imgW * minScale;
                double scaledH = imgH * minScale;

                translateTransform.X = (480.0 - scaledW) / 2.0;
                translateTransform.Y = (360.0 - scaledH) / 2.0;

                scaleTransform.ScaleX = minScale;
                scaleTransform.ScaleY = minScale;
            }
            catch (Exception ex)
            {
                CustomDialog.Show(this, $"Failed to load image for cropping:\n{ex.Message}", "Error");
                Close();
            }
            finally
            {
                _isInitializing = false;
            }
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed && !_isDragging)
                DragMove();
        }

        private void GridWorkspace_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_viewModel.ImageSource == null) return;
            gridWorkspace.CaptureMouse();
            _startPoint = e.GetPosition(gridWorkspace);
            _originX = translateTransform.X;
            _originY = translateTransform.Y;
            _isDragging = true;
            e.Handled = true;
        }

        private void GridWorkspace_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging) return;
            Point currentPoint = e.GetPosition(gridWorkspace);
            translateTransform.X = _originX + (currentPoint.X - _startPoint.X);
            translateTransform.Y = _originY + (currentPoint.Y - _startPoint.Y);
        }

        private void GridWorkspace_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                gridWorkspace.ReleaseMouseCapture();
                _isDragging = false;
            }
        }

        private void GridWorkspace_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_viewModel.ImageSource == null) return;
            double zoomStep = sliderZoom.Value * 0.1;
            double currentScale = sliderZoom.Value;

            if (e.Delta > 0)
                sliderZoom.Value = Math.Min(sliderZoom.Maximum, currentScale + zoomStep);
            else
                sliderZoom.Value = Math.Max(sliderZoom.Minimum, currentScale - zoomStep);

            e.Handled = true;
        }

        private void SliderZoom_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (scaleTransform == null || translateTransform == null || _isInitializing) return;

            double oldScale = scaleTransform.ScaleX;
            double newScale = e.NewValue;
            if (oldScale == 0) return;

            double ratio = newScale / oldScale;
            double cx = 240.0;
            double cy = 180.0;

            translateTransform.X = cx - ((cx - translateTransform.X) * ratio);
            translateTransform.Y = cy - ((cy - translateTransform.Y) * ratio);

            scaleTransform.ScaleX = newScale;
            scaleTransform.ScaleY = newScale;
        }

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // UI rasterization is strictly a View responsibility
                DpiScale dpi = VisualTreeHelper.GetDpi(canvasWorkspace);
                int rtbW = (int)Math.Round(480 * dpi.DpiScaleX);
                int rtbH = (int)Math.Round(360 * dpi.DpiScaleY);

                RenderTargetBitmap rtb = new RenderTargetBitmap(rtbW, rtbH, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
                canvasWorkspace.Measure(new Size(480, 360));
                canvasWorkspace.Arrange(new Rect(new Size(480, 360)));
                rtb.Render(canvasWorkspace);

                int cropX = Math.Max(0, Math.Min((int)Math.Round(80 * dpi.DpiScaleX), rtbW - 1));
                int cropY = Math.Max(0, Math.Min((int)Math.Round(92 * dpi.DpiScaleY), rtbH - 1));
                int cropW = Math.Max(1, Math.Min((int)Math.Round(320 * dpi.DpiScaleX), rtbW - cropX));
                int cropH = Math.Max(1, Math.Min((int)Math.Round(176 * dpi.DpiScaleY), rtbH - cropY));

                CroppedBitmap cropped = new CroppedBitmap(rtb, new Int32Rect(cropX, cropY, cropW, cropH));
                BitmapSource finalBitmap = cropped;

                if (dpi.DpiScaleX != 1.0 || dpi.DpiScaleY != 1.0)
                {
                    var scale = new ScaleTransform(1.0 / dpi.DpiScaleX, 1.0 / dpi.DpiScaleY);
                    finalBitmap = new TransformedBitmap(cropped, scale);
                }

                PngBitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(finalBitmap));

                string tempFile = Path.Combine(Path.GetTempPath(), $"LbpArchiveToolkit_TempCrop_{Guid.NewGuid():N}.png");
                using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write))
                {
                    encoder.Save(fs);
                }

                // Pass the platform-agnostic string to the ViewModel to handle the business state
                _viewModel.ConfirmCrop(tempFile);
            }
            catch (Exception ex)
            {
                CustomDialog.Show(this, $"Failed to process cropped icon:\n{ex.Message}", "Error");
            }
        }
    }
}