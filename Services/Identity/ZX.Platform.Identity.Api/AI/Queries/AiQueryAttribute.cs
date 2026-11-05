namespace ZX.Platform.Identity.Api.AI.Queries;

// The developer confirms this action is a read-only business query.
// This marker exposes no permission by itself; normal endpoint authorization still applies.
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class AiQueryAttribute(string? description = null) : Attribute
{
    public string? Description { get; } = description;

    // Only opt in after confirming the original endpoint returns the whole visible collection.
    // Ordinary { list, total } responses need no extra metadata.
    public AiQueryResultShape ResultShape { get; set; } = AiQueryResultShape.Automatic;
}

public enum AiQueryResultShape
{
    Automatic,
    CompleteList,
    CompleteTree
}
