using MantenimientosTI.Services.ImageValidation.Interfaces;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.PixelFormats;
using System.Text;

namespace MantenimientosTI.Services.ImageValidation
{
    public class PerceptualHashService : IPerceptualHashService
    {
        private readonly ILogger<PerceptualHashService> _logger;

        public PerceptualHashService(ILogger<PerceptualHashService> logger)
        {
            _logger = logger;
        }

        public async Task<string> GeneratePerceptualHashAsync(Stream imageStream)
        {
            try
            {
                // Cargar la imagen y convertir a formato que permita acceso a píxeles
                using var image = await Image.LoadAsync<Rgb24>(imageStream);

                // 1. Convertir a escala de grises
                image.Mutate(x => x.Grayscale());

                // 2. Reducir a 8x8 píxeles
                image.Mutate(x => x.Resize(new Size(8, 8)));

                // 3. Calcular promedio de color
                double totalBrightness = 0;
                var brightnessValues = new double[64]; // 8x8 = 64 píxeles
                int index = 0;

                // Acceso seguro a píxeles
                for (int y = 0; y < image.Height; y++)
                {
                    for (int x = 0; x < image.Width; x++)
                    {
                        var pixel = image[x, y]; // ✅ Ahora funciona con Rgb24
                        var brightness = (pixel.R + pixel.G + pixel.B) / 3.0;
                        brightnessValues[index] = brightness;
                        totalBrightness += brightness;
                        index++;
                    }
                }

                var averageBrightness = totalBrightness / 64;

                // 4. Generar hash binario
                var hashBuilder = new StringBuilder(64);

                foreach (var brightness in brightnessValues)
                {
                    hashBuilder.Append(brightness > averageBrightness ? "1" : "0");
                }

                return hashBuilder.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al generar hash perceptual");
                return string.Empty;
            }
        }

        public int CalculateHammingDistance(string hash1, string hash2)
        {
            if (string.IsNullOrEmpty(hash1) || string.IsNullOrEmpty(hash2) || hash1.Length != hash2.Length)
                return int.MaxValue;

            int distance = 0;
            for (int i = 0; i < hash1.Length; i++)
            {
                if (hash1[i] != hash2[i]) distance++;
            }
            return distance;
        }
    }
}