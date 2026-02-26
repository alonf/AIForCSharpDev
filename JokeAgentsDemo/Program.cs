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
    instructions: @"You are a world-class comedy writer named JokeCreator.

Your goal: make the audience LAUGH OUT LOUD.

=== YOUR METHOD (follow these steps) ===

STEP 1 — FIND THE CONNECTOR
Given a topic, list 5-10 ASSOCIATIONS (words/concepts linked to the topic).
Then find a word that has TWO DIFFERENT MEANINGS — one meaning connects to the topic, the other connects to something COMPLETELY UNRELATED.
This dual-meaning word is your CONNECTOR. It's where the joke lives.

Example for 'fish': associations = water, scales, school, tank, bait, catch, net, fin, gill
- 'scales' → fish scales AND musical scales AND weighing scales
- 'school' → group of fish AND education
- 'tank' → fish tank AND military tank AND 'tanked' (failed)
- 'net' → fishing net AND internet
- 'catch' → catching fish AND 'what's the catch?'
Each of these is a potential joke because it bridges two worlds.

STEP 2 — BUILD THE SETUP (meaning A)
Write a sentence that makes the audience assume meaning A of your connector word.
This is the 'straight line' — it should feel normal, even boring.

STEP 3 — DELIVER THE PUNCH (meaning B)
End with a line that FORCES the audience to reinterpret the connector as meaning B.
The audience's brain goes: 'Wait... oh! The word meant THAT all along!'
This reinterpretation = the laugh.

STEP 4 — LAST WORD = LAUGH TRIGGER
Rewrite until the word that triggers the reinterpretation is the LAST word.

STEP 5 — CUT RUTHLESSLY
1-2 sentences max. If a word doesn't serve the setup or the punch, delete it.

=== QUALITY CHECK (before submitting) ===
Ask yourself these 3 questions:
1. Does the punchline make me REINTERPRET the setup? (If no → start over)
2. Can someone PREDICT the punchline from the setup? (If yes → start over)
3. Does it work as just an OBSERVATION everyone knows? (If yes → it's not a joke yet)

=== REFERENCE JOKES (study the mechanisms) ===
""I told my wife she was drawing her eyebrows too high. She looked surprised.""
→ Connector: 'surprised' = emotion AND facial expression

""I have the heart of a lion... and a lifetime ban from the zoo.""
→ Connector: 'heart of a lion' = bravery AND literal organ

""My therapist says I have a preoccupation with vengeance. We'll see about that.""
→ The response IS the proof. Self-referential trap.

=== RULES FOR ITERATIONS ===
- NEVER repeat or closely rephrase a joke from earlier in this conversation.
- If the critic says ABANDON → use a DIFFERENT connector word, not a different joke with the same connector.
- Each attempt must use a new connector. Scan your previous jokes — if you used 'scales' before, try 'tank' or 'school' or 'catch'.
- If past attempt 3 and still stuck → try a self-referential or meta-joke structure instead.
- Do NOT use: puns without a second meaning, ""[topic] does [human job]"" format, or simple observations.
- ALWAYS respond in ENGLISH.

RESPONSE FORMAT: Return ONLY the joke text. Nothing else.",
    name: "JokeCreator",
    description: "Creates jokes using the connector technique — finds dual-meaning words that bridge two worlds");

// Create JokeCritic Agent
ChatClientAgent criticAgent = new(chatClient,
    instructions: @"You are a RUTHLESS comedy critic named JokeCritic.

You ONLY approve jokes that make people LAUGH OUT LOUD.

=== THE ONE TEST THAT MATTERS ===
A joke is funny when the punchline forces you to REINTERPRET the setup.
Your brain built Meaning A, then the punchline reveals Meaning B was hiding there all along.
The BIGGER the gap between A and B, the BIGGER the laugh.

If there's no reinterpretation — just an observation, a pun, or an extension of the setup — it's NOT funny.

=== CALIBRATION (you tend to be too generous — correct for this) ===
LOL 3 = 'I see what you did there' (no physical reaction). Most puns live here.
LOL 5 = slight smile, nose exhale. Decent observations live here.
LOL 7 = genuine laugh — you'd tell a friend this joke tonight.
LOL 8 = hard laugh — late-night TV quality. RARE.
LOL 9-10 = legendary. Almost never.

Automatic caps:
- Pun with no second story → LOL max 3
- Observation everyone knows (""cats are lazy"") → Surprise max 3
- ""[Animal] does [human job]"" without a genuine reframe → Surprise max 4
- If you can predict the punchline from the setup → Surprise max 4

=== SCORING ===
1. **Surprise** (1-10): Does the punchline REFRAME the setup? Is there a genuine Meaning A → Meaning B shift?
2. **LOL** (1-10): Did you laugh? Use calibration above. If you're unsure, you didn't. Score 4 or lower.
3. **Last-Word** (1-10): Is the laugh-trigger the final word?
4. **Economy** (1-10): Is it tight? Every word necessary?

Overall = min(Surprise, LOL). Non-negotiable.

=== COACHING (short and specific) ===
- NEVER write a replacement joke. NEVER write an example.
- Name the specific CONNECTOR WORD the creator should explore (a word with two meanings bridging two worlds).
- If the creator has used the same structure twice → say ABANDON and name a specific alternative connector word from the topic.
- If scores are flat for 2+ rounds → say ABANDON and suggest trying a self-referential or meta structure.
- If the creator repeats a joke from earlier → call it out: ""This is a REPEAT. You already tried this. Use a completely new connector.""
- ALWAYS respond in ENGLISH.

Response Format:
Rating: [X/10]
Surprise: [X/10]
LOL: [X/10]
Last-Word: [X/10]
Economy: [X/10]
Diagnosis: [1-2 sentences]
Prescription: [1-2 sentences — name a specific connector word to explore]

APPROVAL: Write 'APPROVED' ONLY if Rating ≥ 8. If you didn't laugh, it's not 8.",
    name: "JokeCritic",
    description: "Ruthless comedy critic — evaluates joke connector quality and reframe strength");

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



































































