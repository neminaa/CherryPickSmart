namespace CherryPickSmart.Commands;

public interface ICommand
{
    /// <summary>
    /// Executes the command with the provided services.
    /// </summary>
    /// <param name="services">The service provider for dependency injection.</param>
    /// <returns>An integer representing the exit code (0 for success, non-zero for failure).</returns>
    Task<int> ExecuteAsync(IServiceProvider services);
}