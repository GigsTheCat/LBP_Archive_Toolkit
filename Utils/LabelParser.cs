namespace LbpArchiveToolkit.Utils
{
    public static class LabelParser
    {
        public static IReadOnlyList<string> GetTags() => LabelTags;
        public static IReadOnlyList<string> GetFriendlyNames() => FriendlyLabelNames;
        
        public static bool IsLbp2Label(string friendlyName)
        {
            int idx = System.Array.IndexOf(FriendlyLabelNames, friendlyName);
            if (idx >= 0) return IsLbp2Label(LabelHashes[idx]);
            return false;
        }

        public static bool IsLbp2LabelByTag(string tag)
        {
            int idx = System.Array.IndexOf(LabelTags, tag);
            if (idx >= 0) return IsLbp2Label(LabelHashes[idx]);
            return false;
        }

        public static string GetLabelCategory(string tag)
        {
            switch (tag)
            {
                case "LABEL_SinglePlayer":
                case "LABEL_SINGLE_PLAYER":
                case "LABEL_Multiplayer":
                case "LABEL_CO_OP":
                case "LABEL_Competitive":
                case "LABEL_Tutorial":
                case "LABEL_Challenging":
                case "LABEL_Easy":
                case "LABEL_Long":
                case "LABEL_Quick":
                case "LABEL_Scary":
                case "LABEL_Funny":
                case "LABEL_Artistic":
                case "LABEL_Musical":
                case "LABEL_Story":
                case "LABEL_Cinematic":
                case "LABEL_Seasonal":
                case "LABEL_16_Bit":
                case "LABEL_8_Bit":
                case "LABEL_Homage":
                case "LABEL_SACKBOY":
                case "LABEL_SWOOP":
                case "LABEL_TOGGLE":
                case "LABEL_ODDSOCK":
                case "LABEL_CREATED_CHARACTERS":
                case "LABEL_Social":
                case "LABEL_Hangout":
                case "LABEL_Intricate":
                    return "Experience";

                case "LABEL_Platform":
                case "LABEL_Versus":
                case "LABEL_Fighter":
                case "LABEL_Race":
                case "LABEL_Shooter":
                case "LABEL_Sports":
                case "LABEL_Strategy":
                case "LABEL_SurvivalChallenge":
                case "LABEL_Puzzle":
                case "LABEL_RPG":
                case "LABEL_Movie":
                case "LABEL_Arcade_Game":
                case "LABEL_Board_Game":
                case "LABEL_Card_Game":
                case "LABEL_Party_Game":
                case "LABEL_Mini_Game":
                case "LABEL_Time_Trial":
                case "LABEL_TOP_DOWN":
                case "LABEL_1st_Person":
                case "LABEL_3rd_Person":
                case "LABEL_Gallery":
                case "LABEL_Costume_Gallery":
                case "LABEL_Music_Gallery":
                case "LABEL_Sticker_Gallery":
                case "LABEL_Prop_Hunt":
                case "LABEL_Hide_And_Seek":
                case "LABEL_Pinball":
                case "LABEL_Defence":
                case "LABEL_Driving":
                    return "Type";

                default:
                    return "Content";
            }
        }

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
                
                string friendly = LabelTags[i].Replace("LABEL_", "").Replace("_", " ");
                switch (LabelTags[i])
                {
                    // LBP2 & LBP3 Labels
                    case "LABEL_MagicBag": friendly = "Creatinator"; break;
                    case "LABEL_DirectControl": friendly = "Controlinator"; break;
                    case "LABEL_SinglePlayer": friendly = "Single Player"; break;
                    case "LABEL_Platform": friendly = "Platformer"; break;
                    case "LABEL_SurvivalChallenge": friendly = "Survival Challenge"; break;
                    case "LABEL_WALLJUMP": friendly = "Wall Jump"; break;
                    case "LABEL_MEMORISER": friendly = "Memorizer"; break;
                    case "LABEL_HEROCAPE": friendly = "Hero Cape"; break;
                    case "LABEL_ATTRACT_TWEAK": friendly = "Attract-o-Tweaker"; break;
                    case "LABEL_ATTRACT_GEL": friendly = "Attract-o-Gel"; break;
                    case "LABEL_PowerGlove": friendly = "Grabinators"; break;
                    case "LABEL_LowGravity": friendly = "Low Gravity"; break;
                    case "LABEL_JumpPads": friendly = "Bounce Pads"; break;
                    case "LABEL_GrapplingHook": friendly = "Grappling Hook"; break;

                    // LBP3 only labels
                    case "LABEL_SINGLE_PLAYER": friendly = "Single Player"; break;
                    case "LABEL_Mini_Game": friendly = "Mini-Game"; break;
                    case "LABEL_Sci_Fi": friendly = "Sci-Fi"; break;
                    case "LABEL_CO_OP": friendly = "Co-op"; break;
                    case "LABEL_TOP_DOWN": friendly = "Top Down"; break;
                    case "LABEL_FLOATY_FLUID_NAME": friendly = "Floaty Fluid"; break;
                    case "LABEL_HOVERBOARD_NAME": friendly = "Hoverboard"; break;
                    case "LABEL_SPRINGINATOR": friendly = "Springinator"; break;
                    case "LABEL_SACKPOCKET": friendly = "Sackpocket"; break;
                    case "LABEL_QUESTS": friendly = "Quests"; break;
                    case "LABEL_INTERACTIVE_STREAM": friendly = "Interactive Stream"; break;
                    case "LABEL_CREATED_CHARACTERS": friendly = "Created Characters"; break;
                    case "LABEL_SACKBOY": friendly = "Sackboy"; break;
                    case "LABEL_SWOOP": friendly = "Swoop"; break;
                    case "LABEL_TOGGLE": friendly = "Toggle"; break;
                    case "LABEL_ODDSOCK": friendly = "OddSock"; break;
                }
                
                FriendlyLabelNames[i] = friendly;
            }

            // Pre-calculate LBP2 specific hashes
            foreach (var tag in Lbp2LabelTags)
            {
                Lbp2ValidHashes.Add(CalculateLams(tag));
            }
        }

        public static string GetOriginalTag(string friendlyName)
        {
            int idx = System.Array.IndexOf(FriendlyLabelNames, friendlyName);
            if (idx >= 0) return LabelTags[idx];
            return "LABEL_" + friendlyName.Replace(" ", "_");
        }

        public static bool IsLbp2Label(uint hash) => Lbp2ValidHashes.Contains(hash);

        public static List<uint> ParseLabelHashes(byte[] blob)
        {
            var labels = new List<uint>();
            int len = blob.Length;
            if (len == 0) return labels;

            for (int i = 0; i < LabelHashes.Length; i++)
            {
                // Calculate from the END of the array because it is Big-Endian
                int byteIndex = (len - 1) - (i >> 3);
                if (byteIndex >= 0 && byteIndex < len && (blob[byteIndex] & (1 << (i & 7))) != 0)
                {
                    labels.Add(LabelHashes[i]);
                }
            }
            return labels;
        }

        public static List<string> ParseLabelNames(byte[] blob)
        {
            var labels = new List<string>();
            int len = blob.Length;
            if (len == 0) return labels;

            for (int i = 0; i < LabelTags.Length; i++)
            {
                int byteIndex = (len - 1) - (i >> 3);
                if (byteIndex >= 0 && (blob[byteIndex] & (1 << (i & 7))) != 0)
                {
                    labels.Add(FriendlyLabelNames[i]);
                }
            }
            return labels;
        }

        private static uint CalculateLams(string tag)
        {
            Span<byte> bytes = stackalloc byte[tag.Length];
            System.Text.Encoding.ASCII.GetBytes(tag, bytes);
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