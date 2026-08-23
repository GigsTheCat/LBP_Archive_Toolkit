using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using LbpArchiveToolkit.Configuration;
using LbpArchiveToolkit.Models;
using LbpArchiveToolkit.Utils;

namespace LbpArchiveToolkit.Services
{
    public static class SaveDataBuilder
    {
        public static void UpdateLevelInfo(string bkpDir, string newName, string newDesc, string? newIconPath, bool isLocked, bool isSubLevel, bool isShareable, ViewModels.IViewService viewService)
        {
            var (head, branchId, branchRev, sltHash, hashes) = Far4Archive.ReadSaveArchive(bkpDir);

            string oldHashHex = Convert.ToHexStringLower(sltHash);
            if (!hashes.TryGetValue(oldHashHex, out byte[]? sltData)) throw new Exception("Could not find SLTb resource inside the archive.");

            byte[]? newIconHash = null; byte[]? newIconBytes = null;
            if (!string.IsNullOrEmpty(newIconPath))
            {
                newIconBytes = viewService.CreateIconFromImage(newIconPath);
                if (newIconBytes.Length == 0) throw new Exception("Failed to process the new icon image.");
                newIconHash = SHA1.HashData(newIconBytes);
            }

            sltData = SltbProcessor.DecompressSltData(sltData);
            var (newSltData, npHandle, gameVersion) = SltbProcessor.PatchSltb(sltData!, newName, newDesc, newIconHash, isLocked, isSubLevel, isShareable);

            byte[] newSltHash = SHA1.HashData(newSltData);
            string newHashHex = Convert.ToHexStringLower(newSltHash);

            hashes.Remove(oldHashHex);
            hashes[newHashHex] = newSltData;

            bool isPs4 = File.Exists(Path.Combine(bkpDir, "L0")) || File.Exists(Path.Combine(bkpDir, "sce_sys", "param.sfo"));

            if (newIconHash != null && newIconBytes != null)
            {
                hashes[Convert.ToHexStringLower(newIconHash)] = newIconBytes;
                if (isPs4)
                {
                    string sceSysPath = Path.Combine(bkpDir, "sce_sys");
                    Directory.CreateDirectory(sceSysPath);
                    File.WriteAllBytes(Path.Combine(sceSysPath, "icon0.png"), newIconBytes);
                }
                else
                {
                    File.WriteAllBytes(Path.Combine(bkpDir, "ICON0.PNG"), newIconBytes);
                }
            }

            string tempPackDir = Path.Combine(bkpDir, "temp_repack");
            if (Directory.Exists(tempPackDir)) { try { Directory.Delete(tempPackDir, true); } catch (Exception ex) { LogManager.Log("SaveDataBuilder.UpdateLevelInfo.Cleanup1", ex); } }
            Directory.CreateDirectory(tempPackDir);

            try
            {
                Far4Archive.MakeSaveArchive(head, branchId, branchRev, newSltHash, hashes, tempPackDir, isPs4);

                string bkpDirName = Path.GetFileName(bkpDir);

                if (isPs4)
                {
                    string sceSysPath = Path.Combine(bkpDir, "sce_sys");
                    Directory.CreateDirectory(sceSysPath);
                    byte[] sfo = Ps4SaveFormatter.MakeSfo(newName, bkpDirName, npHandle, newDesc, GetTitleIdPs4(), Ps4SaveDataBlocks);
                    File.WriteAllBytes(Path.Combine(sceSysPath, "param.sfo"), sfo);

                    int chunkIndex = 0;
                    while (File.Exists(Path.Combine(bkpDir, $"L{chunkIndex}"))) { File.Delete(Path.Combine(bkpDir, $"L{chunkIndex}")); chunkIndex++; }
                    
                    int oldChunkIndex = 0;
                    while (File.Exists(Path.Combine(bkpDir, oldChunkIndex.ToString()))) { File.Delete(Path.Combine(bkpDir, oldChunkIndex.ToString())); oldChunkIndex++; }

                    int tempChunkIndex = 0;
                    while (File.Exists(Path.Combine(tempPackDir, $"L{tempChunkIndex}")))
                    {
                        File.Move(Path.Combine(tempPackDir, $"L{tempChunkIndex}"), Path.Combine(bkpDir, $"L{tempChunkIndex}"), overwrite: true);
                        tempChunkIndex++;
                    }
                }
                else
                {
                    byte[] sfo = Ps3SaveFormatter.MakeSfo(newName, bkpDirName, npHandle, newDesc, gameVersion);
                    byte[] pfd = Ps3SaveFormatter.MakePfd((ulong)(gameVersion == 3 ? 4 : 3), sfo, bkpDir);

                    int chunkIndex = 0;
                    while (File.Exists(Path.Combine(bkpDir, chunkIndex.ToString()))) { File.Delete(Path.Combine(bkpDir, chunkIndex.ToString())); chunkIndex++; }

                    int oldChunkIndex = 0;
                    while (File.Exists(Path.Combine(bkpDir, $"L{oldChunkIndex}"))) { File.Delete(Path.Combine(bkpDir, $"L{oldChunkIndex}")); oldChunkIndex++; }

                    int tempChunkIndex = 0;
                    while (File.Exists(Path.Combine(tempPackDir, tempChunkIndex.ToString())))
                    {
                        File.Move(Path.Combine(tempPackDir, tempChunkIndex.ToString()), Path.Combine(bkpDir, tempChunkIndex.ToString()), overwrite: true);
                        tempChunkIndex++;
                    }

                    File.WriteAllBytes(Path.Combine(bkpDir, "PARAM.SFO"), sfo);
                    File.WriteAllBytes(Path.Combine(bkpDir, "PARAM.PFD"), pfd);
                }
            }
            finally
            {
                try { if (Directory.Exists(tempPackDir)) Directory.Delete(tempPackDir, true); } catch (Exception ex) { LogManager.Log("SaveDataBuilder.UpdateLevelInfo.Cleanup2", ex); }
            }
        }

        public static async Task BuildAndWriteSaveDataAsync(LevelItem lvl, SlotInfo slotInfo, SortedDictionary<string, byte[]> resources, string backupDir, HttpClient client, CancellationToken token)
        {
            string rootHashStr = lvl.Hash!.ToLowerInvariant();
            bool isRootGuid = rootHashStr.Length <= 8;
            uint head = 0; ushort branchId = 0; ushort branchRev = 0;

            if (!isRootGuid && resources.ContainsKey(rootHashStr))
            {
                SltbProcessor.ParseResrcRevision(resources[rootHashStr], out head, out branchId, out branchRev);
                
                // Auto-detect the true game version based on the root level's revision
                uint version = head & 0xFFFF;
                uint subversion = (head >> 16) & 0xFFFF;
                int actualGameVersion = subversion != 0 ? 3 : (version >= LbpConstants.REV_ARCADE ? 2 : 1);
                
                // If the file itself says it belongs to a newer game than the DB thought, upgrade the slot version
                if (actualGameVersion > slotInfo.GameVersion)
                {
                    slotInfo.GameVersion = actualGameVersion;
                }
            }
            else head = (uint)(slotInfo.GameVersion == 3 ? LbpConstants.HEAD_LBP3_BASE : (slotInfo.GameVersion == 2 ? LbpConstants.HEAD_LBP2_BETA_FALLBACK : LbpConstants.REV_LBP1_MAX));

            if (ConfigManager.ForceLbp3Ps4Backups) { slotInfo.GameVersion = 3; head = LbpConstants.HEAD_LBP3_PS4_BASE; branchId = 0; branchRev = 0; }
            else if (ConfigManager.ForceLbp3Backups) { slotInfo.GameVersion = 3; head = LbpConstants.HEAD_LBP3_BASE; branchId = 0; branchRev = 0; }
            else if (slotInfo.GameVersion == 2 && (head & 0xFFFF) < LbpConstants.HEAD_LBP2_BETA_FALLBACK && ConfigManager.Lbp2BetaToRetail) { head = LbpConstants.HEAD_LBP2_BETA_FALLBACK; branchId = 0; branchRev = 0; }

            byte[] sltBytes = SltbProcessor.MakeSlotList(head, branchId, branchRev, slotInfo);
            byte[] sltHash = SHA1.HashData(sltBytes);

            resources[Convert.ToHexStringLower(sltHash)] = sltBytes;

                        bool ps4 = ConfigManager.ForceLbp3Ps4Backups;
            string bkpDirName = (ps4 ? GetTitleIdPs4() + "x00" : GetTitleId(slotInfo.GameVersion)) + (slotInfo.IsAdventurePlanet ? "ADVLBP3AAZ" : "LEVEL") + lvl.Id.ToString("X8");
            string bkpPath = Path.Combine(backupDir, bkpDirName);
            Directory.CreateDirectory(bkpPath);

            if (ps4)
            {
                // shadPS4 expects metadata under sce_sys/ and doesn't check any save signature,
                // so there's no PARAM.PFD equivalent to write here.
                string sceSysPath = Path.Combine(bkpPath, "sce_sys");
                Directory.CreateDirectory(sceSysPath);

                await IconSaveHelper.SaveLevelIconAsync(lvl.IconHash, resources, sceSysPath, client, token, "icon0.png").ConfigureAwait(false);
                await Task.Run(() => Far4Archive.MakeSaveArchive(head, branchId, branchRev, sltHash, resources, bkpPath, true, token)).ConfigureAwait(false);

                string titleId = GetTitleIdPs4();
                byte[] sfo = Ps4SaveFormatter.MakeSfo(lvl.LevelName ?? "", bkpDirName, lvl.Creator ?? "", lvl.Description ?? "", titleId, Ps4SaveDataBlocks);
                await File.WriteAllBytesAsync(Path.Combine(sceSysPath, "param.sfo"), sfo, token).ConfigureAwait(false);
            }
            else
            {
                await IconSaveHelper.SaveLevelIconAsync(lvl.IconHash, resources, bkpPath, client, token).ConfigureAwait(false);
                await Task.Run(() => Far4Archive.MakeSaveArchive(head, branchId, branchRev, sltHash, resources, bkpPath, false, token)).ConfigureAwait(false);

                byte[] sfo = Ps3SaveFormatter.MakeSfo(lvl.LevelName ?? "", bkpDirName, lvl.Creator ?? "", lvl.Description ?? "", slotInfo.GameVersion);
                await File.WriteAllBytesAsync(Path.Combine(bkpPath, "PARAM.SFO"), sfo, token).ConfigureAwait(false);

                byte[] pfd = Ps3SaveFormatter.MakePfd((ulong)(slotInfo.GameVersion == 3 ? 4 : 3), sfo, bkpPath);
                await File.WriteAllBytesAsync(Path.Combine(bkpPath, "PARAM.PFD"), pfd, token).ConfigureAwait(false);
            }
        }

        // 32768-byte blocks; ~16 MiB comfortably covers the ~15.7 MB level backups LBP3 uses on
        // real PS4 hardware. shadPS4 doesn't appear to enforce this against actual data size.
        private const ulong Ps4SaveDataBlocks = 512;

        private static string GetTitleId(int gameVersion)
        {
            string region = ConfigManager.GameRegion ?? "EU";
            if (region == "US") return gameVersion == 3 ? "BCUS98362" : (gameVersion == 2 ? "BCUS98245" : "BCUS98148");
            if (region == "JP") return gameVersion == 3 ? "BCJS30095" : (gameVersion == 2 ? "BCJS30058" : "BCJS30018");
            return gameVersion == 3 ? "BCES01663" : (gameVersion == 2 ? "BCES00850" : "BCES00141");
        }

        // EU/US confirmed against public PS4 serial databases; JP/Asia (CUSA00693) is an
        // educated guess (listed for LBP3 with no region tag) — verify before trusting it.
        private static string GetTitleIdPs4()
        {
            string region = ConfigManager.GameRegion ?? "EU";
            if (region == "US") return "CUSA00473";
            if (region == "JP") return "CUSA00693";
            return "CUSA00063";
        }
    }
}