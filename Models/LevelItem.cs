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
        public string? Hash
        {
            get => field;
            set
            {
                field = value;
                OnPropertyChanged(nameof(Hash));
            }
        }
        public string? IconHash
        {
            get => field;
            set
            {
                field = value;
                OnPropertyChanged(nameof(IconHash));
            }
        }
        public string? Description
        {
            get => field;
            set
            {
                field = value;
                OnPropertyChanged(nameof(Description));
            }
        }
        public byte[]? LabelsBlob { get; set; }
        public byte[]? CommunityLabelsBlob { get; set; }
        public byte[]? TagsBlob { get; set; }
        private byte _flags;
        public bool IsMmPick { get => (_flags & 1) != 0; set { if (value) _flags |= 1; else _flags &= 0xFE; } }
        public bool IsLocked { get => (_flags & 2) != 0; set { if (value) _flags |= 2; else _flags &= 0xFD; } }
        public bool IsSubLevel { get => (_flags & 4) != 0; set { if (value) _flags |= 4; else _flags &= 0xFB; } }
        public bool IsShareable { get => (_flags & 8) != 0; set { if (value) _flags |= 8; else _flags &= 0xF7; } }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}