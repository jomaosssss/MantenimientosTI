using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
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
                // Usar ImageSharp para procesamiento de imágenes
                using var image = await Image.LoadAsync(imageStream);

                // 1. Convertir a escala de grises
                image.Mutate(x => x.Grayscale());

                // 2. Reducir a 8x8 píxeles (ignorar detalles)
                image.Mutate(x => x.Resize(new Size(8, 8)));

                // 3. Calcular promedio de color
                double totalBrightness = 0;
                var brightnessMatrix = new double[8, 8];

                for (int y = 0; y < 8; y++)
                {
                    for (int x = 0; x < 8; x++)
                    {
                        var pixel = image[x, y];
                        var brightness = (pixel.R + pixel.G + pixel.B) / 3.0;
                        brightnessMatrix[x, y] = brightness;
                        totalBrightness += brightness;
                    }
                }

                var averageBrightness = totalBrightness / 64;

                // 4. Generar hash binario
                var hashBuilder = new StringBuilder(64);
                for (int y = 0; y < 8; y++)
                {
                    for (int x = 0; x < 8; x++)
                    {
                        hashBuilder.Append(brightnessMatrix[x, y] > averageBrightness ? "1" : "0");
                    }
                }

                return hashBuilder.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al generar hash perceptual de la imagen");
                throw new Exception("Error al generar hash perceptual de la imagen", ex);
            }
        }

        public int CalculateHammingDistance(string hash1, string hash2)
        {
            if (hash1.Length != hash2.Length)
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