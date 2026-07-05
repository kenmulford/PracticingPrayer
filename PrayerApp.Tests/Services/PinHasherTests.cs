using PrayerApp.Services.Confidential;
using Xunit;

namespace PrayerApp.Tests.Services;

/// <summary>
/// Direct unit coverage for the confidential-access PIN salted-hash + verify (issue #251).
/// Exercises the "correct PIN passes / wrong PIN fails / never plaintext / non-reversible /
/// per-install salt" requirements independently of ConfidentialAccessService's routing logic.
/// </summary>
public class PinHasherTests
{
    [Fact]
    public void Verify_CorrectPin_ReturnsTrue()
    {
        var record = PinHasher.Hash("123456");

        Assert.True(PinHasher.Verify("123456", record));
    }

    [Fact]
    public void Verify_WrongPin_ReturnsFalse()
    {
        var record = PinHasher.Hash("123456");

        Assert.False(PinHasher.Verify("654321", record));
    }

    [Fact]
    public void Hash_NeverContainsThePlaintextPin()
    {
        var record = PinHasher.Hash("123456");

        Assert.DoesNotContain("123456", record);
    }

    [Fact]
    public void Hash_SamePin_TwoInstalls_ProducesDifferentRecords()
    {
        // Per-install random salt — the defining property that defeats a precomputed
        // rainbow table across every install choosing the common PIN "123456".
        var recordA = PinHasher.Hash("123456");
        var recordB = PinHasher.Hash("123456");

        Assert.NotEqual(recordA, recordB);
        // ...but both still verify correctly against their own record.
        Assert.True(PinHasher.Verify("123456", recordA));
        Assert.True(PinHasher.Verify("123456", recordB));
    }

    [Fact]
    public void Verify_MalformedRecord_ReturnsFalseRatherThanThrowing()
    {
        Assert.False(PinHasher.Verify("123456", "not-a-valid-record"));
    }
}
