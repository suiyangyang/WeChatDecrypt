using Lumine.Fairy.ContactManagement.Contracts;
using Lumine.Fairy.ContactManagement.Core.Models;
using Lumine.Fairy.ContactManagement.Core.Services;
using Lumine.Fairy.ContactManagement.Infrastructure.Options;
using Lumine.Fairy.ContactManagement.Models;
using Lumine.Fairy.Services;
using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Text.Json;

namespace Lumine.Fairy.ContactManagement.Infrastructure.Persistence;

public sealed class ContactManagementSqliteStore : IContactManagementStore
{
    private readonly ContactManagementStorageOptions _options;
    private readonly ContactManagementValidationService _validationService;
    private readonly object _syncRoot = new();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public ContactManagementSqliteStore(ContactManagementStorageOptions options, ContactManagementValidationService? validationService = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _validationService = validationService ?? new ContactManagementValidationService();

        MigrateLegacyDatabaseIfNeeded();
        Directory.CreateDirectory(_options.BaseDirectoryPath);
        EnsureSchema();
    }

    private void MigrateLegacyDatabaseIfNeeded()
    {
        var defaultBaseDirectory = Path.GetFullPath(ContactManagementStorageOptions.GetDefaultBaseDirectoryPath());
        var currentBaseDirectory = Path.GetFullPath(_options.BaseDirectoryPath);
        if (!string.Equals(currentBaseDirectory, defaultBaseDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var legacyBaseDirectories = new[]
        {
            Path.GetFullPath(Path.Combine(LumineFairyAppDataPaths.GetAppRootDirectory(), "data", "contact-management")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "data", "contact-management"))
        };

        foreach (var legacyBaseDirectory in legacyBaseDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.Equals(legacyBaseDirectory, currentBaseDirectory, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var legacyDatabasePath = Path.Combine(legacyBaseDirectory, _options.DatabaseFileName);
            if (!File.Exists(legacyDatabasePath) || File.Exists(_options.DatabaseFilePath))
            {
                continue;
            }

            Directory.CreateDirectory(_options.BaseDirectoryPath);
            File.Copy(legacyDatabasePath, _options.DatabaseFilePath);
            CopySidecarFileIfExists(legacyDatabasePath + "-wal", _options.DatabaseFilePath + "-wal");
            CopySidecarFileIfExists(legacyDatabasePath + "-shm", _options.DatabaseFilePath + "-shm");
            break;
        }
    }

    private static void CopySidecarFileIfExists(string sourcePath, string targetPath)
    {
        if (!File.Exists(sourcePath) || File.Exists(targetPath))
        {
            return;
        }

        File.Copy(sourcePath, targetPath);
    }

    public Task<ContactManagementSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            using var connection = OpenConnection();
            EnsureSchemaVersion(connection);

            var dataSet = new ContactManagementDataSet
            {
                Contacts = LoadContacts(connection),
                Companies = LoadCompanies(connection),
                Projects = LoadProjects(connection),
                WeChatGroupChats = LoadWeChatGroupChats(connection),
                RelationshipLinks = LoadRelationshipLinks(connection),
                TodoEntries = LoadTodoEntries(connection),
                ClientIssues = LoadClientIssues(connection),
                DailyReports = LoadDailyReports(connection),
                ScratchNoteDocuments = LoadScratchNoteDocuments(connection)
            };

            _validationService.Validate(dataSet);
            return Task.FromResult(dataSet.ToSnapshot());
        }
    }

    public Task SaveAsync(ContactManagementSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();

        var dataSet = ContactManagementDataSet.FromSnapshot(snapshot);
        _validationService.Validate(dataSet);

        lock (_syncRoot)
        {
            using var connection = OpenConnection();
            EnsureSchemaVersion(connection);
            using var transaction = connection.BeginTransaction();

            ExecuteNonQuery(connection, transaction, "DELETE FROM relationship_links;");
            ExecuteNonQuery(connection, transaction, "DELETE FROM projects;");
            ExecuteNonQuery(connection, transaction, "DELETE FROM wechat_group_chats;");
            ExecuteNonQuery(connection, transaction, "DELETE FROM companies;");
            ExecuteNonQuery(connection, transaction, "DELETE FROM contacts;");
            ExecuteNonQuery(connection, transaction, "DELETE FROM daily_reports;");
            ExecuteNonQuery(connection, transaction, "DELETE FROM client_issue_entries;");
            ExecuteNonQuery(connection, transaction, "DELETE FROM todo_entries;");
            ExecuteNonQuery(connection, transaction, "DELETE FROM scratch_note_documents;");

            SaveContacts(connection, transaction, snapshot.Contacts);
            SaveCompanies(connection, transaction, snapshot.Companies);
            SaveProjects(connection, transaction, snapshot.Projects);
            SaveWeChatGroupChats(connection, transaction, snapshot.WeChatGroupChats);
            SaveRelationshipLinks(connection, transaction, snapshot.RelationshipLinks);
            SaveTodoEntries(connection, transaction, snapshot.TodoEntries);
            SaveClientIssues(connection, transaction, snapshot.ClientIssues);
            SaveDailyReports(connection, transaction, snapshot.DailyReports);
            SaveScratchNoteDocuments(connection, transaction, snapshot.ScratchNoteDocuments);

            transaction.Commit();
        }

        return Task.CompletedTask;
    }

    private void EnsureSchema()
    {
        lock (_syncRoot)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS app_meta (
                    key     TEXT PRIMARY KEY,
                    value   TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS contacts (
                    id                  TEXT PRIMARY KEY,
                    full_name           TEXT NOT NULL,
                    nicknames_json      TEXT NOT NULL DEFAULT '[]',
                    wechat_ids_json     TEXT NOT NULL DEFAULT '[]',
                    phones_json         TEXT NOT NULL DEFAULT '[]',
                    emails_json         TEXT NOT NULL DEFAULT '[]',
                    company_ids_json    TEXT NOT NULL DEFAULT '[]',
                    project_ids_json    TEXT NOT NULL DEFAULT '[]',
                    relationship_type   INTEGER NOT NULL,
                    notes               TEXT NOT NULL DEFAULT '',
                    is_active           INTEGER NOT NULL DEFAULT 1
                );

                CREATE TABLE IF NOT EXISTS companies (
                    id                  TEXT PRIMARY KEY,
                    name                TEXT NOT NULL,
                    addresses_json      TEXT NOT NULL DEFAULT '[]',
                    departments_json    TEXT NOT NULL DEFAULT '[]',
                    notes               TEXT NOT NULL DEFAULT '',
                    is_active           INTEGER NOT NULL DEFAULT 1
                );

                CREATE TABLE IF NOT EXISTS projects (
                    id                  TEXT PRIMARY KEY,
                    name                TEXT NOT NULL,
                    company_id          TEXT NULL,
                    owner_contact_id    TEXT NULL,
                    delivery_date       TEXT NULL,
                    tags_json           TEXT NOT NULL DEFAULT '[]',
                    group_chats_json    TEXT NOT NULL DEFAULT '[]',
                    notes               TEXT NOT NULL DEFAULT '',
                    is_active           INTEGER NOT NULL DEFAULT 1
                );

                CREATE TABLE IF NOT EXISTS wechat_group_chats (
                    id                  TEXT PRIMARY KEY,
                    account_id          TEXT NOT NULL,
                    account_display_name TEXT NOT NULL DEFAULT '',
                    wechat_username     TEXT NOT NULL,
                    display_name        TEXT NOT NULL,
                    last_message_at     TEXT NULL,
                    company_id          TEXT NULL,
                    project_id          TEXT NULL,
                    is_active           INTEGER NOT NULL DEFAULT 1,
                    last_synced_at      TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS relationship_links (
                    id                      TEXT PRIMARY KEY,
                    source_entity_id        TEXT NOT NULL,
                    source_entity_kind      INTEGER NOT NULL,
                    target_entity_id        TEXT NOT NULL,
                    target_entity_kind      INTEGER NOT NULL,
                    link_type               INTEGER NOT NULL,
                    department_name         TEXT NOT NULL DEFAULT '',
                    title                   TEXT NOT NULL DEFAULT '',
                    is_primary              INTEGER NOT NULL DEFAULT 0,
                    notes                   TEXT NOT NULL DEFAULT ''
                );

                CREATE TABLE IF NOT EXISTS todo_entries (
                    id                      TEXT PRIMARY KEY,
                    work_date               TEXT NULL,
                    project_name            TEXT NOT NULL,
                    title                   TEXT NOT NULL DEFAULT '',
                    content                 TEXT NOT NULL DEFAULT '',
                    status                  INTEGER NOT NULL,
                    work_hours              REAL NULL,
                    is_daily                INTEGER NOT NULL DEFAULT 0,
                    is_from_scratch_note    INTEGER NOT NULL DEFAULT 0,
                    source_text             TEXT NOT NULL DEFAULT '',
                    sort_order              INTEGER NOT NULL DEFAULT 0,
                    notes                   TEXT NOT NULL DEFAULT ''
                );

                CREATE TABLE IF NOT EXISTS client_issue_entries (
                    id                      TEXT PRIMARY KEY,
                    work_date               TEXT NULL,
                    project_name            TEXT NOT NULL,
                    client_name             TEXT NOT NULL,
                    issue_lines_json        TEXT NOT NULL DEFAULT '[]',
                    solution_lines_json     TEXT NOT NULL DEFAULT '[]',
                    notes                   TEXT NOT NULL DEFAULT ''
                );

                CREATE TABLE IF NOT EXISTS daily_reports (
                    work_date               TEXT PRIMARY KEY,
                    items_json              TEXT NOT NULL DEFAULT '[]',
                    client_issues_json      TEXT NOT NULL DEFAULT '[]',
                    total_hours             REAL NOT NULL DEFAULT 0,
                    raw_scratch_text        TEXT NOT NULL DEFAULT '',
                    formatted_report_text   TEXT NOT NULL DEFAULT ''
                );

                CREATE TABLE IF NOT EXISTS scratch_note_documents (
                    id                      TEXT PRIMARY KEY,
                    work_date               TEXT NULL,
                    title                   TEXT NOT NULL DEFAULT '',
                    raw_text                TEXT NOT NULL DEFAULT '',
                    created_at_utc          TEXT NOT NULL,
                    updated_at_utc          TEXT NOT NULL,
                    last_arranged_at_utc    TEXT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_contacts_full_name ON contacts(full_name);
                CREATE INDEX IF NOT EXISTS idx_companies_name ON companies(name);
                CREATE INDEX IF NOT EXISTS idx_projects_name ON projects(name);
                CREATE UNIQUE INDEX IF NOT EXISTS idx_wechat_group_chat_identity ON wechat_group_chats(account_id, wechat_username);
                CREATE INDEX IF NOT EXISTS idx_wechat_group_chats_company ON wechat_group_chats(company_id);
                CREATE INDEX IF NOT EXISTS idx_relationship_links_source ON relationship_links(source_entity_kind, source_entity_id);
                CREATE INDEX IF NOT EXISTS idx_relationship_links_target ON relationship_links(target_entity_kind, target_entity_id);
                CREATE INDEX IF NOT EXISTS idx_todo_entries_work_date ON todo_entries(work_date, sort_order);
                CREATE INDEX IF NOT EXISTS idx_todo_entries_project_name ON todo_entries(project_name, status);
                CREATE INDEX IF NOT EXISTS idx_client_issue_entries_work_date ON client_issue_entries(work_date, project_name);
                CREATE INDEX IF NOT EXISTS idx_scratch_note_documents_work_date ON scratch_note_documents(work_date, updated_at_utc);
                """;
            command.ExecuteNonQuery();

            EnsureSchemaVersion(connection);
        }
    }

    private void EnsureSchemaVersion(SqliteConnection connection)
    {
        using var select = connection.CreateCommand();
        select.CommandText = "SELECT value FROM app_meta WHERE key='schema_version' LIMIT 1;";
        var value = select.ExecuteScalar()?.ToString();

        if (string.IsNullOrWhiteSpace(value))
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO app_meta(key, value) VALUES('schema_version', $value);";
            insert.Parameters.AddWithValue("$value", _options.SchemaVersion.ToString());
            insert.ExecuteNonQuery();
            return;
        }

        if (!int.TryParse(value, out var schemaVersion))
        {
            throw new InvalidOperationException($"联系人数据库 schemaVersion={value} 无法识别。");
        }

        if (schemaVersion > _options.SchemaVersion)
        {
            throw new InvalidOperationException($"联系人数据库 schemaVersion={value}，当前仅支持 {_options.SchemaVersion}。");
        }

        if (schemaVersion < _options.SchemaVersion)
        {
            UpgradeSchema(connection, schemaVersion, _options.SchemaVersion);
            return;
        }

        if (schemaVersion != _options.SchemaVersion)
        {
            throw new InvalidOperationException($"联系人数据库 schemaVersion={value}，当前仅支持 {_options.SchemaVersion}。");
        }
    }

    private static void UpgradeSchema(SqliteConnection connection, int currentVersion, int targetVersion)
    {
        var version = currentVersion;

        while (version < targetVersion)
        {
            switch (version)
            {
                case 1:
                    SetSchemaVersion(connection, 2);
                    version = 2;
                    break;
                case 2:
                    AddColumnIfMissing(connection, "projects", "delivery_date", "TEXT NULL");

                    SetSchemaVersion(connection, 3);
                    version = 3;
                    break;
                case 3:
                    AddColumnIfMissing(connection, "contacts", "company_ids_json", "TEXT NOT NULL DEFAULT '[]'");
                    AddColumnIfMissing(connection, "contacts", "project_ids_json", "TEXT NOT NULL DEFAULT '[]'");
                    AddColumnIfMissing(connection, "projects", "group_chats_json", "TEXT NOT NULL DEFAULT '[]'");

                    SetSchemaVersion(connection, 4);
                    version = 4;
                    break;
                case 4:
                    AddColumnIfMissing(connection, "contacts", "wechat_ids_json", "TEXT NOT NULL DEFAULT '[]'");

                    SetSchemaVersion(connection, 5);
                    version = 5;
                    break;
                case 5:
                    using (var create = connection.CreateCommand())
                    {
                        create.CommandText = """
                            CREATE TABLE IF NOT EXISTS wechat_group_chats (
                                id TEXT PRIMARY KEY,
                                account_id TEXT NOT NULL,
                                wechat_username TEXT NOT NULL,
                                display_name TEXT NOT NULL,
                                last_message_at TEXT NULL,
                                company_id TEXT NULL,
                                project_id TEXT NULL,
                                is_active INTEGER NOT NULL DEFAULT 1,
                                last_synced_at TEXT NOT NULL
                            );
                            CREATE UNIQUE INDEX IF NOT EXISTS idx_wechat_group_chat_identity ON wechat_group_chats(account_id, wechat_username);
                            CREATE INDEX IF NOT EXISTS idx_wechat_group_chats_company ON wechat_group_chats(company_id);
                            """;
                        create.ExecuteNonQuery();
                    }

                    SetSchemaVersion(connection, 6);
                    version = 6;
                    break;
                case 6:
                    AddColumnIfMissing(connection, "wechat_group_chats", "account_display_name", "TEXT NOT NULL DEFAULT ''");

                    SetSchemaVersion(connection, 7);
                    version = 7;
                    break;
                default:
                    throw new InvalidOperationException($"不支持从 schemaVersion={currentVersion} 升级到 {targetVersion}。");
            }
        }
    }

    private static void AddColumnIfMissing(SqliteConnection connection, string tableName, string columnName, string columnDefinition)
    {
        using var exists = connection.CreateCommand();
        exists.CommandText = "SELECT 1 FROM pragma_table_info($table) WHERE name=$column LIMIT 1;";
        exists.Parameters.AddWithValue("$table", tableName);
        exists.Parameters.AddWithValue("$column", columnName);
        if (exists.ExecuteScalar() is not null)
        {
            return;
        }

        // 迁移中的表名、字段名和字段定义均为代码内常量，不接受外部输入。
        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};";
        alter.ExecuteNonQuery();
    }

    private static void SetSchemaVersion(SqliteConnection connection, int schemaVersion)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE app_meta SET value=$value WHERE key='schema_version';";
        command.Parameters.AddWithValue("$value", schemaVersion.ToString(CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection($"Data Source={_options.DatabaseFilePath};Foreign Keys=True;");
        connection.Open();
        return connection;
    }

    private static void ExecuteNonQuery(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static List<ContactProfile> LoadContacts(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, full_name, nicknames_json, wechat_ids_json, phones_json, emails_json, company_ids_json, project_ids_json, relationship_type, notes, is_active
            FROM contacts
            ORDER BY full_name ASC, id ASC;
            """;

        using var reader = command.ExecuteReader();
        var items = new List<ContactProfile>();
        while (reader.Read())
        {
            items.Add(new ContactProfile
            {
                Id = reader.GetString(0),
                FullName = reader.GetString(1),
                Nicknames = DeserializeStringList(reader.GetString(2)),
                WeChatIds = DeserializeStringList(reader.GetString(3)),
                Phones = DeserializeStringList(reader.GetString(4)),
                Emails = DeserializeStringList(reader.GetString(5)),
                CompanyIds = DeserializeStringList(reader.GetString(6)),
                ProjectIds = DeserializeStringList(reader.GetString(7)),
                RelationshipType = (ContactRelationshipType)reader.GetInt32(8),
                Notes = reader.GetString(9),
                IsActive = reader.GetInt64(10) != 0
            });
        }

        return items;
    }

    private static List<CompanyProfile> LoadCompanies(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, name, addresses_json, departments_json, notes, is_active
            FROM companies
            ORDER BY name ASC, id ASC;
            """;

        using var reader = command.ExecuteReader();
        var items = new List<CompanyProfile>();
        while (reader.Read())
        {
            items.Add(new CompanyProfile
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                Addresses = DeserializeStringList(reader.GetString(2)),
                Departments = DeserializeStringList(reader.GetString(3)),
                Notes = reader.GetString(4),
                IsActive = reader.GetInt64(5) != 0
            });
        }

        return items;
    }

    private static List<ProjectProfile> LoadProjects(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, name, company_id, owner_contact_id, delivery_date, tags_json, group_chats_json, notes, is_active
            FROM projects
            ORDER BY name ASC, id ASC;
            """;

        using var reader = command.ExecuteReader();
        var items = new List<ProjectProfile>();
        while (reader.Read())
        {
            items.Add(new ProjectProfile
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                CompanyId = reader.IsDBNull(2) ? null : reader.GetString(2),
                OwnerContactId = reader.IsDBNull(3) ? null : reader.GetString(3),
                DeliveryDate = reader.IsDBNull(4) ? null : ParseDateOnly(reader.GetString(4)),
                Tags = DeserializeStringList(reader.GetString(5)),
                GroupChats = DeserializeStringList(reader.GetString(6)),
                Notes = reader.GetString(7),
                IsActive = reader.GetInt64(8) != 0
            });
        }

        return items;
    }

    private static List<RelationshipLink> LoadRelationshipLinks(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, source_entity_id, source_entity_kind, target_entity_id, target_entity_kind, link_type, department_name, title, is_primary, notes
            FROM relationship_links
            ORDER BY source_entity_kind ASC, source_entity_id ASC, target_entity_kind ASC, target_entity_id ASC, id ASC;
            """;

        using var reader = command.ExecuteReader();
        var items = new List<RelationshipLink>();
        while (reader.Read())
        {
            items.Add(new RelationshipLink
            {
                Id = reader.GetString(0),
                SourceEntityId = reader.GetString(1),
                SourceEntityKind = (EntityKind)reader.GetInt32(2),
                TargetEntityId = reader.GetString(3),
                TargetEntityKind = (EntityKind)reader.GetInt32(4),
                LinkType = (RelationshipLinkType)reader.GetInt32(5),
                DepartmentName = reader.GetString(6),
                Title = reader.GetString(7),
                IsPrimary = reader.GetInt64(8) != 0,
                Notes = reader.GetString(9)
            });
        }

        return items;
    }

    private static List<WeChatGroupChatProfile> LoadWeChatGroupChats(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, account_id, account_display_name, wechat_username, display_name, last_message_at, company_id, project_id, is_active, last_synced_at
            FROM wechat_group_chats
            ORDER BY last_message_at DESC, display_name ASC, id ASC;
            """;

        using var reader = command.ExecuteReader();
        var items = new List<WeChatGroupChatProfile>();
        while (reader.Read())
        {
            items.Add(new WeChatGroupChatProfile
            {
                Id = reader.GetString(0),
                AccountId = reader.GetString(1),
                AccountDisplayName = reader.GetString(2),
                WeChatUsername = reader.GetString(3),
                DisplayName = reader.GetString(4),
                LastMessageAt = reader.IsDBNull(5) ? null : ParseDateTimeOffset(reader.GetString(5)),
                CompanyId = reader.IsDBNull(6) ? null : reader.GetString(6),
                ProjectId = reader.IsDBNull(7) ? null : reader.GetString(7),
                IsActive = reader.GetInt64(8) != 0,
                LastSyncedAt = ParseDateTimeOffset(reader.GetString(9))
            });
        }

        return items;
    }

    private static List<TodoEntry> LoadTodoEntries(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, work_date, project_name, title, content, status, work_hours, is_daily, is_from_scratch_note, source_text, sort_order, notes
            FROM todo_entries
            ORDER BY work_date ASC, sort_order ASC, id ASC;
            """;

        using var reader = command.ExecuteReader();
        var items = new List<TodoEntry>();
        while (reader.Read())
        {
            items.Add(new TodoEntry
            {
                Id = reader.GetString(0),
                WorkDate = reader.IsDBNull(1) ? null : ParseDateOnly(reader.GetString(1)),
                ProjectName = reader.GetString(2),
                Title = reader.GetString(3),
                Content = reader.GetString(4),
                Status = (TodoStatus)reader.GetInt32(5),
                WorkHours = reader.IsDBNull(6) ? null : Convert.ToDecimal(reader.GetDouble(6), CultureInfo.InvariantCulture),
                IsDaily = reader.GetInt64(7) != 0,
                IsFromScratchNote = reader.GetInt64(8) != 0,
                SourceText = reader.GetString(9),
                SortOrder = reader.GetInt32(10),
                Notes = reader.GetString(11)
            });
        }

        return items;
    }

    private static List<ClientIssueEntry> LoadClientIssues(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, work_date, project_name, client_name, issue_lines_json, solution_lines_json, notes
            FROM client_issue_entries
            ORDER BY work_date ASC, project_name ASC, client_name ASC, id ASC;
            """;

        using var reader = command.ExecuteReader();
        var items = new List<ClientIssueEntry>();
        while (reader.Read())
        {
            items.Add(new ClientIssueEntry
            {
                Id = reader.GetString(0),
                WorkDate = reader.IsDBNull(1) ? null : ParseDateOnly(reader.GetString(1)),
                ProjectName = reader.GetString(2),
                ClientName = reader.GetString(3),
                IssueLines = DeserializeJson<List<string>>(reader.GetString(4)) ?? [],
                SolutionLines = DeserializeJson<List<string>>(reader.GetString(5)) ?? [],
                Notes = reader.GetString(6)
            });
        }

        return items;
    }

    private static List<DailyReport> LoadDailyReports(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT work_date, items_json, client_issues_json, total_hours, raw_scratch_text, formatted_report_text
            FROM daily_reports
            ORDER BY work_date ASC;
            """;

        using var reader = command.ExecuteReader();
        var items = new List<DailyReport>();
        while (reader.Read())
        {
            items.Add(new DailyReport
            {
                Date = ParseDateOnly(reader.GetString(0)),
                Items = DeserializeJson<List<TodoEntry>>(reader.GetString(1)) ?? [],
                ClientIssues = DeserializeJson<List<ClientIssueEntry>>(reader.GetString(2)) ?? [],
                TotalHours = Convert.ToDecimal(reader.GetDouble(3), CultureInfo.InvariantCulture),
                RawScratchText = reader.GetString(4),
                FormattedReportText = reader.GetString(5)
            });
        }

        return items;
    }

    private static List<ScratchNoteDocument> LoadScratchNoteDocuments(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, work_date, title, raw_text, created_at_utc, updated_at_utc, last_arranged_at_utc
            FROM scratch_note_documents
            ORDER BY updated_at_utc DESC, id ASC;
            """;

        using var reader = command.ExecuteReader();
        var items = new List<ScratchNoteDocument>();
        while (reader.Read())
        {
            items.Add(new ScratchNoteDocument
            {
                Id = reader.GetString(0),
                WorkDate = reader.IsDBNull(1) ? null : ParseDateOnly(reader.GetString(1)),
                Title = reader.GetString(2),
                RawText = reader.GetString(3),
                CreatedAtUtc = ParseDateTimeOffset(reader.GetString(4)),
                UpdatedAtUtc = ParseDateTimeOffset(reader.GetString(5)),
                LastArrangedAtUtc = reader.IsDBNull(6) ? null : ParseDateTimeOffset(reader.GetString(6))
            });
        }

        return items;
    }

    private static void SaveContacts(SqliteConnection connection, SqliteTransaction transaction, IReadOnlyList<ContactProfile> contacts)
    {
        foreach (var contact in contacts)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO contacts(id, full_name, nicknames_json, wechat_ids_json, phones_json, emails_json, company_ids_json, project_ids_json, relationship_type, notes, is_active)
                VALUES($id, $full_name, $nicknames_json, $wechat_ids_json, $phones_json, $emails_json, $company_ids_json, $project_ids_json, $relationship_type, $notes, $is_active);
                """;
            command.Parameters.AddWithValue("$id", contact.Id);
            command.Parameters.AddWithValue("$full_name", contact.FullName);
            command.Parameters.AddWithValue("$nicknames_json", SerializeStringList(contact.Nicknames));
            command.Parameters.AddWithValue("$wechat_ids_json", SerializeStringList(contact.WeChatIds));
            command.Parameters.AddWithValue("$phones_json", SerializeStringList(contact.Phones));
            command.Parameters.AddWithValue("$emails_json", SerializeStringList(contact.Emails));
            command.Parameters.AddWithValue("$company_ids_json", SerializeStringList(contact.CompanyIds));
            command.Parameters.AddWithValue("$project_ids_json", SerializeStringList(contact.ProjectIds));
            command.Parameters.AddWithValue("$relationship_type", (int)contact.RelationshipType);
            command.Parameters.AddWithValue("$notes", contact.Notes);
            command.Parameters.AddWithValue("$is_active", contact.IsActive ? 1 : 0);
            command.ExecuteNonQuery();
        }
    }

    private static void SaveCompanies(SqliteConnection connection, SqliteTransaction transaction, IReadOnlyList<CompanyProfile> companies)
    {
        foreach (var company in companies)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO companies(id, name, addresses_json, departments_json, notes, is_active)
                VALUES($id, $name, $addresses_json, $departments_json, $notes, $is_active);
                """;
            command.Parameters.AddWithValue("$id", company.Id);
            command.Parameters.AddWithValue("$name", company.Name);
            command.Parameters.AddWithValue("$addresses_json", SerializeStringList(company.Addresses));
            command.Parameters.AddWithValue("$departments_json", SerializeStringList(company.Departments));
            command.Parameters.AddWithValue("$notes", company.Notes);
            command.Parameters.AddWithValue("$is_active", company.IsActive ? 1 : 0);
            command.ExecuteNonQuery();
        }
    }

    private static void SaveProjects(SqliteConnection connection, SqliteTransaction transaction, IReadOnlyList<ProjectProfile> projects)
    {
        foreach (var project in projects)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO projects(id, name, company_id, owner_contact_id, delivery_date, tags_json, group_chats_json, notes, is_active)
                VALUES($id, $name, $company_id, $owner_contact_id, $delivery_date, $tags_json, $group_chats_json, $notes, $is_active);
                """;
            command.Parameters.AddWithValue("$id", project.Id);
            command.Parameters.AddWithValue("$name", project.Name);
            command.Parameters.AddWithValue("$company_id", (object?)project.CompanyId ?? DBNull.Value);
            command.Parameters.AddWithValue("$owner_contact_id", (object?)project.OwnerContactId ?? DBNull.Value);
            command.Parameters.AddWithValue("$delivery_date", (object?)FormatDateOnly(project.DeliveryDate) ?? DBNull.Value);
            command.Parameters.AddWithValue("$tags_json", SerializeStringList(project.Tags));
            command.Parameters.AddWithValue("$group_chats_json", SerializeStringList(project.GroupChats));
            command.Parameters.AddWithValue("$notes", project.Notes);
            command.Parameters.AddWithValue("$is_active", project.IsActive ? 1 : 0);
            command.ExecuteNonQuery();
        }
    }

    private static void SaveWeChatGroupChats(SqliteConnection connection, SqliteTransaction transaction, IReadOnlyList<WeChatGroupChatProfile> groupChats)
    {
        foreach (var groupChat in groupChats)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO wechat_group_chats(id, account_id, account_display_name, wechat_username, display_name, last_message_at, company_id, project_id, is_active, last_synced_at)
                VALUES($id, $account_id, $account_display_name, $wechat_username, $display_name, $last_message_at, $company_id, $project_id, $is_active, $last_synced_at);
                """;
            command.Parameters.AddWithValue("$id", groupChat.Id);
            command.Parameters.AddWithValue("$account_id", groupChat.AccountId);
            command.Parameters.AddWithValue("$account_display_name", groupChat.AccountDisplayName);
            command.Parameters.AddWithValue("$wechat_username", groupChat.WeChatUsername);
            command.Parameters.AddWithValue("$display_name", groupChat.DisplayName);
            command.Parameters.AddWithValue("$last_message_at", groupChat.LastMessageAt.HasValue ? FormatDateTimeOffset(groupChat.LastMessageAt.Value) : DBNull.Value);
            command.Parameters.AddWithValue("$company_id", (object?)groupChat.CompanyId ?? DBNull.Value);
            command.Parameters.AddWithValue("$project_id", (object?)groupChat.ProjectId ?? DBNull.Value);
            command.Parameters.AddWithValue("$is_active", groupChat.IsActive ? 1 : 0);
            command.Parameters.AddWithValue("$last_synced_at", FormatDateTimeOffset(groupChat.LastSyncedAt));
            command.ExecuteNonQuery();
        }
    }

    private static void SaveRelationshipLinks(SqliteConnection connection, SqliteTransaction transaction, IReadOnlyList<RelationshipLink> relationshipLinks)
    {
        foreach (var link in relationshipLinks)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO relationship_links(id, source_entity_id, source_entity_kind, target_entity_id, target_entity_kind, link_type, department_name, title, is_primary, notes)
                VALUES($id, $source_entity_id, $source_entity_kind, $target_entity_id, $target_entity_kind, $link_type, $department_name, $title, $is_primary, $notes);
                """;
            command.Parameters.AddWithValue("$id", link.Id);
            command.Parameters.AddWithValue("$source_entity_id", link.SourceEntityId);
            command.Parameters.AddWithValue("$source_entity_kind", (int)link.SourceEntityKind);
            command.Parameters.AddWithValue("$target_entity_id", link.TargetEntityId);
            command.Parameters.AddWithValue("$target_entity_kind", (int)link.TargetEntityKind);
            command.Parameters.AddWithValue("$link_type", (int)link.LinkType);
            command.Parameters.AddWithValue("$department_name", link.DepartmentName);
            command.Parameters.AddWithValue("$title", link.Title);
            command.Parameters.AddWithValue("$is_primary", link.IsPrimary ? 1 : 0);
            command.Parameters.AddWithValue("$notes", link.Notes);
            command.ExecuteNonQuery();
        }
    }

    private static void SaveTodoEntries(SqliteConnection connection, SqliteTransaction transaction, IReadOnlyList<TodoEntry> todoEntries)
    {
        foreach (var todoEntry in todoEntries)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO todo_entries(id, work_date, project_name, title, content, status, work_hours, is_daily, is_from_scratch_note, source_text, sort_order, notes)
                VALUES($id, $work_date, $project_name, $title, $content, $status, $work_hours, $is_daily, $is_from_scratch_note, $source_text, $sort_order, $notes);
                """;
            command.Parameters.AddWithValue("$id", todoEntry.Id);
            command.Parameters.AddWithValue("$work_date", (object?)FormatDateOnly(todoEntry.WorkDate) ?? DBNull.Value);
            command.Parameters.AddWithValue("$project_name", todoEntry.ProjectName);
            command.Parameters.AddWithValue("$title", todoEntry.Title);
            command.Parameters.AddWithValue("$content", todoEntry.Content);
            command.Parameters.AddWithValue("$status", (int)todoEntry.Status);
            command.Parameters.AddWithValue("$work_hours", todoEntry.WorkHours.HasValue ? Convert.ToDouble(todoEntry.WorkHours.Value, CultureInfo.InvariantCulture) : DBNull.Value);
            command.Parameters.AddWithValue("$is_daily", todoEntry.IsDaily ? 1 : 0);
            command.Parameters.AddWithValue("$is_from_scratch_note", todoEntry.IsFromScratchNote ? 1 : 0);
            command.Parameters.AddWithValue("$source_text", todoEntry.SourceText);
            command.Parameters.AddWithValue("$sort_order", todoEntry.SortOrder);
            command.Parameters.AddWithValue("$notes", todoEntry.Notes);
            command.ExecuteNonQuery();
        }
    }

    private static void SaveClientIssues(SqliteConnection connection, SqliteTransaction transaction, IReadOnlyList<ClientIssueEntry> clientIssues)
    {
        foreach (var clientIssue in clientIssues)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO client_issue_entries(id, work_date, project_name, client_name, issue_lines_json, solution_lines_json, notes)
                VALUES($id, $work_date, $project_name, $client_name, $issue_lines_json, $solution_lines_json, $notes);
                """;
            command.Parameters.AddWithValue("$id", clientIssue.Id);
            command.Parameters.AddWithValue("$work_date", (object?)FormatDateOnly(clientIssue.WorkDate) ?? DBNull.Value);
            command.Parameters.AddWithValue("$project_name", clientIssue.ProjectName);
            command.Parameters.AddWithValue("$client_name", clientIssue.ClientName);
            command.Parameters.AddWithValue("$issue_lines_json", SerializeJson(clientIssue.IssueLines));
            command.Parameters.AddWithValue("$solution_lines_json", SerializeJson(clientIssue.SolutionLines));
            command.Parameters.AddWithValue("$notes", clientIssue.Notes);
            command.ExecuteNonQuery();
        }
    }

    private static void SaveDailyReports(SqliteConnection connection, SqliteTransaction transaction, IReadOnlyList<DailyReport> dailyReports)
    {
        foreach (var dailyReport in dailyReports)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO daily_reports(work_date, items_json, client_issues_json, total_hours, raw_scratch_text, formatted_report_text)
                VALUES($work_date, $items_json, $client_issues_json, $total_hours, $raw_scratch_text, $formatted_report_text);
                """;
            command.Parameters.AddWithValue("$work_date", FormatDateOnly(dailyReport.Date));
            command.Parameters.AddWithValue("$items_json", SerializeJson(dailyReport.Items));
            command.Parameters.AddWithValue("$client_issues_json", SerializeJson(dailyReport.ClientIssues));
            command.Parameters.AddWithValue("$total_hours", Convert.ToDouble(dailyReport.TotalHours, CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$raw_scratch_text", dailyReport.RawScratchText);
            command.Parameters.AddWithValue("$formatted_report_text", dailyReport.FormattedReportText);
            command.ExecuteNonQuery();
        }
    }

    private static void SaveScratchNoteDocuments(SqliteConnection connection, SqliteTransaction transaction, IReadOnlyList<ScratchNoteDocument> scratchNoteDocuments)
    {
        foreach (var document in scratchNoteDocuments)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO scratch_note_documents(id, work_date, title, raw_text, created_at_utc, updated_at_utc, last_arranged_at_utc)
                VALUES($id, $work_date, $title, $raw_text, $created_at_utc, $updated_at_utc, $last_arranged_at_utc);
                """;
            command.Parameters.AddWithValue("$id", document.Id);
            command.Parameters.AddWithValue("$work_date", (object?)FormatDateOnly(document.WorkDate) ?? DBNull.Value);
            command.Parameters.AddWithValue("$title", document.Title);
            command.Parameters.AddWithValue("$raw_text", document.RawText);
            command.Parameters.AddWithValue("$created_at_utc", FormatDateTimeOffset(document.CreatedAtUtc));
            command.Parameters.AddWithValue("$updated_at_utc", FormatDateTimeOffset(document.UpdatedAtUtc));
            command.Parameters.AddWithValue("$last_arranged_at_utc", document.LastArrangedAtUtc.HasValue ? FormatDateTimeOffset(document.LastArrangedAtUtc.Value) : DBNull.Value);
            command.ExecuteNonQuery();
        }
    }

    private static string SerializeStringList(IReadOnlyList<string> items)
        => JsonSerializer.Serialize(items, JsonOptions);

    private static List<string> DeserializeStringList(string json)
        => string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? [];

    private static string SerializeJson<T>(T value)
        => JsonSerializer.Serialize(value, JsonOptions);

    private static T? DeserializeJson<T>(string json)
        => string.IsNullOrWhiteSpace(json)
            ? default
            : JsonSerializer.Deserialize<T>(json, JsonOptions);

    private static string FormatDateOnly(DateOnly value)
        => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string? FormatDateOnly(DateOnly? value)
        => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateOnly ParseDateOnly(string value)
        => DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string FormatDateTimeOffset(DateTimeOffset value)
        => value.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseDateTimeOffset(string value)
        => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
