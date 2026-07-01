# IronNestFCS 代码审查报告

> 审查范围：`485994e..HEAD` 最新提交 + 全流程卡死分析
> 审查日期：2026-07-01
> 审查文件：FSC.cs / GunSystem.cs / GameStateWatcher.cs / TriggerConsole.cs / Turret.cs / PurchaseDeck.cs / BallisticCalculator.cs / MapTable.cs / FcsModule.cs / FcsSceneInteractor.cs / TacticalRadar.cs / CoroutineLock.cs / ClickRaycaster.cs / FcsWindow.cs

---

## 一、最新 commit `485994e` 质量评估

### 1.1 改动概要

| 文件 | 行数变化 | 内容 |
|------|----------|------|
| `GunSystem.cs` | +24 | 新增 `EjectChamberedShell()` / `RequestFire()`，`WaitBackToIdle` 加后塞锁 |
| `FSC.cs` | -13 / +4 | 退弹流程改用 EjectChamberedShell，击发改用 RequestFire |

### 1.2 正面评价

- **`EjectChamberedShell`** — 直接调用游戏 API 退弹，替代旧的「装 1 包药 → 开保险 → 击发 → 等回位」流程，省时省药
- **`RequestFire`** — 用 `GunController.RequestFire` 替代 Spinner 击发，更直接可靠
- **后塞锁检查** — `WaitBackToIdle` 新增强制锁紧后塞，防止装填未锁紧导致的异常
- **代码风格一致**，改动范围小且聚焦

### 1.3 问题

| 编号 | 严重度 | 问题 | 影响 |
|------|--------|------|------|
| **C1** | P0 | `EjectChamberedShell` 若 `ArtilleryReloadController == null` 静默不执行 — 错误弹种留在膛内，后续因 `BulletInChamber() != null` 跳过 `LoadBullet`，最终错误弹种被击发 | 轰错目标，浪费炮弹 |
| **C2** | P0 | `RequestFire()` / `WaitForReloadComplete` 调用 `gunController` 无 null 检查 — 场景未加载时 NPE 崩溃 | 进程崩溃 |
| **C3** | P1 | 旧退弹后备方案被彻底删除 — 若 `EjectChamberedShell` 不可用（部分游戏版本无此 API），无任何降级路径 | 兼容性断裂 |

---

## 二、全流程卡死分析

### 2.1 流程概览

```
EnqueueTask → TryDispatch → StartTaskRoutine
  │
  ├─[后台] ReserveTurretAndRotate (抢占炮塔锁 → 转向 → 置 Ready)
  │
  └─[主流程]
       ① 检查膛内弹种 → 不对则退弹
       ② 获取 deskLock → 解算 → 采购弹/药 → 释放 deskLock
       ③ 装填炮弹
       ④ 装填发射药
       ⑤ 等待 CanFire
       ⑥ 升仰角
       ⑦ 等待炮塔 Ready
       ⑧ 确认序列(task→bullet→rotation→elevation→arm→fire)
       ⑨ 释放炮塔锁
       ⑩ WaitBackToIdle
       ⑪ ReleaseSlot → TryDispatch(拉取下一任务)
```

### 2.2 无超时无限循环（6 处）

| 编号 | 严重度 | 位置 | 卡死条件 | 当前最大等待 |
|------|--------|------|----------|-------------|
| **L1** | P0 | `GunSystem.cs:220` — `WaitBackToIdle` 仰角速度循环 | `CurrentElevationSpeed` 不断在阈值上下振荡（精度/震动） | **无超时 → 永久卡死** |
| **L2** | P0 | `FSC.cs:350` — 等待 `CanFire()` | 炮状态异常（如无弹、后塞未锁、游戏 bug）导致 `CanFire` 永不返回 true | **无超时 → 永久卡死** |
| **L3** | P0 | `GunSystem.cs:232` — `WaitFire` 等待 `IsReloading` | `RequestFire` 失败不触发 reload（如状态机拒绝） | **无超时 → 永久卡死** |
| **L4** | P0 | `Turret.cs:30` — `SetRotation` 等待速度归零 | `rotationVelocity` 振荡永不精确归零、或 float 精度问题 | **无超时 → 永久卡死** |
| **L5** | P0 | `CoroutineLock.cs:23` — `Acquire` 等待锁释放 | 另一协程持锁时被异常终止→锁泄漏→本协程永久等待 | **无超时 → 永久卡死** |
| **L6** | P1 | `FcsSceneInteractor.cs:271` — `WaitAndClick` | 达到 3s 超时后无任何错误处理，后续操作在按钮未激活状态下继续 | 3s 超时但静默失败 |

### 2.3 炮管槽位泄漏（5 处）

触发条件：子协程 `yield break` 提前退出，但主流程未 `ReleaseSlot`，炮管被标记"占用"且永远无法被调度器重新使用。

| 编号 | 严重度 | 触发路径 | 位置 |
|------|--------|----------|------|
| **S1** | P0 | 弹仓旋转完毕仍无目标弹种 → `LoadBullet` 的 `yield break` | `GunSystem.cs:140` |
| **S2** | P0 | 装药按钮失效且 `RefreshPowderButtons` 无法修复 → `SelectPowder` 的 `yield break` | `GunSystem.cs:153` |
| **S3** | P0 | 推药杆按钮丢失且重新查找失败 → `LoadPowder` 的 `yield break` | `GunSystem.cs:182` |
| **S4** | P1 | 采购弹药 3 秒超时后弹仓仍无目标弹 → 代码继续到 `LoadBullet` → 失败 → S1 | `FSC.cs:320` |
| **S5** | P0 | `EjectChamberedShell` 静默失败（rc==null）→ 错误弹种留膛 → `LoadBullet` 检测膛内非空跳过 → 击发错误弹种 | `GunSystem.cs:194` |

### 2.4 协程清理与内存泄漏（3 处）

| 编号 | 问题 | 位置 |
|------|------|------|
| **M1** | `_runningCoroutines` 从不移除已完成协程 — 每次任务两个条目（主协程+turret 子协程），长期运行无限增长 | `FSC.cs:248-249,265` |
| **M2** | `AbortGun` 遍历 `_runningCoroutines` 时尝试 Stop 已完成的协程（无害但浪费） | `FSC.cs:129-134` |
| **M3** | `ClickRaycaster.targets` 中 Collider 被 `Object.Destroy` 后 Unity 假 null 行为 — `collider == null` 对已销毁但未回收的对象返回 true，但引用仍保留，列表无限增长 | `ClickRaycaster.cs:47` |

### 2.5 异常/崩溃风险（3 处）

| 编号 | 严重度 | 问题 | 位置 |
|------|--------|------|------|
| **E1** | P1 | `TriggerConsole.TryBind` 中 `buttons.Count != 5` 时只打日志，继续取 `buttons[0]~[4]` → **IndexOutOfRangeException** | `TriggerConsole.cs:29-36` |
| **E2** | P1 | `MapTable.GetMarkTarget` 中 index 检查用 `>` 而非 `>=`，若 index 正好等于 `artilleries.Count` 且字典中存在该 key 则仍可访问（极少触发） | `MapTable.cs:87` |
| **E3** | P2 | `GunSystem.TryBind` 中 `powderController` 可能无子节点，但 for 循环直接遍历 `.childCount`（0 次无害） | `GunSystem.cs:61` |

### 2.6 性能问题（3 处）

| 编号 | 问题 | 位置 |
|------|------|------|
| **P1** | `AdjustAllValves` O(N²) — 对每个 SteamLeak 遍历全部 GameObject 找最近 Dial，大场景卡帧 | `FcsModule.cs:93-116` |
| **P2** | `TacticalRadar` 每次扫描调用 `FindObjectsOfType<GameObject>()` 遍历所有对象（O(N)，且每 3 秒一次） | `TacticalRadar.cs:97` |
| **P3** | `swept` HashSet 永不清理，AutoSweep 长时间运行持续累积内存 | `FcsModule.cs:26` |

### 2.7 逻辑隐患（4 处）

| 编号 | 问题 | 位置 |
|------|------|------|
| **L1** | `ProgressTimeoutMonitor` 检测卡在同一 Progress 状态超过 20s，但若协程卡在 `Acquire()` 等待锁（代码标记了上一个 `MarkProgress`），超时检测**可能有效**——前提是锁等待在 `_deskLock.Acquire()` 之前已经标记了 Progress（当前标记了 `Calculating`）。但如果协程卡在 `_turretLock.Acquire()`（在 `ReserveTurretAndRotate` 中），该子协程的等待**不在** Progress 监控范围内，因为 Progress 标记由主协程控制 | `FSC.cs:409` |
| **L2** | `BulletInChamber()` 依赖 `ChamberedShellBlueprint?.shellDefinition?.ShellId`，若游戏对象的 ShellId 与 `BulletType.ToString()` 大小写不一致（如 `"he"` vs `"HE"`），退弹逻辑会误判需要退弹 | `GunSystem.cs:94-95` |
| **L3** | `ReserveTurretAndRotate` 子协程在 `res.Ready = true` 后检查 `res.Canceled` 并自释放，但主流程在 `while (!turret.Ready) { yield return null; }` (FSC.cs:362) 没有超时 — 若子协程在 `yield return _turretLock.Acquire()` 处卡住（锁被另一任务持有且不释放），主流程此循环也永久卡死 | `FSC.cs:362-364` |
| **L4** | `AutoSweep` 每次 `Update` 遍历 `AliveUnits`，对每个存活单位调用 `swept.Add()` — 若单位不断进出存活状态，`swept` 可能包含同一单位的多个 `EntityLocation` 实例（Pointer 不同），导致重复打击 | `FcsModule.cs:43-57` |

---

## 三、问题汇总

| 类别 | P0 | P1 | P2 | 合计 |
|------|-----|-----|-----|------|
| 卡死(无超时循环) | 5 | 1 | 0 | 6 |
| 槽位泄漏/功能错误 | 5 | 1 | 0 | 6 |
| 内存泄漏 | 0 | 2 | 1 | 3 |
| 崩溃风险 | 2 | 1 | 1 | 4 |
| 性能 | 0 | 0 | 3 | 3 |
| 逻辑隐患 | 0 | 2 | 2 | 4 |
| **总计** | **12** | **7** | **7** | **26** |

---

## 四、修复建议（按优先级）

### P0 — 必须立即修复

1. **所有无超时循环加 timeout**（L1-L5）：
   ```csharp
   // 示例: WaitBackToIdle
   float waited = 0f;
   while (Mathf.Abs(gunController.CurrentElevationSpeed) > 0.01f && waited < 30f) {
       yield return new WaitForSeconds(0.1f);
       waited += 0.1f;
   }
   ```
   每处超时触发后应 `MarkProgress(..., Progress.Failed)` + `ReleaseSlot`，让 `ProgressTimeoutMonitor` 也能兜底。

2. **所有子协程 `yield break` 改为通过返回值通知主流程**（S1-S3）：
   - `LoadBullet` / `LoadPowder` / `SelectPowder` 改为返回 `bool`，失败时主流程 `MarkProgress(Failed)` + `ReleaseSlot`

3. **`EjectChamberedShell` 失败时回退到旧的 dump 方式**（C1, C3, S5）：
   ```csharp
   if (rc != null) {
       rc.EjectChamberedShell();
       // ...
   } else {
       // fallback: load 1 powder + fire
       yield return gunSys.LoadPowder(1);
       // ...
   }
   // 最后验证膛内已空
   if (gunSys.BulletInChamber() != null) {
       MelonLogger.Error("Eject failed, chamber still loaded");
       // mark Failed + ReleaseSlot
   }
   ```

4. **`CoroutineLock.Acquire` 加超时**（L5）：
   ```csharp
   public IEnumerator Acquire(float timeout = 30f) {
       float waited = 0f;
       while (_held && waited < timeout) {
           yield return null;
           waited += Time.deltaTime;
       }
       if (_held) throw new TimeoutException("CoroutineLock Acquire timeout");
       _held = true;
   }
   ```

5. **`_runningCoroutines` 在任务完成/失败时移除**（M1）：
   在 `RunTaskRoutine` 结尾和所有 `yield break` 前清理。

6. **`TriggerConsole.TryBind` 中 `buttons.Count != 5` 后 return false**（E1）：
   ```csharp
   if (buttons.Count != 5) {
       MelonLogger.Error("Can't bind trigger console: expected 5 check switches");
       return false;
   }
   ```

### P1 — 应尽快修复

7. **`WaitAndClick` 返回 bool 指示成功/失败**（L6, C3）

8. **购买弹药超时后标记 Failed 而非继续**（S4）

9. **炮塔 Ready 等待加超时**（L3）

10. **`MapTable.GetMarkTarget` 边界检查**（E2）

11. **`swept` HashSet 定期清理或加容量上限**

### P2 — 计划修复

12. **`AdjustAllValves` 改为缓存 DialInteractable 引用**
13. **`TacticalRadar` 避免每次 `FindObjectsOfType`**
14. **`ClickRaycaster` 清理失效 Collider**
15. **弹种字符串比较改为大小写不敏感**

---

## 五、架构建议

1. **统一错误处理模式**：所有子操作返回 `(bool success, string? error)`，主流程统一检查并在失败时 `MarkProgress(Failed)` + `ReleaseSlot`
2. **超时工具类**：封装 `WaitWithTimeout(Func<bool>, float timeout)` 消除重复的超时样板代码
3. **协程生命周期管理**：`RunTaskRoutine` 应该内部管理 `_runningCoroutines` 的注册和注销，而不是让外部去清理
4. **状态机重构（可选）**：当前 Progress enum 用于日志和 UI，但流程控制靠 `yield break` 隐式分支。改用显式状态机可使每个状态的进入/退出/超时/错误统一处理
