using Lumine.Fairy.ContactManagement.Contracts;
using Lumine.Fairy.ContactManagement.Core.Models;
using Lumine.Fairy.ContactManagement.Core.Services;
using Lumine.Fairy.ContactManagement.Infrastructure.Options;
using Lumine.Fairy.ContactManagement.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lumine.Fairy.ContactManagement.Infrastructure.Persistence;

public sealed class ContactManagementJsonStore : IContactManagementStore
{
    private const string ContactsFileName = "contacts.json";
    private const string CompaniesFileName = "companies.json";
    private const string ProjectsFileName = "projects.json";
    private const string RelationshipLinksFileName = "relationship-links.json";
    private const string TodoEntriesFileName = "todo-entries.json";
    private const string ClientIssuesFileName = "client-issues.json";
    private const string DailyReportsFileName = "daily-reports.json";
    private const string ScratchNoteDocumentsFileName = "scratch-note-documents.json";

    private readonly ContactManagementStorageOptions _options;
    private readonly ContactManagementValidationService _validationService;
    private readonly JsonSerializerOptions _serializerOptions;

    public ContactManagementJsonStore(ContactManagementStorageOptions options, ContactManagementValidationService? validationService = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _validationService = validationService ?? new ContactManagementValidationService();
        _serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };
    }

    public async Task<ContactManagementSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        var dataSet = new ContactManagementDataSet
        {
            Contacts = await LoadCollectionAsync<ContactProfile>(ContactsFileName, cancellationToken),
            Companies = await LoadCollectionAsync<CompanyProfile>(CompaniesFileName, cancellationToken),
            Projects = await LoadCollectionAsync<ProjectProfile>(ProjectsFileName, cancellationToken),
            RelationshipLinks = await LoadCollectionAsync<RelationshipLink>(RelationshipLinksFileName, cancellationToken),
            TodoEntries = await LoadCollectionAsync<TodoEntry>(TodoEntriesFileName, cancellationToken),
            ClientIssues = await LoadCollectionAsync<ClientIssueEntry>(ClientIssuesFileName, cancellationToken),
            DailyReports = await LoadCollectionAsync<DailyReport>(DailyReportsFileName, cancellationToken),
            ScratchNoteDocuments = await LoadCollectionAsync<ScratchNoteDocument>(ScratchNoteDocumentsFileName, cancellationToken)
        };

        _validationService.Validate(dataSet);
        return dataSet.ToSnapshot();
    }

    public async Task SaveAsync(ContactManagementSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        Directory.CreateDirectory(_options.BaseDirectoryPath);
        var dataSet = ContactManagementDataSet.FromSnapshot(snapshot);
        _validationService.Validate(dataSet);

        await SaveCollectionAsync(ContactsFileName, snapshot.Contacts, cancellationToken);
        await SaveCollectionAsync(CompaniesFileName, snapshot.Companies, cancellationToken);
        await SaveCollectionAsync(ProjectsFileName, snapshot.Projects, cancellationToken);
        await SaveCollectionAsync(RelationshipLinksFileName, snapshot.RelationshipLinks, cancellationToken);
        await SaveCollectionAsync(TodoEntriesFileName, snapshot.TodoEntries, cancellationToken);
        await SaveCollectionAsync(ClientIssuesFileName, snapshot.ClientIssues, cancellationToken);
        await SaveCollectionAsync(DailyReportsFileName, snapshot.DailyReports, cancellationToken);
        await SaveCollectionAsync(ScratchNoteDocumentsFileName, snapshot.ScratchNoteDocuments, cancellationToken);
    }

    private async Task<List<TModel>> LoadCollectionAsync<TModel>(string fileName, CancellationToken cancellationToken)
    {
        var filePath = GetFilePath(fileName);
        if (!File.Exists(filePath))
        {
            return [];
        }

        await using var stream = File.OpenRead(filePath);
        var payload = await JsonSerializer.DeserializeAsync<StoredCollection<TModel>>(stream, _serializerOptions, cancellationToken);

        if (payload?.SchemaVersion is > 0 && payload.SchemaVersion != _options.SchemaVersion)
        {
            throw new InvalidOperationException($"文件 {fileName} 的 schemaVersion={payload.SchemaVersion}，当前仅支持 {_options.SchemaVersion}。");
        }

        return payload?.Items ?? [];
    }

    private async Task SaveCollectionAsync<TModel>(string fileName, IReadOnlyList<TModel> items, CancellationToken cancellationToken)
    {
        var filePath = GetFilePath(fileName);
        var tempFilePath = filePath + ".tmp";
        var payload = new StoredCollection<TModel>
        {
            SchemaVersion = _options.SchemaVersion,
            Items = [.. items]
        };

        await using (var stream = File.Create(tempFilePath))
        {
            await JsonSerializer.SerializeAsync(stream, payload, _serializerOptions, cancellationToken);
        }

        File.Move(tempFilePath, filePath, true);
    }

    private string GetFilePath(string fileName) => Path.Combine(_options.BaseDirectoryPath, fileName);

    private sealed class StoredCollection<TModel>
    {
        public int SchemaVersion { get; set; }

        public List<TModel> Items { get; set; } = [];
    }
}
