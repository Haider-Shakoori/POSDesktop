using BusinessOS.POS.Application.Abstractions.Storage;

namespace BusinessOS.POS.Infrastructure;

public sealed class ApplicationPaths : IApplicationPaths
{
    public ApplicationPaths()
    {
        RootPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BusinessOS",
            "POS");

        DatabasePath = Path.Combine(RootPath, "businessos-pos.db");
    }

    public string RootPath { get; }

    public string DatabasePath { get; }

    public void EnsureCreated() => Directory.CreateDirectory(RootPath);
}
