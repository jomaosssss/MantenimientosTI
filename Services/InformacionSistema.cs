using MantenimientosTI.Models;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace MantenimientosTI.Services
{
    public class InformacionSistema
    {
        private readonly MantenimientosTIContext _context;

        public InformacionSistema(MantenimientosTIContext context)
        {
            _context = context;
        }

        public async Task<Dictionary<string, string>> ObtenerInfoSistema()
        {
            // CORRECCIÓN: Usar Configuraciones en lugar de Configuracion
            var config = await _context.Configuraciones
                .FirstOrDefaultAsync(c => c.ClaveConfiguracion == "INFO_SISTEMA");

            if (config == null || string.IsNullOrEmpty(config.Valor))
                return new Dictionary<string, string>
                {
                    { "Version", "V1.0" },
                    { "Fecha", DateTime.Now.ToString("dd-MM-yyyy") },
                    { "Ambiente", "PRODUCTIVO" }
                };

            try
            {
                var json = JsonSerializer.Deserialize<Dictionary<string, string>>(config.Valor);
                return json;
            }
            catch
            {
                return new Dictionary<string, string>
                {
                    { "Version", "V1.0" },
                    { "Fecha", DateTime.Now.ToString("dd-MM-yyyy") },
                    { "Ambiente", "PRODUCTIVO" }
                };
            }
        }
    }
}