// This sample shows how to create and use a simple AI agent with Azure OpenAI as the backend.

using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
// Azure OpenAI SDK
// Azure Identity SDK
// MAF (Microsoft Agent Framework)

// OpenAI SDK (used by Azure OpenAI)

var endpoint = new Uri("https://alonlecturedemo-resource.cognitiveservices.azure.com/");
var credential = new DefaultAzureCredential(); // Uses Azure CLI credentials or Managed Identity

// Create an AI Agent using MAF (Microsoft Agent Framework)
var chatClient = new AzureOpenAIClient(endpoint, credential) // Create Azure OpenAI client (Azure.AI.OpenAI SDK)
    .GetChatClient("model-router")                           // Get chat client for the deployment (Azure.AI.OpenAI SDK)
    .AsIChatClient();                                        // Convert to IChatClient (Microsoft.Extensions.AI)

ChatClientAgent agent = new(                                 // Create AI Agent (MAF ChatClientAgent)
    chatClient,
    instructions: "You are a helpful assistant.",
    name: "Assistant");

// Run the agent with a prompt
Console.WriteLine(await agent.RunAsync("Sort this list: [3,1,2]"));
