# CherryPickSmart

CherryPickSmart is a .NET command-line tool designed to analyze commits between branches in a Git repository. It provides detailed insights into commit history, merge status, ticket associations, and orphaned commits.

## Features

- Analyze commits between source and target branches.
- Identify regular and merge commits.
- Detect complete and incomplete merges.
- Extract and map tickets from commit messages based on configurable ticket prefixes.
- Show detailed orphan commit analysis.

## Configuration

Configuration is stored in a JSON file located at:

```
%USERPROFILE%\.cherry-pick-smart\config.json
```

Example configuration options:

- `JiraUrl`: URL of the Jira instance.
- `JiraUsername`: Username for Jira API.
- `JiraApiToken`: API token for Jira.
- `TicketPrefixes`: List of ticket prefixes to recognize (default: `["HSAMED"]`).
- `DefaultFromBranch`: Default source branch (default: `deploy/dev`).
- `DefaultToBranch`: Default target branch (default: `deploy/uat`).

## Usage

Build the project:

```bash
dotnet build CherryPickSmart/CherryPickSmart.csproj
```

Run analysis:

```bash
dotnet run --project CherryPickSmart/CherryPickSmart.csproj -- analyze -f <from_branch> -t <to_branch> -r <repository_path> [-o]
```

- `-f`, `--from`: Source branch (required).
- `-t`, `--to`: Target branch (required).
- `-r`, `--repo`: Path to the Git repository (required).
- `-o`, `--orphans`: Optional flag to show detailed orphan commit analysis.

Example:

```bash
dotnet run --project CherryPickSmart/CherryPickSmart.csproj -- analyze -f deploy/dev -t deploy/uat -r "D:\repos\hsa-share\medics_applications" -o
```

## License

MIT License
