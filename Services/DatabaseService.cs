using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Collections.Frozen;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using LbpArchiveToolkit.Models;
using LbpArchiveToolkit.Utils;

namespace LbpArchiveToolkit.Services
{
    /// <summary>
    /// Handles asynchronous querying, searching, and schema mapping for the LBP SQLite databases.
    /// Supports FTS5 hardware acceleration with graceful fallback to standard LIKE queries.
    /// </summary>
    public class DatabaseService
    {
        #region State & Caching

        private readonly string _dbPath;
        private readonly object _schemaLock = new();
        
        private volatile bool _isSchemaResolved = false;
        
        private bool _hasFtsTable = false; // Flags if FTS5 hardware acceleration is available
        
        private string _colGame = "NULL";
        private string _colDate = "NULL";
        private string _colDesc = "NULL";
        private string _colPlay = "NULL";
        private string _colHeart = "NULL";
        private string _colGenre = "NULL";
        private string _colHash = "NULL";
        private string _colIcon = "NULL";
        private string _colLabels = "NULL";

        private static readonly FrozenDictionary<string, int> _genreToIntMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) {
            {"Platformer", 1}, {"Versus", 2}, {"Arcade", 3}, {"Cinematic", 4}, {"Fighter", 5},
            {"RPG", 6}, {"Shooter", 7}, {"Survival", 8}, {"Tutorial", 9}, {"Music", 10},
            {"Vehicle", 11}, {"Social", 12}, {"Sports", 13}, {"Puzzle", 14}
        }.ToFrozenDictionary();

        private static readonly FrozenDictionary<int, string> _intToGenreMap = new Dictionary<int, string> {
            {1, "Platformer"}, {2, "Versus"}, {3, "Arcade"}, {4, "Cinematic"}, {5, "Fighter"},
            {6, "RPG"}, {7, "Shooter"}, {8, "Survival"}, {9, "Tutorial"}, {10, "Music"},
            {11, "Vehicle"}, {12, "Social"}, {13, "Sports"}, {14, "Puzzle"}
        }.ToFrozenDictionary();

        public DatabaseService(string dbPath)
        {
            _dbPath = dbPath;
        }

        #endregion

        #region Public API

        public async Task<List<LevelItem>> SearchLevelsAsync(string keyword, bool exact, bool searchDesc, int gameFilter, string? genreFilter, string? limitFilter, HashSet<long> savedLevels, AdvancedSearchCriteria advanced)
{
    if (!File.Exists(_dbPath)) throw new FileNotFoundException($"Could not find '{_dbPath}'");

    await Task.Run(() => EnsureSchemaResolved()).ConfigureAwait(false);

    using var conn = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly;");
    await conn.OpenAsync().ConfigureAwait(false);

    // --- 1. ADD CUSTOM SQLITE FUNCTION ---
    conn.CreateFunction("HAS_ALL_LABELS", (byte[] blob, string requiredIndicesStr) =>
    {
        if (blob == null) return false;
        if (string.IsNullOrEmpty(requiredIndicesStr)) return true;

        var indices = requiredIndicesStr.Split(',');
        foreach (var indexStr in indices)
        {
            if (!int.TryParse(indexStr, out int i)) continue;
            
            int byteIndex = (blob.Length - 1) - (i / 8);
            int bitIndex = i % 8;

            // Big-Endian bitmask check (matches LabelParser.cs)
            if (byteIndex < 0 || byteIndex >= blob.Length || (blob[byteIndex] & (1 << bitIndex)) == 0)
            {
                return false;
            }
        }
        return true;
    });

    // --- 2. PRE-CALCULATE REQUIRED LABEL INDICES ---
    string reqIndicesStr = "";
    if (advanced.RequiredLabels.Count > 0)
    {
        var friendlyNames = LabelParser.GetFriendlyNames();
        var reqIndices = new List<int>();
        foreach (var reqLabel in advanced.RequiredLabels)
        {
            for (int i = 0; i < friendlyNames.Count; i++)
            {
                if (friendlyNames[i] == reqLabel)
                {
                    reqIndices.Add(i);
                    break;
                }
            }
        }
        reqIndicesStr = string.Join(",", reqIndices);
    }

    var queryBuilder = new StringBuilder();
    var parameters = new List<SqliteParameter>();

    string pfx = _hasFtsTable ? "s." : "";
    string SafeCol(string col) => col == "NULL" ? "NULL" : $"{pfx}{col}";

    queryBuilder.Append("SELECT ")
                .Append(pfx).Append("id, ")
                .Append(pfx).Append("npHandle, ")
                .Append(pfx).Append("name, ")
                .Append(SafeCol(_colGame)).Append(", ")
                .Append(SafeCol(_colDate)).Append(", ")
                .Append(SafeCol(_colDesc)).Append(", ")
                .Append(SafeCol(_colPlay)).Append(", ")
                .Append(SafeCol(_colHeart)).Append(", ")
                .Append(SafeCol(_colGenre)).Append(", ")
                .Append(SafeCol(_colHash)).Append(", ")
                .Append(SafeCol(_colIcon)).Append(", ")
                .Append(SafeCol(_colLabels))
                .Append(" FROM slot ");

    if (_hasFtsTable)
    {
        queryBuilder.Append("s INNER JOIN slot_fts f ON s.id = f.id WHERE ");
        BuildFtsSearchCondition(queryBuilder, parameters, keyword, exact, searchDesc);
    }
    else
    {
        queryBuilder.Append("WHERE ");
        BuildSearchCondition(queryBuilder, parameters, keyword, exact, searchDesc);
    }
    
    // Pass our calculated indices string to the filter builder
    BuildFilters(queryBuilder, parameters, gameFilter, genreFilter, pfx, advanced, reqIndicesStr);

    if (_colHeart != "NULL") queryBuilder.Append($" ORDER BY {SafeCol(_colHeart)} DESC");
    
    // --- 3. RESTORE NATIVE SQL LIMIT CLAUSE ---
    if (limitFilter != "All" && int.TryParse(limitFilter, out int limit))
    {
        queryBuilder.Append($" LIMIT {limit}");
    }
    
    var items = new List<LevelItem>();
    using var cmd = new SqliteCommand(queryBuilder.ToString(), conn);

    foreach (var param in parameters)
    {
        cmd.Parameters.Add(param);
    }

    using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
    while (await reader.ReadAsync().ConfigureAwait(false))
    {
        long id = reader.GetInt64(0);
        var levelItem = new LevelItem
        {
            Id = id,
            Saved = savedLevels.Contains(id) ? "✓" : "",
            Creator = reader.IsDBNull(1) ? "Unknown" : reader.GetString(1),
            LevelName = reader.IsDBNull(2) ? "Unknown" : reader.GetString(2),
            Game = reader.IsDBNull(3) ? "Unk" : $"LBP{reader.GetInt32(3) + 1}",
            Date = reader.IsDBNull(4) ? "Unknown" : FormatDate(reader.GetValue(4)),
            Description = reader.IsDBNull(5) ? "No description provided." : reader.GetString(5),
            Plays = reader.IsDBNull(6) ? 0 : reader.GetInt32(6),
            Hearts = reader.IsDBNull(7) ? 0 : reader.GetInt32(7),
            Genre = reader.IsDBNull(8) ? "Unknown" : MapGenreToString(reader.GetValue(8)),
            Hash = reader.IsDBNull(9) ? "" : GetHashString(reader.GetValue(9)),
            IconHash = reader.IsDBNull(10) ? "" : GetHashString(reader.GetValue(10)),
            Labels = reader.IsDBNull(11) ? new List<string>() : LabelParser.ParseLabelNames(reader.GetFieldValue<byte[]>(11))
        };

        // We no longer need the C# list filtering here, just add the item!
        items.Add(levelItem);
    }
    return items;
}

        public async Task<HashSet<string>> GetGenresAsync()
        {
            var genres = new HashSet<string>();
            if (!File.Exists(_dbPath)) return genres;

            await Task.Run(() => EnsureSchemaResolved()).ConfigureAwait(false);
            if (_colGenre == "NULL") return genres;

            using var conn = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly;");
            await conn.OpenAsync().ConfigureAwait(false);

            string query = $"SELECT DISTINCT {_colGenre} FROM slot WHERE {_colGenre} IS NOT NULL AND {_colGenre} != ''";
            using var cmd = new SqliteCommand(query, conn);
            using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
            
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                string g = MapGenreToString(reader.GetValue(0));
                if (g != "Unknown" && !string.IsNullOrWhiteSpace(g)) genres.Add(g);
            }

            return genres;
        }

        #endregion

        #region SQL Query Builders

        // The fallback condition for dry.db instances without FTS5 tables
        private void BuildSearchCondition(StringBuilder query, List<SqliteParameter> parameters, string keyword, bool exact, bool searchDesc)
        {
            bool hasDesc = searchDesc && _colDesc != "NULL";

            if (exact)
            {
                string cond = hasDesc ? $"(name LIKE @k OR npHandle LIKE @k OR {_colDesc} LIKE @k)" : "(name LIKE @k OR npHandle LIKE @k)";
                query.Append(cond);
                parameters.Add(new SqliteParameter("@k", $"%{keyword}%"));
            }
            else
            {
                var words = keyword.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var conds = new List<string>();
                
                for (int i = 0; i < words.Length; i++)
                {
                    conds.Add(hasDesc ? $"(name LIKE @w{i} OR npHandle LIKE @w{i} OR {_colDesc} LIKE @w{i})" : $"(name LIKE @w{i} OR npHandle LIKE @w{i})");
                    parameters.Add(new SqliteParameter($"@w{i}", $"%{words[i]}%"));
                }
                
                query.Append(string.Join(" AND ", conds));
            }
        }

        // The ultra-fast FTS5 Virtual Table matcher logic
        private void BuildFtsSearchCondition(StringBuilder query, List<SqliteParameter> parameters, string keyword, bool exact, bool searchDesc)
        {
            string matchTerm = "";
            string Sanitize(string s) => s.Replace("\"", "\"\"");

            if (exact)
            {
                // Adding the * outside the quotes enables phrase prefix matching
                matchTerm = $"\"{Sanitize(keyword)}\"*";
            }
            else
            {
                var words = keyword.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var safeWords = new List<string>();
                
                // Adding the * enables prefix matching for each individual word (e.g. "cat" finds "cats")
                foreach (var w in words) safeWords.Add($"\"{Sanitize(w)}\"*");
                
                matchTerm = string.Join(" AND ", safeWords);
            }

            if (!searchDesc)
            {
                matchTerm = $"{{name npHandle}} : ({matchTerm})";
            }

            query.Append("slot_fts MATCH @match");
            parameters.Add(new SqliteParameter("@match", matchTerm));
        }

        private void BuildFilters(StringBuilder query, List<SqliteParameter> parameters, int gameFilter, string? genreFilter, string pfx, AdvancedSearchCriteria advanced, string reqIndicesStr)
{
    if (gameFilter > 0 && _colGame != "NULL")
    {
        query.Append($" AND {pfx}{_colGame} = @game");
        parameters.Add(new SqliteParameter("@game", gameFilter - 1));
    }

    if (genreFilter != "All Genres" && !string.IsNullOrEmpty(genreFilter) && _colGenre != "NULL")
    {
        int genreId = MapGenreToInt(genreFilter);
        if (genreId != 0)
        {
            query.Append($" AND ({pfx}{_colGenre} = @genreInt OR {pfx}{_colGenre} = @genreStr)");
            parameters.Add(new SqliteParameter("@genreInt", genreId));
            parameters.Add(new SqliteParameter("@genreStr", genreFilter));
        }
        else
        {
            query.Append($" AND {pfx}{_colGenre} = @genreStr");
            parameters.Add(new SqliteParameter("@genreStr", genreFilter));
        }
    }

    if (advanced.MinPlays > 0 && _colPlay != "NULL")
    {
        query.Append($" AND {pfx}{_colPlay} >= @minPlays");
        parameters.Add(new SqliteParameter("@minPlays", advanced.MinPlays));
    }

    if (advanced.MinHearts > 0 && _colHeart != "NULL")
    {
        query.Append($" AND {pfx}{_colHeart} >= @minHearts");
        parameters.Add(new SqliteParameter("@minHearts", advanced.MinHearts));
    }

    // --- 4. APPLY CUSTOM SQL FUNCTION IN WHERE CLAUSE ---
    if (!string.IsNullOrEmpty(reqIndicesStr) && _colLabels != "NULL")
    {
        query.Append($" AND HAS_ALL_LABELS({pfx}{_colLabels}, @reqIndices)");
        parameters.Add(new SqliteParameter("@reqIndices", reqIndicesStr));
    }
}

        #endregion

        #region Schema Resolution & Utilities

        private void EnsureSchemaResolved()
        {
            if (_isSchemaResolved) return;

            lock (_schemaLock)
            {
                if (_isSchemaResolved) return;

                // Open with Mode=ReadOnly to support strictly locked files
                using var conn = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly;");
                conn.Open();

                try
                {
                    using var cmdPragma = new SqliteCommand("PRAGMA journal_mode = WAL;", conn);
                    cmdPragma.ExecuteNonQuery();
                }
                catch
                {
                    // Fall back gracefully if database or directory is read-only
                }

                // Dynamically resolve existing columns
                var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (var cmdInfo = new SqliteCommand("PRAGMA table_info(slot)", conn))
                using (var readerInfo = cmdInfo.ExecuteReader())
                {
                    while (readerInfo.Read()) columns.Add(readerInfo.GetString(1));
                }

                // Check if the FTS5 virtual table was created by the user
                using (var cmdFts = new SqliteCommand("SELECT count(*) FROM sqlite_master WHERE type='table' AND name='slot_fts'", conn))
                {
                    _hasFtsTable = Convert.ToInt32(cmdFts.ExecuteScalar()) > 0;
                }

                _colGame = GetDbColumn(columns, "gameVersion", "game");
                _colDate = GetDbColumn(columns, "timestamp", "publishDate", "firstPublished", "timeCreated");
                _colDesc = GetDbColumn(columns, "description", "desc");
                _colPlay = GetDbColumn(columns, "playCount", "plays", "play_count");
                _colHeart = GetDbColumn(columns, "heartCount", "hearts", "heart_count");
                _colGenre = GetDbColumn(columns, "genre", "levelGenre", "level_genre");
                _colHash = GetDbColumn(columns, "rootLevel", "root_level", "rootLevelHash", "hash");
                _colIcon = GetDbColumn(columns, "icon", "iconHash");
                _colLabels = GetDbColumn(columns, "authorLabels");

                _isSchemaResolved = true;
            }
        }

        private static string GetDbColumn(HashSet<string> columns, params ReadOnlySpan<string> candidates)
        {
            foreach (var c in candidates)
            {
                if (columns.Contains(c)) return c;
            }
            return "NULL";
        }

        private static string FormatDate(object val)
        {
            if (val == null || val is DBNull) return "Unknown";
            try
            {
                if (val is string strVal) return strVal.Length <= 10 ? strVal : strVal.Substring(0, 10);
                long v = Convert.ToInt64(val);
                if (v > 9999999999) v /= 1000;
                return DateTimeOffset.FromUnixTimeSeconds(v).ToString("yyyy-MM-dd");
            }
            catch { return "Unknown"; }
        }

        private static string GetHashString(object val)
        {
            if (val is byte[] bytes) return Convert.ToHexStringLower(bytes);
            return val?.ToString() ?? string.Empty;
        }

        private static int MapGenreToInt(string genreName)
        {
            return _genreToIntMap.TryGetValue(genreName, out int id) ? id : 0;
        }

        private static string MapGenreToString(object val)
        {
            if (val == null || val is DBNull) return "Unknown";
            int id = -1;

            if (val is long l) id = (int)l;
            else if (val is string s)
            {
                if (int.TryParse(s, out int i)) id = i;
                else return s;
            }

            return _intToGenreMap.TryGetValue(id, out string? name) ? name : "Unknown";
        }

        #endregion
    }
}