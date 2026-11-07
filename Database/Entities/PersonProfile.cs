namespace Database.Entities;

public class PersonProfile
{
    public Guid PersonId { get; set; }
    public Person Person { get; set; } = null!;
    public Guid ProfileId { get; set; }
    public Profile Profile { get; set; } = null!;
}
