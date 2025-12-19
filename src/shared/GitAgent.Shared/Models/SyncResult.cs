namespace GitAgent.Shared.Models;

public class SyncResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool HasConflicts { get; set; }
    public List<string> ConflictDetails { get; set; } = new();
}
