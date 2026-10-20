namespace Toolkit.Core.Sdk;

// 原名可能有`A@B`的格式,注意`A@B`整体是本名!而`B`或者按语义产生的`C`才是手动维护别名
internal readonly record struct EnumMemberInfo(string TsName, string RawValue, string? Description = null);

internal static class BuiltinEnumTypes
{
    private static readonly Lazy<Dictionary<string, MethodEnumType>> BuiltinByTsTypeName = new(BuildBuiltinEnumTypes);

    public static readonly MethodEnumType ForceMode = new(
        "ForceMode",
        "力学模式",
        new EnumMember[]
        {
            new("力@Force", Alias: "Force"),
            new("冲量@Impulse", Alias: "Impulse"),
            new("速度变化@VelocityChange", Alias: "VelocityChange"),
            new("加速度@Acceleration", Alias: "Acceleration")
        });

    public static readonly MethodEnumType MechPositionAlignment = new(
        "MechPositionAlignment",
        "机械位置对齐方式",
        new EnumMember[]
        {
            new("核心@Core", Alias: "Core"),
            new("正下方@Bottom", Alias: "Bottom")
        });

    public static readonly MethodEnumType CreateMechPositionAlignment = new(
        "CreateMechPositionAlignment",
        "机械生成位置对齐方式",
        new EnumMember[]
        {
            new("核心@core", Alias: "Core"),
            new("正下方@down", Alias: "Bottom")
        });

    public static readonly MethodEnumType UiAlignment = new(
        "UiAlignment",
        "UI 对齐方式",
        new EnumMember[]
        {
            new("左上@topLeft", Alias: "topLeft"),
            new("上@top", Alias: "top"),
            new("右上@topRight", Alias: "topRight"),
            new("左@left", Alias: "left"),
            new("居中@center", Alias: "center"),
            new("右@right", Alias: "right"),
            new("左下@bottomLeft", Alias: "bottomLeft"),
            new("下@bottom", Alias: "bottom"),
            new("右下@bottomRight", Alias: "bottomRight")
        });

    public static readonly MethodEnumType UiPixelAlignment = new(
        "UiPixelAlignment",
        "UI 像素坐标对齐方式",
        new EnumMember[]
        {
            new("左上@LeftTop", Alias: "topLeft"),
            new("上@Top", Alias: "top"),
            new("右上@RightTop", Alias: "topRight"),
            new("左@Left", Alias: "left"),
            new("居中@Middle", Alias: "center"),
            new("右@Right", Alias: "right"),
            new("左下@RightBottom", Alias: "bottomLeft"),
            new("下@Bottom", Alias: "bottom"),
            new("右下@RightBottom", Alias: "bottomRight")
        });

    public static readonly MethodEnumType LabelEffectStyle = new(
        "LabelEffectStyle",
        "文本特效风格",
        new EnumMember[]
        {
            new("普通@general", Alias: "general"),
            new("粗体@bold", Alias: "bold"),
            new("斜体@italic", Alias: "italic"),
            new("下划线@underline", Alias: "underline"),
            new("删除线@strickout", Alias: "strickout")
        });

    public static readonly MethodEnumType UiFillMethod = new(
        "UiFillMethod",
        "UI 填充方式",
        new EnumMember[]
        {
            new("左右@horizontal", Alias: "horizontal"),
            new("上下@vertical", Alias: "vertical"),
            new("360度@radial360", Alias: "radial360"),
            new("180度@radial180", Alias: "radial180"),
            new("90度@radial90", Alias: "radial90")
        });

    public static readonly MethodEnumType ParticleRenderMode = new(
        "ParticleRenderMode",
        "粒子渲染模式",
        new EnumMember[]
        {
            new("普通@general", Alias: "general"),
            new("拉伸@stretch", Alias: "stretch"),
            new("水平@Horizontal", Alias: "Horizontal"),
            new("垂直@Vertical", Alias: "Vertical"),
            new("模型@Mesh", Alias: "Mesh")
        });

    public static readonly MethodEnumType ParticleMaterial = new(
        "ParticleMaterial",
        "粒子材质类型，与原版粒子材质列表顺序一致",
        new EnumMember[]
        {
            new("glow_mat"),
            new("circle_mat"),
            new("circle2_mat"),
            new("circle3_mat"),
            new("cloud_mat"),
            new("square_mat")
        });

    public static readonly MethodEnumType RotateMode = new(
        "RotateMode",
        "旋转模式",
        new EnumMember[]
        {
            new("相对每秒@Relative", Alias: "Relative"),
            new("绝对@Absolute", Alias: "Absolute"),
            new("相对(无视帧率)@Fixed", Alias: "Fixed")
        });

    public static readonly MethodEnumType LaserEnemyType = new(
        "LaserEnemyType",
        "激光目标类型",
        new EnumMember[]
        {
            new("核心@Core", Alias: "Core"),
            new("任意零件@Any", Alias: "Any"),
            new("激光探测器@Laser", Alias: "Laser")
        });

    public static readonly MethodEnumType TrafficLightState = new(
        "TrafficLightState",
        "红绿灯状态",
        new EnumMember[]
        {
            new("关闭"),
            new("红灯"),
            new("黄灯"),
            new("绿灯")
        });

    public static readonly MethodEnumType TerrainMaterialIndex = new(
        "TerrainMaterialIndex",
        "地形材质序号",
        new EnumMember[]
        {
            new("1", Alias: "V1"),
            new("2", Alias: "V2"),
            new("3", Alias: "V3")
        });

    public static readonly MethodEnumType AstarGridType = new(
        "AstarGridType",
        "寻路网格类型",
        new EnumMember[]
        {
            new("正方形@square", Alias: "square"),
            new("六边形@hex", Alias: "hex")
        });

    public static readonly MethodEnumType RankValueType = new(
        "RankValueType",
        "排行榜计分类型",
        new EnumMember[]
        {
            new("分数@score", Alias: "score"),
            new("时间@time", Alias: "time")
        });

    public static readonly MethodEnumType UiTextType = new(
        "UiTextType",
        "文本组件类型",
        new EnumMember[]
        {
            new("高级特效@advanced effects", Alias: "advancedEffects"),
            new("普通@general", Alias: "general")
        });

    public static readonly MethodEnumType EventCommentGiftLike = new(
        "EventCommentGiftLike",
        "弹幕事件类型",
        new EnumMember[]
        {
            new("评论@Comment", Alias: "Comment"),
            new("礼物@Gift", Alias: "Gift"),
            new("点赞@Like", Alias: "Like")
        });

    public static readonly MethodEnumType EventBulletDeviceOther = new(
        "EventBulletDeviceOther",
        "碰撞事件类型",
        new EnumMember[]
        {
            new("子弹@Bullet", Alias: "Bullet"),
            new("零件@Device", Alias: "Device"),
            new("环境@Other", Alias: "Other")
        }); public static readonly MethodEnumType KeyCode = new(
        new("KeyCode"),
        new("键值枚举，与原版键盘/鼠标键值表顺序一致"),
        new EnumMember[]
        {
            new("Backspace"),
            new("Tab"),
            new("Clear"),
            new("Return"),
            new("Pause"),
            new("Escape"),
            new("Space"),
            new("Exclaim"),
            new("DoubleQuote"),
            new("Hash"),
            new("Dollar"),
            new("Percent"),
            new("Ampersand"),
            new("Quote"),
            new("LeftParen"),
            new("RightParen"),
            new("Asterisk"),
            new("Plus"),
            new("Comma"),
            new("Minus"),
            new("Period"),
            new("Slash"),
            new("Alpha0"),
            new("Alpha1"),
            new("Alpha2"),
            new("Alpha3"),
            new("Alpha4"),
            new("Alpha5"),
            new("Alpha6"),
            new("Alpha7"),
            new("Alpha8"),
            new("Alpha9"),
            new("Colon"),
            new("Semicolon"),
            new("Less"),
            new("Equals"),
            new("Greater"),
            new("Question"),
            new("At"),
            new("LeftBracket"),
            new("Backslash"),
            new("RightBracket"),
            new("Caret"),
            new("Underscore"),
            new("BackQuote"),
            new("A"),
            new("B"),
            new("C"),
            new("D"),
            new("E"),
            new("F"),
            new("G"),
            new("H"),
            new("I"),
            new("J"),
            new("K"),
            new("L"),
            new("M"),
            new("N"),
            new("O"),
            new("P"),
            new("Q"),
            new("R"),
            new("S"),
            new("T"),
            new("U"),
            new("V"),
            new("W"),
            new("X"),
            new("Y"),
            new("Z"),
            new("LeftCurlyBracket"),
            new("Pipe"),
            new("RightCurlyBracket"),
            new("Tilde"),
            new("Delete"),
            new("Keypad0"),
            new("Keypad1"),
            new("Keypad2"),
            new("Keypad3"),
            new("Keypad4"),
            new("Keypad5"),
            new("Keypad6"),
            new("Keypad7"),
            new("Keypad8"),
            new("Keypad9"),
            new("KeypadPeriod"),
            new("KeypadDivide"),
            new("KeypadMultiply"),
            new("KeypadMinus"),
            new("KeypadPlus"),
            new("KeypadEnter"),
            new("KeypadEquals"),
            new("UpArrow"),
            new("DownArrow"),
            new("RightArrow"),
            new("LeftArrow"),
            new("Insert"),
            new("Home"),
            new("End"),
            new("PageUp"),
            new("PageDown"),
            new("F1"),
            new("F2"),
            new("F3"),
            new("F4"),
            new("F5"),
            new("F6"),
            new("F7"),
            new("F8"),
            new("F9"),
            new("F10"),
            new("F11"),
            new("F12"),
            new("F13"),
            new("F14"),
            new("F15"),
            new("Numlock"),
            new("CapsLock"),
            new("ScrollLock"),
            new("RightShift"),
            new("LeftShift"),
            new("RightControl"),
            new("LeftControl"),
            new("RightAlt"),
            new("LeftAlt"),
            new("RightApple"),
            new("LeftCommand"),
            new("LeftWindows"),
            new("RightWindows"),
            new("AltGr"),
            new("Help"),
            new("Print"),
            new("SysReq"),
            new("Break"),
            new("Menu"),
            new("Mouse0"),
            new("Mouse1"),
            new("Mouse2"),
            new("Mouse3"),
            new("Mouse4"),
            new("Mouse5"),
            new("Mouse6"),
            new("JoystickButton0"),
            new("JoystickButton1"),
            new("JoystickButton2"),
            new("JoystickButton3"),
            new("JoystickButton4"),
            new("JoystickButton5"),
            new("JoystickButton6"),
            new("JoystickButton7"),
            new("JoystickButton8"),
            new("JoystickButton9"),
            new("JoystickButton10"),
            new("JoystickButton11"),
            new("JoystickButton12"),
            new("JoystickButton13"),
            new("JoystickButton14"),
            new("JoystickButton15"),
            new("JoystickButton16"),
            new("JoystickButton17"),
            new("JoystickButton18"),
            new("JoystickButton19"),
            new("Joystick1Button0"),
            new("Joystick1Button1"),
            new("Joystick1Button2"),
            new("Joystick1Button3"),
            new("Joystick1Button4"),
            new("Joystick1Button5"),
            new("Joystick1Button6"),
            new("Joystick1Button7"),
            new("Joystick1Button8"),
            new("Joystick1Button9"),
            new("Joystick1Button10"),
            new("Joystick1Button11"),
            new("Joystick1Button12"),
            new("Joystick1Button13"),
            new("Joystick1Button14"),
            new("Joystick1Button15"),
            new("Joystick1Button16"),
            new("Joystick1Button17"),
            new("Joystick1Button18"),
            new("Joystick1Button19"),
            new("Joystick2Button0"),
            new("Joystick2Button1"),
            new("Joystick2Button2"),
            new("Joystick2Button3"),
            new("Joystick2Button4"),
            new("Joystick2Button5"),
            new("Joystick2Button6"),
            new("Joystick2Button7"),
            new("Joystick2Button8"),
            new("Joystick2Button9"),
            new("Joystick2Button10"),
            new("Joystick2Button11"),
            new("Joystick2Button12"),
            new("Joystick2Button13"),
            new("Joystick2Button14"),
            new("Joystick2Button15"),
            new("Joystick2Button16"),
            new("Joystick2Button17"),
            new("Joystick2Button18"),
            new("Joystick2Button19"),
            new("Joystick3Button0"),
            new("Joystick3Button1"),
            new("Joystick3Button2"),
            new("Joystick3Button3"),
            new("Joystick3Button4"),
            new("Joystick3Button5"),
            new("Joystick3Button6"),
            new("Joystick3Button7"),
            new("Joystick3Button8"),
            new("Joystick3Button9"),
            new("Joystick3Button10"),
            new("Joystick3Button11"),
            new("Joystick3Button12"),
            new("Joystick3Button13"),
            new("Joystick3Button14"),
            new("Joystick3Button15"),
            new("Joystick3Button16"),
            new("Joystick3Button17"),
            new("Joystick3Button18"),
            new("Joystick3Button19"),
            new("Joystick4Button0"),
            new("Joystick4Button1"),
            new("Joystick4Button2"),
            new("Joystick4Button3"),
            new("Joystick4Button4"),
            new("Joystick4Button5"),
            new("Joystick4Button6"),
            new("Joystick4Button7"),
            new("Joystick4Button8"),
            new("Joystick4Button9"),
            new("Joystick4Button10"),
            new("Joystick4Button11"),
            new("Joystick4Button12"),
            new("Joystick4Button13"),
            new("Joystick4Button14"),
            new("Joystick4Button15"),
            new("Joystick4Button16"),
            new("Joystick4Button17"),
            new("Joystick4Button18"),
            new("Joystick4Button19")
        });

    public static readonly MethodEnumType MouseLockMode = new(
        "MouseLockMode",
        "鼠标锁定模式",
        new EnumMember[]
        {
            new("不锁定@None", Alias: "None"),
            new("锁定@lock", Alias: "lock"),
            new("窗口内@Confined", Alias: "Confined")
        });

    public static readonly MethodEnumType JoystickInputType = new(
        "JoystickInputType",
        "手柄输入类型",
        new EnumMember[]
        {
            new("左摇杆@LeftAnalogue", Alias: "LeftAnalogue"),
            new("右摇杆@RightAnalogue", Alias: "RightAnalogue"),
            new("扳机@Trigger", Alias: "Trigger"),
            new("方向键@DPad", Alias: "DPad")
        });

    public static readonly MethodEnumType SkyReflectType = new(
        "SkyReflectType",
        "天空反光类型",
        new EnumMember[]
        {
            new("默认@default", Alias: "_default"),
            new("天空@sky", Alias: "sky")
        });

    public static readonly MethodEnumType SoundEffect = new(
        "SoundEffect",
        "音效类型，与原版音效列表顺序一致",
        new EnumMember[]
        {
            new("button_wav"),
            new("Rupture_Metal_A01_wav"),
            new("Rupture_Metal_A02_wav"),
            new("Rupture_Metal_A03_wav"),
            new("Rupture_Metal_A04_wav"),
            new("Missing_af143536"),
            new("Missing_a8bd6c15"),
            new("Missing_dd339bdb"),
            new("Missing_5175afc9"),
            new("Rupture_Brick_Impact_01_wav"),
            new("Rupture_Brick_Impact_02_wav"),
            new("Rupture_Brick_Impact_03_wav"),
            new("Rupture_Brick_Impact_04_wav"),
            new("Rupture_Brick_Impact_05_wav"),
            new("Rupture_Car_Light_Impact_01_wav"),
            new("Rupture_Car_Light_Impact_02_wav"),
            new("Shell_Short_01_SFX_wav"),
            new("Shell_Short_02_SFX_wav"),
            new("Rupture_Glass_Bottle_Impact_01_wav"),
            new("Rupture_Glass_Bottle_Impact_02_wav"),
            new("Rupture_Glass_Bottle_Impact_03_wav"),
            new("Rupture_Glass_Bottle_Impact_04_wav"),
            new("Rupture_Glass_Bottle_Impact_05_wav"),
            new("Rupture_Ice_Impact_01_wav"),
            new("Rupture_Ice_Impact_02_wav"),
            new("Rupture_Ice_Impact_03_wav"),
            new("Rupture_Ice_Impact_04_wav"),
            new("Rupture_Ice_Impact_05_wav"),
            new("Rupture_Bulb_Impact_01_wav"),
            new("Rupture_Bulb_Impact_02_wav"),
            new("Rupture_Bulb_Impact_03_wav"),
            new("Rupture_Bulb_Impact_04_wav"),
            new("Rupture_Bulb_Impact_05_wav"),
            new("Rupture_Marble_Impacts_01_wav"),
            new("dive_splash_mp3"),
            new("fail_mp3"),
            new("good_wav"),
            new("Rupture_BulletProof_Impact_04_wav"),
            new("Rupture_BulletProof_Impact_06_wav"),
            new("Rupture_BulletProof_Impact_07_wav"),
            new("Rupture_Water_Impact_01_wav"),
            new("Rupture_Water_Impact_02_wav"),
            new("Rupture_Marble_Impacts_02_wav")
        });

    public static readonly MethodEnumType EchoMeshPrimitiveType = new(
        "EchoMeshPrimitiveType",
        "网格 UI 形状",
        new EnumMember[]
        {
            new("圆柱@cylinder", Alias: "cylinder"),
            new("半球@halfSphere", Alias: "halfSphere"),
            new("球@sphere", Alias: "sphere"),
            new("箭头@arrow", Alias: "arrow"),
            new("圆环@ring", Alias: "ring"),
            new("立方体@cube", Alias: "cube")
        });

    public static readonly MethodEnumType ScrollViewMovement = new(
        "ScrollViewMovement",
        "滑动模式",
        new EnumMember[]
        {
            new("弹性@Elastic", Alias: "Elastic"),
            new("限制@Clamp", Alias: "Clamp")
        });

    public static readonly MethodEnumType TripoModel = new(
        "TripoModel",
        "Tripo 3D 模型版本",
        new EnumMember[]
        {
            new("Turbo(速度快)", Alias: "Turbo"),
            new("v3.0(效果最好)", Alias: "v3_0"),
            new("v2.5", Alias: "v2_5"),
            new("v2.0", Alias: "v2_0")
        });

    public static readonly MethodEnumType TripoImageModel = new(
        "TripoImageModel",
        "Tripo 图片模型版本",
        new EnumMember[]
        {
            new("flux.1_kontext_pro", Alias: "flux_1_kontext_pro"),
            new("flux.1_dev", Alias: "flux_1_dev"),
            new("GPT-4o", Alias: "GPT_4o"),
            new("Nano Banana", Alias: "Nano_Banana")
        });

    internal static IReadOnlyDictionary<string, MethodEnumType> ByTsTypeName => BuiltinByTsTypeName.Value;

    /// <summary>
    /// 获取所有定义的枚举类型。
    /// 【设计原则】SDK 侧直接全量收集，无需筛选。
    /// </summary>
    internal static IEnumerable<MethodEnumType> GetAll()
    {
        return typeof(BuiltinEnumTypes)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(static x => x.FieldType == typeof(MethodEnumType))
            .Select(static x => x.GetValue(null))
            .Cast<MethodEnumType>();
    }

    private static Dictionary<string, MethodEnumType> BuildBuiltinEnumTypes()
    {
        return GetAll().ToDictionary(static x => x.Name, static x => x, StringComparer.Ordinal);
    }

    internal static IReadOnlyList<EnumMemberInfo> GetMembers(MethodEnumType enumType)
    {
        IReadOnlyList<EnumMember> options = enumType.Options ?? throw new InvalidOperationException($"枚举 {enumType.Name} 没有成员列表");
        EnumMemberInfo[] members = new EnumMemberInfo[options.Count];
        for (int i = 0; i < options.Count; i++)
            members[i] = new EnumMemberInfo(options[i].EffectiveName, i.ToString(), options[i].Description);
        return members;
    }

    internal static EnumMemberInfo? GetMemberByTsName(MethodEnumType enumType, string tsName)
    {
        foreach (EnumMemberInfo current in GetMembers(enumType))
        {
            if (string.Equals(current.TsName, tsName, StringComparison.Ordinal))
                return current;
        }
        return null;
    }

    internal static EnumMemberInfo? GetMemberByRawValue(MethodEnumType enumType, string rawValue)
    {
        foreach (EnumMemberInfo current in GetMembers(enumType))
        {
            if (string.Equals(current.RawValue, rawValue, StringComparison.Ordinal))
                return current;
        }
        return null;
    }
}
