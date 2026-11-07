namespace Database.Entities;

public class Genre
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public Guid? ParentId { get; set; }
    public Genre? Parent { get; set; }
}
