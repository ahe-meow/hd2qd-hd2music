# 上游 HD2 Music 1.2.7 与本地插件：差异与跟进建议

日期：2026-11-27
上游位置：`C:\Users\madma\Desktop\游戏\HD2 TOOLS\HD2 Music\`
本地插件：`hd2qd-hd2music`（1.7.0）
基线：`HD2 Music/网易云在线助手`（上游 11-25 版）与 `HD2 Music/HD2 Music - Arsenal.zip`（mod 1.2.3）

## 一、结论

上游这次是 **mod 1.2.3 → 1.2.7** 与助手的同步升级，改动集中在**播放页交互**（队列编辑、歌词浏览、歌词校准、诊断）与**电台/播客翻页**。
插件已有的核心播放、歌词协议、频谱、封面都没变。缺的是 **7 组新协议字段 + 4 个新命令**，其中 3 组是「新 mod 界面上有按钮、插件点了没反应」的空操作。

## 二、证据：上游相对旧快照改了什么

行数为新文件行号（`ncm_online_helper.py` 1941 行，旧 1610 行，净 +331）。

| # | 改动 | 位置 | 性质 |
|---|---|---|---|
| 1 | 队列编辑：上移/下移/移除/设为下一首 | `edit_queue()` 888；命令 `queue_edit` 1237 | 新增 |
| 2 | 搜索「加入下一首」 | `search_enqueue` 1185 | 新增 |
| 3 | 队列版本号（防界面拿到旧下标） | `queue_revision` 1655，`edit_queue` 加 1 | 新增字段 |
| 4 | 电台列表翻页 | `radio_directory(cookies, offset)` 451；`radio_more` 1136 | 新增 |
| 5 | 电台搜索翻页 + 换接口 | `search_radios(..., offset)` 460，改走 `/api/search/get/web` | 修改 |
| 6 | 节目（播客）翻页 + 跨页关键词检索（最多 3 页） | `radio_program_page()` 494、`search_program_pages()` 483、`podcast_more` 1156 | 新增 |
| 7 | 歌词同步偏移 `±3s`，读 `ncm_lyric_offsets.txt` | 1626 附近读文件；`lyric_offset` 1650 | 新增 |
| 8 | 歌词滚动窗口（±4 行） | `lyric_window` 1280、`lyric_window_row` 1667 | 新增 |
| 9 | 歌词上下文 ±2/3 行 | `lyric_context` 1677 | 新增 |
| 10 | 当前句时间 / 总行数 | `lyric_at` 1660、`lyric_total` 1661 | 新增 |
| 11 | 片段试听（循环听当前句，关闭弹窗即停） | `audition` 1268、`audition_stop` 1278、`stop_audition()` 1072 | 新增 |
| 12 | 续播从 2 次改 3 次；播放 8s 后计数归零；可取消 | `recover_stream()` 1030 | 修改 |
| 13 | seek 保持暂停态 | `player.seek(pos, paused=)`（`ncm_audio.py` 68） | 修改 |
| 14 | 下一首音源预热（曲终 30s 请求 URL，10s 内预热解码器） | `prefetch_next_stream()` 1004、`player.prepare()`（`ncm_audio.py` 196） | 新增 |
| 15 | 断流看门狗：不再只看「首帧 18s」，改为「距上次 PCM」 | `ncm_audio.py` 337 | 修改 |
| 16 | 播放中状态发布 200ms → 100ms | 1903 | 修改 |
| 17 | `track_page` 下界从 `len-4` 放宽到 `len` | 1245 | 修改（修正） |
| 18 | 连接诊断 | `deployment` 1657、`diagnostic_recovery` 1670 | 新增 |
| 19 | 运行时换成内嵌 CPython 3.14 + `_hd2runtime.pyd`（原 `ncm_game_watch.py` 已删除） | `runtime/` | 打包方式 |
| 20 | 封面贴图分块生成重写（纯性能） | `cover_texture_packet()` | 不适用 |

Mod 侧（`HD2 Music - Arsenal.zip` 1.2.3 → 1.2.7）同步变化，可见于 `在线版使用说明.txt`（新 4462 字，旧 3585 字）：

- 底栏新增「播放队列」，可**上移、下移、移除、设为下一首**；歌曲搜索可直接加入下一首。
- 频道搜索可翻页；节目筛选可继续向后检索，单次最多三页。
- 设置内新增「**歌词同步**」（网易云/SMTC 各 ±3s，步进 100ms）与「**连接诊断**」（导出到 `%LOCALAPPDATA%\HD2Phone\ncm_diagnostics.txt`）。
- 播放页歌词栏支持滚轮浏览整首、点歌词跳转、校准弹窗循环试听。
- 缩写：播放卡片显示封面与播放状态；频谱位置可选卡片/屏幕底部/两处；长 2D 歌词换行；前奏提前显示后一句。
- 包内新增 `patch_0.stream`（166 KB）与 `Rime-Pinyin-NOTICE.txt`，即拼音输入在 mod 内实现。

## 三、插件侧对照

| 上游能力 | 插件现状 | 证据 |
|---|---|---|
| 队列编辑 | 缺失：队列页只能点歌 | `PlayerView.cs:1017` 队列页、`1941` 仅 `PlayQueueAt` |
| `queue_revision` | 缺失 | 全仓库无匹配 |
| 搜索加入下一首 | 缺失（只有「播放」替换队列） | `Engine.cs:4012` `search_play` 分支 |
| 电台/播客翻页 | 缺失：`limit 20 / offset 0`、`limit 50 / offset 0` 一次性取完 | `NeteaseClient.cs:576`、`:614` |
| 电台搜索接口 | 用旧的 `/api/cloudsearch/pc` type=1009，无 offset | `NeteaseClient.cs:552` |
| 歌词偏移 | 缺失，`ncm_lyric_offsets.txt` 完全不读 | 全仓库无匹配 |
| `lyric_pos` / `lyric_at` / `lyric_total` / `lyric_context` | 缺失 | 全仓库无匹配 |
| 歌词窗口 / 试听 | 缺失 | 全仓库无匹配 |
| 续播 | 2 次（无出声时 1 次）后停 idle，位置丢失 | `Engine.cs:3025` `Recover()` |
| 预热下一首 | 已有（更强）：提前 35s 解码整曲 PCM 并交叉淡入，默认开 | `Engine.cs:204` `_automix = true`、`StreamPlayer.PrepareNext` |
| 断流看门狗 | 只有首帧 18s；**播放中卡住无检测** | `StreamPlayer.cs:830` |
| 发布间隔 | 200ms | `Engine.cs:973` `Thread.Sleep(200)` |
| `track_page` 下界 | `len-4` | `Engine.cs:3112` `SetTrackFirst` |
| 连接诊断 | 缺失 | 全仓库无匹配 |

## 四、跟进建议（按性价比排序）

1. **协议补字段：`lyric_at`、`lyric_total`、`lyric_context`**（~20 行）
   新 mod 播放页右侧要显示「前后歌词」，滚轮浏览也靠上下文行。纯增量字段，风险最低，收益立刻可见。

2. **歌词偏移**（~30 行）
   读 `%LOCALAPPDATA%\HD2Phone\ncm_lyric_offsets.txt` 的 `netease=` 行，钳制 ±3000ms，加到歌词位置；同时发布 `lyric_offset`、`lyric_pos`。
   不做的后果：新 mod 里「设置 → 歌词同步」对插件完全无效，用户会认为功能坏了。

3. **队列编辑 + `queue_revision` + 搜索加入下一首**（~150 行）
   Engine 加 `QueueEdit(index, op)` 四个操作与下标重映射（照上游 `edit_queue` 的写法：先算 `positions`，再 remap index/shuffle/pending），发布 `queue_revision`；PlayerView 队列页放 4 个按钮；命令解析加 `queue_edit`、`search_enqueue`。
   不做的后果：新 mod 底栏队列的四个按钮点下去没反应。

4. **发布节奏 200ms → 100ms（播放/加载中）**（1 行）
   `LoopRun` 改为按 `Player.Active || 有 pending` 选择 100/200。歌词与逐字高亮的顺滑度直接提升。

5. **断流看门狗补「播放中无 PCM 12s」**（~10 行）
   `StreamPlayer` 记 `LastPcmAt`，`Result()` 在非暂停且 `now - LastPcmAt > 12` 时判中断。现在网络半死（TCP 不断、数据不来）会永久卡在 playing。

6. **续播 3 次 + 保留位置 + 可取消**（~40 行）
   把 1/2 次改成 3 次；放弃时不丢 `resume_position`，下次播放从断开处继续（当前会回到 0）。

7. **电台/播客翻页**（~120 行）
   `NeteaseClient` 加 offset 参数（可复用已有 `BrowsePage<T>`），发布 `radio_more/radio_offset/podcast_more/podcast_offset/radio_serial`，RADIO 页加「更多」。

8. **歌词窗口 + 试听**（~200 行，可延后）
   `lyric_window`/`audition`/`audition_stop` 三个命令 + UI。属于播放页增强，不影响基本播放。

9. **连接诊断**（~30 行，按需）
   跟 `diagnostic_recovery`（最近一次续播事件）与 `deployment`（握手状态，1.7.2 起按实际结果写，见第五节）。

**不建议跟进**：内嵌 CPython 运行时、`_hd2runtime.pyd`、TUI/拼音输入、SMTC 的 Next Lyric 标记、3D 歌词与配色、封面贴图分块优化——这些在 mod 侧或 SMTC 程序侧，插件不参与。

## 五、`deployment` 实测（2026-11-28）

**1.7.2 起写该字段**，取值用的就是插件的真实结果，不是伪造：模组活着（心跳或握手任一成立）写 `verified`，否则写 `waiting`。

早先的结论是「不写」，理由是「插件不参与握手，只能写 `waiting`，写进去等于自报验签失败」。这个理由在 1.7.1 实现 Data Hub 之后就不成立了——插件现在**就是**握手方，`verified` 是它可以负责的事实。

取值（用助手自带 `runtime/python.exe` 实跑 `GameWatch.deployment_status()`）：

| 注入状态 | 返回 |
| --- | --- |
| 默认（`session=None`） | `waiting` |
| `session` 设为任意非 None（`{}`、`{"pid":1}`、`object()`） | `verified` |
| `approved` / `token` / `stamp` / `media` / 进程 pid 任意组合（`session` 仍为 None） | 仍 `waiting` |

12 种心跳内容（`0`、`1`、`2`、`1 0`、`1 1`、`1 mission`、JSON、时间戳…）逐一试过，全部 `waiting`：`sample()` 先要 `game_pid()` 在进程表里找到游戏，未启动游戏时直接返回 `None`，心跳内容再变也不影响结果。所以这个值只反映「模组是否已完成与助手的握手并验签」，正常使用中恒为 `verified`；`waiting` 只在助手起来、模组还没握手时出现。

模组侧的证据（`HD2 Music - Arsenal.zip` → `data/9ba626afa44a3aa3.patch_0`）：

- 包里只有一个脚本 `mods/codex/hd2_music`（11 844 436 字节、非字节码）。其中 11 MB 是拼音表和字宽表，代码是混淆壳。
- 壳只有 4 处 `io.open`，全是 `hd2_music_start.dat`（写 token）／`hd2_music_ready.dat`（读回校验）／`hd2_music_data.dat`（读 ≤1 MB 载荷，magic `d5d713be8f498220`）。整个 11.8 MB 里没有 `ncm`、`ncm_state.txt`、`deployment`、`lyric` 这些字面量——真正的状态读取逻辑在运行时解出来的 `startup` 块里（`_b15(_db5,'@mods/codex/hd2_music/startup')`），字符串是加密的，静态取不到。
- 壳的加载是有门的：`on_enable` 通过 `loadstring('return 0x57')==87` 探针后，`on_update` 必须 `_02e()`（写 start、读 ready 比对 token）与 `_72b()`（解析 data.dat）都成功，才会把 `startup` 块 `loadstring` 起来。**助手不运行时模组不会加载自己的主体**，这与《在线版使用说明》「需要 MDL3、Data Hub 和在线助手」一致。
- 使用说明第 38 行把「助手／资源验签／Data Hub／歌词来源／绘制状态」列在「设置 → 模组设置 → 连接诊断」里，`deployment` 的消费点就在这里。

因此插件的取舍：**写实际值**，不猜。1.7.2 的映射是 `GameWatch.Token != null ? "verified" : "waiting"`，也就是「模组存活」的两种证明（1.2.4 的心跳、1.2.6+ 的握手）任一成立即 `verified`。缺省（不写）在诊断页读作「未知」，比一个假的 `waiting` 更糟——`waiting` 会让人以为是握手没成。

想在游戏里复核，用 `.analysis/deployment_toggle.py`：`--pin verified --minutes 3` 在插件播放期间持续把该行钉进 `ncm_state.txt`（插件每 200 ms 重写，所以必须循环补），对比 `--off` 时诊断页那一行与 3D 歌词、2D 歌词、队列编辑是否一致。

## 六、其余存疑

1. **新 mod 对缺字段的容忍度**：1.2.4 明文源码已给出答案——`parse_kv` 只按 `[%w_]+=...` 建表，缺键就是 `nil`，读侧一律 `tonumber(x) or 默认值`，不会报错；1.2.7 正文运行时才交付，但同一套读取风格。逐字段契约见 `docs/mod-reverse-1.2.4.md`。
2. **`audition` 与 `lyric_window` 的 UI 是否在新 mod 里对「不支持该命令的助手」隐藏**：未知。若隐藏则优先级下降。
3. 上游 `search_radios` 换到 `/api/search/get/web` 是否为**必需**（旧接口可能仍可用）：未实测，翻页时才需要换。

## 七、兼容性判断

插件写的是**基础协议子集**（`state/title/artist/pos/duration/volume/lyric/lyric_marks/previous_lyric/next_lyric/lyric_index/lyric_translation/world_*/spectrum/cover_*`），这些字段上游一个都没删、没改语义。新增字段全是**追加**，没有改名。

因此：**新 mod + 旧插件应当可正常播放**，只是新 UI 的部分按钮/列表会没有反应。真正的硬性破坏没有发现。
