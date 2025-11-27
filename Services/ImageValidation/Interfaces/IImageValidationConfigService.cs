using MantenimientosTI.Models.ImageValidation;

namespace MantenimientosTI.Services.ImageValidation.Interfaces
{
    public interface IImageValidationConfigService
    {
        Task<string> GetConfigValueAsync(string configKey, string defaultValue = "");
        Task<int> GetConfigValueIntAsync(string configKey, int defaultValue = 0);
        Task<bool> GetConfigValueBoolAsync(string configKey, bool defaultValue = false);
        Task UpdateConfigValueAsync(string configKey, string configValue, string updatedBy = "Sistema");
        Task EnsureDefaultConfigurationAsync();
    }
}