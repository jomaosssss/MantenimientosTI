namespace MantenimientosTI.Services.ImageValidation.Interfaces
{
    public interface IPerceptualHashService
    {
        Task<string> GeneratePerceptualHashAsync(Stream imageStream);
        int CalculateHammingDistance(string hash1, string hash2);
    }
}