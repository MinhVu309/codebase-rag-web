namespace Domain.Enums;

/// <summary>Mirrors the RAG contract v0.2 index status values.</summary>
public enum IndexStatus
{
    Pending,
    Cloning,
    Parsing,
    Embedding,
    Ready,
    Failed,
}
