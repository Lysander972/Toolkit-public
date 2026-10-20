using System.Text.Json;
using Microsoft.ClearScript.V8;
using System.Reflection;
using System.Text;
using Xunit;
using Xunit.Abstractions;
using Toolkit.Core.Infra;
using Toolkit.Core.Sdk;
using Toolkit.Core.TsView;

namespace Toolkit.Core.Tests.Sdk;

public class BescriptDtsGeneratorTests
{
    private readonly ITestOutputHelper _output;

    public BescriptDtsGeneratorTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Dts_ShouldFollowNewStructure()
    {
        string dts = BescriptTsViewDeclarationLibrary.GetSdkText();
        Assert.NotEmpty(dts);

        int lineCount = dts.Split('\n').Length;
        _output.WriteLine($"sdk.ts lineCount = {lineCount}");

        Assert.Contains("declare namespace G {", dts);
        Assert.Contains("type BeConstMarker = { readonly __beConst: true };", dts);
        Assert.Contains("type Const<T> = Omit<T, '__beConst'> & BeConstMarker;", dts);
        Assert.Contains("type BeCanvasCreateConst = Omit<Const<BeCanvas>, '__beCanvasNonCreate'> & { readonly __beCanvasNonCreate?: never };", dts);
        Assert.Contains("type BeIconCreateConst = Omit<Const<BeIcon>, '__beIconNonCreate'> & { readonly __beIconNonCreate?: never };", dts);
        Assert.Contains("readonly __beTypeBrand_UI: never;", dts);
        Assert.Contains("readonly __beTypeBrand_Bool: never;", dts);
        Assert.Contains("readonly __beTypeBrand_Float: never;", dts);
        Assert.Contains("readonly __beTypeBrand_UIRect: never;", dts);
        Assert.Contains("readonly __beConst?: never;", dts);
        // 验证装饰器与事件签名约束声明存在
        Assert.Contains("function BeScript(meta: BeScriptMeta)", dts);
        Assert.Contains("interface BeScriptMeta", dts);
        Assert.Contains("interface RetVarTypeMap", dts);
        Assert.Contains("retVarType?: keyof RetVarTypeMap;", dts);
        Assert.Contains("type BeScriptExternalArgsTuple<TArgs extends any[]> = [...{ [I in keyof TArgs]: TArgs[I] | Const<TArgs[I]> }];", dts);
        Assert.Contains("type BeScriptExternalArgs<TFn extends (...args: any[]) => any> = BeScriptExternalArgsTuple<Parameters<TFn>>;", dts);
        // BeScript 有返回值重载：泛型约束 retVarType → 箭头函数返回类型
        Assert.Contains("declare function BeScript<T extends keyof RetVarTypeMap>(meta: BeScriptMeta & { retVar: string; retVarType: T }): <TFn extends (this: any, ...args: any[]) => RetVarTypeMap[T]>(fn: TFn) => (...args: BeScriptExternalArgs<TFn>) => RetVarTypeMap[T];", dts);
        // BeScript 无返回值重载
        Assert.Contains("declare function BeScript(meta: BeScriptMeta): <TFn extends (this: any, ...args: any[]) => any>(fn: TFn) => (...args: BeScriptExternalArgs<TFn>) => ReturnType<TFn>;", dts);
        // BeScriptAsync 有返回值重载：泛型约束
        Assert.Contains("declare function BeScriptAsync<T extends keyof RetVarTypeMap>(meta: BeScriptMetaWithRet<T>): <TFn extends (this: any, ...args: any[]) => Promise<RetVarTypeMap[T]>>(fn: TFn) => (...args: BeScriptExternalArgs<TFn>) => Promise<RetVarTypeMap[T]>;", dts);
        // BeScriptAsync 无返回值重载
        Assert.Contains("declare function BeScriptAsync(meta: BeScriptMeta): <TFn extends (this: any, ...args: any[]) => Promise<void>>(fn: TFn) => (...args: BeScriptExternalArgs<TFn>) => FireAndForget;", dts);
        Assert.Contains("declare function delay(call: BeFloat | Promise<BeFloat>): Promise<void>", dts);
        Assert.Contains("function bool(value: BeBool | Const<BeBool>): BeBool;", dts);
        Assert.Contains("function enable(boolDebug: BeBool | Const<BeBool>): void;", dts);
        Assert.Contains("function canvas(value: Const<BeCanvas> | BeCanvasCreateConst): BeCanvas;", dts);
        Assert.Contains("function icon(value: Const<BeIcon> | BeIconCreateConst): BeIcon;", dts);
        Assert.Contains("function setRank(score: Const<BeNumber> | BeFloat | BeLong, dict: BeDict | Const<BeDict>, type: Const<BeEnum<RankValueType>>, RankIndex: BeLong | Const<BeLong>): void;", dts);
        Assert.DoesNotContain("type BeVarMarker", dts);
        Assert.DoesNotContain("type Var<T>", dts);
        Assert.Contains("static fromBeConst(): Const<BeUI>;", dts);
        Assert.Contains("static fromBeConst(value: \"0\" | \"1\"): Const<BeBool>;", dts);
        Assert.Contains("static fromBeConst(): Const<BeCanvas>;", dts);
        Assert.Contains("static fromBase64Const<T extends string>(value: T extends \"\" ? never : T): BeCanvasCreateConst;", dts);
        Assert.Contains("static fromNodeConst<T extends string>(name: string, value: T extends \"\" ? never : T): BeCanvasCreateConst;", dts);
        Assert.Contains("示例: BeCanvas.fromNodeConst(\"hud\", `<BeCanvas name=\"hud\" width=\"1080\" height=\"2160\"></BeCanvas>`)", dts);
        Assert.Contains("static fromGuidConst<T extends string>(value: T extends \"\" ? never : T): BeCanvasCreateConst;", dts);
        Assert.Contains("static fromBeConst(): Const<BeIcon>;", dts);
        Assert.Contains("static fromBeConst<T extends string>(value: T extends \"\" ? never : T): BeIconCreateConst;", dts);
        Assert.Contains("declare class BeEnum<T extends EnumType>", dts);
        Assert.Contains("/** 枚举类型，值类型 */\ndeclare class BeEnum<T extends EnumType>", TextLineEndings.ToLf(dts));
        Assert.Equal(1, dts.Split("declare class BeEnum<T extends EnumType>", StringSplitOptions.None).Length - 1);
        Assert.Contains("static fromBeConst<T extends EnumType>(value: T): Const<BeEnum<T>>;", dts);
        Assert.Contains("equal(other: Const<BeEnum<T>>): BeBool;", dts);
        Assert.Contains("setPlayerData<T extends BeBool | BeBytes | BeCanvas", dts);
        Assert.Contains("值: T | Const<T>): void;", dts);
        Assert.Contains("BeEnum<EventCommentGiftLike>", dts);
        Assert.Contains("declare enum ForceMode {", dts);
        Assert.Contains("Force = \"0\"", dts);
        Assert.Contains("declare enum KeyCode {", dts);
        Assert.Contains("declare enum EventCommentGiftLike {", dts);
        Assert.Contains("static fromBeConst(value: string): Const<BeVector3>;", dts);
        Assert.Contains("static fromBeConst(): Const<BeBytes>;", dts);
        Assert.Contains("declare class BeMesh {", dts);

        Assert.DoesNotContain("declare namespace BescriptTsView {", dts);
        Assert.DoesNotContain("declare const bes:", dts);
        Assert.DoesNotContain("\"if\"(", dts);
        Assert.DoesNotContain("\"while\"(", dts);

        Assert.Contains("declare abstract class BeUI {", dts);
        // 验证事件签名约束声明
        Assert.Contains("interface TsViewEventSignatureMap", dts);
        Assert.Contains("type TsViewEventName = keyof TsViewEventSignatureMap;", dts);
        Assert.Contains("type TsViewEventMethod<E extends TsViewEventName> = TsViewEventSignatureMap[E];", dts);
        Assert.DoesNotContain("declare const __TsViewAllowedEvents", dts);
        Assert.Contains("type Pick<T, K extends keyof T> = { [P in K]: T[P] };", dts);
        Assert.Contains("type ConstructorParameters<T extends new (...args: any[]) => any> = T extends new (...args: infer P) => any ? P : never;", dts);
        Assert.Contains("type InstanceType<T extends new (...args: any[]) => any> = T extends new (...args: any[]) => infer R ? R : any;", dts);
        Assert.Contains("type TsViewEventExternalArgs<E extends TsViewEventName> = TsViewEventMethod<E> extends (...args: infer P) => any ? BeScriptExternalArgsTuple<P> : never;", dts);
        Assert.Contains("type TsViewEventRegistrationMembers<E extends TsViewEventName, TMembers extends Record<string, (...args: any[]) => any>>", dts);
        Assert.Contains("protected static registerEvent_start<TMembers extends Record<string, (...args: any[]) => any>>", dts);
        Assert.Contains("target: new (...args: any[]) => any, members: TsViewEventRegistrationMembers<\"start\", TMembers>", dts);
        string dtsLf = TextLineEndings.ToLf(dts);
        int deviceActionsDeviceStart = dtsLf.IndexOf("declare class DeviceActionsDevice {", StringComparison.Ordinal);
        Assert.True(deviceActionsDeviceStart >= 0);
        int deviceActionsDeviceEnd = dtsLf.IndexOf("\n}", deviceActionsDeviceStart, StringComparison.Ordinal);
        Assert.True(deviceActionsDeviceEnd > deviceActionsDeviceStart);
        string deviceActionsDeviceBlock = dtsLf[deviceActionsDeviceStart..deviceActionsDeviceEnd];
        Assert.DoesNotContain("registerEvent_", deviceActionsDeviceBlock);
        Assert.DoesNotContain("declare class DeviceActionsFuncDevice", dtsLf);
        Assert.Contains("declare class DeviceType_200 extends DeviceActionsDevice {}", dtsLf);
        Assert.DoesNotContain("declare class DeviceActionsBaseCoreDevice", dtsLf);
        Assert.Contains("declare class DeviceActionsCoreDevice extends DeviceActions", dtsLf);
        Assert.Contains("declare class DeviceActionsClientMapLogicDevice extends DeviceActions", dtsLf);
        Assert.Contains("declare class DeviceType_77 extends DeviceActionsDevice {\n  protected static registerEvent_start", dtsLf);
        Assert.Contains("declare class DeviceType_77 extends DeviceActionsDevice {\n  protected static registerEvent_start<TMembers extends Record<string, (...args: any[]) => any>>(target: new (...args: any[]) => any, members: TsViewEventRegistrationMembers<\"start\", TMembers>): void;\n  protected static registerEvent_update", dtsLf);
        Assert.Contains("declare class DeviceType_1101 extends DeviceActionsClientMapLogicDevice {\n  protected static registerEvent_clientStart", dtsLf);
        string deviceType1101Block = dtsLf[dtsLf.IndexOf("declare class DeviceType_1101 extends DeviceActionsClientMapLogicDevice {", StringComparison.Ordinal)..dtsLf.IndexOf("declare class DeviceType_1102", StringComparison.Ordinal)];
        Assert.DoesNotContain("registerEvent_start", deviceType1101Block);
        Assert.Contains("declare class DeviceType_1110 extends DeviceActionsBirthBuilding {\n  protected static registerEvent_beHit", dtsLf);
        string deviceType1110Block = dtsLf[dtsLf.IndexOf("declare class DeviceType_1110 extends DeviceActionsBirthBuilding {", StringComparison.Ordinal)..dtsLf.IndexOf("declare class DeviceType_1111", StringComparison.Ordinal)];
        Assert.DoesNotContain("registerEvent_mechStart", deviceType1110Block);
        Assert.Contains("declare function events<const TEventNames extends readonly TsViewEventName[]>(...eventNames: TEventNames):", dts);
        Assert.DoesNotContain("declare const __TsViewAllowedEvents", dts);
        Assert.DoesNotContain("TsViewEventDecorator", dts);
        Assert.DoesNotContain("declare function EventGroup", dts);
        Assert.DoesNotContain("type EventGroupInstance", dts);
        Assert.DoesNotContain("declare function Event_start", dts);
    }

    [Fact]
    public void Dts_ShouldRemainGlobalScriptUnderNoLib()
    {
        string dts = BescriptTsViewDeclarationLibrary.GetSdkText();
        string usageText = """
            const values: Array<string> = ["ok"];

            @events("start")
            class UsageDevice extends DeviceType_77 {
            }

            declare const device: ICollidable;
            device.enableCollide(BeBool.fromBeConst("0"));
            """;

        TypeScriptSdkAnalysis analysis = AnalyzeSdkWithTypeScript(dts, usageText);

        Assert.False(analysis.IsExternalModule, "sdk.ts 必须保持全局脚本；顶层 import/export 会让 noLib 全局类型与 events 装饰器失效。");
        Assert.Empty(analysis.Diagnostics);
    }

    [Fact]
    public void Dts_MethodDeclRange_ShouldPointToConcreteMethodLine()
    {
        string dts = BescriptTsViewDeclarationLibrary.GetSdkText();
        BescriptDtsGenerator.DeclLineRange? range = BescriptDtsGenerator.GetMethodDeclLineRange("BeDevice.callFun");

        BescriptDtsGenerator.DeclLineRange rangeValue = Assert.IsType<BescriptDtsGenerator.DeclLineRange>(range);
        string[] lines = TextLineEndings.ToLf(dts).Split('\n');
        Assert.InRange(rangeValue.StartLine, 1, lines.Length);
        Assert.Contains("callFun(", lines[rangeValue.StartLine - 1]);
        Assert.DoesNotContain("declare class", lines[rangeValue.StartLine - 1]);

        BescriptDtsGenerator.DeclLineRange? globalRange = BescriptDtsGenerator.GetMethodDeclLineRange("G.map.getGlobalDataAsync");
        BescriptDtsGenerator.DeclLineRange globalRangeValue = Assert.IsType<BescriptDtsGenerator.DeclLineRange>(globalRange);
        Assert.InRange(globalRangeValue.StartLine, 1, lines.Length);
        Assert.Contains("function getGlobalDataAsync(", lines[globalRangeValue.StartLine - 1]);
    }

    private sealed record TypeScriptSdkAnalysis(bool IsExternalModule, string[] Diagnostics);

    private static TypeScriptSdkAnalysis AnalyzeSdkWithTypeScript(string sdkText, string usageText)
    {
        using V8ScriptEngine engine = new(V8ScriptEngineFlags.DisableGlobalMembers);
        string tsJs = EmbeddedResourceHelper.LoadText("Toolkit.Core.tsview.v8.typescript.js.gz");
        engine.Execute("typescript.js", tsJs);
        engine.Script.___sdkText = sdkText;
        engine.Script.___usageText = usageText;
        string json = (string)engine.Evaluate("""
            (function (sdkText, usageText) {
                const files = {
                    "sdk.ts": sdkText,
                    "usage.ts": usageText
                };
                const options = {
                    noLib: true,
                    strict: true,
                    noEmit: true,
                    skipLibCheck: false,
                    experimentalDecorators: true,
                    noImplicitAny: true,
                    strictNullChecks: true,
                    noUnusedLocals: false,
                    noUnusedParameters: false,
                    allowUnreachableCode: true,
                    target: ts.ScriptTarget.ES2015,
                    module: ts.ModuleKind.None
                };
                const host = {
                    getSourceFile: function (fileName, languageVersion) {
                        return Object.prototype.hasOwnProperty.call(files, fileName)
                            ? ts.createSourceFile(fileName, files[fileName], languageVersion, true, ts.ScriptKind.TS)
                            : undefined;
                    },
                    writeFile: function () {},
                    getDefaultLibFileName: function () { return ""; },
                    getCurrentDirectory: function () { return ""; },
                    getDirectories: function () { return []; },
                    fileExists: function (fileName) { return Object.prototype.hasOwnProperty.call(files, fileName); },
                    readFile: function (fileName) { return files[fileName]; },
                    useCaseSensitiveFileNames: function () { return true; },
                    getCanonicalFileName: function (fileName) { return fileName; },
                    getNewLine: function () { return "\n"; }
                };
                const program = ts.createProgram(["sdk.ts", "usage.ts"], options, host);
                const sdkSource = program.getSourceFile("sdk.ts");
                const diagnostics = ts.getPreEmitDiagnostics(program).map(function (diag) {
                    const message = ts.flattenDiagnosticMessageText(diag.messageText, "\n");
                    if (!diag.file || diag.start === undefined) return message;
                    const pos = diag.file.getLineAndCharacterOfPosition(diag.start);
                    return diag.file.fileName + ":" + (pos.line + 1) + ":" + (pos.character + 1) + " " + message;
                });
                return JSON.stringify({
                    isExternalModule: !!sdkSource.externalModuleIndicator,
                    diagnostics: diagnostics
                });
            })(___sdkText, ___usageText)
            """);
        TypeScriptSdkAnalysis? analysis = JsonSerializer.Deserialize<TypeScriptSdkAnalysis>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        return analysis ?? throw new InvalidOperationException("TypeScript SDK 分析结果反序列化失败");
    }

    [Fact]
    public void SdkDocsQuery_Path_ShouldReturnMethodLevelLocation()
    {
        SdkDocsQueryTool tool = new();
        (SdkDocsQueryData? data, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> warnings) = tool.Query(
            new SdkDocsQueryArgs("path", null, "BeDevice.callFun", null, "ts"));

        Assert.Empty(errors);
        Assert.Empty(warnings);
        SdkDocsQueryData queryData = Assert.IsType<SdkDocsQueryData>(data);
        IReadOnlyList<SdkDocHit> docs = Assert.IsAssignableFrom<IReadOnlyList<SdkDocHit>>(queryData.Docs);
        Assert.NotEmpty(docs);
        Assert.Contains(docs, static doc =>
            doc.TsPath == "BeDevice.callFun" &&
            doc.BesPaths.Contains("varf.Device.callFun0", StringComparer.Ordinal) &&
            doc.Snippet.StartsWith("varf.Device.callFun0(", StringComparison.Ordinal) &&
            doc.TsDeclLocation is not null &&
            doc.TsDeclLocation.Contains("sdk.ts:", StringComparison.Ordinal) &&
            doc.TsDeclLocation.Contains("callFun(", StringComparison.Ordinal));

        (SdkDocsQueryData? globalData, IReadOnlyList<ToolError> globalErrors, IReadOnlyList<ToolError> globalWarnings) = tool.Query(
            new SdkDocsQueryArgs("path", null, "G.map.getGlobalDataAsync", null, "ts"));

        Assert.Empty(globalErrors);
        Assert.Empty(globalWarnings);
        SdkDocsQueryData globalQueryData = Assert.IsType<SdkDocsQueryData>(globalData);
        IReadOnlyList<SdkDocHit> globalDocs = Assert.IsAssignableFrom<IReadOnlyList<SdkDocHit>>(globalQueryData.Docs);
        Assert.Contains(globalDocs, static doc =>
            doc.TsPath == "G.map.getGlobalDataAsync" &&
            doc.BesPaths.Contains("fun.map.getGlobalDataAsync", StringComparer.Ordinal) &&
            doc.Snippet.StartsWith("fun.map.getGlobalDataAsync(", StringComparison.Ordinal) &&
            doc.TsDeclLocation is not null &&
            doc.TsDeclLocation.Contains("function getGlobalDataAsync(", StringComparison.Ordinal));

        (SdkDocsQueryData? uiData, IReadOnlyList<ToolError> uiErrors, IReadOnlyList<ToolError> uiWarnings) = tool.Query(
            new SdkDocsQueryArgs("path", null, "BeUI.active", null, "ts"));

        Assert.Empty(uiErrors);
        Assert.Empty(uiWarnings);
        SdkDocsQueryData uiQueryData = Assert.IsType<SdkDocsQueryData>(uiData);
        IReadOnlyList<SdkDocHit> uiDocs = Assert.IsAssignableFrom<IReadOnlyList<SdkDocHit>>(uiQueryData.Docs);
        Assert.Contains(uiDocs, static doc =>
            doc.TsPath == "BeUI.active" &&
            doc.BesPaths.Contains("varf.UI.active", StringComparer.Ordinal) &&
            doc.TsDeclLocation is not null &&
            doc.TsDeclLocation.Contains("active(", StringComparison.Ordinal));

        (SdkDocsQueryData? derivedUiData, IReadOnlyList<ToolError> derivedUiErrors, IReadOnlyList<ToolError> derivedUiWarnings) = tool.Query(
            new SdkDocsQueryArgs("path", null, "BeUILabel.active", null, "ts"));

        Assert.Empty(derivedUiErrors);
        Assert.Empty(derivedUiWarnings);
        SdkDocsQueryData derivedUiQueryData = Assert.IsType<SdkDocsQueryData>(derivedUiData);
        IReadOnlyList<SdkDocHit> derivedUiDocs = Assert.IsAssignableFrom<IReadOnlyList<SdkDocHit>>(derivedUiQueryData.Docs);
        Assert.Empty(derivedUiDocs);
    }


    [Fact]
    public void EnumSubtypeOutput_ShouldKeepMemberTrailingComments_And_BeEnumClassDoc()
    {
        MethodEnumType enumType = new MethodEnumType(
            "TestEnum",
            "测试枚举",
            new[]
            {
                new EnumMember("Alpha", Description: "首项"),
                new EnumMember("Beta", Alias: "BetaAlias", Description: "第二项")
            });

        MethodSignature[] methods = new[]
        {
            new MethodSignature(
                MethodCategory.GlobalFunc,
                null,
                "test.enumUse",
                new[] { new MethodParameter("mode", "Enum", EnumType: enumType) },
                null,
                "测试方法")
        };

        Type? generatorType = typeof(BescriptTsViewDeclarationLibrary).Assembly.GetType("Toolkit.Core.Sdk.BescriptDtsGenerator");
        Assert.NotNull(generatorType);

        MethodInfo? method = generatorType.GetMethod(
            "AppendEnumSubtypeClasses",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        StringBuilder sb = new StringBuilder();
        method.Invoke(null, new object?[]
        {
            sb,
            methods,
            Array.Empty<MethodSignature>(),
            Array.Empty<MethodSignature>(),
            true
        });

        string text = TextLineEndings.ToLf(sb.ToString());
        Assert.Contains("/** 测试枚举 */\ndeclare enum TestEnum {", text);
        Assert.Contains("  Alpha = \"0\", // 首项", text);
        Assert.Contains("  BetaAlias = \"1\", // 第二项", text);
        Assert.Contains("/** 枚举类型，值类型 */\ndeclare class BeEnum<T extends EnumType> {", text);
    }

    [Fact]
    public void Dts_CallClientMapRetAndCallMapFunc_ShouldExist()
    {
        string dts = BescriptTsViewDeclarationLibrary.GetSdkText();

        Assert.Contains("callClientMapRet", dts);
        Assert.Contains("callMapFunc", dts);

        // callClientMapRet 应包含全部 13 个类型
        Assert.Contains("BeFloat", dts[dts.IndexOf("callClientMapRet")..]);
        Assert.Contains("BeBool", dts[dts.IndexOf("callClientMapRet")..]);
        Assert.Contains("BeString", dts[dts.IndexOf("callClientMapRet")..]);
        Assert.Contains("BeLong", dts[dts.IndexOf("callClientMapRet")..]);
        Assert.Contains("BeColor", dts[dts.IndexOf("callClientMapRet")..]);
        Assert.Contains("BeVector3", dts[dts.IndexOf("callClientMapRet")..]);
        Assert.Contains("BeList", dts[dts.IndexOf("callClientMapRet")..]);
        Assert.Contains("BeDict", dts[dts.IndexOf("callClientMapRet")..]);
        Assert.Contains("BeStruct", dts[dts.IndexOf("callClientMapRet")..]);
        Assert.Contains("BeDevice", dts[dts.IndexOf("callClientMapRet")..]);
        Assert.Contains("BeMech", dts[dts.IndexOf("callClientMapRet")..]);
        Assert.Contains("BePlayer", dts[dts.IndexOf("callClientMapRet")..]);
        Assert.Contains("BeDatetime", dts[dts.IndexOf("callClientMapRet")..]);

        // callMapFunc 应被压缩为可变参数形式
        Assert.DoesNotContain("callMapFunc0", dts);
        Assert.DoesNotContain("callMapFunc1", dts);
    }

    [Fact]
    public void Dts_VariadicMethods_ShouldBeCompressed()
    {
        string dts = BescriptTsViewDeclarationLibrary.GetSdkText();

        // bool.and 应该被压缩为带可选参数的单一签名，统一命名 v1, v2, v3...
        Assert.Contains("function and(v1: BeBool | Const<BeBool>, v2: BeBool | Const<BeBool>, v3?: BeBool | Const<BeBool>", dts);
        // 不应该有单独的基础方法重载
        Assert.DoesNotContain("function and(v1: BeBool | Const<BeBool>, v2: BeBool | Const<BeBool>): BeBool", dts);

        // bool.or 同理
        Assert.Contains("function or(v1: BeBool | Const<BeBool>, v2: BeBool | Const<BeBool>, v3?: BeBool | Const<BeBool>", dts);

        // float.add 应该被压缩
        Assert.Contains("function add(v1: BeFloat | Const<BeFloat>, v2: BeFloat | Const<BeFloat>, v3?: BeFloat | Const<BeFloat>", dts);

        Assert.Contains("function keyDown2(keycode: Const<BeEnum<KeyCode>>): BeBool;", dts);
        Assert.Contains("function lock(type: Const<BeEnum<MouseLockMode>>): void;", dts);
        Assert.Contains("fillMode(fillMethod: Const<BeEnum<UiFillMethod>>, scale: BeFloat | Const<BeFloat>): void;", dts);
    }

    [Fact]
    public void CompressionTable_ShouldMapBidirectionally()
    {
        // 正向映射: 原始名 -> 压缩名
        (bool Success, string CompressedName, string? GenericBeTypeName) globalResult1 = BescriptDtsGenerator.TryGetGlobalCompressedName("bool.and");
        Assert.True(globalResult1.Success);
        Assert.Equal("bool.and", globalResult1.CompressedName);

        (bool Success, string CompressedName, string? GenericBeTypeName) globalResult2 = BescriptDtsGenerator.TryGetGlobalCompressedName("bool.and1");
        Assert.True(globalResult2.Success);
        Assert.Equal("bool.and", globalResult2.CompressedName);

        (bool Success, string CompressedName, string? GenericBeTypeName) globalResult3 = BescriptDtsGenerator.TryGetGlobalCompressedName("bool.and4");
        Assert.True(globalResult3.Success);
        Assert.Equal("bool.and", globalResult3.CompressedName);

        // 反向映射: 压缩名 -> 原始名候选
        IReadOnlyList<BescriptDtsGenerator.MemberCompressionEntry> candidates = BescriptDtsGenerator.GetGlobalOriginalCandidates("bool.and");
        Assert.True(candidates.Count >= 5); // bool.and, bool.and1, bool.and2, bool.and3, bool.and4
        Assert.Contains(candidates, static c => c.OriginalName == "bool.and" && c.ParameterCount == 2);
        Assert.Contains(candidates, static c => c.OriginalName == "bool.and1" && c.ParameterCount == 3);
        Assert.Contains(candidates, static c => c.OriginalName == "bool.and4" && c.ParameterCount == 6);

        // VarFunc 压缩映射
        (bool Success, string CompressedName, string? GenericBeTypeName) varFuncResult1 = BescriptDtsGenerator.TryGetVarFuncCompressedName("List", "add");
        Assert.True(varFuncResult1.Success);
        Assert.Equal("add", varFuncResult1.CompressedName);

        (bool Success, string CompressedName, string? GenericBeTypeName) varFuncResult2 = BescriptDtsGenerator.TryGetVarFuncCompressedName("List", "add1");
        Assert.True(varFuncResult2.Success);
        Assert.Equal("add", varFuncResult2.CompressedName);

        IReadOnlyList<BescriptDtsGenerator.MemberCompressionEntry> listCandidates = BescriptDtsGenerator.GetVarFuncOriginalCandidates("List", "add");
        Assert.True(listCandidates.Count >= 2);
        Assert.Contains(listCandidates, static c => c.OriginalName == "add" && c.ParameterCount == 1);
        Assert.Contains(listCandidates, static c => c.OriginalName == "add1" && c.ParameterCount == 2);

        // string.Format 压缩映射
        (bool Success, string CompressedName, string? GenericBeTypeName) formatResult1 = BescriptDtsGenerator.TryGetGlobalCompressedName("string.Format1");
        Assert.True(formatResult1.Success);
        Assert.Equal("string.Format", formatResult1.CompressedName);
        (bool Success, string CompressedName, string? GenericBeTypeName) formatResult2 = BescriptDtsGenerator.TryGetGlobalCompressedName("string.Format5");
        Assert.True(formatResult2.Success);
        Assert.Equal("string.Format", formatResult2.CompressedName);

        IReadOnlyList<BescriptDtsGenerator.MemberCompressionEntry> formatCandidates = BescriptDtsGenerator.GetGlobalOriginalCandidates("string.Format");
        Assert.True(formatCandidates.Count >= 5);
        Assert.Contains(formatCandidates, static c => c.OriginalName == "string.Format1" && c.ParameterCount == 2);
        Assert.Contains(formatCandidates, static c => c.OriginalName == "string.Format5" && c.ParameterCount == 6);
    }

    [Fact]
    public void Dts_UiBase_ShouldContainAllCommonMethods()
    {
        string dts = BescriptTsViewDeclarationLibrary.GetSdkText();

        HashSet<string> methods = ExtractUiBaseMethodNames(dts);
        _output.WriteLine($"BeUI methodCount = {methods.Count}");

        string[] expected = new[]
        {
            "pos",
            "getPos",
            "setCanvasPos",
            "getCanvasPos",
            "setPixelPos",
            "size",
            "getSize",
            "active",
            "isActive",
            "activeInHierarchy",
            "setColor",
            "shadow",
            "outline",
            "rot",
            "rot3D",
            "rotation",
            "scale",
            "alignment",
            "parentAnchor",
            "breakLink",
            "addSon",
            "sonNum",
            "getSonUIButton",
            "getSonUIInput",
            "getSonUILabel",
            "getSonUIRawImage",
            "getSonUIRect",
            "setSiblingIndex",
            "moveToTop",
            "del",
            "delSon",
            "delAllChild",
            "exist",
            "equal",
            "followDevice",
            "setOwner",
            "isPosInside",
            "posToLocal"
        };

        string[] missing = expected.Where(x => !methods.Contains(x)).ToArray();
        if (missing.Length > 0)
        {
            _output.WriteLine("missing: " + string.Join(", ", missing));
        }

        Assert.Empty(missing);
    }

    private static HashSet<string> ExtractUiBaseMethodNames(string dts)
    {
        string[] lines = TextLineEndings.ToLf(dts).Split('\n');
        int start = Array.FindIndex(lines, static l => string.Equals(l, "declare abstract class BeUI {", StringComparison.Ordinal));
        Assert.True(start >= 0, "未找到 declare abstract class BeUI {");

        HashSet<string> methods = new HashSet<string>(StringComparer.Ordinal);
        for (int i = start + 1; i < lines.Length; i++)
        {
            string line = lines[i];
            // 类体结束行无缩进（生成器固定输出）
            if (string.Equals(line, "}", StringComparison.Ordinal))
            {
                break;
            }

            // JSDoc 注释行包含 /** 标记；方法行不含
#pragma warning disable TK008 // DTS 文本解析：空行跳过
            if (line.Length == 0 || line.IndexOf("/**", StringComparison.Ordinal) >= 0)
#pragma warning restore TK008
            {
                continue;
            }

            int parenIndex = line.IndexOf('(');
            if (parenIndex < 0)
            {
                continue;
            }

            string raw = line[..parenIndex];
            int nameStart = 0;
            while (nameStart < raw.Length && raw[nameStart] == ' ')
            {
                nameStart++;
            }

            string name = raw[nameStart..];
            if (name.Length >= 2 && name[0] == '"' && name[^1] == '"')
            {
                name = name.Substring(1, name.Length - 2);
            }

            methods.Add(name);
        }

        return methods;
    }
}
