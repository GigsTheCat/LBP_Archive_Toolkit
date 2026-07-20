using LbpArchiveToolkit.Models;
using LbpArchiveToolkit.Utils;
using Microsoft.Data.Sqlite;
using System.Collections.Frozen;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

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
        private bool _hasTagsFtsTable = false;
        private bool _hasContribsTable = false;
        private bool _hasContribFtsTable = false;
        private bool _hasObjectContribsTable = false;
        private bool _hasObjectContribFtsTable = false;

        public bool HasContributorsTable
        {
            get
            {
                EnsureSchemaResolved();
                return _hasContribsTable;
            }
        }

        public bool HasObjectContributorsTable
        {
            get
            {
                EnsureSchemaResolved();
                return _hasObjectContribsTable;
            }
        }

        public bool HasCommunityLabels
        {
            get
            {
                EnsureSchemaResolved();
                return _colCommunityLabels != "NULL";
            }
        }

        public bool HasCompletionData
        {
            get
            {
                EnsureSchemaResolved();
                return _colCompletion != "NULL";
            }
        }

        private string _colGame = "NULL";
        private string _colDate = "NULL";
        private string _colDesc = "NULL";
        private string _colPlay = "NULL";
        private string _colCompletion = "NULL";
        private string _colHeart = "NULL";
        private string _colGenre = "NULL";
        private string _colHash = "NULL";
        private string _colIcon = "NULL";
        private string _colLabels = "NULL";
        private string _colCommunityLabels = "NULL";
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

        private void ApplyConnectionOptimizations(SqliteConnection conn)
        {
            try
            {
                string pragmaCmd = "PRAGMA journal_mode = WAL; PRAGMA temp_store = MEMORY; PRAGMA cache_size = -64000;";
                if (LbpArchiveToolkit.Configuration.ConfigManager.UseMemoryMappedIO)
                {
                    pragmaCmd += " PRAGMA mmap_size = 2147483648;";
                }

                using var cmdPragma = new SqliteCommand(pragmaCmd, conn);
                cmdPragma.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                LbpArchiveToolkit.LogManager.Log("DatabaseService.ApplyConnectionOptimizations", ex);
            }
        }

        private async Task EnsureRamDbLoadedAsync(IProgress<string>? progress)
        {
            if (!LbpArchiveToolkit.Configuration.ConfigManager.LoadDbIntoRam || _keepAliveMemConn != null) return;

            await _ramLoadLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_keepAliveMemConn == null)
                {
                    string sizeStr = "the";
                    if (File.Exists(_dbPath))
                    {
                        try
                        {
                            double gbSize = new FileInfo(_dbPath).Length / (1024.0 * 1024.0 * 1024.0);
                            sizeStr = $"{gbSize:F1} GB";
                        }
                        catch { }
                    }
                    progress?.Report($"Loading {sizeStr} database into RAM... (This may take a moment)");
                    
                    await Task.Run(() =>
                    {
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

        public async IAsyncEnumerable<LevelItem> SearchLevelsAsync(string keyword, bool exact, bool searchDesc, int gameFilter, string? genreFilter, string? limitFilter, HashSet<long> savedLevels, HashSet<long> heartedLevels, AdvancedSearchCriteria advanced, IProgress<string>? progress = null, bool searchContributions = false, bool searchObjects = false, bool randomSingle = false, [EnumeratorCancellation] CancellationToken token = default)
        {
            if (!File.Exists(_dbPath)) throw new FileNotFoundException($"Could not find '{_dbPath}'");

            await Task.Run(() => EnsureSchemaResolved()).ConfigureAwait(false);

            // Copy disk database into DDR5 RAM if requested
            if (LbpArchiveToolkit.Configuration.ConfigManager.LoadDbIntoRam)
            {
                await EnsureRamDbLoadedAsync(progress).ConfigureAwait(false);
            }

            // Route standard SQL queries to whichever database source was resolved
            using var conn = new SqliteConnection(GetConnectionString());
            await conn.OpenAsync(token);
            ApplyConnectionOptimizations(conn);

            long reqL0 = 0, reqL1 = 0;
            var labelTags = LabelParser.GetTags();
            foreach (var reqLabel in advanced.RequiredLabels)
            {
                for (int i = 0; i < labelTags.Count; i++)
                {
                    if (labelTags[i] == reqLabel)
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
            bool hasTagsFilter = advanced.RequiredLabels.Count > 0 || advanced.RequiredTags.Count > 0 || advanced.IsTeamPick;
            bool useFtsForTags = hasTagsFilter && _hasTagsFtsTable && !hasKeyword;
            bool searchContribsActive = searchContributions && hasKeyword;
            bool searchObjectsActive = searchObjects && hasKeyword;
            bool isFtsContext = (_hasFtsTable && (hasKeyword || useFtsForTags)) || searchContribsActive || searchObjectsActive;
            string pfx = isFtsContext ? "s." : "";
            string SafeCol(string col) => col == "NULL" ? "NULL" : $"{pfx}{col}";

            queryBuilder.Append("SELECT ")
                        .Append(pfx).Append("id, ")
                        .Append(pfx).Append("npHandle, ")
                        .Append(pfx).Append("name, ")
                        .Append(SafeCol(_colGame)).Append(", ")
                        .Append(SafeCol(_colDate)).Append(", ")
                        .Append(SafeCol(_colDesc)).Append(", ")
                        .Append(SafeCol(_colPlay)).Append(", ")
                        .Append(SafeCol(_colCompletion)).Append(", ")
                        .Append(SafeCol(_colHeart)).Append(", ")
                        .Append(SafeCol(_colGenre)).Append(", ")
                        .Append(SafeCol(_colHash)).Append(", ")
                        .Append(SafeCol(_colIcon)).Append(", ")
                        .Append(SafeCol(_colMmPick)).Append(", ")
                        .Append(SafeCol(_colLabels)).Append(", ")
                        .Append(SafeCol(_colTags)).Append(", ")
                        .Append(SafeCol(_colCommunityLabels));

            queryBuilder.Append(" FROM slot ");

            if (isFtsContext)
            {
                queryBuilder.Append("s ");

                if (hasKeyword && !searchContribsActive && !searchObjectsActive)
                    queryBuilder.Append("INNER JOIN slot_fts f ON s.id = f.id ");


                if (useFtsForTags)
                    queryBuilder.Append("INNER JOIN slot_tags_fts tf ON s.id = tf.rowid ");

                queryBuilder.Append("WHERE 1=1 ");

                if (hasKeyword)
                {
                    queryBuilder.Append("AND ");
                    if (searchContribsActive)
                    {
                        if (_hasContribFtsTable)
                            BuildContribFtsSearchCondition(queryBuilder, parameters, keyword, exact);
                        else if (_hasContribsTable)
                            BuildContribSearchCondition(queryBuilder, parameters, keyword, exact);
                        else
                            queryBuilder.Append("1=0 "); // Fallback if no tables exist
                    }
                    else if (searchObjectsActive)
                    {
                        if (_hasObjectContribFtsTable)
                            BuildObjectFtsSearchCondition(queryBuilder, parameters, keyword, exact);
                        else if (_hasObjectContribsTable)
                            BuildObjectSearchCondition(queryBuilder, parameters, keyword, exact);
                        else
                            queryBuilder.Append("1=0 "); // Fallback if no tables exist
                    }
                    else
                    {
                        BuildFtsSearchCondition(queryBuilder, parameters, keyword, exact, searchDesc);
                    }
                }

                if (useFtsForTags)
                {
                    List<string> tagTokens = [with(capacity: advanced.RequiredLabels.Count + advanced.RequiredTags.Count + 1)];
                    foreach (var l in advanced.RequiredLabels)
                    {
                        // 'l' is already the raw tag at this point (e.g. "LABEL_SINGLE_PLAYER")
                        string ftsTag = l.Replace("LABEL_", "").Replace("_", "");
                        if (advanced.LabelMatchMode == 1)
                            tagTokens.Add($"\"LBL_{ftsTag}\"");
                        else if (advanced.LabelMatchMode == 2)
                            tagTokens.Add($"\"COMM_LBL_{ftsTag}\"");
                        else
                            tagTokens.Add($"(\"LBL_{ftsTag}\" OR \"COMM_LBL_{ftsTag}\")");
                    }
                    foreach (var t in advanced.RequiredTags) tagTokens.Add($"\"TAG_{t.Replace(" ", "")}\"");
                    if (advanced.IsTeamPick) tagTokens.Add("\"MM_PICK\"");

                    queryBuilder.Append(" AND slot_tags_fts MATCH @tagMatch");
                    parameters.Add(new SqliteParameter("@tagMatch", string.Join(" AND ", tagTokens)));
                }
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

            BuildFilters(queryBuilder, parameters, gameFilter, genreFilter, pfx, advanced, reqL0, reqL1, reqT0, reqT1, useFtsForTags);

            bool isAllLimit = (limitFilter == "All" || string.IsNullOrEmpty(limitFilter));
            bool needsCSharpFiltering = !useFtsForTags && (reqL0 != 0 || reqL1 != 0 || reqT0 != 0 || reqT1 != 0);
            int parsedLimit = int.MaxValue;

            if (randomSingle)
            {
                queryBuilder.Append(" ORDER BY RANDOM()");
                if (!needsCSharpFiltering) queryBuilder.Append(" LIMIT 1");
            }
            else
            {
                if (_hasFtsTable && hasKeyword && !searchContribsActive && !searchObjectsActive)
                {
                    if (isAllLimit)
                    {
                        queryBuilder.Append(" ORDER BY f.rank");
                    }
                    else if (_colHeart != "NULL")
                    {
                        queryBuilder.Append($" ORDER BY {SafeCol(_colHeart)} DESC");
                    }
                    else
                    {
                        queryBuilder.Append(" ORDER BY f.rank");
                    }
                }
                else if (_colHeart != "NULL")
                {
                    queryBuilder.Append($" ORDER BY {SafeCol(_colHeart)} DESC");
                }

                if (!isAllLimit && int.TryParse(limitFilter, out parsedLimit))
                {
                    if (!needsCSharpFiltering)
                    {
                        queryBuilder.Append($" LIMIT {parsedLimit}");
                    }
                }
            }

            using var cmd = new SqliteCommand(queryBuilder.ToString(), conn);

            foreach (var param in parameters)
            {
                cmd.Parameters.Add(param);
            }

            var creatorCache = new Dictionary<string, string>();
            var dateCache = new Dictionary<string, string>();
            var nameCache = new Dictionary<string, string>();

            using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            
            byte[] sharedBlobBuffer = System.Buffers.ArrayPool<byte>.Shared.Rent(256);
            try
            {
                void ReadBitmask(int colIndex, out long mask0, out long mask1)
                {
                    mask0 = 0; mask1 = 0;
                    if (!reader.IsDBNull(colIndex))
                    {
                        long bytesRead = reader.GetBytes(colIndex, 0, sharedBlobBuffer, 0, 256);
                        int len = (int)bytesRead;
                        if (len >= 8)
                        {
                            mask0 = (long)System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(sharedBlobBuffer.AsSpan(len - 8, 8));
                            int remaining = len - 8;
                            if (remaining > 0)
                            {
                                Span<byte> temp = stackalloc byte[8];
                                sharedBlobBuffer.AsSpan(0, remaining).CopyTo(temp.Slice(8 - remaining));
                                mask1 = (long)System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(temp);
                            }
                        }
                        else if (len > 0)
                        {
                            Span<byte> temp = stackalloc byte[8];
                            sharedBlobBuffer.AsSpan(0, len).CopyTo(temp.Slice(8 - len));
                            mask0 = (long)System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(temp);
                        }
                    }
                }

                while (await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    long id = reader.GetInt64(0);

                    if (needsCSharpFiltering)
                    {
                        if (reqL0 != 0 || reqL1 != 0)
                        {
                            ReadBitmask(13, out long l0, out long l1);
                            
                            long cL0 = 0, cL1 = 0;
                            if (_colCommunityLabels != "NULL")
                            {
                                ReadBitmask(15, out cL0, out cL1);
                            }

                            long combinedL0 = 0, combinedL1 = 0;
                            if (advanced.LabelMatchMode == 1) { combinedL0 = l0; combinedL1 = l1; }
                            else if (advanced.LabelMatchMode == 2) { combinedL0 = cL0; combinedL1 = cL1; }
                            else { combinedL0 = l0 | cL0; combinedL1 = l1 | cL1; }

                            if ((combinedL0 & reqL0) != reqL0 || (combinedL1 & reqL1) != reqL1)
                            {
                                continue;
                            }
                        }

                        if ((reqT0 != 0 || reqT1 != 0) && _colTags != "NULL")
                        {
                            ReadBitmask(14, out long t0, out long t1);
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
                string gameStr = gameInt switch
                {
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
                    Clears = reader.IsDBNull(7) ? 0 : reader.GetInt32(7),
                    Hearts = reader.IsDBNull(8) ? 0 : reader.GetInt32(8),
                    Genre = reader.IsDBNull(9) ? "Unknown" : (reader.GetFieldType(9) == typeof(long) ? _intToGenreMap.GetValueOrDefault(reader.GetInt32(9), "Unknown") : MapGenreToString(reader.GetValue(9))),
                    Hash = reader.IsDBNull(10) ? "" : (reader.GetFieldType(10) == typeof(byte[]) ? Convert.ToHexStringLower(reader.GetFieldValue<byte[]>(10)) : reader.GetString(10)),
                    IconHash = reader.IsDBNull(11) ? "" : (reader.GetFieldType(11) == typeof(byte[]) ? Convert.ToHexStringLower(reader.GetFieldValue<byte[]>(11)) : reader.GetString(11)),
                    IsMmPick = reader.IsDBNull(12) ? false : reader.GetBoolean(12)
                };

                var levelLabels = new List<string>();
                if (!reader.IsDBNull(13) && reader.GetFieldType(13) == typeof(byte[]))
                {
                    levelLabels.AddRange(LabelParser.ParseLabelNames(reader.GetFieldValue<byte[]>(13)));
                }
                
                var levelTags = new List<string>();
                if (!reader.IsDBNull(14) && reader.GetFieldType(14) == typeof(byte[]))
                {
                    levelTags.AddRange(TagParser.ParseTagNames(reader.GetFieldValue<byte[]>(14)));
                }

                var commLabels = new List<string>();
                if (_colCommunityLabels != "NULL" && !reader.IsDBNull(15) && reader.GetFieldType(15) == typeof(byte[]))
                {
                    commLabels.AddRange(LabelParser.ParseLabelNames(reader.GetFieldValue<byte[]>(15)));
                }
                
                levelItem.Labels = levelLabels;
                levelItem.CommunityLabels = commLabels;
                levelItem.Tags = levelTags;

                 yield return levelItem;

                if (randomSingle)
                {
                    break; 
                }

                if (needsCSharpFiltering && !randomSingle)
                {
                    parsedLimit--;
                    if (parsedLimit <= 0) break;
                }
            }
            }
            finally
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(sharedBlobBuffer);
            }
        }

        public async Task<List<UserItem>> SearchUsersAsync(string keyword, bool exact, string? limitFilter, bool randomSingle = false, CancellationToken token = default)
        {
            return await Task.Run(async () =>
            {
                var items = new List<UserItem>();
                if (!File.Exists(_dbPath)) throw new FileNotFoundException($"Could not find '{_dbPath}'");

                EnsureSchemaResolved();

                if (LbpArchiveToolkit.Configuration.ConfigManager.LoadDbIntoRam)
                {
                    await EnsureRamDbLoadedAsync(null).ConfigureAwait(false);
                }

                using var conn = new SqliteConnection(GetConnectionString());
                await conn.OpenAsync(token).ConfigureAwait(false);
                ApplyConnectionOptimizations(conn);

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

                if (randomSingle)
                {
                    queryBuilder.Append(" ORDER BY RANDOM() LIMIT 1");
                }
                else
                {
                    queryBuilder.Append(" ORDER BY heartCount DESC");

                    if (limitFilter != "All" && int.TryParse(limitFilter, out int limit))
                    {
                        queryBuilder.Append($" LIMIT {limit}");
                    }
                }

                using var cmd = new SqliteCommand(queryBuilder.ToString(), conn);
                foreach (var param in parameters) cmd.Parameters.Add(param);

                using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await reader.ReadAsync(token).ConfigureAwait(false))
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

        private static readonly FrozenSet<string> _actualGenres = [
            with(StringComparer.OrdinalIgnoreCase),
            "Arcade", "Cinematic", "Driving", "Fighter", "FirstOrThirdPerson",
            "Gallery", "MiniGames", "Multiplayer", "Platform", "PlatformerRaces",
            "PlatformShooter", "Puzzle", "RPG", "Shooter", "Social", "Sports",
            "Story", "Strategy", "SurvivalChallenge", "TOP_DOWN", "Tutorial",
            "UniquePlatformer", "VehicleShooter"
        ];

        public async Task<List<string>> GetContributorsAsync(long slotId, CancellationToken token = default)
        {
            await Task.Run(() => EnsureSchemaResolved()).ConfigureAwait(false);
            var list = new List<string>();
            if (!_hasContribsTable) return list;

            using var conn = new SqliteConnection(GetConnectionString());
            await conn.OpenAsync(token).ConfigureAwait(false);
            ApplyConnectionOptimizations(conn);

            using var cmd = new SqliteCommand("SELECT npHandle FROM level_contributors WHERE slot_id = @id ORDER BY npHandle ASC", conn);
            cmd.Parameters.AddWithValue("@id", slotId);

            using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                list.Add(reader.GetString(0));
            }
            return list;
        }

        public async Task<List<string>> GetObjectContributorsAsync(long slotId, CancellationToken token = default)
        {
            await Task.Run(() => EnsureSchemaResolved()).ConfigureAwait(false);
            var list = new List<string>();
            if (!_hasObjectContribsTable) return list;

            using var conn = new SqliteConnection(GetConnectionString());
            await conn.OpenAsync(token).ConfigureAwait(false);
            ApplyConnectionOptimizations(conn);

            using var cmd = new SqliteCommand("SELECT npHandle FROM object_contributors WHERE slot_id = @id ORDER BY npHandle ASC", conn);
            cmd.Parameters.AddWithValue("@id", slotId);

            using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                list.Add(reader.GetString(0));
            }
            return list;
        }

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

        private void BuildContribFtsSearchCondition(StringBuilder query, List<SqliteParameter> parameters, string keyword, bool exact)
        {
            string matchTerm = "";
            string Sanitize(string s) => System.Text.RegularExpressions.Regex.Replace(s, @"[\^\*\(\)\[\]\{\}\:\;\+\'\""]", "");

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

            query.Append("s.id IN (SELECT slot_id FROM level_contributors_fts WHERE level_contributors_fts MATCH @matchContrib) ");
            parameters.Add(new SqliteParameter("@matchContrib", matchTerm));
        }

        private void BuildContribSearchCondition(StringBuilder query, List<SqliteParameter> parameters, string keyword, bool exact)
        {
            if (exact)
            {
                query.Append("s.id IN (SELECT slot_id FROM level_contributors WHERE npHandle LIKE @kContrib) ");
                parameters.Add(new SqliteParameter("@kContrib", $"%{keyword}%"));
            }
            else
            {
                var words = keyword.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var conds = new List<string>();
                for (int i = 0; i < words.Length; i++)
                {
                    conds.Add($"npHandle LIKE @wContrib{i}");
                    parameters.Add(new SqliteParameter($"@wContrib{i}", $"%{words[i]}%"));
                }
                query.Append("s.id IN (SELECT slot_id FROM level_contributors WHERE " + string.Join(" AND ", conds) + ") ");
            }
        }

        private void BuildObjectFtsSearchCondition(StringBuilder query, List<SqliteParameter> parameters, string keyword, bool exact)
        {
            string matchTerm = "";
            string Sanitize(string s) => System.Text.RegularExpressions.Regex.Replace(s, @"[\^\*\(\)\[\]\{\}\:\;\+\'\""]", "");

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

            query.Append("s.id IN (SELECT slot_id FROM object_contributors_fts WHERE object_contributors_fts MATCH @matchObjContrib) ");
            parameters.Add(new SqliteParameter("@matchObjContrib", matchTerm));
        }

        private void BuildObjectSearchCondition(StringBuilder query, List<SqliteParameter> parameters, string keyword, bool exact)
        {
            if (exact)
            {
                query.Append("s.id IN (SELECT slot_id FROM object_contributors WHERE npHandle LIKE @kObjContrib) ");
                parameters.Add(new SqliteParameter("@kObjContrib", $"%{keyword}%"));
            }
            else
            {
                var words = keyword.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var conds = new List<string>();
                for (int i = 0; i < words.Length; i++)
                {
                    conds.Add($"npHandle LIKE @wObjContrib{i}");
                    parameters.Add(new SqliteParameter($"@wObjContrib{i}", $"%{words[i]}%"));
                }
                query.Append("s.id IN (SELECT slot_id FROM object_contributors WHERE " + string.Join(" AND ", conds) + ") ");
            }
        }

        private void BuildFilters(StringBuilder query, List<SqliteParameter> parameters, int gameFilter, string? genreFilter, string pfx, AdvancedSearchCriteria advanced, long reqL0, long reqL1, long reqT0, long reqT1, bool useFtsForTags)
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

            if (advanced.IsTeamPick && !useFtsForTags && _colMmPick != "NULL")
            {
                query.Append($" AND {pfx}{_colMmPick} = 1");
            }


        }

        #endregion

        #region Schema Resolution & Utilities

        private void EnsureSchemaResolved()
        {
            if (_isSchemaResolved) return;

            if (!File.Exists(_dbPath)) return;

            lock (_schemaLock)
            {
                if (_isSchemaResolved) return;

                if (!File.Exists(_dbPath)) return;

                var connStringBuilder = new SqliteConnectionStringBuilder { DataSource = _dbPath, Mode = SqliteOpenMode.ReadWrite };
                using var conn = new SqliteConnection(connStringBuilder.ConnectionString);
                conn.Open();

                ApplyConnectionOptimizations(conn);

                var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (var cmdInfo = new SqliteCommand("PRAGMA table_info(slot)", conn))
                using (var readerInfo = cmdInfo.ExecuteReader())
                {
                    while (readerInfo.Read()) columns.Add(readerInfo.GetString(1));
                }

                using (var cmdTables = new SqliteCommand("SELECT name FROM sqlite_master WHERE type='table' AND name IN ('slot_fts', 'slot_tags_fts', 'level_contributors', 'level_contributors_fts', 'object_contributors', 'object_contributors_fts')", conn))
                using (var reader = cmdTables.ExecuteReader())
                {
                    var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    while (reader.Read()) tables.Add(reader.GetString(0));

                    _hasFtsTable = tables.Contains("slot_fts");
                    _hasTagsFtsTable = tables.Contains("slot_tags_fts");
                    _hasContribsTable = tables.Contains("level_contributors");
                    _hasContribFtsTable = tables.Contains("level_contributors_fts");
                    _hasObjectContribsTable = tables.Contains("object_contributors");
                    _hasObjectContribFtsTable = tables.Contains("object_contributors_fts");
                }

                _colGame = GetDbColumn(columns, "gameVersion", "game");
                _colDate = GetDbColumn(columns, "timestamp", "publishDate", "firstPublished", "timeCreated");
                _colDesc = GetDbColumn(columns, "description", "desc");
                _colPlay = GetDbColumn(columns, "playCount", "plays", "play_count");
                _colCompletion = GetDbColumn(columns, "completionCount", "completions");
                _colHeart = GetDbColumn(columns, "heartCount", "hearts", "heart_count");
                _colGenre = GetDbColumn(columns, "genre", "levelGenre", "level_genre");
                _colHash = GetDbColumn(columns, "rootLevel", "root_level", "rootLevelHash", "hash");
                _colIcon = GetDbColumn(columns, "icon", "iconHash");
                _colLabels = GetDbColumn(columns, "authorLabels");
                _colCommunityLabels = GetDbColumn(columns, "labels", "communityLabels");
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

        private static readonly FrozenDictionary<string, int> _tagToIndexMap = TagParser.GetNames()
            .Select((name, index) => new { name, index })
            .ToFrozenDictionary(x => x.name, x => x.index, StringComparer.OrdinalIgnoreCase);

        private static int GetTagIndex(string tagName) => 
            _tagToIndexMap.TryGetValue(tagName, out int index) ? index : -1;

        #endregion
    }
}
