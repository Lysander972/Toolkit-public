using System.Text.RegularExpressions;
using Xunit;
using Toolkit.Core.Infra;
using Toolkit.Core.Search;

namespace Toolkit.Core.Tests.Search;

public sealed class RegexSearchCommonTests
{
    [Fact]
    public void SnippetContextLength_IsPositive()
    {
        Assert.True(RegexSearchCommon.SnippetContextLength > 0);
    }

    [Fact]
    public void Fail_ReturnsNullData_WithSingleError()
    {
        (string? data, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> warnings) = RegexSearchCommon.Fail<string>("测试错误");

        Assert.Null(data);
        Assert.Single(errors);
        Assert.Equal("测试错误", errors[0].Message);
        Assert.Empty(warnings);
    }

    [Theory]
    [InlineData("hello", false)]
    [InlineData("hello", true)]
    [InlineData(".*test.*", false)]
    [InlineData("\\d+", false)]
    public void CreateRegex_ValidPattern_Succeeds(string pattern, bool caseSensitive)
    {
        Regex regex = RegexSearchCommon.CreateRegex(pattern, caseSensitive);
        Assert.NotNull(regex);
    }

    [Fact]
    public void CreateGuidSet_EmptyWhenNoGuids()
    {
        Assert.Empty(RegexSearchCommon.CreateGuidSet(null));
        Assert.Empty(RegexSearchCommon.CreateGuidSet(Array.Empty<string>()));
    }

    [Fact]
    public void CreateGuidSet_IsCaseSensitive()
    {
        HashSet<string> set = RegexSearchCommon.CreateGuidSet(new[] { "ABC123" });

        Assert.DoesNotContain("abc123", set);
        Assert.Contains("ABC123", set);
    }

    [Fact]
    public void AddUntilLimit_StopsAtLimit()
    {
        List<int> list = [1, 2];
        RegexSearchCommon.AddUntilLimit(list, new[] { 3, 4, 5, 6 }, 4);

        Assert.Equal(4, list.Count);
        Assert.Equal(new[] { 1, 2, 3, 4 }, list);
    }
}
