using Toolkit.Core.Bescript;

namespace Toolkit.Core.TsView;

/// <summary>
/// TsView 脚本返回信息，包含返回类型和返回变量名。
/// </summary>
internal abstract class TsViewScriptReturnInfo
{
    public sealed class NoReturn : TsViewScriptReturnInfo
    {
        public override string ReturnType => "void";
        public override bool HasReturn => false;
        public override string ReturnStatement => "return";
    }

    public sealed class WithReturn : TsViewScriptReturnInfo
    {
        public WithReturn(string returnType, string returnVarName)
        {
            ReturnType = returnType;
            ReturnVarName = returnVarName;
        }

        public override string ReturnType { get; }
        public string ReturnVarName { get; }
        public override bool HasReturn => true;
        public override string ReturnStatement => "return " + ReturnVarName;
    }

    public abstract string ReturnType { get; }
    public abstract bool HasReturn { get; }
    public abstract string ReturnStatement { get; }
}

/// <summary>
/// TsView 返回信息构建器，从 BEScript 返回参数构建 TypeScript 返回信息。
/// </summary>
internal static class TsViewReturnInfoBuilder
{
    internal static TsViewScriptReturnInfo Build(BescriptParameter? returnParam)
    {
        if (returnParam == null)
        {
            return new TsViewScriptReturnInfo.NoReturn();
        }

        string tsName = TsViewIdentifierMap.ToTsIdentifier(returnParam.Name);
        string typeName = TsViewTypeUtils.ConvertToTsTypeName(returnParam.TypeName);
        if (BescriptVarTypeHelper.TryParse(typeName) is not BescriptVarType type)
        {
            throw new InvalidOperationException($"返回类型无法识别: {returnParam.Name}:{returnParam.TypeName}");
        }

        if (type is BescriptVarType.Void)
        {
            return new TsViewScriptReturnInfo.NoReturn();
        }

        string tsType = TsViewTypeUtils.ToTsBeTypeName(type);
        return new TsViewScriptReturnInfo.WithReturn(tsType, tsName);
    }
}
