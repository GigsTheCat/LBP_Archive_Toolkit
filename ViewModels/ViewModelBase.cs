using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

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
            if (parameter is ImageBrush brush && brush.ImageSource is BitmapSource bmp)
            {
                try
                {
                    Clipboard.SetImage(bmp);
                    (Application.Current?.MainWindow as IViewService)?.ShowToast("Image Copied!", "Mouse");
                }
                catch { }
            }
        }

        private void ExecuteSaveImage(object? parameter)
        {
            if (parameter is ImageBrush brush && brush.ImageSource is BitmapSource bmp)
            {
                var dlg = new SaveFileDialog
                {
                    Filter = "PNG Image|*.png",
                    Title = "Save Image",
                    FileName = "icon.png"
                };

                if (dlg.ShowDialog() == true)
                {
                    try
                    {
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(bmp));
                        using var fs = File.Create(dlg.FileName);
                        encoder.Save(fs);
                        (Application.Current?.MainWindow as IViewService)?.ShowToast("Image Saved!", "ContextElement");
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