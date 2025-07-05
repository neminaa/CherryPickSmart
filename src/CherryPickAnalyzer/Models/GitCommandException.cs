using System;

namespace GitCherryHelper.Models;

public class GitCommandException : Exception
{
    public GitCommandException(string message) : base(message)
    {
    }

    public GitCommandException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public GitCommandException(string message, string command, int exitCode) : base(message)
    {
        Command = command;
        ExitCode = exitCode;
    }

    public GitCommandException(string message, string command, int exitCode, Exception innerException) : base(message, innerException)
    {
        Command = command;
        ExitCode = exitCode;
    }

    public string Command { get; }
    public int ExitCode { get; }
}