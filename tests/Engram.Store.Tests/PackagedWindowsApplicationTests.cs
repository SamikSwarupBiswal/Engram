using Engram.Store.Automation.Applications;

namespace Engram.Store.Tests;

public sealed class PackagedWindowsApplicationTests
{
    private const string AppId = "Contoso.Reader_8wekyb3d8bbwe!Reader.Desktop2";

    [Fact]
    public void CanonicalShellEntryBecomesLaunchableWithoutAnExecutableGuess()
    {
        var application = WindowsApplicationDiscovery.CreatePackagedApplication("Reader", AppId, AppId);

        Assert.NotNull(application);
        Assert.True(application.IsLaunchable);
        Assert.Null(application.ExecutablePath);
        Assert.Null(application.ProcessName);
        Assert.Contains(ApplicationDiscoverySource.MicrosoftStore, application.Sources);
        Assert.Contains(AppId, application.Aliases);
        Assert.Equal(@"shell:AppsFolder\" + AppId, application.LaunchTarget);
        Assert.True(PackagedWindowsApplication.TryGetId(application.LaunchTarget, out var id));
        Assert.Equal(AppId, id);
    }

    [Theory]
    [InlineData("Contoso.Reader_8wekyb3d8bbwe!Other", AppId)]
    [InlineData(@"C:\Program Files\Reader.exe", AppId)]
    [InlineData(AppId, null)]
    public void ShellEntryRequiresMatchingIdentityAndPath(string path, string? appUserModelId)
    {
        Assert.Null(WindowsApplicationDiscovery.CreatePackagedApplication("Reader", path, appUserModelId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(AppId)]
    [InlineData(@"shell:AppsFolder\Contoso.Reader_8wekyb3d8bbwe!Reader /new")]
    [InlineData(@"shell:AppsFolder\Contoso.Reader_8wekyb3d8bbwe!Reader\extra")]
    [InlineData(@"shell:AppsFolder\https://contoso.example")]
    [InlineData(@"shell:AppsFolder\Contoso.Reader_8wekyb3d8bbwe!Reader..Desktop")]
    [InlineData(@"shell:AppsFolder\Contoso.Reader_8wekyb3d8bbwe!Reader!Other")]
    [InlineData("shell:AppsFolder\\Contoso.Reader_8wekyb3d8bbwe!Reader\n")]
    public void LaunchTargetCannotCarryAnUnqualifiedIdOrOtherInput(string? launchTarget)
    {
        Assert.False(PackagedWindowsApplication.TryGetId(launchTarget, out var id));
        Assert.Empty(id);
    }
}
