using MantenimientosTI.Models.ImageValidation;
using MantenimientosTI.Services.ImageValidation.Interfaces;
using Microsoft.EntityFrameworkCore;
using MantenimientosTI.Models; // ← AGREGAR ESTE USING

namespace MantenimientosTI.Services.ImageValidation
{
    public class BasicImageValidator : IImageValidator
    {
        private readonly MantenimientosTIContext _context;
        private readonly IPerceptualHashService _hashService;
        private readonly ILogger<BasicImageValidator> _logger;

        public BasicImageValidator(
            MantenimientosTIContext context,
            IPerceptualHashService hashService,
            ILogger<BasicImageValidator> logger)
        {
            _context = context;
            _hashService = hashService;
            _logger = logger;
        }

        public async Task<ImageValidationResult> ValidateImageAsync(IFormFile image, string userId, int mantenimientoId, string tipoFoto)
        {
            var result = new ImageValidationResult();

            try
            {
                using var stream = image.OpenReadStream();
                var perceptualHash = await _hashService.GeneratePerceptualHashAsync(stream);

                if (string.IsNullOrEmpty(perceptualHash))
                {
                    result.ValidationStatus = "Error";
                    result.Warnings.Add("No se pudo procesar la imagen");
                    return result;
                }

                result.PerceptualHash = perceptualHash;

                var duplicates = await FindDuplicatesAsync(perceptualHash, userId, mantenimientoId);
                result.Duplicates = duplicates;

                result.RiskScore = CalculateRiskScore(duplicates);
                result.ValidationStatus = result.RiskScore >= 70 ? "Suspicious" : "Approved";

                if (duplicates.Any())
                {
                    result.Warnings.Add($"Se encontraron {duplicates.Count} imágenes similares");
                }

                _logger.LogInformation($"Validación completada. Score: {result.RiskScore}, Status: {result.ValidationStatus}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error en validación para usuario {userId}");
                result.ValidationStatus = "Error";
                result.Warnings.Add("Error en el proceso de validación");
            }

            return result;
        }

        public async Task<List<DuplicateMatch>> FindDuplicatesAsync(string perceptualHash, string userId, int mantenimientoId)
        {
            var matches = new List<DuplicateMatch>();

            try
            {
                var recentImages = await _context.ImageFingerprints
                    .Where(f => f.UserId == userId && f.CreatedAt >= DateTime.Now.AddDays(-30))
                    .ToListAsync();

                foreach (var existing in recentImages)
                {
                    var distance = _hashService.CalculateHammingDistance(perceptualHash, existing.PerceptualHash);

                    if (distance <= 10)
                    {
                        matches.Add(new DuplicateMatch
                        {
                            ExistingFotoId = existing.FotoId,
                            SimilarityScore = 100 - (int)((distance / 64.0) * 100),
                            HammingDistance = distance,
                            ExistingImageDate = existing.CreatedAt,
                            MantenimientoId = existing.MantenimientoId
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al buscar duplicados");
            }

            return matches.OrderByDescending(m => m.SimilarityScore).ToList();
        }

        private int CalculateRiskScore(List<DuplicateMatch> duplicates)
        {
            if (!duplicates.Any()) return 0;

            var maxSimilarity = duplicates.Max(d => d.SimilarityScore);

            return maxSimilarity switch
            {
                >= 90 => 100,
                >= 80 => 75,
                >= 70 => 50,
                _ => 25
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

                if (result.ValidationStatus == "Suspicious")
                {
                    var alert = new SuspiciousImageAlert
                    {
                        FotoId = fotoId,
                        MantenimientoId = mantenimientoId,
                        UserId = userId,
                        Reason = string.Join("; ", result.Warnings),
                        RiskLevel = result.RiskScore >= 80 ? "High" : "Medium",
                        Status = "Pending",
                        CreatedAt = DateTime.Now
                    };

                    _context.SuspiciousImageAlerts.Add(alert);
                }

                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al guardar log de validación");
            }
        }
    }
}