# 新增飞镖控制指令

## 1. 新增接收消息（mqtt接收）

message DartSelectTargetStatusSync {
optional uint32 target_id = 1;
optional uint32 open = 2;
}

闸门状态（0：关闭，1：开启中，2：已开启）

## 2. 新增控制指令（mqtt发布）

message DartCommand {
optional uint32 target_id = 1;
optional bool open = 2;
optional bool launch_confirm = 3;
}

数据编号 数据类型 数据用途
1 uint32 目标 ID（1 为前哨站，2 为基地固定目标，3 为基地随机固定目标，4 为基地随机移动目标，5 为基地末端移动目标）
2 bool 闸门开关
3 bool 是否确认发射（默认为 0，1 为确认发射）

目标id固定设置为2

当前比赛阶段为比赛中，对方前哨站血量为0且对方基地状态不为无敌时，将闸门开启，然后launch_confirm设置为1，持续发送消息（5hz），直到闸门状态为开启中

然后60s后如果满足条件的话就再来一次，如果不满足则等到满足条件时立即再来一次

## 需求
加上这两个消息收发，然后将底部的hero的弹速，pitch，yaw面板修改成一个开关：是否自动发射飞镖（整个面板可以直接替换掉，不需要显示机器人状态消息，因为我新建了一个分支专门弄这个）

## 其他信息
base_status 枚举值：
 0：无敌
 1：解除无敌，护甲未展开
 2：解除无敌，护甲展开