// Computer Usage Agent - Demonstrates MAF with MCP integration for Windows Event Log analysis

using Azure.AI.OpenAI;
using Azure.Identity;
using ComputerUsageAgent.Visualization;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using OpenAI;
using Spectre.Console;

// Display welcome banner
AnsiConsole.Write(
    new FigletText("Computer Usage Agent")
        .Centered()
        .Color(Color.Cyan1));

AnsiConsole.MarkupLine("[grey]Powered by Microsoft Agent Framework (MAF) + MCP[/]");
AnsiConsole.WriteLine();

McpClient? mcpClient = null; // keep alive across the whole run

try
{
    // Configure Azure connection
    var endpoint = new Uri("https://alonlecturedemo-resource.cognitiveservices.azure.com/");
    var credential = new DefaultAzureCredential();
    string deploymentName = "model-router";

    // Configure MCP HTTP endpoint for our C# EventLog MCP server
    var mcpServerUri = new Uri("http://localhost:5115/mcp");

    AnsiConsole.MarkupLine($"[grey]Endpoint: {endpoint}[/]");
    AnsiConsole.MarkupLine($"[grey]Model: {deploymentName}[/]");
    AnsiConsole.MarkupLine($"[grey]MCP Server: {mcpServerUri}[/]");
    AnsiConsole.WriteLine();

    IList<McpClientTool> mcpTools = new List<McpClientTool>();

    // Connect to MCP first, then create the agent (do not dispose the client yet)
    await AnsiConsole.Status().StartAsync("Initializing agent and MCP tools...", async ctx =>
    {
        ctx.Spinner(Spinner.Known.Dots);
        ctx.SpinnerStyle(Style.Parse("green"));

        mcpClient = await McpClient.CreateAsync(new HttpClientTransport(new()
        {
            Endpoint = mcpServerUri
        }));

        mcpTools = await mcpClient.ListToolsAsync();

        AnsiConsole.MarkupLine($"[green]✓ Connected to MCP server with {mcpTools.Count} tools available[/]");
        foreach (var tool in mcpTools)
        {
            AnsiConsole.MarkupLine($"[grey]  • {tool.Name}: {tool.Description}[/]");
        }
        AnsiConsole.WriteLine();
    });

    // Create AI Agent with MCP tools (after status)
    ChatClientAgent agent = new(
        new AzureOpenAIClient(endpoint, credential)
            .GetChatClient(deploymentName)
            .AsIChatClient(),
        instructions: @"You are a helpful computer usage analysis assistant.
You have access to Windows Event Log tools through the EventLog MCP server.

Available tools:
- get_startup_shutdown_events: List startup/shutdown events
- calculate_uptime: Compute totals and daily breakdown
- get_usage_summary: Human-readable summary of usage

When asked about computer usage:
1. Call get_startup_shutdown_events for the requested period (days)
2. Or call calculate_uptime directly if only stats are needed
3. Present findings in a clear, structured format with a compact day-by-day list of 'YYYY-MM-DD: <hours> hours'.

Be concise and helpful in your responses.",
        name: "ComputerUsageAnalyst",
        tools: [.. mcpTools]);

    AnsiConsole.MarkupLine($"[green]✓ Agent '{agent.Name}' created successfully[/]");
    AnsiConsole.WriteLine();

    // Interactive mode (outside status to avoid visual overlap)
    AnsiConsole.MarkupLine("[cyan]What would you like to do?[/]");
    AnsiConsole.WriteLine();

    var choice = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .Title("Select an option:")
            .AddChoices("Show computer usage for last 30 days", "Show computer usage for last 7 days", "Show computer usage for custom period", "Exit"));

    if (choice != "Exit")
    {
        int days = choice switch
        {
            "Show computer usage for last 7 days" => 7,
            "Show computer usage for custom period" => AnsiConsole.Prompt(
                new TextPrompt<int>("Enter number of days:")
                    .DefaultValue(30)
                    .ValidationErrorMessage("[red]Please enter a valid number[/]")
                    .Validate(d => d is > 0 and <= 365)),
            _ => 30
        };

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[cyan]Analyzing computer usage for the last {days} days...[/]");
        AnsiConsole.WriteLine();

        string prompt = $@"Analyze my computer usage for the last {days} days.
Use the MCP tools to retrieve startup/shutdown events and calculate daily uptime.
Return a day-by-day breakdown, totals, and averages.
Format the daily breakdown as lines like: 'YYYY-MM-DD: <hours> hours'.";

        // Separate status only for the analysis call
        var response = await AnsiConsole.Status()
            .StartAsync("Running analysis...", async _ =>
            {
                var agentResponse = await agent.RunAsync(prompt);
                return agentResponse.Text;
            });

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[green]═══ Analysis Results ═══[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(response);

        // Render a bar chart from the text output if possible
        ConsoleGraphRenderer.RenderFromText(response, maxBars: days);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[green]═══════════════════════[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[green]✓ Analysis complete![/]");
    }
}
catch (Exception ex)
{
    AnsiConsole.MarkupLine("[red]❌ An error occurred:[/]");
    AnsiConsole.WriteException(ex);
    
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[yellow]Troubleshooting tips:[/]");
    AnsiConsole.MarkupLine("  • Ensure the EventLog MCP server is running: cd EventLogMcp; dotnet run");
    AnsiConsole.MarkupLine("  • Verify Azure authentication (az login)");
}
finally
{
    if (mcpClient is not null)
    {
        await mcpClient.DisposeAsync();
    }
}

AnsiConsole.WriteLine();
AnsiConsole.MarkupLine("[grey]Press any key to exit...[/]");
Console.ReadKey();
