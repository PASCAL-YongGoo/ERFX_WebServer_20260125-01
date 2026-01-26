using ErfxWebServer.Data;
using ErfxWebServer.Hubs;
using ErfxWebServer.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

EnsureDatabaseDirectoryExists(builder.Configuration);

builder.Services.AddDbContext<InspectionDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("InspectionDb")));

// Register services
builder.Services.AddScoped<IInspectionService, InspectionService>();

// Add MQTT client as hosted service
builder.Services.AddSingleton<MqttClientService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MqttClientService>());

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Add Blazor Server services
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

// Add SignalR
builder.Services.AddSignalR();

var app = builder.Build();

await InitializeDatabaseAsync(app);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseCors();

app.UseAuthorization();

app.MapBlazorHub();
app.MapHub<InspectionHub>("/inspectionhub");
app.MapFallbackToPage("/_Host");

app.MapControllers();

app.Run();

static void EnsureDatabaseDirectoryExists(IConfiguration configuration)
{
    var connectionString = configuration.GetConnectionString("InspectionDb");
    if (string.IsNullOrEmpty(connectionString)) return;

    try
    {
        // Use SqliteConnectionStringBuilder for safe parsing
        var builder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(connectionString);
        var dbPath = builder.DataSource;

        if (string.IsNullOrEmpty(dbPath)) return;

        // Convert to absolute path if relative
        if (!Path.IsPathRooted(dbPath))
        {
            dbPath = Path.GetFullPath(dbPath);
        }

        var directory = Path.GetDirectoryName(dbPath);

        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
            Console.WriteLine($"Created database directory: {directory}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Warning: Could not create database directory: {ex.Message}");
    }
}

static async Task InitializeDatabaseAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<InspectionDbContext>();
    await context.Database.EnsureCreatedAsync();
}
