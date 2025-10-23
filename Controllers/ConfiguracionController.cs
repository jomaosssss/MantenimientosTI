using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MantenimientosTI.Models;
using Microsoft.AspNetCore.Identity;
using MantenimientosTI.Services;
using MantenimientosTI.Helpers;

namespace ProyectoMantenimientos.Controllers
{
    [Authorize(Roles = "ADMINISTRADOR")]
    public class ConfiguracionController : Controller
    {
        private readonly MantenimientosTIContext _dbocontext;
        private readonly BitacoraService _bitacora;

        public ConfiguracionController(MantenimientosTIContext context, BitacoraService bitacora)
        {
            _dbocontext = context;
            _bitacora = bitacora;
        }

        public async Task<IActionResult> Configuracion()
        {
            // Carga los datos para los modales de usuarios
            ViewBag.Roles = await _dbocontext.CatRols.ToListAsync();
            ViewBag.Zonas = await _dbocontext.CatZonas.ToListAsync();

            // Consulta los registros de las últimas 24 horas en lugar de los últimos 50
            var fechaLimite = DateTime.Now.AddHours(-24);
            var ultimosMovimientos = await _dbocontext.RegistroActividad
                .Where(r => r.FechaHora >= fechaLimite) // Filtro de 24 horas
                .OrderByDescending(r => r.FechaHora)
                .Take(50) // Límite de 50 registros máximo
                .ToListAsync();
            ViewBag.Bitacora = ultimosMovimientos;

            // Carga la lista de usuarios para la tabla principal
            var usuarios = await _dbocontext.Usuarios
                .Include(u => u.ClaveRolNavigation)
                .Include(u => u.CatZona)
                .ToListAsync();

            return View(usuarios);
        }

        [HttpGet]
        public IActionResult ObtenerUsuarioPorRpe(string rpe)
        {
            var usuario = _dbocontext.Usuarios
                .FirstOrDefault(u => u.Rpe == rpe);

            if (usuario == null)
            {
                return NotFound();
            }

            return Json(new
            {
                rpe = usuario.Rpe,
                claveRol = usuario.ClaveRol,
                claveZona = usuario.ClaveZona,
                nombre = usuario.Nombre,
                apellidoP = usuario.ApellidoP,
                apellidoM = usuario.ApellidoM,
                correo = usuario.Correo,
                estatus = usuario.Estatus
            });
        }

        [HttpPost]
        public IActionResult ActualizarUsuario([FromBody] UsuarioEditModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var usuario = _dbocontext.Usuarios.FirstOrDefault(u => u.Rpe == model.Rpe);
            if (usuario == null)
            {
                return NotFound(new { success = false, message = "Usuario no encontrado" });
            }

            usuario.ClaveRol = model.ClaveRol;
            usuario.ClaveZona = model.ClaveZona;
            usuario.Nombre = model.Nombre;
            usuario.ApellidoP = model.ApellidoP;
            usuario.ApellidoM = model.ApellidoM;
            usuario.Correo = model.Correo;
            usuario.Estatus = model.Estatus;

            _dbocontext.SaveChanges();
            return Json(new { success = true });
        }

        [HttpPost]
        public IActionResult CrearUsuario([FromBody] UsuarioCreateModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (_dbocontext.Usuarios.Any(u => u.Rpe == model.Rpe))
            {
                return Json(new { success = false, message = "El RPE ya está registrado" });
            }

            var hasher = new PasswordHasher<Usuario>();
            var nuevoUsuario = new Usuario
            {
                Rpe = model.Rpe,
                ClaveRol = model.ClaveRol,
                ClaveDivision = "DK",
                ClaveZona = model.ClaveZona,
                Nombre = model.Nombre,
                ApellidoP = model.ApellidoP,
                ApellidoM = model.ApellidoM,
                Correo = model.Correo,
                Contrasenia = hasher.HashPassword(null, model.Contrasenia),
                Estatus = "Activo"
            };

            _dbocontext.Usuarios.Add(nuevoUsuario);
            _dbocontext.SaveChanges();

            return Json(new { success = true });
        }

        [HttpGet]
        public IActionResult ObtenerEstadoAgendarPreventivos()
        {
            var config = _dbocontext.Configuraciones.AsNoTracking()
                .FirstOrDefault(c => c.ClaveConfiguracion == "AGENDAR_PREVENTIVOS");

            bool estaHabilitado = (config != null && config.Valor == "1");

            return Json(new { habilitado = estaHabilitado });
        }

        [HttpPost]
        public async Task<IActionResult> CambiarEstadoAgendarPreventivos([FromBody] bool habilitar)
        {
            try
            {
                var config = await _dbocontext.Configuraciones
                    .FirstOrDefaultAsync(c => c.ClaveConfiguracion == "AGENDAR_PREVENTIVOS");

                if (config == null)
                {
                    config = new Configuracion { ClaveConfiguracion = "AGENDAR_PREVENTIVOS" };
                    _dbocontext.Configuraciones.Add(config);
                }

                config.Valor = habilitar ? "1" : "0";

                // Prepara el registro de bitácora
                var usuario = HttpContext.Session.GetString("NombreUsuario") ?? "Sistema";
                var accion = habilitar ? BitacoraAcciones.HabilitarCargaCsv : BitacoraAcciones.DeshabilitarCargaCsv;
                var descripcion = habilitar
                    ? "Habilitó la función de carga masiva de mantenimientos."
                    : "Deshabilitó la función de carga masiva de mantenimientos.";

                _bitacora.RegistrarActividad(usuario, accion, descripcion);

                await _dbocontext.SaveChangesAsync();

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al actualizar la configuración: " + ex.Message });
            }
        }

        [HttpPost]
        public IActionResult ActualizarRecibirReporte([FromBody] ActualizarRecibirReporteModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var usuario = _dbocontext.Usuarios.FirstOrDefault(u => u.Rpe == model.Rpe);
            if (usuario == null)
            {
                return NotFound(new { success = false, message = "Usuario no encontrado" });
            }

            if (usuario.ClaveRol != 1)
            {
                return Json(new { success = false, message = "Solo los usuarios administradores pueden recibir reportes" });
            }

            usuario.RecibirReporte = model.RecibirReporte;
            _dbocontext.SaveChanges();

            return Json(new { success = true });
        }

        // Modelos internos para las acciones

        public class ActualizarRecibirReporteModel
        {
            public string Rpe { get; set; }
            public string RecibirReporte { get; set; }
        }

        public class UsuarioEditModel
        {
            public string Rpe { get; set; }
            public int ClaveRol { get; set; }
            public string ClaveZona { get; set; }
            public string Nombre { get; set; }
            public string ApellidoP { get; set; }
            public string ApellidoM { get; set; }
            public string Correo { get; set; }
            public string Estatus { get; set; }
        }

        public class UsuarioCreateModel
        {
            public string Rpe { get; set; }
            public int ClaveRol { get; set; }
            public string ClaveZona { get; set; }
            public string Nombre { get; set; }
            public string ApellidoP { get; set; }
            public string ApellidoM { get; set; }
            public string Correo { get; set; }
            public string Contrasenia { get; set; }
            public string Estatus { get; set; }
        }
    }
}