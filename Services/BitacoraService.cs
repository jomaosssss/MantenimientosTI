// Services/BitacoraService.cs
using MantenimientosTI.Models;
using MantenimientosTI.Helpers; // ✅ USING AGREGADO
using System.Threading.Tasks;
using System.Collections.Generic;

namespace MantenimientosTI.Services
{
    public class BitacoraService
    {
        private readonly MantenimientosTIContext _context;
        private readonly PlantillaBitacoraService _plantillaService;

        public BitacoraService(MantenimientosTIContext context, PlantillaBitacoraService plantillaService)
        {
            _context = context;
            _plantillaService = plantillaService;
        }

        public async Task RegistrarActividadAsync(string usuario, string rpe, string rol, string zona, string centro,
                                                string claveAccion, Dictionary<string, string> parametrosAdicionales = null)
        {
            var parametros = new Dictionary<string, string>
            {
                { "Usuario", usuario },
                { "RPE", rpe },
                { "Rol", rol },
                { "Zona", zona },
                { "Centro", centro }
            };

            if (parametrosAdicionales != null)
            {
                foreach (var param in parametrosAdicionales)
                {
                    parametros[param.Key] = param.Value;
                }
            }

            var descripcion = _plantillaService.ObtenerDescripcionFormateada(claveAccion, parametros);

            var registro = new RegistroActividad
            {
                FechaHora = DateTime.Now,
                Usuario = usuario,
                Accion = claveAccion,
                Descripcion = descripcion
            };

            _context.RegistroActividad.Add(registro);
            await _context.SaveChangesAsync();
        }

        // ✅ MÉTODOS CORREGIDOS - TODOS USAN CONSTANTES

        public async Task RegistrarSolicitudCancelacionAsync(string usuario, string rpe, string rol, string zona, string centro,
                                                   string idAgenda, string equipo, string motivo, string justificacion)
        {
            await RegistrarActividadAsync(usuario, rpe, rol, zona, centro, "SOLICITUD_CANCELACION_MTTO",
                new Dictionary<string, string>
                {
                    { "ClaveAgenda", idAgenda },
                    { "Equipo", equipo },
                    { "Motivo", motivo },
                    { "Justificacion", justificacion }
                });
        }

        public async Task RegistrarCancelacionDirectaAsync(string usuario, string rpe, string rol, string zona, string centro,
                                                 string idAgenda, string equipo, string motivo, string justificacion)
        {
            await RegistrarActividadAsync(usuario, rpe, rol, zona, centro, "CANCELACION_DIRECTA_MTTO",
                new Dictionary<string, string>
                {
                    { "ClaveAgenda", idAgenda },
                    { "Equipo", equipo },
                    { "Motivo", motivo },
                    { "Justificacion", justificacion }
                });
        }

        public async Task RegistrarPreCancelacionAsync(string usuario, string rpe, string rol, string zona, string centro,
                                             string claveAgenda, string equipo)
        {
            await RegistrarActividadAsync(usuario, rpe, rol, zona, centro, BitacoraAcciones.PreCancelacionMtto,
                new Dictionary<string, string>
                {
                    { "ClaveAgenda", claveAgenda },
                    { "Equipo", equipo }
                });
        }

        public async Task RegistrarConfirmacionCancelacionAsync(string usuario, string rpe, string rol, string zona, string centro,
                                                      string claveAgenda, string equipo)
        {
            await RegistrarActividadAsync(usuario, rpe, rol, zona, centro, BitacoraAcciones.ConfirmacionCancelacion,
                new Dictionary<string, string>
                {
                    { "ClaveAgenda", claveAgenda },
                    { "Equipo", equipo }
                });
        }

        public async Task RegistrarCreacionUsuarioAsync(string usuarioAdmin, string rpeAdmin, string rolAdmin, string zonaAdmin,
                                                      string usuarioCreado, string rpeCreado, string rolCreado, string zonaCreada)
        {
            await RegistrarActividadAsync(usuarioAdmin, rpeAdmin, rolAdmin, zonaAdmin, "N/A", BitacoraAcciones.CrearUsuario,
                new Dictionary<string, string>
                {
                    { "UsuarioAfectado", usuarioCreado },
                    { "RPEAfectado", rpeCreado },
                    { "RolAfectado", rolCreado },
                    { "ZonaAfectada", zonaCreada }
                });
        }

        public async Task RegistrarActualizacionUsuarioAsync(string usuarioAdmin, string rpeAdmin, string rolAdmin, string zonaAdmin,
                                                           string usuarioActualizado, string rpeActualizado, string cambios)
        {
            await RegistrarActividadAsync(usuarioAdmin, rpeAdmin, rolAdmin, zonaAdmin, "N/A", BitacoraAcciones.ActualizarUsuario,
                new Dictionary<string, string>
                {
                    { "UsuarioAfectado", usuarioActualizado },
                    { "RPEAfectado", rpeActualizado },
                    { "CambiosRealizados", cambios }
                });
        }

        public async Task RegistrarCargaCSVAsync(string usuario, string rpe, string rol, string zona, string centro,
                                               string tipoCarga, int registrosProcesados)
        {
            await RegistrarActividadAsync(usuario, rpe, rol, zona, centro, BitacoraAcciones.CargaCsv,
                new Dictionary<string, string>
                {
                    { "RegistrosProcesados", registrosProcesados.ToString() },
                    { "TipoCarga", tipoCarga }
                });
        }

        public async Task RegistrarMantenimientoCorrectivoAsync(string usuario, string rpe, string rol, string zona, string centro)
        {
            await RegistrarActividadAsync(usuario, rpe, rol, zona, centro, BitacoraAcciones.CargaMttoCorrectivo);
        }

        public async Task RegistrarTerminacionMantenimientoAsync(string usuario, string rpe, string rol, string zona, string centro,
                                                               string claveAgenda, string equipo)
        {
            await RegistrarActividadAsync(usuario, rpe, rol, zona, centro, BitacoraAcciones.TerminacionMtto,
                new Dictionary<string, string>
                {
                    { "ClaveAgenda", claveAgenda },
                    { "Equipo", equipo }
                });
        }

        public async Task RegistrarCancelacionMantenimientoAsync(string usuario, string rpe, string rol, string zona, string centro,
                                                               string claveAgenda, string equipo, string motivo, string justificacion)
        {
            await RegistrarActividadAsync(usuario, rpe, rol, zona, centro, "CANCELACION_MTTO",
                new Dictionary<string, string>
                {
                    { "ClaveAgenda", claveAgenda },
                    { "Equipo", equipo },
                    { "Motivo", motivo },
                    { "Justificacion", justificacion }
                });
        }

        // ✅ MÉTODO CRÍTICO CORREGIDO - INICIO DE SESIÓN
        public async Task RegistrarInicioSesionAsync(string usuario, string rpe, string rol, string zona, string ubicacion = "sistema")
        {
            // ✅ COMO EL LOGOUT - USA UNA SOLA CONSTANTE FIJA
            await RegistrarActividadAsync(usuario, rpe, rol, zona, "N/A", BitacoraAcciones.InicioSesion,
                new Dictionary<string, string>
                {
            { "Ubicacion", ubicacion }
                });
        }

        // ✅ FUNCIÓN AUXILIAR PARA DETECTAR ADMINISTRADORES
        private bool EsUsuarioAdministrador(string rpe)
        {
            var administradores = new[] { "ADMIN", "OISM0", "FER01", "MEM03", };
            return administradores.Contains(rpe?.ToUpper() ?? "");
        }

        // ✅ MÉTODO CORREGIDO - CIERRE DE SESIÓN
        public async Task RegistrarCierreSesionAsync(string usuario, string rpe, string rol, string zona)
        {
            await RegistrarActividadAsync(usuario, rpe, rol, zona, "N/A", BitacoraAcciones.LogoutSistema);
        }

        // ✅ MÉTODOS ADICIONALES CORREGIDOS
        public async Task RegistrarHabilitarCargaCSVAsync(string usuario, string rpe, string rol, string zona)
        {
            await RegistrarActividadAsync(usuario, rpe, rol, zona, "N/A", BitacoraAcciones.HabilitarCargaCsv);
        }

        public async Task RegistrarDeshabilitarCargaCSVAsync(string usuario, string rpe, string rol, string zona)
        {
            await RegistrarActividadAsync(usuario, rpe, rol, zona, "N/A", BitacoraAcciones.DeshabilitarCargaCsv);
        }
    }
}