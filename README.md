# Filee

**A free, open-source file converter that works by dragging and dropping.**

Filee lets you convert files from a menu that appears near your cursor. It supports images, documents, PDFs, spreadsheets, presentations, audio, video, archives, fonts, CAD files, 3D models and more. Conversion happens locally on your computer; your files are not uploaded.

> **Upstream project:** This repository is an independent copy of [KnifeLemon/Filee](https://github.com/KnifeLemon/Filee). Original project and author attribution are retained. For the latest official releases and full documentation, see the upstream repository.

## Installation

Download the appropriate package from the [official Filee Releases page](https://github.com/KnifeLemon/Filee/releases/latest).

### Windows 10 / 11

- **Installer:** Download `Filee-<version>-win-Setup.exe` and run it.
- **Portable:** Download and extract `win-Portable.zip`, then run `Filee.exe`.

The installer can optionally add Filee to the Windows Explorer context menu, start it at sign-in, and install the `filee` command-line tool.

### Linux (x64 / ARM64)

On Ubuntu 22.04 or newer:

1. Download the package for your architecture from the [latest release](https://github.com/KnifeLemon/Filee/releases/latest).
2. Extract the downloaded package.
3. Open a terminal in the extracted folder and run:

   ```bash
   ./install.sh
   ```

Python 3 is required. Drag gestures require X11; on Wayland, use the drop zone or the file manager's right-click menu.

### macOS 13 or newer (preview)

1. Download the `osx-arm64` package for Apple Silicon or `osx-x64` for Intel from the [latest release](https://github.com/KnifeLemon/Filee/releases/latest).
2. Extract the package.
3. Open a terminal in the extracted folder and run:

   ```bash
   ./install.sh
   ```

The current macOS build is not notarized. If macOS blocks the first launch, check **System Settings → Privacy & Security** for the option to open it anyway.

## Main features

- Convert files by dragging them onto a format in the menu near your cursor.
- Batch conversion and automatic multi-step conversion routes.
- Image cropping and presets for quality, size, DPI, PDF pages and compression.
- Browser downloads, watch folders, recent conversions and a file drop zone.
- Optional command-line conversion.
- Light and dark themes and multiple interface languages.
- Local processing and offline use for installed conversion capabilities.

## Supported formats and optional components

Filee supports a wide range of formats. Some conversions work immediately; others require an optional engine that Filee can download when needed.

- **Ghostscript** — certain vector and PostScript conversions
- **LibreOffice** — additional office document conversions
- **Pandoc** — additional text and markup formats
- **calibre** — some e-book formats
- **FFmpeg** — audio and video conversions

See the upstream [engine documentation](https://github.com/KnifeLemon/Filee/blob/main/docs/ENGINES.md) for details and the [full format list](https://github.com/KnifeLemon/Filee#formats).

## Command line (optional)

If the command-line tool is installed, examples include:

```bash
filee convert photo.heic --to jpg
filee convert *.png --to webp --quality 80 -o converted
filee help
```

On Windows, the same tool can be used from a terminal after installation. See the [CLI documentation](https://filee.sh/cli/) for all commands and options.

## Privacy

File conversion takes place on your computer. Filee does not upload your files or collect usage telemetry. It may check for updates and download optional engines when requested.

## Original project and license

Filee was created by **Myeonggu Kim (KnifeLemon)** and contributors. This repository preserves the upstream project’s attribution and license.

This project is licensed under the [MIT License](LICENSE). Bundled third-party components may have separate licenses; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

For source code, updates, troubleshooting and contribution guidance, visit the [original Filee repository](https://github.com/KnifeLemon/Filee).
