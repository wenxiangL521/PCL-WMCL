using WMCL.Launch;

namespace WMCL.Launch.Tests;

public class OfflineAccountTests
{
    [Fact]
    public void MatchesJavaOfflineUuid()
    {
        // Java: UUID.nameUUIDFromBytes("OfflinePlayer:Notch".getBytes(UTF_8))
        Assert.Equal(
            "b50ad385-829d-3141-a216-7e7d7539ba7f",
            AccountInfo.OfflineUuid("Notch"));
    }

    [Fact]
    public void OfflineAccountUsesZeroToken()
    {
        var account = AccountInfo.Offline("Steve");

        Assert.Equal("Steve", account.Name);
        Assert.Equal("0", account.AccessToken);
        Assert.Equal("legacy", account.UserType);
        Assert.Equal(36, account.Uuid.Length);
    }
}
