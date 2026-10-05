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
        NetworkConfigurationPath = Path.Combine(RootPath, "network.json");
        NetworkSecretsPath = Path.Combine(RootPath, "network-secrets.dat");
        ServerCertificatePath = Path.Combine(RootPath, "pos-local-server.pfx");
        LanTerminalsPath = Path.Combine(RootPath, "lan-terminals.json");
        BackupsDirectory = Path.Combine(RootPath, "backups");
        UpdatesDirectory = Path.Combine(RootPath, "updates");
    }

    public string RootPath { get; }

    public string DatabasePath { get; }
    public string NetworkConfigurationPath { get; }
    public string NetworkSecretsPath { get; }
    public string ServerCertificatePath { get; }
    public string LanTerminalsPath { get; }
    public string BackupsDirectory { get; }
    public string UpdatesDirectory { get; }

    public void EnsureCreated() => Directory.CreateDirectory(RootPath);
}
