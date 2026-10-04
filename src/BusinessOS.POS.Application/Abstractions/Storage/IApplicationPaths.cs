namespace BusinessOS.POS.Application.Abstractions.Storage;

public interface IApplicationPaths
{
    string RootPath { get; }
    string DatabasePath { get; }
    string NetworkConfigurationPath { get; }
    string NetworkSecretsPath { get; }
    string ServerCertificatePath { get; }
    string LanTerminalsPath { get; }
    void EnsureCreated();
}
