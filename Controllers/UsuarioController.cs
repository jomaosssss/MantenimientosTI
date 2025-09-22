using MantenimientosTI.Models;
using MantenimientosTI.Models.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace ProyectoMantenimientos.Controllers
{
    public class UsuarioController : Controller
    {
        
        private readonly MantenimientosTIContext _dbocontext;
        private readonly IHttpClientFactory _httpClientFactory;

        public UsuarioController(MantenimientosTIContext context, IHttpClientFactory httpClientFactory)
        {
            _dbocontext = context;
            _httpClientFactory = httpClientFactory;
        }

        [HttpGet]
        public IActionResult Login()
        {
            return View();
        }

        [Authorize(Roles = "ADMINISTRADOR")]
        [HttpGet]
        public IActionResult ObtenerEstadoAgendarPreventivos()
        {
            var config = _dbocontext.Configuraciones.AsNoTracking()
                .FirstOrDefault(c => c.ClaveConfiguracion == "AGENDAR_PREVENTIVOS");

            bool estaHabilitado = (config != null && config.Valor == "1");

            return Json(new { habilitado = estaHabilitado });
        }

        [Authorize(Roles = "ADMINISTRADOR")]
        [HttpPost]
        public IActionResult CambiarEstadoAgendarPreventivos([FromBody] bool habilitar)
        {
            var config = _dbocontext.Configuraciones
                .FirstOrDefault(c => c.ClaveConfiguracion == "AGENDAR_PREVENTIVOS");

            if (config == null)
            {
                return Json(new { success = false, message = "Clave de configuración no encontrada." });
            }

            try
            {
                config.Valor = habilitar ? "1" : "0";
                _dbocontext.SaveChanges();
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al actualizar la configuración: " + ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> Login(VMLogin model)
        {
            // Validación manual de campos vacíos
            if (string.IsNullOrWhiteSpace(model.Rpe))
            {
                ViewBag.Mensaje = "El campo RPE es requerido";
                return View(model);
            }
            if (string.IsNullOrWhiteSpace(model.Contrasenia))
            {
                ViewBag.Mensaje = "El campo Contraseña es requerido";
                return View(model);
            }

            try
            {
                // 1. Verificar si el usuario existe en tu base de datos local
                var usuario = await _dbocontext.Usuarios
                    .Include(u => u.ClaveRolNavigation)
                    .Include(u => u.CatZona)
                    .FirstOrDefaultAsync(u => u.Rpe == model.Rpe);

                if (usuario == null)
                {
                    ViewBag.Mensaje = "RPE no registrado en el sistema. Contacte al administrador.";
                    return View(model);
                }

                if (usuario.Estatus.ToLower() != "activo")
                {
                    ViewBag.Mensaje = "Usuario inactivo. Contacte al administrador.";
                    return View(model);
                }

                // 2. Obtener la URL de la API desde la base de datos
                var configApi = await _dbocontext.Configuraciones
                                    .FirstOrDefaultAsync(c => c.ClaveConfiguracion == "LDAP_CFE");

                if (configApi == null || string.IsNullOrWhiteSpace(configApi.Valor))
                {
                    ViewBag.Mensaje = "Error de configuración: No se encontró la URL de la API.";
                    return View(model);
                }
                string apiUrl = configApi.Valor;


                // 3. Llamar a la API para validar las credenciales
                var client = _httpClientFactory.CreateClient();
                var apiRequest = new ApiLoginRequest
                {
                    rpe = model.Rpe,
                    contrasenia = model.Contrasenia
                };

                var jsonContent = new StringContent(JsonSerializer.Serialize(apiRequest), Encoding.UTF8, "application/json");
                var response = await client.PostAsync(apiUrl, jsonContent);

                if (response.IsSuccessStatusCode)
                {
                    var responseString = await response.Content.ReadAsStringAsync();
                    var apiResult = JsonSerializer.Deserialize<ApiResponse>(responseString, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    // 4. Procesar la respuesta de la API
                    if (apiResult != null && apiResult.procesoExitoso == 1)
                    {
                        // ¡ÉXITO! Las credenciales son válidas. Ahora usamos los datos locales.
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

                        var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(claimsIdentity));

                        HttpContext.Session.SetString("Rpe", usuario.Rpe);
                        HttpContext.Session.SetString("NombreUsuario", $"{usuario.Nombre} {usuario.ApellidoP} {usuario.ApellidoM}");
                        HttpContext.Session.SetInt32("Rol", usuario.ClaveRol);
                        HttpContext.Session.SetString("NombreRol", usuario.ClaveRolNavigation?.Nombre ?? "Sin rol");
                        HttpContext.Session.SetString("ClaveZona", usuario.ClaveZona);
                        HttpContext.Session.SetString("NombreZona", usuario.CatZona?.NombreZona ?? "Sin zona");
                        HttpContext.Session.SetString("ClaveDivision", usuario.ClaveDivision);

                        return RedirectToAction("Inicio", "Home");
                    }
                    else
                    {
                        // Credenciales incorrectas según la API
                        ViewBag.Mensaje = apiResult?.mensaje ?? "Usuario y/o contraseña incorrectos.";
                        return View(model);
                    }
                }
                else
                {
                    // La llamada a la API falló (ej. 500 Internal Server Error)
                    ViewBag.Mensaje = "Error al contactar el servicio de autenticación. Intente más tarde.";
                    return View(model);
                }
            }
            catch (Exception ex)
            {
                // Manejo de errores generales
                ViewBag.Mensaje = "Ocurrió un error inesperado al iniciar sesión.";
                // Opcional: Registrar el error 'ex' en un log
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
