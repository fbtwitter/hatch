using Hatch.Helpers;
using Windows.Management.Deployment;

namespace Hatch.Services;

public enum UpdateCheckStatus
{
    Available,
    UpToDate,
    ManagedBySource,
    Failed
}

public sealed record UpdateCheckResult(UpdateCheckStatus Status, Uri? AppInstallerUri = null);

public sealed class UpdateService
{
    private static readonly Uri ReleaseFeedUri = new("https://fbtwitter.github.io/hatch/Hatch.appinstaller");
    private static readonly HttpClient UpdateClient = new() { Timeout = TimeSpan.FromSeconds(15) };

    public async Task<UpdateCheckResult> CheckForUpdatesAsync()
    {
        try
        {
            Windows.ApplicationModel.Package current;
            try
            {
                current = Windows.ApplicationModel.Package.Current;
            }
            catch
            {
                return new(UpdateCheckStatus.ManagedBySource);
            }

            var appInstallerUri = current.GetAppInstallerInfo()?.Uri;
            if (appInstallerUri is null)
            {
                if (current.SignatureKind == Windows.ApplicationModel.PackageSignatureKind.Store)
                    return new(UpdateCheckStatus.ManagedBySource);

                using var stream = await UpdateClient.GetStreamAsync(ReleaseFeedUri);
                var latest = AppInstallerFeed.ReadVersion(stream, current.Id.Name, current.Id.Publisher);
                var installed = current.Id.Version;
                var installedVersion = new Version(installed.Major, installed.Minor, installed.Build, installed.Revision);

                return latest > installedVersion
                    ? new(UpdateCheckStatus.Available, ReleaseFeedUri)
                    : new(UpdateCheckStatus.UpToDate);
            }

            var package = new PackageManager().FindPackageForUser(string.Empty, current.Id.FullName);
            var result = await package.CheckUpdateAvailabilityAsync();
            return result.Availability switch
            {
                Windows.ApplicationModel.PackageUpdateAvailability.Available or
                    Windows.ApplicationModel.PackageUpdateAvailability.Required =>
                    new(UpdateCheckStatus.Available, appInstallerUri),
                Windows.ApplicationModel.PackageUpdateAvailability.NoUpdates =>
                    new(UpdateCheckStatus.UpToDate),
                Windows.ApplicationModel.PackageUpdateAvailability.Unknown =>
                    new(UpdateCheckStatus.ManagedBySource),
                _ => new(UpdateCheckStatus.Failed)
            };
        }
        catch
        {
            return new(UpdateCheckStatus.Failed);
        }
    }

    public async Task InstallUpdateAsync(Uri appInstallerUri)
    {
        if (Hatch.NativeMethods.RegisterApplicationRestart(null, 0) != 0)
            throw new InvalidOperationException("Windows could not register Hatch for restart.");

        await new PackageManager().AddPackageByAppInstallerFileAsync(
            appInstallerUri,
            AddPackageByAppInstallerOptions.ForceTargetAppShutdown,
            null);
    }
}
