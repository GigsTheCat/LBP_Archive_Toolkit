using LbpArchiveToolkit.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LbpArchiveToolkit.Configuration
{
    public static class PlaylistsManager
    {
        private const string FileName = "playlists.json";

        public static List<Playlist> Playlists { get; set; } = new();

        public static void Load() => Playlists = JsonFileHelper.LoadList<Playlist>(FileName);
        public static void Save() => JsonFileHelper.SaveList(FileName, Playlists);

        public static void AddPlaylist(Playlist playlist)
        {
            Playlists.Add(playlist);
            Save();
        }

        public static void RemovePlaylist(string id)
        {
            Playlists.RemoveAll(p => p.Id == id);
            Save();
        }
    }
}