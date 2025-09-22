using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MantenimientosTI.Models;
using Microsoft.AspNetCore.Identity; // Necesario para PasswordHasher

namespace ProyectoMantenimientos.Controllers
{
    [Authorize(Roles = "ADMINISTRADOR")] // Protegemos todo el controlador para que solo los admins entren
    public class ConfiguracionController : Controller
    {
        private readonly MantenimientosTIContext _dbocontext;

        public ConfiguracionController(MantenimientosTIContext context)
        {
            _dbocontext = context;
        }

        // --- ACCIÓN PRINCIPAL PARA MOSTRAR LA VISTA ---
        public IActionResult Index()
        {
            // Carga todos los datos necesarios para la vista (usuarios, roles, zonas)
            var usuarios = _dbocontext.Usuarios
                .Include(u => u.ClaveRolNavigation)
                .Include(u => u.CatZona)
                .ToList();

            ViewBag.Roles = _dbocontext.CatRols.ToList();
            ViewBag.Zonas = _dbocontext.CatZonas.ToList();

            return View(usuarios); // El nombre de la vista por defecto será Index.cshtml o puedes especificar "Configuracion"
        }

        // --- ENDPOINTS PARA LA GESTIÓN DE USUARIOS (Llamados por AJAX) ---

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
                ClaveDivision = "DK", // Valor por defecto
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

        // --- ENDPOINTS PARA LA CONFIGURACIÓN DEL SISTEMA (Llamados por AJAX) ---

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


        // --- MODELOS INTERNOS PARA LAS ACCIONES ---
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