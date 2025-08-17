using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MantenimientosTI.Models;
using MantenimientosTI.Models.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace ProyectoMantenimientos.Controllers
{
    public class UsuarioController : Controller
    {
        
        private readonly MantenimientosTIContext _dbocontext;

        public UsuarioController(MantenimientosTIContext context)
        {
            _dbocontext = context;
        }

        [HttpGet]
        public IActionResult Login()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Login(VMLogin model)
        {
            ModelState.Clear();

            // Validación manual de campos vacíos
            if (string.IsNullOrWhiteSpace(model.Correo) && string.IsNullOrWhiteSpace(model.Contrasenia))
            {
                ViewBag.Mensaje = "Por favor, ingrese su correo electrónico y contraseña";
                return View(model);
            }
            else if (string.IsNullOrWhiteSpace(model.Correo))
            {
                ViewBag.Mensaje = "El campo Correo electrónico es requerido";
                return View(model);
            }
            else if (string.IsNullOrWhiteSpace(model.Contrasenia))
            {
                ViewBag.Mensaje = "El campo Contraseña es requerido";
                return View(model);
            }

            // Validación de credenciales
            try
            {
                var usuario = _dbocontext.Usuarios
                    .Include(u => u.ClaveRolNavigation)
                    .Include(u => u.CatZona)
                    .FirstOrDefault(u => u.Correo == model.Correo);

                if (usuario == null)
                {
                    ViewBag.Mensaje = "Correo electrónico no registrado";
                    return View(model);
                }

                // Verificacion de contraseña con hash
                var hasher = new PasswordHasher<Usuario>();
                var result = hasher.VerifyHashedPassword(usuario, usuario.Contrasenia, model.Contrasenia);

                // Migracion para desarrollo (solo para pruebas)
#if DEBUG
                if (result == PasswordVerificationResult.Failed && usuario.Contrasenia == model.Contrasenia)
                {
                    // Hashear la contraseña antigua y guardarla
                    usuario.Contrasenia = hasher.HashPassword(usuario, model.Contrasenia);
                    _dbocontext.SaveChanges();
                    result = PasswordVerificationResult.Success;
                }
#endif

                if (result != PasswordVerificationResult.Success)
                {
                    ViewBag.Mensaje = "Contraseña incorrecta";
                    return View(model);
                }

                if (usuario.Estatus.ToLower() != "activo")
                {
                    ViewBag.Mensaje = "Usuario inactivo. Contacte al administrador";
                    return View(model);
                }

                // Crear claims para el usuario
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, usuario.Rpe),
                    new Claim(ClaimTypes.Name, $"{usuario.Nombre} {usuario.ApellidoP} {usuario.ApellidoM}"),
                    new Claim(ClaimTypes.Role, usuario.ClaveRolNavigation?.Nombre ?? "Sin rol"),
                    new Claim("RolId", usuario.ClaveRol.ToString()),
                    new Claim("Zona", usuario.ClaveZona),
                    new Claim("ZonaNombre", usuario.CatZona?.NombreZona ?? "Sin zona"),
                    new Claim("Division", usuario.ClaveDivision)
                };

                // Crear identidad y principal
                var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var claimsPrincipal = new ClaimsPrincipal(claimsIdentity);

                // Iniciar sesión
                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, claimsPrincipal);

                // Configuración de sesión
                HttpContext.Session.SetString("Rpe", usuario.Rpe);
                HttpContext.Session.SetString("NombreUsuario", $"{usuario.Nombre} {usuario.ApellidoP} {usuario.ApellidoM}");
                HttpContext.Session.SetInt32("Rol", usuario.ClaveRol);
                HttpContext.Session.SetString("NombreRol", usuario.ClaveRolNavigation?.Nombre ?? "Sin rol");
                HttpContext.Session.SetString("ClaveZona", usuario.ClaveZona);
                HttpContext.Session.SetString("NombreZona", usuario.CatZona?.NombreZona ?? "Sin zona");
                HttpContext.Session.SetString("ClaveDivision", usuario.ClaveDivision);

                return RedirectToAction("Inicio", "Home");
            }
            catch (Exception ex)
            {
                ViewBag.Mensaje = "Ocurrió un error al iniciar sesión. Intente nuevamente";
                return View(model);
            }
        }

        public async Task<IActionResult> CerrarSesion()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            HttpContext.Session.Clear();
            return RedirectToAction("Login", "Usuario");
        }

        [Authorize(Roles = "ADMINISTRADOR")]
        public IActionResult Administrar()
        {
            // Obtener todos los usuarios con sus relaciones
            var usuarios = _dbocontext.Usuarios
                .Include(u => u.ClaveRolNavigation)
                .Include(u => u.CatZona)
                .ToList();

            // Cargar lista de roles y zonas para los dropdowns
            ViewBag.Roles = _dbocontext.CatRols.ToList();
            ViewBag.Zonas = _dbocontext.CatZonas.ToList();

            return View(usuarios);
        }

        // Obtener un usuario por RPE para edición
        [HttpGet]
        public IActionResult ObtenerUsuarioPorRpe(string rpe)
        {
            var usuario = _dbocontext.Usuarios
                .Include(u => u.ClaveRolNavigation)
                .Include(u => u.CatZona)
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

        [Authorize(Roles = "ADMINISTRADOR")]
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

            try
            {
                // Actualizar propiedades
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
            catch (DbUpdateException ex)
            {
                return Json(new { success = false, message = "Error al actualizar usuario: " + ex.Message });
            }
        }

        [Authorize(Roles = "ADMINISTRADOR")]
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

            try
            {
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
                    Contrasenia = hasher.HashPassword(null, model.Contrasenia), // Hashear aquí
                    Estatus = "Activo"
                };

                _dbocontext.Usuarios.Add(nuevoUsuario);
                _dbocontext.SaveChanges();

                return Json(new { success = true });
            }
            catch (DbUpdateException ex)
            {
                return Json(new { success = false, message = "Error al crear usuario: " + ex.Message });
            }
        }

        // Modelos para las acciones
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
