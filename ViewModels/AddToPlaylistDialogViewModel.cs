using LbpArchiveToolkit.Configuration;
using LbpArchiveToolkit.Models;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;

namespace LbpArchiveToolkit.ViewModels
{
    public class AddToPlaylistDialogViewModel : ViewModelBase
    {
        public ObservableCollection<Playlist> Playlists { get; } = new();

        private Playlist? _selectedPlaylist;
        public Playlist? SelectedPlaylist { get => _selectedPlaylist; set => SetProperty(ref _selectedPlaylist, value); }

        private string _newPlaylistName = "";
        public string NewPlaylistName { get => _newPlaylistName; set => SetProperty(ref _newPlaylistName, value); }

        public ICommand AddCommand { get; }
        public ICommand CancelCommand { get; }

        public Action<bool>? RequestClose { get; set; }

        private readonly LevelItem _level;
        private readonly IViewService _viewService;

        public AddToPlaylistDialogViewModel(LevelItem level, IViewService viewService)
        {
            _level = level;
            _viewService = viewService;

            foreach (var p in PlaylistsManager.Playlists)
                Playlists.Add(p);

            if (Playlists.Any())
                SelectedPlaylist = Playlists.First();

            AddCommand = new RelayCommand(_ => ExecuteAdd());
            CancelCommand = new RelayCommand(_ => RequestClose?.Invoke(false));
        }

        private void ExecuteAdd()
        {
            if (!string.IsNullOrWhiteSpace(NewPlaylistName))
            {
                var newPlaylist = new Playlist { Name = NewPlaylistName.Trim() };
                newPlaylist.Levels.Add(_level);
                PlaylistsManager.AddPlaylist(newPlaylist);
                
                _viewService.Alert($"Added to '{newPlaylist.Name}'", "Success");
                RequestClose?.Invoke(true);
            }
            else if (SelectedPlaylist != null)
            {
                if (!SelectedPlaylist.Levels.Any(l => l.Id == _level.Id))
                {
                    SelectedPlaylist.Levels.Add(_level);
                    PlaylistsManager.Save();
                    
                    _viewService.Alert($"Added to '{SelectedPlaylist.Name}'", "Success");
                    RequestClose?.Invoke(true);
                }
                else
                {
                    _viewService.Alert($"This level is already in the playlist '{SelectedPlaylist.Name}'.", "Already Added");
                    // We don't close the dialog here so the user can easily select a different playlist
                }
            }
        }
    }
}