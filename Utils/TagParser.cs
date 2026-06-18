using System.Collections.Generic;

namespace LbpArchiveToolkit.Utils
{
    public static class TagParser
    {
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
            for (int i = 0; i < TagNames.Length; i++)
            {
                // Calculate from the END of the array because it is Big-Endian
                int byteIndex = (blob.Length - 1) - (i / 8); 
                int bitIndex = i % 8;

                if (byteIndex >= 0 && byteIndex < blob.Length && (blob[byteIndex] & (1 << bitIndex)) != 0)
                {
                    tags.Add(TagNames[i]);
                }
            }
            return tags;
        }
    }
}