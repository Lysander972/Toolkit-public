namespace Toolkit.Core.Sdk;

/// <summary>
/// 零件能力。SDK 用能力位表达 Act 方法的可用条件，
/// Lint 与 TS 视图共用同一份能力规则。
/// </summary>
public enum DeviceCapability
{
    /// <summary>
    /// 具备刚体碰撞载体能力，可使用 setKinematic/enableCollide/setDensity 三个方法。
    /// </summary>
    Collidable,
}
