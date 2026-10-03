namespace BusinessOS.POS.Infrastructure;

public sealed record DesktopRuntimeProfile(
    bool OfflineFirst = true,
    bool LanReady = true,
    bool LocalDatabaseRequired = true);
