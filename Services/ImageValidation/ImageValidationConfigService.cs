using MantenimientosTI.Models;
using MantenimientosTI.Models.ImageValidation;
using MantenimientosTI.Services.ImageValidation.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MantenimientosTI.Services.ImageValidation
{
    public class ImageValidationConfigService : IImageValidationConfigService
    {
        private readonly MantenimientosTIContext _context;
        private readonly ILogger<ImageValidationConfigService> _logger;
        private readonly Dictionary<string, string> _configCache = new();
        private DateTime _lastCacheUpdate = DateTime.MinValue;

        public ImageValidationConfigService(MantenimientosTIContext context, ILogger<ImageValidationConfigService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<string> GetConfigValueAsync(string configKey, string defaultValue = "")
        {
            await EnsureCacheUpdatedAsync();

            if (_configCache.TryGetValue(configKey, out var value))
                return value;

            _logger.LogWarning("Configuración no encontrada: {ConfigKey}, usando valor por defecto: {DefaultValue}",
                configKey, defaultValue);
            return defaultValue;
        }

        public async Task<int> GetConfigValueIntAsync(string configKey, int defaultValue = 0)
        {
            var value = await GetConfigValueAsync(configKey, defaultValue.ToString());
            return int.TryParse(value, out int result) ? result : defaultValue;
        }

        public async Task<bool> GetConfigValueBoolAsync(string configKey, bool defaultValue = false)
        {
            var value = await GetConfigValueAsync(configKey, defaultValue.ToString());
            return bool.TryParse(value, out bool result) ? result : defaultValue;
        }

        public async Task UpdateConfigValueAsync(string configKey, string configValue, string updatedBy = "Sistema")
        {
            var config = await _context.ImageValidationConfigs
                .FirstOrDefaultAsync(c => c.ConfigKey == configKey);

            if (config != null)
            {
                config.ConfigValue = configValue;
                config.UpdatedBy = updatedBy;
                config.UpdatedAt = DateTime.Now;
            }
            else
            {
                config = new ImageValidationConfig
                {
                    ConfigKey = configKey,
                    ConfigValue = configValue,
                    Description = GetDefaultDescription(configKey),
                    UpdatedBy = updatedBy,
                    UpdatedAt = DateTime.Now
                };
                _context.ImageValidationConfigs.Add(config);
            }

            await _context.SaveChangesAsync();

            _configCache[configKey] = configValue;
            _logger.LogInformation("Configuración actualizada: {ConfigKey} = {ConfigValue}", configKey, configValue);
        }

        public async Task EnsureDefaultConfigurationAsync()
        {
            var defaultConfigs = new[]
            {
                new { Key = ImageValidationConfig.DuplicateThreshold, Value = "8", Desc = "Distancia Hamming máxima para considerar duplicado (0-64)" },
                new { Key = ImageValidationConfig.MaxImageSizeKB, Value = "512", Desc = "Tamaño máximo de imagen en KB" },
                new { Key = ImageValidationConfig.SimilarityThreshold, Value = "85", Desc = "Umbral de similitud para considerar alto riesgo" },
                new { Key = ImageValidationConfig.SearchMonthsBack, Value = "3", Desc = "Meses hacia atrás para buscar duplicados" },
                new { Key = ImageValidationConfig.MaxSearchResults, Value = "100", Desc = "Máximo número de imágenes a comparar" },
                new { Key = ImageValidationConfig.RiskScoreHigh, Value = "70", Desc = "Puntaje para considerar riesgo alto" },
                new { Key = ImageValidationConfig.RiskScoreMedium, Value = "40", Desc = "Puntaje para considerar riesgo medio" }
            };

            foreach (var config in defaultConfigs)
            {
                var exists = await _context.ImageValidationConfigs
                    .AnyAsync(c => c.ConfigKey == config.Key);

                if (!exists)
                {
                    await UpdateConfigValueAsync(config.Key, config.Value, "Sistema-Inicial");
                    _logger.LogInformation("Configuración por defecto creada: {ConfigKey}", config.Key);
                }
            }
        }

        private async Task EnsureCacheUpdatedAsync()
        {
            if (DateTime.Now - _lastCacheUpdate > TimeSpan.FromMinutes(5))
            {
                var configs = await _context.ImageValidationConfigs
                    .ToDictionaryAsync(c => c.ConfigKey, c => c.ConfigValue);

                _configCache.Clear();
                foreach (var config in configs)
                {
                    _configCache[config.Key] = config.Value;
                }

                _lastCacheUpdate = DateTime.Now;
                _logger.LogDebug("Caché de configuración actualizada: {Count} entradas", _configCache.Count);
            }
        }

        private string GetDefaultDescription(string configKey)
        {
            return configKey switch
            {
                ImageValidationConfig.DuplicateThreshold => "Distancia Hamming máxima para considerar duplicado (0-64)",
                ImageValidationConfig.MaxImageSizeKB => "Tamaño máximo de imagen en KB",
                ImageValidationConfig.SimilarityThreshold => "Umbral de similitud para considerar alto riesgo",
                ImageValidationConfig.SearchMonthsBack => "Meses hacia atrás para buscar duplicados",
                ImageValidationConfig.MaxSearchResults => "Máximo número de imágenes a comparar",
                ImageValidationConfig.RiskScoreHigh => "Puntaje para considerar riesgo alto",
                ImageValidationConfig.RiskScoreMedium => "Puntaje para considerar riesgo medio",
                _ => "Configuración del sistema de validación de imágenes"
            };
        }
    }
}