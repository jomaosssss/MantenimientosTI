using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using MantenimientosTI.Models;
using MantenimientosTI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MantenimientosTI.Controllers
{
    [Authorize]
    public class DashboardController : Controller
    {
        private readonly ReporteService _reporteService;
        private readonly ILogger<DashboardController> _logger;
        private readonly MantenimientosTIContext _context;
        private readonly IConfiguration _configuration;

        public DashboardController(ReporteService reporteService,
                                   ILogger<DashboardController> logger,
                                   MantenimientosTIContext context,
                                   IConfiguration configuration)
        {
            _reporteService = reporteService;
            _logger = logger;
            _context = context;
            _configuration = configuration;
        }

        public async Task<IActionResult> Dashboard()
        {
            string? claveZonaUsuario = HttpContext.Session.GetString("ClaveZona");
            int claveRol = HttpContext.Session.GetInt32("Rol") ?? 0;
            var hoy = DateOnly.FromDateTime(DateTime.Now);

            // Fechas Mes Actual
            var primerDiaMes = new DateOnly(hoy.Year, hoy.Month, 1);
            var ultimoDiaMes = primerDiaMes.AddMonths(1).AddDays(-1);

            // Fechas Próximo Mes
            var primerDiaProximoMes = primerDiaMes.AddMonths(1);
            var ultimoDiaProximoMes = primerDiaProximoMes.AddMonths(1).AddDays(-1);

            // Fechas Mes Anterior
            var primerDiaMesAnterior = primerDiaMes.AddMonths(-1);
            var ultimoDiaMesAnterior = primerDiaMesAnterior.AddMonths(1).AddDays(-1);

            var estatusExcluidos = new List<string> { "CANCELADO" };

            // Base query
            var baseQuery = _context.Agenda
                .Include(a => a.NumActFijoNavigation) // Incluir navegación para filtrar por zona
                .Where(a => !estatusExcluidos.Contains(a.Estatus));

            // Aplicar filtro de zona si no es Administrador (Rol 1)
            if (claveRol != 1 && !string.IsNullOrEmpty(claveZonaUsuario))
            {
                baseQuery = baseQuery.Where(a => a.NumActFijoNavigation != null &&
                                                a.NumActFijoNavigation.ClaveZona == claveZonaUsuario);
            }

            // Cálculos asyncronos
            int pendientesMesAnteriorCount = await baseQuery
                .CountAsync(a => (a.Estatus == "PENDIENTE" || a.Estatus == "PRE-CANCELADO") &&
                                 a.FechaProgramada >= primerDiaMesAnterior &&
                                 a.FechaProgramada <= ultimoDiaMesAnterior);

            int programadosCount = await baseQuery
                .CountAsync(a => a.FechaProgramada >= primerDiaMes &&
                                 a.FechaProgramada <= ultimoDiaMes);

            int terminadosCount = await baseQuery
                .CountAsync(a => a.Estatus == "TERMINADO" &&
                                 a.FechaProgramada >= primerDiaMes &&
                                 a.FechaProgramada <= ultimoDiaMes);

            int pendientesCount = await baseQuery
                .CountAsync(a => (a.Estatus == "PENDIENTE" || a.Estatus == "PRE-CANCELADO") &&
                                 a.FechaProgramada >= primerDiaMes &&
                                 a.FechaProgramada <= ultimoDiaMes);

            int proximoMesCount = await baseQuery
                .CountAsync(a => a.FechaProgramada >= primerDiaProximoMes &&
                                 a.FechaProgramada <= ultimoDiaProximoMes);

            var viewModel = new DashboardViewModel
            {
                PendientesMesAnteriorCount = pendientesMesAnteriorCount,
                ProgramadosCount = programadosCount,
                TerminadosCount = terminadosCount,
                PendientesCount = pendientesCount,
                ProgramadosProximoMesCount = proximoMesCount
            };

            return View(viewModel);
        }

        // --- ACCIONES PARA ENVÍO MANUAL ---
        [HttpPost]
        public async Task<IActionResult> EnviarCorreoPrueba([FromBody] EnviarCorreoModel model)
        {
            try
            {
                _logger.LogInformation($"Iniciando envío de correo manual: {model.FechaInicio:dd/MM/yyyy} - {model.FechaFin:dd/MM/yyyy}");

                if (model.FechaInicio == default || model.FechaFin == default)
                {
                    return Json(new { success = false, message = "Las fechas de inicio y fin son requeridas" });
                }

                if (model.FechaInicio > model.FechaFin)
                {
                    return Json(new { success = false, message = "La fecha de inicio no puede ser mayor a la fecha fin" });
                }

                var resultado = await _reporteService.EnviarCorreoConExcel(model.FechaInicio, model.FechaFin);

                if (resultado)
                {
                    _logger.LogInformation($"Correo manual enviado exitosamente: {model.FechaInicio:dd/MM/yyyy} - {model.FechaFin:dd/MM/yyyy}");
                    return Json(new { success = true, message = $"El reporte del {model.FechaInicio:dd/MM/yyyy} al {model.FechaFin:dd/MM/yyyy} ha sido enviado exitosamente" });
                }
                else
                {
                    _logger.LogError("Error al enviar el correo manual - resultado false del servicio");
                    return Json(new { success = false, message = "Error al enviar el correo. Verifique la configuración del servidor SMTP o los logs." });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Excepción al enviar correo manual: {Message}", ex.Message);
                return Json(new { success = false, message = $"Error interno al enviar correo: {ex.Message}" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> ProgramarEnvioCorreo([FromBody] ProgramarEnvioModel model)
        {
            try
            {
                if (string.IsNullOrEmpty(model.DiaSemana) || string.IsNullOrEmpty(model.HoraProgramada))
                {
                    return Json(new { success = false, message = "El día y la hora programada son requeridos" });
                }

                if (!TimeOnly.TryParse(model.HoraProgramada, out var hora))
                {
                    return Json(new { success = false, message = "El formato de hora no es válido (HH:mm)" });
                }

                // Guardar/Actualizar DÍA
                var configDia = await _context.Configuraciones
                    .FirstOrDefaultAsync(c => c.ClaveConfiguracion == "PROGRAMACION_REPORTE_DIA");
                if (configDia == null)
                {
                    _context.Configuraciones.Add(new Configuracion { ClaveConfiguracion = "PROGRAMACION_REPORTE_DIA", Valor = model.DiaSemana, Descripcion = "Día de la semana para envío de reporte (ej. Friday)" });
                }
                else { configDia.Valor = model.DiaSemana; }

                // Guardar/Actualizar HORA
                var configHora = await _context.Configuraciones
                    .FirstOrDefaultAsync(c => c.ClaveConfiguracion == "PROGRAMACION_REPORTE_HORA");
                if (configHora == null)
                {
                    _context.Configuraciones.Add(new Configuracion { ClaveConfiguracion = "PROGRAMACION_REPORTE_HORA", Valor = model.HoraProgramada, Descripcion = "Hora del día para envío de reporte (ej. 07:30)" });
                }
                else { configHora.Valor = model.HoraProgramada; }

                // Limpiar última ejecución para forzar la ejecución con la nueva config
                var configUltima = await _context.Configuraciones
                    .FirstOrDefaultAsync(c => c.ClaveConfiguracion == "PROGRAMACION_REPORTE_ULTIMA_EJECUCION");
                if (configUltima != null) { _context.Configuraciones.Remove(configUltima); }

                await _context.SaveChangesAsync();

                _logger.LogInformation($"Programación semanal guardada: {model.DiaSemana} a las {model.HoraProgramada}");

                var diasEs = new Dictionary<string, string> {
                    {"Monday", "Lunes"}, {"Tuesday", "Martes"}, {"Wednesday", "Miércoles"},
                    {"Thursday", "Jueves"}, {"Friday", "Viernes"}, {"Saturday", "Sábado"}, {"Sunday", "Domingo"}
                };
                var diaMostrar = diasEs.GetValueOrDefault(model.DiaSemana, model.DiaSemana);
                var horaMostrar = hora.ToString("hh:mm tt"); // Formato AM/PM

                return Json(new { success = true, message = $"Envío programado para cada {diaMostrar} a las {horaMostrar}" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al programar envío semanal");
                return Json(new { success = false, message = $"Error interno al programar: {ex.Message}" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> ObtenerConfiguracionProgramacion()
        {
            try
            {
                var configDia = await _context.Configuraciones
                    .FirstOrDefaultAsync(c => c.ClaveConfiguracion == "PROGRAMACION_REPORTE_DIA");
                var configHora = await _context.Configuraciones
                    .FirstOrDefaultAsync(c => c.ClaveConfiguracion == "PROGRAMACION_REPORTE_HORA");

                string configuracionActual = "No hay programación activa";
                bool programado = false;

                if (configDia != null && configHora != null && TimeOnly.TryParse(configHora.Valor, out var hora))
                {
                    var diasEs = new Dictionary<string, string> {
                        {"Monday", "Lunes"}, {"Tuesday", "Martes"}, {"Wednesday", "Miércoles"},
                        {"Thursday", "Jueves"}, {"Friday", "Viernes"}, {"Saturday", "Sábado"}, {"Sunday", "Domingo"}
                    };
                    var diaMostrar = diasEs.GetValueOrDefault(configDia.Valor, configDia.Valor);
                    var horaMostrar = hora.ToString("hh:mm tt"); // Formato AM/PM

                    configuracionActual = $"Cada {diaMostrar} a las {horaMostrar}";
                    programado = true; // Hay programación en BD
                }
                else
                {
                    // Respaldo de appsettings (opcional, si aún se llega a usar)
                    var scheduleConfig = _configuration.GetSection("ReportSchedule");
                    if (scheduleConfig.GetValue<bool>("Enabled"))
                    {
                        var dayOfWeek = scheduleConfig.GetValue<string>("DayOfWeek") ?? "Friday";
                        var hour = scheduleConfig.GetValue<int>("Hour");
                        var minute = scheduleConfig.GetValue<int>("Minute");
                        configuracionActual = $"Cada {dayOfWeek} a las {hour:00}:{minute:00} (configuración base)";
                    }
                }

                return Json(new { success = true, configuracionActual, programado });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener configuración de programación");
                return Json(new { success = false, message = "Error al obtener la configuración actual" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> EliminarProgramacion()
        {
            try
            {
                var clavesABorrar = new List<string> {
                    "PROGRAMACION_REPORTE_DIA",
                    "PROGRAMACION_REPORTE_HORA",
                    "PROGRAMACION_REPORTE_ULTIMA_EJECUCION"
                };

                var configs = await _context.Configuraciones
                    .Where(c => clavesABorrar.Contains(c.ClaveConfiguracion))
                    .ToListAsync();

                if (configs.Any())
                {
                    _context.Configuraciones.RemoveRange(configs);
                    await _context.SaveChangesAsync();
                    _logger.LogInformation("Programación de envío semanal eliminada");
                }

                return Json(new { success = true, message = "Programación eliminada correctamente" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al eliminar programación semanal");
                return Json(new { success = false, message = $"Error interno al eliminar: {ex.Message}" });
            }
        }
    }

    // --- VIEW MODELS ---

    public class DashboardViewModel
    {
        public int PendientesMesAnteriorCount { get; set; }
        public int ProgramadosCount { get; set; }
        public int TerminadosCount { get; set; }
        public int PendientesCount { get; set; }
        public int ProgramadosProximoMesCount { get; set; }
    }

    public class EnviarCorreoModel
    {
        public DateTime FechaInicio { get; set; }
        public DateTime FechaFin { get; set; }
    }

    public class ProgramarEnvioModel
    {
        public string DiaSemana { get; set; } // Monday, Tuesday, etc.
        public string HoraProgramada { get; set; } // Formato "HH:mm"
    }
}