using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VideoIndexer.Models
{
    // ObservableObject is used for potential UI binding
    public class VideoFile : ObservableObject 
    {
        public int ID { get; set; } // Auto-incremented Primary Key
        public string FilePath { get; set; } = string.Empty;
        public long FileSize { get; set; } // Size in bytes
        public string FileHash { get; set; } = string.Empty; // SHA256 hash
        public DateTime LastScanned { get; set; }
        public DateTime LastWriteTime { get; set; }
        public TimeSpan Duration { get; set; }
        
        // This property is used in the UI for filtering and reporting
        private bool _isDuplicate;
        public bool IsDuplicate
        {
            get => _isDuplicate;
            set => SetProperty(ref _isDuplicate, value);
        }

        public string DisplaySize
        {
            get
            {
                double sizeInGB = FileSize / 1024.0 / 1024.0 / 1024.0;
                if (sizeInGB >= 1.0)
                {
                    return sizeInGB.ToString("N2") + " GB";
                }
                return (FileSize / 1024.0 / 1024.0).ToString("N2") + " MB";
            }
        }
        public string DisplayDuration => Duration.ToString(@"hh\:mm\:ss");
    }
}
