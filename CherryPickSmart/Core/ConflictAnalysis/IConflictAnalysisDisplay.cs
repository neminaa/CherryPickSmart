using Spectre.Console;

namespace CherryPickSmart.Core.ConflictAnalysis;

/// <summary>
/// Interface for displaying conflict analysis progress
/// </summary>
public interface IConflictAnalysisDisplay
{
    /// <summary>
    /// Called when analysis starts
    /// </summary>
    void OnAnalysisStarted(int totalFiles);

    /// <summary>
    /// Called when a file has been analyzed
    /// </summary>
    void OnFileAnalyzed(string file, ConflictPrediction? prediction);

    /// <summary>
    /// Called when analysis is complete
    /// </summary>
    void OnAnalysisCompleted(List<ConflictPrediction> predictions);

    /// <summary>
    /// Called when an error occurs
    /// </summary>
    void OnError(string file, string error);

    /// <summary>
    /// Called to update progress
    /// </summary>
    void OnProgressUpdate(ConflictAnalysisProgress progress);
}

/// <summary>
/// Console implementation of conflict analysis display using Spectre.Console
/// </summary>
public class SpectreConflictAnalysisDisplay : IConflictAnalysisDisplay
{
    private readonly Tree _directoryTree;
    private readonly Dictionary<string, TreeNode> _directoryNodes = new();
    private readonly LiveDisplayContext? _context;
    private readonly TreeNode _progressNode;
    private int _totalFiles;
    private int _processedFiles;
    private int _conflictsFound;

    public SpectreConflictAnalysisDisplay(LiveDisplayContext? context = null)
    {
        _context = context;
        _directoryTree = new Tree("[yellow]Conflict Analysis[/]");
        _progressNode = _directoryTree.AddNode("");
    }

    public void OnAnalysisStarted(int totalFiles)
    {
        _totalFiles = totalFiles;
        _processedFiles = 0;
        _conflictsFound = 0;
        UpdateProgressDisplay();
    }

    public void OnFileAnalyzed(string file, ConflictPrediction? prediction)
    {
        _processedFiles++;

        if (prediction != null)
        {
            _conflictsFound++;
            AddFileToTree(file, prediction);
        }

        UpdateProgressDisplay();
    }

    public void OnAnalysisCompleted(List<ConflictPrediction> predictions)
    {
        _progressNode.Nodes.Clear();
        _progressNode.AddNode($"[green]Analysis complete![/] Found [yellow]{predictions.Count}[/] conflicts");
        _context?.Refresh();
    }

    public void OnError(string file, string error)
    {
        AnsiConsole.MarkupLine($"[red]Error analyzing {file}:[/] {error}");
    }

    public void OnProgressUpdate(ConflictAnalysisProgress progress)
    {
        _processedFiles = progress.ProcessedFiles;
        _conflictsFound = progress.FoundConflicts;
        UpdateProgressDisplay();
    }

    private void UpdateProgressDisplay()
    {
        _progressNode.Nodes.Clear();
        var percentage = _totalFiles > 0 ? (_processedFiles * 100.0 / _totalFiles) : 0;
        _progressNode.AddNode($"[green]{_processedFiles}[/] of [blue]{_totalFiles}[/] files " +
                            $"([yellow]{percentage:F0}%[/]) - " +
                            $"[red]{_conflictsFound}[/] conflicts found");
        _context?.Refresh();
    }

    private void AddFileToTree(string file, ConflictPrediction prediction)
    {
        var dir = Path.GetDirectoryName(file) ?? "/";
        var node = EnsureDirectoryNode(dir);

        var emoji = GetTypeEmoji(prediction.Type);
        var color = GetRiskColor(prediction.Risk);
        var fileName = Path.GetFileName(file);

        node.AddNode($"{emoji} [{color}]{fileName}[/] - {prediction.Risk} | {prediction.ConflictingCommits.Count} commits");
    }

    private TreeNode EnsureDirectoryNode(string directory)
    {
        if (_directoryNodes.TryGetValue(directory, out var existing))
            return existing;

        var parts = directory.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        
        if(parts.Length == 0)
        {
            parts = [""]; // Ensure root directory is handled
        }
        
        TreeNode? parent = null;
        var currentPath = "";

        foreach (var part in parts)
        {
            currentPath = string.IsNullOrEmpty(currentPath) ? part : Path.Combine(currentPath, part);

            if (!_directoryNodes.TryGetValue(currentPath, out var node))
            {
                node = parent == null
                    ? _directoryTree.AddNode($"[blue]{part}/[/]")
                    : parent.AddNode($"[blue]{part}/[/]");

                _directoryNodes[currentPath] = node;
            }

            parent = node;
        }

        return parent!;
    }

    private static string GetRiskColor(ConflictRisk risk) => risk switch
    {
        ConflictRisk.Certain => "red bold",
        ConflictRisk.High => "red",
        ConflictRisk.Medium => "yellow",
        _ => "green"
    };

    private static string GetTypeEmoji(ConflictType type) => type switch
    {
        ConflictType.BinaryFile => "📦",
        ConflictType.ImportConflict => "📚",
        ConflictType.StructuralChange => "🏗️",
        ConflictType.SemanticConflict => "🧠",
        ConflictType.FileRenamed => "📝",
        _ => "⚡"
    };

    public Tree GetTree() => _directoryTree;
}

/// <summary>
/// Null implementation for when no display is needed
/// </summary>
public class NullConflictAnalysisDisplay : IConflictAnalysisDisplay
{
    public void OnAnalysisStarted(int totalFiles) { }
    public void OnFileAnalyzed(string file, ConflictPrediction? prediction) { }
    public void OnAnalysisCompleted(List<ConflictPrediction> predictions) { }
    public void OnError(string file, string error) { }
    public void OnProgressUpdate(ConflictAnalysisProgress progress) { }
}