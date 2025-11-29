// This sample shows how to create and use a simple AI agent with Azure OpenAI as the backend.

using Azure.AI.OpenAI;   // Azure OpenAI SDK
using Azure.Identity;    // Azure Identity SDK
using Microsoft.Agents.AI; // MAF (Microsoft Agent Framework)
using OpenAI;            // OpenAI SDK (used by Azure OpenAI)

var endpoint = new Uri("https://alonlecturedemo-resource.cognitiveservices.azure.com/");
var credential = new DefaultAzureCredential(); // Uses Azure CLI credentials or Managed Identity

// Create an AI Agent using MAF (Microsoft Agent Framework)
AIAgent agent = new AzureOpenAIClient(endpoint, credential) // Create Azure OpenAI client (Azure.AI.OpenAI SDK)
    .GetChatClient("model-router")                          // Get chat client for the deployment (Azure.AI.OpenAI SDK)
    .CreateAIAgent(                                         // Create AI Agent (MAF extension method)
        instructions: "You are a helpful assistant.",
        name: "Assistant");

// Run the agent with a prompt
Console.WriteLine(await agent.RunAsync("Sort this list: [3,1,2]"));
