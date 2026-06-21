using System.Collections.Generic;

namespace LbpArchiveToolkit.Utils
{
    public static class TagParser
    {
        public static IReadOnlyList<string> GetNames() => TagNames;
        // Master list of all 76 player tags used by the LBP Database
        private static readonly string[] TagNames = new string[] {
            "Brilliant", "Beautiful", "Funky", "Points-Fest", "Weird", "Tricky", "Short",
            "Vehicles", "Easy", "Cute", "Quick", "Fun", "Relaxing", "Great", "Speedy", "Race",
            "Multi-Path", "Machines", "Complex", "Pretty", "Rubbish", "Toys", "Repetitive",
            "Machinery", "Satisfying", "Braaains", "Fast", "Simple", "Long", "Slow", "Mad", "Hectic",
            "Creepy", "Perilous", "Empty", "Ingenious", "Lousy", "Frustrating", "Timing", "Boss",
            "Springy", "Funny", "Musical", "Good", "Hilarious", "Electric", "Puzzler", "Platformer",
            "Difficult", "Mechanical", "Horizontal", "Splendid", "Fiery", "Swingy", "Single-Path",
            "Annoying", "Co-op", "Boring", "Moody", "Bubbly", "Nerve-wracking", "Hoists", "Ugly",
            "Daft", "Ramps", "Secrets", "Floaty", "Artistic", "Competitive", "Gas", "Varied",
            "Stickers", "Spikes", "Collectables", "Vertical", "Balancing"
        };

        public static List<string> ParseTagNames(byte[] blob)
        {
            var tags = new List<string>();
            int len = blob.Length;
            if (len == 0) return tags;
            
            for (int i = 0; i < TagNames.Length; i++)
            {
                int byteIndex = (len - 1) - (i >> 3); 
                if (byteIndex >= 0 && (blob[byteIndex] & (1 << (i & 7))) != 0)
                {
                    tags.Add(TagNames[i]);
                }
            }
            return tags;
        }
    }
}