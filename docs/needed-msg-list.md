# MQTT 消息列表

1. GameStatus
message GameStatus {
 optional uint32 current_round = 1;
 optional uint32 total_rounds = 2;
 optional uint32 red_score = 3;
 optional uint32 blue_score = 4;
 optional uint32 current_stage = 5;
 optional int32 stage_countdown_sec = 6;
 optional int32 stage_elapsed_sec = 7；
 optional bool is_paused = 8;
 optional uint32 game_result = 9;
 optional uint32 end_reason =10;
}

2. GlobalUnitStatus
message GlobalUnitStatus {
 optional uint32 base_health = 1;
 optional uint32 base_status = 2;
 optional uint32 base_shield = 3;
 optional uint32 outpost_health = 4;
 optional uint32 outpost_status = 5;
 optional uint32 enemy_base_health = 6;
 optional uint32 enemy_base_status = 7;
 optional uint32 enemy_base_shield=8;
 optional uint32 enemy_outpost_health = 9;
 optional uint32 enemy_outpost_status = 10;
 repeated uint32 robot_health = 11;
 repeated int32 robot_bullets = 12;
 optional uint32 total_damage_ally = 13;
 optional uint32 total_damage_enemy = 14;
}

3. GlobalLogisticsStatus
message GlobalLogisticsStatus {
 optional uint32 remaining_economy = 1;
 optional uint64 total_economy_obtained = 2;
 optional uint32 tech_level = 3;
 optional uint32 encryption_level = 4;
}

4. GlobalSpecialMechanism
message GlobalSpecialMechanism {
 repeated uint32 mechanism_id = 1;
 repeated int32 mechanism_time_sec = 2;
}

5. Event
message Event {
 optional int32 event_id = 1;/
 optional string param = 2;
}

6. RobotStaticStatus
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

7. RobotDynamicStatus
message RobotDynamicStatus {
 optional uint32 current_health = 1;
 optional float current_heat = 2;
 optional float last_projectile_fire_rate = 3;
 optional uint32 current_chassis_energy = 4;
 optional uint32 current_buffer_energy = 5;
 optional uint32 current_experience = 6;
 optional uint32 experience_for_upgrade = 7;
 optional uint32 total_projectiles_fired = 8;
 optional uint32 remaining_ammo = 9;
 optional bool is_out_of_combat = 10;
 optional uint32 out_of_combat_countdown = 11;
 optional bool can_remote_heal = 12;
 optional bool can_remote_ammo = 13;
}

8. Buff
message Buff {
 optional uint32 robot_id = 1;
 optional uint32 buff_type = 2;
 optional int32 buff_level = 3;
 optional uint32 buff_max_time = 4;
 optional uint32 buff_left_time = 5;
}

9. RadarInfoToClient
message RadarInfoToClient {
 repeated uint32 RadarSingleRobotInfo;
}
message RadarSingleRobotInfo
{
 optional uint32 target_pos_x=1;
 optional uint32 target_pos_y=2;
 optional uint32 is_high_light=3;
}