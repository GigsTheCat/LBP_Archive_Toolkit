using System.Collections.Generic;

namespace LbpArchiveToolkit.Models
{
    public class AdvancedSearchCriteria
    {
        public int MinHearts { get; set; } = 0;
        public int MinPlays { get; set; } = 0;
        
        // List of labels/tags the level MUST have to appear in the results
        public List<string> RequiredLabels { get; set; } = new();
        public List<string> RequiredTags { get; set; } = new();
    }
}