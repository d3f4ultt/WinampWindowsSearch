using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using VideoIndexer.Services;
using VideoIndexer.Models;

namespace search
{
    public partial class MainWindow : Window
    {
        private readonly VideoSearchService _searchService = new VideoSearchService();
        private System.Windows.Threading.DispatcherTimer _vizTimer;
        private Random _rng = new Random();
        
        // Define the common search paths
        private readonly string[] _defaultPaths = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads"
        };

        public MainWindow()
        {
            InitializeComponent();
            
            try 
            {
                Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("app.png", UriKind.Relative));
            }
            catch { /* Ignore icon error */ }

            _searchService.OnLogMessage += OnServiceLogMessage;
            
            // Initialize Player with Embedded Resource
            string tempMusicPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "VideoSearchApp_swordfish.mp3");
            
            if (!System.IO.File.Exists(tempMusicPath))
            {
                try
                {
                    var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                    // Resource name is usually Namespace.Filename
                    using (var stream = assembly.GetManifestResourceStream("search.swordfish.mp3"))
                    {
                        if (stream != null)
                        {
                            using (var fileStream = System.IO.File.Create(tempMusicPath))
                            {
                                stream.CopyTo(fileStream);
                            }
                        }
                    }
                }
                catch { /* Handle extraction error silently */ }
            }

            if (System.IO.File.Exists(tempMusicPath))
            {
                WinampPlayer.Source = new Uri(tempMusicPath);
                WinampPlayer.Volume = 0.5;
            }
            
            InitializeVisualization();
        }

        private void InitializeVisualization()
        {
            _vizTimer = new System.Windows.Threading.DispatcherTimer();
            _vizTimer.Interval = TimeSpan.FromMilliseconds(50);
            _vizTimer.Tick += VizTimer_Tick;

            // Create 20 bars
            for (int i = 0; i < 20; i++)
            {
                var rect = new System.Windows.Shapes.Rectangle
                {
                    Fill = System.Windows.Media.Brushes.LimeGreen,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(1, 0, 1, 0)
                };
                SpectrumGrid.Children.Add(rect);
            }
        }

        private void VizTimer_Tick(object? sender, EventArgs e)
        {
            foreach (System.Windows.Shapes.Rectangle rect in SpectrumGrid.Children)
            {
                // Random height between 2 and 28
                rect.Height = _rng.Next(2, 28);
            }
        }

        private void OnServiceLogMessage(string message, long fileSize)
        {
            Dispatcher.Invoke(() =>
            {
                var paragraph = new System.Windows.Documents.Paragraph();
                var run = new System.Windows.Documents.Run(message);

                // Color Coding Logic
                if (fileSize == 0)
                {
                    run.Foreground = System.Windows.Media.Brushes.White; // System message
                }
                else if (fileSize < 524288000) // < 500 MB
                {
                    run.Foreground = System.Windows.Media.Brushes.LightGreen;
                }
                else if (fileSize < 2147483648) // < 2 GB
                {
                    run.Foreground = System.Windows.Media.Brushes.Yellow;
                }
                else // > 2 GB
                {
                    run.Foreground = System.Windows.Media.Brushes.Red;
                }

                paragraph.Inlines.Add(run);
                LogRichTextBox.Document.Blocks.Add(paragraph);
                LogRichTextBox.ScrollToEnd();
            });
        }

        private async void StartScan_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 1. UI Setup (Disable button, show progress)
                StartButton.IsEnabled = false;
                ScanProgressBar.Visibility = Visibility.Visible;
                StatusText.Text = "Scanning and Indexing in progress...";
                
                // Show Winamp, Hide Reports
                WinampContainer.Visibility = Visibility.Visible;
                ReportContainer.Visibility = Visibility.Collapsed;

                // 2. Core Logic Execution
                PlayMusic(); // Auto-start music
                await _searchService.StartScanAndReport(_defaultPaths);

                // 3. UI Update (Retrieve and display results from the DB)
                UpdateUIWithResults();

                StatusText.Text = "Scan Complete!";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during scan: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Scan Failed.";
            }
            finally
            {
                // 4. Final UI Cleanup
                StartButton.IsEnabled = true;
                ScanProgressBar.Visibility = Visibility.Collapsed;
                
                // Hide Winamp, Show Reports
                WinampContainer.Visibility = Visibility.Collapsed;
                ReportContainer.Visibility = Visibility.Visible;
                StopMusic();
            }
        }
        
        private void UpdateUIWithResults()
        {
            // Populate DataGrids
            var allVideos = _searchService.GetAllVideos();
            VideoDataGrid.ItemsSource = allVideos;
            DuplicateDataGrid.ItemsSource = allVideos.Where(v => v.IsDuplicate).ToList();
            
            // Populate Reports
            var totalReport = _searchService.GetTotalStorageReport();
            ReportSummaryTextBlock.Text = $"Total Videos Indexed: {totalReport.TotalCount}. " +
                                           $"Total Size: {(totalReport.TotalSize / 1073741824.0).ToString("N2")} GB. " +
                                           $"Savings potential: {(totalReport.DuplicateSize / 1073741824.0).ToString("N2")} GB (from duplicates).";

            LocationDataGrid.ItemsSource = _searchService.GetStorageBreakdownReport();
            
            // Draw Pie Chart
            var metrics = _searchService.GetStorageMetrics();
            DrawPieChart(metrics);
        }

        private void DrawPieChart(StorageMetrics metrics)
        {
            StoragePieChartCanvas.Children.Clear();
            double total = metrics.TotalDriveSpace;
            if (total <= 0) return;

            double cx = 100, cy = 100, radius = 90;
            double startAngle = 0;

            // Define Slices: Value, Color
            var slices = new[]
            {
                (Value: (double)metrics.FreeDriveSpace, Color: System.Windows.Media.Brushes.LightGray),
                (Value: (double)metrics.OtherUsedSpace, Color: System.Windows.Media.Brushes.DimGray), // Darker Gray
                (Value: (double)metrics.TotalVideoSize, Color: System.Windows.Media.Brushes.DodgerBlue),
                (Value: (double)metrics.DuplicateVideoSize, Color: System.Windows.Media.Brushes.Red)
            };

            foreach (var slice in slices)
            {
                if (slice.Value <= 0) continue;

                double sweepAngle = (slice.Value / total) * 360;
                if (sweepAngle >= 360) sweepAngle = 359.9; // Avoid full circle issues

                double endAngle = startAngle + sweepAngle;

                // Calculate Points
                Point startPoint = new Point(
                    cx + radius * Math.Cos(startAngle * Math.PI / 180),
                    cy + radius * Math.Sin(startAngle * Math.PI / 180));

                Point endPoint = new Point(
                    cx + radius * Math.Cos(endAngle * Math.PI / 180),
                    cy + radius * Math.Sin(endAngle * Math.PI / 180));

                // Create Arc
                var pathFigure = new System.Windows.Media.PathFigure
                {
                    StartPoint = new Point(cx, cy),
                    IsClosed = true
                };
                
                pathFigure.Segments.Add(new System.Windows.Media.LineSegment(startPoint, false));
                pathFigure.Segments.Add(new System.Windows.Media.ArcSegment(
                    endPoint,
                    new Size(radius, radius),
                    0,
                    sweepAngle > 180,
                    System.Windows.Media.SweepDirection.Clockwise,
                    false));

                var pathGeometry = new System.Windows.Media.PathGeometry();
                pathGeometry.Figures.Add(pathFigure);

                var path = new System.Windows.Shapes.Path
                {
                    Fill = slice.Color,
                    Data = pathGeometry,
                    ToolTip = $"{slice.Value / 1024.0 / 1024.0 / 1024.0:N2} GB"
                };

                StoragePieChartCanvas.Children.Add(path);
                startAngle = endAngle;
            }
        }

        // --- Winamp Player Logic ---

        private void PlayMusic()
        {
            if (WinampPlayer.Source != null)
            {
                WinampPlayer.Play();
                _vizTimer.Start();
                PlayerStatusText.Text = "[PLAYING] 00:04"; // Static time for retro feel
            }
            else
            {
                PlayerStatusText.Text = "[NO FILE] swordfish.mp3";
            }
        }

        private void PlayMusic_Click(object sender, RoutedEventArgs e) => PlayMusic();

        private void PauseMusic_Click(object sender, RoutedEventArgs e)
        {
            WinampPlayer.Pause();
            _vizTimer.Stop();
            PlayerStatusText.Text = "[PAUSED]";
        }

        private void StopMusic()
        {
            WinampPlayer.Stop();
            _vizTimer.Stop();
            foreach (System.Windows.Shapes.Rectangle rect in SpectrumGrid.Children) rect.Height = 2;
            PlayerStatusText.Text = "[STOPPED]";
        }

        private void StopMusic_Click(object sender, RoutedEventArgs e) => StopMusic();

        private void WinampPlayer_MediaEnded(object sender, RoutedEventArgs e)
        {
            // Loop the music
            WinampPlayer.Position = TimeSpan.Zero;
            WinampPlayer.Play();
        }
    }
}