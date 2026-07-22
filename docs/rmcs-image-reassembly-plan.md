# RMCS 图像拼合接收端 实现方案

## 1. 概述

接收端通过 MQTT `CustomByteBlock` topic 接收发送端编码的 300 字节 RMCS 图像数据包（详见 `rmcs_image_packet_protocol.md`），使用柯西-里德所罗门前向纠错解码恢复丢包，最终拼合为 JPEG 图像并在 Avalonia UI 窗口展示。

### 1.1 设计约束

| 约束 | 取值/方式 |
|------|----------|
| 每组数据包数 k | 10（硬编码） |
| 每组校验包数 r | 3（硬编码） |
| 单包大小 | 300 字节（4 字节头 + 296 payload） |
| 超时阈值 | 0.5s（同一 message_type + image_sequence 无新包） |
| FEC 实现 | 纯 C#，GF(256) 柯西 RS 矩阵求逆 |
| UI 入口 | MainWindow Settings 下方新增 "Image" 按钮 |
| 窗口模式 | `Show(owner)` 非模态，可调整尺寸 |
| 展示内容 | 同页面并排显示最新背景图（message_type=0x01）和轨迹图（message_type=0x02） |

---

## 2. 架构分层

```
MQTT CustomByteBlock (300-byte raw)
        │
        ▼
  TelemetryStore.ApplyCustomByteBlock()
        │  转发 raw bytes
        ▼
  RmcsImageProcessor.Feed(byte[] data)
        │  解析头部 → 按 message_type 路由
        ├──────────────────────────────────┐
        ▼                                  ▼
  FrameAssembler (type=0x01)      FrameAssembler (type=0x02)
        │  帧缓冲 + 超时管理                 │
        │  分组推导 + FEC 恢复               │
        │  JPEG 拼接                         │
        ▼                                  ▼
  RmcsImageStore.BackgroundBitmap   RmcsImageStore.TrajectoryBitmap
        │                                  │
        └──────────┬───────────────────────┘
                   ▼
            ImageWindow (Avalonia Window)
            绑定 → Image 控件展示
```

---

## 3. 详细模块设计

### 3.1 Gf256.cs —— GF(256) 有限域

**路径**：`src/Alliance.Client/Features/RmcsImage/Gf256.cs`

静态类，提供 GF(256) 的乘法和求逆运算。

- 生成多项式：`x^8 + x^4 + x^3 + x^2 + 1`（`0x11D`）
- `exp_table[512]`、`log_table[256]` 静态构造函数初始化
- `Mul(uint8 a, uint8 b) -> uint8`：查表 `exp[(log[a] + log[b]) % 255]`，零元特判
- `Inv(uint8 a) -> uint8`：`exp[255 - log[a]]`

### 3.2 CauchyRsDecoder.cs —— 柯西 RS 解码器

**路径**：`src/Alliance.Client/Features/RmcsImage/CauchyRsDecoder.cs`

对一组内的丢包进行恢复。

**输入**：
- `kg`（int）：本组数据包数量
- `r`（int）：本组校验包数量（固定 3）
- `survivingPayloads`（`byte[][]`）：kg 个存活包的 payload
- `survivingIndices`（`int[]`）：这些包在组内的原始索引（0 = 第 1 个数据包，...，kg+r-1 = 最后一个校验包）

**输出**：
- `byte[][]`：kg 个原始数据包 payload（按索引 0..kg-1 有序）

**算法**：

1. **构造 kg×kg 柯西子矩阵**：
   ```
   对于每个 (row, col)，其中 row 对应 survivingIndices[row]，col 对应 0..kg-1：
     x = kg + (survivingIndices[row] - kg)    // 当 survivingIndices[row] >= kg 时为校验包行
                                            // 当 survivingIndices[row] < kg 时为数据包行
                                           // 校验包行取 x = kg + i
         实际上更简单的构造方式：
         若 survivingIndex < kg（数据包），该行为单位矩阵行
         若 survivingIndex >= kg（校验包），该行为柯西矩阵行
   ```

2. **构造增广矩阵 `[M | I]`**：kg × 2kg 在 GF(256) 上。

3. **高斯-约当消元**：将左半变换为单位矩阵，右半即为逆矩阵。

4. **矩阵乘法**：`逆矩阵[kg×kg] × 存活payload[kg×296] = 原始data[kg×296]`。

### 3.3 FrameAssembler.cs —— 帧拼合器

**路径**：`src/Alliance.Client/Features/RmcsImage/FrameAssembler.cs`

每个 `message_type`（0x01 背景 / 0x02 轨迹）独立一个实例。

#### 3.3.1 解析数据包

从 300 字节数组中提取：
| 偏移 | 长度 | 字段 | 类型 |
|------|------|------|------|
| 0 | 1 | `message_type` | uint8 |
| 1 | 1 | `status` | uint8 |
| 2 | 1 | `image_sequence` | uint8 |
| 3 | 1 | `packet_sequence` | uint8 |
| 4 | 296 | `payload` | byte[296] |

#### 3.3.2 帧状态管理

```
Dictionary<uint8, FrameBuffer> _frames  // key = image_sequence
```

每个 `FrameBuffer`：
- `ReceivedPackets`：`Dictionary<int, byte[]>`（packet_sequence → payload）
- `LastPacketTime`：`DateTime`，每次收到包更新
- `Timer`：每次收到包重置 500ms，到期触发拼合
- `IsCompleted`：标记是否已处理

#### 3.3.3 帧边界检测

```
(status & 0x0F) == 0x01  →  新帧开始
(status & 0x0F) == 0x02  →  帧结束（立即触发拼合，取消 timer）
```

收到 `0x01` 包时，覆盖同 `image_sequence` 的旧 buffer（序列号已回绕）

#### 3.3.4 分组推导

```
已知：k = 10, r = 3, k_prime = 任一校验包 status >> 4, N = 最后一个 packet_sequence + 1

G = (N + k - k_prime) / (k + r)     // 总组数
D = N - G × r                         // 数据包总数
```

#### 3.3.5 FEC 恢复流程

```
for g = 0 .. G-1:
    kg = (g == G-1) ? k_prime : k
    组内包号范围: [g*(k+r), g*(k+r) + kg + r - 1]
    数据包子范围: [g*(k+r), g*(k+r) + kg - 1]

    统计丢失的包数 e
    if e == 0:
        直接提取 payload 拼入 JPEG buffer
    else if e <= r:
        从存活包中选取任意 kg 个
        调用 CauchyRsDecoder.Decode(kg, r, survivingPayloads, survivingIndices)
        拼入 JPEG buffer
    else:
        不可恢复（e > r），跳过该组
```

#### 3.3.6 JPEG 拼接与截断

- JPEG 总长度 = D × 296 字节
- 按全局 `packet_sequence` 顺序拼接各数据包的 payload
- 搜索 `0xFF 0xD9`（JPEG EOI 标记）定位有效结尾，去除尾部填零字节
- 解码后的 JPEG 字节数组作为输出

#### 3.3.7 线程安全

`FrameAssembler` 内部用 `lock (_gate)` 保护 `_frames` 字典。

### 3.4 RmcsImageProcessor.cs —— 入口协调器

**路径**：`src/Alliance.Client/Features/RmcsImage/RmcsImageProcessor.cs`

单例，持有：
- `FrameAssembler _backgroundAssembler`（message_type 0x01）
- `FrameAssembler _trajectoryAssembler`（message_type 0x02）
- `RmcsImageStore _store`

`Feed(byte[] data)`：
1. 头部校验：`data.Length >= 4`
2. 解析 `message_type = data[0]`
3. 路由到对应 `FrameAssembler.Feed(data)`
4. `FrameAssembler` 用事件回调返回完整 JPEG 字节
5. `Dispatcher.UIThread.InvokeAsync(() => DecodeJpegAndUpdateStore(jpegBytes, messageType))`

### 3.5 RmcsImageStore.cs —— 图像状态存储

**路径**：`src/Alliance.Client/Features/RmcsImage/RmcsImageStore.cs`

```csharp
public sealed class RmcsImageStore : ObservableObject
{
    private Bitmap? _backgroundImage;
    private Bitmap? _trajectoryImage;

    public Bitmap? BackgroundImage { get => _backgroundImage; set => SetProperty(ref _backgroundImage, value); }
    public Bitmap? TrajectoryImage { get => _trajectoryImage; set => SetProperty(ref _trajectoryImage, value); }
}
```

线程安全写入由 `RmcsImageProcessor` 通过 `Dispatcher.UIThread` 保证。

### 3.6 ImageWindow —— 展示窗口

**路径**：`src/Alliance.Client/Features/RmcsImage/ImageWindow.axaml`

```
Window
├── Title="RMCS Images"
├── CanResize="True"
├── Width="800" Height="600"
├── WindowStartupLocation="CenterOwner"
├── Background="#0A0E14"
└── Grid (2 columns)
    ├── Column 0
    │   ├── TextBlock "Background" (label)
    │   └── Image { Binding BackgroundImage }
    └── Column 1
        ├── TextBlock "Trajectory" (label)
        └── Image { Binding TrajectoryImage }
```

**ImageWindowViewModel.cs**：
- 绑定 `RmcsImageStore.BackgroundImage` / `TrajectoryImage`
- `RmcsImageStore` 通过构造函数注入

### 3.7 MainWindow 集成

#### 3.7.1 MainWindow.axaml

在 Settings button 下方新增 Image button：

```xml
<!-- 图像按钮 -->
<Border Classes="settings-btn"
        HorizontalAlignment="Left" VerticalAlignment="Top" Margin="30,70,0,0"
        PointerPressed="OnImagePressed" ZIndex="300">
    <StackPanel Orientation="Horizontal" Spacing="6">
        <TextBlock Text="🖼" FontSize="14" />
        <TextBlock Text="Image" FontSize="12" FontWeight="SemiBold" />
    </StackPanel>
</Border>
```

#### 3.7.2 MainWindow.axaml.cs

```csharp
private void OnImagePressed(object? sender, PointerPressedEventArgs e)
{
    if (DataContext is MainWindowViewModel vm)
    {
        vm.OpenImage(this);
        e.Handled = true;
    }
}
```

#### 3.7.3 MainWindowViewModel.cs

```csharp
private Window? _imageWindow;

public void OpenImage(Window owner)
{
    if (_imageWindow is { IsVisible: true })
    {
        _imageWindow.BringIntoView();
        return;
    }

    var dialog = new ImageWindow(_imageWindowViewModel);
    dialog.Closed += (_, _) => _imageWindow = null;
    _imageWindow = dialog;
    dialog.Show(owner);  // 非模态
}
```

### 3.8 DI 注册

**修改 AppBootstrapper.cs**：

```csharp
services.AddSingleton<RmcsImageStore>();
services.AddSingleton<RmcsImageProcessor>();
services.AddSingleton<ImageWindowViewModel>();
```

`RmcsImageProcessor` 依赖 `RmcsImageStore`（DI 注入）。  
`TelemetryStore` 依赖 `RmcsImageProcessor`（构造函数新增参数）。  
`MainWindowViewModel` 依赖 `ImageWindowViewModel`（构造函数新增参数）。

---

## 4. 错误处理

| 场景 | 处理 |
|------|------|
| 数据包长度 != 300 | 丢弃，日志警告 |
| message_type 未知 | 丢弃 |
| 0.5s 超时未收到末尾包 | 尝试用已有数据恢复 |
| 超时后无 k_prime（无校验包） | 跳过 FEC，仅拼接已有数据包 |
| 组内丢包 > r | 该组标记为不可恢复，跳过 |
| JPEG 解码失败 | 日志错误，丢弃该帧 |
| 同一 image_sequence 重复收首包 | 重置 buffer（旧的帧被覆盖） |

---

## 5. 文件清单

| # | 文件 | 操作 | 说明 |
|---|------|------|------|
| 1 | `src/Alliance.Client/Features/RmcsImage/Gf256.cs` | 新建 | GF(256) 表与运算 |
| 2 | `src/Alliance.Client/Features/RmcsImage/CauchyRsDecoder.cs` | 新建 | 柯西 RS 解码 |
| 3 | `src/Alliance.Client/Features/RmcsImage/FrameAssembler.cs` | 新建 | 帧缓冲 + 超时 + FEC + 拼合 |
| 4 | `src/Alliance.Client/Features/RmcsImage/RmcsImageProcessor.cs` | 新建 | 入口路由 + JPEG 解码 |
| 5 | `src/Alliance.Client/Features/RmcsImage/RmcsImageStore.cs` | 新建 | Bitmap 存储 + UI 通知 |
| 6 | `src/Alliance.Client/Features/RmcsImage/ImageWindow.axaml` | 新建 | 图像展示窗口 XAML |
| 7 | `src/Alliance.Client/Features/RmcsImage/ImageWindow.axaml.cs` | 新建 | code-behind |
| 8 | `src/Alliance.Client/Features/RmcsImage/ImageWindowViewModel.cs` | 新建 | 窗口 VM |
| 9 | `src/Alliance.Client/Features/Telemetry/TelemetryStore.cs` | 修改 | 注入 RmcsImageProcessor |
| 10 | `src/Alliance.Client/Shell/MainWindow.axaml` | 修改 | 新增 Image 按钮 |
| 11 | `src/Alliance.Client/Shell/MainWindow.axaml.cs` | 修改 | OnImagePressed handler |
| 12 | `src/Alliance.Client/Shell/MainWindowViewModel.cs` | 修改 | OpenImage 方法 |
| 13 | `src/Alliance.Client/Infrastructure/Bootstrap/AppBootstrapper.cs` | 修改 | DI 注册 |

---

## 6. 修订记录

| 版本 | 日期 | 说明 |
|------|------|------|
| 1.0 | 2026-07-21 | 初版 |
