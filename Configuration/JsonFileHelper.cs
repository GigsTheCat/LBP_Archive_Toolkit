using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace LbpArchiveToolkit.Configuration
{
    public static class JsonFileHelper
    {
        private static string GetFullPath(string fileName) =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LbpArchiveToolkit", fileName);

        public static List<T> LoadList<T>(string fileName)
        {
            string path = GetFullPath(fileName);
            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    var items = JsonSerializer.Deserialize<List<T>>(json);
                    if (items != null) return items;
                }
                catch (Exception ex)
                {
                    LogManager.Log("JsonFileHelper.LoadList", ex);
                }
            }
            return new List<T>();
        }

        public static void SaveList<T>(string fileName, List<T> items)
        {
            string path = GetFullPath(fileName);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                string json = JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true });
                string tempPath = path + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, path, overwrite: true);
            }
            catch (Exception ex)
            {
                LogManager.Log("JsonFileHelper.SaveList", ex);
            }
        }
    }
}