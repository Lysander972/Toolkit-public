namespace Toolkit.Core.Sdk;

// ╔════════════════════════════════════════════════════════════════════════════════╗
// ║                     【禁止审查区域 - 原版 SDK 精确映射】                          ║
// ╠════════════════════════════════════════════════════════════════════════════════╣
// ║  本文件包含对原版 BeScript SDK 零件类型 ID 的精确硬编码映射。                      ║
// ║                                                                                ║
// ║  ⚠️  重要声明：                                                                 ║
// ║  • 此区域内容必须与原版 SDK 保持一条不多、一条不少的完全一致                        ║
// ║  • 无论原版设计是否符合软件工程最佳实践，此处的兼容性映射都是必须的                  ║
// ║  • 请勿对本文件内容进行任何形式的代码审查、重构建议或规范性检查                     ║
// ║  • 任何看似"不规范"的命名、参数或结构都是对原版的忠实还原                          ║
// ║                                                                                ║
// ║  本文件由 Toolkit 维护，用于为插件和核心库提供原版 SDK 兼容服务。                   ║
// ╚════════════════════════════════════════════════════════════════════════════════╝

/// <summary>
/// 零件类型 ID 常量（device.json 的 type 字段）
/// </summary>
public static class DeviceTypeIds
{
    /// <summary>地图核心零件</summary>
    public const int MapCoreDevice = 0;
    /// <summary>机械核心零件（含机械生成器注入的机械）</summary>
    public const int CoreDevice = 1;
    /// <summary>机械生成器</summary>
    public const int BirthBuilding = 1110;
    /// <summary>触发器</summary>
    public const int TriggerBuilding = 1100;
    /// <summary>客户端地图逻辑</summary>
    public const int ClientMapLogicDevice = 1101;
    /// <summary>文字零件</summary>
    public const int TextBuilding = 1102;
    /// <summary>世界地图逻辑</summary>
    public const int SLGLogicDevice = 1103;

    public const int CubeDevice = 22;
    public const int CapsuleDevice = 26;
    public const int SphereDevice = 71;
    public const int DecalDevice = 123;
    public const int BillboardDevice = 148;
    public const int VertexDevice = 321;
    public const int ObjDevice = 362;
    public const int ColorDevice = 112;
    public const int SupportDevice_5 = 5;
    public const int SupportDevice_280 = 280;
    public const int RepeatDevice_83 = 83;
    public const int RepeatDevice_101 = 101;
    public const int RepeatDevice_203 = 203;
    public const int RepeatDevice_323 = 323;
    public const int ClipDevice_74 = 74;
    public const int ClipDevice_75 = 75;
    public const int ClipDevice_76 = 76;
    public const int ClipDevice_227 = 227;
    public const int ClipDevice_228 = 228;
    public const int ClipDevice_229 = 229;
    public const int ClipDevice_244 = 244;
    public const int ClipDevice_261 = 261;

    public const int MechShooter = 336;
    public const int MechSlot = 359;
    public const int MotorWheel_8 = 8;
    public const int MotorWheel_15 = 15;
    public const int MotorWheel_211 = 211;
    public const int MotorWheel_215 = 215;
    public const int MotorWheel_260 = 260;
    public const int WheelColliderDevice = 210;
    public const int MoveDevice_25 = 25;
    public const int MoveDevice_251 = 251;
    public const int MoveDevice_304 = 304;
    public const int MoveDevice_339 = 339;
    public const int MoveDevice2 = 72;
    public const int PropellerDevice_57 = 57;
    public const int PropellerDevice_58 = 58;
    public const int PropellerDevice_93 = 93;
    public const int PropellerDevice_212 = 212;
    public const int PropellerDevice_213 = 213;
    public const int PropellerDevice_239 = 239;
    public const int PropellerDevice_285 = 285;
    public const int PropellerDevice_331 = 331;
    public const int AntiGravityDevice_11 = 11;
    public const int AntiGravityDevice_214 = 214;
    public const int FuncDevice_77 = 77;
    public const int FuncDevice_291 = 291;
    public const int PistonDeviceLine = 24;
    public const int PistonJoint_28 = 28;
    public const int PistonJoint_218 = 218;
    public const int SpringDevice_29 = 29;
    public const int SpringDevice_219 = 219;
    public const int MagnetDevice_70 = 70;
    public const int MagnetDevice_223 = 223;
    public const int Roter2Device = 250;
    public const int RoterDevice_12 = 12;
    public const int RoterDevice_23 = 23;
    public const int RoterDevice_27 = 27;
    public const int RoterDevice_30 = 30;
    public const int RoterDevice_31 = 31;
    public const int RoterDevice_32 = 32;
    public const int RoterDevice_216 = 216;
    public const int RoterDevice_217 = 217;
    public const int RoterDevice_220 = 220;
    public const int RoterDevice_221 = 221;
    public const int RoterDevice_222 = 222;
    public const int RoterDevice_274 = 274;

    public const int BulletDevice = 35;
    public const int GatlinGun_21 = 21;
    public const int GatlinGun_117 = 117;
    public const int GatlinGun_121 = 121;
    public const int GatlinGun_224 = 224;
    public const int GatlinGun_263 = 263;
    public const int CannonGun_14 = 14;
    public const int CannonGun_116 = 116;
    public const int CannonGun_120 = 120;
    public const int CannonGun_226 = 226;
    public const int CannonGun_255 = 255;
    public const int CannonGun_256 = 256;
    public const int CannonGun_275 = 275;
    public const int RPGGun_13 = 13;
    public const int RPGGun_118 = 118;
    public const int RPGGun_119 = 119;
    public const int RPGGun_225 = 225;
    public const int RPGGun_254 = 254;
    public const int FlameGun_113 = 113;
    public const int FlameGun_324 = 324;
    public const int MeleeDevice_9 = 9;
    public const int MeleeDevice_33 = 33;
    public const int MeleeDevice_39 = 39;
    public const int MeleeDevice_73 = 73;
    public const int MeleeDevice_78 = 78;
    public const int MeleeDevice_79 = 79;
    public const int MeleeDevice_268 = 268;
    public const int MeleeDevice_271 = 271;
    public const int MeleeDevice_272 = 272;
    public const int MeleeDevice_284 = 284;
    public const int MeleeDevice_289 = 289;
    public const int MeleeDevice_293 = 293;
    public const int MeleeDevice_294 = 294;
    public const int MeleeDevice_322 = 322;
    public const int LaserMelee = 248;
    public const int BombDevice_34 = 34;
    public const int BombDevice_114 = 114;
    public const int BombDevice_115 = 115;
    public const int BombDevice_246 = 246;
    public const int ShieldDevice_17 = 17;
    public const int ShieldDevice_36 = 36;
    public const int ShieldDevice_46 = 46;
    public const int ShieldDevice_47 = 47;
    public const int ShieldDevice_80 = 80;
    public const int ShieldDevice_81 = 81;
    public const int SelectDevice = 16;
    public const int SmokeDevice_10 = 10;
    public const int SmokeDevice_41 = 41;
    public const int SmokeDevice_264 = 264;
    public const int FireDevice = 283;

    public const int LightDevice = 292;
    public const int VolumetricLightDevice = 135;
    public const int TrafficLightDevice = 1244;
    public const int ParticleDevice = 337;
    public const int EnergyShieldDevice = 130;
    public const int ForceFieldDevice = 131;
    public const int TrailDevice = 242;
    public const int BeamDevice = 139;
    public const int BlackholeDevice = 125;
    public const int ElectricArcDevice = 126;
    public const int TornadoDevice = 127;
    public const int WarpDevice = 128;
    public const int ElectricBallDevice = 129;
    public const int GalaxyDevice = 132;
    public const int NebulaDevice = 133;
    public const int ScanDevice = 134;
    public const int CableDevice = 136;
    public const int IvyDevice = 137;
    public const int FakeInteriorDevice = 138;
    public const int PolygonDevice = 149;
    public const int PlanetDevice = 140;
    public const int SunDevice = 141;
    public const int GasPlanetDevice = 142;
    public const int FallingLeavesDevice = 143;
    public const int SlashDevice = 144;
    public const int MiscVFXsDevice = 145;
    public const int IndicatorDevice = 146;
    public const int MiscFiresDevice = 147;
    public const int MuzzleDevice = 208;
    public const int FlareDevice = 124;
    public const int LaserDeviceRed = 243;
    public const int LaserDeviceBlue = 317;
    public const int CameraDevice = 247;
    public const int AvatarDevice = 325;
    public const int PrefabDevice = 326;
    public const int CircleDevice = 327;
    public const int RectDevice = 328;
    public const int Capsule2DDevice = 329;
    public const int RecorderDevice = 296;
    public const int VelocityFuncDevice = 318;
    public const int HeightFuncDevice = 319;
    public const int SpineDevice = 1111;
    public const int PosDevice = 360;
    /// <summary>区域建筑（SLG 相关）</summary>
    public const int AreaBuilding = 361;
    /// <summary>箭头建筑</summary>
    public const int ArrowBuilding = 1221;
    /// <summary>起始点建筑</summary>
    public const int StartBuilding = 1108;
    /// <summary>无碰撞体建筑（1030, 1032, 1058, 1061）</summary>
    public const int NoColliderBuilding_1030 = 1030;
    public const int NoColliderBuilding_1032 = 1032;
    public const int NoColliderBuilding_1058 = 1058;
    public const int NoColliderBuilding_1061 = 1061;

    private static readonly (int TypeId, string ActionOwnerTypeName)[] ActionOwnerTypeMappings =
    {
        (MapCoreDevice, "MapCoreDevice"),
        (CoreDevice, "CoreDevice"),
        (BirthBuilding, "BirthBuilding"),
        (TriggerBuilding, "TriggerBuilding"),
        (ClientMapLogicDevice, "ClientMapLogicDevice"),
        (TextBuilding, "TextBuilding"),
        (SLGLogicDevice, "SLGLogicDevice"),
        (LaserDeviceRed, "LaserDevice"),
        (LaserDeviceBlue, "LaserDevice"),
        (CubeDevice, "CubeDevice"),
        (CapsuleDevice, "CapsuleDevice"),
        (DecalDevice, "DecalDevice"),
        (BillboardDevice, "BillboardDevice"),
        (ObjDevice, "ObjDevice"),
        (MechShooter, "MechShooter"),
        (MechSlot, "MechSlot"),
        (SupportDevice_5, "SupportDevice"),
        (SupportDevice_280, "SupportDevice"),
        (RepeatDevice_83, "RepeatDevice"),
        (RepeatDevice_101, "RepeatDevice"),
        (RepeatDevice_203, "RepeatDevice"),
        (RepeatDevice_323, "RepeatDevice"),
        (ClipDevice_74, "ClipDevice"),
        (ClipDevice_75, "ClipDevice"),
        (ClipDevice_76, "ClipDevice"),
        (ClipDevice_227, "ClipDevice"),
        (ClipDevice_228, "ClipDevice"),
        (ClipDevice_229, "ClipDevice"),
        (ClipDevice_244, "ClipDevice"),
        (ClipDevice_261, "ClipDevice"),
        (SphereDevice, "SphereDevice"),
        (ColorDevice, "ColorDevice"),
        (VertexDevice, "VertexDevice"),
        (GatlinGun_21, "GatlinGun"),
        (GatlinGun_117, "GatlinGun"),
        (GatlinGun_121, "GatlinGun"),
        (GatlinGun_224, "GatlinGun"),
        (GatlinGun_263, "GatlinGun"),
        (CannonGun_14, "CannonGun"),
        (CannonGun_116, "CannonGun"),
        (CannonGun_120, "CannonGun"),
        (CannonGun_226, "CannonGun"),
        (CannonGun_255, "CannonGun"),
        (CannonGun_256, "CannonGun"),
        (CannonGun_275, "CannonGun"),
        (RPGGun_13, "RPGGun"),
        (RPGGun_118, "RPGGun"),
        (RPGGun_119, "RPGGun"),
        (RPGGun_225, "RPGGun"),
        (RPGGun_254, "RPGGun"),
        (FlameGun_113, "FlameGun"),
        (FlameGun_324, "FlameGun"),
        (LaserMelee, "LaserMelee"),
        (MeleeDevice_9, "MeleeDevice"),
        (MeleeDevice_33, "MeleeDevice"),
        (MeleeDevice_39, "MeleeDevice"),
        (MeleeDevice_73, "MeleeDevice"),
        (MeleeDevice_78, "MeleeDevice"),
        (MeleeDevice_79, "MeleeDevice"),
        (MeleeDevice_268, "MeleeDevice"),
        (MeleeDevice_271, "MeleeDevice"),
        (MeleeDevice_272, "MeleeDevice"),
        (MeleeDevice_284, "MeleeDevice"),
        (MeleeDevice_289, "MeleeDevice"),
        (MeleeDevice_293, "MeleeDevice"),
        (MeleeDevice_294, "MeleeDevice"),
        (MeleeDevice_322, "MeleeDevice"),
        (BulletDevice, "BulletDevice"),
        (BombDevice_34, "BombDevice"),
        (BombDevice_114, "BombDevice"),
        (BombDevice_115, "BombDevice"),
        (BombDevice_246, "BombDevice"),
        (ShieldDevice_17, "ShieldDevice"),
        (ShieldDevice_36, "ShieldDevice"),
        (ShieldDevice_46, "ShieldDevice"),
        (ShieldDevice_47, "ShieldDevice"),
        (ShieldDevice_80, "ShieldDevice"),
        (ShieldDevice_81, "ShieldDevice"),
        (SelectDevice, "SelectDevice"),
        (SmokeDevice_10, "SmokeDevice"),
        (SmokeDevice_41, "SmokeDevice"),
        (SmokeDevice_264, "SmokeDevice"),
        (FireDevice, "FireDevice"),
        (LightDevice, "LightDevice"),
        (TrafficLightDevice, "TrafficLightDevice"),
        (VolumetricLightDevice, "VolumetricLightDevice"),
        (ParticleDevice, "ParticleDevice"),
        (EnergyShieldDevice, "EnergyShieldDevice"),
        (ForceFieldDevice, "ForceFieldDevice"),
        (TrailDevice, "TrailDevice"),
        (BeamDevice, "BeamDevice"),
        (BlackholeDevice, "BlackholeDevice"),
        (ElectricArcDevice, "ElectricArcDevice"),
        (TornadoDevice, "TornadoDevice"),
        (WarpDevice, "WarpDevice"),
        (ElectricBallDevice, "ElectricBallDevice"),
        (GalaxyDevice, "GalaxyDevice"),
        (NebulaDevice, "NebulaDevice"),
        (ScanDevice, "ScanDevice"),
        (CableDevice, "CableDevice"),
        (IvyDevice, "IvyDevice"),
        (FakeInteriorDevice, "FakeInteriorDevice"),
        (PolygonDevice, "PolygonDevice"),
        (PlanetDevice, "PlanetDevice"),
        (SunDevice, "SunDevice"),
        (GasPlanetDevice, "GasPlanetDevice"),
        (FallingLeavesDevice, "FallingLeavesDevice"),
        (SlashDevice, "SlashDevice"),
        (MiscVFXsDevice, "MiscVFXsDevice"),
        (IndicatorDevice, "IndicatorDevice"),
        (MiscFiresDevice, "MiscFiresDevice"),
        (MuzzleDevice, "MuzzleDevice"),
        (FlareDevice, "FlareDevice"),
        (CameraDevice, "CameraDevice"),
        (AvatarDevice, "AvatarDevice"),
        (PrefabDevice, "PrefabDevice"),
        (CircleDevice, "CircleDevice"),
        (RectDevice, "RectDevice"),
        (Capsule2DDevice, "Capsule2DDevice"),
        (RecorderDevice, "RecorderDevice"),
        (VelocityFuncDevice, "VelocityFuncDevice"),
        (HeightFuncDevice, "HeightFuncDevice"),
        (SpineDevice, "SpineDevice"),
        (PosDevice, "PosDevice"),
        (AreaBuilding, "AreaBuilding"),
        (ArrowBuilding, "ArrowBuilding"),
        (StartBuilding, "StartBuilding"),
        (NoColliderBuilding_1030, "NoColliderBuilding"),
        (NoColliderBuilding_1032, "NoColliderBuilding"),
        (NoColliderBuilding_1058, "NoColliderBuilding"),
        (NoColliderBuilding_1061, "NoColliderBuilding"),
        (AntiGravityDevice_11, "AntiGravityDevice"),
        (AntiGravityDevice_214, "AntiGravityDevice"),
        (MotorWheel_8, "MotorWheel"),
        (MotorWheel_15, "MotorWheel"),
        (MotorWheel_211, "MotorWheel"),
        (MotorWheel_215, "MotorWheel"),
        (MotorWheel_260, "MotorWheel"),
        (WheelColliderDevice, "WheelColliderDevice"),
        (MoveDevice_25, "MoveDevice"),
        (MoveDevice_251, "MoveDevice"),
        (MoveDevice_304, "MoveDevice"),
        (MoveDevice_339, "MoveDevice"),
        (MoveDevice2, "MoveDevice2"),
        (PropellerDevice_57, "PropellerDevice"),
        (PropellerDevice_58, "PropellerDevice"),
        (PropellerDevice_93, "PropellerDevice"),
        (PropellerDevice_212, "PropellerDevice"),
        (PropellerDevice_213, "PropellerDevice"),
        (PropellerDevice_239, "PropellerDevice"),
        (PropellerDevice_285, "PropellerDevice"),
        (PropellerDevice_331, "PropellerDevice"),
        (FuncDevice_77, "FuncDevice"),
        (FuncDevice_291, "FuncDevice"),
        (PistonDeviceLine, "PistonDeviceLine"),
        (PistonJoint_28, "PistonJoint"),
        (PistonJoint_218, "PistonJoint"),
        (SpringDevice_29, "SpringDevice"),
        (SpringDevice_219, "SpringDevice"),
        (MagnetDevice_70, "MagnetDevice"),
        (MagnetDevice_223, "MagnetDevice"),
        (Roter2Device, "Roter2Device"),
        (RoterDevice_12, "RoterDevice"),
        (RoterDevice_23, "RoterDevice"),
        (RoterDevice_27, "RoterDevice"),
        (RoterDevice_30, "RoterDevice"),
        (RoterDevice_31, "RoterDevice"),
        (RoterDevice_32, "RoterDevice"),
        (RoterDevice_216, "RoterDevice"),
        (RoterDevice_217, "RoterDevice"),
        (RoterDevice_220, "RoterDevice"),
        (RoterDevice_221, "RoterDevice"),
        (RoterDevice_222, "RoterDevice"),
        (RoterDevice_274, "RoterDevice")
    };

    public static IReadOnlyList<(int TypeId, string ActionOwnerTypeName)> GetActionOwnerTypeMappings()
    {
        return ActionOwnerTypeMappings;
    }


    private static readonly int[] AllTypeIds =
    {
        0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15,
        16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31,
        32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47,
        48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59, 60, 61, 62, 63,
        64, 65, 66, 67, 68, 69, 70, 71, 72, 73, 74, 75, 76, 77, 78, 79,
        80, 81, 82, 83, 84, 85, 86, 87, 88, 89, 90, 91, 92, 93, 94, 95,
        96, 97, 98, 99, 100, 101, 102, 103, 104, 105, 106, 107, 108, 109, 110, 111,
        112, 113, 114, 115, 116, 117, 118, 119, 120, 121, 122, 123, 124, 125, 126, 127,
        128, 129, 130, 131, 132, 133, 134, 135, 136, 137, 138, 139, 140, 141, 142, 143, 144,
        145, 146, 147, 148, 149, 200, 201, 202, 203, 204, 205, 206, 207, 208, 210,
        211, 212, 213, 214, 215, 216, 217, 218, 219, 220, 221, 222, 223, 224, 225, 226,
        227, 228, 229, 230, 231, 232, 233, 234, 235, 236, 237, 238, 239, 240, 241, 242,
        243, 244, 245, 246, 247, 248, 249, 250, 251, 252, 253, 254, 255, 256, 257, 258,
        259, 260, 261, 262, 263, 264, 265, 266, 267, 268, 269, 270, 271, 272, 273, 274,
        275, 276, 277, 278, 279, 280, 281, 282, 283, 284, 285, 286, 287, 288, 289, 290,
        291, 292, 293, 294, 295, 296, 297, 298, 299, 300, 301, 302, 303, 304, 305, 306,
        307, 308, 309, 310, 311, 312, 313, 314, 315, 316, 317, 318, 319, 320, 321, 322,
        323, 324, 325, 326, 327, 328, 329, 330, 331, 332, 333, 334, 335, 336, 337, 338,
        339, 340, 341, 342, 343, 344, 345, 346, 347, 348, 349, 350, 351, 352, 353, 354,
        355, 356, 357, 358, 359, 360, 361, 362, 363, 364, 365, 366, 400, 401, 402, 403,
        404, 405, 406, 407, 408, 409, 410, 411, 412, 413, 414, 415, 416, 417, 418, 419,
        420, 421, 422, 423, 424, 425, 426, 427, 428, 429, 430, 431, 432, 433, 434, 435,
        436, 437, 438, 439, 440, 441, 1017, 1018, 1019, 1020, 1021, 1022, 1023, 1024, 1025, 1026,
        1027, 1028, 1029, 1030, 1032, 1033, 1034, 1035, 1036, 1037, 1038, 1039, 1040, 1041, 1042, 1043,
        1044, 1045, 1046, 1047, 1048, 1049, 1050, 1051, 1052, 1053, 1054, 1055, 1056, 1057, 1058, 1059,
        1060, 1061, 1062, 1063, 1064, 1065, 1066, 1100, 1101, 1102, 1103, 1108, 1109, 1110, 1111, 1200,
        1201, 1202, 1203, 1204, 1205, 1206, 1207, 1208, 1209, 1210, 1211, 1212, 1213, 1214, 1215, 1216,
        1217, 1221, 1222, 1223, 1224, 1225, 1226, 1227, 1228, 1229, 1230, 1231, 1232, 1233, 1234, 1235,
        1236, 1237, 1238, 1239, 1240, 1241, 1242, 1243, 1244, 1245, 1246, 1247, 1248, 1249, 1250, 1251,
        1252, 1253, 1254, 1255, 1256, 1257, 1258, 1259, 1260, 1261, 1262,
    };

    internal static bool IsKnownTypeId(int typeId)
    {
        return Array.BinarySearch(AllTypeIds, typeId) >= 0;
    }

    public static IReadOnlyList<int> GetAllTypeIds()
    {
        return AllTypeIds;
    }

    public static string? FindActionOwnerTypeName(int typeId)
    {
        foreach ((int id, string name) in ActionOwnerTypeMappings)
        {
            if (id == typeId)
                return name;
        }

        return null;
    }

    /// <summary>
    /// 获取 ActionOwnerTypeName。
    /// </summary>
    /// <remarks>
    /// 已同步但未单独登记动作面的 type，明确归属到 Device。
    /// 未知 type 直接抛错，暴露 SDK 未同步问题。
    /// </remarks>
    public static string GetActionOwnerTypeName(int typeId)
    {
        string? mapped = FindActionOwnerTypeName(typeId);
        if (mapped != null)
            return mapped;

        if (IsKnownTypeId(typeId))
        {
            return "Device";
        }

        throw new InvalidOperationException($"零件类型未同步到 SDK,请联系Toolkit开发者进行登记同步: {typeId}");
    }
}
