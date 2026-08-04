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
        public int Yays { get; set; }
        public string? Genre { get; set; }
        public string? Hash { get; set; }
        public string? IconHash { get; set; }
        public string? Description { get; set; }
        public byte[]? LabelsBlob { get; set; }
        public byte[]? CommunityLabelsBlob { get; set; }
        public byte[]? TagsBlob { get; set; }
        public bool IsMmPick { get; set; }
        public bool IsLocked { get; set; }
        public bool IsSubLevel { get; set; }
        public bool IsShareable { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}