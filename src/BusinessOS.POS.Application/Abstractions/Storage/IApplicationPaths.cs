namespace BusinessOS.POS.Application.Abstractions.Storage;

public interface IApplicationPaths
{
    string RootPath { get; }
    string DatabasePath { get; }

    // Network paths default beneath the application root so existing test and
    // alternate path implementations remain source-compatible. Production
    // implementations may still override these locations explicitly.
    string NetworkConfigurationPath => Path.Combine(RootPath, "network.json");
    string NetworkSecretsPath => Path.Combine(RootPath, "network-secrets.dat");
    string ServerCertificatePath => Path.Combine(RootPath, "local-server.pfx");
    string LanTerminalsPath => Path.Combine(RootPath, "lan-terminals.json");
    string BackupsDirectory => Path.Combine(RootPath, "backups");
    string UpdatesDirectory => Path.Combine(RootPath, "updates");

    void EnsureCreated();
}
