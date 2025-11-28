using System;

namespace VideoIndexer.Models
{
    public enum FileType
    {
        Video,
        Image,
        Other
    }

    public class IndexedFile
    {
        public int ID { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string FileHash { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; }
        public DateTime LastScanned { get; set; }
        public bool IsDuplicate { get; set; }
        public DateTime LastWriteTime { get; set; }
        public FileType Type { get; set; }

        public string DisplaySize
        {
            get
            {
                if (FileSize >= 1073741824) // 1 GB
                {
                    return (FileSize / 1073741824.0).ToString("N2") + " GB";
                }
                return (FileSize / 1048576.0).ToString("N2") + " MB";
            }
        }

        public string DisplayDuration => Duration == TimeSpan.Zero ? "" : Duration.ToString(@"hh\:mm\:ss");
    }
}
