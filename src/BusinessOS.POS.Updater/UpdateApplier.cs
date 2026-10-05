using System.Diagnostics;
using System.Text.Json;
using BusinessOS.POS.Application.Abstractions.Updates;

namespace BusinessOS.POS.Updater;

internal static class UpdateApplier
{
    public static async Task<string> ApplyAsync(string planPath)
    {
        planPath = Path.GetFullPath(planPath);
        if (!File.Exists(planPath))
            throw new FileNotFoundException("Update plan was not found.", planPath);

        var plan = JsonSerializer.Deserialize<UpdateApplyPlan>(
            await File.ReadAllTextAsync(planPath),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Update plan is invalid.");

        if (plan.SchemaVersion != 1)
            throw new InvalidOperationException("Unsupported update plan schema.");

        var install = Path.GetFullPath(plan.InstallationDirectory);
        var dataRoot = Path.GetFullPath(plan.DataRootDirectory);
        var staging = Path.GetFullPath(plan.StagingDirectory);
        var rollback = Path.GetFullPath(plan.RollbackDirectory);

        if (IsSameOrChild(dataRoot, install) || IsSameOrChild(install, dataRoot))
            throw new InvalidOperationException("Installation and POS data directories must remain isolated.");
        if (!Directory.Exists(staging))
            throw new DirectoryNotFoundException("Staged update files are missing.");

        await WaitForProcessExitAsync(plan.WaitForProcessId, TimeSpan.FromMinutes(2));

        if (Directory.Exists(rollback))
            Directory.Delete(rollback, recursive: true);
        Directory.CreateDirectory(rollback);

        if (Directory.Exists(install))
            CopyDirectory(install, rollback);

        try
        {
            Directory.CreateDirectory(install);
            CopyDirectory(staging, install);

            if (!string.IsNullOrWhiteSpace(plan.RestartExecutable))
            {
                var restart = Path.GetFullPath(plan.RestartExecutable);
                if (!IsSameOrChild(restart, install) || !File.Exists(restart))
                    throw new InvalidOperationException("Updated application executable is missing.");

                _ = Process.Start(new ProcessStartInfo(restart) { UseShellExecute = true });
            }

            return $"BusinessOS POS {plan.TargetVersion} installed successfully.";
        }
        catch
        {
            if (Directory.Exists(rollback))
            {
                Directory.CreateDirectory(install);
                CopyDirectory(rollback, install);
            }

            throw;
        }
    }

    private static async Task WaitForProcessExitAsync(int processId, TimeSpan timeout)
    {
        if (processId <= 0) return;

        try
        {
            using var process = Process.GetProcessById(processId);
            using var timeoutCts = new CancellationTokenSource(timeout);
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (ArgumentException)
        {
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(destination, relative));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static bool IsSameOrChild(string path, string root)
    {
        path = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return string.Equals(path, root, StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
