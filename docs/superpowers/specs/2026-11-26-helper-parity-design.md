# HD2 Music 插件 — 在线助手功能对齐设计

日期：2026-11-26

## 目标

在不改变现有播放器架构、不引入新子系统的前提下，补齐插件相对 `HD2 Music/网易云在线助手` 的真实功能缺口：游戏侧分页/筛选协议字段、插件 UI 入口、扫码安全验证辅助与逐字歌词预览。

## 范围

### 实现

- A 协议字段与命令：`search_more`/`search_offset`、`radio_keyword`、`podcast_filter`/`podcast_filter_serial`、`recommend_more`、`account_loading`。
- A 命令：`search_more`、`radio_search`、`radio_clipboard`、`podcast_filter`、`podcast_filter_clipboard`、`recommend more`。
- B UI：每日推荐、私人 FM、停止播放、电台搜索/粘贴搜索、播客筛选/粘贴筛选。
- C：扫码安全验证时“打开官网”按钮；播放页用 `Prefix()` 结果做逐字高亮预览。

### 不实现

- 助手 TUI 菜单、终端二维码/动画、快捷键、后台隐藏模式、`ncm_background.log`。
- SMTC 外部歌词与 `HD2Music-SMTC.exe` 相关逻辑。
- 游戏内 2D/3D 歌词渲染、F7 配色、拼音候选：由 MOD/游戏端负责。
- 排行榜、会员刷新：在线助手源码中未见，非本插件缺口。
- 新分页/筛选结果的持久化：沿用现有队列持久化，不改变语义。

## 架构

原地扩展现有三层：

1. `NeteaseClient.cs`：新增分页/搜索 API，返回精确 Offset 与 more。
2. `Engine.cs`：持有分页状态、处理命令、写 `ncm_state.txt` 字段。
3. `PlayerView.cs` / `LoginWindow.cs`：暴露入口与预览。

不新建 BrowseController。理由：现有 `Engine.Command` 已是协议中枢，UI 已直接调用 Engine；新增控制器会造成双入口和重复状态。

## 数据与协议契约

`ncm_state.txt` 新增/明确字段，取值与在线助手一致：

| 字段 | 值 |
|---|---|
| `search_more` | `""` / `loading` / `ready` / `end` / `error` |
| `search_offset` | 下一次搜索起始整数，初始 0 |
| `radio_keyword` | 最近电台搜索关键词，已 clean |
| `podcast_filter` | 播客筛选文本，已 clean |
| `podcast_filter_serial` | 每次筛选 +1 的整数 |
| `recommend_more` | `""` / `loading` / `ready` / `end` / `error` |
| `account_loading` | 0/1，账号刷新进行中为 1 |

已有字段不变：`search_state`、`search_serial`、`search_result=`、`radio_state`、`radio=`、`podcast_state`、`podcast_episode=`。

命令扩展（均经 `ncm_cmd_*.txt`，ASCII 且以 `\n` 结尾）：

- `search_more <serial>`：serial 等于当前 `search_serial` 时才追加下一页。
- `radio_search <utf8-hex>`：搜索电台，最多 60 字，写 `radio_keyword`。
- `radio_clipboard`：读剪贴板后等同 `radio_search`。
- `podcast_filter <utf8-hex>`：设置筛选文本并递增 serial。
- `podcast_filter_clipboard`：读剪贴板后等同 `podcast_filter`。
- `recommend more`：加载下一页推荐歌单，排除已显示 ID。

## API 契约

在 `NeteaseClient.cs` 新增：

```csharp
public sealed class BrowsePage<T>
{
    public List<T> Rows = new List<T>();
    public int Offset;
    public bool More;
}

public BrowsePage<Track> SearchTracksPage(string keyword, Dictionary<string,string> cookies, int offset);
public BrowsePage<Playlist> RecommendationPage(int offset, HashSet<string> exclude);
public List<RadioChannel> SearchRadios(string keyword, Dictionary<string,string> cookies);
```

- 搜索：`/api/cloudsearch/pc`，`type=1`，`limit=25`，`offset`；`more = songs && (offset+count < songCount || count==25)`。
- 电台搜索：同上但 `type=1009`，读取 `djRadios`，最多 25。
- 推荐下一页：`/api/playlist/list`，`cat=全部`、`order=hot`、`limit=8`、`offset`，跳过 `exclude`。

## UI 契约

- LIBRARY 页头部：新增 `每日推荐`、`私人 FM` 按钮，分别调用 `Engine.Daily()` / `Engine.Fm()`。
- QUEUE 页头部：新增 `STOP` 按钮，调用 `Engine.StopPlayback()`。
- RADIO 页：新增电台搜索输入框 + `搜索电台`/`粘贴` 按钮；节目列表新增筛选输入框 + `筛选`/`粘贴` 按钮。
- LOGIN 页：当 `_qrResult == "verification"` 时显示 `打开官网` 按钮，打开网易云官网。
- 播放页歌词区：在上一行/当前行/翻译/下一行之外，新增一行逐字预览，把 `Prefix()` 文本用黄色 Run 渲染，剩余部分用 Muted Run 渲染。

## 错误处理

- 分页请求失败：对应 `*_more` 置 `error`，写可读 notice，不清空已有结果。
- 搜索关键词为空：`radio_keyword` 清空，恢复到热门电台目录。
- `search_more` serial 不匹配：忽略，避免旧命令污染新搜索。
- 剪贴板不可用：notice 提示，状态不变。
- 助手锁冲突时，新命令同样走 `SendCommand` 转交；不写本地状态。

## 测试

`tests/Verify.cs` 增加契约检查：

- `BrowsePage<T>` 字段存在。
- `NeteaseClient` 三个新方法存在。
- `Engine` 新方法存在：`SearchMore`、`RadioSearch`、`PodcastFilter`、`LoadRecommendations(bool)`。
- `UiState` 新增字段：`SearchMore`、`SearchOffset`、`RadioKeyword`、`PodcastFilter`、`PodcastFilterSerial`、`RecommendMore`、`AccountLoading`、`LyricPrefix`。
- `Publish()` 源码/反射检查上述状态字段写入（通过状态契约检查方法）。
- 命令表包含六个新命令。

每个任务 RED → GREEN，最后 `build.bat` 跑 Full/Lite Verify 全过。

##风险

- 游戏端 F7 UI 是否能显示新增按钮取决于 MOD Lua；插件只保证状态字段与协议一致。
- 逐字预览仅为插件窗口内辅助，不替代游戏内 2D/3D 渲染。
- API 字段 `songCount`/`more` 在某些接口缺失时按 `count==25` 推断。
