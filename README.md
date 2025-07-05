# CherryPickAnalyzer

A powerful .NET command-line tool for analyzing Git deployment differences and automating cherry-pick operations between branches.

## Features

- **Analyze Deployment Differences**: Compare commits between source and target branches to identify what needs to be deployed
- **Cherry-Pick Automation**: Generate and optionally execute cherry-pick commands for selected commits
- **Multiple Output Formats**: Support for table, JSON, and Markdown output formats
- **Interactive Mode**: Interactive cherry-pick selection for precise control
- **Rich Console Output**: Beautiful terminal output using Spectre.Console
- **Git Integration**: Built-in Git operations using LibGit2Sharp

## Prerequisites

- .NET 8.0 SDK or later
- Git installed and accessible from command line

## Installation

### Build from Source

1. Clone the repository:
```bash
git clone <repository-url>
cd CherryPickAnalyzer
```

2. Build the project:
```bash
dotnet build
```

3. Run the tool:
```bash
dotnet run --project src/CherryPickAnalyzer -- [command] [options]
```

### Create a Standalone Executable

To create a standalone executable:

```bash
dotnet publish src/CherryPickAnalyzer -c Release -r win-x64 --self-contained
```

Replace `win-x64` with your target runtime identifier (`linux-x64`, `osx-x64`, etc.).

## Usage

The tool provides two main commands: `analyze` and `cherry-pick`.

### Analyze Command

Analyze deployment differences between branches:

```bash
dotnet run --project src/CherryPickAnalyzer -- analyze -s <source-branch> -t <target-branch> [options]
```

#### Options

- `-r, --repo <path>` - Path to git repository (default: current directory)
- `-s, --source <branch>` - Source branch (contains new changes) **[Required]**
- `-t, --target <branch>` - Target branch (deployment target) **[Required]**
- `--remote <remote>` - Remote to fetch from (default: origin)
- `--no-fetch` - Skip fetching latest changes
- `--format <format>` - Output format: table, json, markdown (default: table)
- `-v, --verbose` - Verbose output
- `--timeout <seconds>` - Timeout in seconds (default: 300)

#### Examples

```bash
# Analyze differences between develop and main branches
dotnet run --project src/CherryPickAnalyzer -- analyze -s develop -t main

# Output as JSON format
dotnet run --project src/CherryPickAnalyzer -- analyze -s develop -t main --format json

# Analyze with verbose output
dotnet run --project src/CherryPickAnalyzer -- analyze -s develop -t main -v
```

### Cherry-Pick Command

Generate cherry-pick commands for commits:

```bash
dotnet run --project src/CherryPickAnalyzer -- cherry-pick -s <source-branch> -t <target-branch> [options]
```

#### Options

- `-r, --repo <path>` - Path to git repository (default: current directory)
- `-s, --source <branch>` - Source branch **[Required]**
- `-t, --target <branch>` - Target branch **[Required]**
- `--execute` - Execute cherry-pick commands automatically
- `--interactive` - Interactive cherry-pick selection

#### Examples

```bash
# Generate cherry-pick commands
dotnet run --project src/CherryPickAnalyzer -- cherry-pick -s develop -t main

# Interactive cherry-pick selection
dotnet run --project src/CherryPickAnalyzer -- cherry-pick -s develop -t main --interactive

# Execute cherry-pick commands automatically
dotnet run --project src/CherryPickAnalyzer -- cherry-pick -s develop -t main --execute
```

## Project Structure

```
CherryPickAnalyzer/
├── src/
│   └── CherryPickAnalyzer/
│       ├── Models/              # Data models
│       ├── Options/             # Command-line option classes
│       ├── Program.cs           # Main entry point
│       ├── GitDeploymentCli.cs  # Main CLI logic
│       ├── GitCommandExecutor.cs # Git command execution
│       ├── CherryPickHelper.cs  # Cherry-pick utilities
│       ├── CommitParser.cs      # Commit parsing utilities
│       ├── BranchValidator.cs   # Branch validation
│       ├── AnalysisDisplay.cs   # Analysis output formatting
│       └── RepositoryInfoDisplay.cs # Repository info display
├── CherryPickAnalyzer.sln       # Solution file
└── README.md                    # This file
```

## Dependencies

- **CommandLineParser** (2.9.1) - Command-line argument parsing
- **LibGit2Sharp** (0.31.0) - Git operations
- **Spectre.Console** (0.50.0) - Rich console output
- **CliWrap** (3.9.0) - Command execution

## Development

### Building

```bash
dotnet build
```

### Running Tests

```bash
dotnet test
```

### Code Formatting

The project uses standard .NET formatting conventions. To format code:

```bash
dotnet format
```

## Contributing

1. Create a feature branch
2. Make your changes
3. Add tests if applicable
4. Submit a pull request