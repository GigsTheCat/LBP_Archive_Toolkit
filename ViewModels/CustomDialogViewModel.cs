using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace LbpArchiveToolkit.ViewModels
{
    public class CustomDialogViewModel : ViewModelBase
    {
        public string Title { get; set => SetProperty(ref field, value); } = "";
        public string Message { get; set => SetProperty(ref field, value); } = "";
        public Visibility YesNoVisibility { get; set => SetProperty(ref field, value); } = Visibility.Collapsed;
        public Visibility OkVisibility { get; set => SetProperty(ref field, value); } = Visibility.Visible;
        public Visibility CopyVisibility { get; set => SetProperty(ref field, value); } = Visibility.Collapsed;
        public string CopyButtonText { get; set => SetProperty(ref field, value); } = "COPY";
        public Visibility CheckboxVisibility { get; set => SetProperty(ref field, value); } = Visibility.Collapsed;
        public string CheckboxText { get; set => SetProperty(ref field, value); } = "";
        public bool IsCheckboxChecked { get; set => SetProperty(ref field, value); }
        public Visibility InputVisibility { get; set => SetProperty(ref field, value); } = Visibility.Collapsed;
        public string InputText { get; set => SetProperty(ref field, value); } = "";

        public ICommand OkCommand { get; }
        public ICommand YesCommand { get; }
        public ICommand NoCommand { get; }
        public ICommand CopyCommand { get; }

        public Action<bool>? RequestClose { get; set; }

        public CustomDialogViewModel(string message, string title, bool isYesNo, string? checkboxText = null, bool isInput = false, string defaultInput = "")
        {
            Title = title;
            Message = message;

            if (isInput)
            {
                InputVisibility = Visibility.Visible;
                InputText = defaultInput;
            }

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