# ?? AG-UI Streaming Integration

## Overview

The JokeAgentsDemo now includes **real-time streaming** capabilities using Server-Sent Events (SSE), providing a ChatGPT-style experience where you can watch agents collaborate live!

---

## ?? What's New

### **Live Streaming Interface**
- ? **Word-by-word streaming** - See jokes appear as they're created
- ? **Real-time agent indicators** - Know which agent is "thinking"
- ? **Live progress tracking** - Watch iteration count and ratings update
- ? **ChatGPT-style UX** - Modern, engaging interface
- ? **Typing animations** - Visual feedback while agents work

### **Dual Interface Options**
1. **Streaming View** (`/streaming.html`) - NEW! Real-time collaboration
2. **Classic View** (`/classic.html`) - Traditional all-at-once results

---

## ??? Architecture

### **Server-Side: SSE Endpoint**

```
GET /api/jokes/stream?topic={topic}
```

**Technology**: Server-Sent Events (SSE)
- Unidirectional streaming from server ? client
- Perfect for real-time agent updates
- Built-in browser support (EventSource API)
- Automatic reconnection

**Event Types**:
```typescript
'status'                  // Status updates
'user_message'            // User's initial prompt
'agent_chunk'             // Streaming text chunks from agents
'agent_message_complete'  // Agent finished speaking
'iteration_complete'      // Iteration summary with rating
'workflow_complete'       // Final workflow results
'error'                   // Error messages
```

### **Client-Side: EventSource**

```javascript
const eventSource = new EventSource('/api/jokes/stream?topic=cats');

eventSource.addEventListener('agent_chunk', (e) => {
    const data = JSON.parse(e.data);
    appendText(data.agent, data.content);
});
```

---

## ?? Comparison: Streaming vs Classic

| Feature | Classic (POST) | Streaming (SSE) |
|---------|----------------|-----------------|
| **Real-time Updates** | ? No | ? Yes |
| **User Experience** | ?? Wait blindly | ? Watch progress |
| **Agent Visibility** | ? Hidden | ? See each turn |
| **Engagement** | ?? Boring wait | ?? Exciting to watch |
| **Implementation** | Simple JSON | SSE events |
| **Use Case** | Batch/API | Live demos |

---

## ?? Event Flow

### **Example: Creating a Joke about "Programming"**

```
Client ? Server: GET /api/jokes/stream?topic=programming
    ?
Server ? Client: event: status
                 data: {"message": "Starting workflow..."}
    ?
Server ? Client: event: user_message
                 data: {"content": "Create joke about programming"}
    ?
Server ? Client: event: agent_chunk
                 data: {"agent": "JokeCreator", "content": "Why did"}
    ?
Server ? Client: event: agent_chunk
                 data: {"agent": "JokeCreator", "content": " the programmer"}
    ?
Server ? Client: event: agent_chunk
                 data: {"agent": "JokeCreator", "content": " quit?"}
    ?
Server ? Client: event: agent_message_complete
                 data: {"agent": "JokeCreator"}
    ?
Server ? Client: event: agent_chunk
                 data: {"agent": "JokeCritic", "content": "Rating: 6/10..."}
    ?
Server ? Client: event: iteration_complete
                 data: {"iteration": 1, "rating": 6, "success": false}
    ?
[Repeat for iteration 2...]
    ?
Server ? Client: event: workflow_complete
                 data: {"success": true, "finalRating": 8}
```

---

## ?? Implementation Details

### **Server: Streaming Endpoint**

```csharp
app.MapGet("/api/jokes/stream", async (HttpContext context, string? topic) =>
{
    // Set SSE headers
    context.Response.Headers.Append("Content-Type", "text/event-stream");
    context.Response.Headers.Append("Cache-Control", "no-cache");
    
    // Helper to send events
    async Task SendEvent(string eventType, object data)
    {
        var json = JsonSerializer.Serialize(data);
        await context.Response.WriteAsync($"event: {eventType}\n");
        await context.Response.WriteAsync($"data: {json}\n\n");
        await context.Response.Body.FlushAsync();
    }
    
    // Execute workflow and stream events
    await foreach (WorkflowEvent evt in run.WatchStreamAsync())
    {
        if (evt is AgentRunUpdateEvent update)
        {
            // Stream each chunk to client
            await SendEvent("agent_chunk", new {
                agent = update.ExecutorId,
                content = update.AsResponse().Text
            });
        }
        else if (evt is WorkflowOutputEvent output)
        {
            // Send final results
            await SendEvent("workflow_complete", new {
                success = true,
                finalJoke = "...",
                finalRating = 8
            });
        }
    }
});
```

### **Client: EventSource**

```javascript
const eventSource = new EventSource('/api/jokes/stream?topic=cats');

let currentMessage = null;

// Handle streaming chunks
eventSource.addEventListener('agent_chunk', (e) => {
    const data = JSON.parse(e.data);
    
    // Create or update message element
    if (!currentMessage || currentMessage.agent !== data.agent) {
        currentMessage = createMessageElement(data.agent);
    }
    
    // Append text chunk
    currentMessage.element.textContent += data.content;
});

// Handle completion
eventSource.addEventListener('workflow_complete', (e) => {
    const data = JSON.parse(e.data);
    showFinalResults(data);
    eventSource.close();
});
```

---

## ?? UI Features

### **1. Real-Time Message Streaming**
- Messages appear in chat-like interface
- Smooth typing animation as text streams
- Agent avatars and names
- Color-coded by agent type

### **2. Status Bar**
- Shows current workflow state
- Updates in real-time
- Visual feedback (colors, icons)

### **3. Iteration Tracking**
- Live iteration counter
- Current rating display
- Success/pending indicators

### **4. Session Statistics**
- Total iterations
- Current rating
- Workflow status

---

## ?? Running the Demo

### **Start the Application**
```bash
cd JokeAgentsDemo
dotnet run
```

### **Access the Demos**
1. **Landing Page**: http://localhost:5000
   - Choose between Streaming or Classic
   
2. **Streaming Demo**: http://localhost:5000/streaming.html
   - Watch agents collaborate live!
   
3. **Classic Demo**: http://localhost:5000/classic.html
   - Traditional batch results

---

## ?? For Lectures/Demos

### **Recommended Flow** (15 minutes)

**Minutes 0-2: Introduction**
- Show landing page
- Explain two viewing modes
- Emphasize the "real-time" aspect

**Minutes 2-5: Streaming Demo**
- Open `/streaming.html`
- Enter topic: "programming"
- Let audience watch the live collaboration
- **Key observation points**:
  - "See how JokeCreator is writing?"
  - "Watch the Critic evaluate it live!"
  - "Notice the iteration counter updating?"

**Minutes 5-8: Classic Demo**
- Switch to `/classic.html`
- Same topic for comparison
- Show comprehensive results at end
- **Compare**: "Notice the difference?"

**Minutes 8-12: Code Walkthrough**
- Show SSE endpoint in `Program.cs`
- Explain `WorkflowEvent` streaming
- Show `agent_chunk` events
- Explain EventSource in HTML

**Minutes 12-15: Q&A**
- SSE vs WebSockets
- Production considerations
- Scaling streaming endpoints

---

## ?? Benefits of Streaming

### **User Experience**
- ? **Engaging** - Users stay interested
- ? **Transparent** - See what agents are doing
- ? **Educational** - Learn how agents collaborate
- ? **Modern** - Matches ChatGPT expectations

### **Technical**
- ? **Simple** - Built-in browser support (EventSource)
- ? **Reliable** - Automatic reconnection
- ? **Efficient** - Unidirectional, text-based
- ? **Scalable** - HTTP/1.1 or HTTP/2 compatible

### **Demonstration**
- ? **Compelling** - Much more interesting to watch
- ? **Clear** - Shows agent workflow visually
- ? **Professional** - Production-quality UX
- ? **Memorable** - Audience remembers the experience

---

## ?? Customization

### **Add More Event Types**

```csharp
// Send custom events
await SendEvent("agent_thinking", new {
    agent = "JokeCreator",
    status = "generating"
});

await SendEvent("quality_gate", new {
    rating = 6,
    threshold = 8,
    passed = false
});
```

### **Enhance UI**

```javascript
// Add typing indicator
eventSource.addEventListener('agent_thinking', (e) => {
    showTypingIndicator(e.data.agent);
});

// Add progress bar
eventSource.addEventListener('iteration_complete', (e) => {
    updateProgressBar(e.data.iteration, maxIterations);
});
```

---

## ?? Production Considerations

### **Scaling**
- SSE connections are long-lived HTTP connections
- Use load balancer with sticky sessions
- Consider Redis for distributed state

### **Timeout Handling**
```csharp
// Add timeout to prevent hung connections
context.RequestAborted.Register(() => {
    logger.LogInformation("Client disconnected");
});
```

### **Error Handling**
```csharp
try {
    await SendEvent("agent_chunk", data);
}
catch (Exception ex) {
    logger.LogError(ex, "Failed to send event");
    await SendEvent("error", new { message = ex.Message });
}
```

---

## ?? References

- **Server-Sent Events (SSE)**: https://developer.mozilla.org/en-US/docs/Web/API/Server-sent_events
- **EventSource API**: https://developer.mozilla.org/en-US/docs/Web/API/EventSource
- **MAF Workflows**: https://learn.microsoft.com/en-us/agent-framework/user-guide/workflows/
- **AG-UI Protocol**: https://github.com/ag-ui-protocol/ag-ui

---

## ?? Summary

The JokeAgentsDemo now provides:
- ? **Real-time streaming** via SSE
- ? **Dual interfaces** (Streaming + Classic)
- ? **ChatGPT-style UX** for live collaboration
- ? **Educational value** - see agents work together
- ? **Production-ready** streaming architecture

**Perfect for demonstrating MAF Group Chat workflows in an engaging, modern way!** ???
