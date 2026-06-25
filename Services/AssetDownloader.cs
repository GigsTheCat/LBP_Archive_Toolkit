using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;
using System.Threading.RateLimiting;
using Microsoft.Data.Sqlite;
using LbpArchiveToolkit.Configuration;
using LbpArchiveToolkit.Models;
using LbpArchiveToolkit.Utils;

namespace LbpArchiveToolkit.Services
{
    public class ExtractionConfig
    {
        public string DownloadServer { get; set; } = "zaprit";
        public string LocalArchivePath { get; set; } = "";
        public int MaxParallelDownloads { get; set; } = 10;
    }

    /// <summary>
    /// Orchestrates the fetching of LBP level assets via HTTP or Local Archives. 
    /// Manages concurrency, rate-limiting, and flat dependency queue processing.
    /// </summary>
    public static class AssetDownloader
    {
        private static volatile Task _globalRateLimitTask = Task.CompletedTask;
        private static readonly System.Threading.Lock _rateLimitLock = new();
        
        private static TokenBucketRateLimiter? _rateLimiter;
        private static string _lastConfiguredServer = string.Empty;
        private static readonly System.Threading.Lock _limiterLock = new();

        private static readonly ConcurrentDictionary<string, Func<string, string, string, string, string>> _layoutBuilderCache = new();

        public static void CleanupLocalArchives()
        {
            _layoutBuilderCache.Clear();
            _globalRateLimitTask = Task.CompletedTask;
            
            lock (_limiterLock)
            {
                _rateLimiter?.Dispose();
                _rateLimiter = null;
                _lastConfiguredServer = string.Empty;
            }
        }

        private static readonly System.Buffers.SearchValues<char> HexChars = 
            System.Buffers.SearchValues.Create("0123456789abcdefABCDEF");

        private static bool IsValidHash(string hash)
        {
            if (string.IsNullOrWhiteSpace(hash) || hash.Length > 40) return false;
            return !hash.AsSpan().ContainsAnyExcept(HexChars);
        }

        private static Func<string, string, string, string, string> DetermineLayoutRobust(string baseDir)
        {
            try
            {
                foreach (string dir in Directory.EnumerateDirectories(baseDir))
                {
                    string name = Path.GetFileName(dir).ToLowerInvariant();
                    
                    if (name.StartsWith("dry23r"))
                        return static (b, p_1, p_2, h) => Path.Combine(b, $"dry23r{p_1[0]}", $"dry{p_1}", p_1, p_2, h);
                    
                    if (name.StartsWith("dry") && name.Length == 5)
                    {
                        string p1 = name.Substring(3, 2);
                        if (Directory.Exists(Path.Combine(dir, p1)))
                            return static (b, p_1, p_2, h) => Path.Combine(b, $"dry{p_1}", p_1, p_2, h);
                        else
                            return static (b, p_1, p_2, h) => Path.Combine(b, $"dry{p_1}", p_2, h);
                    }
                    
                    if (name.Length == 2 && char.IsAsciiHexDigit(name[0]) && char.IsAsciiHexDigit(name[1]))
                        return static (b, p_1, p_2, h) => Path.Combine(b, p_1, p_2, h);
                }
            }
            catch { }
            
            return static (b, p_1, p_2, h) => Path.Combine(b, p_1, p_2, h);
        }

        public static async Task<byte[]?> ExtractLocalArchiveToMemoryAsync(string hash, string baseDir, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(baseDir) || !IsValidHash(hash) || hash.Length < 4) return null;

            string part1 = hash.Substring(0, 2);
            string part2 = hash.Substring(2, 2);

            var pathBuilder = _layoutBuilderCache.GetOrAdd(baseDir, DetermineLayoutRobust);

            try
            {
                string exactPath = pathBuilder(baseDir, part1, part2, hash);
                if (File.Exists(exactPath)) return await File.ReadAllBytesAsync(exactPath, token).ConfigureAwait(false);

                string flatPath = Path.Combine(baseDir, part1, part2, hash);
                if (exactPath != flatPath && File.Exists(flatPath)) return await File.ReadAllBytesAsync(flatPath, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LbpArchiveToolkit.LogManager.Log("AssetDownloader.ExtractLocalArchiveToMemoryAsync", ex);
            }

            return null;
        }

        public static async Task<(bool Success, string ErrorMessage)> RunExtractionProcessAsync(LevelItem lvl, string dbPath, string backupDir, HttpClient client, ExtractionConfig config, CancellationToken externalToken, IProgress<(int processed, int total, string message)>? progress = null)
        {
            if (string.IsNullOrEmpty(lvl.Hash)) return (false, "Level hash is missing or empty.");
            if (!IsValidHash(lvl.Hash)) return (false, "Level hash contains invalid path characters.");

            var slotInfo = CreateSlotInfo(lvl);
            PopulateSlotInfoFromDatabase(lvl.Id, dbPath, slotInfo);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            var token = cts.Token;

            int maxConcurrent = config.MaxParallelDownloads > 0 ? config.MaxParallelDownloads : 10;

            var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
            {
                SingleReader = false,
                SingleWriter = false
            });

            var ctx = new DownloadContext(client, token, progress, channel.Writer, config);

            try
            {
                string rootHash = lvl.Hash.ToLowerInvariant();
                bool isRootGuid = rootHash.Length <= 8;

                if (!isRootGuid)
                {
                    ctx.AddDiscoveredHash(rootHash);
                    ctx.IncrementPending();
                    channel.Writer.TryWrite(rootHash);
                }
                
                string iconHashStr = "";
                if (!string.IsNullOrEmpty(lvl.IconHash))
                {
                    iconHashStr = lvl.IconHash.ToLowerInvariant();
                    if (!IsValidHash(iconHashStr)) return (false, "Level icon hash contains invalid path characters.");
                    if (iconHashStr.Length > 8)
                    {
                        ctx.AddDiscoveredHash(iconHashStr);
                        ctx.IncrementPending();
                        channel.Writer.TryWrite(iconHashStr);
                    }
                }

                ctx.ReportProgress("Starting extraction...");

                // If no remote assets are queued (e.g. built-in level with a GUID), complete immediately
                if (ctx.PendingItems == 0)
                {
                    channel.Writer.TryComplete();
                }

                await Task.Run(async () =>
                {
                    int workerCount = maxConcurrent;
                    Task[] workers = new Task[workerCount];
                    for (int i = 0; i < workerCount; i++)
                    {
                        workers[i] = ProcessQueueAsync(channel.Reader, ctx);
                    }
                    await Task.WhenAll(workers).ConfigureAwait(false);
                }).ConfigureAwait(false);

                if (token.IsCancellationRequested) return (false, "Extraction was cancelled.");

                if (!isRootGuid && !ctx.Resources.ContainsKey(rootHash)) 
                {
                    return (false, "The root level file could not be fetched (Likely missing from server).");
                }

                ctx.ReportProgress("Encrypting and building save archive...");

                var sortedResources = new SortedDictionary<string, byte[]>(ctx.Resources, StringComparer.OrdinalIgnoreCase);
                await SaveDataBuilder.BuildAndWriteSaveDataAsync(lvl, slotInfo, sortedResources, backupDir, client, token).ConfigureAwait(false);

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

        private static async Task ProcessQueueAsync(ChannelReader<string> reader, DownloadContext ctx)
        {
            try
            {
                await foreach (var currentHash in reader.ReadAllAsync(ctx.Token).ConfigureAwait(false))
                {
                    if (ctx.Token.IsCancellationRequested) break;
                    if (!IsValidHash(currentHash)) continue;

                    try
                    {
                        bool isLocal = ctx.Config.DownloadServer.ToLowerInvariant() == "local";
                        bool success = false;
                        byte[]? fileData = null; 

                        if (isLocal)
                        {
                            try
                            {
                                fileData = await ExtractLocalArchiveToMemoryAsync(currentHash, ctx.Config.LocalArchivePath, ctx.Token).ConfigureAwait(false);
                                success = fileData != null; 
                            }
                            catch (OperationCanceledException) { break; }
                        }
                        else
                        {
                            (success, fileData) = await FetchFileWithRetriesAsync(currentHash, ctx).ConfigureAwait(false);
                        }

                        if (ctx.Token.IsCancellationRequested) break;

                        if (success && fileData != null)
                        {
                            ctx.AddResource(currentHash, fileData);

                            var deps = SaveDataBuilder.GetDependenciesFast(fileData);
                            foreach (var dep in deps)
                            {
                                if (IsValidHash(dep) && ctx.AddDiscoveredHash(dep))
                                {
                                    ctx.IncrementPending();
                                    ctx.QueueWriter.TryWrite(dep);
                                }
                            }
                        }
                    }
                    finally
                    {
                        ctx.IncrementProcessed();
                        ctx.DecrementPending();
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                LbpArchiveToolkit.LogManager.Log("AssetDownloader.ProcessQueueAsync", ex);
                ctx.QueueWriter.TryComplete(ex);
            }
        }

        private static void PopulateSlotInfoFromDatabase(long levelId, string dbPath, SlotInfo slotInfo)
{
    try
    {
        var connStringBuilder = new SqliteConnectionStringBuilder { DataSource = dbPath };
        using var conn = new SqliteConnection(connStringBuilder.ConnectionString);
        conn.Open();
        string q = "SELECT minPlayers, maxPlayers, levelType, shareable, initiallyLocked, background, isSubLevel, isAdventurePlanet, authorLabels FROM slot WHERE id = @id";
                using var cmd = new SqliteCommand(q, conn);
                cmd.Parameters.AddWithValue("@id", levelId);
                
                using var r = cmd.ExecuteReader();
                if (r.Read())
                {
                    try { if (!r.IsDBNull(0)) slotInfo.MinPlayers = Convert.ToInt32(r.GetValue(0)); } catch { }
                    try { if (!r.IsDBNull(1)) slotInfo.MaxPlayers = Convert.ToInt32(r.GetValue(1)); } catch { }
                    try 
                    { 
                        if (!r.IsDBNull(2)) 
                        {
                            if (int.TryParse(r.GetValue(2).ToString(), out int parsedType))
                            {
                                slotInfo.LevelType = parsedType;
                            }
                        } 
                    } catch { }
                    try { if (!r.IsDBNull(3)) slotInfo.Shareable = Convert.ToBoolean(r.GetValue(3)); } catch { }
                    try { if (!r.IsDBNull(4)) slotInfo.InitiallyLocked = Convert.ToBoolean(r.GetValue(4)); } catch { }
                    try { if (!r.IsDBNull(5)) slotInfo.BackgroundGuid = Convert.ToUInt32(r.GetValue(5)); } catch { }
                    try { if (!r.IsDBNull(6)) slotInfo.IsSubLevel = Convert.ToBoolean(r.GetValue(6)); } catch { }
                    try { if (!r.IsDBNull(7)) slotInfo.IsAdventurePlanet = Convert.ToBoolean(r.GetValue(7)); } catch { }
                    try { if (!r.IsDBNull(8)) slotInfo.Labels = LabelParser.ParseLabelHashes(r.GetFieldValue<byte[]>(8)); } catch { }
                }
            } 
            catch (Exception ex)
            {
                LbpArchiveToolkit.LogManager.Log("AssetDownloader.PopulateSlotInfoFromDatabase", ex);
            } 
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

        private static async Task<(bool success, byte[]? data)> FetchFileWithRetriesAsync(string currentHash, DownloadContext ctx)
        {
            int maxRetries = 5;
            int currentTry = 0;
            bool success = false;
            byte[]? fileData = null;
            string url = GetDownloadUrl(currentHash, ctx.Config.DownloadServer);
            if (string.IsNullOrEmpty(url)) return (false, null);

            while (!success && currentTry < maxRetries)
            {
                    if (ctx.Token.IsCancellationRequested) break;

                    Task activeDelayTask = _globalRateLimitTask;
                    if (!activeDelayTask.IsCompleted)
                    {
                        ctx.IncrementRetryingThreads();
                        ctx.ReportProgress("Server Paused: Global rate limit active...");
                        
                        try 
                        { 
                            await activeDelayTask.ConfigureAwait(false); 
                            await Task.Delay(Random.Shared.Next(50, 250), ctx.Token).ConfigureAwait(false);
                        } 
                        catch (OperationCanceledException) 
                        { 
                            // Only cancel this thread's operation if this context itself was explicitly cancelled
                            if (ctx.Token.IsCancellationRequested) break;
                        }
                        finally { ctx.DecrementRetryingThreads(); }
                        
                        if (ctx.Token.IsCancellationRequested) break;
                    }

                    currentTry++;
                    int delayMs = 2000 * currentTry; 
                    string failReason = "Network Timeout";
                    bool hitRateLimit = false;

                    int requiredPacingMs = GetServerPacingDelay(ctx.Config.DownloadServer);
                    
                    if (requiredPacingMs > 0)
                    {
                        TokenBucketRateLimiter currentLimiter;
                        lock (_limiterLock)
                        {
                            if (_rateLimiter == null || _lastConfiguredServer != ctx.Config.DownloadServer)
                            {
                                _rateLimiter?.Dispose();
                                _rateLimiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
                                {
                                    TokenLimit = 1, // Enforces strict pacing (1 request at a time)
                                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                                    QueueLimit = 10000,
                                    ReplenishmentPeriod = TimeSpan.FromMilliseconds(requiredPacingMs),
                                    TokensPerPeriod = 1,
                                    AutoReplenishment = true
                                });
                                _lastConfiguredServer = ctx.Config.DownloadServer;
                            }
                            currentLimiter = _rateLimiter;
                        }

                        using var lease = await currentLimiter.AcquireAsync(1, ctx.Token).ConfigureAwait(false);
                        if (!lease.IsAcquired) break; // Exits safely if cancellation triggered during queue
                    }

                    if (ctx.Token.IsCancellationRequested) break;

                    try
                    {
                        using var response = await ctx.Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ctx.Token).ConfigureAwait(false);
                        
                        if (response.IsSuccessStatusCode)
                        {
                            long? contentLength = response.Content.Headers.ContentLength;
                            if (contentLength.HasValue && contentLength.Value > 104857600)
                            {
                                throw new InvalidOperationException("File exceeds maximum allowed size.");
                            }

                            using (var stream = await response.Content.ReadAsStreamAsync(ctx.Token).ConfigureAwait(false))
                            {
                                int capacity = contentLength.HasValue ? (int)contentLength.Value : 81920;
                                byte[] finalBuffer = new byte[capacity];
                                int totalBytes = 0;
                                int bytesRead;

                                while (true)
                                {
                                    if (totalBytes == finalBuffer.Length)
                                    {
                                        Array.Resize(ref finalBuffer, finalBuffer.Length * 2);
                                    }

                                    bytesRead = await stream.ReadAsync(finalBuffer.AsMemory(totalBytes), ctx.Token).ConfigureAwait(false);
                                    if (bytesRead == 0) break;

                                    totalBytes += bytesRead;
                                    if (totalBytes > 104857600) throw new InvalidOperationException("File exceeds maximum allowed size.");
                                }

                                // Trims the array bounds perfectly to size without allocating if it already perfectly matches
                                if (totalBytes != finalBuffer.Length)
                                {
                                    Array.Resize(ref finalBuffer, totalBytes);
                                }
                                fileData = finalBuffer;
                            }

                            if (fileData != null)
                            {
                                string computedHash = Convert.ToHexStringLower(SHA1.HashData(fileData));
                                if (computedHash == currentHash) success = true;
                                else { success = false; fileData = null; failReason = "Hash Mismatch"; }
                            }
                            else
                            {
                                success = false;
                                failReason = "Failed to read data";
                            }
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
                        
                        try { await Task.Delay(delayMs, ctx.Token).ConfigureAwait(false); } 
                        catch (OperationCanceledException) { break; }
                        finally { ctx.DecrementRetryingThreads(); }
                    }
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

        public static string GetDownloadUrl(string hash, string server)
        {
            string h = hash.ToLowerInvariant();
            if (h.Length < 4) return string.Empty;
            string srv = server.ToLowerInvariant();
            if (srv == "bonsai" || srv == "refresh") return $"https://lbp.lbpbonsai.com/api/v3/assets/{h}/download";
            if (srv == "archive") return $"https://archive.org/download/dry23r{h[0]}/dry{h.Substring(0, 2)}.zip/{h.Substring(0, 2)}%2F{h.Substring(2, 2)}%2F{h}";
            return $"https://lbparchive.zaprit.fish/{h.Substring(0, 2)}/{h.Substring(2, 2)}/{h}";
        }

        private class DownloadContext
        {
            public readonly HttpClient Client;
            public readonly CancellationToken Token;
            public readonly ChannelWriter<string> QueueWriter;
            public readonly ConcurrentDictionary<string, byte[]> Resources = new(StringComparer.OrdinalIgnoreCase);
            public readonly ExtractionConfig Config;

            private readonly IProgress<(int processed, int total, string message)>? _progress;
            private readonly ConcurrentDictionary<string, byte> _downloadedHashes = new(StringComparer.OrdinalIgnoreCase);

            private int _totalDiscovered;
            private int _totalProcessed;
            private int _retryingThreads;
            private int _pendingItems;

            private long _lastReportTime = 0;
            private readonly object _reportLock = new();

            public int PendingItems => Volatile.Read(ref _pendingItems);

            public DownloadContext(HttpClient client, CancellationToken token, IProgress<(int, int, string)>? progress, ChannelWriter<string> queueWriter, ExtractionConfig config)
            {
                Client = client;
                Token = token;
                _progress = progress;
                QueueWriter = queueWriter;
                Config = config;
            }

            public bool AddDiscoveredHash(string hash)
            {
                if (_downloadedHashes.TryAdd(hash, 0))
                {
                    Interlocked.Increment(ref _totalDiscovered);
                    return true;
                }
                return false;
            }

            public void AddResource(string hashStr, byte[] data)
            {
                Resources[hashStr] = data;
            }

            public void IncrementPending()
            {
                Interlocked.Increment(ref _pendingItems);
            }

            public void DecrementPending()
            {
                if (Interlocked.Decrement(ref _pendingItems) == 0)
                {
                    QueueWriter.TryComplete(); 
                }
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

                bool isLocal = Config.DownloadServer.ToLowerInvariant() == "local";
                
                string status = overrideMessage ?? (paused > 0 
                    ? $"Server Paused ({paused} thread(s) waiting)..." 
                    : (isLocal ? "Extracting local assets..." : "Downloading assets..."));

                _progress.Report((processed, discovered, status));
            }
        }
    }
}
