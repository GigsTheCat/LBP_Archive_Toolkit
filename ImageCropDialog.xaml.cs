using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LbpArchiveToolkit
{
    public partial class ImageCropDialog : Window
    {
        public string? CroppedImagePath { get; private set; }

        private Point _startPoint;
        private double _originX;
        private double _originY;
        private bool _isDragging = false;
        private bool _isInitializing = false;

        public ImageCropDialog(string imagePath)
        {
            InitializeComponent();
            LoadImage(imagePath);
        }

        private void LoadImage(string imagePath)
        {
            _isInitializing = true;
            try
            {
                var bmp = new BitmapImage();
                using (var ms = new FileStream(imagePath, FileMode.Open, FileAccess.Read))
                {
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                }
                bmp.Freeze();
                imgSource.Source = bmp;

                double imgW = bmp.PixelWidth;
                double imgH = bmp.PixelHeight;

                // Force physical pixel mapping to bypass WPF's internal DPI logic
                imgSource.Width = imgW;
                imgSource.Height = imgH;

                // Establish the baseline scale required to fill the viewport boundary
                double minScale = Math.Max(320.0 / imgW, 176.0 / imgH);
                
                // Temporarily open the slider bounds wide to prevent clamping bugs
                sliderZoom.Minimum = 0.001;
                sliderZoom.Maximum = 10000.0;
                
                sliderZoom.Value = minScale; 
                
                // Now apply correct limits based on the scale
                sliderZoom.Minimum = minScale * 0.5;
                sliderZoom.Maximum = Math.Max(minScale * 6.0, 4.0);

                // Exactly center the image in the 480x360 workspace based on the starting scale
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
            {
                DragMove();
            }
        }

        private void GridWorkspace_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (imgSource.Source == null) return;
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
            double offsetX = currentPoint.X - _startPoint.X;
            double offsetY = currentPoint.Y - _startPoint.Y;

            translateTransform.X = _originX + offsetX;
            translateTransform.Y = _originY + offsetY;
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
            if (imgSource.Source == null) return;

            // Zoom dynamically based on 10% of current scale for smooth scrolling
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

            // Lock the zoom anchor to the center of the Canvas workspace
            double cx = 240.0;
            double cy = 180.0;

            double dx = cx - translateTransform.X;
            double dy = cy - translateTransform.Y;

            // Adjust X/Y translations dynamically so the image scales uniformly from the center
            translateTransform.X = cx - (dx * ratio);
            translateTransform.Y = cy - (dy * ratio);

            scaleTransform.ScaleX = newScale;
            scaleTransform.ScaleY = newScale;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                RenderTargetBitmap rtb = new RenderTargetBitmap(480, 360, 96, 96, PixelFormats.Pbgra32);
                
                canvasWorkspace.Measure(new Size(480, 360));
                canvasWorkspace.Arrange(new Rect(new Size(480, 360)));
                rtb.Render(canvasWorkspace);

                CroppedBitmap cropped = new CroppedBitmap(rtb, new Int32Rect(80, 92, 320, 176));

                PngBitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(cropped));

                string tempFile = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".png");
                using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write))
                {
                    encoder.Save(fs);
                }

                CroppedImagePath = tempFile;
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                CustomDialog.Show(this, $"Failed to process cropped icon:\n{ex.Message}", "Error");
            }
        }
    }
}