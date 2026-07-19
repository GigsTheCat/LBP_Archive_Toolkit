using System.Collections.Generic;

namespace LbpArchiveToolkit.Models
{
    public class SearchState
    {
        public string SearchText { get; set; } = "";
        public int SearchTypeIndex { get; set; } = 0;
        public int GameIndex { get; set; }
        public AdvancedSearchCriteria AdvancedCriteria { get; set; } = new();
        public string Genre { get; set; } = "All Genres";
        public int LimitIndex { get; set; }
        public bool Exact { get; set; }
        public bool SearchDesc { get; set; }
        public LevelItem? SelectedItem { get; set; }
        public UserItem? SelectedUser { get; set; }
        public bool IsSurpriseMe { get; set; }
    }
}