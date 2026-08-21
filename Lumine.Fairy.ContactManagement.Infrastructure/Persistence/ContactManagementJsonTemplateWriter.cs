using Lumine.Fairy.ContactManagement.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lumine.Fairy.ContactManagement.Infrastructure.Persistence;

public static class ContactManagementJsonTemplateWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task WriteExampleTemplateAsync(string filePath, int schemaVersion, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var snapshot = ContactManagementSampleSnapshotFactory.Create();
        var document = ContactManagementJsonExchangeDocument.FromSnapshot(snapshot, schemaVersion);

        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken);
    }
}
