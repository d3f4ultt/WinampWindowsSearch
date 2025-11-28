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

    public class SearchConfig
    {
        public bool IncludeVideos { get; set; }
        public bool IncludeImages { get; set; }
        public bool IncludeOther { get; set; }
    }

    public class FileSearchService
    {
        private readonly DatabaseManager _dbManager = new DatabaseManager();
        
        private readonly string[] VideoExtensions = { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm" };
        private readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tiff" };

        public event Action<string, long>? OnLogMessage;

        private void Log(string message, long fileSize = 0)
        {
            OnLogMessage?.Invoke($"[{DateTime.Now:HH:mm:ss}] {message}", fileSize);
        }

        public async Task StartScanAndReport(SearchConfig config)
        {
            // 0. Load existing index for incremental scanning
            var existingFiles = GetAllFiles().ToDictionary(v => v.FilePath, v => v);
            
            var foundFiles = await Task.Run(() => IndexFiles(config, existingFiles));
            
            // 1. Clear old data and save new results
            Log("Updating database...");
            _dbManager.ClearAllFiles();
            _dbManager.InsertFiles(foundFiles);
            
            // 2. Identify and mark duplicates
            Log("Checking for duplicates...");
            MarkDuplicates();
            Log("Scan complete.");
        }

        private List<IndexedFile> IndexFiles(SearchConfig config, Dictionary<string, IndexedFile> existingIndex)
        {
            var foundFiles = new List<IndexedFile>();
            var searchPaths = GetSearchPaths(config);

            foreach (var path in searchPaths)
            {
                if (!Directory.Exists(path)) 
                {
                    Log($"Skipping missing directory: {path}");
                    continue;
                }

                Log($"Scanning directory: {path}");

                try
                {
                    // Use EnumerateFiles with *.* and filter manually for better control
                    foreach (var file in Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories))
                    {
                        string ext = Path.GetExtension(file).ToLowerInvariant();
                        FileType type;

                        if (config.IncludeVideos && VideoExtensions.Contains(ext)) type = FileType.Video;
                        else if (config.IncludeImages && ImageExtensions.Contains(ext)) type = FileType.Image;
                        else if (config.IncludeOther) type = FileType.Other;
                        else continue; // Skip if not matching any selected category

                        // Skip system files/folders if "Other" is selected to avoid clutter
                        if (type == FileType.Other && (file.Contains("\\Windows\\") || file.Contains("\\Program Files")))
                            continue;

                        var fileInfo = new FileInfo(file);
                        var lastWriteTime = fileInfo.LastWriteTime;
                        
                        // Incremental Check
                        if (existingIndex.TryGetValue(file, out var existing) && 
                            existing.FileSize == fileInfo.Length && 
                            existing.LastWriteTime == lastWriteTime)
                        {
                            // Cache Hit: Reuse expensive data
                            foundFiles.Add(new IndexedFile
                            {
                                FilePath = file,
                                FileSize = fileInfo.Length,
                                FileHash = existing.FileHash, // Reuse Hash
                                Duration = existing.Duration, // Reuse Duration
                                LastWriteTime = lastWriteTime,
                                LastScanned = DateTime.Now,
                                Type = type
                            });
                            continue; 
                        }

                        // Cache Miss: Process File
                        string fileHash = CalculateSha256Hash(file);
                        
                        // Extract Duration (Only for Videos)
                        TimeSpan duration = TimeSpan.Zero;
                        if (type == FileType.Video)
                        {
                            try
                            {
                                using (var tfile = TagLib.File.Create(file))
                                {
                                    duration = tfile.Properties.Duration;
                                }
                            }
                            catch { /* Ignore metadata errors */ }
                        }

                        Log($"Found: {file} ({type})", fileInfo.Length);

                        foundFiles.Add(new IndexedFile
                        {
                            FilePath = file,
                            FileSize = fileInfo.Length,
                            FileHash = fileHash,
                            Duration = duration,
                            LastWriteTime = lastWriteTime,
                            LastScanned = DateTime.Now,
                            Type = type
                        });
                    }
                }
                catch (UnauthorizedAccessException) { /* Handle permission errors quietly */ }
                catch (DirectoryNotFoundException) { /* Handle missing paths quietly */ }
                catch (Exception) { /* Handle other errors quietly */ }
            }
            return foundFiles;
        }

        private List<string> GetSearchPaths(SearchConfig config)
        {
            var paths = new HashSet<string>();

            if (config.IncludeVideos)
            {
                paths.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
            }
            if (config.IncludeImages)
            {
                paths.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
            }
            if (config.IncludeOther || config.IncludeVideos || config.IncludeImages)
            {
                paths.Add(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
                paths.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
                paths.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
            }

            return paths.ToList();
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
                    @"UPDATE IndexedFiles SET IsDuplicate = 1
                      WHERE FilePath NOT IN (
                          SELECT MIN(FilePath)
                          FROM IndexedFiles
                          GROUP BY FileHash
                          HAVING COUNT(FileHash) > 1
                      ) AND FileHash IN (
                          SELECT FileHash
                          FROM IndexedFiles
                          GROUP BY FileHash
                          HAVING COUNT(FileHash) > 1
                      );";
                cmd.ExecuteNonQuery();
            }
        }

        public List<IndexedFile> GetAllFiles()
        {
            var files = new List<IndexedFile>();
            using (var connection = new SqliteConnection(_dbManager.ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT ID, FilePath, FileSize, FileHash, DurationTicks, LastScanned, IsDuplicate, LastWriteTime, FileType FROM IndexedFiles";
                
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        files.Add(new IndexedFile
                        {
                            ID = reader.GetInt32(0),
                            FilePath = reader.GetString(1),
                            FileSize = reader.GetInt64(2),
                            FileHash = reader.GetString(3),
                            Duration = TimeSpan.FromTicks(reader.GetInt64(4)),
                            LastScanned = DateTime.Parse(reader.GetString(5)),
                            IsDuplicate = reader.GetBoolean(6),
                            LastWriteTime = reader.IsDBNull(7) ? DateTime.MinValue : DateTime.Parse(reader.GetString(7)),
                            Type = (FileType)reader.GetInt32(8)
                        });
                    }
                }
            }
            return files;
        }

        public (long TotalSize, long DuplicateSize, int TotalCount) GetTotalStorageReport()
        {
            using (var connection = new SqliteConnection(_dbManager.ConnectionString))
            {
                connection.Open();
                
                var totalCmd = connection.CreateCommand();
                totalCmd.CommandText = "SELECT SUM(FileSize), COUNT(ID) FROM IndexedFiles;";
                
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
                duplicateCmd.CommandText = "SELECT SUM(FileSize) FROM IndexedFiles WHERE IsDuplicate = 1;";
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
            var allFiles = GetAllFiles();
            var uniqueDrives = allFiles.Select(v => Path.GetPathRoot(v.FilePath))
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
                cmd.CommandText = "SELECT FilePath, FileSize FROM IndexedFiles;";
                
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
                if (dir.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), StringComparison.OrdinalIgnoreCase))
                    return "Pictures Folder";
                if (dir.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads", StringComparison.OrdinalIgnoreCase))
                    return "Downloads Folder";
                
                // Return the drive root or top folder if not in standard user folders
                return Path.GetPathRoot(dir) ?? "Unknown Drive"; 
            }
            catch { return "Uncategorized/Error"; }
        }
    }
}
