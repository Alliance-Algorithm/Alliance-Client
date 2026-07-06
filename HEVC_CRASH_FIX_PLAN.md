# Alliance Client HEVC 崩溃修复计划

## 1. 背景

当前客户端通过 UDP 监听 `3334` 端口接收图传码流，视频编码格式为 HEVC/H.265。

已知输入特征如下：

- 传输协议为 UDP，无重传机制。
- 每个 UDP 包最大 `1400` 字节。
- 前 `8` 字节为自定义头部。
- 头部字段为大端序。
- 头部内容依次为：`frameId(2B)`、`segmentIndex(2B)`、`totalFrameBytes(4B)`。
- 剩余 `1392` 字节为 HEVC 码流负载。
- 发送端名义帧率为 `60fps`，分辨率为 `1920x1080`。

当前客户端的处理链路为：

1. `HevcFrameAssembler` 根据 `frameId + segmentIndex + totalFrameBytes` 重组 UDP 包。
2. `UdpHevcVideoStreamService` 在收到完整 `frameData` 后调用 `HevcDecoderAdapter.TryDecode()`。
3. `HevcDecoderAdapter` 通过 FFmpeg `libavcodec` 解码 HEVC 数据，并输出 `WriteableBitmap`。
4. Avalonia `Image` 控件显示图像。

问题表现为：

- 客户端启动后无法正常显示图像。
- 收到一段时间码流后崩溃退出。
- 崩溃发生前没有托管异常日志。

## 2. 现象与证据

### 2.1 运行日志特征

当前最近一次完整日志的关键点如下：

- UDP `3334` 绑定成功。
- FFmpeg HEVC decoder 初始化成功。
- 首个 UDP 包和首个完整 HEVC 数据块重组成功。
- 已成功提取出 `VPS/SPS/PPS`。
- decoder 打开成功。
- 在输出 `IDR found (174 frames discarded before IDR)` 后立即崩溃。

这说明：

- 3334 端口接收本身没有失败。
- 自定义 8 字节头部解析没有直接失败。
- 崩溃发生在“首个 IDR 真正进入 FFmpeg 解码路径”之后。

### 2.2 Core Dump 证据

通过 `coredumpctl` 检查，系统中已有多次 `Alliance.Client` 的 `SIGSEGV` 记录。

最近一次核心栈关键信息为：

- 线程：`.NET TP Worker`
- 信号：`SIGSEGV`
- 原生栈顶：`av_buffer_unref -> av_frame_unref -> avcodec_receive_frame_flags`

结论：

- 这不是 Avalonia UI 崩溃。
- 这不是 `WriteableBitmap` 渲染崩溃。
- 这不是 C# 托管异常被遗漏。
- 崩溃明确发生在 FFmpeg HEVC 解码阶段内部。

### 2.3 已排除的问题

以下问题已经检查过，不再视为当前主因：

- `3334` 端口绑定失败：日志已证明绑定成功。
- MQTT `3333` 干扰：用户已明确该问题与 `3333` 无关，日志也印证视频链路独立工作。
- `extradata` 缺少 padding：已补 `64` 字节 zero padding，但崩溃仍然存在。
- UI 位图释放导致首帧前崩溃：当前 core dump 显示崩溃发生在 FFmpeg 内部，早于图像显示成功。

## 3. 崩溃原因分析

### 3.1 直接原因

当前最可信的直接原因是：

**客户端把 UDP 重组后的 `frameData` 直接当作“一个完整且可立即送入 `avcodec_send_packet()` 的 HEVC 访问单元”处理，但这一路流实际并不满足当前解码实现的假设，导致 FFmpeg decoder 内部状态被喂坏，最终在 `avcodec_receive_frame_flags()` 中发生 `SIGSEGV`。**

### 3.2 为什么当前实现风险高

当前解码器存在以下结构性问题：

1. **手工扫描参数集**

   当前通过扫描 Annex B 起始码提取 `VPS/SPS/PPS`，并手动拼 `extradata`。

   风险在于：

   - 这假设参数集边界和访问单元边界都足够规整。
   - 一旦码流携带方式与假设不完全一致，手工提取就可能与 decoder 实际期望不一致。

2. **手工判定 IDR**

   当前实现通过扫描 NAL type `19/20` 来判断是否已经遇到 IDR，并在此之前丢弃若干“帧”。

   风险在于：

   - “重组后的一个 frameId 是否等于一个真正的 access unit”这个前提并未被 FFmpeg 验证。
   - 即使 `frameId` 对应的是发送端所谓“一帧”，也不代表本地收到的数据已经适合直接送 decoder。

3. **绕过 FFmpeg parser**

   当前实现直接将重组出的字节块送入 decoder，没有经过 `av_parser_parse2()`。

   这会导致：

   - decoder 接收到的 packet 边界完全依赖上游协议假设。
   - 一旦 packet 中存在非标准边界、跨访问单元数据、额外前缀或不完整语义，FFmpeg 解码器内部就可能进入错误状态。

4. **手工 overlay `AVCodecContext`**

   当前实现使用手工布局结构体覆盖 `AVCodecContext`，写入 `extradata` 字段。

   虽然本机当前偏移验证通过，但它仍有两个问题：

   - 这种写法本身脆弱，依赖 FFmpeg ABI 版本。
   - 它让解码链路更难收敛，因为 packet 输入问题和 context 注入问题交织在一起。

### 3.3 为什么图像没有显示

图像未正常显示并不是独立问题，而是崩溃的直接结果。

当前日志中没有出现任何“首个 decoded frame 成功”的记录，说明：

- decoder 在真正产出第一帧之前已经进入异常状态。
- UI 层根本没有拿到有效图像数据。

因此，“无图像显示”和“数秒后崩溃”是同一根因在两个阶段上的表现：

- 第一阶段：decoder 还没成功产出一帧，所以界面没有图像。
- 第二阶段：decoder 在尝试解出第一帧或处理首个关键访问单元时发生原生崩溃。

## 4. 修复目标

本次修复必须同时满足以下目标：

1. 客户端不再因 HEVC 解码触发 `SIGSEGV`。
2. 客户端能够稳定显示首帧图像。
3. 继续保持当前 UDP 3334 接收协议不变。
4. 不引入新的 UI 内存泄漏。
5. 让解码实现尽可能依赖 FFmpeg 的标准解析路径，而不是继续手工推断 HEVC 边界。

## 5. 修复方案

### 5.1 总体方案

修复方向为：

**保留 UDP 重组层，重写 HEVC 解码输入层。核心改动是引入 FFmpeg parser，把“重组后的字节块”先交给 `av_parser_parse2()`，再把 parser 输出的标准 packet 送入 decoder。**

即：

- UDP 重组仍然输出 `frameData`。
- `frameData` 不再被直接视为可解码帧。
- `frameData` 改为视作一段 HEVC 字节流输入。
- 由 FFmpeg parser 决定真正的 packet 边界。

### 5.2 必须修改的核心点

#### 5.2.1 删除手工参数集/IDR 驱动逻辑

在 `HevcDecoderAdapter` 中删除或停用以下逻辑：

- `ScanParameterSets()`
- `ScanForNalType()`
- `ContainsIdr()`
- `SelectFramesStartingFromFirstIdr()`
- `PrimeDecoderWithPendingFrames()`
- 手工 `extradata` 拼装与注入
- `AVCodecContextExt` overlay 写入

原因：

- 这些逻辑都是为了在“没有 parser 的前提下”手工构建 decoder 输入。
- 当前证据已经说明这条路不可靠。

#### 5.2.2 引入 FFmpeg parser

在 `HevcDecoderAdapter` 中新增以下原生对象和流程：

- `AVCodecParserContext* _parserContext`
- `av_parser_init(AV_CODEC_ID_HEVC)`
- `av_parser_parse2(...)`
- `av_parser_close(...)`

处理流程应改为：

1. `TryDecode(byte[] hevcChunk, out WriteableBitmap? bitmap, out string statusText)` 接收一段重组后的 HEVC 字节块。
2. 为输入块构造 `size + AV_INPUT_BUFFER_PADDING_SIZE` 的零填充缓冲。
3. 循环调用 `av_parser_parse2()`，直到该输入块被完全消费。
4. parser 每次可能输出一个 packet，也可能输出 `0` 字节 packet。
5. 当 parser 产出有效 packet 时，再把 packet 包装为 `AVPacket` 并送入 `avcodec_send_packet()`。
6. 每送入一个 packet 后，循环调用 `avcodec_receive_frame()` 直到返回 `EAGAIN` 或 `EOF`。

这样做的收益：

- packet 边界由 FFmpeg 自己决定。
- 不再依赖“一个 frameId 一定等于一个完整访问单元”的脆弱假设。
- HEVC 起始码、参数集、关键帧边界等解析职责交给 FFmpeg，而不是手工实现。

#### 5.2.3 规范化 packet 缓冲构造

当前不能继续沿用“直接复制到 `av_new_packet()` 后就送 decoder”的方式作为唯一输入路径。

修复后应采用更严格的方式：

1. parser 输出 packet 后，为该 packet 单独分配 `packetSize + 64` 的零填充 native buffer。
2. 使用 `av_packet_from_data()` 或同等安全包装方式初始化 `AVPacket`。
3. 确保 packet buffer 尾部 `64` 字节全为 `0`。
4. 每个 packet 用完后及时 `av_packet_unref()`。

原因：

- FFmpeg 文档明确要求 `avpkt->data` 末尾具备 `AV_INPUT_BUFFER_PADDING_SIZE`。
- 当前崩溃虽然已不再是 `extradata` padding 问题，但 packet 输入规范化仍必须彻底做到位。

#### 5.2.4 Decoder 初始化改为标准路径

decoder 初始化应改为：

1. `avcodec_find_decoder(AV_CODEC_ID_HEVC)` 或等价按 codec id 查找。
2. `avcodec_alloc_context3()`。
3. 直接 `avcodec_open2()`。
4. 不再在 open 前手工写 `extradata`。

默认假设：

- 当前码流包含 in-band `VPS/SPS/PPS`。
- parser + decoder 能从字节流本身拿到必要参数集。

只有当实测证明当前流完全不带 in-band 参数集时，才考虑重新引入 `extradata`，但必须通过更安全的方式实现，而不是继续使用当前 overlay 模式。

#### 5.2.5 保留并完善帧资源释放

`UdpHevcVideoStreamService` 中已做的位图替换释放逻辑应保留，并进行一次一致性检查：

- 替换 `CurrentFrame` 时延后释放旧位图。
- `StopAsync()` 清空最后一帧。
- 异常退出路径清空最后一帧。

原因：

- 这不是当前 `SIGSEGV` 主因。
- 但这是 1080p60 场景下的长期稳定性必须项。

### 5.3 日志与诊断增强

为了让后续验证可观测，必须增加以下日志：

1. 首个 parser 输入块大小。
2. 首个 parser 输出 packet 大小。
3. parser 消费输入字节数与剩余字节数。
4. decoder 首次 `avcodec_send_packet()` 成功日志。
5. decoder 首次 `avcodec_receive_frame()` 成功日志。
6. 持续的 `invalid data`、`EAGAIN`、`warming up` 统计。

日志目标不是为了长期保留大量 debug 输出，而是为了回答三个问题：

- parser 是否真的切出了 packet。
- decoder 是否接受了 packet。
- decoder 是否成功产出 frame。

## 6. 具体改动清单

### 6.1 `src/Alliance.Client/Features/Video/Decode/HevcDecoderAdapter.cs`

需要完成以下改动：

- 移除手工 `extradata` 方案。
- 引入 parser 上下文。
- 把 `TryDecode()` 改为“字节块喂 parser，再喂 decoder”。
- 新增 packet/native buffer 安全构造与释放逻辑。
- 保留 `ConvertFrameToBitmap()`，但只在成功拿到 `AVFrame` 后调用。
- 在 `Dispose()` 中释放 parser 相关对象。

### 6.2 `src/Alliance.Client/Features/Video/UdpHevcVideoStreamService.cs`

需要完成以下改动：

- 保持当前 UDP 重组逻辑不变。
- 调整状态文案，从“Waiting SPS/PPS / Waiting IDR”改为基于 parser 的状态描述。
- 保留当前帧释放逻辑。
- 记录 parser/decode 关键阶段日志。

### 6.3 `tests/Alliance.Client.Tests/VideoAssemblerTests.cs`

需要完成以下改动：

- 删除已经不再成立的“手工 IDR 选择”测试。
- 删除或替换 `extradata` 相关旧测试。
- 增加新测试，覆盖：
  - parser 输入缓冲 zero padding。
  - packet 包装缓冲 zero padding。
  - parser 可以从一个输入块中切出 `0..N` 个 packet 的辅助逻辑。

如果测试项目不方便直接依赖本机 FFmpeg parser，可以把“字节块复制与 padding helper”抽成 `internal` 方法先做单元测试，parser 的最终验证交给集成运行。

## 7. 验证方案

### 7.1 编译验证

必须通过：

```bash
dotnet build src/Alliance.Client/Alliance.Client.csproj
```

### 7.2 测试验证

必须通过：

```bash
dotnet test tests/Alliance.Client.Tests/Alliance.Client.Tests.csproj
```

### 7.3 实流验证

必须在真实 3334 图传环境下满足以下条件：

1. 日志能越过 parser 初始化和 decoder 初始化阶段。
2. 日志能输出“首个 parser packet 已产出”。
3. 日志能输出“首个 decoded frame 成功”。
4. 画面正常显示，不再停留在占位文本。
5. 连续运行至少 `1` 分钟不崩溃。
6. `coredumpctl --since <测试开始时间>` 不应再出现新的 `Alliance.Client` `SIGSEGV` 记录。

### 7.4 失败时的下一步定位

如果切换到 parser 路线后仍失败，则按以下顺序继续定位：

1. 检查 parser 是否持续输出 `0` packet。
2. 检查 decoder 是否持续返回 `EAGAIN` 或 `invalid data`。
3. 检查流内是否确实存在 in-band `VPS/SPS/PPS`。
4. 如仍出现 native 崩溃，则停止继续微调当前 unsafe C# 互操作，改为：
   - 使用更完整的 FFmpeg 绑定。
   - 或引入最小 native shim，把 parser + decode 留在 C/C++ 层实现。

## 8. 风险与注意事项

### 8.1 ABI 风险

当前 `AVCodecContextExt` overlay 本身就是 ABI 风险点。

修复方案中应尽量删除这部分逻辑，而不是继续扩大它的使用范围。

### 8.2 流边界假设风险

即使发送端声称每个 `frameId` 是一帧，也不能再依赖“重组后可直接送 decoder”这个假设。

parser 的目标就是把这个假设从实现中拿掉。

### 8.3 性能风险

引入 parser 和 packet 复制后，CPU 开销会略有增加。

但当前问题是稳定性阻塞项。相比于继续接受原生崩溃，这部分开销是可接受的。

后续若需要优化，可再考虑复用 native 缓冲，而不是回退到无 parser 的实现。

## 9. 最终结论

本次问题的核心不是“UI 不显示”，也不是“UDP 收不到数据”，而是：

**当前客户端把 UDP 重组后的 HEVC 字节块过于乐观地当成可直接送入 FFmpeg 的完整 packet 处理，导致 decoder 在首个关键访问单元进入解码时发生原生段错误。**

最合理、最可维护的修复方案是：

**保留 UDP 重组层，废弃手工 SPS/PPS/IDR 推断路径，引入 FFmpeg parser，把字节流先标准化为 decoder 可接受的 packet，再进入解码。**

这也是当前最有把握同时解决“无图像显示”和“数秒后崩溃”两个现象的方案。
