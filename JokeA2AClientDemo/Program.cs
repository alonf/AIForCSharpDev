using A2A;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.A2A;
using Microsoft.Extensions.AI;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);

builder.Services.AddHttpClient();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();

// Base URL of the JokeAgentsDemo A2A server
const string jokeAgentBaseUrl = "http://localhost:5000";

/// <summary>
/// Calls a remote A2A agent and collects the full text response.
/// </summary>
async Task<string> CallA2AAgentAsync(string agentPath, string agentName, string message, HttpClient httpClient, ILoggerFactory loggerFactory)
{
    var agentUrl = new Uri($"{jokeAgentBaseUrl}{agentPath}");
    var a2aClient = new A2AClient(agentUrl, httpClient);
    var agent = a2aClient.AsAIAgent(agentName, agentName, description: $"Remote {agentName} via A2A", loggerFactory);

    var messages = new List<ChatMessage>
    {
        new(ChatRole.User, message)
    };

    // Collect the full response from the agent
    var responseText = new System.Text.StringBuilder();
    await foreach (var update in agent.RunStreamingAsync(messages))
    {
        if (!string.IsNullOrEmpty(update.Text))
        {
            responseText.Append(update.Text);
        }
    }

    return responseText.ToString();
}

// POST /api/improve — send joke text to JokeCreator via A2A, get improved joke back
app.MapPost("/api/improve", async (JokeRequest request, IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory) =>
{
    var httpClient = httpClientFactory.CreateClient();
    var result = await CallA2AAgentAsync("/agents/creator", "JokeCreator", request.Text, httpClient, loggerFactory);
    return Results.Ok(new { text = result });
});

// POST /api/critique — send joke text to JokeCritic via A2A, get critique back
app.MapPost("/api/critique", async (JokeRequest request, IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory) =>
{
    var httpClient = httpClientFactory.CreateClient();
    var result = await CallA2AAgentAsync("/agents/critic", "JokeCritic", request.Text, httpClient, loggerFactory);
    return Results.Ok(new { text = result });
});

// Health check
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    timestamp = DateTime.UtcNow,
    description = "A2A Client Demo — calls JokeAgentsDemo agents via A2A protocol",
    remoteAgents = new
    {
        creator = $"{jokeAgentBaseUrl}/agents/creator",
        critic = $"{jokeAgentBaseUrl}/agents/critic"
    }
}));

Console.WriteLine("====================================================");
Console.WriteLine("\U0001F91D Joke A2A Client Demo");
Console.WriteLine("====================================================");
Console.WriteLine();
Console.WriteLine("\u2728 Demonstrates Agent-to-Agent (A2A) protocol:");
Console.WriteLine("   \u2022 Calls JokeCreator agent via A2A to improve jokes");
Console.WriteLine("   \u2022 Calls JokeCritic agent via A2A to critique jokes");
Console.WriteLine("   \u2022 Remote agents run on JokeAgentsDemo (port 5000)");
Console.WriteLine();
Console.WriteLine("\U0001F310 Application: http://localhost:5002");
Console.WriteLine();
Console.WriteLine("\U0001F4A1 Make sure JokeAgentsDemo is running on port 5000!");
Console.WriteLine("====================================================");
Console.WriteLine();

app.Run("http://localhost:5002");

/// <summary>
/// Request body for the improve and critique endpoints.
/// </summary>
record JokeRequest(string Text);
