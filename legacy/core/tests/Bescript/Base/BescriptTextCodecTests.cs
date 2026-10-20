using Xunit;
using Toolkit.Core.Bescript;

namespace Toolkit.Core.Tests.Bescript.Base;

public class BescriptTextCodecTests
{
    [Fact]
    public void ReplaceInvalidChar_Should_Escape_Reserved_Chars()
    {
        string input = "a:b;c=d(e)f/g\\h i.j@k";
        string escaped = BescriptTextCodec.ReplaceInvalidChar(input.AsSpan());
        Assert.Equal("a@1b;c@3d@4e@5f/g\\h i.j@@k", escaped);
    }

    [Fact]
    public void RecoverInvalidChar_Should_Decode_Reserved_Chars()
    {
        string input = "a@1b@2c@3d@4e@5f@6g@7h@8i@9j@@k";
        string decoded = BescriptTextCodec.RecoverInvalidChar(input.AsSpan());
        Assert.Equal("a:b;c=d(e)f/g\\h i.j@k", decoded);
    }

    [Fact]
    public void ReplaceAndRecover_Should_RoundTrip()
    {
        string original = "中文:;=() /\\ .@";
        string escaped = BescriptTextCodec.ReplaceInvalidChar(original.AsSpan());
        string decoded = BescriptTextCodec.RecoverInvalidChar(escaped.AsSpan());
        Assert.Equal(original, decoded);
    }

    [Fact]
    public void CanvasGuidReference_Should_Require_A_Valid_Guid()
    {
        const string guid = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4";
        string reference = BescriptSyntax.FormatNewRef(guid);

        Assert.Equal($"==guid@1{guid}==", BescriptTextCodec.ReplaceInvalidChar(reference.AsSpan()));
        Assert.Throws<ArgumentException>(() => BescriptSyntax.FormatNewRef("not-a-guid"));
    }
}
