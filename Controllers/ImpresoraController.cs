using Microsoft.AspNetCore.Mvc;
using MantenimientosTI.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;

namespace MantenimientosTI.Controllers
{
    [Authorize(Roles = "ADMINISTRADOR,TÉCNICO DE ZONA")]
    public class ImpresoraController : Controller
    {
        private readonly MantenimientosTIContext _dbContext;

        public ImpresoraController(MantenimientosTIContext context)
        {
            _dbContext = context;
        }

        public IActionResult AgendarImpresoras()
        {
            // Obtener la zona del usuario logueado
            string? claveZonaUsuario = HttpContext.Session.GetString("ClaveZona");
            string? claveDivisionUsuario = HttpContext.Session.GetString("ClaveDivision");

            if (string.IsNullOrEmpty(claveZonaUsuario) || string.IsNullOrEmpty(claveDivisionUsuario))
            {
                return RedirectToAction("Login", "Account");
            }

            // Obtener la zona actual del usuario
            var zonaUsuario = _dbContext.CatZonas
                .FirstOrDefault(z => z.ClaveDivision == claveDivisionUsuario &&
                                   z.ClaveZona == claveZonaUsuario);

            // Obtener las agencias de esa zona
            var agencias = _dbContext.CatAgencia
                .Where(a => a.ClaveDivision == claveDivisionUsuario &&
                           a.ClaveZona == claveZonaUsuario)
                .ToList();

            ViewBag.ZonaUsuario = zonaUsuario?.NombreZona ?? "Zona no identificada";
            ViewBag.Agencias = agencias;

            return View();
        }

        [HttpGet]
        public IActionResult ObtenerCentrosPorAgencia(string division, string zona, string agencia)
        {
            if (string.IsNullOrEmpty(division) || string.IsNullOrEmpty(zona) || string.IsNullOrEmpty(agencia))
            {
                return BadRequest(new { success = false, message = "Parámetros incompletos" });
            }

            try
            {
                var centros = _dbContext.CatCentros
                    .Where(c => c.ClaveDivision == division &&
                               c.ClaveZona == zona &&
                               c.ClaveAgencia == agencia)
                    .OrderBy(c => c.NombreCentro)
                    .Select(c => new
                    {
                        value = c.ClaveCentro,
                        text = c.NombreCentro ?? "Sin nombre"
                    })
                    .ToList();

                if (!centros.Any())
                {
                    return Json(new { success = true, data = new[] { new { value = "", text = "No se encontraron centros" } } });
                }

                return Json(new { success = true, data = centros });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public IActionResult ObtenerImpresorasPorCentro(string division, string zona, string agencia, string centro)
        {
            try
            {
                var impresoras = _dbContext.Impresoras
                    .Include(i => i.Equipo)
                    .Where(i => i.Equipo.ClaveDivision == division &&
                               i.Equipo.ClaveZona == zona &&
                               i.Equipo.ClaveAgencia == agencia &&
                               i.Equipo.ClaveCentro == centro)
                    .Select(i => new
                    {
                        value = i.NumActFijo,
                        text = $"{i.NumActFijo} - {i.Modelo} - {i.NumSerie}"
                    })
                    .Distinct()
                    .OrderBy(i => i.value)
                    .ToList();

                return Json(new { success = true, data = impresoras });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        public IActionResult Index()
        {
            return View();
        }
    }
}