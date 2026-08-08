using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

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

        // Expose a static locator to decouple from System.Windows
        public static IViewService? GlobalViewService { get; set; }

        private IViewService? GetViewService() => GlobalViewService;

        private void ExecuteCopyImage(object? parameter)
        {
            if (parameter != null)
            {
                try
                {
                    var viewService = GetViewService();
                    if (viewService != null)
                    {
                        viewService.SetClipboardImage(parameter);
                        viewService.ShowToast("Image Copied!", "Mouse");
                    }
                }
                catch { }
            }
        }

        private void ExecuteSaveImage(object? parameter)
        {
            if (parameter != null)
            {
                var viewService = GetViewService();
                if (viewService == null) return;

                string? fileName = viewService.ShowSaveFileDialog("PNG Image|*.png", "Save Image", "icon.png");

                if (fileName != null)
                {
                    try
                    {
                        viewService.SaveImageToFile(parameter, fileName);
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