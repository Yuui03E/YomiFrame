<div align="center">
  <h1>📖 YomiFrame</h1>
  <p><strong>A blazing fast, minimalist, GPU-accelerated Manga Reader for Windows.</strong></p>

  [![Platform](https://img.shields.io/badge/Platform-Windows-blue.svg)](#)
  [![Framework](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](#)
  [![Renderer](https://img.shields.io/badge/Renderer-SkiaSharp-orange.svg)](#)
</div>

<br />

YomiFrame is a custom-built manga and comic reader designed for people who want a completely distraction-free, lightning-fast reading experience. No bloated menus, no library managers running in the background—just drop your manga into the frameless window and start reading instantly.

## ✨ Features

- 🚀 **Blazing Fast GPU Rendering:** Powered by `SkiaSharp`, YomiFrame chews through 4K manga pages effortlessly with minimal CPU usage.
- 📚 **Massive Multi-Archive Stitching:** Drag and drop 20 `.zip` or `.rar` volumes into the app at once. YomiFrame instantly stitches them together in memory, allowing you to seamlessly read from Volume 1 to Volume 20 without a single interruption.
- 🎨 **Minimalist Frameless UI:** No title bars, no permanent menus. Just the manga and a sleek right-click context menu.
- 📖 **Advanced Reading Modes:**
  - **Single Page:** Standard fit-to-screen reading.
  - **Double Page:** Automatically pairs pages. Features a realistic **Book Spine Shadow** and a **Page Split** gap to perfectly simulate reading a physical volume.
  - **Webtoon Mode:** Stitches pages vertically for endless, smooth scrolling.
- 🖌️ **Auto-Colors Enhancement:** Automatically boosts the contrast of older or faded scans to give you deep, inky blacks and crisp whites.
- ⌨️ **Ultimate Customization:** Every single feature (Navigation, Zoom, Fit Modes, Filters) can be mapped to custom keyboard shortcuts in a beautifully compact configuration menu.
- 📦 **Fully Portable:** No installation required. Runs entirely out of a single, self-contained `.exe` file.
- 📁 **Universal Format Support:** Reads ZIP, CBZ, RAR, CBR, 7Z, CB7, and standard loose image files (PNG, JPG, WebP, GIF, BMP, TIFF).

## 📥 Installation

1. Go to the [Releases](../../releases) tab.
2. Download the `YomiFrame.exe` portable file.
3. Place it anywhere on your PC and double-click to run. No installation required!

> **Note:** To associate files with YomiFrame, right-click any `.cbz` or `.zip` file, select `Open With...`, and choose `YomiFrame.exe`.

## 🛠️ Building from Source

To build YomiFrame yourself, you'll need the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

1. Clone the repository:
   ```cmd
   git clone https://github.com/Yuui03E/YomiFrame.git
   ```
2. Navigate to the source directory:
   ```cmd
   cd YomiFrame/src/YomiFrame
   ```
3. Publish as a single, self-contained executable:
   ```cmd
   dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
   ```
4. Find your compiled `YomiFrame.exe` in `bin/Release/net8.0-windows/win-x64/publish/`.
