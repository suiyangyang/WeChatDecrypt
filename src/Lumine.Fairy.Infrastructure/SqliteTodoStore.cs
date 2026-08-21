using System.Text.Json;
using Microsoft.Data.Sqlite;
using Lumine.Fairy.Domain;
using Lumine.Fairy.Protocol;

namespace Lumine.Fairy.Infrastructure;

public sealed class SqliteTodoStore : ITodoStore, IDisposable
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public SqliteTodoStore(string? databasePath = null)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lumine.Fairy");
        Directory.CreateDirectory(root);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath ?? Path.Combine(root, "todos.db"),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false,
        }.ToString();
        Initialize();
    }

    private void Initialize()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS todos (
                id TEXT PRIMARY KEY,
                title TEXT NOT NULL,
                summary TEXT NOT NULL,
                priority TEXT NOT NULL,
                due_at TEXT NULL,
                assignee TEXT NULL,
                confidence REAL NOT NULL,
                source_refs_json TEXT NOT NULL,
                status TEXT NOT NULL,
                created_at TEXT NOT NULL,
                confirmed_at TEXT NULL,
                completed_at TEXT NULL,
                source TEXT NOT NULL,
                conversation_id TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_todos_status_created_at ON todos(status, created_at DESC);
            CREATE INDEX IF NOT EXISTS ix_todos_conversation ON todos(conversation_id);
            """;
        command.ExecuteNonQuery();
    }

    public async Task<TodoRecord?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM todos WHERE id = $id";
            command.Parameters.AddWithValue("$id", id);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<TodoRecord?> AddCandidateAsync(TodoRecord candidate, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var exists = connection.CreateCommand();
            exists.CommandText = "SELECT 1 FROM todos WHERE id = $id LIMIT 1";
            exists.Parameters.AddWithValue("$id", candidate.Id);
            if (await exists.ExecuteScalarAsync(cancellationToken) is not null) return null;

            using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO todos
                (id,title,summary,priority,due_at,assignee,confidence,source_refs_json,status,
                 created_at,confirmed_at,completed_at,source,conversation_id)
                VALUES ($id,$title,$summary,$priority,$due,$assignee,$confidence,$refs,$status,
                        $created,$confirmed,$completed,$source,$conversation);
                """;
            AddParameters(insert, candidate);
            await insert.ExecuteNonQueryAsync(cancellationToken);
            return candidate;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<TodoRecord>> ListAsync(bool includeRejected = false, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            using var command = connection.CreateCommand();
            command.CommandText = includeRejected
                ? "SELECT * FROM todos ORDER BY created_at DESC"
                : "SELECT * FROM todos WHERE status IN ('pending', 'in_progress', 'completed') ORDER BY created_at DESC";
            var result = new List<TodoRecord>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) result.Add(Read(reader));
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<TodoRecord?> UpdateStatusAsync(string id, string status, CancellationToken cancellationToken = default)
    {
        if (status is not ("pending_confirmation" or "pending" or "in_progress" or "completed" or "rejected"))
            throw new ArgumentOutOfRangeException(nameof(status));
        var allowedFrom = status switch
        {
            "pending" or "rejected" => "'pending_confirmation'",
            "in_progress" => "'pending'",
            "completed" => "'pending', 'in_progress'",
            _ => "'pending_confirmation'",
        };

        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            var now = DateTimeOffset.UtcNow.ToString("O");
            using var update = connection.CreateCommand();
            update.CommandText = """
                UPDATE todos
                SET status = $status,
                    confirmed_at = CASE WHEN $status = 'pending' AND confirmed_at IS NULL THEN $now ELSE confirmed_at END,
                    completed_at = CASE WHEN $status = 'completed' THEN $now ELSE completed_at END
                WHERE id = $id AND status IN (__allowed_from__);
                """;
            update.CommandText = update.CommandText.Replace("__allowed_from__", allowedFrom, StringComparison.Ordinal);
            update.Parameters.AddWithValue("$status", status);
            update.Parameters.AddWithValue("$now", now);
            update.Parameters.AddWithValue("$id", id);
            if (await update.ExecuteNonQueryAsync(cancellationToken) == 0) return null;

            using var select = connection.CreateCommand();
            select.CommandText = "SELECT * FROM todos WHERE id = $id";
            select.Parameters.AddWithValue("$id", id);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void AddParameters(SqliteCommand command, TodoRecord todo)
    {
        command.Parameters.AddWithValue("$id", todo.Id);
        command.Parameters.AddWithValue("$title", todo.Title);
        command.Parameters.AddWithValue("$summary", todo.Summary);
        command.Parameters.AddWithValue("$priority", todo.Priority);
        command.Parameters.AddWithValue("$due", todo.DueAt?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$assignee", todo.Assignee ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$confidence", todo.Confidence);
        command.Parameters.AddWithValue("$refs", JsonSerializer.Serialize(todo.SourceRefs, _json));
        command.Parameters.AddWithValue("$status", todo.Status);
        command.Parameters.AddWithValue("$created", todo.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$confirmed", todo.ConfirmedAt?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$completed", todo.CompletedAt?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$source", todo.Source);
        command.Parameters.AddWithValue("$conversation", todo.ConversationId);
    }

    private TodoRecord Read(SqliteDataReader reader)
    {
        var due = ReadDateTimeOffset(reader, "due_at");
        var confirmed = ReadDateTimeOffset(reader, "confirmed_at");
        var completed = ReadDateTimeOffset(reader, "completed_at");
        var refs = JsonSerializer.Deserialize<IReadOnlyList<string>>(
                       reader.GetString(reader.GetOrdinal("source_refs_json")), _json)
                   ?? Array.Empty<string>();
        return new TodoRecord(
            reader.GetString(reader.GetOrdinal("id")),
            reader.GetString(reader.GetOrdinal("title")),
            reader.GetString(reader.GetOrdinal("summary")),
            reader.GetString(reader.GetOrdinal("priority")),
            due,
            reader.IsDBNull(reader.GetOrdinal("assignee")) ? null : reader.GetString(reader.GetOrdinal("assignee")),
            reader.GetDouble(reader.GetOrdinal("confidence")),
            refs,
            reader.GetString(reader.GetOrdinal("status")),
            DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("created_at"))),
            confirmed,
            completed,
            reader.GetString(reader.GetOrdinal("source")),
            reader.GetString(reader.GetOrdinal("conversation_id")));
    }

    private static DateTimeOffset? ReadDateTimeOffset(SqliteDataReader reader, string column)
        => reader.IsDBNull(reader.GetOrdinal(column))
            ? null
            : DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal(column)));

    public void Dispose() => _gate.Dispose();
}
