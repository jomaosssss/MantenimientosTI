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

        public async Task<IActionResult> Index(string searchString, string tipoFiltro = "todo")
        {
            // OBTENER INFORMACIÓN DEL USUARIO ACTUAL DESDE SESIÓN
            var usuarioActual = new
            {
                Nombre = HttpContext.Session.GetString("NombreUsuario"),
                RPE = HttpContext.Session.GetString("Rpe"),
                Rol = HttpContext.Session.GetString("NombreRol"),
                Zona = HttpContext.Session.GetString("NombreZona"),
                Division = HttpContext.Session.GetString("ClaveDivision")
            };

            var query = _context.RegistroActividad
                .OrderByDescending(r => r.FechaHora)
                .AsQueryable();

            // FILTROS
            if (!string.IsNullOrEmpty(searchString))
            {
                query = query.Where(r =>
                    r.Usuario.Contains(searchString) ||
                    r.Descripcion.Contains(searchString) ||
                    r.Accion.Contains(searchString)
                );
            }

            if (tipoFiltro != "todo")
            {
                query = tipoFiltro switch
                {
                    "sesion" => query.Where(r => r.Accion.Contains("INICIO_SESION") || r.Accion.Contains("INFO_9.55.0")),
                    "usuarios" => query.Where(r => r.Accion.Contains("USUARIO")),
                    "admin" => query.Where(r => r.Descripcion.Contains("ADMIN") || r.Usuario.Contains("ADMIN")),
                    "tecnico" => query.Where(r => r.Descripcion.Contains("TÉCNICO") || r.Usuario.Contains("TECNICO") || r.Descripcion.Contains("MEM03")),
                    _ => query
                };
            }

            var registros = await query
                .Take(1000)
                .Select(r => new VMBitacora
                {
                    FechaHora = r.FechaHora,
                    Usuario = r.Usuario,
                    RPE = "",
                    ClaveAccion = r.Accion,
                    DescripcionAccion = r.Descripcion,
                    DescripcionAdicional = ""
                })
                .ToListAsync();

            // ENVIAR INFORMACIÓN A LA VISTA
            ViewBag.UsuarioActual = usuarioActual;
            ViewBag.SearchString = searchString;
            ViewBag.TipoFiltro = tipoFiltro;
            ViewBag.TotalRegistros = registros.Count;

            return View("BitacoraVista", registros);
        }

        // MÉTODO PARA REGISTRAR EVENTOS CON ZONA Y CENTRO
        public static async Task RegistrarEventoCompleto(MantenimientosTIContext context, HttpContext httpContext,
            string accion, string descripcion, string usuarioEspecifico = null)
        {
            var usuario = usuarioEspecifico ?? httpContext.Session.GetString("NombreUsuario");
            var rpe = httpContext.Session.GetString("Rpe");
            var rol = httpContext.Session.GetString("NombreRol");
            var zona = httpContext.Session.GetString("NombreZona");
            var division = httpContext.Session.GetString("ClaveDivision");

            // CONSTRUIR DESCRIPCIÓN CON ZONA Y CENTRO
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
    }
}