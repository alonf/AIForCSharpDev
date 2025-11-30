using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.AI;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Builder;
using A2A;

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

// Create JokeCritic Agent
var criticAgent = chatClient.CreateAIAgent(
    instructions: @"You are a professional comedy critic named JokeCritic.

Your role is to evaluate jokes objectively and provide constructive feedback.

Evaluation Criteria:
1. **Humor (1-10)**: Is it funny? Does it have a good punchline?
2. **Originality (1-10)**: Is it creative and fresh?
3. **Wordplay (1-10)**: Does it use clever language?
4. **Timing (1-10)**: Is the setup and delivery well-paced?
5. **Deliverability (1-10)**: Is it easy to tell? Can it be performed naturally?
6. **Structure (1-10)**: Is it a single, cohesive joke (not a list or multiple options)?

Overall Rating: Average of all criteria (1-10 scale) - MUST BE AN INTEGER

CRITICAL REQUIREMENTS for a joke to rate 8 or higher:
- Must be a SINGLE joke, not multiple options or variations
- Must be conversational and easy to deliver
- Must flow naturally when spoken aloud
- No list format (""here are some options"", ""pick from these"")
- No meta-commentary about joke types
- Should sound like something a comedian would actually say on stage

When evaluating a joke:
- If it's formatted as a list or multiple options: rate it 4 or lower
- If it's not conversational/deliverable: rate it 6 or lower
- If rating < 8: Provide specific, actionable feedback
- If rating >= 8: Explain what makes it great
- Always be constructive and encouraging

CRITICAL: You MUST respond with ONLY valid JSON. No markdown code blocks, no extra text, just pure JSON.

Response Format:
{
  ""rating"": 7,
  ""feedback"": ""detailed constructive feedback here"",
  ""isFunny"": false,
  ""strengths"": [""strength 1"", ""strength 2""],
  ""improvements"": [""improvement 1"", ""improvement 2""]
}

Rules:
- rating MUST be an integer between 1 and 10
- isFunny MUST be a boolean (true or false)
- strengths and improvements MUST be arrays of strings
- Do NOT wrap the JSON in markdown code blocks
- Do NOT add any text before or after the JSON",
    name: "JokeCritic");


// Create JokeCreator Agent
var creatorAgent = chatClient.CreateAIAgent(
    instructions: @"You are a creative comedian named JokeCreator.

Your task is to:
1. Create original, funny jokes when requested
2. Improve jokes when given feedback from a critic
3. Make substantial improvements that address specific feedback

CRITICAL REQUIREMENTS:
- Create ONE single joke, not multiple options or variations
- The joke must be conversational and sound natural when spoken aloud
- It should flow like something a comedian would say on stage
- NO list formats (avoid ""here are some options"", ""pick from these"")
- NO meta-commentary about joke structure or types
- The joke should be ready to perform immediately

When creating jokes:
- Be original and creative
- Use different joke formats (one-liner, setup-punchline, observational humor)
- Use wordplay, surprise, or relatable situations
- Keep jokes appropriate and culturally sensitive
- Aim for jokes that would rate 8 or higher on a 1-10 scale
- Make sure the joke is DELIVERABLE - easy to tell and remember

When improving jokes based on feedback:
- Carefully read and understand the critique
- If the joke is formatted as a list, convert it to ONE single performable joke
- If it's not conversational, rewrite it to flow naturally
- Make substantial improvements addressing the feedback
- Don't just make minor changes - really improve the joke
- Try different approaches if the current format isn't working
- Focus on the elements that need improvement (wordplay, timing, surprise, deliverability)

RESPONSE FORMAT:
Just return the joke text - clean, simple, ready to perform. Nothing else.",
    name: "JokeCreator");


// ============================================================================
// MAP AGENTS TO A2A ENDPOINTS
// This uses the actual Microsoft Agent Framework A2A hosting
// ============================================================================

app.MapA2A(criticAgent, "/agents/critic");
app.MapA2A(creatorAgent, "/agents/creator");

// ============================================================================
// ORCHESTRATION API
// Demonstrates calling remote agent via A2A protocol
// ============================================================================

app.MapPost("/api/jokes/create", async (string? topic) =>
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    
    logger.LogInformation("=== Starting Joke Creation Process ===");
    logger.LogInformation("Topic: {Topic}", topic ?? "general");
    
    const int MaxIterations = 5;
    const int MinAcceptableRating = 8;
    var iterations = new List<object>();
    string currentJoke = string.Empty;

    // Create A2A client to call the Critic agent remotely
    var criticEndpoint = "http://localhost:5000/agents/critic";
    var a2aClient = new A2AClient(new Uri(criticEndpoint));
    
    // Get remote critic agent as a proxy
    var remoteCriticAgent = a2aClient.GetAIAgent(
        id: "joke-critic",
        name: "JokeCritic",
        description: "Remote joke evaluation agent",
        displayName: "Joke Critic",
        loggerFactory: app.Services.GetRequiredService<ILoggerFactory>());

    for (int i = 0; i < MaxIterations; i++)
    {
        logger.LogInformation("");
        logger.LogInformation("--- Iteration {Iteration}/{MaxIterations} ---", i + 1, MaxIterations);

        // Step 1: Create or improve joke using JokeCreator
        if (i == 0)
        {
            logger.LogInformation("JokeCreator: Creating initial joke...");
            var prompt = string.IsNullOrWhiteSpace(topic)
                ? "Create an original, funny joke."
                : $"Create an original, funny joke about: {topic}";
            var response = await creatorAgent.RunAsync(prompt);
            currentJoke = response.Text;
        }
        else
        {
            var previousFeedback = iterations[i - 1];
            logger.LogInformation("JokeCreator: Improving joke based on feedback...");
            var prompt = $@"Here is a joke that needs improvement:
""{currentJoke}""

The critic's feedback:
{System.Text.Json.JsonSerializer.Serialize(previousFeedback)}

Please create an improved version that addresses this feedback.";
            var response = await creatorAgent.RunAsync(prompt);
            currentJoke = response.Text;
        }

        logger.LogInformation("Joke: \"{Joke}\"", currentJoke);

        // Step 2: Evaluate using remote Critic via A2A protocol
        logger.LogInformation("Calling JokeCritic via A2A protocol at {Endpoint}...", criticEndpoint);
        var evalPrompt = $@"Please evaluate this joke:

""{currentJoke}""

IMPORTANT: Return ONLY valid JSON with this exact structure (no markdown, no extra text):
{{
  ""rating"": <integer 1-10>,
  ""feedback"": ""<string>"",
  ""isFunny"": <boolean>,
  ""strengths"": [""<string>"", ...],
  ""improvements"": [""<string>"", ...]
}}";
        
        var evaluationResponse = await remoteCriticAgent.RunAsync(evalPrompt);
        
        logger.LogInformation("JokeCritic Response: {Response}", evaluationResponse.Text);

        // Parse evaluation
        var evaluation = ParseEvaluation(evaluationResponse.Text, logger);
        
        logger.LogInformation("Rating: {Rating}/10, IsFunny: {IsFunny}", evaluation.Rating, evaluation.IsFunny);

        // Record iteration
        iterations.Add(new
        {
            iteration = i + 1,
            joke = currentJoke,
            rating = evaluation.Rating,
            feedback = evaluation.Feedback,
            isFunny = evaluation.IsFunny,
            strengths = evaluation.Strengths,
            improvements = evaluation.Improvements,
            timestamp = DateTime.UtcNow
        });

        // Check if we're done
        if (evaluation.Rating >= MinAcceptableRating)
        {
            logger.LogInformation("");
            logger.LogInformation("=== SUCCESS! Joke rated {Rating}/10 ===", evaluation.Rating);
            
            return Results.Ok(new
            {
                success = true,
                message = $"Created a funny joke in {i + 1} iteration(s)!",
                finalJoke = currentJoke,
                finalRating = evaluation.Rating,
                totalIterations = i + 1,
                iterations
            });
        }
    }

    // Max iterations reached
    logger.LogWarning("Maximum iterations reached");
    var lastIteration = iterations.Last();
    var lastRating = ((dynamic)lastIteration).rating;
    
    return Results.Ok(new
    {
        success = false,
        message = $"Reached max iterations. Best rating: {lastRating}/10",
        finalJoke = currentJoke,
        finalRating = lastRating,
        totalIterations = MaxIterations,
        iterations
    });
}).WithName("CreateFunnyJoke");

// ============================================================================
// WEB UI
// Simple interface to interact with the agents
// ============================================================================

app.MapGet("/", () => Results.Content("""
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta http-equiv="Content-Type" content="text/html; charset=UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>Joke Agents Demo - A2A Protocol (MAF)</title>
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
            <p class="subtitle">Agent-to-Agent (A2A) Communication with Microsoft Agent Framework</p>
            <span class="badge-maf">&#10004; Using Official MAF A2A APIs</span>
        </div>

        <div class="content">
            <div class="panel full-width">
                <h2>Create a Funny Joke</h2>
                <p>Watch two AI agents collaborate using <strong>real A2A protocol</strong> to create the perfect joke!</p>
                <div class="feature-list">
                    <strong>&#127919; Using Microsoft Agent Framework:</strong>
                    <ul>
                        <li><code>MapA2A()</code> - Official agent hosting</li>
                        <li><code>A2AClient</code> - Remote agent discovery</li>
                        <li><code>GetAIAgent()</code> - Agent proxy creation</li>
                        <li>Automatic Agent Card generation</li>
                    </ul>
                </div>
                <input type="text" id="topic" placeholder="Enter a topic (optional, e.g., 'programming', 'cats')">
                <button onclick="createJoke()" id="createBtn">&#127914; Create Funny Joke (Real A2A Demo)</button>
                <div id="result" class="result"></div>
            </div>

            <div class="panel">
                <h2>&#129302; JokeCreator Agent</h2>
                <p>Creates and improves jokes</p>
                <div class="endpoint">
                    <div><strong>Endpoint:</strong> /agents/creator</div>
                    <div><strong>Hosted with:</strong> <code>MapA2A()</code></div>
                </div>
            </div>

            <div class="panel">
                <h2>&#127919; JokeCritic Agent</h2>
                <p>Evaluates jokes and provides feedback</p>
                <div class="endpoint">
                    <div><strong>Endpoint:</strong> /agents/critic</div>
                    <div><strong>Called via:</strong> <code>A2AClient</code></div>
                </div>
            </div>
        </div>

        <div class="panel">
            <h2>&#128200; How Real A2A Protocol Works (MAF)</h2>
            <ol style="line-height:1.8; color:#666; margin-left:20px">
                <li><strong>Agent Hosting:</strong> Agents mapped using <code>app.MapA2A(agent, "/path")</code></li>
                <li><strong>Agent Discovery:</strong> <code>A2AClient</code> creates connection to remote agent</li>
                <li><strong>Agent Proxy:</strong> <code>GetAIAgent()</code> creates local proxy for remote agent</li>
                <li><strong>Communication:</strong> Proxy calls remote agent via HTTP (A2A protocol)</li>
                <li><strong>Agent Cards:</strong> Automatically generated by MapA2A</li>
                <li><strong>Observability:</strong> All interactions logged via MAF logging</li>
            </ol>
        </div>
    </div>

    <script>
        async function createJoke() {
            const topic = document.getElementById('topic').value;
            const btn = document.getElementById('createBtn');
            const result = document.getElementById('result');
            
            btn.disabled = true;
            btn.innerHTML = '&#128260; Creating joke...'; // Use innerHTML instead of textContent
            result.style.display = 'block';
            result.innerHTML = '<div class="loading">&#129302; JokeCreator and JokeCritic are working together via A2A protocol...<br>Using official Microsoft Agent Framework APIs!<br>Check the console for detailed logs!</div>';
            
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
                    <h3 style="margin-top:20px">Iteration History (A2A Calls via A2AClient):</h3>
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
                            ${iter.strengths?.length ? `<div style="margin-top:5px;color:#28a745"><strong>&#10003; Strengths:</strong> ${iter.strengths.join(', ')}</div>` : ''}
                            ${iter.improvements?.length ? `<div style="margin-top:5px;color:#ffc107"><strong>&#8594; Improvements:</strong> ${iter.improvements.join(', ')}</div>` : ''}
                        </div>
                    `;
                });
                
                result.innerHTML = html;
            } catch (error) {
                result.innerHTML = `<p style="color:red">Error: ${error.message}</p>`;
                console.error('Error creating joke:', error);
            } finally {
                btn.disabled = false;
                btn.innerHTML = '&#127914; Create Another Joke'; // Use innerHTML
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
    a2aProtocol = "Official Implementation",
    agents = new
    {
        creator = "http://localhost:5000/agents/creator",
        critic = "http://localhost:5000/agents/critic"
    }
})).WithName("Health");

Console.WriteLine("====================================================");
Console.WriteLine("?? Joke Agents Demo - Microsoft Agent Framework A2A");
Console.WriteLine("====================================================");
Console.WriteLine();
Console.WriteLine("? Using Official MAF A2A APIs:");
Console.WriteLine("   • MapA2A() for agent hosting");
Console.WriteLine("   • A2AClient for remote agent discovery");
Console.WriteLine("   • GetAIAgent() for agent proxy creation");
Console.WriteLine();
Console.WriteLine("?? Application: http://localhost:5000");
Console.WriteLine();
Console.WriteLine("?? Agent Endpoints (A2A Protocol):");
Console.WriteLine("   JokeCreator: http://localhost:5000/agents/creator");
Console.WriteLine("   JokeCritic:  http://localhost:5000/agents/critic");
Console.WriteLine();
Console.WriteLine("?? Open http://localhost:5000 in your browser!");
Console.WriteLine("====================================================");
Console.WriteLine();

app.Run("http://localhost:5000");

// Helper function to parse evaluation
static JokeEvaluation ParseEvaluation(string evaluationText, ILogger logger)
{
    try
    {
        // Try to extract JSON from the response
        var jsonStart = evaluationText.IndexOf('{');
        var jsonEnd = evaluationText.LastIndexOf('}');
        
        if (jsonStart >= 0 && jsonEnd > jsonStart)
        {
            var jsonText = evaluationText.Substring(jsonStart, jsonEnd - jsonStart + 1);
            var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var evaluation = System.Text.Json.JsonSerializer.Deserialize<JokeEvaluation>(jsonText, options);
            
            if (evaluation != null)
            {
                return evaluation with { IsFunny = evaluation.Rating >= 8 };
            }
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Failed to parse evaluation JSON");
    }

    // Fallback
    var ratingMatch = System.Text.RegularExpressions.Regex.Match(
        evaluationText, 
        @"rating[\""\s:]*(\d+)", 
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    
    var rating = ratingMatch.Success && int.TryParse(ratingMatch.Groups[1].Value, out int r) ? r : 5;
    
    return new JokeEvaluation(
        Rating: rating,
        Feedback: evaluationText,
        IsFunny: rating >= 8,
        Strengths: Array.Empty<string>(),
        Improvements: Array.Empty<string>()
    );
}

record JokeEvaluation(
    int Rating,
    string Feedback,
    bool IsFunny,
    string[] Strengths,
    string[] Improvements
);
