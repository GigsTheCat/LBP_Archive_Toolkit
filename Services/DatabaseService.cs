using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Collections.Frozen;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using LbpArchiveToolkit.Models;
using LbpArchiveToolkit.Utils;

namespace LbpArchiveToolkit.Services
{
    public class DatabaseService
    {
        #region State & Caching

        private readonly string _dbPath;
        private readonly System.Threading.Lock _schemaLock = new();
        
        private SqliteConnection? _keepAliveMemConn;
        private readonly SemaphoreSlim _ramLoadLock = new(1, 1);

        private volatile bool _isSchemaResolved = false;
        private bool _hasFtsTable = false; 
        
        private string _colGame = "NULL";
        private string _colDate = "NULL";
        private string _colDesc = "NULL";
        private string _colPlay = "NULL";
        private string _colHeart = "NULL";
        private string _colGenre = "NULL";
        private string _colHash = "NULL";
        private string _colIcon = "NULL";
        private string _colLabels = "NULL";
        private string _colTags = "NULL";
        private string _colMmPick = "NULL";

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

        private string GetConnectionString()
        {
            if (LbpArchiveToolkit.Configuration.ConfigManager.LoadDbIntoRam && _keepAliveMemConn != null)
                return "Data Source=lbpramdb;Mode=Memory;Cache=Shared";
                
            return new SqliteConnectionStringBuilder { DataSource = _dbPath, Mode = SqliteOpenMode.ReadWrite }.ConnectionString;
        }

        private async Task EnsureRamDbLoadedAsync(IProgress<string>? progress)
        {
            if (!LbpArchiveToolkit.Configuration.ConfigManager.LoadDbIntoRam || _keepAliveMemConn != null) return;

            await _ramLoadLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_keepAliveMemConn == null)
                {
                    progress?.Report("Loading 4.9GB database into RAM... (This may take a moment)");
                    await Task.Run(() => {
                        var memConn = new SqliteConnection("Data Source=lbpramdb;Mode=Memory;Cache=Shared");
                        memConn.Open();
                        
                        var builder = new SqliteConnectionStringBuilder { DataSource = _dbPath, Mode = SqliteOpenMode.ReadOnly };
                        using var diskConn = new SqliteConnection(builder.ConnectionString);
                        diskConn.Open();
                        
                        // Performs an ultra-fast raw page copy from the SSD directly into RAM
                        diskConn.BackupDatabase(memConn);
                        
                        _keepAliveMemConn = memConn;
                    }).ConfigureAwait(false);
                }
            }
            finally
            {
                _ramLoadLock.Release();
            }
        }

        #endregion

        #region Public API

        public async IAsyncEnumerable<LevelItem> SearchLevelsAsync(string keyword, bool exact, bool searchDesc, int gameFilter, string? genreFilter, string? limitFilter, HashSet<long> savedLevels, HashSet<long> heartedLevels, AdvancedSearchCriteria advanced, IProgress<string>? progress = null, [EnumeratorCancellation] CancellationToken token = default)
        {
            if (!File.Exists(_dbPath)) throw new FileNotFoundException($"Could not find '{_dbPath}'");

            await Task.Run(() => EnsureSchemaResolved()).ConfigureAwait(false);

            // Always open a disk connection first to apply structural migrations permanently
            var diskConnBuilder = new SqliteConnectionStringBuilder { DataSource = _dbPath, Mode = SqliteOpenMode.ReadWrite };
            using var diskConn = new SqliteConnection(diskConnBuilder.ConnectionString);
            await diskConn.OpenAsync(token);
            
            // Execute seamless, one-time integer conversion only for FTS5-enabled databases
            if (_hasFtsTable)
            {
                await MigrateBitfieldsAsync(diskConn, progress, token).ConfigureAwait(false);
            }

            // Copy disk database into DDR5 RAM if requested
            if (LbpArchiveToolkit.Configuration.ConfigManager.LoadDbIntoRam)
            {
                await EnsureRamDbLoadedAsync(progress).ConfigureAwait(false);
            }

            // Route standard SQL queries to whichever database source was resolved
            using var conn = new SqliteConnection(GetConnectionString());
            await conn.OpenAsync(token);

            long reqL0 = 0, reqL1 = 0;
            var friendlyNames = LabelParser.GetFriendlyNames();
            foreach (var reqLabel in advanced.RequiredLabels)
            {
                for (int i = 0; i < friendlyNames.Count; i++)
                {
                    if (friendlyNames[i] == reqLabel)
                    {
                        if (i < 64) reqL0 |= (1L << i); else reqL1 |= (1L << (i - 64));
                        break;
                    }
                }
            }

            long reqT0 = 0, reqT1 = 0;
            foreach (var reqTag in advanced.RequiredTags)
            {
                int i = GetTagIndex(reqTag);
                if (i >= 0)
                {
                    if (i < 64) reqT0 |= (1L << i); else reqT1 |= (1L << (i - 64));
                }
            }

            var queryBuilder = new StringBuilder();
            var parameters = new List<SqliteParameter>();

            bool hasKeyword = !string.IsNullOrWhiteSpace(keyword);
            string pfx = (_hasFtsTable && hasKeyword) ? "s." : "";
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
                        .Append(SafeCol(_colMmPick)).Append(", ")
                        .Append(SafeCol(_colLabels));

            if (!_hasFtsTable)
            {
                queryBuilder.Append(", ").Append(SafeCol(_colTags));
            }

            queryBuilder.Append(" FROM slot ");

            if (_hasFtsTable && hasKeyword)
            {
                queryBuilder.Append("s INNER JOIN slot_fts f ON s.id = f.id WHERE ");
                BuildFtsSearchCondition(queryBuilder, parameters, keyword, exact, searchDesc);
            }
            else
            {
                queryBuilder.Append("WHERE 1=1 ");
                if (hasKeyword)
                {
                    queryBuilder.Append("AND ");
                    BuildSearchCondition(queryBuilder, parameters, keyword, exact, searchDesc);
                }
            }
            
            BuildFilters(queryBuilder, parameters, gameFilter, genreFilter, pfx, advanced, reqL0, reqL1, reqT0, reqT1, _hasFtsTable);

            if (_colHeart != "NULL") queryBuilder.Append($" ORDER BY {SafeCol(_colHeart)} DESC");
            
            if (limitFilter != "All" && int.TryParse(limitFilter, out int limit))
            {
                queryBuilder.Append($" LIMIT {limit}");
            }
            
            using var cmd = new SqliteCommand(queryBuilder.ToString(), conn);

            foreach (var param in parameters)
            {
                cmd.Parameters.Add(param);
            }

            var creatorCache = new Dictionary<string, string>();
            var dateCache = new Dictionary<string, string>();
            var nameCache = new Dictionary<string, string>();

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (token.IsCancellationRequested) yield break;

                long id = reader.GetInt64(0);

                if (!_hasFtsTable)
                {
                    long l0 = 0, l1 = 0;
                    if (reqL0 != 0 || reqL1 != 0)
                    {
                        byte[]? labelsBlob = reader.IsDBNull(12) ? null : reader.GetFieldValue<byte[]>(12);
                        if (labelsBlob != null)
                        {
                            for (int i = 0; i < 85; i++)
                            {
                                int byteIndex = (labelsBlob.Length - 1) - (i >> 3);
                                if (byteIndex >= 0 && byteIndex < labelsBlob.Length && (labelsBlob[byteIndex] & (1 << (i & 7))) != 0)
                                {
                                    if (i < 64) l0 |= (1L << i); else l1 |= (1L << (i - 64));
                                }
                            }
                        }
                        if ((l0 & reqL0) != reqL0 || (l1 & reqL1) != reqL1)
                        {
                            continue;
                        }
                    }

                    long t0 = 0, t1 = 0;
                    if ((reqT0 != 0 || reqT1 != 0) && _colTags != "NULL")
                    {
                        byte[]? tagsBlob = reader.IsDBNull(13) ? null : reader.GetFieldValue<byte[]>(13);
                        if (tagsBlob != null)
                        {
                            for (int i = 0; i < 76; i++)
                            {
                                int byteIndex = (tagsBlob.Length - 1) - (i >> 3);
                                if (byteIndex >= 0 && byteIndex < tagsBlob.Length && (tagsBlob[byteIndex] & (1 << (i & 7))) != 0)
                                {
                                    if (i < 64) t0 |= (1L << i); else t1 |= (1L << (i - 64));
                                }
                            }
                        }
                        if ((t0 & reqT0) != reqT0 || (t1 & reqT1) != reqT1)
                        {
                            continue;
                        }
                    }
                }

                bool isSaved = savedLevels.Contains(id);
                bool isHearted = heartedLevels.Contains(id);
                string savedStr = isSaved ? (isHearted ? "✓ ♥" : "✓") : (isHearted ? "♥" : "");

                string creator = "Unknown";
                if (!reader.IsDBNull(1))
                {
                    string? raw = reader.GetString(1);
                    if (raw != null)
                    {
                        if (creatorCache.TryGetValue(raw, out var cachedCreator) && cachedCreator != null)
                        {
                            creator = cachedCreator;
                        }
                        else
                        {
                            creatorCache[raw] = raw;
                            creator = raw;
                        }
                    }
                }

                string levelName = "Unknown";
                if (!reader.IsDBNull(2))
                {
                    string? raw = reader.GetString(2);
                    if (raw != null)
                    {
                        if (nameCache.TryGetValue(raw, out var cachedName) && cachedName != null)
                        {
                            levelName = cachedName;
                        }
                        else
                        {
                            nameCache[raw] = raw;
                            levelName = raw;
                        }
                    }
                }

                int gameInt = reader.IsDBNull(3) ? -1 : reader.GetInt32(3);
                string gameStr = gameInt switch {
                    0 => "LBP1",
                    1 => "LBP2",
                    2 => "LBP3",
                    _ => "Unk"
                };

                string date = "Unknown";
                if (!reader.IsDBNull(4))
                {
                    string? raw = FormatDate(reader.GetValue(4));
                    if (raw != null)
                    {
                        if (dateCache.TryGetValue(raw, out var cachedDate) && cachedDate != null)
                        {
                            date = cachedDate;
                        }
                        else
                        {
                            dateCache[raw] = raw;
                            date = raw;
                        }
                    }
                }

                var levelItem = new LevelItem
                {
                    Id = id,
                    Saved = savedStr,
                    Creator = creator,
                    LevelName = levelName,
                    Game = gameStr,
                    Date = date,
                    Description = reader.IsDBNull(5) ? "No description provided." : reader.GetString(5),
                    Plays = reader.IsDBNull(6) ? 0 : reader.GetInt32(6),
                    Hearts = reader.IsDBNull(7) ? 0 : reader.GetInt32(7),
                    Genre = reader.IsDBNull(8) ? "Unknown" : MapGenreToString(reader.GetValue(8)),
                    Hash = reader.IsDBNull(9) ? "" : GetHashString(reader.GetValue(9)),
                    IconHash = reader.IsDBNull(10) ? "" : GetHashString(reader.GetValue(10)),
                    IsMmPick = reader.IsDBNull(11) ? false : Convert.ToBoolean(reader.GetValue(11))
                };

                yield return levelItem;
            }
        }

        public async Task<List<UserItem>> SearchUsersAsync(string keyword, bool exact, string? limitFilter)
        {
            return await Task.Run(() =>
            {
                var items = new List<UserItem>();
                if (!File.Exists(_dbPath)) throw new FileNotFoundException($"Could not find '{_dbPath}'");

                EnsureSchemaResolved();

                // Wait synchronously if a RAM load was requested (since this method is inside Task.Run)
                if (LbpArchiveToolkit.Configuration.ConfigManager.LoadDbIntoRam)
                {
                    EnsureRamDbLoadedAsync(null).GetAwaiter().GetResult();
                }

                using var conn = new SqliteConnection(GetConnectionString());
                conn.Open();

                var queryBuilder = new StringBuilder();
                var parameters = new List<SqliteParameter>();

                queryBuilder.Append("SELECT npHandle, icon, heartCount, lbp1UsedSlots, lbp2UsedSlots, lbp3UsedSlots FROM user WHERE 1=1 ");

                bool hasKeyword = !string.IsNullOrWhiteSpace(keyword);
                if (hasKeyword)
                {
                    queryBuilder.Append("AND ");
                    if (exact)
                    {
                        queryBuilder.Append("npHandle LIKE @k");
                        parameters.Add(new SqliteParameter("@k", $"%{keyword}%"));
                    }
                    else
                    {
                        var words = keyword.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        var conds = new List<string>();
                        for (int i = 0; i < words.Length; i++)
                        {
                            conds.Add("npHandle LIKE @w" + i);
                            parameters.Add(new SqliteParameter("@w" + i, $"%{words[i]}%"));
                        }
                        queryBuilder.Append("(" + string.Join(" AND ", conds) + ")");
                    }
                }

                queryBuilder.Append(" ORDER BY heartCount DESC");

                if (limitFilter != "All" && int.TryParse(limitFilter, out int limit))
                {
                    queryBuilder.Append($" LIMIT {limit}");
                }

                using var cmd = new SqliteCommand(queryBuilder.ToString(), conn);
                foreach (var param in parameters) cmd.Parameters.Add(param);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    items.Add(new UserItem
                    {
                        NpHandle = reader.IsDBNull(0) ? "Unknown" : reader.GetString(0),
                        IconHash = reader.IsDBNull(1) ? "" : GetHashString(reader.GetValue(1)),
                        HeartCount = reader.IsDBNull(2) ? 0 : reader.GetInt64(2),
                        Lbp1UsedSlots = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                        Lbp2UsedSlots = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                        Lbp3UsedSlots = reader.IsDBNull(5) ? 0 : reader.GetInt32(5)
                    });
                }
                return items;
            });
        }

        private static readonly HashSet<string> _actualGenres = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Arcade", "Cinematic", "Driving", "Fighter", "FirstOrThirdPerson",
            "Gallery", "MiniGames", "Multiplayer", "Platform", "PlatformerRaces",
            "PlatformShooter", "Puzzle", "RPG", "Shooter", "Social", "Sports",
            "Story", "Strategy", "SurvivalChallenge", "TOP_DOWN", "Tutorial",
            "UniquePlatformer", "VehicleShooter"
        };

        public Task<HashSet<string>> GetGenresAsync()
        {
            // Instantly return the statically known genres to avoid the 1-2 second startup delay
            return Task.FromResult(new HashSet<string>(_actualGenres));
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

        private void BuildFtsSearchCondition(StringBuilder query, List<SqliteParameter> parameters, string keyword, bool exact, bool searchDesc)
        {
            string matchTerm = "";
            string Sanitize(string s) 
            {
                return System.Text.RegularExpressions.Regex.Replace(s, @"[\^\*\(\)\[\]\{\}\:\;\+\'\""]", "");
            }

            if (exact)
            {
                matchTerm = $"\"{Sanitize(keyword)}\"*";
            }
            else
            {
                var words = keyword.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var safeWords = new List<string>();
                
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

        private void BuildFilters(StringBuilder query, List<SqliteParameter> parameters, int gameFilter, string? genreFilter, string pfx, AdvancedSearchCriteria advanced, long reqL0, long reqL1, long reqT0, long reqT1, bool hasFts)
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

            if (advanced.IsTeamPick && _colMmPick != "NULL")
            {
                query.Append($" AND {pfx}{_colMmPick} = 1");
            }

            if (hasFts)
            {
                if (reqL0 != 0) {
                    query.Append($" AND ({pfx}labels_0 & @reqL0) = @reqL0");
                    parameters.Add(new SqliteParameter("@reqL0", reqL0));
                }
                if (reqL1 != 0) {
                    query.Append($" AND ({pfx}labels_1 & @reqL1) = @reqL1");
                    parameters.Add(new SqliteParameter("@reqL1", reqL1));
                }

                if (reqT0 != 0) {
                    query.Append($" AND ({pfx}tags_0 & @reqT0) = @reqT0");
                    parameters.Add(new SqliteParameter("@reqT0", reqT0));
                }
                if (reqT1 != 0) {
                    query.Append($" AND ({pfx}tags_1 & @reqT1) = @reqT1");
                    parameters.Add(new SqliteParameter("@reqT1", reqT1));
                }
            }
        }

        #endregion

        #region Schema Resolution & Utilities

        private async Task MigrateBitfieldsAsync(SqliteConnection conn, IProgress<string>? progress, CancellationToken token)
        {
            using var versionCmd = new SqliteCommand("PRAGMA user_version;", conn);
            long userVersion = (long)(await versionCmd.ExecuteScalarAsync(token) ?? 0L);
            if (userVersion >= 1) return; // Already fully migrated

            bool hasLabels0 = false;
            using var checkCmd = new SqliteCommand("PRAGMA table_info(slot);", conn);
            using var reader = await checkCmd.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token)) 
            {
                if (reader.GetString(1) == "labels_0")
                {
                    hasLabels0 = true;
                    break;
                }
            }

            if (!hasLabels0)
            {
                progress?.Report("One-time database optimization in progress... (Adding columns)");
                using (var trans = conn.BeginTransaction())
                {
                    using (var cmdL0 = new SqliteCommand("ALTER TABLE slot ADD COLUMN labels_0 INTEGER DEFAULT 0;", conn, trans)) await cmdL0.ExecuteNonQueryAsync(token);
                    using (var cmdL1 = new SqliteCommand("ALTER TABLE slot ADD COLUMN labels_1 INTEGER DEFAULT 0;", conn, trans)) await cmdL1.ExecuteNonQueryAsync(token);
                    using (var cmdT0 = new SqliteCommand("ALTER TABLE slot ADD COLUMN tags_0 INTEGER DEFAULT 0;", conn, trans)) await cmdT0.ExecuteNonQueryAsync(token);
                    using (var cmdT1 = new SqliteCommand("ALTER TABLE slot ADD COLUMN tags_1 INTEGER DEFAULT 0;", conn, trans)) await cmdT1.ExecuteNonQueryAsync(token);
                    trans.Commit();
                }
            }

            progress?.Report("One-time database optimization in progress... (Counting rows)");
            long totalRows = 0;
            using (var countCmd = new SqliteCommand("SELECT COUNT(*) FROM slot", conn))
            {
                totalRows = (long)(await countCmd.ExecuteScalarAsync(token) ?? 0L);
            }

            long processed = 0;
            long lastId = -1;
            int batchSize = 50000;

            while (true)
            {
                var updates = new List<(long id, long l0, long l1, long t0, long t1)>();
                
                using (var sel = new SqliteCommand($"SELECT id, authorLabels, tags FROM slot WHERE id > {lastId} ORDER BY id ASC LIMIT {batchSize}", conn))
                using (var res = await sel.ExecuteReaderAsync(token))
                {
                    while (await res.ReadAsync(token))
                    {
                        long id = res.GetInt64(0);
                        lastId = id;
                        byte[]? labels = res.IsDBNull(1) ? null : res.GetFieldValue<byte[]>(1);
                        byte[]? tags = res.IsDBNull(2) ? null : res.GetFieldValue<byte[]>(2);

                        long l0 = 0, l1 = 0, t0 = 0, t1 = 0;
                        if (labels != null) {
                            for (int i = 0; i < 85; i++) {
                                int byteIndex = (labels.Length - 1) - (i >> 3);
                                if (byteIndex >= 0 && byteIndex < labels.Length && (labels[byteIndex] & (1 << (i & 7))) != 0) {
                                    if (i < 64) l0 |= (1L << i); else l1 |= (1L << (i - 64));
                                }
                            }
                        }
                        if (tags != null) {
                            for (int i = 0; i < 76; i++) {
                                int byteIndex = (tags.Length - 1) - (i >> 3);
                                if (byteIndex >= 0 && byteIndex < tags.Length && (tags[byteIndex] & (1 << (i & 7))) != 0) {
                                    if (i < 64) t0 |= (1L << i); else t1 |= (1L << (i - 64));
                                }
                            }
                        }
                        updates.Add((id, l0, l1, t0, t1));
                    }
                }

                if (updates.Count == 0) break;

                using (var trans = conn.BeginTransaction())
                {
                    using var upd = new SqliteCommand("UPDATE slot SET labels_0=@l0, labels_1=@l1, tags_0=@t0, tags_1=@t1 WHERE id=@id", conn, trans);
                    upd.Parameters.Add("@l0", SqliteType.Integer); upd.Parameters.Add("@l1", SqliteType.Integer);
                    upd.Parameters.Add("@t0", SqliteType.Integer); upd.Parameters.Add("@t1", SqliteType.Integer);
                    upd.Parameters.Add("@id", SqliteType.Integer);

                    foreach (var u in updates) {
                        upd.Parameters["@l0"].Value = u.l0; upd.Parameters["@l1"].Value = u.l1;
                        upd.Parameters["@t0"].Value = u.t0; upd.Parameters["@t1"].Value = u.t1;
                        upd.Parameters["@id"].Value = u.id;
                        await upd.ExecuteNonQueryAsync(token);
                    }
                    trans.Commit();
                }

                processed += updates.Count;
                double percent = totalRows > 0 ? (double)processed / totalRows * 100 : 100;
                progress?.Report($"One-time database optimization... {percent:F1}% ({processed:N0} / {totalRows:N0} levels migrated)");
            }

            progress?.Report("Building search indices... (This may take a moment)");
            using (var trans = conn.BeginTransaction())
            {
                using (var cmdIdxLabels = new SqliteCommand("CREATE INDEX IF NOT EXISTS idx_slot_labels ON slot(labels_0, labels_1);", conn, trans)) await cmdIdxLabels.ExecuteNonQueryAsync(token);
                using (var cmdIdxTags = new SqliteCommand("CREATE INDEX IF NOT EXISTS idx_slot_tags ON slot(tags_0, tags_1);", conn, trans)) await cmdIdxTags.ExecuteNonQueryAsync(token);
                using (var cmdIdxHeart = new SqliteCommand("CREATE INDEX IF NOT EXISTS idx_slot_heartCount ON slot(heartCount DESC);", conn, trans)) await cmdIdxHeart.ExecuteNonQueryAsync(token);
                using (var cmdIdxPlay = new SqliteCommand("CREATE INDEX IF NOT EXISTS idx_slot_playCount ON slot(playCount DESC);", conn, trans)) await cmdIdxPlay.ExecuteNonQueryAsync(token);
                trans.Commit();
            }

            using var setVersionCmd = new SqliteCommand("PRAGMA user_version = 1;", conn);
            await setVersionCmd.ExecuteNonQueryAsync(token);
        }

        private void EnsureSchemaResolved()
{
    if (_isSchemaResolved) return;

    lock (_schemaLock)
    {
        if (_isSchemaResolved) return;

        var connStringBuilder = new SqliteConnectionStringBuilder { DataSource = _dbPath, Mode = SqliteOpenMode.ReadWrite };
        using var conn = new SqliteConnection(connStringBuilder.ConnectionString);
        conn.Open();

                try
                {
                    // WAL: Write-Ahead Logging
                    // temp_store = MEMORY: Forces complex ORDER BY sorts into RAM instead of temp files
                    // cache_size = -64000: Gives SQLite ~64MB of dedicated RAM for caching query results
                    string pragmaCmd = "PRAGMA journal_mode = WAL; PRAGMA temp_store = MEMORY; PRAGMA cache_size = -64000;";
                    
                    if (LbpArchiveToolkit.Configuration.ConfigManager.UseMemoryMappedIO)
                    {
                        // Maps the entire DB directly into memory to bypass OS read-buffers
                        pragmaCmd += " PRAGMA mmap_size = 2147483648;";
                    }

                    using var cmdPragma = new SqliteCommand(pragmaCmd, conn);
                    cmdPragma.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    LbpArchiveToolkit.LogManager.Log("DatabaseService.EnsureSchemaResolved (WAL Mode)", ex);
                }

                var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (var cmdInfo = new SqliteCommand("PRAGMA table_info(slot)", conn))
                using (var readerInfo = cmdInfo.ExecuteReader())
                {
                    while (readerInfo.Read()) columns.Add(readerInfo.GetString(1));
                }

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
                _colTags = GetDbColumn(columns, "tags");
                _colMmPick = GetDbColumn(columns, "mmpick", "mmPick");

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
            catch (Exception ex)
            {
                LbpArchiveToolkit.LogManager.Log("DatabaseService.FormatDate", ex);
                return "Unknown";
            }
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

        private static int GetTagIndex(string tagName)
        {
            return Array.IndexOf((string[])TagParser.GetNames(), tagName); 
        }

        #endregion
    }
}
