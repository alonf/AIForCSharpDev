using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.AI;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using JokeAgentsDemo;


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

// ============================================================================
// CREATE AGENTS
// ============================================================================

// Create JokeCreator Agent  
ChatClientAgent creatorAgent = new(chatClient, new ChatClientAgentOptions(
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
Just return the joke text - clean, simple, ready to perform. Nothing else.")
{
    Name = "JokeCreator",
    Description = "Creates and improves jokes based on feedback"
});

// Create JokeCritic Agent
ChatClientAgent criticAgent = new(chatClient, new ChatClientAgentOptions(
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
- The word 'APPROVED' should ONLY appear when joke truly deserves 8+ rating")
{
    Name = "JokeCritic",
    Description = "Strict comedy critic who demands short, punchy, memorable jokes"
});

// ============================================================================
// MAP AGENTS TO A2A ENDPOINTS
// ============================================================================

app.MapA2A(criticAgent, "/agents/critic");
app.MapA2A(creatorAgent, "/agents/creator");

// ============================================================================
// ORCHESTRATION API - Using MAF Group Chat Workflow
// ============================================================================

app.MapPost("/api/jokes/create", async (string? topic) =>
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    
    logger.LogInformation("=== Starting Joke Creation Process with MAF Group Chat ===");
    logger.LogInformation("Topic: {Topic}", topic ?? "general");
    
    // Build the group chat workflow with quality gate
    var workflow = AgentWorkflowBuilder
        .CreateGroupChatBuilderWith(agents => 
            new JokeQualityManager(agents, logger))
        .AddParticipants(creatorAgent, criticAgent)
        .Build();
    
    // Prepare initial prompt
    var initialPrompt = string.IsNullOrWhiteSpace(topic)
        ? "Create an original, funny joke."
        : $"Create an original, funny joke about: {topic}";
    
    var messages = new List<ChatMessage> { new(ChatRole.User, initialPrompt) };
    
    // Execute the workflow
    logger.LogInformation("Starting Group Chat workflow...");
    
    StreamingRun run = await InProcessExecution.StreamAsync(workflow, messages);
    await run.TrySendMessageAsync(new TurnToken(emitEvents: true));
    
    var iterations = new List<object>();
    var conversationHistory = new List<ChatMessage>();
    string? finalJoke = null;
    int? finalRating = null;
    int iterationCount = 0;
    
    await foreach (WorkflowEvent evt in run.WatchStreamAsync().ConfigureAwait(false))
    {
        if (evt is AgentRunUpdateEvent update)
        {
            // Accumulate streaming updates - don't log every token
            var response = update.AsResponse();
            if (response.Messages.Any())
            {
                var lastMessage = response.Messages.Last();
                if (!string.IsNullOrEmpty(lastMessage.Text))
                {
                    // Only log when we have a complete message
                    // The streaming is handled internally by the framework
                }
            }
        }
        else if (evt is WorkflowOutputEvent output)
        {
            conversationHistory = output.As<List<ChatMessage>>() ?? new List<ChatMessage>();
            
            // Extract iterations from conversation
            // Skip the initial user message (first message)
            for (int i = 1; i < conversationHistory.Count; i += 2)
            {
                if (i + 1 < conversationHistory.Count)
                {
                    var jokeMessage = conversationHistory[i];
                    var evaluationMessage = conversationHistory[i + 1];
                    
                    // Only process if these are from the agents (not the user)
                    if (jokeMessage.AuthorName != "user" && evaluationMessage.AuthorName != "user")
                    {
                        var joke = jokeMessage.Text ?? "";
                        var evaluation = evaluationMessage.Text ?? "";
                        var rating = JokeQualityManager.ExtractRating(evaluation);
                        var sentenceCount = JokeQualityManager.ExtractSentenceCount(evaluation);
                        var tellTime = JokeQualityManager.ExtractTellTime(evaluation);
                        
                        iterationCount++;
                        
                        // Log complete turn
                        logger.LogInformation("");
                        logger.LogInformation("--- Turn {Turn} ---", iterationCount);
                        logger.LogInformation("[{Agent}]: {Joke}", jokeMessage.AuthorName ?? "Unknown", joke.Length > 150 ? joke.Substring(0, 150) + "..." : joke);
                        logger.LogInformation("[{Agent}]: Rating {Rating}/10 | Sentences: {Sentences} | Tell Time: {Time}s", 
                            evaluationMessage.AuthorName ?? "Unknown", rating, sentenceCount > 0 ? sentenceCount.ToString() : "?", tellTime > 0 ? tellTime.ToString() : "?");
                        
                        iterations.Add(new
                        {
                            iteration = iterationCount,
                            joke,
                            rating,
                            sentenceCount,
                            tellTime,
                            feedback = evaluation,
                            isFunny = rating >= 8,
                            timestamp = DateTime.UtcNow
                        });
                        
                        finalJoke = joke;
                        finalRating = rating;
                    }
                }
            }
            
            break;
        }
    }
    
    var success = (finalRating ?? 0) >= 8;
    
    logger.LogInformation("");
    if (success)
    {
        logger.LogInformation("=== SUCCESS! Joke rated {Rating}/10 ===", finalRating);
    }
    else
    {
        logger.LogWarning("=== Maximum iterations reached. Best rating: {Rating}/10 ===", finalRating ?? 0);
    }
    
    return Results.Ok(new
    {
        success,
        message = success 
            ? $"Created a funny joke in {iterationCount} iteration(s)!" 
            : $"Reached max iterations. Best rating: {finalRating ?? 0}/10",
        finalJoke = finalJoke ?? "",
        finalRating = finalRating ?? 0,
        totalIterations = iterationCount,
        iterations
    });
}).WithName("CreateFunnyJoke");

// ============================================================================
// WEB UI
// ============================================================================

app.MapGet("/", () => Results.Content("""
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta http-equiv="Content-Type" content="text/html; charset=UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>Joke Agents Demo - MAF Group Chat Workflow</title>
    <style>
        * { margin: 0; padding: 0; box-sizing: border-box; }
        body {
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            min-height: 100vh;
            padding: 20px;
        }
        .container {
            max-width: 1200px;
            margin: 0 auto;
        }
        .header {
            background: white;
            padding: 30px;
            border-radius: 15px;
            box-shadow: 0 10px 30px rgba(0,0,0,0.2);
            margin-bottom: 30px;
            text-align: center;
        }
        .emoji-icon {
            font-size: 2em;
            display: inline-block;
            margin-right: 10px;
        }
        .badge-maf {
            display: inline-block;
            background: #28a745;
            color: white;
            padding: 5px 15px;
            border-radius: 20px;
            font-size: 0.9em;
            font-weight: bold;
            margin-top: 10px;
        }
        h1 {
            color: #667eea;
            font-size: 2.5em;
            margin-bottom: 10px;
        }
        .subtitle {
            color: #666;
            font-size: 1.1em;
        }
        .content {
            display: grid;
            grid-template-columns: 1fr 1fr;
            gap: 20px;
            margin-bottom: 30px;
        }
        .panel {
            background: white;
            padding: 25px;
            border-radius: 15px;
            box-shadow: 0 10px 30px rgba(0,0,0,0.2);
        }
        .full-width { grid-column: 1 / -1; }
        h2 {
            color: #667eea;
            margin-bottom: 15px;
            font-size: 1.5em;
        }
        input, button {
            width: 100%;
            padding: 12px;
            margin: 10px 0;
            border: 2px solid #ddd;
            border-radius: 8px;
            font-size: 16px;
        }
        button {
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: white;
            border: none;
            cursor: pointer;
            font-weight: bold;
            transition: transform 0.2s;
        }
        button:hover { transform: scale(1.02); }
        button:disabled {
            background: #ccc;
            cursor: not-allowed;
        }
        .result {
            background: #f8f9fa;
            padding: 20px;
            border-radius: 8px;
            margin-top: 15px;
            display: none;
        }
        .joke-text {
            font-size: 1.3em;
            color: #333;
            margin: 15px 0;
            padding: 15px;
            background: #fff;
            border-left: 4px solid #667eea;
            border-radius: 5px;
        }
        .rating {
            font-size: 2em;
            font-weight: bold;
            color: #667eea;
        }
        .iteration {
            background: white;
            padding: 15px;
            margin: 10px 0;
            border-radius: 8px;
            border-left: 4px solid #ddd;
        }
        .iteration.success { border-left-color: #28a745; }
        .loading {
            text-align: center;
            color: #667eea;
            font-style: italic;
        }
        .endpoint {
            background: #f8f9fa;
            padding: 15px;
            margin: 10px 0;
            border-radius: 8px;
            font-family: 'Courier New', monospace;
        }
        .badge {
            display: inline-block;
            padding: 5px 10px;
            border-radius: 5px;
            font-size: 0.9em;
            font-weight: bold;
            margin-right: 5px;
        }
        .badge-success { background: #28a745; color: white; }
        .badge-warning { background: #ffc107; color: black; }
        .badge-info { background: #17a2b8; color: white; }
        .feature-list {
            background: #e8f5e9;
            padding: 15px;
            border-radius: 8px;
            margin: 15px 0;
        }
        .feature-list ul {
            margin-left: 20px;
            color: #2e7d32;
        }
        .feature-list li {
            margin: 5px 0;
        }
    </style>
</head>
<body>
    <div class="container">
        <div class="header">
            <h1><span class="emoji-icon">&#127917;</span>Joke Agents Demo</h1>
            <p class="subtitle">MAF Group Chat Workflow with Quality Gate</p>
            <span class="badge-maf">&#10004; Using MAF Workflows</span>
        </div>

        <div class="content">
            <div class="panel full-width">
                <h2>Create a Funny Joke</h2>
                <p>Watch two AI agents collaborate using <strong>MAF Group Chat workflow</strong> to create the perfect joke!</p>
                <div class="feature-list">
                    <strong>&#127919; Using Microsoft Agent Framework:</strong>
                    <ul>
                        <li><code>GroupChatBuilder</code> - Workflow orchestration</li>
                        <li><code>RoundRobinGroupChatManager</code> - Agent coordination</li>
                        <li><code>ShouldTerminateAsync()</code> - Custom quality gate</li>
                        <li>Automatic conversation history management</li>
                    </ul>
                </div>
                <input type="text" id="topic" placeholder="Enter a topic (optional, e.g., 'programming', 'cats')">
                <button onclick="createJoke()" id="createBtn">&#127914; Create Funny Joke (MAF Workflow)</button>
                <div id="result" class="result"></div>
            </div>

            <div class="panel">
                <h2>&#129302; JokeCreator Agent</h2>
                <p>Creates and improves jokes</p>
                <div class="endpoint">
                    <div><strong>Endpoint:</strong> /agents/creator</div>
                    <div><strong>Pattern:</strong> Group Chat Participant</div>
                </div>
            </div>

            <div class="panel">
                <h2>&#127919; JokeCritic Agent</h2>
                <p>Evaluates jokes and provides feedback</p>
                <div class="endpoint">
                    <div><strong>Endpoint:</strong> /agents/critic</div>
                    <div><strong>Pattern:</strong> Group Chat Participant</div>
                </div>
            </div>
        </div>

        <div class="panel">
            <h2>&#128200; How MAF Group Chat Works</h2>
            <ol style="line-height:1.8; color:#666; margin-left:20px">
                <li><strong>Group Chat Manager:</strong> Coordinates agent turns using <code>RoundRobinGroupChatManager</code></li>
                <li><strong>Iterative Refinement:</strong> Agents take turns, each seeing full conversation history</li>
                <li><strong>Quality Gate:</strong> <code>ShouldTerminateAsync()</code> checks rating and terminates when ≥8</li>
                <li><strong>Automatic Context:</strong> Conversation history automatically shared between agents</li>
                <li><strong>Managed Iteration:</strong> Framework handles turn-taking and history</li>
            </ol>
        </div>
    </div>

    <script>
        async function createJoke() {
            const topic = document.getElementById('topic').value;
            const btn = document.getElementById('createBtn');
            const result = document.getElementById('result');
            
            btn.disabled = true;
            btn.innerHTML = '&#128260; Creating joke...';
            result.style.display = 'block';
            result.innerHTML = '<div class="loading">&#129302; JokeCreator and JokeCritic are collaborating via MAF Group Chat workflow...<br>Check the console for detailed logs!</div>';
            
            try {
                const response = await fetch(`/api/jokes/create?topic=${encodeURIComponent(topic || '')}`, {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json'
                    }
                });
                
                if (!response.ok) {
                    throw new Error(`HTTP error! status: ${response.status}`);
                }
                
                const data = await response.json();
                
                let html = `
                    <h3>${data.success ? '&#10004; Success!' : '&#9888; Max Iterations Reached'}</h3>
                    <p>${data.message}</p>
                    <div class="joke-text">"${data.finalJoke}"</div>
                    <p><span class="rating">${data.finalRating}/10</span> ${data.finalRating >= 8 ? '&#127881;' : '&#128522;'}</p>
                    <h3 style="margin-top:20px">Iteration History (Group Chat Turns):</h3>
                `;
                
                data.iterations.forEach(iter => {
                    const isSuccess = iter.rating >= 8;
                    html += `
                        <div class="iteration ${isSuccess ? 'success' : ''}">
                            <div>
                                <span class="badge badge-info">Iteration ${iter.iteration}</span>
                                <span class="badge ${isSuccess ? 'badge-success' : 'badge-warning'}">Rating: ${iter.rating}/10</span>
                            </div>
                            <div style="margin-top:10px"><strong>Joke:</strong> "${iter.joke}"</div>
                            <div style="margin-top:5px;color:#666"><strong>Feedback:</strong> ${iter.feedback}</div>
                        </div>
                    `;
                });
                
                result.innerHTML = html;
            } catch (error) {
                result.innerHTML = `<p style="color:red">Error: ${error.message}</p>`;
                console.error('Error creating joke:', error);
            } finally {
                btn.disabled = false;
                btn.innerHTML = '&#127914; Create Another Joke';
            }
        }
    </script>
</body>
</html>
""", "text/html")).WithName("Home");

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









