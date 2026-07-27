using System.Globalization;
using Dapper;
using Harness.DataAccess.Configuration;
using Harness.DataAccess.Models;
using Microsoft.Data.Sqlite;

namespace Harness.DataAccess.Conversations;

internal sealed class SqliteConversationStore(IApplicationPaths applicationPaths)
    : IConversationStore
{
    public async ValueTask<Conversation> GetOrCreateAsync(
        string conversationId,
        string title,
        string model,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        await using SqliteConnection connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        string now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        CommandDefinition insert = new("""
            INSERT INTO conversations (id, title, model, created_at, updated_at)
            VALUES (@conversationId, @title, @model, @now, @now)
            ON CONFLICT(id) DO NOTHING;
            """, new { conversationId, title, model, now }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(insert);

        CommandDefinition select = new("""
            SELECT id, title, model, created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM conversations
            WHERE id = @conversationId;
            """, new { conversationId }, cancellationToken: cancellationToken);
        ConversationRow row = await connection.QuerySingleAsync<ConversationRow>(select);
        return row.ToRecord();
    }

    public async ValueTask<IReadOnlyList<ConversationMessage>> GetMessagesAsync(
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        CommandDefinition command = new("""
            SELECT id,
                   conversation_id AS ConversationId,
                   role,
                   content,
                   status,
                   input_tokens AS InputTokens,
                   output_tokens AS OutputTokens,
                   created_at AS CreatedAt
            FROM conversation_messages
            WHERE conversation_id = @conversationId
            ORDER BY id;
            """, new { conversationId }, cancellationToken: cancellationToken);
        IEnumerable<ConversationMessageRow> rows = await connection
            .QueryAsync<ConversationMessageRow>(command);
        return rows.Select(row => row.ToRecord()).ToArray();
    }

    public async ValueTask<Conversation> UpdateModelAsync(
        string conversationId,
        string model,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        await using SqliteConnection connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        string now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        CommandDefinition command = new("""
            UPDATE conversations
            SET model = @model, updated_at = @now
            WHERE id = @conversationId
            RETURNING id, title, model, created_at AS CreatedAt, updated_at AS UpdatedAt;
            """, new { conversationId, model, now }, cancellationToken: cancellationToken);
        ConversationRow row = await connection.QuerySingleAsync<ConversationRow>(command);
        return row.ToRecord();
    }

    public async ValueTask<ConversationMessage> AppendMessageAsync(
        string conversationId,
        string role,
        string content,
        string status,
        ProviderUsage usage,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        string now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken);
        CommandDefinition insert = new("""
            INSERT INTO conversation_messages (
                conversation_id, role, content, status, input_tokens, output_tokens, created_at)
            VALUES (
                @conversationId, @role, @content, @status, @inputTokens, @outputTokens, @now)
            RETURNING id;
            """, new
        {
            conversationId,
            role,
            content,
            status,
            inputTokens = usage.InputTokens,
            outputTokens = usage.OutputTokens,
            now,
        }, transaction, cancellationToken: cancellationToken);
        long id = await connection.QuerySingleAsync<long>(insert);

        CommandDefinition touch = new("""
            UPDATE conversations SET updated_at = @now WHERE id = @conversationId;
            """, new { now, conversationId }, transaction, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(touch);
        await transaction.CommitAsync(cancellationToken);

        return new(
            id,
            conversationId,
            role,
            content,
            status,
            usage.InputTokens,
            usage.OutputTokens,
            DateTimeOffset.Parse(now, CultureInfo.InvariantCulture));
    }

    private SqliteConnection CreateConnection() => new(new SqliteConnectionStringBuilder
    {
        DataSource = applicationPaths.Current.DatabasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Shared,
        ForeignKeys = true,
    }.ToString());

    private sealed class ConversationRow
    {
        public string Id { get; init; } = string.Empty;

        public string Title { get; init; } = string.Empty;

        public string Model { get; init; } = string.Empty;

        public string CreatedAt { get; init; } = string.Empty;

        public string UpdatedAt { get; init; } = string.Empty;

        internal Conversation ToRecord() => new(
            Id,
            Title,
            Model,
            DateTimeOffset.Parse(CreatedAt, CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(UpdatedAt, CultureInfo.InvariantCulture));
    }

    private sealed class ConversationMessageRow
    {
        public long Id { get; init; }

        public string ConversationId { get; init; } = string.Empty;

        public string Role { get; init; } = string.Empty;

        public string Content { get; init; } = string.Empty;

        public string Status { get; init; } = string.Empty;

        public int InputTokens { get; init; }

        public int OutputTokens { get; init; }

        public string CreatedAt { get; init; } = string.Empty;

        internal ConversationMessage ToRecord() => new(
            Id,
            ConversationId,
            Role,
            Content,
            Status,
            InputTokens,
            OutputTokens,
            DateTimeOffset.Parse(CreatedAt, CultureInfo.InvariantCulture));
    }
}
