// En /Services/BitacoraService.cs
using MantenimientosTI.Models;
using System.Threading.Tasks;

namespace MantenimientosTI.Services
{
    public class BitacoraService
    {
        private readonly MantenimientosTIContext _context;

        public BitacoraService(MantenimientosTIContext context)
        {
            _context = context;
        }

        public void RegistrarActividad(string usuario, string accion, string descripcion, int? idEntidad = null)
        {
            var registro = new RegistroActividad
            {
                FechaHora = DateTime.Now,
                Usuario = usuario,
                Accion = accion,
                Descripcion = descripcion,
                IdEntidadAfectada = idEntidad
            };
            _context.Add(registro);
        }

        public async Task RegistrarYGuardarAsync(string usuario, string accion, string descripcion, int? idEntidad = null)
        {
            var registro = new RegistroActividad
            {
                FechaHora = DateTime.Now,
                Usuario = usuario,
                Accion = accion,
                Descripcion = descripcion,
                IdEntidadAfectada = idEntidad
            };
            _context.Add(registro);
            await _context.SaveChangesAsync();
        }
    }
}