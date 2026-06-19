namespace LbpArchiveToolkit.Models
{
    public class UserItem
    {
        public string NpHandle { get; set; } = string.Empty;
        public string IconHash { get; set; } = string.Empty;
        public long HeartCount { get; set; }
        public int Lbp1UsedSlots { get; set; }
        public int Lbp2UsedSlots { get; set; }
        public int Lbp3UsedSlots { get; set; }
        public int TotalLevels => Lbp1UsedSlots + Lbp2UsedSlots + Lbp3UsedSlots;
    }
}