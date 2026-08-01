# 更丰富的信息显示

1. 基地护盾值

2. 己方、对方前哨站重建标识和己方、对方堡垒被占领计时显示

3. 空中支援被反制情况

4. 基地遭到攻击

5. 上一次弹丸射速、当前热量

6. 当前机器人持有buff

## 详细布局

1. 当前的基地血量需要带护盾值，护盾条额外接在基地血条的后面，base的数值显示为 当前血量（护盾量），例如5000（150），不再显示最大基地血量（前哨站也不再显示最大血量）

2. 己方、对方前哨站重建标识：
GlobalUnitStatus中outpost_status 枚举值：
0：无敌
1：存活，解除无敌，中部装甲旋转
2：存活，解除无敌，中部装甲停转
3：被击毁，不可重建
4：被击毁，可重建
5：被击毁，重建中
触发可重建时，在视频面板的侧边弹出一个面板（我方前哨站显示左侧，对方前哨站右侧），位置在赛场信息面板的下方。
触发重建中时，在上面面板那个下方弹出一个面板，然后本地计算占领时间，并展示进度条，最大为10s，并在5s处打一个阶段点（一定时间不在重建中了需要清零，时间规定为1s，进度条颜色和双方血条颜色形式统一）
这样两个面板需要用醒目的颜色提示

3. 己方、对方堡垒被占领计时显示：
message GlobalSpecialMechanism {
repeated uint32 mechanism_id = 1;
repeated int32 mechanism_time_sec = 2;
}
mechanism_id 枚举值：
1：己方堡垒被对方占领计时
2：对方堡垒被己方占领计时

和上述的表现形式一样，从侧边弹出，弹出位置在上面那个下方。用一个进度条，进度条满时20s，面板需要使用醒目的颜色，并且需要文本标识比如：“己方堡垒正在被占领”

4. 基地遭到攻击：从顶部弹出一个框，显示该消息，持续5s

5. 上一次弹丸射速、当前热量
用reverse的右半面板显示，显示上次一弹速数值（float），热量冷却速率（格式为具体数值/s，比如30/s）和当前热量（用热量条+数字表示，数字格式为：当前热量 | 最大热量）

6. 当前机器人面板的底盘剩余能量也改到reverse右半区域，然后当前机器人的面板修改：名字改为client id对应的机器人，比如1为红方1号-英雄，103为蓝方3号-步兵，视觉呈现参考双方机器人面板。
然后需要显示当前机器人的性能体系，包括发射机构和底盘。总体布局为第一行显示名称、性能体系。第二行显示血条和血量数值，第三行显示等级、升级需要经验和剩余发弹量。

message RobotStaticStatus {
optional uint32 connection_state = 1;
optional uint32 field_state = 2;
optional uint32 alive_state = 3;
optional uint32 robot_id = 4;
optional uint32 robot_type = 5;
optional uint32 performance_system_shooter = 6;
optional uint32 performance_system_chassis = 7;
optional uint32 level = 8;
optional uint32 max_health = 9;
optional uint32 max_heat = 10;
optional float heat_cooldown_rate = 11;
optional uint32 max_power = 12;
optional uint32 max_buffer_energy = 13;
optional uint32 max_chassis_energy = 14;
}

performance_system_shooter 枚举值：
1：冷却优先
2：爆发优先
3：英雄近战优先
4：英雄远程优先
performance_system_chassis 枚举值：
1：血量优先
2：功率优先
3：英雄近战优先
4：英雄远程优先

6. 在revese左半区域中显示当前机器人持有的buff，格式为【类型】数值%（剩余时间s）

竖着显示，按照类型排列。数值需要带正负号，例如如下显示（只显示这四种）：
【攻击】+50%（2s）
【防御】-50%（4s）
【冷却】+100（6s）
【回血】+10%（8s）

## 补充约定（实现时确认）

- 基地血条：HP 与护盾按 `HP/(HP+Shield)`、`Shield/(HP+Shield)` 同条比例显示，护盾黄色
- 空中支援被反制：仅敌方无人机卡片显示「【被反制】」；Event 8 置位，Event 7（对方呼叫空中支援）解除；己方本轮不做
- Reverse 使用原 RESERVED 区域左右拆分
