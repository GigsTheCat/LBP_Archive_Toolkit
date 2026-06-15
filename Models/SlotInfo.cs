using System.Collections.Generic;

namespace LbpArchiveToolkit.Models
{
    public class SlotInfo
    {
        public string NpHandle = "Unknown";
        public string Name = "Unnamed Level";
        public string Description = "";
        public string RootLevelHash = "";
        public string IconHash = "";
        public bool IsAdventurePlanet = false;
        public int LevelType = 0;
        public bool InitiallyLocked = false;
        public bool Shareable = true;
        public uint BackgroundGuid = 0;
        public bool IsSubLevel = false;
        public int MinPlayers = 1;
        public int MaxPlayers = 4;
        public List<uint> Labels = new List<uint>();
        public int GameVersion = 1;
    }
}