using MeetingScribe.Web.Services;
using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.Configure<VideoProcessingOptions>(
    builder.Configuration.GetSection("Processing"));
builder.Services.AddSingleton<ProcessingProgressTracker>();
builder.Services.AddScoped<VideoProcessingService>();
builder.Services.Configure<FormOptions>(options =>
{
    const long maxUploadBytes = 2L * 1024 * 1024 * 1024; // 2 GB
    options.MultipartBodyLengthLimit = maxUploadBytes;
});

builder.WebHost.ConfigureKestrel(options =>
{
    const long maxUploadBytes = 2L * 1024 * 1024 * 1024; // 2 GB
    options.Limits.MaxRequestBodySize = maxUploadBytes;
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.MapRazorPages();

app.MapGet("/api/progress/{operationId}", (string operationId, ProcessingProgressTracker tracker) =>
    tracker.GetSnapshot(operationId) is { } snapshot
        ? Results.Ok(snapshot)
        : Results.NotFound());

app.Run();
