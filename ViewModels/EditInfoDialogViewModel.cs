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
        private readonly string? _originalIconPath;

        public string LevelName
        {
            get;
            set
            {
                if (SetProperty(ref field, value))
                    OnPropertyChanged(nameof(TitleCountText));
            }
        } = "";

        public string Description
        {
            get;
            set
            {
                if (SetProperty(ref field, value))
                    OnPropertyChanged(nameof(DescCountText));
            }
        } = "";

        // Dynamically compute character counts purely through binding evaluation
        public string TitleCountText => $"{LevelName?.Length ?? 0} / 100";
        public string DescCountText => $"{Description?.Length ?? 0} / 1000";

        public bool IsLocked { get; set => SetProperty(ref field, value); }
        public bool IsSubLevel { get; set => SetProperty(ref field, value); }
        public bool IsShareable { get; set => SetProperty(ref field, value); }
        public string? NewIconPath { get; private set => SetProperty(ref field, value); }
        public ImageSource? IconImage { get; private set => SetProperty(ref field, value); }

        // Commands
        public ICommand ChangeIconCommand { get; }
        public ICommand EditIconCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        public Visibility EditIconVisibility => File.Exists(NewIconPath ?? _originalIconPath) ? Visibility.Visible : Visibility.Collapsed;

        public Action<bool>? RequestClose { get; set; }

        public EditInfoDialogViewModel(Window ownerWindow, string currentName, string currentDesc, string? currentIconPath, bool isLocked, bool isSubLevel, bool isShareable)
        {
            _ownerWindow = ownerWindow;
            _originalIconPath = currentIconPath;
            LevelName = currentName;
            Description = currentDesc;
            IsLocked = isLocked;
            IsSubLevel = isSubLevel;
            IsShareable = isShareable;

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
            EditIconCommand = new RelayCommand(_ => ExecuteEditIcon());
            SaveCommand = new RelayCommand(_ => RequestClose?.Invoke(true));
            CancelCommand = new RelayCommand(_ => RequestClose?.Invoke(false));
        }

        private void ExecuteEditIcon()
        {
            string? pathToEdit = NewIconPath ?? _originalIconPath;
            if (string.IsNullOrEmpty(pathToEdit) || !File.Exists(pathToEdit)) return;

            var cropDialog = new ImageCropDialog(pathToEdit)
            {
                Owner = _ownerWindow
            };

            if (cropDialog.ShowDialog() == true)
            {
                if (!string.IsNullOrEmpty(NewIconPath) && NewIconPath != cropDialog.CroppedImagePath && NewIconPath.Contains(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Delete(NewIconPath); } catch (Exception ex) { LogManager.Log("EditInfoDialogViewModel.ExecuteEditIcon", ex); }
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
                OnPropertyChanged(nameof(EditIconVisibility));
            }
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
                    OnPropertyChanged(nameof(EditIconVisibility));
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