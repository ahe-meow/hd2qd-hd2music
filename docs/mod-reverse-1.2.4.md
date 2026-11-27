# HD2 Music 模组逆向：1.2.4 明文源码 + 1.2.7 加载壳

对象：`HD2 Music - Arsenal.zip`（`data/9ba626afa44a3aa3.patch_0`，12 760 768 字节；`data/09caa46bc556e38f.patch_0`，962 448 字节，字库）。
目的：把「模组到底读什么、怎么读、缺字段会怎样」从猜测变成可引用的结论，并据此校对插件。

## 一、包结构与取得源码的路径

`patch_0` 是 Stingray 的补丁包（头 `110000f0`，`0x657` = 1623，偏移 24 处是包长），里面只有一个 Lua 脚本：

| 版本 | 脚本条目 | 大小 | 形态 |
| --- | --- | --- | --- |
| 1.2.4 | `mods/codex/hd2_music` | 12 748 770 | **明文 Lua，带注释与可读变量名** |
| 1.2.5 – 1.2.7 | 同上 | 11 844 436（1.2.7） | 混淆壳（`_padding` + `loadstring` 门） |

1.2.4 的正文起点是包里第一处 `-- HD2-Addon: mods/codex/hd2_music`（偏移 91 704），结尾是最后一处 `return mod`。切出来 75 822 行，`local CFG = {...}`、`function online.step()` 都可读——本文所有「模组侧」结论都来自这份源码（`1.2.4`，正文里 `version = '1.2.3'` 是作者漏改的字符串）。

1.2.5 起作者加了壳。1.2.7 的正文不在脚本里，而在 `data/9ba626afa44a3aa3.patch_0.stream`
（166 097 字节密文，由外部交给数据目录后由壳解密，见第三节），所以 1.2.7 的字段语义用
1.2.4 源码 + 1.2.7 助手的发布逻辑交叉确认。

## 二、1.2.4 源码给出的协议契约

### 状态文件 `ncm_state.txt`

解析器（`parse_kv`）：按 `\n` 切行，`^([%w_]+)=([^\r]*)`，键只允许字母数字下划线。**缺键 = nil，不报错**，读侧一律 `tonumber(x) or 默认值` 或 `or ''`。这就是「缺字段不会崩」的源码依据。

| 键 | 模组怎么用 | 约束 |
| --- | --- | --- |
| `time` | `online.active = |os.time() - time| < 25` | 秒级 epoch，必须持续刷新 |
| `state` | `playing` / `paused` / `buffering` / `loading` 分支；其它值等于「什么都没发生」 | 卡片、歌词、HUD 都靠它 |
| `account` `notice` `account_notice` | 直接显示 | `Clean` 后不得含控制字符 |
| `playlist` `title` `artist` | 标题栏；`title==''` 时卡片不画 | |
| `index` `count` | 1 基；`count` 用于队列滚动夹取 `max(1, count-4)` | |
| `pos` `duration` | 秒；卡片会按 `sample_at` 外推最多 0.25 s | 拖动后靠它回正 |
| `volume` | `math.floor(v+.5)`，0–100 | `vol` / `vol_set` 的落点 |
| `lyric` `next_lyric` `previous_lyric` `lyric_translation` | 2D/3D 歌词 | 空串等于「没有」 |
| `lyric_index` `lyric_at` `lyric_total` | 歌词面板的滚动定位 | |
| `lyric_marks` | `(%d+):(%d+)` 逐字：`毫秒:字节尾`，逗号分隔也能匹配 | 字节偏移基于**同一行** `lyric` 的 UTF-8 |
| `lyric_prefix` | 已唱部分的整段文本 | |
| `lyric_offset` | 毫秒，±3000 | 由 `ncm_lyric_offsets.txt` 覆盖 |
| `lyrics_on` | `'0'` / `'1'` | `'0'` 时 HUD 与卡片都不画 |
| `seek_ready` | `'1'` 才允许拖进度 | |
| `seek_serial` | 与 `list_id`、`index` 一起组成拖动键 `seek_key` | 变化即放弃旧的拖动预览 |
| `quality` | 与 `standard/higher/exhigh/lossless/hires` 精确比较 | 大小写敏感 |
| `order` | `order` / `shuffle` / `repeat`，映射到顺序/随机/单曲 | 其它值回落到顺序 |
| `list_id` `track_first` | `track_page <list_id> <target>` 的键；`target` 夹到 `[1, max(1,count-4)]` | |
| `world_timing` | `'1'` 才读 `world_*` | |
| `world_timed` | `role\tindex\tat\ttext\ttranslation`，`role ∈ {previous,next}` | 逐字/定时预告 |
| `world_cue` | `role\tindex\ttext\ttranslation` | |
| `world_expiry` | `index\tat`，与 `world_cue` 配对 | 到点撤回 |
| `list` | `id\t名字`，id 必须是数字 | 我的歌单卡片 |
| `list_art` | `id\t分块串` | 与 `list` 配对 |
| `recommend` | `id\t名字\t分块串`，**名字至少一个字符** | 空名字整行被丢弃 |
| `search_result` | `序号\t歌名\t歌手` | |
| `radio` | `id\t名字\t描述`，**名字至少一个字符** | 空名字整行被丢弃 |
| `podcast_episode` | `id\t名字\t秒数`，秒数必须是整数 | `(%d+)` |
| `*_key` | `([a-z]+)_key=(%d+)\t([a-f0-9]{64})`；带下划线的键（`cover_texture_key`）不走这条 | 分块贴图的键 |
| `qr` | 一整行内以非 `01` 分隔的方阵行；行数 ≤57 且每行等长 | 行内不能有 `=`（会被 `parse_kv` 切断） |
| `cover` | 72×72×6 个十六进制字符 | 贴图未就绪时的色块封面 |
| `cover_tiles` | 分块串 | |

`state` 之外的写入纪律：每帧全量重写（模组每 0.2 s 读一次），写完必须 `\n` 结尾；`ncm_state.txt` 被删除时模组退回「助手未连接」，并在 3 秒内保留上一次状态用于过渡。

### 频谱 `ncm_spectrum.txt`

每 40 ms 读一次，**整棵文件不超过 120 字节**：`time=<epoch>`、`serial=<seek_serial>`、`bands=<32 个十六进制字符>`。`serial` 必须等于状态里的 `seek_serial`，否则整帧丢弃；`|os.time()-time| >= 2` 也丢弃。16 个柱，`hex/255` 是幅度。

### 命令 `ncm_cmd_*.txt`

文件名 `ncm_cmd_<epoch>_<6 位序号>.txt`，ASCII，一行，`\n` 结尾，45 秒内有效，最多 64 个；`playlist`/`track_page` 只认最后一个。命令与参数（1.2.4 会发出的）：

| 命令 | 参数 | 备注 |
| --- | --- | --- |
| `playlist` | id | 读整个歌单进队列 |
| `track_page` | list_id first | 请求队列的可见页 |
| `track_play` | list_id index | 1 基 |
| `search` | UTF-8 十六进制 | 模组的拼音输入转字节再转 hex |
| `search_more` | serial | serial 必须与状态一致 |
| `search_play` | serial index | |
| `search_clipboard` | – | 取剪贴板 |
| `radio` | [refresh] | 空参数取首屏，`refresh` 重拉 |
| `radio_search` / `radio_clipboard` | hex / – | 频道名搜索 |
| `radio_play` | channel id | |
| `podcast` | channel id | |
| `podcast_filter_clipboard` / `radio_clipboard` | – | 同一个搜索框按页面分流 |
| `list_art` | 可见歌单 id（空格分隔，≤8） | 请求这几张卡片的分块贴图 |
| `lyrics` | 0 / 1 | 开关 |
| `seek` | `list_id index serial pos(3 位小数)` | 空格分隔，整体作为一个参数串 |
| `vol` | ±5 | 每次 5 个百分点 |
| `vol_set` | 0–100 | |
| `order` | order / shuffle / repeat | 循环切换 |
| `quality` | standard…hires | |
| `daily` `fm` `recommend` `recommend refresh` `login` `cancel` `refresh` `toggle` `next` `prev` `stop` | | |

### 模组写出的文件

| 文件 | 内容 | 谁读 |
| --- | --- | --- |
| `ncm_game_alive.txt` | `os.time()\n`，退场写 `0\n` | 助手的 `GameWatch`（判断游戏在不在、换没换进程） |
| `ncm_lyric_offsets.txt` | `netease=<ms>\nsmtc=<ms>\n`，±3000 | 助手发布前套用 |
| `ncm_player_appearance.txt` `ncm_lyric_colors.txt` `ncm_lyrics_enabled.txt` `ncm_order.txt` `ncm_quality.txt` `ncm_automix.txt` | 模组设置 | 助手 / SMTC |
| `ncm_3d_trace_motion.txt` | 3D 歌词轨迹调试 | 无人读（诊断用） |

## 三、1.2.7 的加载壳（正文为什么是密文）

1.2.7 的 11.8 MB 里：11 MB 是拼音表与字宽表（`pinyin_words` / `font_metrics` / `font_masks`），可执行部分是尾部约 45 KB 的壳。壳只做四件事：

1. `on_enable`：用 `loadstring('return 0x57')` 探针确认宿主没换掉 `loadstring`，通过后置 `_429=true`。
2. `on_update`：先 `_02e()`——往 `HD2Phone\` 写 `hd2_music_start.dat`（`<28 位 session>:<epoch>:<表地址>`），再读 `hd2_music_ready.dat` 要求原样回显；再 `_72b()` 解析 `hd2_music_data.dat`（≤1 MB，magic `d5d713be8f498220`，40 字节头 + 若干条目，条目带 12 字节 id、16 字节签名、32 字节键、16 KB 上限载荷）。
3. `_d67 = _65e(...)`：把条目载荷解成 `startup` 块，`_b15(_db5, '@mods/codex/hd2_music/startup')` 起 `loadstring`，再把 `mod`、状态表、拼音表、字宽表、贴图掩码、键一起传进去。
4. `on_disable`：清状态。

结论：**模组主体（播放器窗口、歌词绘制、设置页、诊断页）在 1.2.7 的包内**，只是不在脚本里——
它是 `data/9ba626afa44a3aa3.patch_0.stream` 这个 166 097 字节的密文流。外部把它原样复制成
`%LOCALAPPDATA%\HD2Phone\hd2_music_data.dat`、再把 `hd2_music_start.dat` 回显成
`hd2_music_ready.dat`，模组侧的 `_72b()` 自己解密。外部不运行 → `_02e()` 拿不到 `ready.dat`
→ `on_update` 直接返回 → 主体不加载。完整协议、实测证据与替代实现见
[`data-hub-handshake.md`](data-hub-handshake.md)。

**本节的早期版本写错了**（2026-11-28 修正）：曾写作「主体不在包内、由助手在握手后解密生成
并校验，属于密码学边界」。对 `_hd2runtime.pyd` 的 `reply()` 实测表明交付是**逐字节复制**
（166 097 字节，0 处差异，换任意 session 结果不变），签名保护的是「别被换掉」，不是「解不开」。

**仍未确定**：`deployment` 在 1.2.7 诊断页上的具体呈现。

## 四、据此对插件的修正

已修（1.7.0）：

1. `account_loading` 从「只在搜索块里发」改为**每帧都发**（助手 `publish()` 是无条件写的）。
2. `recommend=` / `radio=` 的名字为空时回落到 id：模组的行模式要求名字至少一个字符，空名字会让整张卡片消失而不是显示空白。

核对通过、未改：

- `quality`（`hires` 不是 `Hi-Res`）、`order`（`order/shuffle/repeat`）、`state` 四种取值、`seek_ready/seek_serial/seek_key`、`track_page` 的夹取、`vol`/`vol_set` 语义、`search <hex>` 的编码、`lyric_marks` 的「毫秒:字节尾」、`world_*` 的角色与过期、`qr` 的方阵（`/` 分隔、4 模块留白）、频谱 32 hex + `serial` 绑定、封面 360/200/80 与 72×72 色块、`ncm_lyric_offsets.txt` 的 ±3 s 偏移、`ncm_game_alive.txt` 心跳。
- 命令表：插件 41 条覆盖助手 39 条（多出的是 `automix`、`podcast_filter` 两条插件自有命令），模组会发的命令全部有处理。

遗留（不影响播放）：

- `deployment` 仍不写（见 `docs/upstream-1.2.7-parity.md` 第五节）。
- 1.2.7 的诊断页字段（`deployment` 的呈现方式、`诊断页` 的其它行）无法在包内验证。
