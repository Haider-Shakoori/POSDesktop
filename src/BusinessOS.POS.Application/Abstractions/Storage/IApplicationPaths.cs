namespace BusinessOS.POS.Application.Abstractions.Storage;

public interface IApplicationPaths
{
    string RootPath { get; }
    string DatabasePath { get; }
    void EnsureCreated();
}
