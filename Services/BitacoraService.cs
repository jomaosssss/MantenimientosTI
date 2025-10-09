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

        // MÉTODO 1: Para operaciones transaccionales (Crear/Actualizar Usuario, Switch).
        // Solo PREPARA el registro. NO guarda. NO es async.
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

        // MÉTODO 2: Para acciones simples e independientes (como Login).
        // PREPARA Y GUARDA el registro en un solo paso. SÍ es async.
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