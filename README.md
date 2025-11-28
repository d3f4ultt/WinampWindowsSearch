# Winamp Windows Search ⚡🦙

**The Ultimate Retro Search Tool for Windows**

Winamp Windows Search is a blazing fast, local file indexer that brings back the soul of 90s computing. It combines modern performance with the nostalgic aesthetic of the legendary Winamp media player.

## 🔮 The Vision
We are building the **best Windows search program** ever made.
- **Current State**: A powerful video indexer with instant incremental scanning, duplicate detection, and storage visualization.
- **Future Plans**: We will slowly integrate more file types, advanced metadata search, and deep shell integration, all while keeping the **Winamp spirit** alive. Expect more skins, more visualizations, and yes, more hacker music. 🎹

## 🚀 Installation

### Option 1: Download the Executable
1.  Go to the **Releases** section.
2.  Download `WinampWindowsSearch.exe`.
3.  Run it. No installation required.

### Option 2: Build from Source
If you want to hack on the code or build it yourself:

1.  **Prerequisites**:
    - Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

2.  **Clone the Repository**:
    ```bash
    git clone https://github.com/yourusername/WinampWindowsSearch.git
    cd WinampWindowsSearch
    ```

3.  **Restore Dependencies**:
    The project uses the following NuGet packages:
    - `Microsoft.Data.Sqlite` (Database)
    - `CommunityToolkit.Mvvm` (MVVM Pattern)
    - `TagLibSharp` (Metadata Extraction)
    
    Run this command to install them automatically:
    ```bash
    dotnet restore
    ```

4.  **Build & Run**:
    ```bash
    dotnet run
    ```

5.  **Compile for Release (Single File EXE)**:
    ```bash
    dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o dist
    ```

## 📂 What's in the Code?
- **`WinampWindowsSearch.csproj`**: The project configuration.
- **`MainWindow.xaml`**: The WPF User Interface.
- **`Services/VideoSearchService.cs`**: The core scanning and indexing engine.
- **`Services/DatabaseManager.cs`**: SQLite database handler.
- **`swordfish.mp3`**: The embedded anthem.

---
*It really whips the llama's ass.* 🦙
