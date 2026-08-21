using System.Text.Json;
using Microsoft.Data.Sqlite;
using QwenPawRemotePc.Domain;
using QwenPawRemotePc.Protocol;

namespace QwenPawRemotePc.Infrastructure;

public sealed class SqliteNotificationStore : INotificationStore, IDisposable
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public SqliteNotificationStore(string? databasePath = null)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QwenPawRemotePc");
        Directory.CreateDirectory(root);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath ?? Path.Combine(root, "notifications.db"),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString();
        Initialize();
    }

    private void Initialize()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS notifications (
                id TEXT PRIMARY KEY,
                title TEXT NOT NULL,
                body TEXT NOT NULL,
                source TEXT NOT NULL,
                category TEXT NOT NULL,
                severity TEXT NOT NULL,
                dismiss_policy TEXT NOT NULL,
                duration_seconds INTEGER NOT NULL,
                notification_type TEXT NOT NULL,
                created_at TEXT NOT NULL,
                is_read INTEGER NOT NULL,
                is_resolved INTEGER NOT NULL,
                deduplication_key TEXT NULL,
                actions_json TEXT NOT NULL,
                question_json TEXT NULL,
                related_task_id TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_notifications_created_at ON notifications(created_at DESC);
            CREATE INDEX IF NOT EXISTS ix_notifications_source ON notifications(source);
            CREATE INDEX IF NOT EXISTS ix_notifications_dedup ON notifications(deduplication_key);
            """;
        command.ExecuteNonQuery();
    }

    public async Task<NotificationRecord> AddAsync(NotificationRequest request, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(request.DeduplicationKey))
            {
                using var duplicate = connection.CreateCommand();
                duplicate.CommandText = "SELECT * FROM notifications WHERE deduplication_key = $key ORDER BY created_at DESC LIMIT 1";
                duplicate.Parameters.AddWithValue("$key", request.DeduplicationKey);
                await using var reader = await duplicate.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                    return Read(reader);
            }

            var record = new NotificationRecord(
                Guid.NewGuid().ToString("N"),
                request.Title.Trim(),
                request.Body.Trim(),
                string.IsNullOrWhiteSpace(request.Source) ? "local" : request.Source.Trim(),
                string.IsNullOrWhiteSpace(request.Category) ? "other" : request.Category.Trim(),
                string.IsNullOrWhiteSpace(request.Severity) ? "info" : request.Severity.Trim(),
                string.IsNullOrWhiteSpace(request.DismissPolicy) ? "timeout" : request.DismissPolicy.Trim(),
                Math.Clamp(request.DurationSeconds ?? 5, 1, 86400),
                string.IsNullOrWhiteSpace(request.NotificationType) ? "normal" : request.NotificationType.Trim(),
                DateTimeOffset.UtcNow,
                false,
                false,
                request.DeduplicationKey,
                request.Actions ?? Array.Empty<NotificationAction>(),
                request.Question,
                null);

            using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO notifications
                (id,title,body,source,category,severity,dismiss_policy,duration_seconds,notification_type,
                 created_at,is_read,is_resolved,deduplication_key,actions_json,question_json,related_task_id)
                VALUES ($id,$title,$body,$source,$category,$severity,$dismiss,$duration,$type,
                        $created,$read,$resolved,$dedup,$actions,$question,$task);
                """;
            insert.Parameters.AddWithValue("$id", record.Id);
            insert.Parameters.AddWithValue("$title", record.Title);
            insert.Parameters.AddWithValue("$body", record.Body);
            insert.Parameters.AddWithValue("$source", record.Source);
            insert.Parameters.AddWithValue("$category", record.Category);
            insert.Parameters.AddWithValue("$severity", record.Severity);
            insert.Parameters.AddWithValue("$dismiss", record.DismissPolicy);
            insert.Parameters.AddWithValue("$duration", record.DurationSeconds);
            insert.Parameters.AddWithValue("$type", record.NotificationType);
            insert.Parameters.AddWithValue("$created", record.CreatedAt.ToString("O"));
            insert.Parameters.AddWithValue("$read", record.IsRead ? 1 : 0);
            insert.Parameters.AddWithValue("$resolved", record.IsResolved ? 1 : 0);
            insert.Parameters.AddWithValue("$dedup", (object?)record.DeduplicationKey ?? DBNull.Value);
            insert.Parameters.AddWithValue("$actions", JsonSerializer.Serialize(record.Actions, _json));
            insert.Parameters.AddWithValue("$question", record.Question is null ? DBNull.Value : JsonSerializer.Serialize(record.Question, _json));
            insert.Parameters.AddWithValue("$task", DBNull.Value);
            await insert.ExecuteNonQueryAsync(cancellationToken);
            return record;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<NotificationRecord>> ListAsync(string? keyword = null, bool unreadOnly = false, CancellationToken cancellationToken = default)
        => await ListPageAsync(keyword, unreadOnly, false, 0, 200, cancellationToken);

    public async Task<IReadOnlyList<NotificationRecord>> ListPageAsync(string? keyword, bool unreadOnly, bool readOnly, int offset, int limit, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT * FROM notifications
                WHERE ($keyword = '' OR title LIKE $pattern OR body LIKE $pattern OR source LIKE $pattern
                       OR category LIKE $pattern OR severity LIKE $pattern OR notification_type LIKE $pattern
                       OR deduplication_key LIKE $pattern)
                  AND ($unread = 0 OR is_read = 0)
                  AND ($read = 0 OR is_read = 1)
                ORDER BY created_at DESC LIMIT $limit OFFSET $offset;
                """;
            command.Parameters.AddWithValue("$keyword", keyword?.Trim() ?? string.Empty);
            command.Parameters.AddWithValue("$pattern", $"%{keyword?.Trim() ?? string.Empty}%");
            command.Parameters.AddWithValue("$unread", unreadOnly ? 1 : 0);
            command.Parameters.AddWithValue("$read", readOnly ? 1 : 0);
            command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 100));
            command.Parameters.AddWithValue("$offset", Math.Max(offset, 0));
            var result = new List<NotificationRecord>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) result.Add(Read(reader));
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<NotificationRecord?> GetAsync(string id, CancellationToken cancellationToken = default)
        => GetByIdAsync(id, cancellationToken);

    private async Task<NotificationRecord?> GetByIdAsync(string id, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM notifications WHERE id = $id";
            command.Parameters.AddWithValue("$id", id);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
        }
        finally { _gate.Release(); }
    }

    public Task<bool> MarkReadAsync(string id, CancellationToken cancellationToken = default)
        => UpdateFlagAsync(id, "is_read", cancellationToken);

    public async Task<int> MarkAllReadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE notifications SET is_read = 1 WHERE is_read = 0";
            return await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public Task<bool> ResolveAsync(string id, CancellationToken cancellationToken = default)
        => UpdateFlagAsync(id, "is_resolved", cancellationToken);

    private async Task<bool> UpdateFlagAsync(string id, string column, CancellationToken cancellationToken)
    {
        if (column is not ("is_read" or "is_resolved")) throw new ArgumentOutOfRangeException(nameof(column));
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            using var command = connection.CreateCommand();
            command.CommandText = $"UPDATE notifications SET {column} = 1 WHERE id = $id";
            command.Parameters.AddWithValue("$id", id);
            return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
        }
        finally { _gate.Release(); }
    }

    private NotificationRecord Read(SqliteDataReader reader)
    {
        var actions = JsonSerializer.Deserialize<IReadOnlyList<NotificationAction>>(reader.GetString(reader.GetOrdinal("actions_json")), _json)
                      ?? Array.Empty<NotificationAction>();
        var questionValue = reader.IsDBNull(reader.GetOrdinal("question_json")) ? null : reader.GetString(reader.GetOrdinal("question_json"));
        var question = string.IsNullOrWhiteSpace(questionValue) ? null : JsonSerializer.Deserialize<QuestionPayload>(questionValue, _json);
        return new NotificationRecord(
            reader.GetString(reader.GetOrdinal("id")),
            reader.GetString(reader.GetOrdinal("title")),
            reader.GetString(reader.GetOrdinal("body")),
            reader.GetString(reader.GetOrdinal("source")),
            reader.GetString(reader.GetOrdinal("category")),
            reader.GetString(reader.GetOrdinal("severity")),
            reader.GetString(reader.GetOrdinal("dismiss_policy")),
            reader.GetInt32(reader.GetOrdinal("duration_seconds")),
            reader.GetString(reader.GetOrdinal("notification_type")),
            DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("created_at"))),
            reader.GetInt32(reader.GetOrdinal("is_read")) != 0,
            reader.GetInt32(reader.GetOrdinal("is_resolved")) != 0,
            reader.IsDBNull(reader.GetOrdinal("deduplication_key")) ? null : reader.GetString(reader.GetOrdinal("deduplication_key")),
            actions,
            question,
            reader.IsDBNull(reader.GetOrdinal("related_task_id")) ? null : reader.GetString(reader.GetOrdinal("related_task_id")));
    }

    public void Dispose() => _gate.Dispose();
}
