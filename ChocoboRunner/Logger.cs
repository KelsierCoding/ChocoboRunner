using System.Runtime.CompilerServices;

namespace ChocoboRunner;

public static class Logger
{
    private static readonly string LogFilePath = Path.Combine(AppContext.BaseDirectory, "app.log");
    private static readonly Lock LockObject = new Lock();

    static Logger()
    {
        try
        {
            File.WriteAllText(LogFilePath, $"--- Log Started at {DateTime.Now} ---{Environment.NewLine}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to initialize log file: {ex.Message}");
        }
    }

    public static void Info(
        string message,
        [CallerFilePath] string sourceFilePath = "",
        [CallerMemberName] string memberName = "")
    {
        Log("INFO", message, sourceFilePath, memberName);
    }

    public static void Warning(
        string message,
        [CallerFilePath] string sourceFilePath = "",
        [CallerMemberName] string memberName = "")
    {
        Log("WARN", message, sourceFilePath, memberName);
    }

    public static void Error(
        string message,
        [CallerFilePath] string sourceFilePath = "",
        [CallerMemberName] string memberName = "")
    {
        Log("ERROR", message, sourceFilePath, memberName);
    }

    private static void Log(string level, string message, string filePath, string methodName)
    {
        lock (LockObject)
        {
            try
            {
                string fileName = Path.GetFileName(filePath);

                string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] [{fileName} -> {methodName}()] {message.Replace(AppEnvironment.Home, "/home/{user}")}{Environment.NewLine}";
                
                File.AppendAllText(LogFilePath, logEntry);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to write to log file: {ex.Message}");
            }
        }
    }
}