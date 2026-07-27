namespace LbpArchiveToolkit.Models
{
    public class AdvancedSearchCriteria
    {
        public int MinHearts { get; set; } = 0;
        public int MinPlays { get; set; } = 0;
        public int MinHeartPercentage { get; set; } = 0;
        public int MinYayPercentage { get; set; } = 0;
        public int MinClearPercentage { get; set; } = 0;
        public int MaxClearPercentage { get; set; } = 100;
        public bool IsTeamPick { get; set; } = false;
        public bool RequireLocked { get; set; } = false;
        public bool RequireSubLevel { get; set; } = false;
        public bool RequireShareable { get; set; } = false;

        // List of labels/tags the level MUST have to appear in the results
        public List<string> RequiredLabels { get; set; } = [];
        public List<string> RequiredTags { get; set; } = [];
        // 0 = Any (Author or Community), 1 = Author Only, 2 = Community Only
        public int LabelMatchMode { get; set; } = 0; 

        // Exclusions
        public List<string> ExcludedLabels { get; set; } = [];
        public List<string> ExcludedTags { get; set; } = [];
        public string ExcludedCreators { get; set; } = "";
        public string ExcludedContributors { get; set; } = "";
        public string ExcludedObjectContributors { get; set; } = "";
        public DateTime? PublishedBefore { get; set; }
        public DateTime? PublishedAfter { get; set; }
        public bool ExcludeTeamPick { get; set; } = false;
        public bool ExcludeLocked { get; set; } = false;
        public bool ExcludeSubLevels { get; set; } = false;
        public bool ExcludeShareable { get; set; } = false;
        public int MaxHearts { get; set; } = 0;
        public int MaxPlays { get; set; } = 0;
    }
}