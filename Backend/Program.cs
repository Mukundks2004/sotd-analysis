using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Sotd.Backend.Api;
using Sotd.Database;

var builder = WebApplication.CreateBuilder(args);

// The database is the same sqlite file the importer writes. The path is taken relative to the repo root so
// that the committed appsettings works from a plain `dotnet run` in the Backend folder.
var repoRoot = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, ".."));
var dbPath = Path.GetFullPath(builder.Configuration["Database:Path"] ?? "sotd.db", repoRoot);
builder.Services.AddDbContext<SotdDbContext>(options => options.UseSqlite($"Data Source={dbPath}"));

builder.Services.AddControllers().AddJsonOptions(o =>
    o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// This is a local tool with no authentication, so the dev frontend on its own port just needs to be let in.
const string DevCors = "frontend";
var allowedOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
                     ?? new[] { "http://localhost:5173", "http://127.0.0.1:5173" };
builder.Services.AddCors(o =>
    o.AddPolicy(DevCors, p => p.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// Every unhandled failure still has to come back in the shape the UI's toast understands.
app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    context.Response.StatusCode = error is BadHttpRequestException
        ? StatusCodes.Status400BadRequest
        : StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(new ApiError(error?.Message ?? "Something went wrong."));
}));

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors(DevCors);

// Profile pictures, emojis and attachments are stored in the database as paths under Download/, so the UI
// loads them from here rather than the API handing back base64.
var downloads = Path.Combine(repoRoot, "Download");
Directory.CreateDirectory(downloads);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(downloads),
    RequestPath = "/files",
    ServeUnknownFileTypes = true,
});

app.MapControllers();

// A little context for the UI header, and a cheap way to confirm the API is up and pointed at the right file.
app.MapGet("/api/health", async (SotdDbContext db) => Results.Ok(new
{
    database = dbPath,
    exists = File.Exists(dbPath),
    songs = await db.Songs.CountAsync(),
    entries = await db.SotdEntries.CountAsync(),
}));

app.Run();
