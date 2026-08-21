using Lumine.Fairy.ContactManagement.Infrastructure.Options;
using Lumine.Fairy.ContactManagement.Contracts;
using Lumine.Fairy.ContactManagement.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lumine.Fairy.ContactManagement.App.ImportExport;

public sealed class ContactDomainDataExportService : IDataExportService
{
    private readonly IContactManagementStore _store;
    private readonly ContactManagementStorageOptions _options;
    private readonly JsonSerializerOptions _jsonOptions;

    public ContactDomainDataExportService(IContactManagementStore store, ContactManagementStorageOptions options)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };
    }

    public async Task ExportJsonAsync(ExportRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.TargetKind == ExportTargetKind.Excel)
        {
            await ExportExcelAsync(request, cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(request.FilePath))
        {
            throw new InvalidOperationException("导出文件路径不能为空。");
        }

        var snapshot = ContactDomainSnapshotFilter.ToContactDomainSnapshot(await _store.LoadAsync(cancellationToken));
        var document = ContactDomainJsonExchangeDocument.FromSnapshot(snapshot, _options.SchemaVersion);
        var directory = Path.GetDirectoryName(request.FilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(request.FilePath);
        await JsonSerializer.SerializeAsync(stream, document, _jsonOptions, cancellationToken);
    }

    public async Task ExportExcelAsync(ExportRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.FilePath))
        {
            throw new InvalidOperationException("导出文件路径不能为空。");
        }

        var snapshot = ContactDomainSnapshotFilter.ToContactDomainSnapshot(await _store.LoadAsync(cancellationToken));
        await Task.Run(() => ContactDomainExcelWorkbookMapper.WriteSnapshot(request.FilePath, snapshot), cancellationToken);
    }
}
