using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using VideoIndexer.Models;

namespace VideoIndexer.Services
{
    public class StorageMetrics
    {
        public long TotalDriveSpace { get; set; }
        public long FreeDriveSpace { get; set; }
        public long TotalVideoSize { get; set; }
        public long DuplicateVideoSize { get; set; }
        public long OtherUsedSpace => TotalDriveSpace - FreeDriveSpace - TotalVideoSize;
    }

    public class VideoSearchService
    {
        private readonly DatabaseManager _dbManager = new DatabaseManager();
        private readonly string[] VideoExtensions = { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm" };
        
        public event Action<string, long>? OnLogMessage;

        private void Log(string message, long fileSize = 0)
        {
            OnLogMessage?.Invoke($"[{DateTime.Now:HH:mm:ss}] {message}", fileSize);
        }

        public async Task StartScanAndReport(string[] searchPaths)
        {
            // 0. Load existing index for incremental scanning
            var existingVideos = GetAllVideos().ToDictionary(v => v.FilePath, v => v);
            
            var foundVideos = await Task.Run(() => IndexVideos(searchPaths, existingVideos));
            
            // 1. Clear old data and save new results
            Log("Updating database...");
            _dbManager.ClearAllVideos();
            _dbManager.InsertVideos(foundVideos);
            
            // 2. Identify and mark duplicates
            Log("Checking for duplicates...");
            MarkDuplicates();
            Log("Scan complete.");
        }

        private List<VideoFile> IndexVideos(string[] searchPaths, Dictionary<string, VideoFile> existingIndex)
        {
            var foundVideos = new List<VideoFile>();

            foreach (var path in searchPaths)
            {
                if (!Directory.Exists(path)) 
                {
                    Log($"Skipping missing directory: {path}");
                    continue;
                }

                Log($"Scanning directory: {path}");

                foreach (var extension in VideoExtensions)
                {
                    try
                    {
                        foreach (var file in Directory.EnumerateFiles(path, $"*{extension}", SearchOption.AllDirectories))
                        {
                            var fileInfo = new FileInfo(file);
                            var lastWriteTime = fileInfo.LastWriteTime;
                            
                            // Incremental Check
                            if (existingIndex.TryGetValue(file, out var existing) && 
                                existing.FileSize == fileInfo.Length && 
                                existing.LastWriteTime == lastWriteTime)
                            {
                                // Cache Hit: Reuse expensive data
                                foundVideos.Add(new VideoFile
                                {
                                    FilePath = file,
                                    FileSize = fileInfo.Length,
                                    FileHash = existing.FileHash, // Reuse Hash
                                    Duration = existing.Duration, // Reuse Duration
                                    LastWriteTime = lastWriteTime,
                                    LastScanned = DateTime.Now
                                });
                                continue; // Skip expensive processing
                            }

                            // Cache Miss: Process File
                            // Calculate the file hash for duplicate detection
                            string fileHash = CalculateSha256Hash(file);
                            
                            // Extract Duration
                            TimeSpan duration = TimeSpan.Zero;
                            try
                            {
                                using (var tfile = TagLib.File.Create(file))
                                {
                                    duration = tfile.Properties.Duration;
                                }
                            }
                            catch { /* Ignore metadata errors */ }

                            Log($"Found: {file} ({duration:hh\\:mm\\:ss})", fileInfo.Length);

                            foundVideos.Add(new VideoFile
                            {
                                FilePath = file,
                                FileSize = fileInfo.Length,
                                FileHash = fileHash,
                                Duration = duration,
                                LastWriteTime = lastWriteTime,
                                LastScanned = DateTime.Now
                            });
                        }
                    }
                    catch (UnauthorizedAccessException) { /* Handle permission errors quietly */ }
                    catch (DirectoryNotFoundException) { /* Handle missing paths quietly */ }
                    catch (Exception) { /* Handle other errors quietly */ }
                }
            }
            return foundVideos;
        }
        
        private string CalculateSha256Hash(string filePath)
        {
            try 
            {
                using (var sha256 = SHA256.Create())
                {
                    using (var stream = File.OpenRead(filePath))
                    {
                        byte[] hashBytes = sha256.ComputeHash(stream);
                        return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                    }
                }
            }
            catch 
            {
                return "HASH_ERROR";
            }
        }

        public void MarkDuplicates()
        {
            using (var connection = new SqliteConnection(_dbManager.ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText =
                    @"UPDATE Videos SET IsDuplicate = 1
                      WHERE FilePath NOT IN (
                          SELECT MIN(FilePath)
                          FROM Videos
                          GROUP BY FileHash
                          HAVING COUNT(FileHash) > 1
                      ) AND FileHash IN (
                          SELECT FileHash
                          FROM Videos
                          GROUP BY FileHash
                          HAVING COUNT(FileHash) > 1
                      );";
                cmd.ExecuteNonQuery();
            }
        }

        public List<VideoFile> GetAllVideos()
        {
            var videos = new List<VideoFile>();
            using (var connection = new SqliteConnection(_dbManager.ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT ID, FilePath, FileSize, FileHash, DurationTicks, LastScanned, IsDuplicate, LastWriteTime FROM Videos";
                
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        videos.Add(new VideoFile
                        {
                            ID = reader.GetInt32(0),
                            FilePath = reader.GetString(1),
                            FileSize = reader.GetInt64(2),
                            FileHash = reader.GetString(3),
                            Duration = TimeSpan.FromTicks(reader.GetInt64(4)),
                            LastScanned = DateTime.Parse(reader.GetString(5)),
                            IsDuplicate = reader.GetBoolean(6),
                            LastWriteTime = reader.IsDBNull(7) ? DateTime.MinValue : DateTime.Parse(reader.GetString(7))
                        });
                    }
                }
            }
            return videos;
        }

        public (long TotalSize, long DuplicateSize, int TotalCount) GetTotalStorageReport()
        {
            using (var connection = new SqliteConnection(_dbManager.ConnectionString))
            {
                connection.Open();
                
                var totalCmd = connection.CreateCommand();
                totalCmd.CommandText = "SELECT SUM(FileSize), COUNT(ID) FROM Videos;";
                
                long totalSize = 0;
                int totalCount = 0;

                using (var reader = totalCmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        totalSize = reader.IsDBNull(0) ? 0 : reader.GetInt64(0);
                        totalCount = reader.GetInt32(1);
                    }
                }
                
                var duplicateCmd = connection.CreateCommand();
                duplicateCmd.CommandText = "SELECT SUM(FileSize) FROM Videos WHERE IsDuplicate = 1;";
                var result = duplicateCmd.ExecuteScalar();
                long duplicateSize = (result == DBNull.Value || result == null) ? 0 : Convert.ToInt64(result);
                
                return (totalSize, duplicateSize, totalCount);
            }
        }
        
        public StorageMetrics GetStorageMetrics()
        {
            var metrics = new StorageMetrics();
            
            // 1. Calculate Video Metrics from DB
            var report = GetTotalStorageReport();
            metrics.TotalVideoSize = report.TotalSize;
            metrics.DuplicateVideoSize = report.DuplicateSize;

            // 2. Calculate Drive Metrics (approximate based on scanned paths)
            var allVideos = GetAllVideos();
            var uniqueDrives = allVideos.Select(v => Path.GetPathRoot(v.FilePath))
                                        .Where(r => r != null)
                                        .Distinct()
                                        .ToList();

            foreach (var drivePath in uniqueDrives)
            {
                try
                {
                    var driveInfo = new DriveInfo(drivePath);
                    if (driveInfo.IsReady)
                    {
                        metrics.TotalDriveSpace += driveInfo.TotalSize;
                        metrics.FreeDriveSpace += driveInfo.AvailableFreeSpace;
                    }
                }
                catch { /* Ignore drive access errors */ }
            }

            return metrics;
        }

        public class LocationReportItem
        {
            public string Location { get; set; } = string.Empty;
            public long Size { get; set; }
            public string DisplaySize => (Size / 1024.0 / 1024.0 / 1024.0).ToString("N2") + " GB";
        }

        public List<LocationReportItem> GetStorageBreakdownReport()
        {
            using (var connection = new SqliteConnection(_dbManager.ConnectionString))
            {
                connection.Open();
                var report = new List<LocationReportItem>();

                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT FilePath, FileSize FROM Videos;";
                
                using (var reader = cmd.ExecuteReader())
                {
                    var breakdown = new Dictionary<string, long>();
                    while (reader.Read())
                    {
                        string path = reader.GetString(0);
                        long size = reader.GetInt64(1);
                        
                        string rootFolder = GetTopLevelFolder(path);
                        
                        if (breakdown.ContainsKey(rootFolder))
                            breakdown[rootFolder] += size;
                        else
                            breakdown.Add(rootFolder, size);
                    }

                    foreach (var kvp in breakdown.OrderByDescending(x => x.Value))
                    {
                        report.Add(new LocationReportItem { Location = kvp.Key, Size = kvp.Value });
                    }
                }
                return report;
            }
        }

        private string GetTopLevelFolder(string path)
        {
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(dir)) return "Unknown";

                if (dir.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), StringComparison.OrdinalIgnoreCase))
                    return "Videos Folder";
                if (dir.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), StringComparison.OrdinalIgnoreCase))
                    return "Desktop Folder";
                if (dir.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), StringComparison.OrdinalIgnoreCase))
                    return "Documents Folder";
                if (dir.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads", StringComparison.OrdinalIgnoreCase))
                    return "Downloads Folder";
                
                // Return the drive root or top folder if not in standard user folders
                return Path.GetPathRoot(dir) ?? "Unknown Drive"; 
            }
            catch { return "Uncategorized/Error"; }
        }
    }
}
