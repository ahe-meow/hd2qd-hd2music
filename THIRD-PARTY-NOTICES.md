# Third-party notices

The plugin's own source is MIT (see [LICENSE](LICENSE)). Two third-party programs travel with
it; their licences are their own.

## FFmpeg 6.1.1-essentials_build

- File: `assets/ffmpeg.exe.gz` (gzip of `ffmpeg.exe`, 78.7 MB extracted), embedded in the
  plugin DLL as `ffmpeg.exe.gz` and written out on first use by `src/Ffmpeg.cs`.
- Build: `ffmpeg version 6.1.1-essentials_build-www.gyan.dev`, Copyright (c) 2000-2023 the
  FFmpeg developers, built with gcc 12.2.0 (MSYS2), `--enable-gpl --enable-version3`, a GPLv3
  build.
- SHA-256 of the extracted `ffmpeg.exe`:
  `04e1307997530f9cf2fe35cba2ca7e8875ca91da02f89d6c7243df819c94ad00`.
- Licence: GNU General Public License v3.0 — https://www.gnu.org/licenses/gpl-3.0.html
- Source: https://ffmpeg.org/download.html and the build recipe at
  https://www.gyan.dev/ffmpeg/builds/ (the corresponding source is the FFmpeg 6.1.1 release
  plus the libraries named in `ffmpeg -version`).
- FFmpeg is a separate program extracted next to the plugin; it is not linked into, nor a
  derivative work of, this plugin's MIT code.

## QRCoder 1.4.3

- File: `QRCoder.dll` (net40), embedded in the plugin DLL and loaded from bytes by
  `src/QrCode.cs`.
- Copyright (c) 2013-2020 Raffael Herrmann.
- Licence: MIT — https://github.com/codebude/QRCoder/blob/master/LICENSE.txt
