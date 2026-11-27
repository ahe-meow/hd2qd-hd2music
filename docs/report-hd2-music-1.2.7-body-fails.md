# HD2 Music 1.2.7 主体加载失败 —— 已定位并修复

**结论：不是模组的问题，是插件交付的 `data.dat` 少了 32 字节解锁头。** 2026-11-28，1.8.0 修好。

## 根因

官方助手的 `authorize()` 返回的媒体字节**不是**包内 `.stream` 的原样拷贝。它用随助手内嵌的 32 字节 `BUNDLED_UNLOCK` 覆盖 `.stream` 的第 8–39 字节，再交给 `reply()` 写入 `hd2_music_data.dat`：

```
原始 stream     166097 B  sha256 8592e8c576df733aca98146f25c17f75fe85b4725058d1d9fd2fe144ffa0b134
助手交付载荷    166097 B  sha256 4d975b059f02fd20de33d1de6de335e73bbf82a529c210742ca7fa6fb93fe9d9
差异            31 字节，全部落在偏移 8..39
偏移 8..39      461433360eb3027dd4713c4…  →  a65cc602ea1313601e04d9bb4d4ad2ee5…
                                              （= BUNDLED_UNLOCK）
```

模组的 `_72b()` 会把 `data.dat` 的第 9–40 字节记进 `_43a[1]`，随后在握手后作为 `_d67(0, _43a[1], …)` 的**解密密钥材料**。插件交原始 stream，密钥就是错的，于是 `_cd1`/`_65e` 解出垃圾、`_6b4`/`_b15` 失败，壳只置 `_1d7` 后静默退出——磁盘和日志都不留痕，看起来像「模组没装」。

1.2.6 的壳不取这段做密钥，所以原始 stream 也能跑。这就是「1.2.6 正常、1.2.7 完全无界面」的全部原因。

## 修复

`src/DataHub.cs` 的 `Known` 表为 1.2.7 钉住：

- `Unlock` = 上述 32 字节
- `UnlockedDigest` = `4d975b05…`

交付前先校验原始 `.stream` 摘要，再克隆字节、把解锁头写到偏移 8，并要求结果命中 `UnlockedDigest`。1.2.6 无 `Unlock`，行为不变。

## 验证

```
红（修复前）：dbcheck … 4d975b05…  → FAIL digest mismatch
绿（修复后）：data.dat : 166097 bytes sha256 4d975b05… / PASS
逐字节：插件交付与助手 authorize() 输出 0 处差异
build.bat：两版全过；Verify.cs 新增「必须钉住解锁头」断言
```

`docs/data-hub-handshake.md` 第十三节有完整推导，包括第三节「交付就是复制」这一错误结论的纠错记录——那次探针把 `gw.media` 手工赋成原始 stream，只能证明 `reply()` 是拷贝函数，不能证明 `authorize()` 的输入就是原始字节。
