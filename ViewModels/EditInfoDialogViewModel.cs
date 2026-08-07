using LbpArchiveToolkit.Utils;
using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace LbpArchiveToolkit.ViewModels
{
    public class EditInfoDialogViewModel : ViewModelBase
    {
        private readonly IViewService _viewService;
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

        public bool IsEditIconVisible => File.Exists(NewIconPath ?? _originalIconPath);

        public Action<bool>? RequestClose { get; set; }

        public EditInfoDialogViewModel(IViewService viewService, string currentName, string currentDesc, string? currentIconPath, bool isLocked, bool isSubLevel, bool isShareable)
        {
            _viewService = viewService;
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

            string? croppedPath = _viewService.ShowImageCropDialog(pathToEdit);

            if (croppedPath != null)
            {
                if (!string.IsNullOrEmpty(NewIconPath) && NewIconPath != croppedPath && NewIconPath.Contains(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Delete(NewIconPath); } catch (Exception ex) { LogManager.Log("EditInfoDialogViewModel.ExecuteEditIcon", ex); }
                }

                NewIconPath = croppedPath;
                try
                {
                    IconImage = TextureDecoder.LoadBitmapImage(NewIconPath!);
                }
                catch
                {
                    _viewService.Alert("Failed to load the cropped image preview.", "Error");
                        NewIconPath = null;
                }
                OnPropertyChanged(nameof(IsEditIconVisible));
            }
        }

        private void ExecuteChangeIcon()
        {
            string? fileName = _viewService.ShowOpenFileDialog("Images (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg|All files (*.*)|*.*", "Select New Icon");

            if (fileName != null)
            {
                string? croppedPath = _viewService.ShowImageCropDialog(fileName);

                if (croppedPath != null)
                {
                    if (!string.IsNullOrEmpty(NewIconPath) && NewIconPath != croppedPath && NewIconPath.Contains(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase))
                    {
                        try { File.Delete(NewIconPath); } catch (Exception ex) { LogManager.Log("EditInfoDialogViewModel.ExecuteChangeIcon", ex); }
                    }

                    NewIconPath = croppedPath;
                    try
                    {
                        IconImage = TextureDecoder.LoadBitmapImage(NewIconPath!);
                    }
                    catch
                    {
                        _viewService.Alert("Failed to load the cropped image preview.", "Error");
                        NewIconPath = null;
                    }
                    OnPropertyChanged(nameof(IsEditIconVisible));
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