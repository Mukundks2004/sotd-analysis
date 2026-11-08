using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Database.Converters;

internal class NullableUtcDateTimeConverter : ValueConverter<DateTime?, DateTime?>
{
    public NullableUtcDateTimeConverter() : base(
        value => value == null ? null : (value.Value.Kind == DateTimeKind.Utc ? value : value.Value.ToUniversalTime()),
        value => value == null ? null : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc))
    { }
}