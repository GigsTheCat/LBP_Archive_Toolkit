using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LbpArchiveToolkit.ViewModels
{
    public class ViewModelBase : INotifyPropertyChanged
    {
        public ICommand CopyImageCommand { get; }
        public ICommand SaveImageCommand { get; }

        public ViewModelBase()
        {
            CopyImageCommand = new RelayCommand(ExecuteCopyImage);
            SaveImageCommand = new RelayCommand(ExecuteSaveImage);
        }

        private void ExecuteCopyImage(object? parameter)
        {
            if (parameter is BitmapSource bmp)
            {
                try
                {
                    var viewService = Application.Current?.MainWindow as IViewService;
                    if (viewService != null)
                    {
                        viewService.SetClipboardImage(bmp);
                        viewService.ShowToast("Image Copied!", "Mouse");
                    }
                }
                catch { }
            }
        }

        private void ExecuteSaveImage(object? parameter)
        {
            if (parameter is BitmapSource bmp)
            {
                var viewService = Application.Current?.MainWindow as IViewService;
                if (viewService == null) return;

                string? fileName = viewService.ShowSaveFileDialog("PNG Image|*.png", "Save Image", "icon.png");

                if (fileName != null)
                {
                    try
                    {
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(bmp));
                        using var fs = File.Create(fileName);
                        encoder.Save(fs);
                        viewService.ShowToast("Image Saved!", "ContextElement");
                    }
                    catch { }
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}