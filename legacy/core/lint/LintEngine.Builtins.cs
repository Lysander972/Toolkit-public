using Toolkit.Core.Sdk;

namespace Toolkit.Core.Lint;

public static partial class LintEngine
{
    private static HashSet<string> BuildBuiltinDeviceMethodNames()
    {
        BuiltinMethodRegistry registry = BuiltinMethodRegistry.Instance;
        HashSet<string> names = new(StringComparer.Ordinal);

        const int pageSize = 1000;
        int skip = 0;
        while (true)
        {
            MethodQueryResult page = registry.Query(
                namePattern: null,
                category: MethodCategory.DeviceAction,
                ownerType: null,
                skip: skip,
                take: pageSize);

            foreach (MethodSignature sig in page.Items)
            {
                names.Add(sig.MethodName);
            }

            if (page.Items.Count == 0 || skip + page.Items.Count >= page.Total)
            {
                break;
            }

            skip += page.Items.Count;
        }

        return names;
    }

    private static HashSet<string> BuildBuiltinParamNames()
    {
        EventSignatureRegistry registry = EventSignatureRegistry.Instance;
        HashSet<string> names = new(StringComparer.Ordinal);

        foreach (EventSignature sig in registry.GetAll())
        {
            foreach (EventParameter p in sig.Parameters)
            {
                names.Add(p.EffectiveName);
            }
        }

        return names;
    }

}
