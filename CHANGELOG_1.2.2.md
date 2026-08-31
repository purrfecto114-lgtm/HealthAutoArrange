# HealthAutoArrange v1.2.2 — 互搏根因修复 + Updater 修复 + 发布产物修正

## 一句话总结

v1.2.1 的"互相替代"残余回归的**真正根因**由新建的行为级模拟器定位并修复：`ApplyAnchoredSlots`
把"按 sibling 顺序取的当前位置"当作槽位表（未排序），每轮周期性重排都在**把布局写乱**，
下一轮重建又"修好"，再下一轮又写乱——用户看到的就是两种顺序互相替代。v1.2.2 修复槽位语义、
增加创建时定位与逐帧看门狗，让 mod 顺序在任何代码路径下都持续成立。

## 根因（行为级证据，不是纸面推断）

新建 `HealthAutoArrange.Sim` 行为模拟器：把**真实源码** `UnityUiAdapter.cs` 编译进模拟器，
对着忠实还原反编译逻辑的假游戏（0.5s 定时 → ClearMoodles 延迟销毁 → AddAllMoodles 重建链）逐帧驱动，
在**每种 fake-null 语义**下断言"每一帧结束时 mod 顺序都成立"。

修复前模拟器逐位复现了用户症状（`brokenleg@0, heartstop@70, wet@140, bleeding@210` 的乱序输出），
反推出完整因果链：

1. 重建帧：游戏按自身顺序创建图标（x = 序号×70，sibling 序 = 递增 x）。
2. mod 排序后：位置 = mod 顺序，**sibling 序不再是递增 x**（本来就不该是）。
3. 下一次周期性重排（0.25s）扫描时，`OriginalAnchoredPosition` 按 sibling 序取值 →
   槽位表是"乱序的当前位置集合"，未排序就与计划顺序逐一配对 → **写出乱序布局**。
4. 下一次 0.5s 重建：新节点按游戏顺序创建 → 排序"修好"。
5. 循环往复 → 0.25s/0.5s 两种相位互相覆盖 = "互相替代，频率变低"。

v1.2.1 把频率降低（多挂钩 + 4Hz 兜底）但没有消除：兜底本身就是打乱源之一。

## v1.2.2 修复内容

### 1. ApplyAnchoredSlots 槽位语义修复（根因修复）
槽位集合改为**该行 x 值的升序集合**（`OrderBy(x => x)`）。位置集合不变（仍是同一组槽），
但配对关系正确：mod 想要的第 i 个状态拿到第 i 小的槽位。任何重复排序幂等收敛。

### 2. 创建时定位（消灭"游戏顺序可见窗口"）
`AddMoodle` 后缀在本轮重建进行中立即对当前已创建成员做**增量重排**（仅写 x，
游戏弹入动画的 y/缩放不受影响）。渲染发生在帧末——**游戏的创建顺序从头到尾不会被显示**，
无论重建从哪条路径发起。

### 3. 全边界挂钩（v1.2.1 只挂了 UpdateMoodles 一条）
新增 `AddAllMoodles` 后缀（所有重建路径的最低汇聚边界）与 `ClearMoodles` 后缀（周期重置）。
任何绕过 UpdateMoodles 的重建（现在或未来版本）都会被覆盖。同帧去重防止
UpdateMoodles+AddAllMoodles 双 postfix 重复处理。

### 4. 逐帧位置看门狗（对未知路径的最后防线）
缓存 (rect, 期望x) 计划，每帧校验；任何漂移（未知重定位路径、局部增删、重建）当帧触发全量重排
（60fps 下 ≤16ms 内纠正）。稳态开销约为每帧十几次空引用检查与浮点比较。

### 5. 幽灵防御（三处）
- AddMoodle 空跑检测（`chippedOnly && unchipped` 不创建节点时，不再把上一轮的
  待销毁节点记入 fresh-set）；
- 重建帧扫描门：无 fresh-set 过滤时拒绝扫描同帧重建中的层级（销毁延迟使 fake-null 不可靠），
  杜绝幽灵被当成活成员造成槽位重复；
- 跨帧 stale fresh-set 自动失效。

### 6. Updater 修复（jsDelivr 镜像自动回退）
`raw.githubusercontent.com` 在很多网络（尤其无代理的国内网络）不可达 → 检查 10 秒后失败，
用户以为"updater 没工作"。现在自动依次尝试：GitHub 原始 → cdn.jsdelivr.net → fastly.jsdelivr.net
（镜像本仓库 update-dist 分支，已验证可匿名拉取）。清单源白名单相应扩展；
**release 页面链接仍仅限 github.com**；updater 依旧只做通知，不下载安装任何文件。
F8 更新面板可见每级回退过程。

### 7. Release 产物修正
- CI `release.yml` 现在只产出**一个** zip：`HealthAutoArrange-<版本>-v701.zip`（demo 7.0.1 目标）。
  移除了旧版同时出现的无后缀 stub 构建 zip 与散装 DLL（zip 内已含两个 DLL）。
- 资产列表干净：`HealthAutoArrange-x.y.z-v701.zip` + `latest.txt`（游戏内更新清单）。

## 诊断（F9）
新增 `v1.2.2 stats` 行：`MoodlesClearedCount / CreationTimePositionCount / WatchdogCorrectionCount /
Frame / LastClearFrame / LastFinalizeFrame / WatchdogPlanSize`，
加上补丁触发计数（AddAllMoodlesPostfix/ClearMoodlesPostfix）。任何一次实机回归都可以用它
一眼定位发生在哪条路径。

## 验证矩阵（"测试通过 ≠ 能用"的回应）

| 验证 | 结果 |
|---|---|
| 行为模拟器（真实 Adapter 源码 × 忠实游戏循环 × 逐帧断言 × 2 种 fake-null 语义 × 7 场景） | **14/14 PASS** |
| 模拟器回归证明（修复前） | 复现用户症状：逐帧乱序/互搏输出 |
| xUnit 单元测试 | 213/213 PASS |
| 静态契约检查 | 103/103 PASS |
| 版本一致性 | Plugin=1.2.2 / Assembly=1.2.2.0 [PASS] |
| 构建（真实 7.0.1 公开化引用） | 0 警告 0 错误 |
| 反编译核对 | 槽位升序排序、看门狗、创建时定位、双后缀、镜像链均已编译进 DLL |

模拟器场景覆盖：稳态重建循环、状态增删时间线、side moodle + "+N" 子物体、
AddMoodle 空跑、外部位置攻击（模拟未挂钩的游戏重定位路径）、周期外零散添加、
场景切换 manager 替换。全部在"立即 fake-null"与"延迟 fake-null（幽灵不可辨）"两种引擎语义下通过。

## 兼容性

- Casualties: Unknown **Demo 7.0.1**（Steam build 24774057；当前最新 demo，NuGet GameLibs 与
  社区交叉确认）。v7.0.1 运行时方法体无公开渠道（Steam 独占），行为假设基于 v6.1 反编译 +
  全边界挂钩 + 逐帧看门狗三层防御，对"未知路径"保持鲁棒。
- BepInEx 5.4.23.5 / Harmony 2.x，net472 插件 + netstandard2.0 核心。

## 安装

1. 安装 BepInEx 5.4.23.5（游戏为 Unity Mono，x64）。
2. 解压 `HealthAutoArrange-1.2.2-v701.zip` 到游戏根目录（DLL 落入 `BepInEx/plugins/HealthAutoArrange/`）。
3. F8 设置窗口 / F9 诊断 / Ctrl+R 立即重排 / Ctrl+E 快速开关。
