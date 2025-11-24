using MantenimientosTI.Models;
using MantenimientosTI.Models.ImageValidation;
using System.Text.Json;

namespace MantenimientosTI.Services.ImageValidation
{
        public interface IImageValidator
        {
            Task<ImageValidationResult> ValidateImageAsync(IFormFile image, string userId, int mantenimientoId, string tipoFoto);
            Task<List<DuplicateMatch>> FindDuplicatesAsync(string perceptualHash, string userId, int mantenimientoId);
            Task LogValidationAsync(ImageValidationResult result, int fotoId, int mantenimientoId, string userId);
        }
}

