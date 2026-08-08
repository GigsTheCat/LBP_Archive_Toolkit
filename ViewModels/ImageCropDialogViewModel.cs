using System;
using System.Windows.Input;
namespace LbpArchiveToolkit.ViewModels
{
    public class ImageCropDialogViewModel : ViewModelBase
    {
        public object? ImageSource { get; set => SetProperty(ref field, value); }

        public string? CroppedImagePath { get; private set; }

        public ICommand CancelCommand { get; }

        public Action<bool>? RequestClose { get; set; }

        public ImageCropDialogViewModel()
        {
            CancelCommand = new RelayCommand(_ => RequestClose?.Invoke(false));
        }

        public void ConfirmCrop(string imagePath)
        {
            CroppedImagePath = imagePath;
            RequestClose?.Invoke(true);
        }
    }
}