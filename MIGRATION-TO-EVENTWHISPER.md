# Migration from WinLog-MCP to EventWhisper

## Summary of Changes

This document tracks the migration from the WinLog-MCP server to EventWhisper for Windows Event Log access via MCP.

## ? Completed Actions

### 1. Cleanup of Old Files

**Removed WinLog-MCP Installation:**
- ? `C:\Users\alon.HOME\mcp-tools\winlog-mcp\` (entire directory)
- ? `C:\Users\alon.HOME\mcp-tools\logs\*.log` (old log files)

**Removed Old Scripts:**
- ? `Setup/setup-winlog-mcp.ps1`
- ? `Setup/test-mcp-server.py`

### 2. New EventWhisper Integration

**Added Files:**
- ? `Setup/setup-eventwhisper.ps1` - Complete setup script with Poetry installation

**Updated Files:**
- ? `ComputerUsageAgent/Program.cs` - Updated to use EventWhisper with Poetry
- ? `README.md` - Updated documentation to reference EventWhisper

### 3. Key Configuration Changes

**Old Configuration (WinLog-MCP):**
```json
{
  "mcpServerPath": "C:\\...\\winlog-mcp\\src\\main.py",
  "logsPath": "C:\\...\\logs"
}
```

**New Configuration (EventWhisper):**
```json
{
  "mcpServerType": "EventWhisper",
  "mcpServerPath": "C:\\Users\\...\\mcp-tools\\eventwhisper",
  "installDate": "..."
}
```

**Old MCP Command:**
```csharp
Command = "python",
Arguments = [serverPath, "--storage-path", logsPath]
```

**New MCP Command:**
```csharp
Command = "poetry",
Arguments = ["-C", serverPath, "run", "python", "-m", "eventwhisper.mcp.server"]
```

## ?? Why EventWhisper?

### Advantages over WinLog-MCP

| Feature | WinLog-MCP | EventWhisper |
|---------|------------|--------------|
| **Protocol Handling** | Basic stdio | Robust MCP protocol implementation |
| **Performance** | Moderate | Fast EVTX parsing |
| **Safety** | PowerShell execution | Pure Python, no shell execution |
| **Filtering** | Basic | Advanced (time, EventID, keywords) |
| **Design Focus** | General logging | DFIR/IR/Threat hunting |
| **Communication** | Had hanging issues | Proper MCP protocol compliance |
| **Maintenance** | Less active | Actively maintained |

### Technical Improvements

1. **No More Hanging**
   - EventWhisper properly implements MCP protocol
   - Clean stdio communication without deadlocks

2. **Better Tool Design**
   - `list_evtx_files`: Discover available log files
   - `filter_events`: Targeted filtering with multiple criteria
   - Field projection for efficient data transfer

3. **Professional Implementation**
   - Uses Poetry for dependency management
   - Comprehensive test coverage
   - Built specifically for incident response

## ?? Fresh Installation Steps

On a new computer, follow these steps:

### 1. Prerequisites
```powershell
# Install via winget (or manual)
winget install Microsoft.DotNet.SDK.10
winget install Python.Python.3.12
winget install Git.Git
```

### 2. Clone Repository
```powershell
git clone https://github.com/alonf/AIForCSharpDev.git
cd AIForCSharpDev
```

### 3. Azure Authentication
```powershell
az login
```

### 4. Setup EventWhisper MCP Server
```powershell
# Run as Administrator for Event Log access
cd Setup
.\setup-eventwhisper.ps1
```

**What the script does:**
1. ? Checks/installs Python
2. ? Checks/installs Git  
3. ? Installs Poetry (Python package manager)
4. ? Clones EventWhisper from GitHub
5. ? Installs all Python dependencies via Poetry
6. ? Tests the MCP server
7. ? Saves configuration to `mcp-config.json`

### 5. Run ComputerUsageAgent
```powershell
# Run as Administrator
cd ..\ComputerUsageAgent
dotnet run
```

## ?? Configuration Details

### Setup Script Features

The `setup-eventwhisper.ps1` script is designed for fresh installations:

**Automatic Installation:**
- Python (if not installed)
- Git (if not installed)
- Poetry (Python package manager)
- EventWhisper and all dependencies

**Error Handling:**
- Checks for existing installations
- Provides clear error messages
- Graceful fallbacks
- Comprehensive status reporting

**Path Configuration:**
- Stores EventWhisper in `%USERPROFILE%\mcp-tools\eventwhisper`
- Automatically detects Poetry installation path
- Saves config to `Setup/mcp-config.json`

### Running on Different Computers

The setup is designed to work on any Windows computer:

1. **No hardcoded paths** - Uses `%USERPROFILE%` environment variable
2. **Automatic dependency installation** - Script installs everything needed
3. **Poetry management** - Handles virtual environment automatically
4. **Config file** - Stores paths in `mcp-config.json` for runtime discovery

## ?? Verification

After setup, verify the installation:

```powershell
# Check Poetry is installed
poetry --version

# Check EventWhisper location
Test-Path $env:USERPROFILE\mcp-tools\eventwhisper

# Check configuration
Get-Content Setup\mcp-config.json | ConvertFrom-Json
```

## ?? Learning Points

This migration demonstrates:

1. **MCP Server Interoperability**
   - Can swap MCP servers without changing agent code much
   - Standardized protocol enables tool ecosystem

2. **Production Considerations**
   - Choose servers with proper protocol implementation
   - Consider maintenance and community support
   - Evaluate performance and safety requirements

3. **Setup Automation**
   - Automated setup reduces onboarding friction
   - Dependency management (Poetry) ensures reproducibility
   - Configuration files enable portable setups

## ?? Additional Resources

- **EventWhisper GitHub**: https://github.com/Hexastrike/EventWhisper
- **Poetry Documentation**: https://python-poetry.org/docs/
- **MCP Specification**: https://spec.modelcontextprotocol.io/
- **Windows Event Logs**: https://learn.microsoft.com/en-us/windows/win32/eventlog/event-logging

## ? Migration Complete!

The system is now configured to use EventWhisper, which provides:
- ? Robust MCP protocol implementation (no hanging)
- ? Professional-grade Windows Event Log access
- ? Better performance and safety
- ? Designed for incident response and forensics
- ? Active maintenance and support

All old WinLog-MCP files have been removed, and the system is clean for fresh deployments.
