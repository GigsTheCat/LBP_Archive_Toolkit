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
    /// </summary>
    public class DatabaseService
    {
        #region State & Caching

        private readonly string _dbPath;
        private readonly object _schemaLock = new();
        
        private bool _isSchemaResolved = false;
        
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

        /// <summary>
        /// Searches the database for levels matching the provided keyword and filters.
        /// </summary>
        public async Task<List<LevelItem>> SearchLevelsAsync(string keyword, bool exact, bool searchDesc, int gameFilter, string? genreFilter, string? limitFilter, HashSet<string> savedLevels)
        {
            if (!File.Exists(_dbPath)) throw new FileNotFoundException($"Could not find '{_dbPath}'");

            return await Task.Run(() =>
            {
                EnsureSchemaResolved();

                using var conn = new SqliteConnection($"Data Source={_dbPath}");
                conn.Open();

                var queryBuilder = new StringBuilder();
                var parameters = new List<SqliteParameter>();

                queryBuilder.Append($"SELECT id, npHandle, name, {_colGame}, {_colDate}, {_colDesc}, {_colPlay}, {_colHeart}, {_colGenre}, {_colHash}, {_colIcon}, {_colLabels} FROM slot WHERE ");

                BuildSearchCondition(queryBuilder, parameters, keyword, exact, searchDesc);
                BuildFilters(queryBuilder, parameters, gameFilter, genreFilter);

                if (_colHeart != "NULL") queryBuilder.Append($" ORDER BY {_colHeart} DESC");
                if (limitFilter != "All") queryBuilder.Append($" LIMIT {limitFilter}");

                var items = new List<LevelItem>();
                using var cmd = new SqliteCommand(queryBuilder.ToString(), conn);
                cmd.Parameters.AddRange(parameters.ToArray());
                
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    long id = reader.GetInt64(0);
                    items.Add(new LevelItem
                    {
                        Id = id,
                        Saved = savedLevels.Contains(id.ToString()) ? "✓" : "",
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
                        Labels = reader.IsDBNull(11) ? new List<string>() : LabelParser.ParseLabelNames((byte[])reader.GetValue(11))
                    });
                }

                return items;
            });
        }

        /// <summary>
        /// Retrieves a distinct list of all genres currently available in the database.
        /// </summary>
        public async Task<HashSet<string>> GetGenresAsync()
        {
            var genres = new HashSet<string>();
            if (!File.Exists(_dbPath)) return genres;

            return await Task.Run(() =>
            {
                EnsureSchemaResolved();
                if (_colGenre == "NULL") return genres;

                using var conn = new SqliteConnection($"Data Source={_dbPath}");
                conn.Open();

                string query = $"SELECT DISTINCT {_colGenre} FROM slot WHERE {_colGenre} IS NOT NULL AND {_colGenre} != ''";
                using var cmd = new SqliteCommand(query, conn);
                using var reader = cmd.ExecuteReader();
                
                while (reader.Read())
                {
                    string g = MapGenreToString(reader.GetValue(0));
                    if (g != "Unknown" && !string.IsNullOrWhiteSpace(g)) genres.Add(g);
                }

                return genres;
            });
        }

        #endregion

        #region SQL Query Builders

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

        private void BuildFilters(StringBuilder query, List<SqliteParameter> parameters, int gameFilter, string? genreFilter)
        {
            if (gameFilter > 0 && _colGame != "NULL")
            {
                query.Append($" AND {_colGame} = @game");
                parameters.Add(new SqliteParameter("@game", gameFilter - 1));
            }

            if (genreFilter != "All Genres" && !string.IsNullOrEmpty(genreFilter) && _colGenre != "NULL")
            {
                int genreId = MapGenreToInt(genreFilter);
                if (genreId != 0)
                {
                    query.Append($" AND ({_colGenre} = @genreInt OR {_colGenre} = @genreStr)");
                    parameters.Add(new SqliteParameter("@genreInt", genreId));
                    parameters.Add(new SqliteParameter("@genreStr", genreFilter));
                }
                else
                {
                    query.Append($" AND {_colGenre} = @genreStr");
                    parameters.Add(new SqliteParameter("@genreStr", genreFilter));
                }
            }
        }

        #endregion

        #region Schema Resolution & Utilities

        /// <summary>
        /// Reads the SQLite PRAGMA info to map standard variable names to the specific database's columns.
        /// Result is cached to avoid massive redundant disk I/O on repeated searches.
        /// </summary>
        private void EnsureSchemaResolved()
        {
            if (_isSchemaResolved) return;

            lock (_schemaLock)
            {
                if (_isSchemaResolved) return;

                using var conn = new SqliteConnection($"Data Source={_dbPath}");
                conn.Open();

                var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (var cmdInfo = new SqliteCommand("PRAGMA table_info(slot)", conn))
                using (var readerInfo = cmdInfo.ExecuteReader())
                {
                    while (readerInfo.Read()) columns.Add(readerInfo.GetString(1));
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