using LbpArchiveToolkit.Utils;
using Microsoft.Win32;
using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace LbpArchiveToolkit.ViewModels
{
    public class EditInfoDialogViewModel : ViewModelBase
    {
        private readonly Window _ownerWindow;

        private string _levelName = "";
        public string LevelName
        {
            get => _levelName;
            set
            {
                if (SetProperty(ref _levelName, value))
                    OnPropertyChanged(nameof(TitleCountText));
            }
        }

        private string _description = "";
        public string Description
        {
            get => _description;
            set
            {
                if (SetProperty(ref _description, value))
                    OnPropertyChanged(nameof(DescCountText));
            }
        }

        // Dynamically compute character counts purely through binding evaluation
        public string TitleCountText => $"{LevelName?.Length ?? 0} / 100";
        public string DescCountText => $"{Description?.Length ?? 0} / 1000";

        private string? _newIconPath;
        public string? NewIconPath
        {
            get => _newIconPath;
            private set => SetProperty(ref _newIconPath, value);
        }

        private ImageSource? _iconImage;
        public ImageSource? IconImage
        {
            get => _iconImage;
            private set => SetProperty(ref _iconImage, value);
        }

        // Commands
        public ICommand ChangeIconCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        public Action<bool>? RequestClose { get; set; }

        public EditInfoDialogViewModel(Window ownerWindow, string currentName, string currentDesc, string? currentIconPath)
        {
            _ownerWindow = ownerWindow;
            LevelName = currentName;
            Description = currentDesc;

            if (File.Exists(currentIconPath))
            {
                try
                {
                    IconImage = TextureDecoder.LoadBitmapImage(currentIconPath);
                }
                catch (Exception ex)
                {
                    LogManager.Log("EditInfoDialogViewModel.Constructor", ex);
                }
            }

            ChangeIconCommand = new RelayCommand(_ => ExecuteChangeIcon());
            SaveCommand = new RelayCommand(_ => RequestClose?.Invoke(true));
            CancelCommand = new RelayCommand(_ => RequestClose?.Invoke(false));
        }

        private void ExecuteChangeIcon()
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Images (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg|All files (*.*)|*.*",
                Title = "Select New Icon"
            };

            if (dlg.ShowDialog() == true)
            {
                var cropDialog = new ImageCropDialog(dlg.FileName)
                {
                    Owner = _ownerWindow
                };

                if (cropDialog.ShowDialog() == true)
                {
                    if (!string.IsNullOrEmpty(NewIconPath) && NewIconPath != cropDialog.CroppedImagePath && NewIconPath.Contains(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase))
                    {
                        try { File.Delete(NewIconPath); } catch (Exception ex) { LogManager.Log("EditInfoDialogViewModel.ExecuteChangeIcon", ex); }
                    }

                    NewIconPath = cropDialog.CroppedImagePath;
                    try
                    {
                        IconImage = TextureDecoder.LoadBitmapImage(NewIconPath!);
                    }
                    catch
                    {
                        CustomDialog.Show(_ownerWindow, "Failed to load the cropped image preview.", "Error");
                        NewIconPath = null;
                    }
                }
            }
        }

        public void CleanupOnCancel()
        {
            if (!string.IsNullOrEmpty(NewIconPath) && NewIconPath.Contains(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(NewIconPath); } catch (Exception ex) { LogManager.Log("EditInfoDialogViewModel.CleanupOnCancel", ex); }
            }
        }
    }
}