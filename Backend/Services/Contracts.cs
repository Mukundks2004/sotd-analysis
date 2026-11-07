namespace Sotd.Backend.Services;

public record TagDto(Guid Id, string Name);

/// <summary>One row of the annotation screen: the thing being annotated, plus its current tags in order.</summary>
public record AnnotationRow(Guid Id, string Label, string? Detail, IReadOnlyList<TagDto> Tags);

/// <summary>Which relationships can be edited, and how the UI should label them.</summary>
public record RelationshipInfo(string Key, string Label, string Owner, string Tag, bool Ordered, bool Big);

public class AddTagRequest
{
    public Guid OwnerId { get; set; }

    /// <summary>An existing tag picked from the dropdown.</summary>
    public Guid? TagId { get; set; }

    /// <summary>A brand new tag typed into the search box ("add X"). Creates the row before linking it.</summary>
    public string? NewName { get; set; }
}

public class ReorderRequest
{
    public Guid SongId { get; set; }

    /// <summary>The artist IDs in their new credited order; positions are renumbered from 1.</summary>
    public IReadOnlyList<Guid> ArtistIds { get; set; } = Array.Empty<Guid>();
}

public class DeleteRequest
{
    public IReadOnlyList<Guid> Ids { get; set; } = Array.Empty<Guid>();
}

public record GenreNode(Guid Id, string Name);

/// <summary>A parent-to-child arrow. A genre has at most one parent, so a child appears at most once.</summary>
public record GenreEdge(Guid ParentId, Guid ChildId);

public record GenreGraph(IReadOnlyList<GenreNode> Nodes, IReadOnlyList<GenreEdge> Edges);

public class SetParentRequest
{
    public Guid ChildId { get; set; }
    public Guid ParentId { get; set; }
}

public class CreateGenreRequest
{
    public string Name { get; set; } = "";
}
