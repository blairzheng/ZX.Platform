using ZX.Platform.LowCode.Infrastructure.Oracle;

namespace ZX.Platform.LowCode.Tests;

public class OracleIdentifierTests
{
    [Fact]
    public void 标识符转大写并加引号()
    {
        Assert.Equal("\"CODE\"", OracleIdentifier.Quote("code"));
        Assert.Equal("\"SBU_ID\"", OracleIdentifier.Quote("sbu_id"));
    }

    [Theory]
    [InlineData("co de")]
    [InlineData("co\"de")]
    [InlineData("")]
    [InlineData("   ")]
    public void 非法标识符被拒绝(string identifier)
    {
        Assert.Throws<ArgumentException>(() => OracleIdentifier.Quote(identifier));
    }

    [Fact]
    public void 超长标识符截断后仍不超过上限且可复现()
    {
        const string name = "UX_LC_MES_STOCKTAKE_LINE_ACTIVE_MATERIAL_CODE";

        var first = OracleIdentifier.Shorten(name);
        var second = OracleIdentifier.Shorten(name);

        Assert.Equal(first, second);
        Assert.True(first.Length <= OracleIdentifier.MaxLength);
        Assert.NotEqual(OracleIdentifier.Shorten(name + "_2"), first);
    }

    [Fact]
    public void 短标识符不截断()
    {
        Assert.Equal("IX_LC_MES_STATION_CODE", OracleIdentifier.Shorten("IX_LC_MES_STATION_CODE"));
    }

    [Fact]
    public void 限定名带schema()
    {
        Assert.Equal("\"ZX_LOWCODE\".\"LC_MES_STATION\"",
            OracleIdentifier.QuoteQualified("zx_lowcode", "lc_mes_station"));
    }
}
