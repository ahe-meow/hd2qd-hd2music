# Data Hub 握手：`_hd2runtime.pyd` 逆向结论

对象：`网易云在线助手/runtime/_hd2runtime.cp314-win_amd64.pyd`（430 592 B，PE32+ x86-64，
SHA-256 `25dc01fe118e37d19b3402f2e0cab70bf7859c3461726828ac2702ced8613847`），
Nuitka 以 module 模式编译的 CPython 3.14 扩展（`__code__.co_varnames` 保留，`co_consts` 是占位符）。

## 一、结论

**模组主体不需要解密、不需要重新编码，也不需要联网。** 它是模组包里一个 166 097 字节的
`.stream` 文件，逐字节复制成 `hd2_music_data.dat` 即可。pyd 在这条链上只做三件事：
定位游戏进程、校验包没被篡改、把字节搬运到数据目录。

因此「把 pyd 内嵌进插件」在技术上成立（430 KB + 23.7 MB 运行时），但**没有必要**：
插件用几十行 C# 就能完成同样的交付，零额外体积、零原生依赖。

## 二、握手协议（三个文件，全在 `%LOCALAPPDATA%\HD2Phone\`）

| 文件 | 方向 | 内容 |
| --- | --- | --- |
| `hd2_music_start.dat` | 模组 → 外部 | `<session>:<epoch>:<lua 表地址>`，单行 |
| `hd2_music_ready.dat` | 外部 → 模组 | `start.dat` 的**原样回显**（含结尾换行） |
| `hd2_music_data.dat` | 外部 → 模组 | 4 字节 magic `d5d713be 8f498220` 开头的密文载荷 |

`GameWatch` 的实例字段直接印证这四个路径都从心跳文件同目录派生：

```
heartbeat  ...\HD2Phone\ncm_game_alive.txt
request    ...\HD2Phone\hd2_music_start.dat
response   ...\HD2Phone\hd2_music_ready.dat
payload    ...\HD2Phone\hd2_music_data.dat
```

## 三、实测证据

在隔离的 `LOCALAPPDATA` 下用随包 CPython 3.14.9 直接驱动 `GameWatch`：

1. **载荷来源**
   - `SIGNED_PAYLOAD.stream_size = 166097`，等于 `HD2 Music - Arsenal.zip` 内
     `data/9ba626afa44a3aa3.patch_0.stream` 的字节数。
   - `sha256(该 .stream) = 8592e8c576df733aca98146f25c17f75fe85b4725058d1d9fd2fe144ffa0b134`，
     与 `SIGNED_PAYLOAD.stream_digest` **完全一致**——这份流是被签名覆盖的。
   - `font_files` 三条（962 448 / 240 997 376 / 0）与字库 `09caa46bc556e38f` 的三个文件一一对应。

2. **`reply()` 的输出就是原样拷贝**
   把 `self.media` 设为上述 166 097 字节、`self.session` 设为任意值后调用 `reply()`：

   ```
   hd2_music_data.dat   166097 bytes  delta=+0  same=True  differing bytes: 0
   hd2_music_ready.dat       47 bytes  = "ffffffffffffffffffffffff:1795799176:table: 0x1\n"
   ```

   换成另一个全 `f` 的 session，`data.dat` 仍然逐字节相同（`same=True`）。
   **session 不参与 `data.dat` 的编码**，只出现在 `ready.dat` 的回显里。

3. **签名是真实有效的**
   `verify_signature(base64 文本, SIGNATURE) = False`，
   `verify_signature(base64 解码后的 856 字节 JSON, SIGNATURE) = True`。
   RSA-2048（modulus 512 hex / exponent `010001` / signature 512 hex），签名覆盖 payload 明文。

4. **不联网**
   导入表共 239 个函数：`python314.dll`（CPython API）、`KERNEL32.dll`、`VCRUNTIME140`、
   CRT。**没有** `WS2_32` / `WINHTTP` / `WININET` / `URLMON` / `ADVAPI32` / `bcrypt`。
   二进制里也没有任何 URL 或域名，只有三个 `.dat` 文件名、`LOCALAPPDATA`、`HD2Phone`。

5. **游戏进程定位**
   `game_pid()` 按进程名找 `helldivers2`（把 `python.exe` 改名成 `helldivers2.exe` 能被认出，
   返回其 pid）。`game_data(pid)` 还要求父进程/路径符合，因此伪造进程拿不到数据——
   真实的游戏进程天然满足，对插件无影响。

## 四、插件要做的（替代 pyd，无原生依赖）

```
1. 轮询 %LOCALAPPDATA%\HD2Phone\hd2_music_start.dat
2. 有内容 → 记录文本（这就是要回显的全部）
3. 找  <mods>\HD2 Music - Arsenal*\data\9ba626afa44a3aa3.patch_0.stream
   校验 sha256 == 8592e8c576df733aca98146f25c17f75fe85b4725058d1d9fd2fe144ffa0b134
4. 原子写 hd2_music_data.dat（先 .tmp 再 rename，166 097 B）
5. 原子写 hd2_music_ready.dat（step 2 的原文，一字不改）
6. 删 start.dat，等下一轮
```

要点：

- **必须原子写**。pyd 的 `reply(self)` 里就有一个 `temporary` 局部变量，说明它也是
  「写临时文件再 rename」。模组侧读文件时不会重试。
- **`ready.dat` 一字不改**，连结尾换行都要保留。实测输出 47 字节，等于
  `"<24 字符 session>:<10 位 epoch>:table: 0x1\n"`。
- **校验 digest 再交付**。这是 pyd 里 `SIGNATURE`/`verify_signature` 存在的唯一理由：
  防止有人往库里塞一个被改过的 `.stream`。插件只搬运作者签名保护的字节，不伪造任何东西。
- **`patch_0.stream` 的读取位置**：Arsenal 部署到游戏目录时会把同名 `.stream` 写成 **0 字节**
  （实测 `9ba626afa44a3aa3.patch_90.stream` = 0 B），所以**不能**读游戏目录，
  要读 Arsenal 库目录里的原始包（`<mods>\HD2 Music - Arsenal_*\data\...`）。

## 五、未验证

- 没有在运行中的游戏里跑完整握手，所以「模组收到这份 166 KB 后能正常加载」是推论，
  不是观测。要坐实需要一次游戏内实测（启动游戏 → 启用模组 → 放行插件交付 → 按 F7）。
- 模组侧对 `.stream` 的解密算法在 1.2.7 的壳里（`_72b()`），本次没有逆它——
  插件只搬运、不需要知道密钥。

## 六、1.2.6 里有同一份逻辑的明文（2026-11-28 补）

1.2.7 把 `authorize` 编译进了 pyd，但 **1.2.6 把它加密打包在 `hd2_music_session.bin` 里**，
而解包器 `ncm_session_loader.py` 是明文：

```python
CONTEXT = b'2x\xd1<\x02\x94H\xe6\xe8j%\xc8\xd8)\xc2S'
LAYOUT  = ('cipher', 'nonce', 'tag', 'key')          # key 在最后 32 字节

stream_key = hmac.digest(key, b'stream' + CONTEXT + nonce, 'sha256')
pad        = hmac.digest(stream_key, (offset // 32).to_bytes(8, 'big'), 'sha256')  # 32 B 块 XOR
mac_key    = hmac.digest(key, b'auth'   + CONTEXT + nonce, 'sha256')
tag        = hmac.digest(mac_key, '/'.join(LAYOUT).encode() + cipher, 'sha256')
```

解开后是 7 145 字节的 Python，`authorize(pid)` 全文可读（见 `.analysis/authorize_126.py`）。
它把上面第三节的推论全部坐实，并补上四条之前只能猜的事实：

1. **media 就是 `.stream` 文件本身**（`return manifest['session'], stream.read()`）。
2. **session 来自签名清单**（`manifest['session']`），不是随机数，1.2.6 是
   `4611eedcff5adcb7ba9a36cb`，1.2.7 是 `b47b8bca95c7b43374b34f41`。
3. **签名是 RSA-3072 PSS**（modulus/signature 各 384 B），公钥另有指纹自校验
   `sha256(modulus + b'\x01\x00\x01') == TRUSTED_FINGERPRINT`。
4. **`resource_hashes` 解析的是 Stingray patch**（magic `0xf0000011`，表偏移
   `72 + 32*kinds`，每行 80 B 的 `<7Q6I`），用来确认补丁里那个 resource 的摘要。

顺带暴露了官方助手自己的一个坑：`authorize` 在**游戏目录**里找 `<archive>.patch_*.stream`，
而 Arsenal 部署时把这个文件写成 0 字节，于是 `stream.stat().st_size != stream_size`
直接返回 `None`——交付失败。插件的实现因此先看游戏目录，再退到 Arsenal 库里的原始包。

## 七、实现（1.7.1 起）

`src/DataHub.cs`，约 300 行，纯 C#、无原生依赖：

- 轮询 `hd2_music_start.dat`，校验「有结尾换行 + 至少两个冒号」。
- 候选 `.stream` 顺序：游戏 `data/` → 宿主 `config.json` 的 `mods_dir` 与
  `library_folders` → `Documents\Helldivers2\mods` → 数据目录里的
  `hd2_music_stream.bin` 缓存。
- 只交付 sha256 命中 `Known` 表的字节（1.2.6 的 154 907 / 1.2.7 的 166 097），
  未知 session 也接受——只要摘要可信，补丁升级不会静默断掉交付。
- `Util.AtomicWrite` 写 `data.dat` 与 `ready.dat`，同一请求不重复交付，
  `start.dat` 消失后重置（模组关闭再开会重新交付）。
- **只在 `!IsLockedOut` 时工作**：助手在跑时它拥有握手，插件不抢。

`tests/Verify.cs` 的 `CheckDataHub` 固化了两条清单的格式与存在性，
每次 `build.bat` 都会跑。

## 八、为什么上一轮的结论是错的

上一轮据 `docs/mod-reverse-1.2.4.md` 第三节推断「主体由助手在握手后交付并校验，
属于密码学边界」。方向没错（确实是外部交付），但把「交付」误当成「解密生成」。
这次的 `reply()` 实测把中间那一步消掉了：**交付就是复制**。
签名保护的是「别被换掉」，不是「解不开」。

## 九、握手成了活锁（2026-11-28 实测故障，1.7.2 修）

**现象**：部署 1.2.7 后进游戏，F7 无播放器界面；插件一律提示「请先进入游戏并启用新版播放器」。

**取证**（`%LOCALAPPDATA%\HD2Phone\` 与 `%LOCALAPPDATA%\MDL3\Logs\MDL3.log`，02:14:49 那次启动）：

| 文件 | 观察 |
| --- | --- |
| `hd2_music_start.dat` | 60 B，`b47b8bca95c7b43374b34f41:1795803308:table: 0x2B03B2D1180\n`，02:15:08.428 **只写了一次** |
| `hd2_music_ready.dat` | 60 B，与 start 逐字节相同，02:15:08.529 |
| `hd2_music_data.dat` | 166 097 B，02:15:08.522，sha256 命中 `Known` |
| `ncm_game_alive.txt` | `0\n`，mtime **02:09:23**——整场（02:14:49→02:15:58）没被碰过 |

只写一次 `start.dat` 正说明第一次就握手成功：`_02e()` 每秒重写，一旦 `ready.dat` 回显相等就置 `_7e2 = true` 并停止写。载荷用模组自己的 `_72b()` 解码器复核也是 ACCEPT（42 条目、尾部长度自洽）。**交付这一侧没有问题。**

`ncm_game_alive.txt` 停在 `0` 才是关键。它是模组主体（运行时 `startup` 块）写的：1.2.4 明文里 `online.heartbeat(stopped)` 写 `tostring(stopped and 0 or now)`，`0` = 主体 `on_disable` 过。而 1.2.6 起主体由握手交付，**主体加载不出来就不会有心跳**。

于是两条判据互相等待：

1. 模组要插件交付载荷才加载主体（见第五节 `_b15` 的门）；
2. 插件的 `GameWatch.Sample()` 要「心跳新鲜且非 0」才给出 `Token`，`CheckGame()` 才放行播放。

1.2.4 时代第 2 条成立，是因为主体内嵌在补丁里、不依赖任何人就能 `on_enable` 写心跳。1.2.6 把主体搬到握手之后，这条判据就从「确认游戏在」退化成了**活锁**：插件等一个只有它能解锁的东西。

**修复**（1.7.2）：握手本身也是「模组活着」的证据，而且比心跳更早出现。

- `DataHub.Handshaked`：交付过握手、且 pid 与当前游戏进程一致才算数，换局自动失效。
- `GameWatch.Modern`：由 `Engine.LoopRun()` 每帧从上面那个属性灌入。
- `Sample()` 改为 `beat || handed`——两条证明任一成立都给 `Token`，任一条失效就撤。

**同时改掉那条会误导人的提示。** 原来三种「不能播」共用一句话，用户明明已经在游戏里，却被要求「先进入游戏」。现在分开：

| 情况 | 提示 |
| --- | --- |
| 进程不在 | `请先进入游戏并启用新版播放器` |
| 在游戏里，但模组没来请求载荷 | `游戏已启动，但模组没有向插件请求播放器（请确认 HD2 Music 已在游戏中启用）` |
| 交出载荷后主体没起来 | `播放器资源已交付，游戏内主体尚未就绪（模组加载失败）` |

**仍未确定**：这次故障里主体为什么没加载。握手成功之后，壳的 `on_update` 要依次过 `_cd1()`（用 `font_masks` 里的两段 16 字节派生密钥）、`_65e()`（每帧解 12 个 64 B 块）、`_6b4()`（解压）、`_b15()`（`loadstring`）。`_b15` 里还有一道自检：`loadstring` 前后各取一次 `debug.gethook()`，若变了就直接 `return`（不返回函数、不报错）——MDL3 的 `E_BUDGET` 正是靠指令计数/调试钩子实现的，这条路径会在无日志的情况下永久放弃。要在游戏里证实，得看 MDL3 日志在握手时刻之后有没有 `E_BUDGET`，以及模组是否连 `ncm_state.txt` 都没开始读。

## 十、真正让窗口一句话不说的：锁（2026-11-28 第二次实测，1.7.3 修）

第九节发版后复测，现象照旧：「看不到模组载入」「不能播放」「提示和上次一模一样」。

**取证**：玩家导出的 `%LOCALAPPDATA%\HD2Phone\ncm_diagnostics.txt`（02:33:40）：

```
version=1.7.2.0
locked_out=yes
```

`_lockedOut` 不是提示语层面的开关，它把整条链路都掐了：

| 位置 | 被锁后发生什么 |
| --- | --- |
| `Publish()` | 第一行 `return`——**`ncm_state.txt` 一个字节都不写**，模组读到的是「没有播放器」 |
| `DataHub.Tick()` | 跳过——**不参与握手** |
| `StartPlayAt()` | `_lockedOut` 判断在 `CheckGame()` **之前**就 `return`——按播放毫无反应，`_notice` 也不会被刷新 |

最后一条正是「提示和上次相同」的来源：不是又判断了一次，而是**这条提示从上一个会话冻结到现在**，任何操作都没能覆盖它。

**谁拿走了锁**：`ncm_state.txt` 在 02:35 已不存在，而删除它的唯一位置是 `Dispose()` 里的 `if (!_lockedOut)`——也就是说，**必须存在另一个持有锁的实例**，否则没人会删。同一时刻被用户操作的那个实例却报 `locked_out=yes`。两个实例并存，可见的那个输了。02:31:46 那次成功交付（166 097 B）是持锁实例做的。

`EnsureEngine()` 用静态字段做单例，但静态字段是**每个 AppDomain 一份**：宿主若在两个加载上下文里各起一次插件（或用户开了两个管理器窗口），就会有两个 `Engine`，第二个必然锁失败。

**修复**（1.7.3）：

- `TryLock()` 抽出来，失败后每 2 s 重试；持锁方退出（或那个多余窗口被关掉）本实例自动接回，不需要重启管理器。
- 失败不再泄漏 `FileStream` 句柄（旧代码把流留在打开状态）。
- 持锁时把 pid 写进 `ncm_plugin_owner.txt`；失败方据此说明**是谁**在接管：官方助手（python）还是另一个管理器窗口。
- 锁定时按播放不再静默：`Play()` / `StartPlay()` 会写一条说明持有者的提示。
- 新增 `ncm_plugin.log`（256 KB 上限），`start` / `stop` / `lock acquired on retry` / 每 10 s 一行 `tick locked_out=.. pid=.. beat=.. handed=.. token=.. state=..`。这四个字段能一次分清「没拿锁 / 没看到游戏 / 没心跳 / 没握手」。
- 诊断页补 `lock_holder`、`hub_handshaked`、`hub_note`、`game_pid/heartbeat/token`、`log=` 路径。

**已证**：`.analysis/lockcheck` 打桩——对手占住 byte 0 时 `acquired=False`，放手后同一实例 `acquired=True`，`ncm_plugin_owner.txt` 写出。

**仍未确定**：模组主体为什么不加载。02:31:46 交付成功仍无心跳，说明卡在模组自身的 `_cd1 → _65e → _6b4 → _b15` 链上（第九节末段），与锁无关。下一次复测先看 `ncm_plugin.log` 里 `handed=yes` 与 `beat=no` 是否同时出现：同时出现即主体没起来，属模组侧；若 `locked_out=yes` 反复出现，则是还有第二个实例在抢。

## 十一、交付已证清白，卡点在模组加载器（2026-11-28 第三次实测，1.7.3 日志）

`ncm_plugin.log`（1.7.3 首次带日志运行，02:43:58 起，02:45:01 那次开局）：

```
02:43:58 start version=1.7.3.0 locked_out=no holder=-
02:43:58 tick locked_out=no pid=0     beat=no handed=no  token=no  hub=找不到模组主体…
02:44:18 tick locked_out=no pid=46576 beat=no handed=yes token=yes
02:48:11 tick locked_out=no pid=46576 beat=no handed=yes token=yes state=playing
```

24 条 tick，`locked_out=no` 全程、`handed=yes` 从第一帧起、`beat=no` **一条都没有例外**。即：锁没问题、交付成功、模组主体从未写过心跳。

**交叉验证过的三件事**：

1. 载荷正确。`Known` 表的两条摘要就是从 1.2.6/1.2.7 正式版包内层 `.stream` 核出来的，重新读包结果一致：1.2.6 = 154 907、1.2.7 = 166 097，1.2.3/1.2.4/1.2.5 = **0（主体内嵌）**。交付的 166 097 字节 sha256 命中 1.2.7。
2. 载荷能被模组自己的解码器接受（`_72b()` 42 条目、尾部长度自洽）；`_cd1()` 需要的两段字模（`尖`/`糖`）在 1.2.7 的 `font_masks` 里都在。
3. 主体曾经活过。`ncm_game_alive.txt` 的内容 `0`、mtime 02:09:23 —— `0` 只有主体的 `on_disable` 会写（`online.heartbeat(true)`）。那属于 01:53:29 那次开局，02:09:23 随游戏退出。

**所以 01:53 那次会话主体是加载成功的**；02:11:53 之后同一个部署（`data/9ba626afa44a3aa3.patch_90` 与 `.stream` 的 mtime 停在 11-27 19:56，02:10:40 那次 `add --replace` 只是重新硬链）、同一个插件、同一份载荷，主体再没出现。02:10:40 是唯一变量。

**为什么是模组侧**：握手只是把 `data.dat` 交给壳，壳后面还要 `_cd1()`（派生密钥）→ `_65e()`（每帧 12×64 B 滚动解密）→ `_6b4()`（解压）→ `_b15()`（`loadstring`）。`_b15` 用 `pcall` 包着，**任何失败都只是 `_1d7 = true` 后静默退出**，不写日志、不报错。而 MDL3 3.0.0-dev.57 在 02:45 那次会话里刷了 14 次 `E_BUDGET`、9 次 `SYS_TOO_HEAVY`、4 次 `SYS_WATCH_FAILED`；`loadstring` 一帧读完整个主体在这种预算下被杀，正是「设置里没有入口、F7 无界面、对局无歌词」——入口和 UI 都由主体注册。

**绕开办法（本轮验证过的路径）**：1.2.5 及更早的 `.stream` 是 0 字节，主体内嵌在 `patch_0` 里，**不经过 Data Hub、不经过 `loadstring`**，加载完直接读 `ncm_state.txt`、写 `ncm_game_alive.txt`。插件的状态契约就是按 1.2.4 明文源码写的（`mod-reverse-1.2.4.md`），这条路径是插件最熟的一条。可取出的现成包：`qd-plugins/HD2 Music 1.2.5 - Arsenal.zip`（内层 `9ba626…patch_0` 12 683 104 B、`.stream` 0）。

**1.7.4 的加固**：`start.dat` 超过 120 秒没有刷新就当作上一局的残留，不再回答。换回旧版后，1.2.7 留下的那份请求不会被误答成 `handed=yes`（那样会让 `token` 凭一个没人等待的握手成立）。
