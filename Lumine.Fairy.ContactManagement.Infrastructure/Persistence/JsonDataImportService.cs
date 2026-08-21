using Lumine.Fairy.ContactManagement.Contracts;
using Lumine.Fairy.ContactManagement.Core.Services;
using Lumine.Fairy.ContactManagement.Infrastructure.Options;
using Lumine.Fairy.ContactManagement.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lumine.Fairy.ContactManagement.Infrastructure.Persistence;

public sealed class JsonDataImportService : IDataImportService
{
    private readonly IContactManagementStore _store;
    private readonly ContactManagementStorageOptions _options;
    private readonly ContactManagementValidationService _validationService;
    private readonly JsonSerializerOptions _jsonOptions;

    public JsonDataImportService(
        IContactManagementStore store,
        ContactManagementStorageOptions options,
        ContactManagementValidationService? validationService = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _validationService = validationService ?? new ContactManagementValidationService();
        _jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };
    }

    public async Task<ImportPreviewResult> PreviewAsync(ImportRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.SourceKind == ImportSourceKind.Excel)
        {
            return await PreviewExcelAsync(request, cancellationToken);
        }

        if (request.SourceKind != ImportSourceKind.Json)
        {
            return ContactManagementImportAnalysis.BuildInvalidResult("当前导入源不受支持。", "import.source.not_supported", "当前版本无法识别导入源类型。");
        }

        if (string.IsNullOrWhiteSpace(request.FilePath) || !File.Exists(request.FilePath))
        {
            return new ImportPreviewResult
            {
                IsValid = false,
                Summary = "导入文件不存在。",
                Conflicts =
                [
                    new ImportConflictItem
                    {
                        Code = "import.file.not_found",
                        Message = "指定的 JSON 文件不存在。",
                        Severity = ImportConflictSeverity.Error,
                        EntityType = "Import",
                        RecommendedAction = ImportConflictResolutionAction.Skip
                    }
                ]
            };
        }

        try
        {
            await using var stream = File.OpenRead(request.FilePath);
            var document = await JsonSerializer.DeserializeAsync<ContactManagementJsonExchangeDocument>(stream, _jsonOptions, cancellationToken);
            if (document is null)
            {
                return ContactManagementImportAnalysis.BuildInvalidResult("无法读取导入文件内容。", "import.file.empty", "JSON 内容为空或格式无效。");
            }

            var conflicts = new List<ImportConflictItem>();
            if (document.SchemaVersion != _options.SchemaVersion)
            {
                conflicts.Add(new ImportConflictItem
                {
                    Code = "import.schema.mismatch",
                    Message = $"导入文件 schemaVersion={document.SchemaVersion}，当前系统 schemaVersion={_options.SchemaVersion}。",
                    Severity = ImportConflictSeverity.Error,
                    EntityType = "Import",
                    RecommendedAction = ImportConflictResolutionAction.Skip
                });
            }

            var previewSnapshot = document.ToSnapshot();
            var currentSnapshot = await _store.LoadAsync(cancellationToken);
            return ContactManagementImportAnalysis.BuildPreviewResult(currentSnapshot, previewSnapshot, _validationService, conflicts);
        }
        catch (JsonException ex)
        {
            return ContactManagementImportAnalysis.BuildInvalidResult("JSON 解析失败。", "import.json.invalid", ex.Message);
        }
    }

    public async Task<ImportCommitResult> CommitAsync(ImportCommitRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.PreviewResult.IsValid)
        {
            return new ImportCommitResult
            {
                Success = false,
                Summary = "导入预检未通过，未执行写入。"
            };
        }

        var currentSnapshot = await _store.LoadAsync(cancellationToken);
        var committedSnapshot = ContactManagementImportCommitter.BuildCommittedSnapshot(
            currentSnapshot,
            request.PreviewResult.PreviewSnapshot,
            request.ConflictResolutions);

        await _store.SaveAsync(committedSnapshot, cancellationToken);
        var latestSnapshot = await _store.LoadAsync(cancellationToken);
        var skippedCount = request.ConflictResolutions.Count(static item => item.Action == ImportConflictResolutionAction.Skip);
        return new ImportCommitResult
        {
            Success = true,
            Summary = skippedCount > 0
                ? $"导入已完成，按确认结果跳过了 {skippedCount} 个冲突项。"
                : "导入已成功写入 SQLite。",
            Statistics = _validationService.BuildStatistics(latestSnapshot)
        };
    }

    private async Task<ImportPreviewResult> PreviewExcelAsync(ImportRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.FilePath) || !File.Exists(request.FilePath))
        {
            return ContactManagementImportAnalysis.BuildInvalidResult("导入文件不存在。", "import.file.not_found", "指定的 Excel 文件不存在。");
        }

        try
        {
            List<ImportConflictItem> conflicts = [];
            var previewSnapshot = await Task.Run(() => ExcelContactManagementWorkbookMapper.ReadSnapshot(request.FilePath, out conflicts), cancellationToken);
            var currentSnapshot = await _store.LoadAsync(cancellationToken);
            return ContactManagementImportAnalysis.BuildPreviewResult(currentSnapshot, previewSnapshot, _validationService, conflicts);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            return ContactManagementImportAnalysis.BuildInvalidResult("Excel 解析失败。", "import.excel.invalid", ex.Message);
        }
        catch (Exception ex)
        {
            return ContactManagementImportAnalysis.BuildInvalidResult("Excel 解析失败。", "import.excel.unhandled", ex.Message);
        }
    }
}
