using LbpArchiveToolkit.Models;

namespace LbpArchiveToolkit.Configuration
{
    public static class HeartedLevelsManager
    {
        private const string FileName = "heartedlevels.json";

        public static List<LevelItem> HeartedLevels { get; set; } = [];

        public static void Load() => HeartedLevels = JsonFileHelper.LoadList<LevelItem>(FileName);
        public static void Save() => JsonFileHelper.SaveList(FileName, HeartedLevels);

        public static bool IsHearted(long id)
        {
            return HeartedLevels.Exists(l => l.Id == id);
        }

        public static void Add(LevelItem level)
        {
            if (!IsHearted(level.Id))
            {
                HeartedLevels.Add(level);
                Save();
            }
        }

        public static void Remove(long id)
        {
            HeartedLevels.RemoveAll(l => l.Id == id);
            Save();
        }
    }
}