using MantenimientosTI.Models.ImageValidation;

namespace MantenimientosTI.Services.ImageValidation.Interfaces
{
    public interface IImageValidator
    {
        Task<ImageValidationResult> ValidateImageAsync(IFormFile image, string userId, int mantenimientoId, string tipoFoto);
        Task<List<DuplicateMatch>> FindDuplicatesAsync(string perceptualHash, string userId, int mantenimientoId);
        Task LogValidationAsync(ImageValidationResult result, int fotoId, int mantenimientoId, string userId);
    }
}