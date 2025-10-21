using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using MantenimientosTI.Models;
using MantenimientosTI.Services;

namespace MantenimientosTI.Controllers
{
    [Authorize]
    public class DashboardController : Controller
    {
        private readonly ReporteService _reporteService;
        private readonly ILogger<DashboardController> _logger;

        public DashboardController(ReporteService reporteService, ILogger<DashboardController> logger)
        {
            _reporteService = reporteService;
            _logger = logger;
        }

        public IActionResult Dashboard()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> EnviarCorreoPrueba([FromBody] EnviarCorreoModel model)
        {
            try
            {
                if (model.FechaInicio == default || model.FechaFin == default)
                {
                    return Json(new
                    {
                        success = false,
                        message = "Las fechas de inicio y fin son requeridas"
                    });
                }

                if (model.FechaInicio > model.FechaFin)
                {
                    return Json(new
                    {
                        success = false,
                        message = "La fecha de inicio no puede ser mayor a la fecha fin"
                    });
                }

                var resultado = await _reporteService.EnviarCorreoConExcel(model.FechaInicio, model.FechaFin);

                if (resultado)
                {
                    return Json(new
                    {
                        success = true,
                        message = $"El reporte del {model.FechaInicio:dd/MM/yyyy} al {model.FechaFin:dd/MM/yyyy} ha sido enviado exitosamente"
                    });
                }
                else
                {
                    return Json(new
                    {
                        success = false,
                        message = "Error al enviar el correo"
                    });
                }
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = $"Error: {ex.Message}"
                });
            }
        }

        public async Task<bool> EnviarCorreoProgramado()
        {
            try
            {
                _logger.LogInformation("Iniciando envío programado de reporte...");

                // Calcular el mes anterior
                var ahora = DateTime.Now;
                var primerDiaMesAnterior = new DateTime(ahora.Year, ahora.Month, 1).AddMonths(-1);
                var ultimoDiaMesAnterior = new DateTime(ahora.Year, ahora.Month, 1).AddDays(-1);

                var resultado = await _reporteService.EnviarCorreoConExcel(primerDiaMesAnterior, ultimoDiaMesAnterior);

                if (resultado)
                {
                    _logger.LogInformation("Reporte programado enviado exitosamente.");
                    return true;
                }
                else
                {
                    _logger.LogError("Error al enviar reporte programado.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Excepción al enviar reporte programado.");
                return false;
            }
        }
    }

    public class EnviarCorreoModel
    {
        public DateTime FechaInicio { get; set; }
        public DateTime FechaFin { get; set; }
        public DateTime? FechaProgramada { get; set; }
        public string? HoraProgramada { get; set; }
    }
}