# HD2QD HD2 Music

A plugin of [HD2 Mod Quick Deploy](https://github.com/pat-pat-patrick/hd2-mod-cli): the NetEase
Cloud Music player of the **HD2 Music** mod, as a tab of the manager's window (Quick Deploy
0.25.0 and later) or a window on an older manager. Sign in with a QR code,
a cookie or an SMS code; browse playlists, search, the daily recommendation, private FM, radio
and podcasts; play, seek and follow the lyrics — and the mod's in-game player, 2D/3D lyrics and
spectrum keep working, because the plugin writes the same state files the mod's helper does.

![The player as a tab: tab strip above, now playing, lyrics, spectrum, next up, and the player's state](docs/window.png)

## What it does

- **Sign-in**: QR code (rendered in the plugin's own window), a `MUSIC_U` cookie, or phone +
  SMS code (`+86` default, any country code).
- **Library**: your playlists and their covers, daily recommendations, private FM, the radio
  directory and its episodes, recommended playlists, search by song or artist, queue.
- **Playback**: play/pause, previous/next, seek, volume, five quality levels (standard, higher,
  exhigh, lossless, Hi-Res), repeat/shuffle/order, gapless next-track preparation.
- **Queue editing**: every row has up, down, remove and play-next; a search hit can be queued
  behind the current song without interrupting it. The playing row is refused — the decoder is
  reading from it — and each edit bumps `queue_revision` so the game knows its rows moved.
- **Lyrics**: line-synced, word-synced where NetEase provides it, translations; the current,
  previous and next line are published to the mod for its 2D and 3D lyrics. The panel lists the
  whole sheet beside the current line: click a row to seek there, and **试听本句** repeats the
  line being sung (the mod gets the same two tools as `lyric_window` and `audition`).
- **Paging**: the radio directory and a show's episodes load twenty at a time, and **更多** asks
  for the next page; a filtered episode list reads up to three pages looking for a match.
- **State for the mod**: now playing, progress, queue, cover (texture, preview and mini), the
  16-band spectrum and the timed world-lyric cues — all in `ncm_state.txt`,
  `ncm_spectrum.txt` and the `.dat` cover files the mod already reads.
- **Manager integration**: the plugin's card in **Plugins**, the command `hd2music`, its body
  handed to the manager's tab bar (`host["tab"]`, Quick Deploy 0.25.0+) with `host["window"]`
  as the fallback, and the manager's own tokens
  (Ground900/700, Grid, Ink, Muted, Yellow, Blue, Bahnschrift + YaHei UI, Ghost/Primary buttons)
  through the shared `Theme`/`Ui.xaml` component set.

## The window

One command bar, five views, no sidebar and no separate player strip:

- **CONSOLE** — the cover, the current line and its translation with the next line under it,
  the whole lyric sheet beside it (click a row to seek, **试听本句** to loop the line), the
  16-band spectrum the mod also draws in game, the transport and the volume; on the right, the
  next five tracks and a SYSTEM panel: state, **quality and order as segmented switches**,
  lyrics, queue, playlist, position, ffmpeg, the helper lock and **导出诊断**.
- **LIBRARY** — playlist cards as numbered tiles; a click reads the playlist into **QUEUE** so a
  song can be chosen, and **播放全部** starts it from the top. **QUEUE** and **SEARCH** — two-line
  track tables (title over artist), one click plays, the current row is yellow; queue rows carry
  the four edit buttons and search rows a **⇢** that queues the hit next. **RADIO** —
  channels on the left, episodes on the right, one click plays an episode, **更多** pages either
  list.
- The gear opens a settings sheet over the same window (lyrics, decoder, account, about);
  quality and order live in the console's SYSTEM panel, where they are one click away. Esc
  closes the sheet. The status strip carries the current reason (reading a playlist, buffering,
  …), account, queue, quality, ffmpeg and the version in mono.

Cards are grid-lined ground, section labels are small caps with a yellow square, numbers are
mono, and yellow is the only accent — the manager's own board, not a stock WPF window.

## Install

1. HD2 Mod Quick Deploy **0.25.0 or later** for the tab (plugin Api 2); 0.15.0–0.24.x still
   works, as a window of its own.
2. Pick **one** of the two builds — never install both, they share the same state files and lock:

   | File | Size | Decoder |
   |---|---:|---|
   | `HD2QD.HD2Music.dll` | ~29 MB | **Full**: FFmpeg 6.1.1 is inside the DLL and written out once on first use; zero setup. |
   | `HD2QD.HD2Music-Lite.dll` | ~340 KB | **Lite**: finds `ffmpeg.exe` through `HD2MUSIC_FFMPEG`, `ncm_ffmpeg.txt`, the plugin folder, `%LOCALAPPDATA%\HD2Phone`, the mod helper's `tools` folder, **`PATH`**, **App Paths in the registry**, or winget/scoop/chocolatey locations. |

3. **Plugins** page → **Add plugin…** → the chosen file (or drop it on the page).
4. Deploy **HD2 Music** in Arsenal, start the game, then open **HD2 Music** on the Plugins page;
   it appears as a tab beside the manager, and a second Open shows that same tab.

The Full build extracts FFmpeg (78.7 MB) next to the plugin, or into the settings folder if that
is not writable, and checks its SHA-256. The Lite build never writes a decoder; besides the paths
above it checks the `PATH` entries, the `App Paths\ffmpeg.exe` registration in HKCU/HKLM (both
registry views), and the usual winget, scoop and chocolatey locations. If none is found, the
status strip says `FFMPEG MISSING` and the **设置** page can point at one. `HD2MUSIC_FFMPEG`
overrides both.

Playback needs the game: the player follows the mod's heartbeat, stops when the game exits, and
writes the state the in-game player reads. Clicking a song while the game is not running does not
start audio; the status strip turns yellow and says `STATUS 未启动游戏 · 启动游戏后即可播放`
(or the engine's own "请先进入游戏并启用新版播放器" after a click).

The plugin and the mod's Python helper (`网易云在线助手`) share
`%LOCALAPPDATA%\HD2Phone\ncm_helper.lock`, so only one of them decodes at a time. If the helper
is already running (F7 playback), the plugin detects the lock and **hands play commands to the
helper through the same `ncm_cmd_*.txt` protocol the game uses**: clicking a song, a playlist's
「播放全部」, a radio channel, an episode, or the transport controls still works; the tab shows
`在线助手已接管播放` and mirrors the helper's title, position and lyrics. The plugin never writes
state or spectrum while locked out. It takes the lock lazily — only when the window or a command
is used, never at manager launch.

## What it reads and writes

Under `%LOCALAPPDATA%\HD2Phone`:

| File | Use |
|---|---|
| `ncm_state.txt` | Written every 200 ms: the mod's whole in-game state (see *Compatibility*). |
| `ncm_spectrum.txt` | Written about 28 times a second: the 16 spectrum bands. |
| `ncm_cover_preview.dat`, `ncm_cover_texture.dat`, `ncm_mini_cover.dat` | Cover art for the mod (360/200/80 px paths). |
| `ncm_recommend_*.dat` | Recommended-playlist cover tiles, at most 24 kept. |
| `ncm_session.json` | The NetEase cookies; written only when you sign in. |
| `ncm_queue.json` | The current playlist and selected row, kept between manager launches. Cleared when the account changes, the saved login fails, or the session is gone; choosing another playlist replaces it. |
| `ncm_pending_login.json` | The QR login session while a QR is on screen. |
| `ncm_lyrics_enabled.txt`, `ncm_quality.txt`, `ncm_order.txt`, `ncm_automix.txt`, `ncm_ffmpeg.txt` | The plugin's own settings, the same names the helper uses. |
| `ffmpeg.exe` | Written next to the plugin (or in this folder) from the packed copy when none exists; the decoder the player spawns. |
| `ncm_diagnostics.txt` | Written only when you press **导出诊断**: build, decoder, state, track, paging states and the state file's own timestamp, for a bug report. The account name is masked. |
| `ncm_helper.lock` | The single-instance lock shared with the helper. |
| `ncm_game_alive.txt` | **Read** only: the mod's heartbeat. Playback is refused without it. |

Elsewhere:

- `%USERPROFILE%\.hd2-mod-cli\plugins` — the plugin file itself, the path the manager installs it to.
- The temporary audio pipe between `ffmpeg.exe` and the player (no file is kept).

**Hosts it contacts** (no telemetry, no update check, nothing else):

- `music.163.com`, `interface.music.163.com` — sign-in, playlists, search, lyrics, stream URLs;
- `*.music.126.net`, `*.music.163.com` — cover images and the audio streams themselves;
- `space.bilibili.com` — only if you click the mod author's link on the **关于** page.

## Compatibility

The state writer is checked field by field against `ncm_online_helper.py` 1.2.7's `publish()`:
current song, progress, lyrics (line, word marks, translation, timed world cues), queue, cover
keys, playlist/recommend/radio/podcast/search lists, spectrum, and the paging/filter rows
`search_more`, `search_offset`, `radio_keyword`, `radio_more`, `radio_offset`, `radio_serial`,
`podcast_filter`, `podcast_filter_serial`, `podcast_more`, `podcast_offset`, `recommend_more`,
`queue_revision`, `audition` and `account_loading` are all written, as are the lyric rows
`lyric_pos`, `lyric_offset`, `lyric_at`, `lyric_total`, `lyric_context`, `lyric_window_center`
and `lyric_window_row`. The game-side paging, queue editing, lyric window and audition
affordances therefore work; the commands `radio_more`, `podcast_more`, `queue_edit`,
`search_enqueue`, `lyric_window`, `lyric_window_close`, `audition` and `audition_stop` are read
from `ncm_cmd_*.txt`, and `deployment` is the one field deliberately not written (the plugin
has no deployment story to report). The interface is bilingual. Settings → 语言 offers
跟随 HD2QD / 强制中文 / 强制 English; "follow" tracks the manager's language, and the choice is
stored in `ncm_language.txt`.

The reader side was checked against the mod itself, not only the helper: HD2 Music 1.2.4 ships
plaintext Lua, and `docs/mod-reverse-1.2.4.md` records every row pattern, value vocabulary
(`quality` is `hires`, `order` is `order|shuffle|repeat`, `state` is the four playback words),
the 32-hex spectrum frame bound to `seek_serial`, and the loader hop that keeps 1.2.7's body out
of the package. Two fixes came out of it: `account_loading` is published on every frame like the
helper does, and a nameless `recommend=`/`radio=` row falls back to its id because the mod's
pattern needs one character of name and would otherwise drop the card.

The tab also exposes 每日推荐, 私人 FM, STOP, radio search and podcast filter,
plus a verification-state website link and a karaoke prefix preview in the lyric panel.

The player also refuses to play when the mod's heartbeat (`ncm_game_alive.txt`) is absent, which
is the mod's own rule: start the game, deploy HD2 Music, keep the manager running.

## Command line

```
hd2music                  status: account, song, queue, position, ffmpeg, notice
hd2music play|pause|toggle
hd2music next|prev|stop
hd2music search <words>
hd2music login            start QR sign-in
hd2music lyrics 0|1
hd2music vol 0-100
```

`status` returns `0`; a refused action `1`; a wrong command line `2`.

## Build

Needs the .NET SDK 8 or later (`dotnet` on `PATH`, or set `DOTNET`):

```
build.bat
```

It builds both DLLs, then runs `tests\Verify.exe` on each, which loads the file the way the
manager does (bytes, not `LoadFrom`), checks the contract (`Api` 2, `Kind` `window`, the one
public `Plugin`, `Title`/`Summary` in both languages), the embedded icon and text, that only .NET
Framework assemblies are referenced, the tab path with a fake `host["tab"]`, and builds the view,
the fallback window and the login window off-screen. It needs no game, network or manager.

Current output:

```
  ok   assembly name HD2QD.HD2Music, version 1.8.0
  ok   target framework .NETFramework,Version=v4.8
  ok   Api = 2 (a const the manager reads before running any code)
  ok   Kind = window
  ok   embedded Lang.en.json, Lang.zh-CN.json, Ui.xaml, app.ico, QRCoder.dll
  ok   embedded ffmpeg.exe.gz (full build)
  ok   references only .NET Framework assemblies (8)
  ok   QR encoder embedded (QRCoder, MIT): 29x29 matrix, no QRCoder.dll on disk
  ok   embedded ffmpeg extracted to a temp folder: 78 MB, SHA-256 matches
  ok   opens as a manager tab: "HD2 音乐"
  ok   a second Open shows the same tab, no second view
  ok   closing the tab runs the plugin's cleanup
  ok   player view built off-screen and laid out on #0C0E11
  ok   fallback window for a manager without tabs: "HD2 音乐"
  ok   login window built off-screen and laid out: "登录 · HD2 音乐"
OK: all checks passed
```

The Lite build reports the other side of the same checks:

```
  ok   lite build: decoder is external
  ok   lite build: no embedded ffmpeg to extract
  ok   opens as a manager tab: "HD2 音乐 Lite"
OK: all checks passed
```

Runtime dependencies: .NET Framework 4.8 (part of Windows 10/11), `System.Web.Extensions`
(part of the framework) and WPF. The QR encoder (QRCoder 1.4.3, MIT) is in both builds.
The Full build also carries FFmpeg 6.1.1 essentials (GPLv3) and extracts it on first use; the
Lite build needs an existing `ffmpeg.exe` as above. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Licence and credits

MIT — see [LICENSE](LICENSE).

- **HD2 Music** and its `网易云在线助手` are the work of [@127_MU](https://space.bilibili.com/283469182);
  this plugin is an unofficial companion that brings the helper's features into the manager.
- The manager, its plugin contract and its theme are [pat-pat-patrick](https://github.com/pat-pat-patrick/hd2-mod-cli)'s.
- The NetEase Web API request shape (`weapi`) follows the public reverse-engineered protocol used
  by the helper and other open clients. NetEase Cloud Music is a service of NetEase, Inc.; this
  plugin is not affiliated with it.
- QR encoding: [QRCoder](https://github.com/codebude/QRCoder) 1.4.3 (MIT), by Raffael Herrmann,
  embedded in the DLL and loaded from bytes.
- The bundled decoder is FFmpeg 6.1.1-essentials_build (gyan.dev), GPLv3, extracted at run
  time; it is a separate program, not part of this MIT-licensed code. Source and licence:
  [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

---

# HD2 音乐 · 管理器插件

[HD2 Mod Quick Deploy](https://github.com/pat-pat-patrick/hd2-mod-cli) 的插件：把 **HD2 Music**
模组的网易云在线助手搬进管理器窗口。扫码、Cookie 或短信登录；歌单、搜索、每日推荐、私人 FM、
电台播客；播放、拖动进度、歌词同步。插件写入与助手相同的状态文件，因此游戏内播放器、2D/3D
歌词和频谱照常工作。

- 安装：管理器 0.25.0 以上 → 插件页 → 添加**其中一个**文件；以标签页打开，再次“打开”会回到同一页签。旧版 0.15.0–0.24.x 回退为独立窗口。
  - `HD2QD.HD2Music.dll`：完整版，约 29 MB，内置 FFmpeg，首次启动自动释放。
  - `HD2QD.HD2Music-Lite.dll`：精简版，约 340 KB，自动查找 `ffmpeg.exe`：环境变量、设置页、助手 tools、插件目录、**PATH**、注册表 **App Paths**，以及 winget/scoop/chocolatey 常见位置。两个版本不要同时安装。
- 播放需要游戏运行：播放器跟随 MOD 心跳，游戏退出即停止。未启动游戏时点击歌曲不会出声，底部状态栏会变黄并提示“未启动游戏 · 启动游戏后即可播放”（点击后显示引擎的“请先进入游戏并启用新版播放器”）。
- 播放列表在管理器重启后保留在 `ncm_queue.json`：只保存队列与选中行，不保存播放进度。账号切换、登录失效、退出登录或重新选择歌单会清除/替换它。
- 插件与 Python 助手共用 `ncm_helper.lock`，同一时间只有一个解码。助手已在运行（F7 播放）时，
  插件会把点击的播放/切歌/音量/音质等命令写进 `ncm_cmd_*.txt` 转交助手执行；状态栏显示
  “在线助手已接管播放”，标题、进度、歌词从助手状态同步。插件占锁时不写状态与频谱。
- 状态字段与助手 1.2.7 的 `publish()` 逐项对照，含游戏内分页/筛选/队列/歌词窗口字段：`search_more`、`search_offset`、`radio_keyword`、`radio_more`、`radio_offset`、`radio_serial`、`podcast_filter`、`podcast_filter_serial`、`podcast_more`、`podcast_offset`、`recommend_more`、`queue_revision`、`audition`、`account_loading`，以及 `lyric_pos`、`lyric_offset`、`lyric_at`、`lyric_total`、`lyric_context`、`lyric_window_center`、`lyric_window_row`；“加载更多/筛选/队列编辑/歌词窗口/试听”可用。唯一故意不写的是 `deployment`（插件没有可汇报的部署状态）。
- 队列每行带上移、下移、移出、设为下一首；搜索结果可用 ⇢ 插到当前歌曲之后。正在播放的那一行不可编辑——解码器正从它读取。
- 播放页当前歌词右侧列出整张歌词：点击某行跳转，**试听本句**循环当前这一句（最长 90 秒，或直到按下别的操作）。
- 电台频道与播客节目每次加载 20 条，底部“更多”翻页；带筛选词时最多连读三页找匹配标题。
- SYSTEM 面板的**导出诊断**把版本、解码器、状态、曲目与状态文件时间写进 `ncm_diagnostics.txt`，账号名已打码。
- 界面中英双语；设置页“语言”可选“跟随 HD2QD / 强制中文 / 强制 English”，选择保存在 `ncm_language.txt`；默认跟随管理器语言。
- 插件页新增每日推荐、私人 FM、STOP、电台搜索、播客筛选；扫码需要安全验证时显示“打开官网”；歌词面板显示逐字高亮预览。

构建：`build.bat`，同时产出 `release\HD2QD.HD2Music.dll`（完整版）与 `release\HD2QD.HD2Music-Lite.dll`（精简版），并分别运行 `tests\Verify.exe` 检查契约、
资源、依赖与窗口构造。
