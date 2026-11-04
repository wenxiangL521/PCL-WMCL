using WMCL.App.ViewModels.Instances;

namespace WMCL.App.Tests;

public sealed class InstanceIdentityTests
{
    [Fact]
    public void EmptyCategoryBecomesUncategorized()
    {
        var identity = InstanceIdentity.From(null, "  ", false, null);
        Assert.Equal(InstanceIdentity.Uncategorized, identity.Category);
        Assert.Equal("", identity.StoredCategory);
        Assert.Equal("", identity.TrimmedDescription);
        Assert.Equal("", identity.TrimmedLogoPath);
    }

    [Fact]
    public void CustomCategoryPersistsVerbatim()
    {
        var identity = InstanceIdentity.From(" 说明 ", " 生存 ", true, " logo.png ");
        Assert.Equal("生存", identity.Category);
        Assert.Equal("生存", identity.StoredCategory);
        Assert.Equal("说明", identity.TrimmedDescription);
        Assert.Equal("logo.png", identity.TrimmedLogoPath);
        Assert.True(identity.Favorite);
    }

    [Fact]
    public void UncategorizedRoundTripsAsEmpty()
    {
        var identity = InstanceIdentity.From("d", "", false, "");
        var restored = InstanceIdentity.From(identity.TrimmedDescription, identity.StoredCategory, identity.Favorite, identity.TrimmedLogoPath);
        Assert.Equal(identity, restored);
    }
}
