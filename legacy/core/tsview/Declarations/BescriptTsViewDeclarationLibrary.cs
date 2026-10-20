using Toolkit.Core.Sdk;

namespace Toolkit.Core.TsView;

/// <summary>
/// BEScript TypeScript 声明库，提供 sdk.ts 文件内容。
/// 【职责边界】生成 BEScript SDK 的 TypeScript 类型声明。
/// </summary>
public static class BescriptTsViewDeclarationLibrary
{
    public const string SdkFileName = "sdk.ts";

    private static readonly Lazy<string> SdkText =
        new(BescriptDtsGenerator.Generate, LazyThreadSafetyMode.ExecutionAndPublication);

    public static string GetSdkText() => SdkText.Value;
}
