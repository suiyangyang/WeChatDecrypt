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
                conversation_id TEXT NOT NULL,
                company_id TEXT NULL,
                company_name TEXT NULL,
                conversation_display_name TEXT NULL,
                is_group_conversation INTEGER NOT NULL DEFAULT 0,
                board_id TEXT NOT NULL DEFAULT 'default',
                list_id TEXT NOT NULL DEFAULT 'todo',
                position INTEGER NOT NULL DEFAULT 0,
                is_archived INTEGER NOT NULL DEFAULT 0,
                labels_json TEXT NOT NULL DEFAULT '[]',
                checklist_json TEXT NOT NULL DEFAULT '[]',
                comments_json TEXT NOT NULL DEFAULT '[]',
                updated_at TEXT NULL
            );
            CREATE TABLE IF NOT EXISTS todo_boards (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                color TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS todo_lists (
                id TEXT PRIMARY KEY,
                board_id TEXT NOT NULL,
                name TEXT NOT NULL,
                position INTEGER NOT NULL,
                status TEXT NULL,
                is_archived INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS ix_todos_status_created_at ON todos(status, created_at DESC);
            CREATE INDEX IF NOT EXISTS ix_todos_conversation ON todos(conversation_id);
            """;
        command.ExecuteNonQuery();
        EnsureColumn(connection, "todos", "company_id", "TEXT NULL");
        EnsureColumn(connection, "todos", "company_name", "TEXT NULL");
        EnsureColumn(connection, "todos", "conversation_display_name", "TEXT NULL");
        EnsureColumn(connection, "todos", "is_group_conversation", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "todos", "board_id", "TEXT NOT NULL DEFAULT 'default'");
        EnsureColumn(connection, "todos", "list_id", "TEXT NOT NULL DEFAULT 'todo'");
        EnsureColumn(connection, "todos", "position", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "todos", "is_archived", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "todos", "labels_json", "TEXT NOT NULL DEFAULT '[]'");
        EnsureColumn(connection, "todos", "checklist_json", "TEXT NOT NULL DEFAULT '[]'");
        EnsureColumn(connection, "todos", "comments_json", "TEXT NOT NULL DEFAULT '[]'");
        EnsureColumn(connection, "todos", "updated_at", "TEXT NULL");

        using var layoutIndex = connection.CreateCommand();
        layoutIndex.CommandText = "CREATE INDEX IF NOT EXISTS ix_todos_board_list_position ON todos(board_id, list_id, position)";
        layoutIndex.ExecuteNonQuery();

        using var seed = connection.CreateCommand();
        seed.CommandText = """
            INSERT OR IGNORE INTO todo_boards (id, name, color)
            VALUES ('default', '工作看板', '#1769D1');
            INSERT OR IGNORE INTO todo_lists (id, board_id, name, position, status)
            VALUES ('pending-confirmation', 'default', '待确认', 0, 'pending_confirmation');
            UPDATE todo_lists
            SET position = position + 1
            WHERE board_id = 'default'
              AND id <> 'pending-confirmation'
              AND position >= 0
              AND changes() > 0;
            UPDATE todo_lists
            SET name = '待确认', status = 'pending_confirmation', is_archived = 0
            WHERE id = 'pending-confirmation' AND board_id = 'default';
            INSERT OR IGNORE INTO todo_lists (id, board_id, name, position, status)
            VALUES ('todo', 'default', '待办', 1, 'pending');
            INSERT OR IGNORE INTO todo_lists (id, board_id, name, position, status)
            VALUES ('in-progress', 'default', '进行中', 2, 'in_progress');
            INSERT OR IGNORE INTO todo_lists (id, board_id, name, position, status)
            VALUES ('done', 'default', '已完成', 3, 'completed');
            UPDATE todos SET list_id = 'pending-confirmation' WHERE status = 'pending_confirmation';
            UPDATE todos SET list_id = 'in-progress' WHERE status = 'in_progress' AND list_id = 'todo';
            UPDATE todos SET list_id = 'done' WHERE status = 'completed' AND list_id = 'todo';
            """;
        seed.ExecuteNonQuery();
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

            using var nextPosition = connection.CreateCommand();
            nextPosition.CommandText = "SELECT COALESCE(MAX(position), -1) + 1 FROM todos WHERE board_id = $board AND list_id = $list";
            var listId = candidate.Status.Equals("pending_confirmation", StringComparison.OrdinalIgnoreCase)
                ? "pending-confirmation"
                : candidate.ListId;
            nextPosition.Parameters.AddWithValue("$board", candidate.BoardId);
            nextPosition.Parameters.AddWithValue("$list", listId);
            var position = Convert.ToInt32(await nextPosition.ExecuteScalarAsync(cancellationToken));
            var storedCandidate = candidate with { ListId = listId, Position = position };

            using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO todos
                (id,title,summary,priority,due_at,assignee,confidence,source_refs_json,status,
                 created_at,confirmed_at,completed_at,source,conversation_id,company_id,company_name,
                 conversation_display_name,is_group_conversation,board_id,list_id,position,is_archived,
                 labels_json,checklist_json,comments_json,updated_at)
                VALUES ($id,$title,$summary,$priority,$due,$assignee,$confidence,$refs,$status,
                        $created,$confirmed,$completed,$source,$conversation,$companyId,$companyName,
                        $conversationDisplayName,$isGroupConversation,$boardId,$listId,$position,$isArchived,
                        $labels,$checklist,$comments,$updated);
                """;
            AddParameters(insert, storedCandidate);
            await insert.ExecuteNonQueryAsync(cancellationToken);
            return storedCandidate;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<TodoRecord>> ListAsync(bool includeRejected = false, bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            using var command = connection.CreateCommand();
            var statusFilter = includeRejected ? "1 = 1" : "status IN ('pending_confirmation', 'pending', 'in_progress', 'completed')";
            var archiveFilter = includeArchived ? "1 = 1" : "is_archived = 0";
            command.CommandText = $"SELECT * FROM todos WHERE {statusFilter} AND {archiveFilter} ORDER BY board_id, list_id, position, created_at DESC";
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
                    list_id = CASE $status WHEN 'pending' THEN 'todo' WHEN 'in_progress' THEN 'in-progress' WHEN 'completed' THEN 'done' ELSE list_id END,
                    updated_at = $now,
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

    public async Task<TodoRecord?> UpdateDetailsAsync(TodoRecord todo, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            using var update = connection.CreateCommand();
            update.CommandText = """
                UPDATE todos
                SET title = $title,
                    summary = $summary,
                    priority = $priority,
                     due_at = $due,
                     assignee = $assignee,
                     company_id = $companyId,
                     company_name = $companyName,
                     status = $status,
                    board_id = $boardId,
                    list_id = $listId,
                    position = $position,
                    is_archived = $isArchived,
                    labels_json = $labels,
                    checklist_json = $checklist,
                    comments_json = $comments,
                    updated_at = $updated
                WHERE id = $id;
                """;
            update.Parameters.AddWithValue("$id", todo.Id);
            update.Parameters.AddWithValue("$title", todo.Title);
            update.Parameters.AddWithValue("$summary", todo.Summary);
            update.Parameters.AddWithValue("$priority", todo.Priority);
            update.Parameters.AddWithValue("$due", todo.DueAt?.ToString("O") ?? (object)DBNull.Value);
            update.Parameters.AddWithValue("$assignee", todo.Assignee ?? (object)DBNull.Value);
            update.Parameters.AddWithValue("$companyId", todo.CompanyId ?? (object)DBNull.Value);
            update.Parameters.AddWithValue("$companyName", todo.CompanyName ?? (object)DBNull.Value);
            update.Parameters.AddWithValue("$status", todo.Status);
            update.Parameters.AddWithValue("$boardId", todo.BoardId);
            update.Parameters.AddWithValue("$listId", todo.ListId);
            update.Parameters.AddWithValue("$position", todo.Position);
            update.Parameters.AddWithValue("$isArchived", todo.IsArchived ? 1 : 0);
            update.Parameters.AddWithValue("$labels", JsonSerializer.Serialize(todo.Labels ?? Array.Empty<TodoLabel>(), _json));
            update.Parameters.AddWithValue("$checklist", JsonSerializer.Serialize(todo.Checklist ?? Array.Empty<TodoChecklistItem>(), _json));
            update.Parameters.AddWithValue("$comments", JsonSerializer.Serialize(todo.Comments ?? Array.Empty<TodoComment>(), _json));
            update.Parameters.AddWithValue("$updated", (todo.UpdatedAt ?? DateTimeOffset.UtcNow).ToString("O"));
            if (await update.ExecuteNonQueryAsync(cancellationToken) == 0) return null;

            using var select = connection.CreateCommand();
            select.CommandText = "SELECT * FROM todos WHERE id = $id";
            select.Parameters.AddWithValue("$id", todo.Id);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<TodoRecord?> MoveAsync(string todoId, string boardId, string listId, int position, string? status = null, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            using var transaction = connection.BeginTransaction();

            using var current = connection.CreateCommand();
            current.Transaction = transaction;
            current.CommandText = "SELECT board_id, list_id, position FROM todos WHERE id = $id";
            current.Parameters.AddWithValue("$id", todoId);
            await using var currentReader = await current.ExecuteReaderAsync(cancellationToken);
            if (!await currentReader.ReadAsync(cancellationToken)) return null;
            var oldBoardId = currentReader.GetString(0);
            var oldListId = currentReader.GetString(1);
            var oldPosition = currentReader.GetInt32(2);
            await currentReader.DisposeAsync();

            using var slotCount = connection.CreateCommand();
            slotCount.Transaction = transaction;
            slotCount.CommandText = "SELECT COUNT(*) FROM todos WHERE board_id = $board AND list_id = $list AND id <> $id";
            slotCount.Parameters.AddWithValue("$board", boardId);
            slotCount.Parameters.AddWithValue("$list", listId);
            slotCount.Parameters.AddWithValue("$id", todoId);
            var availableSlots = Convert.ToInt32(await slotCount.ExecuteScalarAsync(cancellationToken));
            var targetPosition = Math.Min(Math.Max(0, position), availableSlots);
            if (oldBoardId == boardId && oldListId == listId)
            {
                using var reorder = connection.CreateCommand();
                reorder.Transaction = transaction;
                reorder.CommandText = oldPosition < targetPosition
                    ? "UPDATE todos SET position = position - 1 WHERE board_id = $board AND list_id = $list AND position > $old AND position <= $target AND id <> $id"
                    : "UPDATE todos SET position = position + 1 WHERE board_id = $board AND list_id = $list AND position >= $target AND position < $old AND id <> $id";
                reorder.Parameters.AddWithValue("$board", boardId);
                reorder.Parameters.AddWithValue("$list", listId);
                reorder.Parameters.AddWithValue("$old", oldPosition);
                reorder.Parameters.AddWithValue("$target", targetPosition);
                reorder.Parameters.AddWithValue("$id", todoId);
                await reorder.ExecuteNonQueryAsync(cancellationToken);
            }
            else
            {
                using var removeGap = connection.CreateCommand();
                removeGap.Transaction = transaction;
                removeGap.CommandText = "UPDATE todos SET position = position - 1 WHERE board_id = $oldBoard AND list_id = $oldList AND position > $old AND id <> $id";
                removeGap.Parameters.AddWithValue("$oldBoard", oldBoardId);
                removeGap.Parameters.AddWithValue("$oldList", oldListId);
                removeGap.Parameters.AddWithValue("$old", oldPosition);
                removeGap.Parameters.AddWithValue("$id", todoId);
                await removeGap.ExecuteNonQueryAsync(cancellationToken);

                using var makeGap = connection.CreateCommand();
                makeGap.Transaction = transaction;
                makeGap.CommandText = "UPDATE todos SET position = position + 1 WHERE board_id = $board AND list_id = $list AND position >= $target AND id <> $id";
                makeGap.Parameters.AddWithValue("$board", boardId);
                makeGap.Parameters.AddWithValue("$list", listId);
                makeGap.Parameters.AddWithValue("$target", targetPosition);
                makeGap.Parameters.AddWithValue("$id", todoId);
                await makeGap.ExecuteNonQueryAsync(cancellationToken);
            }

            var now = DateTimeOffset.UtcNow.ToString("O");
            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE todos
                SET board_id = $board,
                    list_id = $list,
                    position = $position,
                    status = COALESCE($status, status),
                    completed_at = CASE WHEN $status = 'completed' THEN COALESCE(completed_at, $now) ELSE completed_at END,
                    updated_at = $now
                WHERE id = $id;
                """;
            update.Parameters.AddWithValue("$board", boardId);
            update.Parameters.AddWithValue("$list", listId);
            update.Parameters.AddWithValue("$position", targetPosition);
            update.Parameters.AddWithValue("$status", status ?? (object)DBNull.Value);
            update.Parameters.AddWithValue("$now", now);
            update.Parameters.AddWithValue("$id", todoId);
            if (await update.ExecuteNonQueryAsync(cancellationToken) == 0) return null;

            transaction.Commit();

            using var select = connection.CreateCommand();
            select.CommandText = "SELECT * FROM todos WHERE id = $id";
            select.Parameters.AddWithValue("$id", todoId);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM todos WHERE id = $id";
            command.Parameters.AddWithValue("$id", id);
            return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<TodoBoardRecord> GetBoardAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT id, name, color FROM todo_boards ORDER BY id LIMIT 1";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken)
                ? new TodoBoardRecord(reader.GetString(0), reader.GetString(1), reader.GetString(2))
                : new TodoBoardRecord("default", "工作看板", "#1769D1");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<TodoListRecord>> ListListsAsync(string boardId, bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            using var command = connection.CreateCommand();
            command.CommandText = includeArchived
                ? "SELECT id, board_id, name, position, status, is_archived FROM todo_lists WHERE board_id = $board ORDER BY position, id"
                : "SELECT id, board_id, name, position, status, is_archived FROM todo_lists WHERE board_id = $board AND is_archived = 0 ORDER BY position, id";
            command.Parameters.AddWithValue("$board", boardId);
            var result = new List<TodoListRecord>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                result.Add(new TodoListRecord(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetInt32(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.GetInt64(5) != 0));
            }

            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<TodoListRecord?> AddListAsync(TodoListRecord list, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            using var nextPosition = connection.CreateCommand();
            nextPosition.CommandText = "SELECT COALESCE(MAX(position), -1) + 1 FROM todo_lists WHERE board_id = $board AND is_archived = 0";
            nextPosition.Parameters.AddWithValue("$board", list.BoardId);
            var position = Convert.ToInt32(await nextPosition.ExecuteScalarAsync(cancellationToken));
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO todo_lists (id, board_id, name, position, status, is_archived)
                VALUES ($id, $board, $name, $position, $status, $archived);
                """;
            command.Parameters.AddWithValue("$id", list.Id);
            command.Parameters.AddWithValue("$board", list.BoardId);
            command.Parameters.AddWithValue("$name", list.Name);
            command.Parameters.AddWithValue("$position", position);
            command.Parameters.AddWithValue("$status", list.Status ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$archived", list.IsArchived ? 1 : 0);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return list with { Position = position };
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<TodoListRecord?> UpdateListAsync(TodoListRecord list, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE todo_lists
                SET name = $name, position = $position, is_archived = $archived
                WHERE id = $id AND board_id = $board AND id <> 'pending-confirmation';
                """;
            command.Parameters.AddWithValue("$id", list.Id);
            command.Parameters.AddWithValue("$board", list.BoardId);
            command.Parameters.AddWithValue("$name", list.Name);
            command.Parameters.AddWithValue("$position", list.Position);
            command.Parameters.AddWithValue("$archived", list.IsArchived ? 1 : 0);
            return await command.ExecuteNonQueryAsync(cancellationToken) == 0 ? null : list;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<TodoListRecord?> MoveListAsync(string id, string boardId, int position, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            using var transaction = connection.BeginTransaction();

            using var current = connection.CreateCommand();
            current.Transaction = transaction;
            current.CommandText = "SELECT position, is_archived, status FROM todo_lists WHERE id = $id AND board_id = $board";
            current.Parameters.AddWithValue("$id", id);
            current.Parameters.AddWithValue("$board", boardId);
            await using var reader = await current.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken) || reader.GetInt64(1) != 0)
                return null;
            var oldPosition = reader.GetInt32(0);
            var status = reader.IsDBNull(2) ? null : reader.GetString(2);
            await reader.DisposeAsync();

            using var count = connection.CreateCommand();
            count.Transaction = transaction;
            count.CommandText = "SELECT COUNT(*) FROM todo_lists WHERE board_id = $board AND is_archived = 0";
            count.Parameters.AddWithValue("$board", boardId);
            var listCount = Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken));
            var targetPosition = Math.Clamp(position, 0, Math.Max(0, listCount - 1));

            if (oldPosition < targetPosition)
            {
                using var shift = connection.CreateCommand();
                shift.Transaction = transaction;
                shift.CommandText = "UPDATE todo_lists SET position = position - 1 WHERE board_id = $board AND is_archived = 0 AND position > $old AND position <= $target AND id <> $id";
                shift.Parameters.AddWithValue("$board", boardId);
                shift.Parameters.AddWithValue("$old", oldPosition);
                shift.Parameters.AddWithValue("$target", targetPosition);
                shift.Parameters.AddWithValue("$id", id);
                await shift.ExecuteNonQueryAsync(cancellationToken);
            }
            else if (oldPosition > targetPosition)
            {
                using var shift = connection.CreateCommand();
                shift.Transaction = transaction;
                shift.CommandText = "UPDATE todo_lists SET position = position + 1 WHERE board_id = $board AND is_archived = 0 AND position >= $target AND position < $old AND id <> $id";
                shift.Parameters.AddWithValue("$board", boardId);
                shift.Parameters.AddWithValue("$old", oldPosition);
                shift.Parameters.AddWithValue("$target", targetPosition);
                shift.Parameters.AddWithValue("$id", id);
                await shift.ExecuteNonQueryAsync(cancellationToken);
            }

            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = "UPDATE todo_lists SET position = $position WHERE id = $id AND board_id = $board AND is_archived = 0";
            update.Parameters.AddWithValue("$position", targetPosition);
            update.Parameters.AddWithValue("$id", id);
            update.Parameters.AddWithValue("$board", boardId);
            if (await update.ExecuteNonQueryAsync(cancellationToken) == 0)
                return null;
            transaction.Commit();

            using var select = connection.CreateCommand();
            select.CommandText = "SELECT id, board_id, name, position, status, is_archived FROM todo_lists WHERE id = $id";
            select.Parameters.AddWithValue("$id", id);
            await using var selected = await select.ExecuteReaderAsync(cancellationToken);
            return await selected.ReadAsync(cancellationToken)
                ? new TodoListRecord(selected.GetString(0), selected.GetString(1), selected.GetString(2), selected.GetInt32(3), selected.IsDBNull(4) ? null : selected.GetString(4), selected.GetInt64(5) != 0)
                : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> ArchiveListAsync(string id, bool isArchived, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE todo_lists SET is_archived = $archived WHERE id = $id AND status IS NULL";
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$archived", isArchived ? 1 : 0);
            return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> DeleteListAsync(string id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            using var transaction = connection.BeginTransaction();

            using var listInfo = connection.CreateCommand();
            listInfo.Transaction = transaction;
            listInfo.CommandText = "SELECT board_id, position FROM todo_lists WHERE id = $id AND status IS NULL";
            listInfo.Parameters.AddWithValue("$id", id);
            await using var listReader = await listInfo.ExecuteReaderAsync(cancellationToken);
            if (!await listReader.ReadAsync(cancellationToken))
                return false;
            var boardId = listReader.GetString(0);
            var position = listReader.GetInt32(1);
            await listReader.DisposeAsync();

            using var deleteCards = connection.CreateCommand();
            deleteCards.Transaction = transaction;
            deleteCards.CommandText = "DELETE FROM todos WHERE list_id = $id";
            deleteCards.Parameters.AddWithValue("$id", id);
            await deleteCards.ExecuteNonQueryAsync(cancellationToken);

            using var deleteList = connection.CreateCommand();
            deleteList.Transaction = transaction;
            deleteList.CommandText = "DELETE FROM todo_lists WHERE id = $id AND status IS NULL";
            deleteList.Parameters.AddWithValue("$id", id);
            var changed = await deleteList.ExecuteNonQueryAsync(cancellationToken) > 0;
            if (changed)
            {
                using var closeGap = connection.CreateCommand();
                closeGap.Transaction = transaction;
                closeGap.CommandText = "UPDATE todo_lists SET position = position - 1 WHERE board_id = $board AND is_archived = 0 AND position > $position";
                closeGap.Parameters.AddWithValue("$board", boardId);
                closeGap.Parameters.AddWithValue("$position", position);
                await closeGap.ExecuteNonQueryAsync(cancellationToken);
                transaction.Commit();
            }
            return changed;
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
        command.Parameters.AddWithValue("$companyId", todo.CompanyId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$companyName", todo.CompanyName ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$conversationDisplayName", todo.ConversationDisplayName ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$isGroupConversation", todo.IsGroupConversation ? 1 : 0);
        command.Parameters.AddWithValue("$boardId", todo.BoardId);
        command.Parameters.AddWithValue("$listId", todo.ListId);
        command.Parameters.AddWithValue("$position", todo.Position);
        command.Parameters.AddWithValue("$isArchived", todo.IsArchived ? 1 : 0);
        command.Parameters.AddWithValue("$labels", JsonSerializer.Serialize(todo.Labels ?? Array.Empty<TodoLabel>(), _json));
        command.Parameters.AddWithValue("$checklist", JsonSerializer.Serialize(todo.Checklist ?? Array.Empty<TodoChecklistItem>(), _json));
        command.Parameters.AddWithValue("$comments", JsonSerializer.Serialize(todo.Comments ?? Array.Empty<TodoComment>(), _json));
        command.Parameters.AddWithValue("$updated", todo.UpdatedAt?.ToString("O") ?? (object)DBNull.Value);
    }

    private TodoRecord Read(SqliteDataReader reader)
    {
        var due = ReadDateTimeOffset(reader, "due_at");
        var confirmed = ReadDateTimeOffset(reader, "confirmed_at");
        var completed = ReadDateTimeOffset(reader, "completed_at");
        var refs = JsonSerializer.Deserialize<IReadOnlyList<string>>(
                       reader.GetString(reader.GetOrdinal("source_refs_json")), _json)
                   ?? Array.Empty<string>();
        var labels = ReadJson<IReadOnlyList<TodoLabel>>(reader, "labels_json") ?? Array.Empty<TodoLabel>();
        var checklist = ReadJson<IReadOnlyList<TodoChecklistItem>>(reader, "checklist_json") ?? Array.Empty<TodoChecklistItem>();
        var comments = ReadJson<IReadOnlyList<TodoComment>>(reader, "comments_json") ?? Array.Empty<TodoComment>();
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
            reader.GetString(reader.GetOrdinal("conversation_id")),
            ReadNullableString(reader, "company_id"),
            ReadNullableString(reader, "company_name"),
            ReadNullableString(reader, "conversation_display_name"),
            reader.GetInt64(reader.GetOrdinal("is_group_conversation")) != 0,
            reader.GetString(reader.GetOrdinal("board_id")),
            reader.GetString(reader.GetOrdinal("list_id")),
            reader.GetInt32(reader.GetOrdinal("position")),
            reader.GetInt64(reader.GetOrdinal("is_archived")) != 0,
            labels,
            checklist,
            comments,
            ReadDateTimeOffset(reader, "updated_at"));
    }

    private T? ReadJson<T>(SqliteDataReader reader, string column)
        => reader.IsDBNull(reader.GetOrdinal(column))
            ? default
            : JsonSerializer.Deserialize<T>(reader.GetString(reader.GetOrdinal(column)), _json);

    private static string? ReadNullableString(SqliteDataReader reader, string column)
        => reader.IsDBNull(reader.GetOrdinal(column)) ? null : reader.GetString(reader.GetOrdinal(column));

    private static DateTimeOffset? ReadDateTimeOffset(SqliteDataReader reader, string column)
        => reader.IsDBNull(reader.GetOrdinal(column))
            ? null
            : DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal(column)));

    private static void EnsureColumn(SqliteConnection connection, string table, string column, string definition)
    {
        var exists = false;
        using (var check = connection.CreateCommand())
        {
            check.CommandText = $"PRAGMA table_info({table})";
            using var reader = check.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }
        }

        if (exists) return;

        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition}";
        alter.ExecuteNonQuery();
    }

    public void Dispose() => _gate.Dispose();
}
