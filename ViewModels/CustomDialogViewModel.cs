using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace LbpArchiveToolkit.ViewModels
{
    public class CustomDialogViewModel : ViewModelBase
    {
        private string _title = "";
        public string Title { get => _title; set => SetProperty(ref _title, value); }

        private string _message = "";
        public string Message { get => _message; set => SetProperty(ref _message, value); }

        private Visibility _yesNoVisibility = Visibility.Collapsed;
        public Visibility YesNoVisibility { get => _yesNoVisibility; set => SetProperty(ref _yesNoVisibility, value); }

        private Visibility _okVisibility = Visibility.Visible;
        public Visibility OkVisibility { get => _okVisibility; set => SetProperty(ref _okVisibility, value); }

        private Visibility _copyVisibility = Visibility.Collapsed;
        public Visibility CopyVisibility { get => _copyVisibility; set => SetProperty(ref _copyVisibility, value); }

        private string _copyButtonText = "COPY";
        public string CopyButtonText { get => _copyButtonText; set => SetProperty(ref _copyButtonText, value); }

        private Visibility _checkboxVisibility = Visibility.Collapsed;
        public Visibility CheckboxVisibility { get => _checkboxVisibility; set => SetProperty(ref _checkboxVisibility, value); }

        private string _checkboxText = "";
        public string CheckboxText { get => _checkboxText; set => SetProperty(ref _checkboxText, value); }

        private bool _isCheckboxChecked;
        public bool IsCheckboxChecked { get => _isCheckboxChecked; set => SetProperty(ref _isCheckboxChecked, value); }

        public ICommand OkCommand { get; }
        public ICommand YesCommand { get; }
        public ICommand NoCommand { get; }
        public ICommand CopyCommand { get; }

        public Action<bool>? RequestClose { get; set; }

        public CustomDialogViewModel(string message, string title, bool isYesNo, string? checkboxText = null)
        {
            Title = title;
            Message = message;

            if (!string.IsNullOrEmpty(checkboxText))
            {
                CheckboxText = checkboxText;
                CheckboxVisibility = Visibility.Visible;
            }

            if (title.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("Failed", StringComparison.OrdinalIgnoreCase))
            {
                CopyVisibility = Visibility.Visible;
            }

            if (isYesNo)
            {
                OkVisibility = Visibility.Collapsed;
                YesNoVisibility = Visibility.Visible;
            }
            else
            {
                OkVisibility = Visibility.Visible;
                YesNoVisibility = Visibility.Collapsed;
            }

            OkCommand = new RelayCommand(_ => RequestClose?.Invoke(true));
            YesCommand = new RelayCommand(_ => RequestClose?.Invoke(true));
            NoCommand = new RelayCommand(_ => RequestClose?.Invoke(false));
            CopyCommand = new RelayCommand(_ => ExecuteCopy());
        }

        private async void ExecuteCopy()
        {
            try
            {
                Clipboard.SetText(Message);
                CopyButtonText = "COPIED!";
                await Task.Delay(2000);
                CopyButtonText = "COPY";
            }
            catch (Exception ex)
            {
                LogManager.Log("CustomDialogViewModel.ExecuteCopy", ex);
            }
        }
    }
}