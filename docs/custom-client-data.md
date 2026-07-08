# 自定义客户端可获取的机器人数据整理

## 一、消息来源分类

自定义客户端的数据来源主要有三种：
| 来源 | 说明 |
|------|------|
| **服务器→客户端** | 通过 MQTT 协议，Topic 为指令名，数据为 Protobuf 序列化二进制流 |
| **机器人→客户端** | 通过图传链路（0x0310），频率上限 50Hz |
| **雷达→客户端** | 通过无线链路获取敌方信息，再经服务器转发 |

---

## 二、服务器→自定义客户端（通过 MQTT 订阅）

### 1. 全局比赛状态（双方数据）

| 指令名 | 说明 | 频率 |
|--------|------|------|
| `GameStatus` | 当前局数、红蓝方得分、比赛阶段、剩余时间、暂停状态 | 5Hz |
| `Event` | 全局事件通知（击杀、前哨站摧毁、能量机关激活、飞镖命中等） | 触发式 |

### 2. 己方数据

| 指令名 | 说明 | 频率 |
|--------|------|------|
| `GlobalUnitStatus` | 己方基地/前哨站血量、状态、护盾值，己方机器人血量、发弹量、累计伤害 | 1Hz |
| `GlobalLogisticsStatus` | 己方当前经济、累计总经济、科技等级、加密等级 | 1Hz |
| `RobotStaticStatus` | 机器人固定属性：连接状态、上场状态、存活状态、ID、类型、性能体系、等级、最大血量/热量/功率等 | 1Hz |
| `RobotDynamicStatus` | 机器人实时数据：当前血量、热量、底盘能量、缓冲能量、经验值、剩余发弹量、是否脱战、是否可远程补血/补弹 | 10Hz |
| `RobotModuleStatus` | 各模块运行状态：电源、灯条、RFID、发射机构、定位、装甲、图传、电容、主控、激光检测 | 1Hz |
| `RobotRespawnStatus` | 复活状态：是否待复活、复活读条进度、是否可免费复活、金币复活信息 | 1Hz |
| `RobotPathPlanInfo` | 哨兵轨迹规划：意图、起点、增量数组、发送者ID | 触发式 |

### 3. 敌方数据

| 指令名 | 说明 | 频率 |
|--------|------|------|
| `GlobalUnitStatus` | 敌方基地/前哨站血量、状态、护盾值，敌方机器人血量、累计伤害 | 1Hz |
| `RadarInfoToClient` | 雷达发送的所有机器人位置（敌方+己方），含特殊标识情况 | 触发式 |

### 4. 双方共有/状态同步

| 指令名 | 说明 | 频率 |
|--------|------|------|
| `Buff` | Buff 效果信息（攻击/防御/冷却/功率/回血增益等） | 获得增益时触发 |
| `PenaltyInfo` | 判罚信息（黄牌/红牌/超功率/超热量等） | 触发式 |
| `RobotInjuryStat` | 一次存活期间受伤统计（弹丸/撞击/离线/判罚扣血等） | 1Hz |
| `RobotPosition` | 机器人空间坐标和朝向（x, y, z, yaw） | 1Hz |
| `GlobalSpecialMechanism` | 全局特殊机制（堡垒占领计时等） | 1Hz |
| `MapClickInfo` | 地图点击标记信息（攻击/防御/警戒/自定义标记） | 触发式 |
| `MapClickCmd` | 地图点击标记指令 | 触发式 |
| `TechCoreMotionStateSync` | 科技核心运动状态 | 1Hz |
| `DeployModeStatusSync` | 英雄部署模式状态 | 1Hz |
| `RuneStatusSync` | 能量机关状态 | 1Hz |
| `SentryStatusSync` | 哨兵姿态和弱化状态 | 1Hz |
| `RobotPerformanceSelectionSync` | 步兵/英雄性能体系状态 | 1Hz |
| `DartSelectTargetStatusSync` | 飞镖目标选择状态 | 1Hz |
| `SentryCtrlResult` | 哨兵控制指令结果反馈 | 触发式 |
| `AirSupportStatusSync` | 空中支援状态 | 1Hz |

---

## 三、客户端→服务器（通过 MQTT 发布）

| 指令名 | 说明 | 频率 |
|--------|------|------|
| `KeyboardMouseControl` | 鼠标键盘输入（位移、按键状态） | 75Hz |
| `CustomControl` | 最大 30 字节自定义数据 | 75Hz |
| `AssemblyCommand` | 工程装配指令 | 触发式 |
| `RobotPerformanceSelectionCommand` | 选择性能体系/控制方式 | 触发式 |
| `CommonCommand` | 兑换发弹量、确认复活、远程补血/补弹等 | 触发式 |
| `HeroDeployModeEventCommand` | 英雄部署模式指令 | 触发式 |
| `RuneActivateCommand` | 能量机关激活指令 | 触发式 |
| `DartCommand` | 飞镖控制指令 | 触发式 |
| `SentryCtrlCommand` | 哨兵控制指令请求 | 触发式 |
| `AirSupportCommand` | 空中支援指令 | 触发式 |
| `MapClickCmd` | 地图点击标记指令 | 触发式 |

---

## 四、机器人→自定义客户端（通过图传链路）

| 指令名 | 说明 | 频率 |
|--------|------|------|
| `CustomByteBlock` | 最大 2.4kbit 的自定义数据包（对应串口 0x0310） | 50Hz |

---

## 五、ID 编号说明

| ID | 含义 |
|----|------|
| 1 | 红方英雄 |
| 2 | 红方工程 |
| 3/4/5 | 红方步兵 |
| 6 | 红方空中 |
| 7 | 红方哨兵 |
| 10 | 红方前哨站 |
| 11 | 红方基地 |
| 101 | 蓝方英雄 |
| 102 | 蓝方工程 |
| 103/104/105 | 蓝方步兵 |
| 106 | 蓝方空中 |
| 107 | 蓝方哨兵 |
| 110 | 蓝方前哨站 |
| 111 | 蓝方基地 |

---

## 六、通信配置

- **MQTT 服务器**: `192.168.12.1:3333`
- **客户端 IP**: `192.168.12.2`
- **协议格式**: Protobuf v3（≥3.15）
- **MQTT clientID**: 需填入对应机器人 ID 编号
- **图传码流**: UDP 监听 3334 端口，HEVC 编码
