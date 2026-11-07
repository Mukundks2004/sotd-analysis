using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Sotd.Backend.Api;
using Sotd.Backend.Dtos;
using Sotd.Backend.Services;

namespace Backend.Tests;

/// <summary>
/// The single place a service outcome becomes HTTP. Everything else in the backend is deliberately free of
/// ASP.NET types, so this mapping is the only thing that has to be right about status codes.
/// </summary>
[TestFixture]
public class ServiceResultsTests
{
    [Test]
    public void AValueComesBackAsTwoHundredWithTheValueItself()
    {
        var dto = new PersonDto(Guid.NewGuid(), "Alice");

        var result = (OkObjectResult)ServiceResult<PersonDto>.Ok(dto).ToActionResult().Result!;

        Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
        Assert.That(result.Value, Is.SameAs(dto));
    }

    [Test]
    public void AnEmptySuccessComesBackAsTwoHundredAndFour()
    {
        var result = ServiceResult<PersonDto>.NoContent().ToActionResult().Result!;

        Assert.That(result, Is.TypeOf<NoContentResult>());
    }

    [Test]
    public void EveryFailureCarriesAMessageTheUiCanShowInItsToast()
    {
        var notFound = (ObjectResult)ServiceResult<PersonDto>.NotFound("gone").ToActionResult().Result!;
        var conflict = (ObjectResult)ServiceResult<PersonDto>.Conflict("taken", "UNIQUE constraint failed").ToActionResult().Result!;
        var bad = (ObjectResult)ServiceResult<PersonDto>.BadRequest("nope").ToActionResult().Result!;

        Assert.That(notFound.StatusCode, Is.EqualTo(StatusCodes.Status404NotFound));
        Assert.That(((ApiError)notFound.Value!).Message, Is.EqualTo("gone"));

        Assert.That(conflict.StatusCode, Is.EqualTo(StatusCodes.Status409Conflict));
        Assert.That(((ApiError)conflict.Value!).Detail, Is.EqualTo("UNIQUE constraint failed"));

        Assert.That(bad.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
        Assert.That(((ApiError)bad.Value!).Message, Is.EqualTo("nope"));
    }

    [Test]
    public void AFailureWithNoMessageStillSaysSomething()
    {
        var result = (ObjectResult)new ServiceResult<PersonDto>(ResultStatus.NotFound, null).ToActionResult().Result!;

        Assert.That(((ApiError)result.Value!).Message, Is.Not.Empty);
    }
}

/// <summary>Discord IDs are bigger than a JavaScript number, so they travel as strings.</summary>
[TestFixture]
public class SnowflakeTests
{
    [Test]
    public void ASnowflakeTooBigForJavascriptSurvivesTheRoundTrip()
    {
        const string id = "9007199254740993";

        Assert.That(Snowflake.Parse(id, "Discord ID"), Is.EqualTo(9007199254740993L));
    }

    [Test]
    public void SurroundingSpaceIsIgnored() =>
        Assert.That(Snowflake.Parse("  123  ", "Discord ID"), Is.EqualTo(123L));

    [TestCase("")]
    [TestCase(null)]
    [TestCase("not a number")]
    [TestCase("12.5")]
    public void AnythingThatIsNotAWholeNumberIsARequestProblem(string? value) =>
        Assert.That(() => Snowflake.Parse(value, "Discord ID"),
            Throws.TypeOf<BadHttpRequestException>().With.Message.Contains("Discord ID"));

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void AnOptionalSnowflakeMayBeAbsent(string? value) =>
        Assert.That(Snowflake.ParseOptional(value, "Attachment ID"), Is.Null);

    [Test]
    public void AnOptionalSnowflakeThatIsPresentIsStillValidated()
    {
        Assert.That(Snowflake.ParseOptional("123", "Attachment ID"), Is.EqualTo(123L));
        Assert.That(() => Snowflake.ParseOptional("nope", "Attachment ID"), Throws.TypeOf<BadHttpRequestException>());
    }
}
