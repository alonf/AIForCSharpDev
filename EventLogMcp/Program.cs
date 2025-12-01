using EventLogMcp.Services;
using EventLogMcp.Tools;

var builder = WebApplication.CreateBuilder(args);

// Fixed URL for the MCP server
builder.WebHost.UseUrls("http://localhost:5115");

// Log everything to stderr (common for MCP servers)
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

// Services
builder.Services.AddSingleton<WindowsEventLogReader>();

// Configure MCP server with HTTP transport and register tool classes
builder.Services
    .AddMcpServer()
    .WithHttpTransport(o => o.Stateless = true)
    .WithTools<EventLogTools>();

var app = builder.Build();

// Map MCP endpoint
app.MapMcp("/mcp");

app.Run();
