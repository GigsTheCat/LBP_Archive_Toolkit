using LbpArchiveToolkit.Models;

namespace LbpArchiveToolkit.Configuration
{
    public static class HeartedCreatorsManager
    {
        private const string FileName = "heartedcreators.json";

        public static List<UserItem> HeartedCreators { get; set; } = [];

        public static void Load() => HeartedCreators = JsonFileHelper.LoadList<UserItem>(FileName);
        public static void Save() => JsonFileHelper.SaveList(FileName, HeartedCreators);

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