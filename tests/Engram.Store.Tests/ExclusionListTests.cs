using Engram.Store.Capture;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Tests for excluded app enforcement.
/// Production requirement: excluded apps are NEVER captured.
/// </summary>
public class ExclusionListTests
{
    [Fact]
    public void DefaultExclusions_IncludePasswordManagers()
    {
        var list = new ExclusionList();

        Assert.True(list.IsExcluded("1password"));
        Assert.True(list.IsExcluded("bitwarden"));
        Assert.True(list.IsExcluded("keepass"));
        Assert.True(list.IsExcluded("lastpass"));
        Assert.True(list.IsExcluded("dashlane"));
    }

    [Fact]
    public void IsExcluded_CaseInsensitive()
    {
        var list = new ExclusionList();

        Assert.True(list.IsExcluded("1Password"));
        Assert.True(list.IsExcluded("BITWARDEN"));
        Assert.True(list.IsExcluded("Keepass"));
    }

    [Fact]
    public void IsExcluded_ReturnsFalse_ForNormalApps()
    {
        var list = new ExclusionList();

        Assert.False(list.IsExcluded("chrome"));
        Assert.False(list.IsExcluded("code"));
        Assert.False(list.IsExcluded("explorer"));
    }

    [Fact]
    public void IsExcluded_ReturnsFalse_ForEmptyOrNull()
    {
        var list = new ExclusionList();

        Assert.False(list.IsExcluded(""));
        Assert.False(list.IsExcluded("  "));
    }

    [Fact]
    public void Add_IncludesNewApp()
    {
        var list = new ExclusionList();
        list.Add("my_secret_app");

        Assert.True(list.IsExcluded("my_secret_app"));
    }

    [Fact]
    public void Add_IgnoresEmpty()
    {
        var list = new ExclusionList();
        var countBefore = list.GetAll().Count;

        list.Add("");
        list.Add("  ");

        Assert.Equal(countBefore, list.GetAll().Count);
    }

    [Fact]
    public void Remove_RemovesApp()
    {
        var list = new ExclusionList();
        list.Add("temp_app");

        Assert.True(list.Remove("temp_app"));
        Assert.False(list.IsExcluded("temp_app"));
    }

    [Fact]
    public void Remove_ReturnsFalse_ForNonExistent()
    {
        var list = new ExclusionList();
        Assert.False(list.Remove("nonexistent_app_xyz"));
    }

    [Fact]
    public void LoadFromConfig_MergesWithDefaults()
    {
        var list = new ExclusionList();
        list.LoadFromConfig(new[] { "custom_app_1", "custom_app_2" });

        // Defaults still present
        Assert.True(list.IsExcluded("1password"));
        // Custom apps added
        Assert.True(list.IsExcluded("custom_app_1"));
        Assert.True(list.IsExcluded("custom_app_2"));
    }

    [Fact]
    public void GetAll_ReturnsAllExclusions()
    {
        var list = new ExclusionList();
        var all = list.GetAll();

        Assert.Contains("1password", all);
        Assert.Contains("bitwarden", all);
        Assert.True(all.Count >= 15); // At least 15 default exclusions
    }
}
