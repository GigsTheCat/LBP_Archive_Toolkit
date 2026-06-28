using System.IO;

namespace LbpArchiveToolkit.Configuration
{
    public static class SavedLevelsManager
    {
        private const string FileName = "savedlevels.json";

        public static List<string> SavedLevels { get; set; } = [];

        public static void Load(List<string> legacyLevels)
        {
            string path = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "LbpArchiveToolkit", FileName);

            if (File.Exists(path))
            {
                SavedLevels = JsonFileHelper.LoadList<string>(FileName);
            }
            else if (legacyLevels != null && legacyLevels.Count > 0)
            {
                // Migrate from legacy ConfigManager
                SavedLevels = new List<string>(legacyLevels);
                Save();
            }
        }

        public static void Save() => JsonFileHelper.SaveList(FileName, SavedLevels);

        public static bool Contains(string id)
        {
            return SavedLevels.Contains(id);
        }

        public static void Add(string id)
        {
            if (!Contains(id))
            {
                SavedLevels.Add(id);
                Save();
            }
        }

        public static void Clear()
        {
            SavedLevels.Clear();
            Save();
        }
    }
}