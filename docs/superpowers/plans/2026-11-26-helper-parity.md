# HD2 Music Helper Parity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 补齐插件相对在线助手的分页/筛选协议、UI 入口、扫码验证辅助与逐字预览。

**Architecture:** 原地扩展 `NeteaseClient` → `Engine`/`UiState` → `PlayerView`/`LoginWindow`。不新增 BrowseController；`Engine.Command` 保持协议中枢。

**Tech Stack:** C# / .NET Framework 4.8 / WPF / JavaScriptSerializer / dotnet build / tests/Verify.exe

**Spec:** `docs/superpowers/specs/2026-11-26-helper-parity-design.md`

## Global Constraints

- 目标框架保持 `.NETFramework,Version=v4.8`；插件 Api 保持 2。
- 命令文件仍为 ASCII、以 `\n` 结尾、45 秒有效、最多 64 个。
- 新状态字段取值必须与 spec 表格一致，不得发明新值。
- `search_more` / `recommend more` 只在对应 `*_more` 为 `ready` 或 `error` 且无在途任务时执行。
- 助手持锁（`_lockedOut`）时不得写本地状态；新命令走 `SendCommand`。
- Full/Lite 双版本必须同源码构建；`build.bat` 对两者都跑 Verify。
- 不修改在线助手源码，不修改游戏端文件。

---

### Task 1: API 分页与电台搜索

**Files:**
- Create: `src/BrowsePage.cs`
- Modify: `src/NeteaseClient.cs`
- Modify: `src/HD2QD.HD2Music.csproj`
- Test: `tests/Verify.cs`

**Interfaces:**
- Consumes: 现有 `ApiCallOn`、`TrackRows`、`SideSession`、`Util` 工具。
- Produces: `BrowsePage<T>`、`SearchTracksPage`、`RecommendationPage`、`SearchRadios`。

- [ ] **Step 1: RED — 在 Verify 加契约检查**

```csharp
static void CheckBrowseApi(Assembly asm)
{
    var page = asm.GetType("Hd2Qd.HD2Music.BrowsePage`1");
    if (page == null) { Fail("BrowsePage<T> is missing"); return; }
    foreach (var f in new[] { "Rows", "Offset", "More" })
        if (page.GetField(f) == null) { Fail("BrowsePage." + f + " is missing"); return; }
    var client = asm.GetType("Hd2Qd.HD2Music.NeteaseClient");
    foreach (var m in new[] { "SearchTracksPage", "RecommendationPage", "SearchRadios" })
        if (client.GetMethod(m) == null) { Fail("NeteaseClient." + m + " is missing"); return; }
    Pass("browse APIs present");
}
```

在 `Main()` 中 `CheckFfmpeg(asm)` 后调用。运行：

```bash
cmd //c build.bat
```

Expected: FAIL — `BrowsePage<T> is missing`。

- [ ] **Step 2: GREEN — 新增类型与 API**

`src/BrowsePage.cs`:

```csharp
using System.Collections.Generic;
namespace Hd2Qd.HD2Music
{
    public sealed class BrowsePage<T>
    {
        public List<T> Rows = new List<T>();
        public int Offset;
        public bool More;
    }
}
```

`NeteaseClient.cs` 新增三个方法：

```csharp
public BrowsePage<Track> SearchTracksPage(string keyword, Dictionary<string,string> cookies, int offset)
{
    // /api/cloudsearch/pc, type=1, limit=25, offset=offset
    // rows = TrackRows(result.songs).Take(25); next = offset + rows.Count;
    // more = rows.Count > 0 && (next < songCount || rows.Count == 25)
}

public BrowsePage<Playlist> RecommendationPage(int offset, HashSet<string> exclude)
{
    // /api/playlist/list, cat=全部, order=hot, limit=8, offset, total=true
    // skip ids in exclude; rows.Add(new Playlist { Id, Name, CoverUrl }); offset++
    // more = response.more || entries.Count == 8
}

public List<RadioChannel> SearchRadios(string keyword, Dictionary<string,string> cookies)
{
    // /api/cloudsearch/pc, type=1009, limit=25, offset=0
    // djRadios -> RadioChannel { Id, Name, Description }
}
```

保留原 `SearchTracks` 签名不变；它可委托 `SearchTracksPage(keyword, cookies, 0).Rows`。

- [ ] **Step 3: Verify**
Run: `cmd //c build.bat`
Expected: `OK: all checks passed`（Full + Lite），含 `browse APIs present`。

- [ ] **Step 4: Commit**
`git add src/BrowsePage.cs src/NeteaseClient.cs src/HD2QD.HD2Music.csproj tests/Verify.cs && git commit -m "feat: add paged browse APIs"`

---

### Task 2: 引擎协议字段与命令

**Files:**
- Modify: `src/UiState.cs`
- Modify: `src/Engine.cs`
- Test: `tests/Verify.cs`

**Interfaces:**
- Consumes: Task 1 的 `SearchTracksPage`、`RecommendationPage`、`SearchRadios`。
- Produces: `Engine.SearchMore`、`RadioSearch`、`PodcastFilter`、`LoadRecommendations(bool more)`、`UiState` 新字段。

- [ ] **Step 1: RED — Verify 检查字段与命令**

在 `CheckQueuePersistence` 后调用新方法：

```csharp
static void CheckBrowseState(Assembly asm)
{
    Type ui = asm.GetType("Hd2Qd.HD2Music.UiState");
    foreach (var f in new[] { "SearchMore", "SearchOffset", "RadioKeyword", "PodcastFilter", "PodcastFilterSerial", "RecommendMore", "AccountLoading", "LyricPrefix" })
        if (ui.GetField(f) == null) { Fail("UiState." + f + " is missing"); return; }
    Type engine = asm.GetType("Hd2Qd.HD2Music.Engine");
    foreach (var m in new[] { "SearchMore", "RadioSearch", "PodcastFilter", "LoadRecommendations" })
        if (engine.GetMethod(m, BindingFlags.Instance | BindingFlags.Public) == null) { Fail("Engine." + m + " is missing"); return; }
    Pass("browse state fields and commands present");
}
```

Run: `cmd //c build.bat`
Expected: FAIL — `UiState.SearchMore is missing`。

- [ ] **Step 2: GREEN — UiState 字段**

```csharp
public string SearchMore = "";
public int SearchOffset;
public string RadioKeyword = "";
public string PodcastFilter = "";
public int PodcastFilterSerial;
public string RecommendMore = "";
public bool AccountLoading;
public string LyricPrefix = "";
```

- [ ] **Step 3: GREEN — Engine 状态与命令**

新增字段：`_searchOffset`、`_searchMore`、`_recommendOffset`、`_recommendMore`、`_radioKeyword`、`_podcastFilter`、`_podcastFilterSerial`、`_accountLoading`。

修改：
- `Search()`：重置 `_searchOffset=0`、`_searchMore="loading"`，完成后 `ready`/`end`/`error`。
- `SearchMore(string serial)`：校验 serial 且状态允许；调用 `SearchTracksPage`，追加结果，写 offset/more。
- `RadioSearch(string keyword)` / `RadioClipboard()`：设置 `_radioKeyword`，调用 `SearchRadios`，写 `_radios`。
- `PodcastFilter(string text)` / `PodcastFilterClipboard()`：设置 `_podcastFilter` 并 `_podcastFilterSerial++`。
- `LoadRecommendations(bool more)`：more=true 时调用 `RecommendationPage(_recommendOffset, existing ids)`，追加，写 `_recommendMore`。
- `RefreshAccount()`：开始时 `_accountLoading=true`，结束/异常时 false。
- `Snapshot()`：把上述字段写入 `UiState`；`LyricPrefix` 从 `LyricLines` 的 prefix 输出。
- `Publish()`：写 `search_more`、`search_offset`、`radio_keyword`、`podcast_filter`、`podcast_filter_serial`、`recommend_more`、`account_loading`。
- `Command()`：新增 `search_more`、`radio_search`、`radio_clipboard`、`podcast_filter`、`podcast_filter_clipboard`、`recommend more`，并把 `recommend` 无参保持原行为。

- [ ] **Step 4: Verify**
Run: `cmd //c build.bat`
Expected: `OK: all checks passed`。

- [ ] **Step 5: Commit**
`git add src/UiState.cs src/Engine.cs tests/Verify.cs && git commit -m "feat: add browse protocol fields and commands"`

---

### Task 3: 插件 UI 入口

**Files:**
- Modify: `src/PlayerView.cs`
- Modify: `src/Words.cs` / `Lang/en.json` / `Lang/zh-CN.json`（仅文案需要时）
- Test: `tests/Verify.cs`

**Interfaces:**
- Consumes: Task 2 的 Engine 方法。
- Produces: 按钮/输入框事件绑定。

- [ ] **Step 1: RED — Verify 检查 UI 文本存在**

```csharp
static void CheckBrowseUi(Assembly asm)
{
    string[] expected = { "每日推荐", "私人 FM", "STOP", "搜索电台", "粘贴", "筛选" };
    // Verify 通过反射构造 PlayerView 后遍历 LogicalTreeHelper 的 TextBlock/Button Content
    // 缺少任一文本则 Fail
}
```

Run: `cmd //c build.bat`
Expected: FAIL — 缺少 `搜索电台`。

- [ ] **Step 2: GREEN — UI 控件**

- `LibraryPage` 头部：在 `PLAYLIST`/`刷新` 旁加 `每日推荐`、`私人 FM`。
- `QueuePage` 头部：加 `STOP`，调用 `_engine.StopPlayback()`。
- `RadioPage`：
  - 电台搜索 `TextBox _radioSearch` + `搜索电台` + `粘贴` 按钮；回车调用 `_engine.RadioSearch(text)`。
  - 播客筛选 `TextBox _podcastFilter` + `筛选` + `粘贴` 按钮；调用 `_engine.PodcastFilter(text)`。
- 低端管理器回退窗口同样使用这些页面，无需单独处理。

- [ ] **Step 3: Verify**
Run: `cmd //c build.bat`
Expected: `OK: all checks passed`，并含 `browse UI entries present`。

- [ ] **Step 4: Commit**
`git add src/PlayerView.cs src/Words.cs src/Lang/en.json src/Lang/zh-CN.json tests/Verify.cs && git commit -m "feat: expose daily, fm, stop, radio and podcast search UI"`

---

### Task 4: 登录验证与逐字预览

**Files:**
- Modify: `src/LoginWindow.cs`
- Modify: `src/PlayerView.cs`
- Modify: `src/UiState.cs`（见 Task 2 字段）
- Test: `tests/Verify.cs`

**Interfaces:**
- Consumes: `UiState.LyricPrefix`、`Engine._qrResult`。
- Produces: 验证态官网按钮、逐字高亮 Run。

- [ ] **Step 1: RED — Verify 检查**

```csharp
static void CheckVerificationUi(Assembly asm)
{
    // LoginWindow 构建后，设置 _qrResult="verification"，刷新，断言出现“打开官网”按钮
    // PlayerView 构建后，断言 LyricPrefix 行存在并绑定 state.LyricPrefix
}
```

Run: `cmd //c build.bat`
Expected: FAIL — 缺少 `打开官网`。

- [ ] **Step 2: GREEN — 登录页**

`LoginWindow.Refresh()` 中 `_qrResult == "verification"` 时显示按钮：

```csharp
Button open = SmallButton("打开官网", delegate { Process.Start("https://music.163.com"); });
open.ToolTip = "完成安全验证后返回此窗口，重新扫码或点刷新二维码。";
```

- [ ] **Step 3: GREEN — 播放页逐字预览**

在歌词区新增 `TextBlock _lyricPrefixLine`；`RefreshCore()`：

```csharp
string prefix = state.LyricPrefix ?? "";
string current = state.Lyric ?? "";
_lyricPrefixLine.Inlines.Clear();
_lyricPrefixLine.Inlines.Add(new Run(prefix) { Foreground = Theme.Yellow });
_lyricPrefixLine.Inlines.Add(new Run(current.Substring(Math.Min(prefix.Length, current.Length))) { Foreground = Theme.Muted });
```

- [ ] **Step 4: Verify**
Run: `cmd //c build.bat`
Expected: `OK: all checks passed`。

- [ ] **Step 5: Commit**
`git add src/LoginWindow.cs src/PlayerView.cs tests/Verify.cs && git commit -m "feat: add verification website link and karaoke lyric preview"`

---

### Task 5: 文档、验证与发布

**Files:**
- Modify: `README.md`
- Modify: `docs/superpowers/specs/2026-11-26-helper-parity-design.md`（若计划实现有偏差）
- Test: `build.bat`

- [ ] **Step 1: 更新 README**
写入新增状态字段、命令、UI 入口、逐字预览说明；中文段落同步。

- [ ] **Step 2: 全量验证**
Run: `cmd //c build.bat`
Expected: Full/Lite 均 `OK: all checks passed`。

- [ ] **Step 3: 版本与发布**
版本递增到 `1.4.0`；更新 `PlayerView` 版本串；创建 release 并上传两个 DLL；复制 Full 到 `%USERPROFILE%\.hd2-mod-cli\plugins\HD2QD.HD2Music.dll`。

- [ ] **Step 4: Commit**
`git add -A && git commit -m "feat: helper parity — paging, filters, browse UI and lyric preview, 1.4.0"`

## Self-Review

- Spec 的每个 A/B/C 要求都有对应任务。
- 类型名、方法名、字段名在任务间一致：`BrowsePage<T>`、`UiState.SearchMore`、`Engine.SearchMore`、`Engine.LoadRecommendations(bool)`。
- 无 TBD；每个任务有 RED/GREEN/Verify/Commit。
