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

**文件:** `src/Alliance.Client/Features/Video/VideoSupervisorService.cs:92`

**状态:** 已修复

**实现:**
```csharp
await pipeServer.WaitForConnectionAsync(cancellationToken)
    .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
```
超时 → `TimeoutException` → catch → kill + restart。

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

### M2 — `DrainStreamAsync` fire-and-forget

**文件:** `src/Alliance.Client/Features/Video/VideoSupervisorService.cs:193-194, 236-242`

**问题:**
stdout/stderr 的 drain 任务从未被 await，StreamReader 可能在 drain 完成前被 dispose，异常被静默吞噬。

**修复方案:**
将 drain task 保存并在 finally 中 await：
```csharp
var drainStdout = DrainStreamAsync(process.StandardOutput, cancellationToken);
var drainStderr = DrainStreamAsync(process.StandardError, cancellationToken);
// finally 中:
process.Kill(entireProcessTree: true);
await Task.WhenAll(drainStdout, drainStderr).WaitAsync(TimeSpan.FromSeconds(3));
```

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

### M4 — `CopyVideoWorkerOutput` ContinueOnError 吞构建错误

**文件:** `src/Alliance.Client/Alliance.Client.csproj:38-40`

**问题:**
`ContinueOnError="WarnAndContinue"` — Worker 构建失败时主项目仍然编译通过，运行时因缺 DLL 崩溃。

**修复方案:**
移除 `ContinueOnError="WarnAndContinue"`，Worker 构建失败则主项目构建也失败。

### M5 — swscale 输入格式硬编码 YUV420P ✅

**文件:** `src/Alliance.VideoWorker/WorkerRuntime.cs` — `FfmpegHevcDecoder`

**状态:** 已修复（附带于 C4）

**实现:**
- 新增 `EnsureScaleContext(AVPixelFormat)` 方法，从 `_decodedFrame->format` 动态读取像素格式
- 首次解码或格式变化时重建 `SwsContext`
- 不再硬编码 `AV_PIX_FMT_YUV420P`

---

## Minor (P3)

### N1 — `VideoFeedControl` 不监听 Surface 属性变更

**文件:** `src/Alliance.Client/Features/Video/VideoFeedControl.cs:18-26`

**问题:**
只监听 `StoreProperty` 变更，不监听 `Store.Surface`。如果 `Surface` 被替换为新的 `WriteableBitmap` 实例，控件不会自动重绑。

**修复方案:**
在 `StoreProperty` 变更处理中订阅/取消订阅 `Store.PropertyChanged`，当 `Surface` 属性变化时调用 `InvalidateVisual()`。

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
| **P1** | M1 | 宽泛 `catch (Exception)` | `VideoSupervisorService.cs` | 待修复 |
| **P1** | M4 | MSBuild `ContinueOnError` | `Alliance.Client.csproj` | 待修复 |
| **P1** | M3 | Debug fallback 路径 | `VideoSupervisorService.cs` | 待修复 |
| **P2** | M2 | `DrainStreamAsync` fire-and-forget | `VideoSupervisorService.cs` | 待修复 |
| **P2** | M5 | swscale YUV420P 硬编码 | `WorkerRuntime.cs` | ✅ 已修复 |
| **P3** | N1 | `VideoFeedControl` Surface 监听 | `VideoFeedControl.cs` | 待修复 |
| **P3** | N2 | `CleanupExpired` 高频全表扫描 | `WorkerRuntime.cs` | 待修复 |
| **P3** | N3 | `appsettings.json` 过多参数 | `appsettings.json` + `AppSettings.cs` | 待修复 |

**当前状态:** P0（4/4）+ M5 已修复并编译通过，14 个测试全绿。剩余 P1-P3 待后续迭代。
