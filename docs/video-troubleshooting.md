# 视频链路排障记录（Video Pipeline Troubleshooting）

本文记录从「客户端视频黑屏 / 反复重启」到最终出图的完整排障过程，按问题被发现的**时间顺序**整理。每个问题包含：现象、排查手段、根因、修复。

架构背景见 [video-implementation.md](./video-implementation.md)、协议见 [video.md](./video.md)、问题清单见 [video-issues.md](./video-issues.md)。

> 多进程架构简述：`Alliance.Client`（主进程/supervisor）启动独立子进程 `Alliance.VideoWorker`；Named Pipe 传状态（心跳），Memory-Mapped File 传 BGRA 解码帧；Worker 崩溃由 supervisor 指数退避重启。

---

## 问题总览

| # | 现象 | 根因 | 关键修复 |
|---|------|------|---------|
| 1 | `[FATAL] Unobserved task exception: Pipe is broken / Operation canceled` | `WaitForConnectionAsync().WaitAsync(timeout)` 遗弃底层连接任务，超时后被 dispose 变孤儿任务 | linked CTS + `CancelAfter`，直接 await |
| 2 | Worker 输出被吞、崩溃原因不可见 | `DrainStreamAsync` 丢弃 stdout/stderr；`loggerFactory` 未 flush | 转发到主日志 + `using` flush |
| 3 | `TimeoutException: worker did not connect within 5s` 反复刷 | FFmpeg 大版本不匹配：AutoGen 7.1.1(major 61) vs 系统 FFmpeg 8(avcodec 62)，`avcodec_find_decoder` 未定义符号秒退 | 升级 AutoGen 8.1.0 + 动态绑定 + 目录探测 |
| 4 | `FileNotFoundException: FFmpeg.AutoGen 8.1.0.0` | copy target 只拷 3 个写死文件，缺 `FFmpeg.AutoGen.dll` | glob 拷贝 Worker 整个输出目录 |
| 5 | 有 fps 显示但 UI 无画面 | `VideoFeedControl` 只在 Store 实例变化时重绘，帧内容更新不触发 | Store 加 `FrameUpdated` 事件，控件订阅后 `InvalidateVisual` |
| 6 | `state=Connecting pkts=0` 持续，WAITING FOR STREAM | 孤儿 Worker 霸占 UDP:3334 + `Task.WhenAll` 不响应单任务故障 | `WhenAny`+cancel；`PR_SET_PDEATHSIG` 防孤儿 |

---

## 问题 1：孤儿任务未观察异常（Unobserved Task Exception）

**现象**
```
[FATAL] Unobserved task exception: System.AggregateException ... (Pipe is broken.)
[FATAL] Unobserved task exception: ... SocketException (125): Operation canceled
   at ...NamedPipeServerStream...WaitForConnectionAsync...
```

**排查**
- grep 定位到唯一的 server 端管道：`VideoSupervisorService.cs` 的 `WaitForConnectionAsync`。
- 异常在终结器线程抛出 → 说明有 Task 故障后无人 await/观察。

**根因**
```csharp
await pipeServer.WaitForConnectionAsync(ct).WaitAsync(TimeSpan.FromSeconds(5), ct);
```
`Task.WaitAsync(timeout)` 只停止「等待」，不取消底层操作。超时/取消后 `using var pipeServer` 被 dispose，而原始 `WaitForConnectionAsync` 任务仍在后台运行 → 管道被销毁 → 该任务 fault（`Pipe is broken` / `Operation canceled`）→ 无人观察 → 终结器 rethrow。

**修复**（`VideoSupervisorService.cs`）
```csharp
using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
{
    connectCts.CancelAfter(TimeSpan.FromSeconds(5));
    try { await pipeServer.WaitForConnectionAsync(connectCts.Token); }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    { throw new TimeoutException("Video worker did not connect within 5 seconds."); }
}
```
直接 await（异常始终被观察）；`CancelAfter` 真正取消底层操作；超时转 `TimeoutException` 走重启，外部停止则透传退出。

顺带修复 M2：`DrainStreamAsync` fire-and-forget → 保存 drain task 并在 `finally` 中 `await ... WaitAsync(3s)`。

---

## 问题 2：Worker 崩溃原因不可见（诊断能力缺失）

这是**贯穿全程的关键使能项**——若无此修复，后续问题 3/4/6 都只能靠 `LD_DEBUG`、`ss`、`strace` 等外部手段盲猜。

**根因**
- `VideoSupervisorService.DrainStreamAsync` 把 Worker 的 stdout/stderr 读出后**直接丢弃**。
- `Alliance.VideoWorker/Program.cs` 的 `loggerFactory` 未 `Dispose`，`AddSimpleConsole` 异步队列在进程秒退时来不及 flush → 日志丢失。

**修复**
- `DrainStreamAsync`：stdout→`LogInformation`、stderr→`LogWarning`，统一加 `[worker]` 前缀转发到主日志。
- `Program.cs`：`using var loggerFactory = ...` 保证退出前 flush。

**效果**：此后 Worker 的一切错误（FileNotFound、FFmpeg、Bind 失败等）直接出现在主进程日志，排障从「黑盒」变「白盒」。

---

## 问题 3：FFmpeg 大版本不匹配（Worker 秒退）

**现象**：主进程反复 `TimeoutException: worker did not connect within 5s` + `worker will restart`。

**排查**
1. 手动跑 Worker：`dotnet Alliance.VideoWorker.dll <base64 payload>` → `EXIT=1` 但无日志（因问题 2 尚未修，异步日志丢失）。
2. `LD_DEBUG=libs` 观测动态加载器：
   ```
   dotnet: error: symbol lookup error: undefined symbol: avcodec_find_decoder (fatal)
   ```
3. 版本比对：

| 组件 | AutoGen 7.1.1 期望 major | 系统 `/usr/lib` 实装 |
|------|------|------|
| libavcodec | 61 | **62** |
| libavutil | 59 | **60** |
| libswscale | (8) | **9** |

**根因**
- `FFmpeg.AutoGen 7.1.1` 绑定 FFmpeg 7.x 符号，系统是 FFmpeg 8.0 → 符号版本对不上，native fatal，进程在 `avcodec_find_decoder` 处秒退。
- 且旧代码只设 `ffmpeg.RootPath = "/usr/lib"` 却**未调 `DynamicallyLoadedBindings.Initialize()`**，`RootPath` 实际未生效。

**修复**（`WorkerRuntime.cs`、`Alliance.VideoWorker.csproj`）
- 升级 `FFmpeg.AutoGen` `7.1.1 → 8.1.0`（绑定 major 62，匹配系统）。
- 新增 `FfmpegLoader.EnsureInitialized()`：定目录 → 设 `RootPath` → `DynamicallyLoadedBindings.Initialize()`。
- **目录探测（可移植）**：环境变量 `ALLIANCE_FFMPEG_ROOT` → 扫描 `/usr/lib`、`/usr/lib/x86_64-linux-gnu`、`/usr/lib64`、`/usr/local/lib` 找含 `libavcodec.so.62` 的目录；找不到抛清晰异常（期望 major / 已搜目录 / 实际发现版本 / 补救建议）。
- FFmpeg 8 弃用 API：`ffmpeg.SWS_BILINEAR` → `(int)SwsFlags.SWS_BILINEAR`。

**注意事项**：AutoGen 版本在编译期锁定 FFmpeg 大版本，无法运行时适配不同 major。换机器需保证装有 FFmpeg 8.x，否则 `FfmpegLoader` 会给出明确报错。

---

## 问题 4：缺 FFmpeg.AutoGen.dll（打包遗漏）

由问题 2 的日志转发暴露：
```
[worker] fail: FileNotFoundException: Could not load file or assembly 'FFmpeg.AutoGen, Version=8.1.0.0'
```

**根因**
`Alliance.Client.csproj` 的 `CopyVideoWorkerOutput` target 只拷贝 3 个写死文件（Worker dll/deps.json/runtimeconfig.json），**没拷 `FFmpeg.AutoGen.dll`**。之前用 7.1.1 时被 native 崩溃（问题 3）掩盖；版本修对后，托管加载器先报此依赖缺失。

**修复**（`Alliance.Client.csproj`，顺带 M4）
- copy target 改为 glob 拷贝 Worker `bin/$(Configuration)/net10.0/**/*` **整个输出目录**，自动带上所有依赖。
- 移除 `ContinueOnError="WarnAndContinue"`：Worker 构建失败即主构建失败。
- 先 `Exec` 构建 Worker，再 glob 采集，保证拷到最新产物。

---

## 问题 5：有 fps 无画面（渲染不刷新）

**现象**：Worker 正常解码、fps 文本更新，但视频区域始终空白。

**排查**
- fps 文本走 `{Binding Video.Snapshot.MetricsText}`（`ObservableObject` 通知）能刷新。
- `VideoFeedControl` 是自绘控件，`OnPropertyChanged` 只在 `StoreProperty`（Store 实例）变化时 `InvalidateVisual()`。

**根因**
`Store` 实例启动后不再变；`UpdateFrame` 更新的是 `WriteableBitmap` **像素内容**，不发任何属性通知 → 控件 `Render()` 此后再不被触发。

**修复**（`VideoStreamStore.cs`、`VideoFeedControl.cs`，即 N1）
- `VideoStreamStore` 新增 `event Action? FrameUpdated`，`UpdateFrame`/`ClearFrame` 写完像素后触发（调用方已在 UI 线程）。
- `VideoFeedControl` 在 `StoreProperty` 变更时订阅/退订 `FrameUpdated`，事件到达即 `InvalidateVisual()`（`CheckAccess` + `Post` 兜底）；`OnDetachedFromVisualTree` 退订防泄漏；引用比较防重复订阅。
- 顺带修 stride 对齐：改用 `locked.RowBytes`，`RowBytes != FrameWidth*4` 时逐行拷贝防花屏。

---

## 问题 6：孤儿 Worker 占用端口 + Bind 失败静默挂起

**现象**：`[video-diag] state=Connecting pkts=0` 持续刷屏，心跳一直在发但永不 Ready，WAITING FOR STREAM。

**排查（关键靠新增诊断日志）**
- 新增 `[video-diag]`（主进程每秒打印心跳分级计数）与 `[assembler-diag]`（Worker 每秒打印分片丢弃分支计数）。
- `ss -lunp | grep 3334` 发现一个 **34 分钟前**的旧 Worker（`ppid=1` 僵尸）霸占 UDP:3334。
- `ss -uanp` 看 `Recv-Q` 曾积压 53504 字节——是那个僵尸在收包，不是新进程。
- 系统 UDP 计数 `/proc/net/snmp` 的 `InDatagrams` 2 秒增长约 1600（有流），但新 Worker 收不到。

**根因（两个 bug 叠加）**
1. **孤儿进程**：`App.axaml.cs` 只在 `desktop.Exit`（优雅退出）时调 `StopAsync → TryTerminate`。`dotnet run` 被 Ctrl-C/SIGKILL 时 Worker 不被清理，变 `ppid=1` 僵尸继续占端口。
2. **`Task.WhenAll` 不响应单任务故障**：`WorkerRuntime.RunAsync` 的 `await Task.WhenAll(heartbeatTask, receiveTask)`。新 Worker `socket.Bind(3334)` 抛 `SocketException(98)` 后 `receiveTask` 故障，但 `heartbeatTask` 无限运行 → `WhenAll` 永不返回 → Worker 卡 `Connecting`、错误不暴露、supervisor 也不重启。反向：父进程死后管道断开使 heartbeat 故障，但 receive 永不退出 → Worker 成僵尸。

**修复**（`WorkerRuntime.cs`、`Program.cs`、新增 `ParentDeathGuard.cs`，即 C6）
- `RunAsync`：`Task.WhenAll` → `Task.WhenAny` + `_cts.Cancel()`，任一循环故障即取消另一个并退出；`completed.IsFaulted` 时 await 重抛（错误经 `Program.cs` catch 暴露）。
- 新增 `ParentDeathGuard`：Linux `prctl(PR_SET_PDEATHSIG, SIGTERM)`，父进程死亡时内核自动向 Worker 发 SIGTERM；并处理「启动前父已死」（`getppid()==1`）竞态。
- `Program.cs` 启动即 `ParentDeathGuard.Enable()`。

**验证（构造 pipe-host 集成测试模拟 supervisor）**
- 端口冲突：Worker 打印 `SocketException (98): Address already in use` 并快速退出（不再挂起）。
- 孤儿防护：SIGKILL 父进程后，Worker 被 PDEATHSIG 终止，3334 自动释放。

---

## 排障工具箱（本次用到的只读诊断手段）

| 工具 | 用途 |
|------|------|
| `LD_DEBUG=libs dotnet ...` | 观测 native 库加载 / 未定义符号（定位问题 3） |
| `ss -lunp \| grep 3334` | 谁在监听 UDP 端口（定位问题 6 孤儿进程） |
| `ss -uanp` `Recv-Q` | UDP socket 接收队列积压，判断是否真在收包 |
| `grep Udp: /proc/net/snmp` | 系统级 UDP 收包计数增量，判断是否有流进本机 |
| `ps -eo pid,ppid,etime` | 找 `ppid=1` 的孤儿进程 |
| `nm -D / objdump -T` | 确认 `.so` 是否导出目标符号 |
| `[worker]` / `[video-diag]` / `[assembler-diag]` 日志 | 应用内建的白盒诊断（本次新增） |
| 手动跑 Worker + `EXIT=$?` | `EXIT=124`=挂在等管道（初始化通过）；`=1`=未捕获异常 |

## 常驻诊断日志说明

- **`[video-diag]`**（主进程，每秒）：`state note pkts assembled decoded presented decodeErr fps lastPacket lastFrame`。分级定位断点：pkts↑/assembled=0=组装问题；assembled↑/decoded=0=解码问题；decoded↑/无图=渲染问题。
- **`[assembler-diag]`**（Worker，每秒）：`lastLen lastFrameId lastFragIdx lastTotalBytes lastExpectedFrags pending | drops: tooShort badTotal fragOOR totalMismatch offsetOOR expired completed`。各 drop 计数直接对应 `HevcFrameAssembler.TryAddPacket` 的丢弃分支。

## 复现验证清单

```bash
# 1. 全量构建
dotnet build Alliance.sln

# 2. 单测
dotnet test        # 期望 14 全绿

# 3. Worker 独立冒烟（端口空闲时，期望 EXIT=124 挂在等管道）
MMAP=/tmp/av.mmap; truncate -s 24883520 "$MMAP"
JSON='{"sharedMemoryPath":"'"$MMAP"'","statusPipeName":"t","udpPort":3334,"frameWidth":1920,"frameHeight":1080,"expectedFps":60,"presentFps":60,"frameAssemblyTimeoutMs":50,"heartbeatIntervalMs":250,"signalLostAfterMs":500,"clearFrameAfterMs":2000}'
timeout 8 dotnet src/Alliance.Client/bin/Debug/net10.0/Alliance.VideoWorker.dll "$(printf '%s' "$JSON" | base64 -w0)"; echo "EXIT=$?"

# 4. 端到端（推 HEVC 流至 UDP:3334 后）
dotnet run --project src/Alliance.Client   # 观察 [video-diag] state=Ready、pkts 增长、UI 出图
```
