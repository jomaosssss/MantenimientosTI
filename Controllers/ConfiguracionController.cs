using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MantenimientosTI.Models;
using Microsoft.AspNetCore.Identity; // Se ocupa para PasswordHasher

namespace MantenimientosTI.Controllers
{
    [Authorize(Roles = "ADMINISTRADOR")]
    public class ConfiguracionController : Controller
    {
        private readonly MantenimientosTIContext _dbocontext;

        public ConfiguracionController(MantenimientosTIContext context)
        {
            _dbocontext = context;
        }

        public IActionResult Configuracion()
        {
            var usuarios = _dbocontext.Usuarios
                .Include(u => u.ClaveRolNavigation)
                .Include(u => u.CatZona)
                .ToList();

            ViewBag.Roles = _dbocontext.CatRols.ToList();
            ViewBag.Zonas = _dbocontext.CatZonas.ToList();

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
        public IActionResult CambiarEstadoAgendarPreventivos([FromBody] bool habilitar)
        {
            var config = _dbocontext.Configuraciones
                .FirstOrDefault(c => c.ClaveConfiguracion == "AGENDAR_PREVENTIVOS");

            if (config == null)
            {
                return Json(new { success = false, message = "Clave de configuración no encontrada." });
            }

            config.Valor = habilitar ? "1" : "0";
            _dbocontext.SaveChanges();
            return Json(new { success = true });
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

            // Validar que solo los administradores pueden recibir reportes
            if (usuario.ClaveRol != 1)
            {
                return Json(new { success = false, message = "Solo los usuarios administradores pueden recibir reportes" });
            }

            usuario.RecibirReporte = model.RecibirReporte;
            _dbocontext.SaveChanges();

            return Json(new { success = true });
        }

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