using System.Collections.Generic;

namespace LbpArchiveToolkit.Utils
{
    public static class LabelParser
    {
        public static IReadOnlyList<string> GetFriendlyNames() => FriendlyLabelNames;
        // Master list of all 85 labels used by the LBP Database
        private static readonly string[] LabelTags = new string[] {
            "LABEL_SinglePlayer", "LABEL_RPG", "LABEL_Multiplayer", "LABEL_SINGLE_PLAYER", "LABEL_Musical",
            "LABEL_Artistic", "LABEL_Funny", "LABEL_Scary", "LABEL_Easy", "LABEL_Challenging",
            "LABEL_Long", "LABEL_Quick", "LABEL_Time_Trial", "LABEL_Seasonal", "LABEL_16_Bit",
            "LABEL_8_Bit", "LABEL_Homage", "LABEL_Technology", "LABEL_Pinball", "LABEL_Movie",
            "LABEL_Sticker_Gallery", "LABEL_Costume_Gallery", "LABEL_Music_Gallery", "LABEL_Prop_Hunt", "LABEL_Hide_And_Seek",
            "LABEL_Hangout", "LABEL_Driving", "LABEL_Defence", "LABEL_Party_Game", "LABEL_Mini_Game",
            "LABEL_Card_Game", "LABEL_Board_Game", "LABEL_Arcade_Game", "LABEL_Social", "LABEL_Sci_Fi",
            "LABEL_3rd_Person", "LABEL_1st_Person", "LABEL_CO_OP", "LABEL_TOP_DOWN", "LABEL_Retro",
            "LABEL_Tutorial", "LABEL_SurvivalChallenge", "LABEL_Strategy", "LABEL_Story", "LABEL_Sports",
            "LABEL_Shooter", "LABEL_Race", "LABEL_Platform", "LABEL_Puzzle", "LABEL_Gallery",
            "LABEL_Fighter", "LABEL_Competitive", "LABEL_Cinematic", "LABEL_FLOATY_FLUID_NAME", "LABEL_HOVERBOARD_NAME",
            "LABEL_SPRINGINATOR", "LABEL_SACKPOCKET", "LABEL_QUESTS", "LABEL_INTERACTIVE_STREAM", "LABEL_WALLJUMP",
            "LABEL_MEMORISER", "LABEL_HEROCAPE", "LABEL_ATTRACT_TWEAK", "LABEL_ATTRACT_GEL", "LABEL_Paint",
            "LABEL_Movinator", "LABEL_Brain_Crane", "LABEL_Water", "LABEL_Vehicles", "LABEL_Sackbots",
            "LABEL_PowerGlove", "LABEL_Paintinator", "LABEL_LowGravity", "LABEL_MagicBag", "LABEL_JumpPads",
            "LABEL_GrapplingHook", "LABEL_Glitch", "LABEL_Explosives", "LABEL_DirectControl", "LABEL_Collectables",
            "LABEL_CREATED_CHARACTERS", "LABEL_SACKBOY", "LABEL_SWOOP", "LABEL_TOGGLE", "LABEL_ODDSOCK"
        };

        // Strict list of the 46 labels natively supported by LittleBigPlanet 2
        private static readonly string[] Lbp2LabelTags = new string[] {
            "LABEL_SinglePlayer", "LABEL_Multiplayer", "LABEL_Quick", "LABEL_Long", "LABEL_Challenging",
            "LABEL_Easy", "LABEL_Scary", "LABEL_Funny", "LABEL_Artistic", "LABEL_Musical",
            "LABEL_Intricate", "LABEL_Cinematic", "LABEL_Competitive", "LABEL_Fighter", "LABEL_Gallery",
            "LABEL_Puzzle", "LABEL_Platform", "LABEL_Race", "LABEL_Shooter", "LABEL_Sports",
            "LABEL_Story", "LABEL_Strategy", "LABEL_SurvivalChallenge", "LABEL_Tutorial", "LABEL_Retro",
            "LABEL_Collectables", "LABEL_DirectControl", "LABEL_Explosives", "LABEL_Glitch", "LABEL_GrapplingHook",
            "LABEL_JumpPads", "LABEL_MagicBag", "LABEL_LowGravity", "LABEL_Paintinator", "LABEL_PowerGlove",
            "LABEL_Sackbots", "LABEL_Vehicles", "LABEL_Water", "LABEL_Brain_Crane", "LABEL_Movinator",
            "LABEL_Paint", "LABEL_ATTRACT_GEL", "LABEL_ATTRACT_TWEAK", "LABEL_HEROCAPE", "LABEL_MEMORISER",
            "LABEL_WALLJUMP"
        };

        private static readonly uint[] LabelHashes;
        private static readonly string[] FriendlyLabelNames;
        private static readonly HashSet<uint> Lbp2ValidHashes = new HashSet<uint>();

        static LabelParser()
        {
            LabelHashes = new uint[LabelTags.Length];
            FriendlyLabelNames = new string[LabelTags.Length];

            for (int i = 0; i < LabelTags.Length; i++)
            {
                LabelHashes[i] = CalculateLams(LabelTags[i]);
                // Pre-compute the friendly formatted names exactly once
                FriendlyLabelNames[i] = LabelTags[i].Replace("LABEL_", "").Replace("_", " ");
            }

            // Pre-calculate LBP2 specific hashes
            foreach (var tag in Lbp2LabelTags)
            {
                Lbp2ValidHashes.Add(CalculateLams(tag));
            }
        }

        public static bool IsLbp2Label(uint hash) => Lbp2ValidHashes.Contains(hash);

        public static List<uint> ParseLabelHashes(byte[] blob)
        {
            var labels = new List<uint>();
            for (int i = 0; i < LabelHashes.Length; i++)
            {
                // Calculate from the END of the array because it is Big-Endian
                int byteIndex = (blob.Length - 1) - (i / 8); 
                int bitIndex = i % 8;

                if (byteIndex >= 0 && byteIndex < blob.Length && (blob[byteIndex] & (1 << bitIndex)) != 0)
                {
                    labels.Add(LabelHashes[i]);
                }
            }
            return labels;
        }

        public static List<string> ParseLabelNames(byte[] blob)
        {
            var labels = new List<string>();
            for (int i = 0; i < LabelTags.Length; i++)
            {
                // Calculate from the END of the array because it is Big-Endian
                int byteIndex = (blob.Length - 1) - (i / 8); 
                int bitIndex = i % 8;

                if (byteIndex >= 0 && byteIndex < blob.Length && (blob[byteIndex] & (1 << bitIndex)) != 0)
                {
                    // Directly access the pre-computed string cache
                    labels.Add(FriendlyLabelNames[i]);
                }
            }
            return labels;
        }

        private static uint CalculateLams(string tag)
        {
            byte[] bytes = System.Text.Encoding.ASCII.GetBytes(tag);
            ulong v0 = 0;
            ulong v1 = 0xC8509800;

            for (int i = 31; i >= 0; i--)
            {
                ulong c = (i < bytes.Length) ? bytes[i] : 0x20UL;
                v0 = unchecked((v0 * 0x1b) + c);
            }

            if (bytes.Length > 32)
            {
                v1 = 0;
                for (int i = 63; i >= 32; i--)
                {
                    ulong c = (i < bytes.Length) ? bytes[i] : 0x20UL;
                    v1 = unchecked((v1 * 0x1b) + c);
                }
            }

            return unchecked((uint)((v0 + (v1 * 0xDEADBEEF)) & 0xFFFFFFFF));
        }
    }
}