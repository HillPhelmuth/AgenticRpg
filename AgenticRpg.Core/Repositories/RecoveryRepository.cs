using System.Net;
using System.Text.Json;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Enums;
using AgenticRpg.Core.Messaging;

namespace AgenticRpg.Core.Repositories;

public sealed class RecoveryDocument
{
    [System.Text.Json.Serialization.JsonPropertyName("id"), JsonProperty("id")] public string Id { get; set; } = "";
    [System.Text.Json.Serialization.JsonPropertyName("scopeId"), JsonProperty("scopeId")] public string ScopeId { get; set; } = "";
    public string? ClientMessageId { get; set; }
    public bool IsTerminal { get; set; }
    public bool NeedsReview { get; set; }
    public int Visibility { get; set; }
    public string? TargetCharacterId { get; set; }
    public string? OrderKey { get; set; }
    public void IndexMessage()
    {
        if (Kind != "message") return;
        var m = Read<RuntimeMessage>();
        ClientMessageId = m.ClientMessageId; IsTerminal = m.IsComplete;
        NeedsReview = m.NeedsReview;
        Visibility = (int)m.Visibility; TargetCharacterId = m.TargetCharacterId;
        OrderKey = $"{m.Timestamp.Ticks:D19}|{m.Id}";
    }
    public int SchemaVersion { get; set; } = 1;
    public string OwnerId { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Payload { get; set; } = "";
    public long Version { get; set; }
    public DateTime UpdatedAt { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("_etag"), JsonProperty("_etag")] public string? ETag { get; set; }
    public T Read<T>() => System.Text.Json.JsonSerializer.Deserialize<T>(Payload, RecoveryJson.Options)!;
}

public static class RecoveryJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

public interface IRecoveryRepository
{
    Task<RecoveryDocument?> GetAsync(string scopeId, string id);
    Task WriteAsync(RecoveryDocument document, long expectedVersion);
    Task<List<RecoveryDocument>> ListAsync(string? scopeId = null, string? kind = null, string? ownerId = null);
    Task DeleteScopeAsync(string scopeId);
    async Task<List<RecoveryDocument>> RecoverMessagesAsync(string scopeId) => (await ListAsync(scopeId, "message"))
        .Where(d => !d.Read<RuntimeMessage>().IsComplete).Concat((await ListAsync(scopeId, "message"))
        .OrderByDescending(d => d.Read<RuntimeMessage>().Timestamp).ThenByDescending(d => d.Id).Take(100)).DistinctBy(d => d.Id).ToList();
    async Task<RecoveryDocument?> FindRequestAsync(string scopeId, string playerId, string clientId) =>
        (await ListAsync(scopeId, "message", playerId)).FirstOrDefault(d => d.Read<RuntimeMessage>().ClientMessageId == clientId);
    async Task<List<RecoveryDocument>> ReviewMessagesAsync(string scopeId, string playerId) =>
        (await ListAsync(scopeId, "message", playerId)).Where(d => d.Read<RuntimeMessage>().NeedsReview).ToList();
    async Task<TranscriptPage> TranscriptAsync(string scopeId, string? cursor, int take, string? characterId)
    {
        var messages = (await ListAsync(scopeId, "message")).Select(d => d.Read<RuntimeMessage>())
            .Where(m => m.Visibility == NarrativeVisibility.Global || m.Visibility == NarrativeVisibility.CharacterSpecific && characterId is not null && m.TargetCharacterId == characterId)
            .OrderByDescending(m => m.Timestamp).ThenByDescending(m => m.Id, StringComparer.Ordinal).ToList();
        var offset = cursor is null ? 0 : messages.FindIndex(m => m.Id == cursor) + 1;
        if (cursor is not null && offset == 0) throw new ArgumentException("Invalid transcript cursor.");
        var page = messages.Skip(offset).Take(Math.Clamp(take, 1, 100)).ToList();
        return new() { Messages = page.AsEnumerable().Reverse().ToList(), Cursor = offset + page.Count < messages.Count ? page.LastOrDefault()?.Id : null };
    }
    async Task<(long Revision, DateTime? SavedAt)> InfoAsync(string scopeId)
    { var rows = await ListAsync(scopeId); return (rows.Sum(d => d.Version), rows.Count == 0 ? null : rows.Max(d => d.UpdatedAt)); }

}

/// <summary>Conditional writes protect checkpoints from stale tabs and delayed stream events.</summary>
public sealed class RecoveryRepository(CosmosClient client) : IRecoveryRepository
{
    private readonly Container _container = client.GetDatabase("AgenticRpgDb")
        .CreateContainerIfNotExistsAsync("ScopeRecovery", "/scopeId").GetAwaiter().GetResult().Container;
    public async Task<RecoveryDocument?> GetAsync(string scopeId, string id)
    {
        try { return (await _container.ReadItemAsync<RecoveryDocument>(id, new PartitionKey(scopeId))).Resource; }
        catch (CosmosException e) when (e.StatusCode == HttpStatusCode.NotFound) { return null; }
    }
    public async Task WriteAsync(RecoveryDocument document, long expectedVersion)
    {
        document.IndexMessage();
        var current = await GetAsync(document.ScopeId, document.Id);
        if ((current?.Version ?? 0) != expectedVersion) throw new RecoveryConflictException();
        try
        {
            if (current is null) await _container.CreateItemAsync(document, new PartitionKey(document.ScopeId));
            else await _container.ReplaceItemAsync(document, document.Id, new PartitionKey(document.ScopeId),
                new ItemRequestOptions { IfMatchEtag = current.ETag });
        }
        catch (CosmosException e) when (e.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed)
        { throw new RecoveryConflictException(); }
    }
    public async Task<List<RecoveryDocument>> ListAsync(string? scopeId = null, string? kind = null, string? ownerId = null)
    {
        var q = new QueryDefinition("SELECT * FROM c WHERE (@scope = null OR c.scopeId = @scope) AND (@kind = null OR c.Kind = @kind) AND (@owner = null OR c.OwnerId = @owner)")
            .WithParameter("@scope", scopeId).WithParameter("@kind", kind).WithParameter("@owner", ownerId);
        using var iterator = _container.GetItemQueryIterator<RecoveryDocument>(q, requestOptions:
            scopeId is null ? null : new QueryRequestOptions { PartitionKey = new PartitionKey(scopeId) });
        List<RecoveryDocument> result = [];
        while (iterator.HasMoreResults) result.AddRange(await iterator.ReadNextAsync());
        return result;
    }
    private async Task<List<RecoveryDocument>> QueryAsync(string scopeId, QueryDefinition query)
    {
        using var iterator = _container.GetItemQueryIterator<RecoveryDocument>(query.WithParameter("@scope", scopeId),
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(scopeId) });
        List<RecoveryDocument> result = [];
        while (iterator.HasMoreResults) result.AddRange(await iterator.ReadNextAsync());
        return result;
    }
    public async Task<List<RecoveryDocument>> RecoverMessagesAsync(string scopeId)
    {
        var active = await QueryAsync(scopeId, new("SELECT * FROM c WHERE c.scopeId = @scope AND c.Kind = 'message' AND c.IsTerminal = false"));
        var recent = await QueryAsync(scopeId, new("SELECT TOP 100 * FROM c WHERE c.scopeId = @scope AND c.Kind = 'message' ORDER BY c.OrderKey DESC"));
        return active.Concat(recent).DistinctBy(d => d.Id).ToList();
    }
    public async Task<RecoveryDocument?> FindRequestAsync(string scopeId, string playerId, string clientId) =>
        (await QueryAsync(scopeId, new QueryDefinition("SELECT TOP 1 * FROM c WHERE c.scopeId = @scope AND c.Kind = 'message' AND c.OwnerId = @player AND c.ClientMessageId = @client")
            .WithParameter("@player", playerId).WithParameter("@client", clientId))).FirstOrDefault();
    public Task<List<RecoveryDocument>> ReviewMessagesAsync(string scopeId, string playerId) => QueryAsync(scopeId,
        new QueryDefinition("SELECT * FROM c WHERE c.scopeId = @scope AND c.Kind = 'message' AND c.OwnerId = @player AND c.NeedsReview = true").WithParameter("@player", playerId));
    public async Task<TranscriptPage> TranscriptAsync(string scopeId, string? cursor, int take, string? characterId)
    {
        string? before = null;
        if (cursor is not null)
        {
            var d = await GetAsync(scopeId, cursor);
            if (d?.Kind != "message" || !(d.Visibility == (int)NarrativeVisibility.Global || d.Visibility == (int)NarrativeVisibility.CharacterSpecific && characterId is not null && d.TargetCharacterId == characterId))
                throw new ArgumentException("Invalid transcript cursor.");
            before = d.OrderKey;
        }
        take = Math.Clamp(take, 1, 100);
        var rows = await QueryAsync(scopeId, new QueryDefinition("SELECT TOP @take * FROM c WHERE c.scopeId = @scope AND c.Kind = 'message' AND (@before = null OR c.OrderKey < @before) AND (c.Visibility = 0 OR (c.Visibility = 2 AND @character != null AND c.TargetCharacterId = @character)) ORDER BY c.OrderKey DESC")
            .WithParameter("@take", take + 1).WithParameter("@before", before).WithParameter("@character", characterId));
        var messages = rows.Take(take).Select(d => d.Read<RuntimeMessage>()).ToList();
        return new() { Messages = messages.AsEnumerable().Reverse().ToList(), Cursor = rows.Count > take ? messages.LastOrDefault()?.Id : null };
    }
    public async Task<(long Revision, DateTime? SavedAt)> InfoAsync(string scopeId)
    {
        // Cosmos rejects aggregates composed inside a SELECT VALUE object.
        var revision = await ScalarAsync<long?>(scopeId, "SELECT VALUE SUM(c.Version) FROM c WHERE c.scopeId = @scope");
        var savedAt = await ScalarAsync<DateTime?>(scopeId, "SELECT VALUE MAX(c.UpdatedAt) FROM c WHERE c.scopeId = @scope");
        return (revision ?? 0, savedAt);
    }
    private async Task<T?> ScalarAsync<T>(string scopeId, string query)
    {
        using var iterator = _container.GetItemQueryIterator<T>(new QueryDefinition(query).WithParameter("@scope", scopeId),
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(scopeId) });
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync();
            if (page.Count > 0) return page.First();
        }
        return default;
    }
    public async Task DeleteScopeAsync(string scopeId)
    {
        foreach (var d in await ListAsync(scopeId)) await _container.DeleteItemAsync<RecoveryDocument>(d.Id, new PartitionKey(scopeId));
    }
}
public sealed class RecoveryConflictException() : InvalidOperationException("The saved draft has changed. Reload it before saving again.");

/// <summary>Used by deterministic tests and hosts without durable storage.</summary>
public sealed class InMemoryRecoveryRepository : IRecoveryRepository
{
    private readonly Dictionary<(string, string), RecoveryDocument> _documents = [];
    private readonly object _sync = new();
    private static RecoveryDocument Copy(RecoveryDocument d) => System.Text.Json.JsonSerializer.Deserialize<RecoveryDocument>(System.Text.Json.JsonSerializer.Serialize(d))!;
    public Task<RecoveryDocument?> GetAsync(string scopeId, string id)
    { lock (_sync) return Task.FromResult(_documents.TryGetValue((scopeId, id), out var d) ? Copy(d) : null); }
    public Task WriteAsync(RecoveryDocument d, long expectedVersion)
    {
        lock (_sync)
        {
            if ((_documents.GetValueOrDefault((d.ScopeId, d.Id))?.Version ?? 0) != expectedVersion) throw new RecoveryConflictException();
            d.IndexMessage();
            _documents[(d.ScopeId, d.Id)] = Copy(d);
        }
        return Task.CompletedTask;
    }
    public Task<List<RecoveryDocument>> ListAsync(string? scopeId = null, string? kind = null, string? ownerId = null)
    { lock (_sync) return Task.FromResult(_documents.Values.Where(d => (scopeId is null || d.ScopeId == scopeId) && (kind is null || d.Kind == kind) && (ownerId is null || d.OwnerId == ownerId)).Select(Copy).ToList()); }
    public Task DeleteScopeAsync(string scopeId)
    { lock (_sync) foreach (var key in _documents.Keys.Where(k => k.Item1 == scopeId).ToList()) _documents.Remove(key); return Task.CompletedTask; }
}
