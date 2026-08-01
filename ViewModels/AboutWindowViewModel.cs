using System;
using System.Reflection;
using System.Windows.Input;

namespace LbpArchiveToolkit.ViewModels
{
    public class AboutWindowViewModel : ViewModelBase
    {
        public string VersionText { get; set => SetProperty(ref field, value); } = "Version Unknown";

        public ICommand CloseCommand { get; }

        // Action delegate to let the View know when it needs to close itself
        public Action? RequestClose { get; set; }

        public AboutWindowViewModel()
        {
            CloseCommand = new RelayCommand(_ => RequestClose?.Invoke());
            LoadVersionInfo();
        }

        private void LoadVersionInfo()
        {
            var attr = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            if (attr != null)
            {
                string versionStr = attr.InformationalVersion;
                int plusIndex = versionStr.IndexOf('+');
                if (plusIndex > 0)
                {
                    versionStr = versionStr.Substring(0, plusIndex);
                }
                VersionText = $"Version {versionStr}";
            }
            else
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version;
                VersionText = $"Version {version?.ToString()}";
            }
        }
    }
}