using MantenimientosTI.Models.ImageValidation;
using MantenimientosTI.Services.ImageValidation.Interfaces;
using Microsoft.EntityFrameworkCore;
using MantenimientosTI.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;

namespace MantenimientosTI.Services.ImageValidation
{
    public class BasicImageValidator : IImageValidator
    {
        private readonly MantenimientosTIContext _context;
        private readonly IPerceptualHashService _hashService;
        private readonly IImageValidationConfigService _configService;
        private readonly ILogger<BasicImageValidator> _logger;

        public BasicImageValidator(
            MantenimientosTIContext context,
            IPerceptualHashService hashService,
            IImageValidationConfigService configService,
            ILogger<BasicImageValidator> logger)
        {
            _context = context;
            _hashService = hashService;
            _configService = configService;
            _logger = logger;
        }

        public async Task<ImageValidationResult> ValidateImageAsync(IFormFile image, string userId, int mantenimientoId, string tipoFoto)
        {
            var result = new ImageValidationResult();

            try
            {
                // VALIDACIONES DE ENTRADA CRÍTICAS
                if (image == null || image.Length == 0)
                {
                    result.ValidationStatus = "Error";
                    result.Warnings.Add("Imagen vacía o nula");
                    return result;
                }

                // Validar tipo MIME
                if (!image.ContentType.StartsWith("image/"))
                {
                    result.ValidationStatus = "Error";
                    result.Warnings.Add("Archivo no es una imagen válida");
                    return result;
                }

                // ✅ CORREGIDO: Usar configuración para tamaño máximo
                var maxSizeKB = await _configService.GetConfigValueIntAsync(ImageValidationConfig.MaxImageSizeKB, 512);
                var maxSizeBytes = maxSizeKB * 1024;

                if (image.Length > maxSizeBytes)
                {
                    result.ValidationStatus = "Error";
                    result.Warnings.Add($"La imagen excede el tamaño máximo de {maxSizeKB}KB");
                    return result;
                }

                _logger.LogInformation("Iniciando validación imagen: User={UserId}, Tipo={TipoFoto}, Tamaño={SizeBytes}, FileName={FileName}",
                    userId, tipoFoto, image.Length, image.FileName);

                // GENERAR HASH PERCEPTUAL CON MANEJO DE EXCEPCIONES ESPECÍFICO
                string perceptualHash;
                try
                {
                    using var stream = image.OpenReadStream();
                    perceptualHash = await _hashService.GeneratePerceptualHashAsync(stream);
                }
                catch (UnknownImageFormatException ex)
                {
                    _logger.LogWarning(ex, "Formato de imagen no soportado: {FileName}", image.FileName);
                    result.ValidationStatus = "Error";
                    result.Warnings.Add("Formato de imagen no compatible");
                    return result;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error al procesar imagen: {FileName}", image.FileName);
                    result.ValidationStatus = "Error";
                    result.Warnings.Add("Error al procesar la imagen");
                    return result;
                }

                // VALIDAR HASH GENERADO
                if (string.IsNullOrEmpty(perceptualHash) || perceptualHash.Length != 64)
                {
                    _logger.LogWarning("Hash perceptual inválido generado para imagen: {FileName}", image.FileName);
                    result.ValidationStatus = "Error";
                    result.Warnings.Add("No se pudo generar un identificador único para la imagen");
                    return result;
                }

                result.PerceptualHash = perceptualHash;

                // BUSCAR DUPLICADOS
                var duplicates = await FindDuplicatesAsync(perceptualHash, userId, mantenimientoId);
                result.Duplicates = duplicates;

                // ✅ CORREGIDO: Usar configuración para cálculo de riesgo
                var riskScoreHigh = await _configService.GetConfigValueIntAsync(ImageValidationConfig.RiskScoreHigh, 70);
                result.RiskScore = CalculateRiskScore(duplicates);
                result.ValidationStatus = result.RiskScore >= riskScoreHigh ? "Suspicious" : "Approved";

                // AGREGAR ADVERTENCIAS SI HAY DUPLICADOS
                if (duplicates.Any())
                {
                    var maxSimilarity = duplicates.Max(d => d.SimilarityScore);
                    result.Warnings.Add($"Se encontraron {duplicates.Count} imágenes similares (Máxima similitud: {maxSimilarity}%)");

                    _logger.LogWarning("Duplicados detectados: User={UserId}, Mantenimiento={MantenimientoId}, " +
                        "Duplicados={Count}, MaxSimilitud={Similarity}%",
                        userId, mantenimientoId, duplicates.Count, maxSimilarity);
                }

                _logger.LogInformation("Validación completada: User={UserId}, Score={RiskScore}, Status={ValidationStatus}, Hash={HashPrefix}...",
                    userId, result.RiskScore, result.ValidationStatus, perceptualHash[..10]);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error crítico en validación para usuario {UserId}", userId);
                result.ValidationStatus = "Error";
                result.Warnings.Add("Error inesperado en el proceso de validación");
                return result;
            }
        }

        public async Task<List<DuplicateMatch>> FindDuplicatesAsync(string perceptualHash, string userId, int mantenimientoId)
        {
            var matches = new List<DuplicateMatch>();

            // VALIDACIÓN CRÍTICA DEL HASH
            if (string.IsNullOrEmpty(perceptualHash) || perceptualHash.Length != 64)
            {
                _logger.LogWarning("Hash perceptual inválido recibido para búsqueda de duplicados: User={UserId}", userId);
                return matches;
            }

            try
            {
                // ✅ CORREGIDO: Usar configuración parametrizable en TODOS los valores
                var duplicateThreshold = await _configService.GetConfigValueIntAsync(ImageValidationConfig.DuplicateThreshold, 8);
                var searchMonthsBack = await _configService.GetConfigValueIntAsync(ImageValidationConfig.SearchMonthsBack, 3);
                var maxSearchResults = await _configService.GetConfigValueIntAsync(ImageValidationConfig.MaxSearchResults, 100);

                var recentImages = await _context.ImageFingerprints
                    .Where(f => f.UserId == userId &&
                               f.MantenimientoId != mantenimientoId && // Excluir mantenimiento actual
                               f.CreatedAt >= DateTime.Now.AddMonths(-searchMonthsBack)) // ✅ Usar configuración
                    .OrderByDescending(f => f.CreatedAt) // Más recientes primero
                    .Take(maxSearchResults) // ✅ Usar configuración
                    .ToListAsync();

                _logger.LogDebug("Buscando duplicados en {Count} imágenes recientes del usuario {UserId}",
                    recentImages.Count, userId);

                foreach (var existing in recentImages)
                {
                    var distance = _hashService.CalculateHammingDistance(perceptualHash, existing.PerceptualHash);

                    // ✅ CORREGIDO: Usar umbral configurable en lugar de hardcodeado
                    if (distance <= duplicateThreshold)
                    {
                        var similarityScore = 100 - (int)((distance / 64.0) * 100);

                        matches.Add(new DuplicateMatch
                        {
                            ExistingFotoId = existing.FotoId,
                            SimilarityScore = similarityScore,
                            HammingDistance = distance,
                            ExistingImageDate = existing.CreatedAt,
                            MantenimientoId = existing.MantenimientoId
                        });

                        _logger.LogDebug("Duplicado encontrado: Distance={Distance}, Similarity={Similarity}%, " +
                            "ExistingMantenimiento={ExistingMantenimientoId}",
                            distance, similarityScore, existing.MantenimientoId);
                    }
                }

                _logger.LogInformation("Búsqueda de duplicados completada: User={UserId}, TotalEncontrados={MatchesCount}",
                    userId, matches.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al buscar duplicados para usuario {UserId}", userId);
            }

            return matches.OrderByDescending(m => m.SimilarityScore).ToList();
        }

        private int CalculateRiskScore(List<DuplicateMatch> duplicates)
        {
            if (!duplicates.Any())
                return 0;

            var maxSimilarity = duplicates.Max(d => d.SimilarityScore);

            // ✅ MEJORADO: Escala de riesgo más precisa
            return maxSimilarity switch
            {
                >= 95 => 100, // Casi idénticas
                >= 90 => 85,  // Muy similares
                >= 80 => 70,  // Similares
                >= 70 => 50,  // Algo similares
                _ => 25       // Poco similares
            };
        }

        public async Task LogValidationAsync(ImageValidationResult result, int fotoId, int mantenimientoId, string userId)
        {
            try
            {
                var log = new ImageValidationLog
                {
                    FotoId = fotoId,
                    MantenimientoId = mantenimientoId,
                    UserId = userId,
                    ValidationResult = System.Text.Json.JsonSerializer.Serialize(result),
                    RiskScore = result.RiskScore,
                    ValidationStatus = result.ValidationStatus,
                    ValidatedAt = DateTime.Now
                };

                _context.ImageValidationLogs.Add(log);

                // CREAR ALERTA SI ES SOSPECHOSA
                if (result.ValidationStatus == "Suspicious")
                {
                    // ✅ MEJORADO: Mensaje más descriptivo
                    var alertReason = result.Duplicates.Any()
                        ? $"Imagen duplicada detectada ({result.Duplicates.Count} coincidencias, máxima similitud: {result.Duplicates.Max(d => d.SimilarityScore)}%, Score: {result.RiskScore})"
                        : $"Validación sospechosa: {string.Join("; ", result.Warnings)}";

                    // ✅ CORREGIDO: Usar configuración para niveles de riesgo
                    var riskScoreHigh = await _configService.GetConfigValueIntAsync(ImageValidationConfig.RiskScoreHigh, 70);
                    var riskLevel = result.RiskScore >= riskScoreHigh ? "High" : "Medium";

                    var alert = new SuspiciousImageAlert
                    {
                        FotoId = fotoId,
                        MantenimientoId = mantenimientoId,
                        UserId = userId,
                        Reason = alertReason,
                        RiskLevel = riskLevel,
                        Status = "Pending",
                        CreatedAt = DateTime.Now
                    };

                    _context.SuspiciousImageAlerts.Add(alert);

                    _logger.LogWarning("Alerta creada: FotoId={FotoId}, User={UserId}, RiskLevel={RiskLevel}, Reason={Reason}",
                        fotoId, userId, alert.RiskLevel, alertReason);
                }

                await _context.SaveChangesAsync();

                _logger.LogInformation("Log de validación guardado: FotoId={FotoId}, Status={Status}, RiskScore={RiskScore}",
                    fotoId, result.ValidationStatus, result.RiskScore);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al guardar log de validación para FotoId {FotoId}", fotoId);
            }
        }
    }
}