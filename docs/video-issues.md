# 视频流接入 Review — 问题与修复计划

## 架构概述

本轮将原来"主进程内 UDP + FFmpeg HEVC 解码"切换为**多进程隔离架构**：
- `Alliance.Client`（主进程）启动独立的 `Alliance.VideoWorker` 子进程
- Named Pipe 传递状态
- Memory-Mapped File (共享内存) 传递 BGRA 解码帧数据
- Worker 崩溃由 Supervisor 自动重启（指数退避）

---

## Critical (P0) ✅ 已修复

### C1 — `_packet->data` 指向托管内存，存在 UAF 风险 ✅

**文件:** `src/Alliance.VideoWorker/WorkerRuntime.cs` — `FfmpegHevcDecoder`

**状态:** 已修复

**实现:**
- `Decode()` 内改为 `ffmpeg.av_new_packet(_packet, poutbufSize)` 从 FFmpeg 内部池分配 native buffer
- 用 `Buffer.MemoryCopy` 从 parser 输出拷贝到 native packet
- 每次 `avcodec_send_packet()` 后立即 `ffmpeg.av_packet_unref(_packet)`，生命周期由 FFmpeg ref-count 管理
- 不再有 `fixed` 块 + 裸指针赋值

### C2 — Supervisor 读心跳阻塞帧轮询 ✅

**文件:** `src/Alliance.Client/Features/Video/VideoSupervisorService.cs` — `RunAsync()`

**状态:** 已修复

**实现:**
- 心跳读取移入独立后台 `Task`（`heartbeatTask`），`ReadLineAsync` 循环写入 `Interlocked` 共享的 `lastHeartbeatAtTicks` 和 `lock` 共享的 `latestStatus`
- 主循环只负责帧轮询 + status apply + 心跳超时检测 + 帧老化清除
- 主循环用 `Task.Delay(5)` 做 tight poll，帧读取和合法性检查不再依赖心跳到达
- `finally` 中 cancel heartbeatCts 并 await heartbeatTask 做优雅清理

### C3 — `WaitForConnectionAsync` 无超时 ✅

**文件:** `src/Alliance.Client/Features/Video/VideoSupervisorService.cs:88-104`

**状态:** 已修复

**实现:**
```csharp
using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
{
    connectCts.CancelAfter(TimeSpan.FromSeconds(5));
    try
    {
        await pipeServer.WaitForConnectionAsync(connectCts.Token);
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        throw new TimeoutException("Video worker did not connect within 5 seconds.");
    }
}
```
超时 → `TimeoutException` → catch → kill + restart；外部停止 → `OperationCanceledException` 透传退出。

**修订:** 最初实现用 `.WaitAsync(timeout, ct)`，只停止等待而不取消底层连接操作，超时/取消后 `pipeServer` 被 dispose，遗弃的 `WaitForConnectionAsync` 任务 fault（`Pipe is broken` / `Operation canceled`）→ 终结器线程 rethrow `[FATAL] Unobserved task exception`。改为 linked CTS + `CancelAfter` 并直接 await，异常始终被观察，超时真正取消底层操作。

### C4 — 缺少 FFmpeg parser ✅

**文件:** `src/Alliance.VideoWorker/WorkerRuntime.cs` — `FfmpegHevcDecoder`

**状态:** 已修复

**实现:**
- 构造函数中初始化 `AVCodecParserContext* _parser = ffmpeg.av_parser_init(AV_CODEC_ID_HEVC)`
- `Decode(byte[], List<DecodedFrame>)` — 新签名，返回 0~N 个 decoded frame
- 输入 buffer 末尾补齐 `64` 字节 zero padding
- 循环 `av_parser_parse2()` 消费输入 → parser 产出 packet → `avcodec_send_packet()` → 循环 `avcodec_receive_frame()` 直到 `EAGAIN`
- `Dispose()` 中 `av_parser_close(_parser)`
- `ReceiveLoopAsync` 调用方改为 `List<DecodedFrame>` 收集多帧输出
- 附带修复 swscale 输入像素格式硬编码（见 M5）

### C5 — FFmpeg 大版本不匹配导致 Worker 秒退 → 连接超时 ✅

**文件:** `src/Alliance.VideoWorker/WorkerRuntime.cs`、`Alliance.VideoWorker.csproj`、`Program.cs`；`src/Alliance.Client/Features/Video/VideoSupervisorService.cs`

**状态:** 已修复

**现象:**
主进程反复刷 `TimeoutException: Video worker did not connect within 5 seconds.` → "worker will restart"。

**根因（`LD_DEBUG=libs` 确诊）:**
- Worker native 层致命错误 `undefined symbol: avcodec_find_decoder (fatal)`，进程在 `ConnectAsync` 之前秒退 → 主进程 `WaitForConnectionAsync` 5s 超时。
- `FFmpeg.AutoGen 7.1.1` 绑定 major 61（FFmpeg 7.x），系统 `/usr/lib` 实装 avcodec **62** / avutil **60**（FFmpeg 8.0），符号版本对不上。
- 且旧代码只设 `ffmpeg.RootPath = "/usr/lib"` 却**未调 `DynamicallyLoadedBindings.Initialize()`**，`RootPath` 实际未生效，靠 OS 加载器解析 `libavcodec.so`（依赖 dev 符号链接）。
- Worker 的 `LoggerFactory` 未 flush、supervisor 的 `DrainStreamAsync` 丢弃子进程输出 → 崩溃原因不可见。

**实现:**
- `FFmpeg.AutoGen` `7.1.1 → 8.1.0`（绑定 major 62，匹配系统 FFmpeg 8）
- 新增 `FfmpegLoader.EnsureInitialized()`：确定目录后设 `ffmpeg.RootPath` + 调 `DynamicallyLoadedBindings.Initialize()`
- FFmpeg 目录探测（Linux x64）：环境变量 `ALLIANCE_FFMPEG_ROOT` → 扫描 `/usr/lib`、`/usr/lib/x86_64-linux-gnu`、`/usr/lib64`、`/usr/local/lib` 找含 `libavcodec.so.62` 的目录；找不到抛出带"期望 major / 已搜目录 / 实际发现版本 / 补救建议"的清晰异常
- FFmpeg 8 弃用 API：`ffmpeg.SWS_BILINEAR` → `(int)SwsFlags.SWS_BILINEAR`
- `Program.cs` 的 `loggerFactory` 加 `using` 保证退出前 flush
- `DrainStreamAsync` 从丢弃改为转发到主日志（stdout→`LogInformation`、stderr→`LogWarning`，`[worker]` 前缀）

**验证:** 手动跑 Worker `EXIT=124`（挂在等管道 = FFmpeg 初始化通过），`dotnet build` + 14 测试全绿。

### C6 — 孤儿 Worker 占用 UDP 端口 + Bind 失败静默挂起 ✅

**文件:** `src/Alliance.VideoWorker/WorkerRuntime.cs`、`Program.cs`、`ParentDeathGuard.cs`；`src/Alliance.Client/Features/Video/VideoSupervisorService.cs`

**状态:** 已修复

**现象:**
`[video-diag] state=Connecting pkts=0` 持续刷屏，心跳一直在发但状态永不变 Ready，UI 停在 WAITING FOR STREAM。`ss` 显示一个 34 分钟前的旧 Worker（`ppid=1`）霸占 UDP:3334。

**根因:**
1. **孤儿进程**：`App.axaml.cs` 只在 `desktop.Exit`（优雅退出）时调 `StopAsync` → `TryTerminate`。`dotnet run` 被 Ctrl-C / SIGKILL 时 Worker 不被清理，变成 `ppid=1` 僵尸继续占 3334。
2. **`Task.WhenAll` 不响应单任务故障**：`WorkerRuntime.RunAsync` 里 `await Task.WhenAll(heartbeatTask, receiveTask)`。当新 Worker `socket.Bind(3334)` 因端口被占抛 `SocketException(98)` 时，`receiveTask` 故障但 `heartbeatTask` 仍无限运行 → `WhenAll` 永不返回 → Worker 卡 `Connecting`、错误不暴露、supervisor 也不重启。反向同理：父进程死后管道断开使 `heartbeatTask` 故障，但 `receiveTask` 永不退出 → Worker 成僵尸。

**实现:**
- `RunAsync`：`Task.WhenAll` → `Task.WhenAny` + `_cts.Cancel()`，任一循环故障即取消另一个并退出；`completed.IsFaulted` 时 `await` 重抛，故障经 `Program.cs` catch `LogError` 暴露
- 新增 `ParentDeathGuard`（Linux `prctl(PR_SET_PDEATHSIG, SIGTERM)`）：父进程死亡时内核自动向 Worker 发 SIGTERM；并处理"启动前父已死"（`getppid()==1`）竞态
- `Program.cs` 启动时 `ParentDeathGuard.Enable()`

**验证（构造 pipe-host 集成测试）:**
- 端口冲突：Worker 打印 `SocketException (98): Address already in use` 并快速退出（不再挂起）
- 孤儿防护：SIGKILL 父进程后，Worker 被 PDEATHSIG 终止，3334 自动释放
- `dotnet test` 14 全绿

**诊断增强（顺带保留）:** `[video-diag]`（主进程每秒打印心跳分级计数：pkts/assembled/decoded/presented/decodeErr/fps）与 `[assembler-diag]`（Worker 每秒打印分片丢弃分支计数与最近包解析值），用于快速定位链路断点。

---

## Medium (P1–P2)

### M1 — `catch (Exception)` 过于宽泛

**文件:** `src/Alliance.Client/Features/Video/VideoSupervisorService.cs:156`

**问题:**
JSON 反序列化心跳消息出错也会触发完整重启（杀 Worker、重建 MMF），应该只对进程退出/管道断开 restart。

**修复方案:**
```csharp
try
{
    // heartbeat read + JSON parse + ApplyWorkerStatusAsync
}
catch (JsonException ex)
{
    _logger.LogWarning(ex, "Failed to parse worker status message.");
    continue; // 继续读下一行，不重启
}
// 进程退出/管道断开/超时 → 重新抛出让外层 catch 处理 restart
```

### M2 — `DrainStreamAsync` fire-and-forget ✅

**文件:** `src/Alliance.Client/Features/Video/VideoSupervisorService.cs`

**状态:** 已修复

**实现:**
- `StartWorker` 改为返回 `(Process, Task drainStdout, Task drainStderr)`，drain 任务不再 fire-and-forget
- `finally` 中 `await Task.WhenAll(drainStdout, drainStderr).WaitAsync(TimeSpan.FromSeconds(3))`，随后显式 `process.Dispose()`
- `DrainStreamAsync` 新增 `CancellationToken` 参数，用 `ReadLineAsync(ct)`，并吞掉 `OperationCanceledException`/`IOException`
- 附带修复 C3 遗留的孤儿任务问题：`WaitForConnectionAsync` 改为 linked CTS + `CancelAfter` 直接 await（见 C3）

### M3 — `ResolveWorkerDllPath` fallback 路径硬编码 Debug

**文件:** `src/Alliance.Client/Features/Video/VideoSupervisorService.cs:198-213`

**问题:**
```csharp
var repoPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
    "../../../../src/Alliance.VideoWorker/bin/Debug/net10.0/Alliance.VideoWorker.dll"));
```
Release 构建时找不到 Worker DLL，抛 `FileNotFoundException`。

**修复方案:**
```csharp
#if DEBUG
    var config = "Debug";
#else
    var config = "Release";
#endif
    var repoPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
        $"../../../../src/Alliance.VideoWorker/bin/{config}/net10.0/Alliance.VideoWorker.dll"));
```
或者直接依赖 M4 修复后的 Copy target，确保 output 目录总有 Worker.dll，不再需要 fallback。

### M4 — `CopyVideoWorkerOutput` ContinueOnError 吞构建错误 ✅

**文件:** `src/Alliance.Client/Alliance.Client.csproj:34-45`

**状态:** 已修复

**实现:**
- 移除 `ContinueOnError="WarnAndContinue"`，Worker 构建失败 → 主项目构建也失败
- Copy target 改为 glob 拷贝 Worker `bin/$(Configuration)/net10.0/**/*` **整个输出目录**（而非写死 3 个文件），自动带上 `FFmpeg.AutoGen.dll` 及未来任何新依赖
- 先 `Exec` 构建 Worker，再用 `ItemGroup` glob 采集，保证拷到的是最新产物
- **修复了 C5 后暴露的连带问题**：旧 copy 只拷 3 个文件，缺 `FFmpeg.AutoGen.dll` → Worker 抛 `FileNotFoundException: FFmpeg.AutoGen, Version=8.1.0.0` 秒退（此错误此前被 native 崩溃掩盖，靠 C5 的日志转发才暴露）

### M5 — swscale 输入格式硬编码 YUV420P ✅

**文件:** `src/Alliance.VideoWorker/WorkerRuntime.cs` — `FfmpegHevcDecoder`

**状态:** 已修复（附带于 C4）

**实现:**
- 新增 `EnsureScaleContext(AVPixelFormat)` 方法，从 `_decodedFrame->format` 动态读取像素格式
- 首次解码或格式变化时重建 `SwsContext`
- 不再硬编码 `AV_PIX_FMT_YUV420P`

---

## Minor (P3)

### N1 — `VideoFeedControl` 不随新帧重绘（有 fps 无画面）✅

**文件:** `src/Alliance.Client/Features/Video/VideoFeedControl.cs`、`VideoStreamStore.cs`

**状态:** 已修复

**问题:**
只在 `StoreProperty` 变更时 `InvalidateVisual()`。`Store` 实例启动后不再变，`WriteableBitmap` 像素内容更新不发任何属性通知 → 自绘控件从不重绘。表现为 fps 文本正常刷新（走 `{Binding Snapshot}`），但画面区域始终空白。

**实现（方案 ii — Store 显式帧事件）:**
- `VideoStreamStore` 新增 `event Action? FrameUpdated`，`UpdateFrame`/`ClearFrame` 写完像素后触发（调用方已在 UI 线程）
- `VideoFeedControl` 在 `StoreProperty` 变更时订阅/退订 `FrameUpdated`，事件到达即 `InvalidateVisual()`（`CheckAccess` + `Post` 兜底跨线程）
- `OnDetachedFromVisualTree` 退订防泄漏；`Subscribe` 用引用比较避免重复订阅
- 附带修复 stride 对齐：`UpdateFrame`/`ClearFrame` 改用 `locked.RowBytes`，`RowBytes != FrameWidth*4` 时逐行拷贝，防潜在花屏

### N2 — `CleanupExpired` 每次 UDP 包都遍历全字典

**文件:** `src/Alliance.VideoWorker/WorkerRuntime.cs:354-369`

**问题:**
60fps 高频下 `CleanupExpired` 每次收到 UDP 包都遍历全部未完成帧，产生不必要的 GC 分配。

**修复方案:**
添加 `_lastCleanupAt` 时间戳字段，仅在距离上次清理超过 1 秒时才扫描。

### N3 — `appsettings.json` 暴露过多内部参数

**文件:** `src/Alliance.Client/appsettings.json:8-21` + `Features/Settings/AppSettings.cs:VideoSettings`

**问题:**
`FrameAssemblyTimeoutMs`、`HeartbeatIntervalMs`、`RestartInitialDelayMs` 等内部调优参数暴露给用户配置，增加配置文件复杂度和用户困惑。

**修复方案:**
保留 `Enabled`、`UdpPort`、`FrameWidth`、`FrameHeight` 为用户配置项。其余移入 `VideoConstants` 或代码常量。

---

## 修复优先级总览

| 优先级 | 编号 | 问题简述 | 文件 | 状态 |
|--------|------|----------|------|------|
| **P0** | C1 | `_packet->data` 指向托管内存 UAF | `WorkerRuntime.cs` | ✅ 已修复 |
| **P0** | C4 | 缺少 FFmpeg parser | `WorkerRuntime.cs` | ✅ 已修复 |
| **P0** | C2 | 心跳读阻塞帧轮询 | `VideoSupervisorService.cs` | ✅ 已修复 |
| **P0** | C3 | `WaitForConnectionAsync` 无超时 | `VideoSupervisorService.cs` | ✅ 已修复 |
| **P0** | C5 | FFmpeg 大版本不匹配致 Worker 秒退 | `WorkerRuntime.cs` + `.csproj` + `Program.cs` | ✅ 已修复 |
| **P0** | C6 | 孤儿 Worker 占用端口 + Bind 静默挂起 | `WorkerRuntime.cs` + `Program.cs` + `ParentDeathGuard.cs` | ✅ 已修复 |
| **P1** | M1 | 宽泛 `catch (Exception)` | `VideoSupervisorService.cs` | 待修复 |
| **P1** | M4 | MSBuild `ContinueOnError` | `Alliance.Client.csproj` | ✅ 已修复 |
| **P1** | M3 | Debug fallback 路径 | `VideoSupervisorService.cs` | 待修复 |
| **P2** | M2 | `DrainStreamAsync` fire-and-forget | `VideoSupervisorService.cs` | ✅ 已修复 |
| **P2** | M5 | swscale YUV420P 硬编码 | `WorkerRuntime.cs` | ✅ 已修复 |
| **P3** | N1 | `VideoFeedControl` 不随新帧重绘 | `VideoFeedControl.cs` + `VideoStreamStore.cs` | ✅ 已修复 |
| **P3** | N2 | `CleanupExpired` 高频全表扫描 | `WorkerRuntime.cs` | 待修复 |
| **P3** | N3 | `appsettings.json` 过多参数 | `appsettings.json` + `AppSettings.cs` | 待修复 |

**当前状态:** P0（6/6，含 C5/C6）+ M2 + M4 + M5 + N1 已修复并编译通过，14 个测试全绿。剩余 P1（M1/M3）与 P3（N2/N3）待后续迭代。
