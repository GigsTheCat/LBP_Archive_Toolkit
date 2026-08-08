using System;
using System.Windows.Input;

namespace LbpArchiveToolkit.ViewModels
{
    public class ImageCropDialogViewModel : ViewModelBase
    {
        public object? ImageSource { get; set => SetProperty(ref field, value); }

        public ICommand CancelCommand { get; }
        public ICommand ApplyCommand { get; }

        public Action? RequestCancel { get; set; }
        public Action? RequestApply { get; set; }

        public ImageCropDialogViewModel()
        {
            CancelCommand = new RelayCommand(_ => RequestCancel?.Invoke());
            ApplyCommand = new RelayCommand(_ => RequestApply?.Invoke());
        }
    }
}