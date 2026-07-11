# 图传接入实施方案

## 1. 目标

将 `UDP 3334` 上的 HEVC 裸码流稳定显示到客户端左上角视频区域，并满足以下要求：

- 主 UI 不因视频解码异常而崩溃
- 长时间运行下延迟不持续累积
- 推流中断后能够自动降级、自动恢复
- 依赖版本可控，不依赖目标机器“刚好装了可用环境”

当前仓库只存在协议说明和 UI 占位区，尚未实现视频接收、重组、解码和显示链路。

## 2. 最终选型

采用“双进程 + 固定版本 FFmpeg + 共享内存传帧”的方案。

### 2.1 为什么不用单进程

把 UDP、HEVC 解码、FFmpeg native 调用全部放进 Avalonia 主进程，实现最直接，但可靠性不足：

- 任意 native 崩溃都会直接带死 UI
- 解码线程阻塞、内存破坏、库加载异常都会影响主窗口
- 排查时视频故障和 UI 故障耦合过深

### 2.2 为什么不用 `ffmpeg` 子进程直接输出原始帧

`ffmpeg` 子进程本身可以隔离解码崩溃，但如果通过 `stdout` 持续输出 `1920x1080 BGRA` 原始帧，会出现严重吞吐和背压问题：

- 单帧约 `7.9MB`
- `60fps` 时原始像素吞吐接近 `475MB/s`
- UI 端一旦消费稍慢，管道会积压，画面延迟会越来越大

### 2.3 为什么不用 VLC / GStreamer 直接做播放器接入

这些方案在标准流媒体播放上成熟，但这里的输入不是现成 RTP/RTSP/TS，而是自定义 UDP 分片协议。若直接接播放器库，仍然要先自己做分片重组，集成复杂度和调试成本更高，插件和依赖分发也更难统一控制。

### 2.4 最终建议

新增一个独立 worker 进程 `Alliance.VideoWorker`，专门负责视频链路：

- 监听 `UDP 3334`
- 按协议重组完整 HEVC 帧
- 用 FFmpeg 库解码为 `BGRA32`
- 把最新稳定帧写入共享内存
- 通过轻量控制通道上报心跳、状态和指标

主程序 `Alliance.Client` 只负责：

- 启动和监督 worker
- 从共享内存读取最新完整帧
- 将帧拷贝到 Avalonia `WriteableBitmap`
- 在 HUD 左上角视频区域渲染画面并叠加现有 HUD

这样即使视频 worker 崩溃，主 UI 仍然存活，并且可以自动拉起恢复。

## 3. 进程与模块划分

### 3.1 主程序 `Alliance.Client`

新增模块建议放在 `src/Alliance.Client/Features/Video/`：

- `VideoStreamStore`
  - 保存视频状态、最后一帧时间、分辨率、fps、错误信息、`WriteableBitmap`
- `VideoSupervisorService`
  - 启动 `Alliance.VideoWorker`
  - 监听心跳
  - 异常退出后按退避策略重启
- `SharedFrameReader`
  - 从共享内存读取最新稳定帧
  - 把像素拷贝到 `WriteableBitmap`
- `VideoFeedControl`
  - 在 Avalonia 中绘制视频位图

### 3.2 Worker `Alliance.VideoWorker`

建议新增项目 `src/Alliance.VideoWorker/`，内部模块拆分如下：

- `UdpIngress`
  - 使用 `Socket` 监听 `0.0.0.0:3334`
- `HevcFrameAssembler`
  - 负责分片重组和超时清理
- `DecodePipeline`
  - 调用 FFmpeg `libavcodec/libavutil/libswscale`
  - 把 HEVC 解码为 `BGRA32`
- `SharedFramePublisher`
  - 把最新解码帧写入共享内存环形缓冲区
- `StatusPipeServer`
  - 周期发送心跳、状态、吞吐、错误计数

## 4. 完整数据流

端到端链路固定如下：

`UDP 3334` -> `UdpIngress` -> `HevcFrameAssembler` -> `DecodePipeline` -> `SharedFramePublisher` -> `SharedFrameReader` -> `WriteableBitmap` -> `VideoFeedControl` -> `HudOverlay` 左上视频区域

UI 上的挂载位置为现有 [HudOverlay.axaml](../src/Alliance.Client/Features/Hud/HudOverlay.axaml) 左上 `6x6` 视频区域。底层放视频控件，上层继续放 `TeamPanel` 和 `MatchTimer`。

## 5. UDP 分片重组规则

每个 UDP 包前 8 字节按大端解析：

- `frameId`: `ushort`
- `fragmentIndex`: `ushort`
- `totalBytes`: `uint`

分片重组规则固定如下：

- 每个分片最大负载为 `1392` 字节
- 分片写入偏移为 `fragmentIndex * 1392`
- 期望分片数为 `ceil(totalBytes / 1392)`
- 为每个待组帧维护：
  - `byte[] buffer`
  - `bool[] received`
  - `int receivedCount`
  - `DateTime startedAt`
- 若以下任一情况发生，直接丢弃该帧：
  - `fragmentIndex` 越界
  - `totalBytes` 前后不一致
  - 写入偏移越界
  - 超过 `50ms` 未收齐
- `frameId` 需要按 `ushort` 环绕比较，支持 `65535 -> 0`

说明：该协议无重传机制，因此丢帧是正常行为。设计目标不是“补齐丢失帧”，而是“尽快丢弃坏帧并恢复到后续新帧”。

## 6. 解码与显示策略

### 6.1 解码策略

- 解码库使用固定版本 FFmpeg runtime，不依赖系统全局安装
- worker 内通过 `FFmpeg.AutoGen` 调用 native 库
- 默认只启用软件解码，先保证稳定性和一致性
- 每个完整 HEVC 帧按顺序送入解码器
- 从解码器取出 `AVFrame` 后统一转成 `BGRA32`

### 6.2 显示策略

- UI 不需要展示“每一帧”，只需要展示“最新已完成解码帧”
- 若 UI 渲染速度落后，可以丢中间显示帧，但不能打乱解码输入顺序
- `WriteableBitmap` 只创建一次并复用，避免频繁分配
- 视频丢失时不要保留旧画面太久，避免误导操作人员

### 6.3 状态切换建议

- `0ms ~ 500ms` 未收到新帧：维持 `Ready`
- `> 500ms` 未收到新帧：状态改为 `Degraded`，显示 `VIDEO LOST`
- `> 2000ms` 未收到新帧：清黑视频画面，仅保留状态文字

## 7. 共享内存与控制通道

### 7.1 帧传输

使用 `MemoryMappedFile` 建立 3 槽环形缓冲区，每槽保存一帧 `1920x1080 BGRA32`。

原因：

- 吞吐稳定，不受 `stdout` 管道背压影响
- 主程序总是读取“最新完整槽位”
- worker 进程崩溃不会污染主 UI 线程模型

每个槽位建议包含：

- 帧序号
- 宽高
- 像素格式
- 有效字节数
- 提交版本号
- 帧像素数据

采用类似 `seqlock` 的提交方式：

- worker 写入前先标记“写入中”
- 数据写完后再原子提交“稳定版本”
- reader 只读取稳定版本，避免读到半帧

### 7.2 状态上报

使用命名管道或 Unix domain socket 作为控制通道，传输 JSON 状态消息即可。

worker 每 `250ms` 上报一次：

- `pid`
- `streamState`
- `lastPacketAt`
- `lastFrameAt`
- `packetCount`
- `assembledFrameCount`
- `decodedFrameCount`
- `decodeErrorCount`
- `presentFps`
- `note`

主程序若 `1000ms` 内未收到心跳，视为 worker 不健康，触发重启。

## 8. 主程序改动点

### 8.1 配置

在 `AppSettings` 中新增：

```json
{
  "Video": {
    "Enabled": true,
    "UdpPort": 3334,
    "FrameWidth": 1920,
    "FrameHeight": 1080,
    "ExpectedFps": 60,
    "PresentFps": 60,
    "HeartbeatTimeoutMs": 1000,
    "SignalLostAfterMs": 500,
    "ClearFrameAfterMs": 2000,
    "RestartInitialDelayMs": 1000,
    "RestartMaxDelayMs": 30000
  }
}
```

### 8.2 DI 与运行时

在 `AppBootstrapper` 中新增注册：

- `VideoStreamStore`
- `VideoSupervisorService`

在运行时协调器中：

- 启动时同时启动 MQTT 和视频监督服务
- 关闭时同时停止 MQTT 和视频监督服务
- 视频链路异常不得阻塞 MQTT 和主窗口初始化

### 8.3 UI

改造 `HudOverlay.axaml`：

- 左上视频区域底层替换为 `VideoFeedControl`
- 右上角或底部加一小行 `Video` 状态文字
- 保持现有 HUD 叠加布局不变

改造设置窗口：

- 在连接状态区域新增只读 `Video` 状态
- v1 不提供视频参数热更新按钮

## 9. 依赖与安装要求

为保证可控性，建议将 FFmpeg runtime 固定版本随程序发布，而不是要求目标机器预装系统包。

建议准备内容：

- `libavcodec`
- `libavutil`
- `libswscale`
- 其依赖的对应动态库

开发机可额外安装：

- `ffmpeg`
  - 用于录制样本流、回放、调试，不作为主链路运行时依赖
- `ffprobe`
  - 用于检查码流可解码性

## 10. 容错与恢复策略

### 10.1 Worker 异常退出

- 主程序捕获退出事件
- 状态更新为 `NotConnected`
- 按指数退避自动重启
- 重启成功后自动恢复视频显示

### 10.2 无数据输入

- 保持主 UI 可操作
- 视频区显示 `WAITING FOR STREAM`
- 不影响遥测和其他 HUD 数据

### 10.3 解码错误

- 记录错误计数和最近错误原因
- 丢弃当前坏帧，继续处理后续帧
- 连续错误过多时允许重置解码器上下文，但不退出主程序

## 11. 测试与验收

### 11.1 单元测试

- `HevcFrameAssembler` 顺序分片重组
- `HevcFrameAssembler` 乱序分片重组
- 缺片超时丢弃
- `frameId` 环绕处理
- 非法分片拒绝
- 共享内存稳定提交与读取
- worker 心跳超时与自动重启逻辑

### 11.2 集成验收

- 无推流时客户端正常启动，视频区显示等待状态
- 有效 HEVC UDP 推流时视频区稳定出图
- 停止推流后 `500ms` 内状态进入 `Degraded`
- 停止推流 `2s` 后自动黑屏
- 手工杀掉 worker 后主程序不退出，并能自动恢复
- 长时间运行下延迟不持续累积

## 12. 实施顺序

建议按以下顺序落地：

1. 新增 `Video` 配置、`VideoStreamStore`、UI 状态占位
2. 新增 `Alliance.VideoWorker` 项目和最小心跳链路
3. 实现 UDP 分片重组并补单元测试
4. 接入 FFmpeg 解码并输出到共享内存
5. 主程序接入共享内存 reader 和 `VideoFeedControl`
6. 完成监督重启、状态展示和错误处理
7. 做端到端联调和长时间稳定性验证

## 13. 结论

如果目标是“最快看到画面”，单进程 FFmpeg 就够了。

如果目标是“最可靠、能长期跑、视频故障不拖死主 UI”，当前最合适的方案是：

`Alliance.Client` + `Alliance.VideoWorker` + 固定版本 FFmpeg runtime + 共享内存传帧 + 轻量控制通道监督

这也是本项目后续实现视频接入时建议采用的正式方案。
