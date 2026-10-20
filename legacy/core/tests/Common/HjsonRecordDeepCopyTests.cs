using Hjson;
using Xunit;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Tests.Common;

public sealed class HjsonRecordDeepCopyTests
{
    [Fact]
    public void VarsConfig_DeepCopy_Should_Not_Duplicate_Variables()
    {
        string raw = """
[
{
 name:hp
 type:Float
}
{
 name:isBoss
 type:Bool
 comment:flag
}
]
""";

        VarsConfig original = new(raw);
        VarsConfig copy = (VarsConfig)original.DeepCopy();

        Assert.Collection(
            original.Variables,
            static v => Assert.Equal("hp", v.Name),
            static v => Assert.Equal("isBoss", v.Name));
        Assert.Collection(
            copy.Variables,
            static v => Assert.Equal("hp", v.Name),
            static v => Assert.Equal("isBoss", v.Name));
        Assert.Equal(original.Variables, copy.Variables);
    }

    [Fact]
    public void VarsConfig_DeepCopy_Chain_Should_Keep_Count_Stable()
    {
        string raw = """
[
{
 name:score
 type:Long
}
]
""";

        VarsConfig level1 = new(raw);
        VarsConfig level2 = (VarsConfig)level1.DeepCopy();
        VarsConfig level3 = (VarsConfig)level2.DeepCopy();

        Assert.Single(level1.Variables);
        Assert.Single(level2.Variables);
        Assert.Single(level3.Variables);
    }

    [Fact]
    public void ListJsonRecord_DeepCopy_Should_Not_Duplicate_Entries()
    {
        string raw = """
[
scriptA
{
 guid:a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4
 name:scriptB
}
]
""";

        ListJsonRecord original = new(raw);
        ListJsonRecord copy = (ListJsonRecord)original.DeepCopy();

        Assert.Collection(
            original.Entries,
            static e => Assert.Equal("scriptA", e.Name),
            static e => Assert.Equal("scriptB", e.Name));
        Assert.Collection(
            copy.Entries,
            static e => Assert.Equal("scriptA", e.Name),
            static e => Assert.Equal("scriptB", e.Name));
        Assert.Equal(original.Entries, copy.Entries);
    }

    [Fact]
    public void GuidJsonRecord_ToText_Should_Preserve_Unchanged_Pure_Guid()
    {
        const string guid = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4";
        GuidJsonRecord record = new(guid);

        Assert.Equal(guid, record.Guid);
        Assert.Equal(guid, record.ToText());
    }

    [Theory]
    [InlineData(" a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4")]
    [InlineData("a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4\n")]
    public void GuidJsonRecord_PureGuid_With_Whitespace_Should_Fail(string text)
    {
        Assert.Throws<InvalidDataException>(() => new GuidJsonRecord(text));
        Assert.Null(GuidFile.TryParse(text));
    }

    [Fact]
    public void GuidJsonRecord_SetGuid_Should_Preserve_Unmodeled_Object_Fields()
    {
        GuidJsonRecord record = new(
            """
guid:a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4
quality:80
""");

        record.SetGuid("b1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4");

        JsonObject obj = Assert.IsType<JsonObject>(HjsonValue.Parse(record.ToText()));
        Assert.Equal("b1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4", (string)obj["guid"]);
        Assert.Equal(80, obj["quality"].Qd());
    }

    [Fact]
    public void GuidJsonRecord_SetSlice_Should_Write_Guid_And_Slice()
    {
        GuidJsonRecord record = new("a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4");

        record.SetSlice("1,2,3,4,5,6,7,8");

        JsonObject obj = Assert.IsType<JsonObject>(HjsonValue.Parse(record.ToText()));
        Assert.Equal("a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4", (string)obj["guid"]);
        Assert.Equal("1,2,3,4,5,6,7,8", (string)obj["slice"]);
    }

    [Theory]
    [InlineData("a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4", "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4")]
    [InlineData("guid:a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4", "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4")]
    [InlineData("guid:not-a-guid", null)]
    [InlineData("a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4\n", null)]
    public void GuidFile_TryParse_Should_Use_Hjson_Parsing(string text, string? expected)
    {
        GuidFile.Content? content = GuidFile.TryParse(text);
        Assert.Equal(expected is not null, content is not null);
        Assert.Equal(expected, content?.Guid);
    }
}
