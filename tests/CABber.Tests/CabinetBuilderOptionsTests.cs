using CABber;
using Xunit;

namespace CABber.Tests;

public class CabinetBuilderOptionsTests
{
    [Fact]
    public void Defaults_MatchDesignDoc()
    {
        var options = new CabinetBuilderOptions();

        Assert.Equal(int.MaxValue, options.MaxCabinetSize);
        Assert.Equal(CompressionType.MsZip, options.Compression);
        Assert.Null(options.Progress);
    }
}
