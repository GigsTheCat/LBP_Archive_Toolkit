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

        protected readonly IViewService? BaseViewService;

        public ViewModelBase(IViewService? viewService = null)
        {
            BaseViewService = viewService;
            CopyImageCommand = new RelayCommand(ExecuteCopyImage);
            SaveImageCommand = new RelayCommand(ExecuteSaveImage);
        }

        private void ExecuteCopyImage(object? parameter)
        {
            if (parameter != null && BaseViewService != null)
            {
                try
                {
                    BaseViewService.SetClipboardImage(parameter);
                    BaseViewService.ShowToast("Image Copied!", "Mouse");
                }
                catch { }
            }
        }

        private void ExecuteSaveImage(object? parameter)
        {
            if (parameter != null && BaseViewService != null)
            {
                string? fileName = BaseViewService.ShowSaveFileDialog("PNG Image|*.png", "Save Image", "icon.png");

                if (fileName != null)
                {
                    try
                    {
                        BaseViewService.SaveImageToFile(parameter, fileName);
                        BaseViewService.ShowToast("Image Saved!", "ContextElement");
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