using System.Text.Json;
using Microsoft.ClearScript.V8;
using Xunit;
using Toolkit.Core.Infra;
using Toolkit.Core.TsView;

namespace Toolkit.Core.Tests.Sdk;

/// <summary>
/// sdk.ts 中按能力门控的方法分层结构测试。
/// SDK 将具有能力要求的方法放入独立的 I{Capability} 接口，
/// 不出现在 DeviceActionsDevice 基类（零件基类无冗余声明），具体零件类按 capability extends。
/// </summary>
public sealed class BescriptDtsGeneratorCapabilityTests
{
    private static readonly string SdkText = BescriptTsViewDeclarationLibrary.GetSdkText();

    /// <summary>
    /// 场景 3：DeviceActionsDevice 基类不得包含任何受能力门控的方法声明。
    /// 成员边界由官方 TypeScript AST 判定，注释和其他类型中的同名文本不得误判。
    /// </summary>
    [Fact]
    public void Sdk_DeviceActionsDevice_BaseClass_Does_Not_Declare_Collidable_Methods()
    {
        IReadOnlySet<string> members = GetTypeMemberNames("DeviceActionsDevice");

        Assert.DoesNotContain("enableCollide", members);
        Assert.DoesNotContain("setKinematic", members);
        Assert.DoesNotContain("setDensity", members);
    }

    /// <summary>
    /// 场景 4：ICollidable 接口声明，包含全部 Collidable 方法签名。
    /// </summary>
    [Fact]
    public void Sdk_ICollidable_Interface_Declares_All_Collidable_Methods()
    {
        IReadOnlySet<string> members = GetTypeMemberNames("ICollidable");

        Assert.Contains("enableCollide", members);
        Assert.Contains("setKinematic", members);
        Assert.Contains("setDensity", members);
    }

    private static IReadOnlySet<string> GetTypeMemberNames(string typeName)
    {
        using V8ScriptEngine engine = new(V8ScriptEngineFlags.DisableGlobalMembers);
        engine.Execute("typescript.js", EmbeddedResourceHelper.LoadText("Toolkit.Core.tsview.v8.typescript.js.gz"));
        engine.Script.___sdkText = SdkText;
        engine.Script.___typeName = typeName;
        string json = (string)engine.Evaluate("""
            (function (sdkText, typeName) {
                const sourceFile = ts.createSourceFile("sdk.ts", sdkText, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS);
                const declaration = sourceFile.statements.find(function (statement) {
                    return (ts.isClassDeclaration(statement) || ts.isInterfaceDeclaration(statement)) && statement.name?.text === typeName;
                });
                if (!declaration) throw new Error("未找到类型: " + typeName);
                return JSON.stringify(declaration.members.map(function (member) {
                    return member.name?.getText(sourceFile) ?? "";
                }));
            })(___sdkText, ___typeName)
            """);
        string[] members = JsonSerializer.Deserialize<string[]>(json)
            ?? throw new InvalidOperationException("TypeScript 成员解析返回空结果。");
        return members.ToHashSet(StringComparer.Ordinal);
    }
}
