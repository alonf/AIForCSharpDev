using Azure.AI.OpenAI;
using Azure.Identity;
using JokeAgentsDemo;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;
using Microsoft.Extensions.AI;
// AG-UI protocol layer


var builder = WebApplication.CreateBuilder(args);

// Configure logging to show A2A communication
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);

// Configure Azure OpenAI
var endpoint = new Uri("https://alonlecturedemo-resource.cognitiveservices.azure.com/");
var credential = new DefaultAzureCredential();
string deploymentName = "model-router";

// Create the chat client and wrap it as IChatClient
var azureClient = new AzureOpenAIClient(endpoint, credential);
var openAIChatClient = azureClient.GetChatClient(deploymentName);
IChatClient chatClient = openAIChatClient.AsIChatClient();

// Register services
builder.Services.AddSingleton(chatClient);
builder.Services.AddHttpClient(); // For A2A communication

// Add CORS for web UI
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

// Serve static files from wwwroot
app.UseDefaultFiles(); // Serves index.html by default
app.UseStaticFiles();

// ============================================================================
// CREATE AGENTS
// ============================================================================

// Create JokeCreator Agent  
ChatClientAgent creatorAgent = new(chatClient,
    instructions: @"You are a creative comedian named JokeCreator.

Your task is to create original, funny jokes and improve them based on feedback.

CRITICAL REQUIREMENTS:
- Create ONE single joke, not multiple options or variations
- The joke must be conversational and sound natural when spoken aloud
- It should flow like something a comedian would say on stage
- NO list formats (avoid ""here are some options"", ""pick from these"")
- NO meta-commentary about joke structure or types
- The joke should be ready to perform immediately

When creating jokes:
- Be original and creative
- Use wordplay, surprise, or relatable situations
- Keep jokes appropriate and culturally sensitive
- Aim for jokes that would rate 8 or higher on a 1-10 scale
- Make sure the joke is DELIVERABLE - easy to tell and remember

When improving jokes based on feedback:
- Carefully read and understand the critique from previous messages
- If the joke is formatted as a list, convert it to ONE single performable joke
- If it's not conversational, rewrite it to flow naturally
- Make substantial improvements addressing the feedback
- Try different approaches if the current format isn't working

RESPONSE FORMAT:
Just return the joke text - clean, simple, ready to perform. Nothing else.",
    name: "JokeCreator",
    description: "Creates and improves jokes based on feedback");

// Create JokeCritic Agent
ChatClientAgent criticAgent = new(chatClient,
    instructions: @"You are a STRICT professional comedy critic named JokeCritic.

Your role is to evaluate jokes with HIGH STANDARDS and provide tough but constructive feedback.

CRITICAL QUALITY REQUIREMENTS for a joke to rate 8 or higher:
- Must be SHORT: 2-4 sentences maximum
- Must be TIGHT: Every word must earn its place
- Must be PUNCHY: Get to the punchline FAST (within 10-15 seconds when spoken)
- Must be MEMORABLE: Simple enough to retell immediately
- Must be CONVERSATIONAL: Sound natural when spoken aloud
- Must be a SINGLE cohesive joke (not rambling or multiple tangents)
- Must have CLEAR setup and punchline structure
- NO list formats or multiple options
- NO meta-commentary about joke structure

HARSH RATING CRITERIA:
1. **Length**: If joke takes more than 20 seconds to tell → automatic 6 or lower
2. **Clarity**: If punchline isn't obvious and immediate → 6 or lower  
3. **Brevity**: If joke has unnecessary setup or padding → 5 or lower
4. **Structure**: If joke rambles or has multiple tangents → 4 or lower
5. **Punchiness**: If it takes too long to get to the funny part → 5 or lower

Evaluation Criteria (1-10 scale):
1. **Humor (1-10)**: Is it genuinely funny? Strong punchline?
2. **Brevity (1-10)**: Is it SHORT and TIGHT? (2-4 sentences max)
3. **Clarity (1-10)**: Is the punchline crystal clear and immediate?
4. **Timing (1-10)**: Does it get to the funny part FAST?
5. **Deliverability (1-10)**: Can someone retell it in 15 seconds?
6. **Memorability (1-10)**: Is it simple enough to remember and share?

Overall Rating: Average of all criteria (1-10 scale) - MUST BE AN INTEGER

BE TOUGH:
- Rate 8-10: RARE - Only for truly excellent, tight, punchy jokes
- Rate 6-7: Decent joke but needs tightening or faster pacing
- Rate 4-5: Too long, rambling, or unclear punchline
- Rate 1-3: Not funny, confusing, or poorly structured

When evaluating:
- If joke is longer than 4 sentences: rate 6 or lower
- If joke takes more than 15-20 seconds to tell: rate 5 or lower
- If punchline isn't immediate and clear: rate 6 or lower
- If joke has unnecessary padding: rate 5 or lower
- Count the sentences - more than 4 is TOO MANY

Response Format:
Rating: [X/10]
Sentence Count: [X sentences]
Estimated Tell Time: [X seconds]
Feedback: [Your TOUGH, specific feedback here]
Strengths: [List strengths if any]
Improvements: [List SPECIFIC improvements needed - be DEMANDING]

CRITICAL RULE ABOUT APPROVAL:
- Do NOT say 'APPROVED' or 'APPROVED' anywhere in your response if rating < 8
- ONLY use the word 'APPROVED' if rating is 8, 9, or 10
- If rating is 7 or lower: DO NOT APPROVE under any circumstances
- The word 'APPROVED' should ONLY appear when joke truly deserves 8+ rating",
    name: "JokeCritic",
    description: "Strict comedy critic who demands short, punchy, memorable jokes");

// ============================================================================
// MAP AGENTS TO A2A ENDPOINTS
// ============================================================================

app.MapA2A(criticAgent, "/agents/critic");
app.MapA2A(creatorAgent, "/agents/creator");

// ============================================================================
// AG-UI ENDPOINT - Protocol Layer for Workflow
// ============================================================================

// Create AG-UI-compatible agent wrapper for our workflow
// AG-UI is the PROTOCOL LAYER, workflow is the BUSINESS LOGIC
var jokeWorkflowAgent = JokeWorkflowAgentFactory.Create(
    creatorAgent,
    criticAgent,
    app.Services.GetRequiredService<ILogger<Program>>()
);

// MapAGUI: AG-UI automatically handles the streaming protocol!
// The workflow logic is completely independent of the communication layer
app.MapAGUI("/agui/jokes", jokeWorkflowAgent);

// ============================================================================
// API ENDPOINTS
// ============================================================================
// Health check
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    timestamp = DateTime.UtcNow,
    framework = "Microsoft Agent Framework (MAF)",
    pattern = "Group Chat Orchestration",
    agents = new
    {
        creator = "http://localhost:5000/agents/creator",
        critic = "http://localhost:5000/agents/critic"
    }
})).WithName("Health");

Console.WriteLine("====================================================");
Console.WriteLine("🎭 Joke Agents Demo - MAF Group Chat Workflow");
Console.WriteLine("====================================================");
Console.WriteLine();
Console.WriteLine("✅ Using MAF Group Chat Orchestration:");
Console.WriteLine("   • AgentWorkflowBuilder for workflow construction");
Console.WriteLine("   • RoundRobinGroupChatManager for coordination");
Console.WriteLine("   • Custom quality gate (ShouldTerminateAsync)");
Console.WriteLine("   • Automatic conversation history management");
Console.WriteLine();
Console.WriteLine("📍 Application: http://localhost:5000");
Console.WriteLine();
Console.WriteLine("🤖 Agent Endpoints:");
Console.WriteLine("   JokeCreator: http://localhost:5000/agents/creator");
Console.WriteLine("   JokeCritic:  http://localhost:5000/agents/critic");
Console.WriteLine();
Console.WriteLine("💡 Open http://localhost:5000 in your browser!");
Console.WriteLine("====================================================");
Console.WriteLine();

app.Run("http://localhost:5000");



































































