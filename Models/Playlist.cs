using System;
using System.Collections.Generic;

namespace LbpArchiveToolkit.Models
{
    public class Playlist
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "New Playlist";
        public List<LevelItem> Levels { get; set; } = new();
    }
}