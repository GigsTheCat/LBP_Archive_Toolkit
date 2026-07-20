using System.ComponentModel;

namespace LbpArchiveToolkit.Models
{
    public class LevelItem : INotifyPropertyChanged
    {
        public long Id { get; set; }
        public string? Saved
        {
            get => field;
            set
            {
                field = value;
                OnPropertyChanged(nameof(Saved));
            }
        }
        public string? Game { get; set; }
        public string? Date { get; set; }
        public string? Creator { get; set; }
        public string? LevelName { get; set; }
        public int Plays { get; set; }
        public int Clears { get; set; }
        public int Hearts { get; set; }
        public string? Genre { get; set; }
        public string? Hash { get; set; }
        public string? IconHash { get; set; }
        public string? Description { get; set; }
        public List<string>? Labels { get; set; }
        public List<string>? CommunityLabels { get; set; }
        public List<string>? Tags { get; set; }
        public bool IsMmPick { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}