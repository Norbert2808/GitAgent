namespace GitAgent.Shared.DTOs;

public class PushBranchRequest
{
    public string Branch { get; set; } = string.Empty;
    public string FromRepository { get; set; } = string.Empty;
    public string ToRepository { get; set; } = string.Empty;
    public bool Force { get; set; }
}
