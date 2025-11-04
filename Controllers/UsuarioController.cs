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
using MantenimientosTI.Services;
using MantenimientosTI.Helpers;

namespace ProyectoMantenimientos.Controllers
{
    public class UsuarioController : Controller
    {
        private readonly MantenimientosTIContext _dbocontext;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly BitacoraService _bitacora;
        private readonly IPasswordHasher<Usuario> _passwordHasher;
        private readonly InformacionSistema _informacionSistema;


        public UsuarioController(MantenimientosTIContext context, IPasswordHasher<Usuario> passwordHasher, BitacoraService bitacora, IHttpClientFactory httpClientFactory, InformacionSistema informacionSistema)
        {
            _dbocontext = context;
            _httpClientFactory = httpClientFactory;
            _passwordHasher = passwordHasher;
            _bitacora = bitacora;
            _informacionSistema = informacionSistema;

        }

        [HttpGet]
        public async Task<IActionResult> Login() 
        {
            
            var infoSistema = await _informacionSistema.ObtenerInfoSistema();

            ViewBag.VersionSistema = infoSistema["Version"];
            ViewBag.FechaLiberacion = infoSistema["Fecha"];
            ViewBag.TipoAmbiente = infoSistema["Ambiente"];

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

            if (model.Rpe.ToUpper() == "ADMIN" || model.Rpe.ToUpper() == "OISM0" || model.Rpe.ToUpper() == "FER01" || model.Rpe.ToUpper() == "MEM03")
            {
                try
                {
                    var usuarioAdmin = await _dbocontext.Usuarios
                        .Include(u => u.ClaveRolNavigation)
                        .Include(u => u.CatZona)
                        .FirstOrDefaultAsync(u => u.Rpe.ToUpper() == model.Rpe.ToUpper());

                    if (usuarioAdmin == null)
                    {
                        ViewBag.Mensaje = "Usuario no encontrado en la base de datos.";
                        return View(model);
                    }

                    var hasher = new PasswordHasher<Usuario>();
                    var result = hasher.VerifyHashedPassword(usuarioAdmin, usuarioAdmin.Contrasenia, model.Contrasenia);

                    if (result != PasswordVerificationResult.Success)
                    {
                        ViewBag.Mensaje = "Contraseña incorrecta para el usuario.";
                        return View(model);
                    }

                    if (usuarioAdmin.Estatus.ToLower() != "activo")
                    {
                        ViewBag.Mensaje = "Usuario ADMIN inactivo. Contacte al administrador.";
                        return View(model);
                    }

                    var claimsAdmin = new List<Claim>
                    {
                        new Claim(ClaimTypes.NameIdentifier, usuarioAdmin.Rpe),
                        new Claim(ClaimTypes.Name, $"{usuarioAdmin.Nombre} {usuarioAdmin.ApellidoP} {usuarioAdmin.ApellidoM}"),
                        new Claim(ClaimTypes.Role, usuarioAdmin.ClaveRolNavigation?.Nombre ?? "Sin rol"),
                        new Claim("RolId", usuarioAdmin.ClaveRol.ToString()),
                        new Claim("Zona", usuarioAdmin.ClaveZona),
                        new Claim("ZonaNombre", usuarioAdmin.CatZona?.NombreZona ?? "Sin zona"),
                        new Claim("Division", usuarioAdmin.ClaveDivision),
                        new Claim("Correo", usuarioAdmin.Correo)
                    };

                    var claimsIdentityAdmin = new ClaimsIdentity(claimsAdmin, CookieAuthenticationDefaults.AuthenticationScheme);
                    await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(claimsIdentityAdmin));

                    HttpContext.Session.SetString("Rpe", usuarioAdmin.Rpe);
                    HttpContext.Session.SetString("NombreUsuario", $"{usuarioAdmin.Nombre} {usuarioAdmin.ApellidoP} {usuarioAdmin.ApellidoM}");
                    HttpContext.Session.SetInt32("Rol", usuarioAdmin.ClaveRol);
                    HttpContext.Session.SetString("NombreRol", usuarioAdmin.ClaveRolNavigation?.Nombre ?? "Sin rol");
                    HttpContext.Session.SetString("ClaveZona", usuarioAdmin.ClaveZona);
                    HttpContext.Session.SetString("NombreZona", usuarioAdmin.CatZona?.NombreZona ?? "Sin zona");
                    HttpContext.Session.SetString("ClaveDivision", usuarioAdmin.ClaveDivision);
                    HttpContext.Session.SetString("Correo", usuarioAdmin.Correo);

                    // Registro en bitacora (usuario local (no del directorio activo))
                    // Para técnicos (línea ~180)
                    var descripcionAdmin = $"Inicio de sesión | RPE: {usuarioAdmin.Rpe} | Rol: {usuarioAdmin.ClaveRolNavigation?.Nombre} | Zona: {usuarioAdmin.CatZona?.NombreZona}";
                    await _bitacora.RegistrarYGuardarAsync(
                        HttpContext.Session.GetString("NombreUsuario"),
                        BitacoraAcciones.InicioSesionAdmin,
                        descripcionAdmin  // ✅ CON FORMATO DE PIPES
                    );

                    return RedirectToAction("Inicio", "Home");
                }
                catch (Exception ex)
                {
                    ViewBag.Mensaje = "Ocurrió un error al iniciar sesión como ADMIN.";
                    return View(model);
                }
            }
            else
            {
                try
                {
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

                    var configApi = await _dbocontext.Configuraciones.FirstOrDefaultAsync(c => c.ClaveConfiguracion == "LDAP_CFE");
                    if (configApi == null || string.IsNullOrWhiteSpace(configApi.Valor))
                    {
                        ViewBag.Mensaje = "Error de configuración: No se encontró la URL de la API.";
                        return View(model);
                    }
                    string apiUrl = configApi.Valor;

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
                            var claims = new List<Claim>
                            {
                                new Claim(ClaimTypes.NameIdentifier, usuario.Rpe),
                                new Claim(ClaimTypes.Name, $"{usuario.Nombre} {usuario.ApellidoP} {usuario.ApellidoM}"),
                                new Claim(ClaimTypes.Role, usuario.ClaveRolNavigation?.Nombre ?? "Sin rol"),
                                new Claim("RolId", usuario.ClaveRol.ToString()),
                                new Claim("Zona", usuario.ClaveZona),
                                new Claim("ZonaNombre", usuario.CatZona?.NombreZona ?? "Sin zona"),
                                new Claim("Division", usuario.ClaveDivision),
                                new Claim("Correo", usuario.Correo)
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
                            HttpContext.Session.SetString("Correo", usuario.Correo);

                            // Registro en bitacora del usuario del directorio activo
                            var descripcionTecnico = $"Inicio de sesión | RPE: {usuario.Rpe} | Rol: {usuario.ClaveRolNavigation?.Nombre} | Zona: {usuario.CatZona?.NombreZona}";
                            await _bitacora.RegistrarYGuardarAsync(
                                HttpContext.Session.GetString("NombreUsuario"),
                                BitacoraAcciones.InicioSesionTecnico,
                                descripcionTecnico  
                            );

                            return RedirectToAction("Inicio", "Home");
                        }
                        else
                        {
                            // Credenciales incorrectas
                            ViewBag.Mensaje = apiResult?.mensaje ?? "Usuario y/o contraseña incorrectos.";
                            return View(model);
                        }
                    }
                    else
                    {
                        ViewBag.Mensaje = "Error al contactar el servicio de autenticación. Intente más tarde.";
                        return View(model);
                    }
                }
                catch (Exception ex)
                {
                    ViewBag.Mensaje = "Ocurrió un error inesperado al iniciar sesión.";
                    return View(model);
                }
            }
        }

        public async Task<IActionResult> CerrarSesion()
        {
            // REGISTRO EN BITÁCORA - LOGOUT
            var usuario = HttpContext.Session.GetString("NombreUsuario");
            var rpe = HttpContext.Session.GetString("Rpe");
            var rol = HttpContext.Session.GetString("NombreRol");
            var zona = HttpContext.Session.GetString("NombreZona");

            if (!string.IsNullOrEmpty(usuario))
            {
                await _bitacora.RegistrarLogoutAsync(usuario, rpe, rol, zona);
            }

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
        [HttpPost]
        public async Task<IActionResult> ActualizarUsuario([FromBody] UsuarioEditModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                // 1. Busca el usuario de forma asíncrona
                var usuario = await _dbocontext.Usuarios.FirstOrDefaultAsync(u => u.Rpe == model.Rpe);
                if (usuario == null)
                {
                    return NotFound(new { success = false, message = "Usuario no encontrado" });
                }

                // 2. Actualiza sus propiedades en memoria
                usuario.ClaveRol = model.ClaveRol;
                usuario.ClaveZona = model.ClaveZona;
                usuario.Nombre = model.Nombre;
                usuario.ApellidoP = model.ApellidoP;
                usuario.ApellidoM = model.ApellidoM;
                usuario.Correo = model.Correo;
                usuario.Estatus = model.Estatus;

                // ✅ 3. BITÁCORA CORREGIDA - CON FORMATO DE PIPES
                var adminQueActualiza = HttpContext.Session.GetString("NombreUsuario") ?? "Sistema";

                // Obtener nombres del rol y zona
                var rolUsuario = await _dbocontext.CatRols
                    .Where(r => r.ClaveRol == model.ClaveRol)
                    .Select(r => r.Nombre)
                    .FirstOrDefaultAsync() ?? "Sin rol";

                var zonaUsuario = await _dbocontext.CatZonas
                    .Where(z => z.ClaveZona == model.ClaveZona)
                    .Select(z => z.NombreZona)
                    .FirstOrDefaultAsync() ?? "Sin zona";

                // ✅ FORMATO CON PIPES para información adicional
                var descripcion = $"Actualizó los datos del usuario '{model.Nombre} {model.ApellidoP}' | RPE: {model.Rpe} | Rol: {rolUsuario} | Zona: {zonaUsuario}";

                await _bitacora.RegistrarYGuardarAsync(adminQueActualiza, BitacoraAcciones.ActualizarUsuario, descripcion);

                await _dbocontext.SaveChangesAsync();

                return Json(new { success = true });
            }
            catch (DbUpdateException ex)
            {
                return Json(new { success = false, message = "Error al guardar en la base de datos: " + (ex.InnerException?.Message ?? ex.Message) });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error inesperado al actualizar el usuario: " + ex.Message });
            }
        }

        [Authorize(Roles = "ADMINISTRADOR")]
        [HttpPost]
        public async Task<IActionResult> CrearUsuario([FromBody] UsuarioCreateModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (await _dbocontext.Usuarios.AnyAsync(u => u.Rpe == model.Rpe))
            {
                return Json(new { success = false, message = "El RPE ya está registrado" });
            }

            try
            {
                var hasher = new PasswordHasher<Usuario>();

                // 1. Prepara el nuevo usuario
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
                    Estatus = "Activo",
                    RecibirReporte = "SI"
                };
                _dbocontext.Usuarios.Add(nuevoUsuario);

                // ✅ 2. BITÁCORA CORREGIDA - CON FORMATO DE PIPES
                var adminQueCrea = HttpContext.Session.GetString("NombreUsuario") ?? "Sistema";

                // Obtener nombres del rol y zona
                var rolNuevoUsuario = await _dbocontext.CatRols
                    .Where(r => r.ClaveRol == model.ClaveRol)
                    .Select(r => r.Nombre)
                    .FirstOrDefaultAsync() ?? "Sin rol";

                var zonaNuevoUsuario = await _dbocontext.CatZonas
                    .Where(z => z.ClaveZona == model.ClaveZona)
                    .Select(z => z.NombreZona)
                    .FirstOrDefaultAsync() ?? "Sin zona";

                // ✅ FORMATO CON PIPES para información adicional
                var descripcion = $"Creó al nuevo usuario '{model.Nombre} {model.ApellidoP}' | RPE: {model.Rpe} | Rol: {rolNuevoUsuario} | Zona: {zonaNuevoUsuario}";

                await _bitacora.RegistrarYGuardarAsync(adminQueCrea, BitacoraAcciones.CrearUsuario, descripcion);

                // 3. Guarda ambos cambios en una sola transacción
                await _dbocontext.SaveChangesAsync();

                return Json(new { success = true });
            }
            catch (DbUpdateException ex)
            {
                return Json(new { success = false, message = "Error al guardar en la base de datos: " + (ex.InnerException?.Message ?? ex.Message) });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error inesperado al crear el usuario: " + ex.Message });
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