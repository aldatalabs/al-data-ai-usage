using System;
using System.Threading.Tasks;
using Windows.Services.Store;

namespace AlData.AiUsage.Ui;

/// <summary>Updates come from the Store, which installs them in the background by itself; this is
/// the check the user asks for from the menu. Only a package has a Store to ask.</summary>
internal static class Updates
{
    /// <summary>False when this is the latest version. Otherwise the Store's own dialog asks the
    /// user and, on yes, installs the update, which ends this process.</summary>
    public static async Task<bool> InstallAsync(IntPtr window)
    {
        var store = StoreContext.GetDefault();
        // A desktop app names the window that owns the Store's dialog.
        WinRT.Interop.InitializeWithWindow.Initialize(store, window);
        var updates = await store.GetAppAndOptionalStorePackageUpdatesAsync();
        if (updates.Count == 0)
            return false;
        // Windows starts a desktop app again after an update only when it asked for that.
        Native.RegisterApplicationRestart(null, Native.RestartNoCrash | Native.RestartNoHang | Native.RestartNoReboot);
        var result = await store.RequestDownloadAndInstallStorePackageUpdatesAsync(updates);
        if (result.OverallState is not (StorePackageUpdateState.Completed or StorePackageUpdateState.Canceled))
            throw new InvalidOperationException(result.OverallState.ToString());
        return true;
    }
}
