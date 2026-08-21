namespace Lumine.Fairy.ContactManagement.Infrastructure.Persistence;

public static class ContactManagementImportTemplateWriter
{
    public static Task WriteExampleTemplateAsync(string filePath, int schemaVersion, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var extension = Path.GetExtension(filePath);
        if (string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return Task.Run(() => ExcelContactManagementWorkbookMapper.WriteSnapshot(filePath, ContactManagementSampleSnapshotFactory.Create()), cancellationToken);
        }

        return ContactManagementJsonTemplateWriter.WriteExampleTemplateAsync(filePath, schemaVersion, cancellationToken);
    }
}
