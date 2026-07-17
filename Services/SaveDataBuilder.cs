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
        public static void UpdateLevelInfo(string bkpDir, string newName, string newDesc, string? newIconPath = null)
        {
            var (head, branchId, branchRev, sltHash, hashes) = Far4Archive.ReadSaveArchive(bkpDir);

            string oldHashHex = Convert.ToHexStringLower(sltHash);
            if (!hashes.TryGetValue(oldHashHex, out byte[]? sltData)) throw new Exception("Could not find SLTb resource inside the archive.");

            byte[]? newIconHash = null; byte[]? newIconBytes = null;
            if (!string.IsNullOrEmpty(newIconPath))
            {
                newIconBytes = TextureDecoder.CreateIconFromImage(newIconPath);
                if (newIconBytes.Length == 0) throw new Exception("Failed to process the new icon image.");
                newIconHash = SHA1.HashData(newIconBytes);
            }

            sltData = SltbProcessor.DecompressSltData(sltData);
            var (newSltData, npHandle, gameVersion) = SltbProcessor.PatchSltb(sltData!, newName, newDesc, newIconHash);

            byte[] newSltHash = SHA1.HashData(newSltData);
            string newHashHex = Convert.ToHexStringLower(newSltHash);

            hashes.Remove(oldHashHex);
            hashes[newHashHex] = newSltData;

            if (newIconHash != null && newIconBytes != null)
            {
                hashes[Convert.ToHexStringLower(newIconHash)] = newIconBytes;
                File.WriteAllBytes(Path.Combine(bkpDir, "ICON0.PNG"), newIconBytes);
            }

            string tempPackDir = Path.Combine(bkpDir, "temp_repack");
            if (Directory.Exists(tempPackDir)) { try { Directory.Delete(tempPackDir, true); } catch (Exception ex) { LogManager.Log("SaveDataBuilder.UpdateLevelInfo.Cleanup1", ex); } }
            Directory.CreateDirectory(tempPackDir);

            try
            {
                Far4Archive.MakeSaveArchive(head, branchId, branchRev, newSltHash, hashes, tempPackDir);

                string bkpDirName = Path.GetFileName(bkpDir);
                byte[] sfo = Ps3SaveFormatter.MakeSfo(newName, bkpDirName, npHandle, newDesc, gameVersion);
                byte[] pfd = Ps3SaveFormatter.MakePfd((ulong)(gameVersion == 3 ? 4 : 3), sfo, bkpDir);

                int chunkIndex = 0;
                while (File.Exists(Path.Combine(bkpDir, chunkIndex.ToString()))) { File.Delete(Path.Combine(bkpDir, chunkIndex.ToString())); chunkIndex++; }

                int tempChunkIndex = 0;
                while (File.Exists(Path.Combine(tempPackDir, tempChunkIndex.ToString())))
                {
                    File.Move(Path.Combine(tempPackDir, tempChunkIndex.ToString()), Path.Combine(bkpDir, tempChunkIndex.ToString()), overwrite: true);
                    tempChunkIndex++;
                }

                File.WriteAllBytes(Path.Combine(bkpDir, "PARAM.SFO"), sfo);
                File.WriteAllBytes(Path.Combine(bkpDir, "PARAM.PFD"), pfd);
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

            if (ConfigManager.ForceLbp3Backups) { slotInfo.GameVersion = 3; head = LbpConstants.HEAD_LBP3_BASE; branchId = 0; branchRev = 0; }
            else if (slotInfo.GameVersion == 2 && (head & 0xFFFF) < LbpConstants.HEAD_LBP2_BETA_FALLBACK && ConfigManager.Lbp2BetaToRetail) { head = LbpConstants.HEAD_LBP2_BETA_FALLBACK; branchId = 0; branchRev = 0; }

            byte[] sltBytes = SltbProcessor.MakeSlotList(head, branchId, branchRev, slotInfo);
            byte[] sltHash = SHA1.HashData(sltBytes);

            resources[Convert.ToHexStringLower(sltHash)] = sltBytes;

            string bkpDirName = GetTitleId(slotInfo.GameVersion) + (slotInfo.IsAdventurePlanet ? "ADVLBP3AAZ" : "LEVEL") + lvl.Id.ToString("X8");
            string bkpPath = Path.Combine(backupDir, bkpDirName);
            Directory.CreateDirectory(bkpPath);

            await IconSaveHelper.SaveLevelIconAsync(lvl.IconHash, resources, bkpPath, client, token).ConfigureAwait(false);
            await Task.Run(() => Far4Archive.MakeSaveArchive(head, branchId, branchRev, sltHash, resources, bkpPath, token)).ConfigureAwait(false);

            byte[] sfo = Ps3SaveFormatter.MakeSfo(lvl.LevelName ?? "", bkpDirName, lvl.Creator ?? "", lvl.Description ?? "", slotInfo.GameVersion);
            await File.WriteAllBytesAsync(Path.Combine(bkpPath, "PARAM.SFO"), sfo, token).ConfigureAwait(false);

            byte[] pfd = Ps3SaveFormatter.MakePfd((ulong)(slotInfo.GameVersion == 3 ? 4 : 3), sfo, bkpPath);
            await File.WriteAllBytesAsync(Path.Combine(bkpPath, "PARAM.PFD"), pfd, token).ConfigureAwait(false);
        }

        private static string GetTitleId(int gameVersion)
        {
            string region = ConfigManager.GameRegion ?? "EU";
            if (region == "US") return gameVersion == 3 ? "BCUS98362" : (gameVersion == 2 ? "BCUS98245" : "BCUS98148");
            if (region == "JP") return gameVersion == 3 ? "BCJS30095" : (gameVersion == 2 ? "BCJS30058" : "BCJS30018");
            return gameVersion == 3 ? "BCES01663" : (gameVersion == 2 ? "BCES00850" : "BCES00141");
        }
    }
}