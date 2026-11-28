# assets

`ffmpeg.exe.lzma` is the decoder the plugin carries: FFmpeg 6.1.1-essentials_build (gyan.dev),
GPLv3, packed from 78.7 MB to 20.7 MB (LZMA, `.lzma` alone format: 1 byte of properties, 4 of
dictionary size, 8 of the unpacked length, then the data) and embedded in the **Full** build at
build time as `ffmpeg.exe.lzma`. `src/Ffmpeg.cs` writes it out on first use when no `ffmpeg.exe`
exists beside the plugin or in the player's settings folder, and checks the extracted file against
`04e1307997530f9cf2fe35cba2ca7e8875ca91da02f89d6c7243df819c94ad00`.

Gzip left the DLL at 29.6 MB, over the 25 MB a community plugin list accepts; LZMA packs the same
file to 20.7 MB. To repack (Python's standard library writes the same format):

    python -c "import lzma,sys; raw=open('ffmpeg.exe','rb').read(); \
      f={'id':lzma.FILTER_LZMA1,'preset':9|lzma.PRESET_EXTREME,'dict_size':8<<20}; \
      open('ffmpeg.exe.lzma','wb').write(lzma.compress(raw,format=lzma.FORMAT_ALONE,filters=[f]))"

See [../THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md) for the licence and source.
