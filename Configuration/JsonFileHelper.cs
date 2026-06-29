using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using LbpArchiveToolkit.Models;

namespace LbpArchiveToolkit.Configuration
{
    [JsonSerializable(typeof(List<UserItem>))]
    [JsonSerializable(typeof(List<LevelItem>))]
    [JsonSerializable(typeof(List<string>))]
    internal partial class AppJsonContext : JsonSerializerContext { }

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
                    var options = new JsonSerializerOptions { TypeInfoResolver = AppJsonContext.Default };
                    var items = JsonSerializer.Deserialize<List<T>>(json, options);
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
                var options = new JsonSerializerOptions { WriteIndented = true, TypeInfoResolver = AppJsonContext.Default };
                string json = JsonSerializer.Serialize(items, options);
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