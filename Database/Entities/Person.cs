using Sotd.Database;
namespace Database.Entities;

public class Person
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public List<PersonProfile> PersonProfiles { get; set; } = [];
}
