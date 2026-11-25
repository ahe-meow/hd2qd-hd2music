# assets

`ffmpeg.exe.gz` is the decoder the plugin carries: FFmpeg 6.1.1-essentials_build (gyan.dev),
GPLv3, gzipped from 78.7 MB to about 28 MB and embedded in the DLL at build time as
`ffmpeg.exe.gz`. `src/Ffmpeg.cs` writes it out on first use when no `ffmpeg.exe` exists beside
the plugin or in the player's settings folder, and checks the extracted file against
`04e1307997530f9cf2fe35cba2ca7e8875ca91da02f89d6c7243df819c94ad00`.

See [../THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md) for the licence and source.
