using Hjson;
using Xunit;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Tests.Common;

public sealed class MechJsonRecordTests
{
    [Fact]
    public void Parse_Should_Read_DeviceEntries_From_String_List()
    {
        MechJsonRecord record = new(
            """
name:test
list:
[
 9-9999
 1-1101
]
""");

        Assert.Collection(
            record.DeviceEntries,
            static entry =>
            {
                Assert.Equal(9, entry.DeviceId);
                Assert.Equal(9999, entry.DeviceType);
            },
            static entry =>
            {
                Assert.Equal(1, entry.DeviceId);
                Assert.Equal(1101, entry.DeviceType);
            });
    }

    [Fact]
    public void Parse_Should_Read_DeviceEntries_Recursively_From_Nested_List()
    {
        MechJsonRecord record = new(
            """
name:test
list:
[
 {
  n:group
  list:
  [
   3-3303
   4-4404
  ]
 }
 9-9999
]
""");

        Assert.Collection(
            record.DeviceEntries,
            static entry =>
            {
                Assert.Equal(3, entry.DeviceId);
                Assert.Equal(3303, entry.DeviceType);
            },
            static entry =>
            {
                Assert.Equal(4, entry.DeviceId);
                Assert.Equal(4404, entry.DeviceType);
            },
            static entry =>
            {
                Assert.Equal(9, entry.DeviceId);
                Assert.Equal(9999, entry.DeviceType);
            });
    }

    [Fact]
    public void ToText_Should_Preserve_RawText_Unchanged()
    {
        string rawText =
            """
name:test
list:
[
 1-1101
 {
  n:group
  list:
  [
   2-2202
  ]
 }
]
""";

        MechJsonRecord record = new(rawText);

        string text = record.ToText();
        JsonValue rootValue = HjsonValue.Parse(text);
        JsonObject obj = Assert.IsType<JsonObject>(rootValue);
        Assert.Equal("test", (string)obj["name"]);
        Assert.Equal(rawText, text);
    }
}
