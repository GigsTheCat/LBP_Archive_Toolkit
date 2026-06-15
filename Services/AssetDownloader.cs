using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using LbpArchiveToolkit.Configuration;
using LbpArchiveToolkit.Models;
using LbpArchiveToolkit.Utils;

namespace LbpArchiveToolkit.Services
{
    /// <summary>
    /// Orchestrates the fetching of LBP level assets via HTTP or Local Archives. 
    /// Manages concurrency, rate-limiting, and recursive dependency discovery.
    /// </summary>
    public static class AssetDownloader
    {
        #region State & Constants

        private static volatile Task _globalRateLimitTask = Task.CompletedTask;
        private static readonly object _rateLimitLock = new object();
        
        private static DateTime _lastRequestTime = DateTime.MinValue;
        private static readonly object _pacingLock = new object();

        private static readonly ConcurrentDictionary<string, Func<string, string, string, string, string>> _pathBuilderCache = new();

        #endregion

        #region Public API

        /// <summary>
        /// Clears internal path caches and resets network pacing timers.
        /// </summary>
        public static void CleanupLocalArchives()
        {
            _pathBuilderCache.Clear();
            _globalRateLimitTask = Task.CompletedTask;
            _lastRequestTime = DateTime.MinValue;
        }

        /// <summary>
        /// Extracts a specific resource from a local archive folder based on its SHA1 hash.
        /// </summary>
        public static async Task<byte[]?> ExtractLocalArchiveToMemoryAsync(string hash, string baseDir, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(baseDir) || hash.Length < 4) return null;

            string part1 = hash.Substring(0, 2);
            string part2 = hash.Substring(2, 2);

            var pathBuilder = _pathBuilderCache.GetOrAdd(part1, p1 =>
            {
                if (Directory.Exists(Path.Combine(baseDir, $"dry{p1}", p1)))
                    return (b, p_1, p_2, h) => Path.Combine(b, $"dry{p_1}", p_1, p_2, h);

                if (Directory.Exists(Path.Combine(baseDir, $"dry23r{p1[0]}", $"dry{p1}", p1)))
                    return (b, p_1, p_2, h) => Path.Combine(b, $"dry23r{p1[0]}", $"dry{p1}", p1, p_2, h);

                if (Directory.Exists(Path.Combine(baseDir, $"dry{p1}")))
                    return (b, p_1, p_2, h) => Path.Combine(b, $"dry{p_1}", p_2, h);

                return (b, p_1, p_2, h) => Path.Combine(b, p_1, p_2, h);
            });

            string exactPath = pathBuilder(baseDir, part1, part2, hash);
            if (File.Exists(exactPath)) return await File.ReadAllBytesAsync(exactPath, token);

            string flatPath = Path.Combine(baseDir, part1, part2, hash);
            if (exactPath != flatPath && File.Exists(flatPath)) return await File.ReadAllBytesAsync(flatPath, token);

            return null;
        }

        /// <summary>
        /// The main entry point for archiving a level. Downloads dependencies, builds the save file, and writes to disk.
        /// </summary>
        public static async Task<(bool Success, string ErrorMessage)> RunExtractionProcessAsync(LevelItem lvl, string dbPath, string backupDir, HttpClient client, CancellationToken externalToken, IProgress<(int processed, int total, string message)>? progress = null)
        {
            if (string.IsNullOrEmpty(lvl.Hash)) return (false, "Level hash is missing or empty.");

            var slotInfo = CreateSlotInfo(lvl);
            PopulateSlotInfoFromDatabase(lvl.Id, dbPath, slotInfo);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            var token = cts.Token;

            int maxConcurrent = ConfigManager.MaxParallelDownloads > 0 ? ConfigManager.MaxParallelDownloads : 10;
            using var semaphore = new SemaphoreSlim(maxConcurrent);
            var ctx = new DownloadContext(client, token, semaphore, progress);

            try
            {
                string rootHash = lvl.Hash.ToLowerInvariant();
                ctx.AddDiscoveredHash(rootHash);
                
                string iconHashStr = "";
                if (!string.IsNullOrEmpty(lvl.IconHash))
                {
                    iconHashStr = lvl.IconHash.ToLowerInvariant();
                    ctx.AddDiscoveredHash(iconHashStr);
                }

                ctx.ReportProgress("Starting extraction...");

                await Task.Run(async () =>
                {
                    var mainTask = DownloadAssetRecursiveAsync(rootHash, ctx);
                    var iconTask = string.IsNullOrEmpty(iconHashStr) ? Task.CompletedTask : DownloadAssetRecursiveAsync(iconHashStr, ctx);
                    
                    await Task.WhenAll(mainTask, iconTask);
                });

                if (token.IsCancellationRequested) return (false, "Extraction was cancelled.");

                byte[] rootHashBytes = SaveDataBuilder.StringToByteArray(lvl.Hash);
                if (!ctx.Resources.ContainsKey(rootHashBytes)) 
                {
                    return (false, "The root level file could not be fetched (Likely missing).");
                }

                ctx.ReportProgress("Encrypting and building save archive...");

                var sortedResources = new SortedDictionary<byte[], byte[]>(ctx.Resources, new SaveDataBuilder.ByteArrayComparer());
                await SaveDataBuilder.BuildAndWriteSaveDataAsync(lvl, slotInfo, sortedResources, backupDir, client, token);

                ctx.ReportProgress("Finished successfully!");
                return (true, string.Empty);
            }
            catch (OperationCanceledException) { return (false, "Extraction was cancelled."); }
            catch (Exception ex)
            {
                cts.Cancel();
                return (false, $"File saving or network error: {ex.Message}");
            }
        }

        #endregion

        #region Extraction Orchestration

        private static void PopulateSlotInfoFromDatabase(long levelId, string dbPath, SlotInfo slotInfo)
        {
            try
            {
                using var conn = new SqliteConnection($"Data Source={dbPath}");
                conn.Open();
                string q = "SELECT minPlayers, maxPlayers, levelType, shareable, initiallyLocked, background, isSubLevel, isAdventurePlanet, authorLabels FROM slot WHERE id = @id";
                using var cmd = new SqliteCommand(q, conn);
                cmd.Parameters.AddWithValue("@id", levelId);
                
                using var r = cmd.ExecuteReader();
                if (r.Read())
                {
                    try { if (!r.IsDBNull(0)) slotInfo.MinPlayers = r.GetInt32(0); } catch { }
                    try { if (!r.IsDBNull(1)) slotInfo.MaxPlayers = r.GetInt32(1); } catch { }
                    try { if (!r.IsDBNull(2)) slotInfo.LevelType = r.GetInt32(2); } catch { }
                    try { if (!r.IsDBNull(3)) slotInfo.Shareable = r.GetBoolean(3); } catch { }
                    try { if (!r.IsDBNull(4)) slotInfo.InitiallyLocked = r.GetBoolean(4); } catch { }
                    try { if (!r.IsDBNull(5)) slotInfo.BackgroundGuid = (uint)r.GetInt64(5); } catch { }
                    try { if (!r.IsDBNull(6)) slotInfo.IsSubLevel = r.GetBoolean(6); } catch { }
                    try { if (!r.IsDBNull(7)) slotInfo.IsAdventurePlanet = r.GetBoolean(7); } catch { }
                    try { if (!r.IsDBNull(8)) slotInfo.Labels = LabelParser.ParseLabelHashes((byte[])r.GetValue(8)); } catch { }
                }
            } 
            catch { } 
        }

        private static SlotInfo CreateSlotInfo(LevelItem lvl)
        {
            return new SlotInfo
            {
                NpHandle = lvl.Creator ?? "Unknown",
                Name = lvl.LevelName ?? "Unnamed Level",
                Description = lvl.Description ?? "",
                RootLevelHash = lvl.Hash ?? "",
                IconHash = lvl.IconHash ?? "",
                GameVersion = lvl.Game == "LBP1" ? 1 : (lvl.Game == "LBP3" ? 3 : 2)
            };
        }

        #endregion

        #region Network & Downloading

        private static async Task DownloadAssetRecursiveAsync(string currentHash, DownloadContext ctx)
        {
            await Task.Yield();
            if (ctx.Token.IsCancellationRequested) return;

            bool isLocal = ConfigManager.DownloadServer.ToLowerInvariant() == "local";
            bool success = false;
            byte[]? fileData = null; 

            if (isLocal)
            {
                await ctx.Semaphore.WaitAsync(ctx.Token);
                try
                {
                    fileData = await ExtractLocalArchiveToMemoryAsync(currentHash, ConfigManager.LocalArchivePath ?? "", ctx.Token);
                    success = fileData != null; 
                }
                catch (OperationCanceledException) { return; }
                finally { ctx.Semaphore.Release(); }
            }
            else
            {
                (success, fileData) = await FetchFileWithRetriesAsync(currentHash, ctx);
            }

            if (ctx.Token.IsCancellationRequested) return;

            if (success && fileData != null)
            {
                ctx.AddResource(currentHash, fileData);

                var deps = SaveDataBuilder.GetDependenciesFast(fileData);
                var newDeps = new List<string>();

                foreach (var dep in deps)
                {
                    if (ctx.AddDiscoveredHash(dep))
                    {
                        newDeps.Add(dep);
                    }
                }

                var tasks = new List<Task>();
                foreach (var dep in newDeps)
                {
                    tasks.Add(DownloadAssetRecursiveAsync(dep, ctx)); 
                }
                
                ctx.IncrementProcessed();
                await Task.WhenAll(tasks);
            }
            else
            {
                ctx.IncrementProcessed();
            }
        }

        private static async Task<(bool success, byte[]? data)> FetchFileWithRetriesAsync(string currentHash, DownloadContext ctx)
        {
            int maxRetries = 5;
            int currentTry = 0;
            bool success = false;
            byte[]? fileData = null;
            string url = GetDownloadUrl(currentHash, ConfigManager.DownloadServer);

            await ctx.Semaphore.WaitAsync(ctx.Token);
            try
            {
                while (!success && currentTry < maxRetries)
                {
                    if (ctx.Token.IsCancellationRequested) break;

                    Task activeDelayTask = _globalRateLimitTask;
                    if (!activeDelayTask.IsCompleted)
                    {
                        ctx.IncrementRetryingThreads();
                        ctx.ReportProgress("Server Paused: Global rate limit active...");
                        
                        try { await activeDelayTask; } 
                        catch (OperationCanceledException) { break; }
                        finally { ctx.DecrementRetryingThreads(); }
                        
                        if (ctx.Token.IsCancellationRequested) break;
                    }

                    currentTry++;
                    int delayMs = 2000 * currentTry; 
                    string failReason = "Network Timeout";
                    bool hitRateLimit = false;

                    int waitTimeMs = 0;
                    int requiredPacingMs = GetServerPacingDelay(ConfigManager.DownloadServer);
                    
                    if (requiredPacingMs > 0)
                    {
                        lock (_pacingLock)
                        {
                            var timeSinceLast = DateTime.UtcNow - _lastRequestTime;
                            var minTime = TimeSpan.FromMilliseconds(requiredPacingMs);
                            if (timeSinceLast < minTime)
                            {
                                waitTimeMs = (int)(minTime - timeSinceLast).TotalMilliseconds;
                                _lastRequestTime = DateTime.UtcNow.AddMilliseconds(waitTimeMs);
                            }
                            else
                            {
                                _lastRequestTime = DateTime.UtcNow;
                            }
                        }
                    }

                    if (waitTimeMs > 0)
                    {
                        try { await Task.Delay(waitTimeMs, ctx.Token); }
                        catch (OperationCanceledException) { break; }
                    }

                    if (ctx.Token.IsCancellationRequested) break;

                    try
                    {
                        using var response = await ctx.Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ctx.Token);
                        
                        if (response.IsSuccessStatusCode)
                        {
                            fileData = await response.Content.ReadAsByteArrayAsync(ctx.Token);
                            string computedHash = Convert.ToHexStringLower(SHA1.HashData(fileData));
                            
                            if (computedHash == currentHash) success = true;
                            else { success = false; fileData = null; failReason = "Hash Mismatch"; }
                        }
                        else if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500)
                        {
                            failReason = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
                            
                            if (response.Headers.RetryAfter != null && response.Headers.RetryAfter.Delta.HasValue)
                                delayMs = (int)response.Headers.RetryAfter.Delta.Value.TotalMilliseconds;
                            else if ((int)response.StatusCode == 429)
                                delayMs = 10000;
                                
                            hitRateLimit = true;

                            lock (_rateLimitLock)
                            {
                                if (_globalRateLimitTask.IsCompleted)
                                {
                                    _globalRateLimitTask = Task.Delay(delayMs, ctx.Token);
                                }
                            }
                        }
                        else if (response.StatusCode == System.Net.HttpStatusCode.NotFound || response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                        {
                            success = true; 
                        }
                        else
                        {
                            failReason = $"HTTP {(int)response.StatusCode}";
                        }
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex) { failReason = ex.InnerException?.Message ?? ex.Message; }

                    if (!success && currentTry < maxRetries && !ctx.Token.IsCancellationRequested && !hitRateLimit)
                    {
                        ctx.IncrementRetryingThreads();
                        ctx.ReportProgress($"Retrying ({currentTry}/{maxRetries}): {failReason}. Waiting {delayMs / 1000}s...");
                        
                        try { await Task.Delay(delayMs, ctx.Token); } 
                        catch (OperationCanceledException) { break; }
                        finally { ctx.DecrementRetryingThreads(); }
                    }
                }
            }
            finally
            {
                ctx.Semaphore.Release();
            }

            return (success, fileData);
        }

        private static int GetServerPacingDelay(string server)
        {
            string srv = server.ToLowerInvariant();
            if (srv == "local") return 0;
            if (srv == "bonsai" || srv == "refresh") return 120; 
            if (srv == "archive") return 100;                    
            return 70;                                          
        }

        private static string GetDownloadUrl(string hash, string server)
        {
            string h = hash.ToLowerInvariant();
            string srv = server.ToLowerInvariant();
            if (srv == "bonsai" || srv == "refresh") return $"https://lbp.lbpbonsai.com/api/v3/assets/{h}/download";
            if (srv == "archive") return $"https://archive.org/download/dry23r{h[0]}/dry{h.Substring(0, 2)}.zip/{h.Substring(0, 2)}%2F{h.Substring(2, 2)}%2F{h}";
            return $"https://lbparchive.zaprit.fish/{h.Substring(0, 2)}/{h.Substring(2, 2)}/{h}";
        }

        private class DownloadContext
        {
            public readonly HttpClient Client;
            public readonly CancellationToken Token;
            public readonly SemaphoreSlim Semaphore;
            public readonly ConcurrentDictionary<byte[], byte[]> Resources = new(new SaveDataBuilder.ByteArrayComparer());

            private readonly IProgress<(int processed, int total, string message)>? _progress;
            private readonly HashSet<string> _downloadedHashes = new(StringComparer.OrdinalIgnoreCase);
            private readonly object _stateLock = new();

            private int _totalDiscovered;
            private int _totalProcessed;
            private int _retryingThreads;

            private long _lastReportTime = 0;
            private readonly object _reportLock = new();

            public int TotalDiscovered => _totalDiscovered;

            public DownloadContext(HttpClient client, CancellationToken token, SemaphoreSlim semaphore, IProgress<(int, int, string)>? progress)
            {
                Client = client;
                Token = token;
                Semaphore = semaphore;
                _progress = progress;
            }

            public bool AddDiscoveredHash(string hash)
            {
                lock (_stateLock)
                {
                    if (_downloadedHashes.Add(hash))
                    {
                        _totalDiscovered++;
                        return true;
                    }
                    return false;
                }
            }

            public void AddResource(string hashStr, byte[] data)
            {
                byte[] hashBytes = SaveDataBuilder.StringToByteArray(hashStr);
                Resources[hashBytes] = data;
            }

            public void IncrementProcessed()
            {
                Interlocked.Increment(ref _totalProcessed);
                ReportProgress();
            }

            public void IncrementRetryingThreads() => Interlocked.Increment(ref _retryingThreads);
            public void DecrementRetryingThreads() => Interlocked.Decrement(ref _retryingThreads);

            public void ReportProgress(string? overrideMessage = null)
            {
                if (_progress == null) return;
                
                if (overrideMessage == null)
                {
                    lock (_reportLock)
                    {
                        long now = Environment.TickCount64;
                        if (now - _lastReportTime < 33) 
                            return; 
                        
                        _lastReportTime = now;
                    }
                }
                
                int paused = Volatile.Read(ref _retryingThreads);
                int processed = Volatile.Read(ref _totalProcessed);
                int discovered = Volatile.Read(ref _totalDiscovered);

                bool isLocal = ConfigManager.DownloadServer.ToLowerInvariant() == "local";
                
                string status = overrideMessage ?? (paused > 0 
                    ? $"Server Paused ({paused} thread(s) waiting)..." 
                    : (isLocal ? "Extracting local assets..." : "Downloading assets..."));

                _progress.Report((processed, discovered, status));
            }
        }

        #endregion
    }
}