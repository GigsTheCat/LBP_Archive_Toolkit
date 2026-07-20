namespace LbpArchiveToolkit.Models
{
    public class AdvancedSearchCriteria
    {
        public int MinHearts { get; set; } = 0;
        public int MinPlays { get; set; } = 0;
        public bool IsTeamPick { get; set; } = false;

        // List of labels/tags the level MUST have to appear in the results
        public List<string> RequiredLabels { get; set; } = [];
        public List<string> RequiredTags { get; set; } = [];
        // 0 = Any (Author or Community), 1 = Author Only, 2 = Community Only
        public int LabelMatchMode { get; set; } = 0; 
    }
}