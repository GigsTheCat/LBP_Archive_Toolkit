using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using LbpArchiveToolkit.Models;

namespace LbpArchiveToolkit.Configuration
{
    public static class HeartedCreatorsManager
    {
        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
            "LbpArchiveToolkit", "heartedcreators.json");

        public static List<UserItem> HeartedCreators { get; set; } = [];

        public static void Load()
        {
            if (File.Exists(FilePath))
            {
                try
                {
                    string json = File.ReadAllText(FilePath);
                    var users = JsonSerializer.Deserialize<List<UserItem>>(json);
                    if (users != null) HeartedCreators = users;
                }
                catch { }
            }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                string json = JsonSerializer.Serialize(HeartedCreators, new JsonSerializerOptions { WriteIndented = true });
                string tempPath = FilePath + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, FilePath, overwrite: true);
            }
            catch { }
        }

        public static bool IsHearted(string npHandle)
        {
            return HeartedCreators.Exists(u => u.NpHandle.Equals(npHandle, StringComparison.OrdinalIgnoreCase));
        }

        public static void Add(UserItem user)
        {
            if (!IsHearted(user.NpHandle))
            {
                HeartedCreators.Add(user);
                Save();
            }
        }

        public static void Remove(string npHandle)
        {
            HeartedCreators.RemoveAll(u => u.NpHandle.Equals(npHandle, StringComparison.OrdinalIgnoreCase));
            Save();
        }
    }
}