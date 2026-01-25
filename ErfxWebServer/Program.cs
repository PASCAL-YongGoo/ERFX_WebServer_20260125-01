using ErfxWebServer.Data;
using ErfxWebServer.Hubs;
using ErfxWebServer.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// Add DbContext with SQLite
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

// Configure the HTTP request pipeline.
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
