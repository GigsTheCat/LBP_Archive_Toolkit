using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using LbpArchiveToolkit.Models;

namespace LbpArchiveToolkit.Configuration
{
    public static class HeartedLevelsManager
    {
        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
            "LbpArchiveToolkit", "heartedlevels.json");

        public static List<LevelItem> HeartedLevels { get; set; } = [];

        public static void Load()
        {
            if (File.Exists(FilePath))
            {
                try
                {
                    string json = File.ReadAllText(FilePath);
                    var levels = JsonSerializer.Deserialize<List<LevelItem>>(json);
                    if (levels != null) HeartedLevels = levels;
                }
                catch { }
            }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                string json = JsonSerializer.Serialize(HeartedLevels, new JsonSerializerOptions { WriteIndented = true });
                string tempPath = FilePath + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, FilePath, overwrite: true);
            }
            catch { }
        }

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