# 🎵 Modern Music Player

A sleek, high-performance desktop music player built with **WPF** and **.NET 8**, featuring rich UI animations, real-time DSP audio processing, intelligent library caching, and dynamic wallpaper theming.

---

## ✨ Features

- **🎧 High-Performance Audio Engine**:
  - Powered by **NAudio** and **SoundTouch.Net**.
  - Real-time 10-band Equalizer, Pitch Shifting, and Tempo manipulation without distortion.
  - ASIO / WASAPI low-latency output support.

- **⚡ Ultra-Smooth Ergonomic UI**:
  - Custom critically-damped spring physics for mouse wheel scrolling (60 FPS smooth scrolling).
  - Virtualized UI rendering for artists and albums with hardware-accelerated image caching.
  - Modern dark-mode interface with glassmorphism and subtle micro-animations.

- **📚 Fast Music Library & Metadata**:
  - High-speed tag parsing with **TagLibSharp**.
  - Local **SQLite** database storage for instant search, playlists, favorite tracks, and persistent statistics.

- **🎨 Dynamic Theming & Wallpaper Engine**:
  - Live background wallpapers with customizable blur, dimming opacity, and category filtering.
  - Custom accent colors and dynamic theme switching.

---

## 🛠️ Tech Stack & Dependencies

- **Framework**: [.NET 8 (Windows 10/11)](https://dotnet.microsoft.com/)
- **UI Framework**: Windows Presentation Foundation (WPF) with XAML
- **MVVM Architecture**: `CommunityToolkit.Mvvm`
- **Audio Processing**: `NAudio` (2.2.1) & `SoundTouch.Net` (2.3.2)
- **Database**: `sqlite-net-pcl` & `SQLitePCLRaw`
- **Metadata**: `TagLibSharp`

---

## 🚀 Getting Started

### Prerequisites
- Windows 10 (version 19041+) or Windows 11
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or Runtime

### Building from Source
Clone the repository and build:
```bash
git clone https://github.com/Soulaiman08/MusicPlayer.git
cd MusicPlayer
dotnet build
```

Or run directly via PowerShell:
```powershell
.\run.ps1
```

### Standalone Executable
You can download the self-contained single-file executable (`MusicPlayerFinal.exe`) directly from the [Releases](https://github.com/Soulaiman08/MusicPlayer/releases) tab. No .NET runtime installation required.

---

## 📄 License
This project is open-source and available under the [MIT License](LICENSE).
