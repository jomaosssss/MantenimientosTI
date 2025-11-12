// Services/PlantillaBitacoraService.cs
using MantenimientosTI.Models;
using System.Collections.Generic;
using System.Linq;

namespace MantenimientosTI.Services
{
    public class PlantillaBitacoraService
    {
        private readonly MantenimientosTIContext _context;

        public PlantillaBitacoraService(MantenimientosTIContext context)
        {
            _context = context;
        }

        public string ObtenerDescripcionFormateada(string claveAccion, Dictionary<string, string> parametros)
        {
            var plantilla = _context.CatAcciones
                .Where(a => a.ClaveAccion == claveAccion)
                .Select(a => a.Descripcion)
                .FirstOrDefault();

            if (string.IsNullOrEmpty(plantilla))
                return $"Acción no definida: {claveAccion}";

            // Reemplazar placeholders con valores reales
            foreach (var param in parametros)
            {
                plantilla = plantilla.Replace($"{{{param.Key}}}", param.Value ?? "N/A");
            }

            return plantilla;
        }

        // Métodos específicos para cada tipo de acción
        public string FormatearCreacionUsuario(string usuario, string rpe, string usuarioAfectado, string rpeAfectado, string rolAfectado, string zonaAfectada)
        {
            var parametros = new Dictionary<string, string>
            {
                { "Usuario", usuario },
                { "RPE", rpe },
                { "UsuarioAfectado", usuarioAfectado },
                { "RPEAfectado", rpeAfectado },
                { "RolAfectado", rolAfectado },
                { "ZonaAfectada", zonaAfectada }
            };
            return ObtenerDescripcionFormateada("CREAR_USUARIO", parametros);
        }

        public string FormatearActualizacionUsuario(string usuario, string rpe, string usuarioAfectado, string rpeAfectado, string cambios)
        {
            var parametros = new Dictionary<string, string>
            {
                { "Usuario", usuario },
                { "RPE", rpe },
                { "UsuarioAfectado", usuarioAfectado },
                { "RPEAfectado", rpeAfectado },
                { "CambiosRealizados", cambios }
            };
            return ObtenerDescripcionFormateada("ACTUALIZAR_USUARIO", parametros);
        }

        public string FormatearCargaCSV(string usuario, string rpe, int registrosProcesados, string tipoCarga, string zona)
        {
            var parametros = new Dictionary<string, string>
            {
                { "Usuario", usuario },
                { "RPE", rpe },
                { "RegistrosProcesados", registrosProcesados.ToString() },
                { "TipoCarga", tipoCarga },
                { "Zona", zona }
            };
            return ObtenerDescripcionFormateada("CARGA_CSV_PREVENTIVOS", parametros);
        }

        public string FormatearMantenimientoCorrectivo(string usuario, string rpe, string zona, string centro)
        {
            var parametros = new Dictionary<string, string>
            {
                { "Usuario", usuario },
                { "RPE", rpe },
                { "Zona", zona },
                { "Centro", centro }
            };
            return ObtenerDescripcionFormateada("CARGA_MTTO_CORRECTIVO", parametros);
        }

        public string FormatearTerminacionMantenimiento(string usuario, string rpe, string claveAgenda, string equipo, string zona)
        {
            var parametros = new Dictionary<string, string>
            {
                { "Usuario", usuario },
                { "RPE", rpe },
                { "ClaveAgenda", claveAgenda },
                { "Equipo", equipo },
                { "Zona", zona }
            };
            return ObtenerDescripcionFormateada("TERMINACION_MTTO", parametros);
        }

        public string FormatearCancelacionMantenimiento(string usuario, string rpe, string claveAgenda, string equipo, string motivo, string justificacion)
        {
            var parametros = new Dictionary<string, string>
            {
                { "Usuario", usuario },
                { "RPE", rpe },
                { "ClaveAgenda", claveAgenda },
                { "Equipo", equipo },
                { "Motivo", motivo },
                { "Justificacion", justificacion }
            };
            return ObtenerDescripcionFormateada("CANCELACION_MTTO", parametros);
        }
    }
}