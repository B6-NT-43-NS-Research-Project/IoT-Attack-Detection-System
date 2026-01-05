using IDSServer.Hubs;
using IDSServer.Services; // Add this

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddSignalR();

// --- NEW: Register the Analyzer as a Singleton "Brain" ---
builder.Services.AddSingleton<ITrafficAnalyzer, TrafficAnalyzer>();
// --------------------------------------------------------

var app = builder.Build();

// ... rest of your code (MapHub, Run, etc.) ...
app.MapHub<IdsHub>("/idshub");
app.Run();