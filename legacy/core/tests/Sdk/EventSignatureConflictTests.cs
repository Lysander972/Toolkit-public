using Xunit;
using Xunit.Abstractions;
using Toolkit.Core.Sdk;

namespace Toolkit.Core.Tests.Sdk;

public class EventSignatureConflictTests
{
    private readonly ITestOutputHelper _output;

    public EventSignatureConflictTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void DetectEventSignatureConflicts()
    {
        EventSignatureRegistry registry = EventSignatureRegistry.Instance;
        IReadOnlyList<EventSignature> allSignatures = registry.GetAll();

        // 按事件名分组
        IEnumerable<IGrouping<string, EventSignature>> grouped = allSignatures.GroupBy(static sig => sig.EventName, StringComparer.Ordinal);

        List<string> conflicts = [];
        foreach (IGrouping<string, EventSignature> group in grouped)
        {
            if (group.Count() <= 1)
                continue;

            // 检查同名事件是否有不同的签名
            List<EventSignature> signatures = group.ToList();
            EventSignature firstSig = signatures[0];

            for (int i = 1; i < signatures.Count; i++)
            {
                EventSignature sig = signatures[i];
                if (!SignaturesMatch(firstSig, sig))
                {
                    string conflict = $"事件 '{group.Key}' 存在签名冲突:\n" +
                        $"  {firstSig.DeviceType}: {FormatSignature(firstSig)}\n" +
                        $"  {sig.DeviceType}: {FormatSignature(sig)}";
                    conflicts.Add(conflict);
                    _output.WriteLine(conflict);
                }
            }
        }

        if (conflicts.Count > 0)
        {
            _output.WriteLine($"\n总计发现 {conflicts.Count} 个签名冲突");
        }
        else
        {
            _output.WriteLine("未发现签名冲突");
        }
    }

    private bool SignaturesMatch(EventSignature sig1, EventSignature sig2)
    {
        if (sig1.Parameters.Count != sig2.Parameters.Count)
            return false;

        for (int i = 0; i < sig1.Parameters.Count; i++)
        {
            EventParameter p1 = sig1.Parameters[i];
            EventParameter p2 = sig2.Parameters[i];

            if (!string.Equals(p1.EffectiveName, p2.EffectiveName, StringComparison.Ordinal))
                return false;
            if (!string.Equals(p1.DisplayTypeName, p2.DisplayTypeName, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private string FormatSignature(EventSignature sig)
    {
        if (sig.Parameters.Count == 0)
            return "() => void";

        IEnumerable<string> paramStrs = sig.Parameters.Select(static p => $"{p.EffectiveName}: {p.TypeName}");
        return $"({string.Join(", ", paramStrs)}) => void";
    }
}
