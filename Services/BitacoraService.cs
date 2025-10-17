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

        public async Task RegistrarLogoutAsync(string usuario, string rpe, string rol, string zona)
        {
            var descripcion = $"Cierre de sesión | RPE: {rpe} | Rol: {rol} | Zona: {zona}";
            await RegistrarYGuardarAsync(usuario, "LOGOUT_SISTEMA", descripcion);
        }

        public void RegistrarCargaCSV(string usuario, string rpe, string rol, string zona, string centro, string tipoCarga, int registrosProcesados)
        {
            var descripcion = $"Carga de archivo CSV ({tipoCarga}) | Registros: {registrosProcesados} | RPE: {rpe} | Rol: {rol} | Zona: {zona} | Centro: {centro}";
            RegistrarActividad(usuario, "CARGA_CSV", descripcion);
        }

        public void RegistrarCargaMantenimientoCorrectivo(string usuario, string rpe, string rol, string zona, string centro, int registrosCargados)
        {
            var descripcion = $"Carga de mantenimientos correctivos | Registros: {registrosCargados} | RPE: {rpe} | Rol: {rol} | Zona: {zona} | Centro: {centro}";
            RegistrarActividad(usuario, "CARGA_MTTO_CORRECTIVO", descripcion);
        }

        public void RegistrarTerminacionMantenimiento(string usuario, string rpe, string rol, string zona, string centro, string claveAgenda, string equipo)
        {
            var descripcion = $"Terminación de mantenimiento | Orden: {claveAgenda} | Equipo: {equipo} | RPE: {rpe} | Rol: {rol} | Zona: {zona} | Centro: {centro}";
            RegistrarActividad(usuario, "TERMINACION_MTTO", descripcion);
        }

        public void RegistrarPreCancelacion(string usuario, string rpe, string rol, string zona, string centro, string claveAgenda, string equipo)
        {
            var descripcion = $"Solicitud de pre-cancelación | Orden: {claveAgenda} | Equipo: {equipo} | RPE: {rpe} | Rol: {rol} | Zona: {zona} | Centro: {centro}";
            RegistrarActividad(usuario, "PRECANCELACION_MTTO", descripcion);
        }

        public void RegistrarConfirmacionCancelacion(string usuario, string rpe, string rol, string zona, string centro, string claveAgenda, string equipo)
        {
            var descripcion = $"Confirmación de cancelación | Orden: {claveAgenda} | Equipo: {equipo} | RPE: {rpe} | Rol: {rol} | Zona: {zona} | Centro: {centro}";
            RegistrarActividad(usuario, "CONFIRMACION_CANCELACION", descripcion);
        }
    }
}