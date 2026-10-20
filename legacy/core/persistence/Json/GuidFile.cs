using Hjson;
using Toolkit.Core.Infra;

namespace Toolkit.Core.Persistence;

internal static class GuidFile
{
    internal sealed record Content(string Guid, JsonObject? Object, string? Slice);

    public static Content Parse(string text)
    {
        JsonValue root;
        try
        {
            root = HjsonText.Parse(text);
        }
        catch (Exception) when (GuidText.Validate(text))
        {
            return new Content(text, null, null);
        }

        if (root.JsonType == JsonType.String)
        {
            string pureGuid = (string)root;
            if (!GuidText.Validate(pureGuid) || !string.Equals(text, pureGuid, StringComparison.Ordinal))
                throw new InvalidDataException("GUID 文件内容无效：不是有效的 32 位十六进制 GUID");

            return new Content(pureGuid, null, null);
        }

        if (root is not JsonObject obj)
        {
            if (GuidText.Validate(text))
                return new Content(text, null, null);

            throw new InvalidDataException("GUID 文件内容无效：必须为完整 GUID 文本或包含 guid 字段的 Hjson 对象");
        }

        string guid = HjsonSchema.RequireString(obj, "guid", "GUID 文件");
        if (!GuidText.Validate(guid))
            throw new InvalidDataException("GUID 文件内容无效：不是有效的 32 位十六进制 GUID");

        return new Content(guid, obj, HjsonSchema.OptionalString(obj, "slice"));
    }

    public static Content? TryParse(string text)
    {
        try
        {
            return Parse(text);
        }
        catch
        {
            return null;
        }
    }
}
