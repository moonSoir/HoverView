
# HoverView

**HoverView** is a lightweight utility for Windows 10/11 that shows file previews directly in Explorer when you hover over a file. No need to open a file to see what's inside — just hover your cursor over it.

![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-blue)
![.NET](https://img.shields.io/badge/.NET-8.0-purple)
![License](https://img.shields.io/badge/license-GPLv3-green)

[🇷🇺 Русская версия](README.md)

---

## ✨ Features

- **Images**: JPG, PNG, BMP, GIF (with animation support), WebP.
- **Video**: MP4, AVI, MKV, MOV, WMV (played without sound).
- **Audio**: MP3, WAV, FLAC, AAC, OGG, M4A (with volume control and bitrate display).
- **Text & Code**: TXT, LOG, JSON, XML, CSV, MD, INI, CONFIG, and source code (`.cs`, `.cpp`, `.py`, `.js`, `.html`, `.css`, etc.).
- **Archives**: ZIP, 7Z, RAR, TAR, GZ, BZ2, XZ — displayed as a tree of folders and files.
- **File Info**: the bottom panel shows resolution, size, duration, and bitrate (for audio), and the number of files in an archive.
- **Smart Positioning**: the window automatically fits within the monitor boundaries (multi-monitor and DPI aware).
- **Flexible Settings**:
  - Preview window size and content-fitting mode (with minimal frame or without).
  - Hover and hide delays (in milliseconds).
  - Supported formats configuration (images, video, audio, text, archives).
  - Maximum file size for preview.
  - Maximum archive entries and text lines (0 = unlimited).
  - Image cache size (by count and by weight in MB).
  - Audio volume control.
  - Auto-start with Windows.
  - Interface language selection (Russian / English).
  - Full-screen app behavior settings (for performance optimization).
- **Tray Icon** with quick access to settings and exit.
- **Window Hold** for text files and archives: hovering over the preview window keeps it visible, allowing you to scroll through text or the archive tree.

---

## 🚀 Installation & Usage

### Requirements
- Windows 10 or Windows 11 (x64).
- [.NET 8.0 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0).
- **File extensions must be visible in Explorer**, otherwise the program will not work!

### Build from Source
1. Clone the repository:
   ```bash
   git clone https://github.com/moonsoir/HoverView.git
   cd HoverView
   ```
2. Build the project:
   ```bash
   dotnet build -c Release
   ```
3. Run the executable from `bin\Release\net8.0-windows\`.

### Pre-built Binaries
You can find ready-to-use `.exe` files in the [Releases](https://github.com/moonsoir/HoverView/releases) section.

---

## 🛠 Usage

1. Launch `HoverView.exe`. The program will minimize to the system tray (near the clock).
2. Open Windows Explorer and hover over a file.
3. After the configured delay (300 ms by default), the preview window will appear.
4. To change settings, right-click the tray icon and select **"Settings..."**.

### Settings
All parameters are saved in `%AppData%\HoverView\settings.json` and applied automatically.

---

## 📄 License

This project is licensed under the **GNU General Public License v3.0 (GPLv3)**. 
See the [LICENSE](LICENSE) file for details.

---

If you'd like to contribute, please create an Issue or Pull Request.

---

## 💡 Acknowledgements

- [whistyun](https://github.com/whistyun) for the `AnimatedImage.Wpf` library.
- [adamhathcock](https://github.com/adamhathcock) for the `SharpCompress` library.
- [QtTabBar](https://github.com/indiff/qttabbar) — spiritual inspiration.
- [kyosora](https://github.com/kyosora) — the basis for the file-detection logic was taken from [HoverPeek](https://github.com/kyosora/HoverPeek).

---

TODO:
  - code syntax support
  - markdown support

---
