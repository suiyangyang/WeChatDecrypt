using Lumine.Fairy.ContactManagement.Infrastructure.Options;
using Lumine.Fairy.ContactManagement.Infrastructure.Persistence;
using Xunit;

namespace Lumine.Fairy.ContactManagement.Tests;

public sealed class DirectoryCompanyImportServiceTests
{
    [Fact]
    public async Task ImportAsync_AddsFirstLevelDirectoriesIdempotently()
    {
        var tempRoot = Directory.CreateTempSubdirectory("lumine-company-import-");
        try
        {
            var sourceDirectory = Directory.CreateDirectory(Path.Combine(tempRoot.FullName, "source"));
            Directory.CreateDirectory(Path.Combine(sourceDirectory.FullName, "客户甲"));
            Directory.CreateDirectory(Path.Combine(sourceDirectory.FullName, "客户乙"));
            File.WriteAllText(Path.Combine(sourceDirectory.FullName, "ignore.txt"), "not a directory");

            var result = await RunImportAsync(tempRoot.FullName, sourceDirectory.FullName);

            Assert.Equal(2, result.Result.First.AddedCount);
            Assert.Equal(0, result.Result.First.ExistingCount);
            Assert.Equal(0, result.Result.Second.AddedCount);
            Assert.Equal(2, result.Result.Second.ExistingCount);
            Assert.Equal(["客户甲", "客户乙"], result.Names);
        }
        finally
        {
            try
            {
                tempRoot.Delete(recursive: true);
            }
            catch (IOException)
            {
                // SQLite native handles can outlive the managed store for a short period in the test host.
            }
        }
    }

    private static async Task<((DirectoryCompanyImportResult First, DirectoryCompanyImportResult Second) Result, string[] Names)> RunImportAsync(
        string tempRoot,
        string sourceDirectory)
    {
        var options = new ContactManagementStorageOptions
        {
            BaseDirectoryPath = Path.Combine(tempRoot, "database")
        };
        var store = new ContactManagementSqliteStore(options);
        var importer = new DirectoryCompanyImportService(store);
        var first = await importer.ImportAsync(sourceDirectory);
        var second = await importer.ImportAsync(sourceDirectory);
        var snapshot = await store.LoadAsync();
        return ((first, second), snapshot.Companies.Select(company => company.Name).OrderBy(name => name).ToArray());
    }
}
