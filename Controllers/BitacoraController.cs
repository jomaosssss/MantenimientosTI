using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MantenimientosTI.Models;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;

namespace ProyectoMantenimientos.Controllers
{
    [Authorize(Roles = "ADMINISTRADOR")]
    public class BitacoraController : Controller
    {
        private readonly MantenimientosTIContext _context;

        public BitacoraController(MantenimientosTIContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index(string searchString = "", string tipoFiltro = "todo")
        {
            try
            {
                // Obtener informacion del usuario loggeado
                var usuarioActual = new
                {
                    Nombre = HttpContext.Session.GetString("NombreUsuario") ?? "Usuario no identificado",
                    RPE = HttpContext.Session.GetString("Rpe") ?? "N/A",
                    Rol = HttpContext.Session.GetString("NombreRol") ?? "N/A",
                    Zona = HttpContext.Session.GetString("NombreZona") ?? "N/A",
                    Division = HttpContext.Session.GetString("ClaveDivision") ?? "N/A"
                };

                // QUERY BASE CON JOIN A CatAcciones
                // En BitacoraController - Modifica la consulta
                var query = from r in _context.RegistroActividad
                            join a in _context.CatAcciones on r.Accion equals a.ClaveAccion into accionJoin
                            from accion in accionJoin.DefaultIfEmpty()
                            orderby r.FechaHora descending
                            select new VMBitacora
                            {
                                IdRegistroActividad = r.IdRegistroActividad,
                                FechaHora = r.FechaHora,
                                Usuario = r.Usuario,
                                RPE = ExtraerRPE(r.Descripcion),
                                ClaveAccion = r.Accion,
                                // Usar la descripción ya formateada que viene del registro
                                DescripcionAccion = r.Descripcion,
                                DescripcionAdicional = ""
                            };

                // FILTRO DE BÚSQUEDA
                if (!string.IsNullOrEmpty(searchString))
                {
                    query = query.Where(r =>
                        r.Usuario.Contains(searchString) ||
                        r.DescripcionAccion.Contains(searchString) ||
                        r.ClaveAccion.Contains(searchString)
                    );
                }

                // FILTRO POR TIPO
                if (!string.IsNullOrEmpty(tipoFiltro) && tipoFiltro != "todo")
                {
                    query = tipoFiltro switch
                    {
                        "sesion" => query.Where(r =>
                            r.ClaveAccion.Contains("SESION") ||
                            r.ClaveAccion.Contains("INFO_9.55.0") ||
                            r.ClaveAccion.Contains("LOGOUT")),
                        "usuarios" => query.Where(r =>
                            r.ClaveAccion.Contains("USUARIO")),
                        "admin" => query.Where(r =>
                            r.DescripcionAccion.Contains("ADMIN") ||
                            r.Usuario.Contains("ADMIN")),
                        "tecnico" => query.Where(r =>
                            r.DescripcionAccion.Contains("TÉCNICO") ||
                            r.DescripcionAccion.Contains("TECNICO") ||
                            r.Usuario.Contains("TECNICO") ||
                            r.DescripcionAccion.Contains("MEM03")),
                        "mantenimientos" => query.Where(r =>
                            r.ClaveAccion.Contains("MTTO") ||
                            r.ClaveAccion.Contains("MANTENIMIENTO")),
                        "csv" => query.Where(r =>
                            r.ClaveAccion.Contains("CSV")),
                        _ => query
                    };
                }

                // EJECUTAR CONSULTA
                var registros = await query
                    .Take(1000)
                    .ToListAsync();

                // ENVIAR INFORMACIÓN A LA VISTA
                ViewBag.UsuarioActual = usuarioActual;
                ViewBag.SearchString = searchString;
                ViewBag.TipoFiltro = tipoFiltro;
                ViewBag.TotalRegistros = registros.Count;

                return View("BitacoraVista", registros);
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Error al cargar la bitácora: " + ex.Message;

                var usuarioActual = new
                {
                    Nombre = HttpContext.Session.GetString("NombreUsuario") ?? "Usuario no identificado",
                    RPE = "N/A",
                    Rol = "N/A",
                    Zona = "N/A",
                    Division = "N/A"
                };

                ViewBag.UsuarioActual = usuarioActual;
                ViewBag.TotalRegistros = 0;

                return View("BitacoraVista", new List<VMBitacora>());
            }
        }

        // MÉTODO AUXILIAR PARA EXTRAER RPE
        private static string ExtraerRPE(string descripcion)
        {
            if (string.IsNullOrEmpty(descripcion)) return "";

            if (descripcion.Contains("RPE:"))
            {
                var inicio = descripcion.IndexOf("RPE:") + 4;
                var fin = descripcion.IndexOf("|", inicio);
                if (fin == -1) fin = descripcion.Length;
                return descripcion.Substring(inicio, fin - inicio).Trim();
            }

            return "";
        }

        // MÉTODO PARA REGISTRAR EVENTOS
        public static async Task RegistrarEventoCompleto(MantenimientosTIContext context, HttpContext httpContext,
            string accion, string descripcion, string usuarioEspecifico = null)
        {
            try
            {
                var usuario = usuarioEspecifico ?? httpContext.Session.GetString("NombreUsuario") ?? "Usuario no identificado";
                var rpe = httpContext.Session.GetString("Rpe") ?? "N/A";
                var rol = httpContext.Session.GetString("NombreRol") ?? "N/A";
                var zona = httpContext.Session.GetString("NombreZona") ?? "N/A";
                var division = httpContext.Session.GetString("ClaveDivision") ?? "N/A";

                var descripcionCompleta = $"{descripcion} | RPE: {rpe} | Rol: {rol} | Zona: {zona} | Centro: {division}";

                var registro = new RegistroActividad
                {
                    FechaHora = DateTime.Now,
                    Usuario = usuario,
                    Accion = accion,
                    Descripcion = descripcionCompleta
                };

                context.RegistroActividad.Add(registro);
                await context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al registrar evento: {ex.Message}");
            }
        }
    }
}