using MantenimientosTI.Models;
using MantenimientosTI.Models.ImageValidation;
using Microsoft.EntityFrameworkCore;

namespace MantenimientosTI.Services.ImageValidation
{
    public class BasicImageValidator : IImageValidator
    {
        private readonly MantenimientosTIContext _context;
        private readonly IPerceptualHashService _hashService;
        private readonly ILogger<BasicImageValidator> _logger;

        public BasicImageValidator(MantenimientosTIContext context, IPerceptualHashService hashService, ILogger<BasicImageValidator> logger)
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
                // 1. Generar hash perceptual
                using var stream = image.OpenReadStream();
                var perceptualHash = await _hashService.GeneratePerceptualHashAsync(stream);
                result.PerceptualHash = perceptualHash;

                // 2. Buscar duplicados
                var duplicates = await FindDuplicatesAsync(perceptualHash, userId, mantenimientoId);
                result.Duplicates = duplicates;

                // 3. Calcular riesgo basado en duplicados
                result.RiskScore = CalculateRiskScore(duplicates);
                result.ValidationStatus = result.RiskScore >= 70 ? "Suspicious" : "Approved";

                // 4. Agregar advertencias si hay duplicados
                if (duplicates.Any())
                {
                    result.Warnings.Add($"Se encontraron {duplicates.Count} imágenes similares en el sistema");
                }

                _logger.LogInformation($"Validación completada para usuario {userId}. Score: {result.RiskScore}, Status: {result.ValidationStatus}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error en validación de imagen para usuario {userId}");
                result.ValidationStatus = "Error";
                result.Warnings.Add("Error en el proceso de validación");
            }

            return result;
        }

        public async Task<List<DuplicateMatch>> FindDuplicatesAsync(string perceptualHash, string userId, int mantenimientoId)
        {
            var matches = new List<DuplicateMatch>();

            // Buscar imágenes del mismo usuario en los últimos 30 días
            var recentImages = await _context.ImageFingerprints
                .Where(f => f.UserId == userId && f.CreatedAt >= DateTime.Now.AddDays(-30))
                .ToListAsync();

            foreach (var existing in recentImages)
            {
                var distance = _hashService.CalculateHammingDistance(perceptualHash, existing.PerceptualHash);

                // Si la distancia es menor al threshold, es un posible duplicado
                if (distance <= 10) // 10/64 = ~15% de diferencia
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

            return matches.OrderByDescending(m => m.SimilarityScore).ToList();
        }

        private int CalculateRiskScore(List<DuplicateMatch> duplicates)
        {
            if (!duplicates.Any()) return 0;

            var maxSimilarity = duplicates.Max(d => d.SimilarityScore);

            return maxSimilarity switch
            {
                >= 90 => 100, // Muy similar
                >= 80 => 75,  // Similar
                >= 70 => 50,  // Algo similar
                _ => 25       // Poco similar
            };
        }

        public async Task LogValidationAsync(ImageValidationResult result, int fotoId, int mantenimientoId, string userId)
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

            // Crear alerta si es sospechosa
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
    }
}
