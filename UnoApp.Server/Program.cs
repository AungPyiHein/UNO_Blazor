using UnoApp.Server.Hubs;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();

var app = builder.Build();

// Serve the Blazor WebAssembly client files (built output from UnoApp project)
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

// ── Routes ──────────────────────────────────────────────────────────────────
app.MapHub<GameHub>("/gamehub");

// Fall back to the Blazor shell for all other routes
app.MapFallbackToFile("index.html");

app.Run();
