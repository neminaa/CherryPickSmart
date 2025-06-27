namespace CherryPickSmart.Services.Interfaces
{
    public interface IConfigurationService
    {
        /// <summary>
        /// Gets the configuration file path
        /// </summary>
        string ConfigFilePath { get; }
        
        /// <summary>
        /// Load configuration from file
        /// </summary>
        Task<T?> LoadConfigAsync<T>() where T : class, new();
        
        /// <summary>
        /// Save configuration to file
        /// </summary>
        Task SaveConfigAsync<T>(T config) where T : class;
        
        /// <summary>
        /// Check if configuration exists
        /// </summary>
        bool ConfigExists();
    }
}
