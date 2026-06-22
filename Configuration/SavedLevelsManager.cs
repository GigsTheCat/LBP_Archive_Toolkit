using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace LbpArchiveToolkit.Configuration
{
    public static class SavedLevelsManager
    {
        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
            "LbpArchiveToolkit", "savedlevels.json");

        public static List<string> SavedLevels { get; set; } = new List<string>();

        public static void Load(List<string> legacyLevels)
        {
            if (File.Exists(FilePath))
            {
                try
                {
                    string json = File.ReadAllText(FilePath);
                    var levels = JsonSerializer.Deserialize<List<string>>(json);
                    if (levels != null) SavedLevels = levels;
                }
                catch { }
            }
            else if (legacyLevels != null && legacyLevels.Count > 0)
            {
                // Migrate from legacy ConfigManager
                SavedLevels = new List<string>(legacyLevels);
                Save();
            }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                string json = JsonSerializer.Serialize(SavedLevels, new JsonSerializerOptions { WriteIndented = true });
                string tempPath = FilePath + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, FilePath, overwrite: true);
            }
            catch { }
        }

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