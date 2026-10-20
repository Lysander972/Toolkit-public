using System.Reflection;

namespace Toolkit.Mcp;

/// <summary>
/// Host 进程身份,供 /host/negotiate 端点返回,用于新旧形态协商复用同一 Host 实例。
/// InstanceId 进程启动生成一次;Version 唯一权威是 .bazelrc --embed_label(Bazel stamping),
/// 构建时由 Bazel 注入并冻结进程序集(运行时形态没有开发工作区,禁止运行时找文件)。
/// （旧版的 Bazel 现已不再使用，已改回纯 pnpm）
/// 协商双方版本号必须绝对一致才放行,无兼容空间;开发侧 bump 后需重建 Host(临时构建)。
/// </summary>
internal static class HostIdentity
{
    public static readonly string InstanceId = Guid.NewGuid().ToString("N");

    /// <summary>
    /// 库版本号,从程序集 InformationalVersion 读取（构建期自 Bazel stamping 冻结）。
    /// （旧版的 Bazel 现已不再使用，已改回纯 pnpm）
    /// 用 InformationalVersion 而非 Assembly.GetName().Version:前者保留权威原值("0.0.155"),
    /// 后者是四段 AssemblyVersion("0.0.155.0"),与权威原值不一致会导致协商版本比对失败。
    /// </summary>
    public static readonly string Version =
        typeof(HostIdentity).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>() is { InformationalVersion: { Length: > 0 } raw }
            ? raw.Split('+')[0]
            : throw new InvalidOperationException("程序集 InformationalVersion 缺失(构建未注入版本号)");
}

/// <summary>
/// /host/negotiate 响应体。字段经 System.Text.Json 默认 camelCase 序列化。
/// </summary>
internal sealed record HostNegotiationResponse(
    string WorkspaceRoot,
    string HostInstanceId,
    string Version,
    int Pid,
    bool AcceptReuse);
