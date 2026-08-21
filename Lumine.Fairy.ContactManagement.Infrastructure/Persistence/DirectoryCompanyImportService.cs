using Lumine.Fairy.ContactManagement.Contracts;
using Lumine.Fairy.ContactManagement.Models;

namespace Lumine.Fairy.ContactManagement.Infrastructure.Persistence;

public sealed record DirectoryCompanyImportResult(
    int AddedCount,
    int ExistingCount,
    IReadOnlyList<string> AddedNames,
    IReadOnlyList<string> ExistingNames);

/// <summary>
/// Imports first-level directory names as companies without replacing existing contact-domain data.
/// </summary>
public sealed class DirectoryCompanyImportService(IContactManagementStore store)
{
    public async Task<DirectoryCompanyImportResult> ImportAsync(
        string directoryPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        if (!Directory.Exists(directoryPath))
        {
            throw new DirectoryNotFoundException($"客户目录不存在：{directoryPath}");
        }

        var directoryNames = new DirectoryInfo(directoryPath)
            .EnumerateDirectories()
            .Select(directory => directory.Name.Trim())
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var snapshot = await store.LoadAsync(cancellationToken);
        var existingNames = snapshot.Companies
            .Where(company => !string.IsNullOrWhiteSpace(company.Name))
            .Select(company => company.Name.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var companies = snapshot.Companies.ToList();
        var addedNames = new List<string>();
        var existingDirectoryNames = new List<string>();

        foreach (var name in directoryNames)
        {
            if (!existingNames.Add(name))
            {
                existingDirectoryNames.Add(name);
                continue;
            }

            companies.Add(new CompanyProfile
            {
                Name = name,
                IsActive = true
            });
            addedNames.Add(name);
        }

        if (addedNames.Count > 0)
        {
            await store.SaveAsync(new ContactManagementSnapshot
            {
                Contacts = snapshot.Contacts,
                Companies = companies,
                Projects = snapshot.Projects,
                RelationshipLinks = snapshot.RelationshipLinks,
                TodoEntries = snapshot.TodoEntries,
                ClientIssues = snapshot.ClientIssues,
                DailyReports = snapshot.DailyReports,
                ScratchNoteDocuments = snapshot.ScratchNoteDocuments
            }, cancellationToken);
        }

        return new DirectoryCompanyImportResult(
            addedNames.Count,
            existingDirectoryNames.Count,
            addedNames,
            existingDirectoryNames);
    }
}
