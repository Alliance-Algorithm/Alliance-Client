# 更丰富信息显示 — 实现计划

> 来源：`information.md` + 需求澄清（2026-07-23）

## 1. 目标

在现有 HUD 上补齐基地护盾、前哨/堡垒侧边提示、基地被攻击 toast、Reverse 区（Buff + 弹速/热量/能量）、当前机器人面板改版、敌方无人机「被反制」状态。

## 2. 范围

| 做 | 不做（本轮） |
|----|----------------|
| 基地护盾（同条双色比例条） | 己方空中支援被反制 |
| 前哨站可重建/重建中侧边面板 | `AirSupportStatusSync` 新消息 |
| 堡垒占领侧边面板 | MAP / EVENTS 面板内容 |
| 顶部「基地遭到攻击」5s toast | 新 MQTT topic / proto 字段 |
| RESERVED 拆左右 | |
| 当前机器人面板三行改版 | |
| 敌方无人机 `【被反制】` | |

## 3. 规则约定

### 3.1 基地血条 + 护盾（同条比例）

- 总长语义：`total = HP + Shield`
- `HP 占比 = HP / total`，`护盾占比 = Shield / total`
- 例：5000+150 → 5000/5150 与 150/5150；4000+2000 → 4000/6000 与 2000/6000
- 同一底槽内：左段队伍色（血量），右段黄色（护盾）
- 文案：`当前血量（护盾量）`，如 `5000（150）`
- 前哨站：仅当前血量，不显示最大血量，无护盾段

### 3.2 前哨站重建侧边

- 数据：`outpost_status` / `enemy_outpost_status`
- `4` 可重建：侧边提示（己方左 / 敌方右），在赛场信息面板下方
- `5` 重建中：其下进度条，本地满 **10s**，**5s** 阶段点
- 离开 `5` 连续 **≥1s** 清零进度与面板

### 3.3 堡垒占领

- `GlobalSpecialMechanism`：`1` 己方被占 / `2` 对方被占
- 侧边叠在前哨提示下方；进度满 **20s**；文案「己方/对方堡垒正在被占领」

### 3.4 基地遭到攻击

- Event id=`11` → 顶部 toast，持续 **5s**

### 3.5 敌方空支被反制（己方不做）

- Event id=`8` → 置位 `EnemyAirSupportCountered = true`
- Event id=`7`（对方呼叫空中支援）→ 清除
- 仅敌方机器人列表无人机槽（relative id=6）显示 `【被反制】`

### 3.6 Reverse（原 RESERVED）

| 区 | 内容 |
|----|------|
| 左 | 当前机器人 Buff 竖排，仅 type 1/2/3/5；格式 `【攻击】+50%（2s）` 等（冷却无 `%`） |
| 右 | 上次弹速、热量冷却 `N/s`、热量条+`当前 \| 最大`、底盘剩余能量 |

### 3.7 当前机器人面板

- 名称：`红方1号-英雄` / `蓝方3号-步兵`
- 行1：名称 + 性能体系（发射 + 底盘）
- 行2：血条 + 血量
- 行3：等级、升级经验、剩余发弹量

性能枚举：

- 发射：1 冷却优先 / 2 爆发优先 / 3 英雄近战优先 / 4 英雄远程优先
- 底盘：1 血量优先 / 2 功率优先 / 3 英雄近战优先 / 4 英雄远程优先

## 4. 架构

```
MQTT (已有 topics)
  → MqttTelemetryService（基本不动）
  → TelemetryStore（映射 + 本地状态机）
  → TelemetrySnapshot
  → HUD 控件绑定
```

## 5. 文件改动清单

| 文件 | 动作 |
|------|------|
| `Features/Telemetry/TelemetrySnapshot.cs` | 扩展 Team/Robot/Current/Reverse/SideAlert/Toast |
| `Features/Telemetry/TelemetryStore.cs` | 映射与状态机 |
| `Features/Hud/TeamPanel.axaml` | 基地双色条 + 文案 |
| `Features/Hud/CurrentRobotPanel.axaml` | 三行布局 |
| `Features/Hud/ReversePanel.axaml`(+.cs) | 新建 |
| `Features/Hud/SideAlertStack.axaml`(+.cs) | 新建 |
| `Features/Hud/HudOverlay.axaml` | 挂载 Reverse / toast / 侧边 |
| `Features/Hud/RobotStatusBar*` | 反制 StateText |
| `Styles/Hud.axaml` | 样式 |
| `tests/.../TelemetryMappingTests.cs` | 用例更新与新增 |

## 6. 实现顺序

1. Snapshot 模型
2. TelemetryStore 映射与状态机
3. TeamPanel / CurrentRobot / Reverse
4. 侧边 alert + 顶部 toast
5. 敌方无人机反制
6. 测试与编译

## 7. 验收

- [ ] 基地同条比例双色 + `HP（Shield）`；前哨无最大血量
- [ ] 前哨 4/5 侧边行为与 10s/1s 清零
- [ ] 堡垒 20s 进度与文案
- [ ] 基地攻击顶部 5s
- [ ] 敌方无人机反制：8 置位、7 解除；己方不变
- [ ] Reverse 左四类 Buff；右弹速/冷却/热量/底盘
- [ ] 当前机器人三行 + 性能 + 中文名
