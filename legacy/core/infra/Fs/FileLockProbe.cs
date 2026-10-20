using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace Toolkit.Core.Infra;

// =============================================================================
// 文件占用进程诊断 — Windows Restart Manager 封装
// =============================================================================
//
// 受阻态提示需报出"是谁在干扰"（占用进程名 + PID），让用户知道该关哪个程序。
// 本封装是项目里唯一查询文件占用归属的地方，与 SystemIOWrapper 同目录同边界定位
// （前者管文件读写，本类管文件占用归属）。
//
// Windows 无"谁锁了文件"直查 API，Restart Manager（rstrtmgr.dll）是官方标准方案
// （Windows 安装器同款），比句柄枚举可靠。
//
// 边界铁律：本类是诊断增强，任何失败（非 Windows / API 错误 / 无占用）一律返回空串，
// 绝不抛异常 —— 失败不得阻塞受阻提示的生成与返回。
//
// =============================================================================

/// <summary>
/// 查询文件/目录被哪些外部进程占用。
/// </summary>
public static class FileLockProbe
{
    private const int CchRmMaxAppName = 255;
    private const int CchRmMaxSvcName = 63;
    private const int ErrorSuccess = 0;
    private const int ErrorMoreData = 234;

    /// <summary>
    /// 返回占用 <paramref name="fullPath"/> 的进程列表，格式 "Name(pid), Name2(pid2)"。
    /// 无占用者 / 查询失败 / 非 Windows 返回空串。目录展开其顶层文件批量查询。
    /// </summary>
    public static string DescribeLockers(string fullPath)
    {
        if (!OperatingSystem.IsWindows()) return string.Empty;

        try
        {
            // Restart Manager 不接受目录路径 —— 目录展开顶层文件批量注册（一次会话查多文件）。
            string[] files = Directory.Exists(fullPath)
                ? Directory.GetFiles(fullPath, "*", SearchOption.TopDirectoryOnly)
                : [fullPath];
            if (files.Length == 0) return string.Empty;

            return QueryAndFormat(files);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string QueryAndFormat(string[] files)
    {
        int rc = RmStartSession(out uint session, 0, Guid.NewGuid().ToString());
        if (rc != ErrorSuccess) return string.Empty;

        try
        {
            rc = RmRegisterResources(session, (uint)files.Length, files, 0, null, 0, null);
            if (rc != ErrorSuccess) return string.Empty;

            uint pnProcInfoNeeded = 0;
            uint pnProcInfo = 0;
            uint rebootReasons = 0;

            // 第一趟：数组传 null 探出所需数量（ERROR_MORE_DATA）。
            rc = RmGetList(session, out pnProcInfoNeeded, ref pnProcInfo, null, ref rebootReasons);
            if (rc == ErrorSuccess) return string.Empty; // 无占用者
            if (rc != ErrorMoreData) return string.Empty;

            RM_PROCESS_INFO[] infos = new RM_PROCESS_INFO[pnProcInfoNeeded];
            pnProcInfo = pnProcInfoNeeded;
            rc = RmGetList(session, out pnProcInfoNeeded, ref pnProcInfo, infos, ref rebootReasons);
            if (rc != ErrorSuccess) return string.Empty;

            return FormatDistinct(infos, pnProcInfo);
        }
        finally
        {
            _ = RmEndSession(session);
        }
    }

    /// <summary>
    /// 同一进程占用多文件时按 PID 去重，格式 "Name(pid)"。PID=0 跳过。
    /// </summary>
    private static string FormatDistinct(RM_PROCESS_INFO[] infos, uint count)
    {
        Dictionary<uint, string> byPid = [];
        for (int i = 0; i < count; i++)
        {
            uint pid = infos[i].Process.dwProcessId;
            if (pid == 0) continue;
            string name = infos[i].strAppName;
#pragma warning disable TK008 // strAppName 为空时以 PID 兜底显示（Windows 互操作边界）
            byPid.TryAdd(pid, name.Length == 0 ? pid.ToString(System.Globalization.CultureInfo.InvariantCulture) : name);
#pragma warning restore TK008
        }

        if (byPid.Count == 0) return string.Empty;

        StringBuilder sb = new();
        foreach (KeyValuePair<uint, string> kv in byPid)
        {
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(kv.Value).Append('(').Append(kv.Key).Append(')');
        }
        return sb.ToString();
    }

    // ========== Restart Manager P/Invoke ==========
#pragma warning disable TK001 // P/Invoke 互操作必需 out/ref 修饰符

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint pSessionHandle);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(
        uint dwSessionHandle,
        uint nFiles,
        string[] rgsFilenames,
        uint nApplications,
        [In] RM_UNIQUE_PROCESS[]? rgApplications,
        uint nServices,
        string[]? rgsServiceNames);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmGetList(
        uint dwSessionHandle,
        out uint pnProcInfoNeeded,
        ref uint pnProcInfo,
        [In, Out] RM_PROCESS_INFO[]? rgAffectedApps,
        ref uint lpdwRebootReasons);

#pragma warning restore TK001

    [StructLayout(LayoutKind.Sequential)]
    private struct RM_UNIQUE_PROCESS
    {
        public uint dwProcessId;
        public FILETIME ProcessStartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RM_PROCESS_INFO
    {
        public RM_UNIQUE_PROCESS Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchRmMaxAppName)]
        public string strAppName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchRmMaxSvcName)]
        public string strServiceShortName;
        public int ApplicationType;
        public uint AppStatus;
        public uint dwSessionId;
        [MarshalAs(UnmanagedType.Bool)]
        public bool bRestartable;
    }

}
