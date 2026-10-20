namespace Toolkit.Core.Infra;

/// <summary>
/// 对外接口标记：框架 dispatch/协议入口/宿主回调的入口（仓内 C# 代码无调用方，调用方在仓外）。
/// TK 分析器识别本标记：TK006（一次性 Helper）豁免，TK023（死方法）以标记为存活依据。
/// 运行时零语义，仅供 lint 与读者识别入口。
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = false)]
public sealed class ExternalInterfaceAttribute : Attribute;
