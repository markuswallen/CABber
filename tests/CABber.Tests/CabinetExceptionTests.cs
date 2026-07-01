using CABber;
using Xunit;

namespace CABber.Tests;

public class CabinetExceptionTests
{
    [Fact]
    public void MessageOnlyConstructor_LeavesErrorCodeAndTypeAtDefault()
    {
        var ex = new CabinetException("boom");

        Assert.Equal("boom", ex.Message);
        Assert.Equal(0, ex.ErrorCode);
        Assert.Equal(0, ex.ErrorType);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public void InnerExceptionConstructor_PreservesInnerException()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new CabinetException("outer", inner);

        Assert.Equal("outer", ex.Message);
        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public void ErrorCodeConstructor_PopulatesErrorCodeAndType()
    {
        var ex = new CabinetException("native failure", errorCode: 4, errorType: 1);

        Assert.Equal(4, ex.ErrorCode);
        Assert.Equal(1, ex.ErrorType);
    }

    [Theory]
    [InlineData(typeof(CabinetNotFoundException))]
    [InlineData(typeof(CabinetCorruptException))]
    [InlineData(typeof(CabinetIOException))]
    public void Subtypes_DeriveFromCabinetException(Type subtype)
    {
        Assert.True(typeof(CabinetException).IsAssignableFrom(subtype));
    }

    [Fact]
    public void CabinetNotFoundException_ErrorCodeConstructor_PopulatesErrorCodeAndType()
    {
        var ex = new CabinetNotFoundException("missing", errorCode: 1, errorType: 0);

        Assert.Equal(1, ex.ErrorCode);
        Assert.IsAssignableFrom<CabinetException>(ex);
    }

    [Fact]
    public void CabinetCorruptException_ErrorCodeConstructor_PopulatesErrorCodeAndType()
    {
        var ex = new CabinetCorruptException("corrupt", errorCode: 4, errorType: 0);

        Assert.Equal(4, ex.ErrorCode);
        Assert.IsAssignableFrom<CabinetException>(ex);
    }

    [Fact]
    public void CabinetIOException_ErrorCodeConstructor_PopulatesErrorCodeAndType()
    {
        var ex = new CabinetIOException("io failure", errorCode: 8, errorType: 0);

        Assert.Equal(8, ex.ErrorCode);
        Assert.IsAssignableFrom<CabinetException>(ex);
    }
}
