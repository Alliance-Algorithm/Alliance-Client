# RMCS 图像分包传输协议

## 1. 概述

本协议定义了 RMCS 图像数据的分包传输格式，包括 JPEG 压缩、数据分片、以及基于柯西-里德所罗门前向纠错编码（Cauchy Reed-Solomon FEC）的冗余校验规则。接收方可据此完成收包、纠错与 JPEG 拼合重建。

---

## 2. 核心常量

| 常量 | 值 | 说明 |
|------|-----|------|
| `kPacketSize` | 300 | 单包总字节数 |
| `kHeaderSize` | 4 | 包头字节数 |
| `kPayloadSize` | 296 | 有效载荷字节数（300 − 4） |
| `kMaxPacketCount` | 255 | 单帧最大包数（无符号 8 位整数上限） |
| `kFecDataPerGroup`（默认） | 10 | 每组数据包数量 k |
| `kFecFecPerGroup`（默认） | 3 | 每组校验包数量 r |
| `kStatusStart` | 0x01 | 起始标记 |
| `kStatusEnd` | 0x02 | 结束标记 |
| `kStatusFec` | 0x03 | 校验包标记 |
| `kStatusMask` | 0x0F | 状态字段低 4 位掩码 |
| `kKPrimeShift` | 4 | 尾组数据包数在状态字段中的左移位数 |

> **可配置项**：每组数据包数 `fec_data_per_group`（k）、每组校验包数 `fec_fec_per_group`（r）、JPEG 压缩质量 `jpeg_quality`（默认 95）、消息类型 `message_type` 均通过 ROS 参数配置，运行时不可变。

---

## 3. 数据包格式

每包固定 **300** 字节，结构如下：

```
偏移量 │ 字节数 │ 字段名            │ 类型      │ 说明
───────┼───────┼──────────────────┼───────────┼────────────────────────────
0      │ 1     │ message_type     │ uint8_t   │ 消息类型。背景图为 0x01，轨迹为 0x02
1      │ 1     │ status           │ uint8_t   │ 状态标记（详见 §3.1）
2      │ 1     │ image_sequence   │ uint8_t   │ 帧序号，范围 [0, 255]，每帧加 1，溢出回 0
3      │ 1     │ packet_sequence  │ uint8_t   │ 帧内包序号，范围 [0, N−1]，跨数据包和校验包统一连续编号
4~299  │ 296   │ payload          │ uint8_t[296] │ 有效载荷（JPEG 分片数据或校验数据）
```

### 3.1 状态字段（字节 1）

#### 数据包

| 取值 | 触发条件 | 说明 |
|------|----------|------|
| `0x01` | `packet_sequence == 0` | 帧的第一个数据包（全局唯一） |
| `0x00` | `packet_sequence > 0` | 所有其余数据包 |

> 数据包**不使用 `0x02`（结束标记）**。帧结束由最后一个校验包标识。

#### 校验包

```
bit[7:4] = k_prime     // 最后一组的数据包数量
bit[3:0] = 状态标记     // 0x03：中间校验包，0x02：末尾校验包
```

校验包的状态字节由函数 `make_fec_status(k_prime, is_last_fec)` 计算：

```cpp
status = (k_prime << 4) | (is_last_fec ? 0x02 : 0x03);
```

其中 `is_last_fec`（是否为末尾校验包）当且仅当该包属于**最后一组** **且** 是组内第 r 个（即最后一个）校验包时为真。具体判断条件为：`is_last_group && f == r − 1`。

> **关键**：`k_prime` 在 `build_fec_packets()` 函数开头（packetizer.hpp 第 104 行）一次性计算，**所有校验包的高 4 位均携带同一个 `k_prime` 值**，即最后一组的数据包数量。

#### 快速判定公式

```
(status & 0x0F) == 0x01  →  帧起始（第一个数据包）
(status & 0x0F) == 0x02  →  帧结束（最后一个校验包）
(status & 0x0F) == 0x00  →  非首数据包
(status & 0x0F) == 0x03  →  非末尾校验包
```

---

## 4. 编码流程（ImagePacketizer::update）

```
原始图像（cv::Mat 格式）
  │
  ▼  cv::imencode(".jpg", quality=jpeg_quality_)
JPEG 字节流
  │
  ▼  packetize_jpeg(jpeg_bytes, image_seq_, message_type_)
D 个原始数据包（每个 payload 296 字节，末尾不足部分填 0）
  │
  ▼  build_fec_packets()
分组 → 柯西 RS 编码 → 交织输出
  │
  ▼
N = D + G×r 个最终包
  │
  ▼  发布到输出接口
packets_ / sequence_
```

### 4.1 分组规则（build_fec_packets 第 100–109 行）

```
设：
  D = 原始数据包总数（data_payloads.size()）
  k = fec_data_per_group_
  r = fec_fec_per_group_

计算：
  full_groups = D / k
  k_prime     = D % k

  if (k_prime == 0 && full_groups > 0)：
      k_prime = k;              // 数据包总数恰为 k 的整数倍
      total_groups = full_groups;
  else if (k_prime > 0)：
      total_groups = full_groups + 1;  // 有尾组
  else：
      total_groups = 0;         // D == 0，无包可发
```

- 前 `total_groups − 1` 组（若存在）：每组 k 个数据包 + r 个校验包
- 最后一组（尾组）：k_prime 个数据包 + r 个校验包
- 总输出包数 N = D + total_groups × r

### 4.2 输出包序列排布

输出按组顺序依次写入，`packet_sequence` 从 0 开始全局递增：

```
第 0 组  数据包 (kg=k)  → packet_sequence 0 .. k−1
第 0 组  校验包 (r 个)  → packet_sequence k .. k+r−1

第 1 组  数据包 (kg=k)  → packet_sequence k+r .. 2k+r−1
第 1 组  校验包 (r 个)  → packet_sequence 2k+r .. 2k+2r−1

...

第 g 组 (g < G−1)        → packet_sequence 起始 = g(k+r)
  ├ 数据包: [g(k+r), g(k+r)+k−1]
  └ 校验包: [g(k+r)+k, g(k+r)+k+r−1]

第 G−1 组（尾组）          → packet_sequence 起始 = (G−1)(k+r)
  ├ 数据包: [(G−1)(k+r), (G−1)(k+r)+k_prime−1]
  └ 校验包: [(G−1)(k+r)+k_prime, (G−1)(k+r)+k_prime+r−1]
```

---

## 5. 前向纠错编码（Reed-Solomon FEC）

### 5.1 有限域 GF(256) 定义

$$
\mathrm{GF}(256) = \mathrm{GF}(2)[x] / (x^8 + x^4 + x^3 + x^2 + 1)
$$

生成多项式为 `0x11D`。

- 加法与减法：均按位异或（XOR）
- 乘法：通过对数表 `g_log_table` 和指数表 `g_exp_table` 查表实现。指数表长度为 512，后半段为首 256 项的循环复制。

### 5.2 柯西编码矩阵

对任意一组（包含 kg 个数据包），柯西矩阵第 `fec_idx` 行、第 `data_idx` 列的元素在 GF(256) 中计算：

$$
C_{i,\,j} = \frac{1}{x_i \oplus y_j}
$$

其中：

$$
\begin{aligned}
x_i &= kg + i, \quad i \in [0,\, r-1] \\
y_j &= j,          \quad j \in [0,\, kg-1]
\end{aligned}
$$

**注意**：不同组的 kg 值可能不同（尾组 kg = k_prime），因此柯西矩阵也不同。**各组校验完全独立**，不可跨组混合恢复。

### 5.3 编码计算

校验包 `fec_idx ∈ [0, r−1]` 的 payload 中第 `byte` 个字节为：

$$
\mathrm{fec}[\;fec\_idx\;][\;byte\;] = \bigoplus_{d=0}^{kg-1} \Big( C_{fec\_idx,\;d} \otimes \mathrm{data}[\;d\;][\;byte\;] \Big)
$$

其中 ⊕ 为 GF(256) 加法（XOR），⊗ 为 GF(256) 乘法。

### 5.4 解码恢复

各组独立恢复。若组内丢失了 e 个包（e ≤ r），恢复步骤如下：

1. 从该组的 (kg + r) 个包中任选 kg 个未丢失的包
2. 从柯西编码矩阵中取出这 kg 个包对应的行，构造一个 kg×kg 的子矩阵
3. 在 GF(256) 中对子矩阵求逆
4. 将逆矩阵乘以收到的 kg 个 payload，即得到原始的 kg 个数据包

---

## 6. 接收端解包流程

### 6.1 帧边界检测

```
① 收到满足 (status & 0x0F) == 0x01 的包 → 新帧开始（同时记录 image_sequence）
② 持续收集直到收到满足 (status & 0x0F) == 0x02 的包 → 帧结束
③ 从帧内任意一个校验包的 status 高 4 位提取 k_prime 值
④ 帧内总包数 N = 最后一个包的 packet_sequence + 1
```

### 6.2 分组结构推导

```
已知 k、r、k_prime、N，推导：

总组数      G = total_groups = (N + k − k_prime) / (k + r)
数据包总数   D = N − G × r
```

> 验算：
> $$
> \begin{aligned}
> D &= (G-1) \times k + k\_prime \\
> N &= D + G \times r = G \times (k + r) - k + k\_prime \quad\checkmark
> \end{aligned}
> $$

### 6.3 数据提取与拼接

```
idx = 0
for g = 0 .. G-1:
    kg = (g == G-1) ? k_prime : k

    // 根据 §4.2 的 packet_sequence 范围提取该组所有数据包的 payload
    // 若有丢包：用该组内任意 kg 个剩余包 + 柯西逆矩阵进行恢复

    拼接到 JPEG 缓冲区：
        memcpy(jpeg_buf + idx × kPayloadSize, payload, kPayloadSize)
        idx += kg

去除末尾填零字节：
    有效 JPEG 长度 = D × kPayloadSize − (最后一个数据包中未填充部分的零字节)
    也可通过搜索 JPEG EOI 标记 0xFFD9 定位实际结尾
```

---

## 7. 配置参数

| ROS 参数 | 类型 | 默认值 | 说明 |
|----------|------|--------|------|
| `interface_name` | string | 无，必填 | 输入输出接口名称 |
| `input_interface_name` | string | 同上 | 输入接口名称（可选覆盖） |
| `message_type` | int [0, 255] | 1 | 消息类型：1 为背景图，2 为轨迹 |
| `fec_data_per_group` | int | 10 | 每组数据包数量 k |
| `fec_fec_per_group` | int | 3 | 每组校验包数量 r |
| `jpeg_quality` | int | 95 | JPEG 压缩质量 [0, 100] |

---

## 8. 示例

> 以下示例取 k=10，r=3。**请特别注意：所有校验包状态字段的高 4 位均为同一个 k_prime 值（最后一组的数据包数量），不会因所属组不同而变化。**

### 示例 1：1 个数据包（D=1, k_prime=1, G=1, N=4）

| packet_sequence | 角色 | status | 说明 |
|-----------------|------|--------|------|
| 0 | 数据 | `0x01` | 首包，也是唯一的数据包 |
| 1 | 校验 | `0x13` | 高 4 位 k_prime=1, 低 4 位=0x03（非末尾校验） |
| 2 | 校验 | `0x13` | 同上 |
| 3 | 校验 | `0x12` | 高 4 位 k_prime=1, 低 4 位=0x02（末尾校验，帧结束） |

### 示例 2：10 个数据包（D=10, k_prime=10, G=1, N=13）

| packet_sequence | 角色 | status | 说明 |
|-----------------|------|--------|------|
| 0 | 数据 | `0x01` | 首包 |
| 1~9 | 数据 | `0x00` | 中间数据包 |
| 10~11 | 校验 | `0xA3` | 高 4 位 k_prime=10, 低 4 位=0x03 |
| 12 | 校验 | `0xA2` | 高 4 位 k_prime=10, 低 4 位=0x02（末尾，帧结束） |

### 示例 3：12 个数据包（D=12, k_prime=2, G=2, N=18）

| packet_sequence | 角色 | status | 说明 |
|-----------------|------|--------|------|
| 0 | 数据 | `0x01` | 首包 |
| 1~9 | 数据 | `0x00` | 第 0 组其余数据包（该组 kg=10） |
| **10~11** | **校验** | **`0x23`** | **第 0 组的校验包，但高 4 位同为 k_prime=2** |
| 12 | 校验 | `0x23` | 同上，第 0 组的最后一个校验包 |
| 13~14 | 数据 | `0x00` | 第 1 组（尾组）数据包（kg=2） |
| 15~16 | 校验 | `0x23` | 第 1 组前 2 个校验包 |
| 17 | 校验 | `0x22` | 第 1 组末校验包，低 4 位=0x02（帧结束） |

> **特别注意**：packet_sequence 10~12 虽然属于第 0 组，但其 status 高 4 位是 2 而非 10。**k_prime 永远等于最后一组的数据包数，所有校验包统一携带该值。**

---

## 9. 附录：GF(256) 表的生成

```cpp
// 初始化代码（packetizer.hpp 第 40–50 行）
void init_gf256() {
    uint16_t value = 1;
    for (uint16_t i = 0; i < 255; ++i) {
        exp_table[i] = (uint8_t)value;
        log_table[value] = (uint8_t)i;
        value <<= 1;
        if (value >= 256) value ^= 0x11D;
    }
    // 扩展至 512 项以支持查表优化
    for (uint16_t i = 255; i < 512; ++i)
        exp_table[i] = exp_table[i - 255];
}
```

乘法（a, b 均非零时）：

```
gf_mul(a, b) = exp_table[(log_table[a] + log_table[b]) % 255]
```

求逆：

```
gf_inv(a) = exp_table[255 - log_table[a]]
```

---

## 10. 修订记录

| 版本 | 日期 | 说明 |
|------|------|------|
| 1.0 | 2026-07-21 | 初版 |
| 1.1 | 2026-07-21 | 校对修正：所有校验包 k_prime 统一；示例重新计算；message_type 说明完善 |
