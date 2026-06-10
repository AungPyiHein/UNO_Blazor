using UnoApp.Server.Hubs;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();

// CORS: in production set AllowedOrigins env var (comma-separated).
// In development, allow any origin so Replit's proxy domain works.
var allowedOrigins = builder.Configuration["AllowedOrigins"]?.Split(',');

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (allowedOrigins?.Length > 0)
            policy.WithOrigins(allowedOrigins);
        else
            policy.SetIsOriginAllowed(_ => true); // dev: allow Replit proxy domain

        policy.AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

builder.WebHost.UseUrls("http://0.0.0.0:5000");

var app = builder.Build();

app.UseCors();

// Serve the Blazor WebAssembly client files (built output from UnoApp project)
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

// ── Routes ──────────────────────────────────────────────────────────────────
app.MapHub<GameHub>("/gamehub");

// Fall back to the Blazor shell for all other routes
app.MapFallbackToFile("index.html");

app.Run();
