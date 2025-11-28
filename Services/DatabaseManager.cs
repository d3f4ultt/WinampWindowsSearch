using Microsoft.Data.Sqlite;
using VideoIndexer.Models;
using System.Collections.Generic;
using System;

namespace VideoIndexer.Services
{
    public class DatabaseManager
    {
        private const string DbFile = "VideoIndex.db";
        private readonly string _connectionString = $"Data Source={DbFile}";

        public string ConnectionString => _connectionString;

        public DatabaseManager()
        {
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                // Create the table if it doesn't exist
                var createTableCmd = connection.CreateCommand();
                createTableCmd.CommandText =
                    @"CREATE TABLE IF NOT EXISTS Videos (
                        ID INTEGER PRIMARY KEY AUTOINCREMENT,
                        FilePath TEXT NOT NULL,
                        FileSize INTEGER NOT NULL,
                        FileHash TEXT NOT NULL,
                        DurationTicks INTEGER NOT NULL DEFAULT 0,
                        LastScanned DATETIME NOT NULL,
                        LastWriteTime DATETIME NOT NULL DEFAULT '1970-01-01',
                        IsDuplicate BOOLEAN NOT NULL DEFAULT 0
                    );
                    CREATE INDEX IF NOT EXISTS idx_hash ON Videos (FileHash);";
                createTableCmd.ExecuteNonQuery();
            }
        }

        public void InsertVideos(IEnumerable<VideoFile> videos)
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                // Use a transaction for fast bulk insertion
                using (var transaction = connection.BeginTransaction())
                {
                    var insertCmd = connection.CreateCommand();
                    insertCmd.Transaction = transaction;
                    insertCmd.CommandText = "INSERT INTO Videos (FilePath, FileSize, FileHash, DurationTicks, LastScanned, LastWriteTime) VALUES (@path, @size, @hash, @duration, @scanned, @writeTime)";

                    insertCmd.Parameters.Add("@path", SqliteType.Text);
                    insertCmd.Parameters.Add("@size", SqliteType.Integer);
                    insertCmd.Parameters.Add("@hash", SqliteType.Text);
                    insertCmd.Parameters.Add("@duration", SqliteType.Integer);
                    insertCmd.Parameters.Add("@scanned", SqliteType.Text);
                    insertCmd.Parameters.Add("@writeTime", SqliteType.Text);

                    foreach (var video in videos)
                    {
                        insertCmd.Parameters["@path"].Value = video.FilePath;
                        insertCmd.Parameters["@size"].Value = video.FileSize;
                        insertCmd.Parameters["@hash"].Value = video.FileHash;
                        insertCmd.Parameters["@duration"].Value = video.Duration.Ticks;
                        insertCmd.Parameters["@scanned"].Value = DateTime.Now.ToString("o");
                        insertCmd.Parameters["@writeTime"].Value = video.LastWriteTime.ToString("o");
                        insertCmd.ExecuteNonQuery();
                    }

                    transaction.Commit();
                }
            }
        }
        public void ClearAllVideos()
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "DELETE FROM Videos;";
                cmd.ExecuteNonQuery();
            }
        }
    }
}
