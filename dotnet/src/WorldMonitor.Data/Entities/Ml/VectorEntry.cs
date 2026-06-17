namespace WorldMonitor.Data.Entities.Ml;

/// <summary>A stored headline embedding for semantic search. Id is a content hash (natural key) so
/// re-ingest is idempotent. Embedding is a Float32[384] serialized to VARBINARY(1536).</summary>
public sealed class VectorEntry
{
    public required string Id { get; set; }
    public required string Text { get; set; }        // nvarchar(200)
    public required byte[] Embedding { get; set; }    // varbinary(1536) = Float32[384]
    public DateTime? PubDate { get; set; }
    public DateTime IngestedAt { get; set; }
    public string? Source { get; set; }
    public string? Url { get; set; }
    public List<string> Tags { get; set; } = [];      // JSON
}
