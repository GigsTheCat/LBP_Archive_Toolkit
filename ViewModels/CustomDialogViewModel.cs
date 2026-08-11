using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace LbpArchiveToolkit.ViewModels
{
    public class CustomDialogViewModel : ViewModelBase
    {
        public string Title { get; set => SetProperty(ref field, value); } = "";
        public string Message { get; set => SetProperty(ref field, value); } = "";
        public bool IsYesNoVisible { get; set => SetProperty(ref field, value); }
        public bool IsOkVisible { get; set => SetProperty(ref field, value); } = true;
        public bool IsCopyVisible { get; set => SetProperty(ref field, value); }
        public string CopyButtonText { get; set => SetProperty(ref field, value); } = "COPY";
        public bool IsCheckboxVisible { get; set => SetProperty(ref field, value); }
        public string CheckboxText { get; set => SetProperty(ref field, value); } = "";
        public bool IsCheckboxChecked { get; set => SetProperty(ref field, value); }
        public bool IsInputVisible { get; set => SetProperty(ref field, value); }
        public string InputText { get; set => SetProperty(ref field, value); } = "";

        public ICommand OkCommand { get; }
        public ICommand YesCommand { get; }
        public ICommand NoCommand { get; }
        public ICommand CopyCommand { get; }

        public Action<bool>? RequestClose { get; set; }

        public CustomDialogViewModel(IViewService? viewService, string message, string title, bool isYesNo, string? checkboxText = null, bool isInput = false, string defaultInput = "") : base(viewService)
        {
            Title = title;
            Message = message;

            if (isInput)
            {
                IsInputVisible = true;
                InputText = defaultInput;
            }

            if (!string.IsNullOrEmpty(checkboxText))
            {
                CheckboxText = checkboxText;
                IsCheckboxVisible = true;
            }

            if (title.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("Failed", StringComparison.OrdinalIgnoreCase))
            {
                IsCopyVisible = true;
            }

            if (isYesNo)
            {
                IsOkVisible = false;
                IsYesNoVisible = true;
            }
            else
            {
                IsOkVisible = true;
                IsYesNoVisible = false;
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
                BaseViewService?.SetClipboardText(Message);
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