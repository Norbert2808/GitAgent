# Git Agent

Git Agent - a tool for synchronizing branches between two git repositories through a web interface.

## Architecture

The project consists of three components:

- **GitAgent.Server** - ASP.NET Core web server with API and UI
- **GitAgent.Worker** - Worker service for git repository operations
- **GitAgent.Shared** - Shared models

## Technologies

- .NET 8.0
- SignalR for Server ↔ Worker communication
- LibGit2Sharp for git operations
- Vanilla JS for web interface
