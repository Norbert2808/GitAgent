namespace GitAgent.Shared.DTOs;

public class GetCommitsRequest
{
    public string Repository { get; set; } = string.Empty;
    public string Branch { get; set; } = string.Empty;
    public int Count { get; set; } = 10;
}
