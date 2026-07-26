using System;
using System.Windows;
using System.Windows.Input;
using LbpArchiveToolkit.ViewModels;

namespace LbpArchiveToolkit
{
    public partial class EditInfoDialog : Window
    {
        private readonly EditInfoDialogViewModel _viewModel;

        // Passed through from ViewModel so the instantiating class 
        // doesn't have to be refactored too!
        public string LevelName => _viewModel.LevelName;
        public string Description => _viewModel.Description;
        public string? NewIconPath => _viewModel.NewIconPath;
        public bool IsLocked => _viewModel.IsLocked;
        public bool IsSubLevel => _viewModel.IsSubLevel;
        public bool IsShareable => _viewModel.IsShareable;

        public EditInfoDialog(string currentName, string currentDesc, string? currentIconPath, bool isLocked, bool isSubLevel, bool isShareable)
        {
            InitializeComponent();
            
            _viewModel = new EditInfoDialogViewModel(this, currentName, currentDesc, currentIconPath, isLocked, isSubLevel, isShareable);

            // Reacts to commands setting the dialog's close request
            _viewModel.RequestClose += (result) =>
            {
                DialogResult = result;
                Close();
            };

            DataContext = _viewModel;
        }

        protected override void OnClosed(EventArgs e)
        {
            if (DialogResult != true)
            {
                _viewModel.CleanupOnCancel();
            }
            base.OnClosed(e);
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }
    }
}