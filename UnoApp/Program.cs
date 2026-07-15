using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using UnoApp;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddSingleton<UnoApp.Services.NavigationState>();
builder.Services.AddSingleton<UnoApp.Services.LobbyService>();
builder.Services.AddScoped<UnoApp.Services.AudioService>();
builder.Services.AddSingleton<UnoApp.Services.SupabaseService>();

var host = builder.Build();

// Initialize Supabase on startup
var supabase = host.Services.GetRequiredService<UnoApp.Services.SupabaseService>();
await supabase.InitializeAsync();

await host.RunAsync();
