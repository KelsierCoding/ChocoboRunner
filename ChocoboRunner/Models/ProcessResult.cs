namespace ChocoboRunner.Models;

public class ProcessResult
{
    public int ExitCode { get; set; }
    public string StdOut { get; set; } = string.Empty;
    public string StdErr { get; set; } = string.Empty;
    public bool TimedOut { get; set; } = false;
}