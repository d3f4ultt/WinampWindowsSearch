using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using VideoIndexer.Models;

namespace VideoIndexer.Services
{
    public class DatabaseManager
    {
        private const string DbFileName = "VideoIndex.db";
        public string ConnectionString => $"Data Source={DbFileName}";

        public DatabaseManager()
        {
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText =
                @"
                    CREATE TABLE IF NOT EXISTS IndexedFiles (
                        ID INTEGER PRIMARY KEY AUTOINCREMENT,
                        FilePath TEXT NOT NULL,
                        FileSize INTEGER NOT NULL,
                        FileHash TEXT NOT NULL,
                        DurationTicks INTEGER DEFAULT 0,
                        LastScanned TEXT NOT NULL,
                        IsDuplicate INTEGER DEFAULT 0,
                        LastWriteTime TEXT NOT NULL DEFAULT '1970-01-01',
                        FileType INTEGER DEFAULT 0
                    );
                ";
                command.ExecuteNonQuery();
            }
        }

        public void InsertFiles(List<IndexedFile> files)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    var command = connection.CreateCommand();
                    command.CommandText =
                    @"
                        INSERT INTO IndexedFiles (FilePath, FileSize, FileHash, DurationTicks, LastScanned, IsDuplicate, LastWriteTime, FileType)
                        VALUES ($path, $size, $hash, $duration, $scanned, $isDup, $lwt, $type);
                    ";

                    var pPath = command.CreateParameter(); pPath.ParameterName = "$path"; command.Parameters.Add(pPath);
                    var pSize = command.CreateParameter(); pSize.ParameterName = "$size"; command.Parameters.Add(pSize);
                    var pHash = command.CreateParameter(); pHash.ParameterName = "$hash"; command.Parameters.Add(pHash);
                    var pDur = command.CreateParameter(); pDur.ParameterName = "$duration"; command.Parameters.Add(pDur);
                    var pScan = command.CreateParameter(); pScan.ParameterName = "$scanned"; command.Parameters.Add(pScan);
                    var pDup = command.CreateParameter(); pDup.ParameterName = "$isDup"; command.Parameters.Add(pDup);
                    var pLwt = command.CreateParameter(); pLwt.ParameterName = "$lwt"; command.Parameters.Add(pLwt);
                    var pType = command.CreateParameter(); pType.ParameterName = "$type"; command.Parameters.Add(pType);

                    foreach (var file in files)
                    {
                        pPath.Value = file.FilePath;
                        pSize.Value = file.FileSize;
                        pHash.Value = file.FileHash;
                        pDur.Value = file.Duration.Ticks;
                        pScan.Value = file.LastScanned.ToString("o");
                        pDup.Value = file.IsDuplicate ? 1 : 0;
                        pLwt.Value = file.LastWriteTime.ToString("o");
                        pType.Value = (int)file.Type;
                        command.ExecuteNonQuery();
                    }

                    transaction.Commit();
                }
            }
        }
        
        public void ClearAllFiles()
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "DELETE FROM IndexedFiles"; // Or DROP TABLE if you want a full reset
                cmd.ExecuteNonQuery();
            }
        }
    }
}
