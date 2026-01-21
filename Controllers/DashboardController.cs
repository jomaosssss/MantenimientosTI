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
using System.Text.Json;

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

        [Authorize(Roles = "ADMINISTRADOR, CONSULTOR")]
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

            // Base query - SIMPLIFICADA
            var baseQuery = _context.Agenda
                .Where(a => !estatusExcluidos.Contains(a.Estatus));

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

            // Cálculo de datos para gráficas por zona
            var graficasZonas = new List<ZonaChartData>();

            // Obtener todas las zonas
            var zonas = await _context.CatZonas.ToListAsync();

            foreach (var zona in zonas)
            {
                var queryZona = _context.Agenda
                    .Where(a => !estatusExcluidos.Contains(a.Estatus) &&
                               a.FechaProgramada >= primerDiaMes &&
                               a.FechaProgramada <= ultimoDiaMes &&
                               a.NumActFijoNavigation != null &&
                               a.NumActFijoNavigation.ClaveZona == zona.ClaveZona);

                var totalProgramados = await queryZona.CountAsync();
                var terminados = await queryZona.CountAsync(a => a.Estatus == "TERMINADO");
                var pendientes = await queryZona.CountAsync(a => a.Estatus == "PENDIENTE" || a.Estatus == "PRE-CANCELADO");
                var otros = totalProgramados - terminados - pendientes;

                graficasZonas.Add(new ZonaChartData
                {
                    Zona = zona.ClaveZona,
                    NombreZona = zona.NombreZona ?? zona.ClaveZona,
                    TotalProgramados = totalProgramados,
                    Terminados = terminados,
                    Pendientes = pendientes,
                    Otros = otros > 0 ? otros : 0,
                    TieneDatos = totalProgramados > 0
                });
            }

            var viewModel = new DashboardViewModel
            {
                PendientesMesAnteriorCount = pendientesMesAnteriorCount,
                ProgramadosCount = programadosCount,
                TerminadosCount = terminadosCount,
                PendientesCount = pendientesCount,
                ProgramadosProximoMesCount = proximoMesCount,
                GraficasZonas = graficasZonas,
                Zonas = zonas,
                TiposEquipo = new List<string> { "TODOS", "PC", "LAPTOP", "CFECAM", "CFETURNO", "CFEMÁTICOS" }
            };

            return View(viewModel);
        }

        [HttpPost]
        public async Task<IActionResult> ObtenerDatosGraficasFiltroCompleto([FromBody] FiltroCompletoModel model)
        {
            try
            {
                _logger.LogInformation($"ObtenerDatosGraficasFiltroCompleto: {model.FechaInicio:dd/MM/yyyy} - {model.FechaFin:dd/MM/yyyy}, Zona: {model.ZonaSeleccionada}");

                if (model.FechaInicio == default || model.FechaFin == default)
                {
                    return Json(new { success = false, message = "Las fechas de inicio y fin son requeridas" });
                }

                if (model.FechaInicio > model.FechaFin)
                {
                    return Json(new { success = false, message = "La fecha de inicio no puede ser mayor a la fecha fin" });
                }

                var primerDia = DateOnly.FromDateTime(model.FechaInicio);
                var ultimoDia = DateOnly.FromDateTime(model.FechaFin);

                var estatusExcluidos = new List<string> { "CANCELADO" };

                // Base query con filtro de fechas
                var baseQuery = _context.Agenda
                    .Where(a => !estatusExcluidos.Contains(a.Estatus) &&
                               a.FechaProgramada >= primerDia &&
                               a.FechaProgramada <= ultimoDia);

                // Determinar si mostramos por zona o por tipo de equipo
                if (model.ZonaSeleccionada == "TODAS")
                {
                    // Mostrar gráficas por ZONA - CORREGIDO: No aplicar filtro de zona específica
                    var graficasZonas = new List<object>();
                    var zonas = await _context.CatZonas.ToListAsync();

                    foreach (var zona in zonas)
                    {
                        // CONSULTA CORREGIDA: Solo filtramos por zona sin restricción adicional
                        var queryZona = baseQuery
                            .Where(a => a.NumActFijoNavigation != null &&
                                       a.NumActFijoNavigation.ClaveZona == zona.ClaveZona);

                        var totalProgramados = await queryZona.CountAsync();
                        var terminados = await queryZona.CountAsync(a => a.Estatus == "TERMINADO");
                        var pendientes = await queryZona.CountAsync(a => a.Estatus == "PENDIENTE" || a.Estatus == "PRE-CANCELADO");
                        var otros = totalProgramados - terminados - pendientes;

                        graficasZonas.Add(new
                        {
                            zona = zona.ClaveZona,
                            nombreZona = zona.NombreZona ?? zona.ClaveZona,
                            totalProgramados,
                            terminados,
                            pendientes,
                            otros = otros > 0 ? otros : 0,
                            tieneDatos = totalProgramados > 0
                        });
                    }

                    _logger.LogInformation($"Gráficas por zona TODAS: {graficasZonas.Count} zonas procesadas, {graficasZonas.Count(z => ((dynamic)z).tieneDatos)} con datos");

                    return Json(new { success = true, datosGraficas = graficasZonas, tipo = "zonas" });
                }
                else
                {
                    // Mostrar gráficas por TIPO DE EQUIPO para la zona seleccionada
                    var graficasTipos = new List<object>();

                    // CORREGIDO: Aplicar filtro de zona específica
                    var baseQueryConZona = baseQuery
                        .Where(a => a.NumActFijoNavigation != null &&
                                   a.NumActFijoNavigation.ClaveZona == model.ZonaSeleccionada);

                    // 1. CFEMÁTICOS
                    var cfematicosQuery = baseQueryConZona
                        .Where(a => _context.EquipoCfematicos.Any(ec => ec.NumActFijo == a.NumActFijo));

                    var cfematicosProgramados = await cfematicosQuery.CountAsync();
                    var cfematicosTerminados = await cfematicosQuery.CountAsync(a => a.Estatus == "TERMINADO");
                    var cfematicosPendientes = await cfematicosQuery.CountAsync(a => a.Estatus == "PENDIENTE" || a.Estatus == "PRE-CANCELADO");
                    var cfematicosOtros = cfematicosProgramados - cfematicosTerminados - cfematicosPendientes;

                    graficasTipos.Add(new
                    {
                        tipoEquipo = "CFEMÁTICOS",
                        programados = cfematicosProgramados,
                        terminados = cfematicosTerminados,
                        pendientes = cfematicosPendientes,
                        otros = cfematicosOtros > 0 ? cfematicosOtros : 0
                    });

                    // 2. EQUIPOS DE ATENCIÓN A CLIENTES
                    var equiposACQuery = baseQueryConZona
                        .Where(a => _context.EquipoAcs.Any(ea => ea.NumActFijo == a.NumActFijo));

                    var equiposACProgramados = await equiposACQuery.CountAsync();
                    var equiposACTerminados = await equiposACQuery.CountAsync(a => a.Estatus == "TERMINADO");
                    var equiposACPendientes = await equiposACQuery.CountAsync(a => a.Estatus == "PENDIENTE" || a.Estatus == "PRE-CANCELADO");
                    var equiposACOtros = equiposACProgramados - equiposACTerminados - equiposACPendientes;

                    graficasTipos.Add(new
                    {
                        tipoEquipo = "EQUIPOS_AC",
                        programados = equiposACProgramados,
                        terminados = equiposACTerminados,
                        pendientes = equiposACPendientes,
                        otros = equiposACOtros > 0 ? equiposACOtros : 0
                    });

                    // 3. EQUIPOS DE CÓMPUTO
                    var equiposComputoQuery = baseQueryConZona
                        .Where(a => _context.EquipoComputos.Any(ec => ec.NumActFijo == a.NumActFijo));

                    var equiposComputoProgramados = await equiposComputoQuery.CountAsync();
                    var equiposComputoTerminados = await equiposComputoQuery.CountAsync(a => a.Estatus == "TERMINADO");
                    var equiposComputoPendientes = await equiposComputoQuery.CountAsync(a => a.Estatus == "PENDIENTE" || a.Estatus == "PRE-CANCELADO");
                    var equiposComputoOtros = equiposComputoProgramados - equiposComputoTerminados - equiposComputoPendientes;

                    graficasTipos.Add(new
                    {
                        tipoEquipo = "EQUIPOS_COMPUTO",
                        programados = equiposComputoProgramados,
                        terminados = equiposComputoTerminados,
                        pendientes = equiposComputoPendientes,
                        otros = equiposComputoOtros > 0 ? equiposComputoOtros : 0
                    });

                    _logger.LogInformation($"Gráficas por tipo equipo - Zona: {model.ZonaSeleccionada}");
                    _logger.LogInformation($"CFEMÁTICOS: Programados={cfematicosProgramados}, Terminados={cfematicosTerminados}, Pendientes={cfematicosPendientes}");
                    _logger.LogInformation($"EQUIPOS_AC: Programados={equiposACProgramados}, Terminados={equiposACTerminados}, Pendientes={equiposACPendientes}");
                    _logger.LogInformation($"EQUIPOS_COMPUTO: Programados={equiposComputoProgramados}, Terminados={equiposComputoTerminados}, Pendientes={equiposComputoPendientes}");

                    return Json(new { success = true, datosGraficas = graficasTipos, tipo = "tiposEquipo" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener datos de gráficas con filtro completo");
                return Json(new { success = false, message = $"Error interno: {ex.Message}" });
            }
        }

        [Authorize(Roles = "ADMINISTRADOR,TÉCNICO DE ZONA")]
        public async Task<IActionResult> DashboardTecnico()
        {
            string? claveZonaUsuario = HttpContext.Session.GetString("ClaveZona");
            var hoy = DateOnly.FromDateTime(DateTime.Now);

            // Fechas Mes Actual
            var primerDiaMes = new DateOnly(hoy.Year, hoy.Month, 1);
            var ultimoDiaMes = primerDiaMes.AddMonths(1).AddDays(-1);

            var estatusExcluidos = new List<string> { "CANCELADO" };

            // Base query - SIMPLIFICADA
            var baseQuery = _context.Agenda
                .Where(a => !estatusExcluidos.Contains(a.Estatus) &&
                           a.FechaProgramada >= primerDiaMes &&
                           a.FechaProgramada <= ultimoDiaMes);

            // Aplicar filtro de zona del técnico
            if (!string.IsNullOrEmpty(claveZonaUsuario))
            {
                baseQuery = baseQuery.Where(a => a.NumActFijoNavigation != null &&
                                                a.NumActFijoNavigation.ClaveZona == claveZonaUsuario);
            }

            // Cálculos asyncronos
            int programadosCount = await baseQuery.CountAsync();
            int terminadosCount = await baseQuery.CountAsync(a => a.Estatus == "TERMINADO");
            int pendientesCount = await baseQuery.CountAsync(a => a.Estatus == "PENDIENTE" || a.Estatus == "PRE-CANCELADO");

            // Obtener todas las agencias de la zona del usuario
            var agencias = await _context.CatAgencia
                .Where(a => a.ClaveZona == claveZonaUsuario)
                .ToListAsync();

            // Cálculo de datos para gráficas por AGENCIA
            var graficasAgencias = new List<AgenciaChartData>();

            foreach (var agencia in agencias)
            {
                var queryAgencia = baseQuery
                    .Where(a => a.NumActFijoNavigation != null &&
                               a.NumActFijoNavigation.ClaveAgencia == agencia.ClaveAgencia);

                var totalProgramados = await queryAgencia.CountAsync();
                var terminados = await queryAgencia.CountAsync(a => a.Estatus == "TERMINADO");
                var pendientes = await queryAgencia.CountAsync(a => a.Estatus == "PENDIENTE" || a.Estatus == "PRE-CANCELADO");
                var otros = totalProgramados - terminados - pendientes;

                graficasAgencias.Add(new AgenciaChartData
                {
                    Agencia = agencia.ClaveAgencia,
                    NombreAgencia = agencia.NombreAgencia ?? agencia.ClaveAgencia,
                    TotalProgramados = totalProgramados,
                    Terminados = terminados,
                    Pendientes = pendientes,
                    Otros = otros > 0 ? otros : 0,
                    TieneDatos = totalProgramados > 0
                });
            }

            var viewModel = new DashboardTecnicoViewModel
            {
                ProgramadosCount = programadosCount,
                TerminadosCount = terminadosCount,
                PendientesCount = pendientesCount,
                GraficasAgencias = graficasAgencias,
                Agencias = agencias
            };

            return View(viewModel);
        }

        [HttpPost]
        public async Task<IActionResult> ObtenerDatosGraficasCentrosPorAgencia([FromBody] FiltroCentroModel model)
        {
            try
            {
                string? claveZonaUsuario = HttpContext.Session.GetString("ClaveZona");

                if (model.FechaInicio == default || model.FechaFin == default)
                {
                    return Json(new { success = false, message = "Las fechas de inicio y fin son requeridas" });
                }

                if (model.FechaInicio > model.FechaFin)
                {
                    return Json(new { success = false, message = "La fecha de inicio no puede ser mayor a la fecha fin" });
                }

                var primerDia = DateOnly.FromDateTime(model.FechaInicio);
                var ultimoDia = DateOnly.FromDateTime(model.FechaFin);

                var estatusExcluidos = new List<string> { "CANCELADO" };

                // Base query - FILTRADO POR ZONA DEL USUARIO
                var baseQuery = _context.Agenda
                    .Include(a => a.NumActFijoNavigation)
                    .Where(a => !estatusExcluidos.Contains(a.Estatus));

                // Aplicar filtro de zona del técnico
                if (!string.IsNullOrEmpty(claveZonaUsuario))
                {
                    baseQuery = baseQuery.Where(a => a.NumActFijoNavigation != null &&
                                                    a.NumActFijoNavigation.ClaveZona == claveZonaUsuario);
                }

                // Cálculo de datos para gráficas por CENTRO de la agencia seleccionada
                var graficasCentros = new List<object>();

                // Obtener todos los centros de la agencia seleccionada
                var centrosQuery = _context.CatCentros
                    .Where(c => c.ClaveAgencia == model.AgenciaSeleccionada &&
                               c.ClaveZona == claveZonaUsuario);

                var centros = await centrosQuery.ToListAsync();

                foreach (var centro in centros)
                {
                    var queryCentro = baseQuery.Where(a => a.NumActFijoNavigation != null &&
                                                          a.NumActFijoNavigation.ClaveAgencia == model.AgenciaSeleccionada &&
                                                          a.NumActFijoNavigation.ClaveCentro == centro.ClaveCentro &&
                                                          a.FechaProgramada >= primerDia &&
                                                          a.FechaProgramada <= ultimoDia);

                    var totalProgramados = await queryCentro.CountAsync();
                    var terminados = await queryCentro.CountAsync(a => a.Estatus == "TERMINADO");
                    var pendientes = await queryCentro.CountAsync(a => a.Estatus == "PENDIENTE" || a.Estatus == "PRE-CANCELADO");
                    var otros = totalProgramados - terminados - pendientes;

                    graficasCentros.Add(new
                    {
                        centro = centro.ClaveCentro,
                        nombreCentro = centro.NombreCentro ?? centro.ClaveCentro,
                        totalProgramados,
                        terminados,
                        pendientes,
                        otros = otros > 0 ? otros : 0,
                        tieneDatos = totalProgramados > 0
                    });
                }

                return Json(new { success = true, datosGraficas = graficasCentros, tipo = "centros" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener datos de gráficas por centro y agencia");
                return Json(new { success = false, message = $"Error interno: {ex.Message}" });
            }
        }

        public class FiltroCentroModel
        {
            public DateTime FechaInicio { get; set; }
            public DateTime FechaFin { get; set; }
            public string AgenciaSeleccionada { get; set; }
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

                // Pasar la lista de usuarios seleccionados al servicio
                var resultado = await _reporteService.EnviarCorreoConExcel(model.FechaInicio, model.FechaFin, model.Usuarios);

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

        [HttpGet]
        public async Task<IActionResult> ObtenerUsuariosAdministradores()
        {
            try
            {
                var usuariosAdministradores = await _context.Usuarios
                    .Where(u => u.ClaveRol != 2 && u.Estatus.ToLower() == "activo")
                    .Select(u => new
                    {
                        rpe = u.Rpe,
                        nombre = u.Nombre,
                        apellidoP = u.ApellidoP,
                        apellidoM = u.ApellidoM,
                        correo = u.Correo,
                        recibirReporte = u.RecibirReporte
                    })
                    .ToListAsync();

                return Json(new { success = true, usuarios = usuariosAdministradores });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener usuarios administradores");
                return Json(new { success = false, message = "Error al cargar usuarios" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> ProgramarEnvioCorreo([FromBody] ProgramarEnvioModel model)
        {
            try
            {
                if (string.IsNullOrEmpty(model.DiaSemana) || model.HorasProgramadas == null || !model.HorasProgramadas.Any())
                {
                    return Json(new { success = false, message = "El día y al menos un horario son requeridos" });
                }

                // Validar formato de horas
                foreach (var horaStr in model.HorasProgramadas)
                {
                    if (!TimeOnly.TryParse(horaStr, out var hora))
                    {
                        return Json(new { success = false, message = "El formato de hora no es válido (HH:mm)" });
                    }
                }

                // Guardar/Actualizar DÍA
                var configDia = await _context.Configuraciones
                    .FirstOrDefaultAsync(c => c.ClaveConfiguracion == "PROGRAMACION_REPORTE_DIA");
                if (configDia == null)
                {
                    _context.Configuraciones.Add(new Configuracion
                    {
                        ClaveConfiguracion = "PROGRAMACION_REPORTE_DIA",
                        Valor = model.DiaSemana,
                        Descripcion = "Día de la semana para envío de reporte (ej. Friday)"
                    });
                }
                else
                {
                    configDia.Valor = model.DiaSemana;
                }

                // Guardar/Actualizar HORAS (como JSON array)
                var horasJson = System.Text.Json.JsonSerializer.Serialize(model.HorasProgramadas);
                var configHoras = await _context.Configuraciones
                    .FirstOrDefaultAsync(c => c.ClaveConfiguracion == "PROGRAMACION_REPORTE_HORAS");
                if (configHoras == null)
                {
                    _context.Configuraciones.Add(new Configuracion
                    {
                        ClaveConfiguracion = "PROGRAMACION_REPORTE_HORAS",
                        Valor = horasJson,
                        Descripcion = "Horas programadas para envío de reporte (formato JSON array)"
                    });
                }
                else
                {
                    configHoras.Valor = horasJson;
                }

                // Limpiar última ejecución para forzar la ejecución con la nueva config
                var configsUltima = await _context.Configuraciones
                    .Where(c => c.ClaveConfiguracion.StartsWith("PROGRAMACION_REPORTE_ULTIMA_EJECUCION_"))
                    .ToListAsync();
                _context.Configuraciones.RemoveRange(configsUltima);

                await _context.SaveChangesAsync();

                _logger.LogInformation($"Programación semanal guardada: {model.DiaSemana} a las {string.Join(" y ", model.HorasProgramadas)}");

                var diasEs = new Dictionary<string, string> {
            {"Monday", "Lunes"}, {"Tuesday", "Martes"}, {"Wednesday", "Miércoles"},
            {"Thursday", "Jueves"}, {"Friday", "Viernes"}, {"Saturday", "Sábado"}, {"Sunday", "Domingo"}
        };
                var diaMostrar = diasEs.GetValueOrDefault(model.DiaSemana, model.DiaSemana);

                var horasMostrar = model.HorasProgramadas.Select(h =>
                    TimeOnly.Parse(h).ToString("hh:mm tt")).ToArray();

                string mensajeHoras;
                if (horasMostrar.Length == 1)
                {
                    mensajeHoras = $"a las {horasMostrar[0]}";
                }
                else
                {
                    mensajeHoras = $"a las {horasMostrar[0]} y {horasMostrar[1]}";
                }

                return Json(new { success = true, message = $"Envío programado para cada {diaMostrar} {mensajeHoras}" });
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
                var configHoras = await _context.Configuraciones
                    .FirstOrDefaultAsync(c => c.ClaveConfiguracion == "PROGRAMACION_REPORTE_HORAS");

                string configuracionActual = "No hay programación activa";
                bool programado = false;

                if (configDia != null && configHoras != null)
                {
                    try
                    {
                        var horas = System.Text.Json.JsonSerializer.Deserialize<List<string>>(configHoras.Valor);
                        if (horas != null && horas.Any())
                        {
                            var diasEs = new Dictionary<string, string> {
                        {"Monday", "Lunes"}, {"Tuesday", "Martes"}, {"Wednesday", "Miércoles"},
                        {"Thursday", "Jueves"}, {"Friday", "Viernes"}, {"Saturday", "Sábado"}, {"Sunday", "Domingo"}
                    };
                            var diaMostrar = diasEs.GetValueOrDefault(configDia.Valor, configDia.Valor);

                            var horasMostrar = horas.Select(h =>
                                TimeOnly.Parse(h).ToString("hh:mm tt")).ToArray();

                            if (horasMostrar.Length == 1)
                            {
                                configuracionActual = $"cada {diaMostrar} a las {horasMostrar[0]}";
                            }
                            else
                            {
                                configuracionActual = $"cada {diaMostrar} a las {horasMostrar[0]} y {horasMostrar[1]}";
                            }

                            programado = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error al deserializar horas programadas");
                    }
                }
                else
                {
                    var scheduleConfig = _configuration.GetSection("ReportSchedule");
                    if (scheduleConfig.GetValue<bool>("Enabled"))
                    {
                        var dayOfWeek = scheduleConfig.GetValue<string>("DayOfWeek") ?? "Friday";
                        var hour = scheduleConfig.GetValue<int>("Hour");
                        var minute = scheduleConfig.GetValue<int>("Minute");
                        configuracionActual = $"cada {dayOfWeek} a las {hour:00}:{minute:00} (configuración base)";
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
            "PROGRAMACION_REPORTE_HORAS"
        };

                // También eliminar todas las configuraciones de última ejecución
                var configsUltima = await _context.Configuraciones
                    .Where(c => c.ClaveConfiguracion.StartsWith("PROGRAMACION_REPORTE_ULTIMA_EJECUCION_"))
                    .Select(c => c.ClaveConfiguracion)
                    .ToListAsync();
                clavesABorrar.AddRange(configsUltima);

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

        [HttpPost]
        public async Task<IActionResult> ObtenerDatosGraficasAgenciasPorZona([FromBody] FiltroZonaModel model)
        {
            try
            {
                if (model.FechaInicio == default || model.FechaFin == default)
                {
                    return Json(new { success = false, message = "Las fechas de inicio y fin son requeridas" });
                }

                if (model.FechaInicio > model.FechaFin)
                {
                    return Json(new { success = false, message = "La fecha de inicio no puede ser mayor a la fecha fin" });
                }

                var primerDia = DateOnly.FromDateTime(model.FechaInicio);
                var ultimoDia = DateOnly.FromDateTime(model.FechaFin);

                var estatusExcluidos = new List<string> { "CANCELADO" };

                // Base query
                var baseQuery = _context.Agenda
                    .Include(a => a.NumActFijoNavigation)
                    .Where(a => !estatusExcluidos.Contains(a.Estatus));

                // Cálculo de datos para gráficas por AGENCIA de la zona seleccionada
                var graficasAgencias = new List<object>();

                // Obtener todas las agencias de la zona seleccionada
                var agenciasQuery = _context.CatAgencia
                    .Where(a => a.ClaveZona == model.ZonaSeleccionada);

                var agencias = await agenciasQuery.ToListAsync();

                foreach (var agencia in agencias)
                {
                    var queryAgencia = baseQuery.Where(a => a.NumActFijoNavigation != null &&
                                                           a.NumActFijoNavigation.ClaveZona == model.ZonaSeleccionada &&
                                                           a.NumActFijoNavigation.ClaveAgencia == agencia.ClaveAgencia &&
                                                           a.FechaProgramada >= primerDia &&
                                                           a.FechaProgramada <= ultimoDia);

                    var totalProgramados = await queryAgencia.CountAsync();
                    var terminados = await queryAgencia.CountAsync(a => a.Estatus == "TERMINADO");
                    var pendientes = await queryAgencia.CountAsync(a => a.Estatus == "PENDIENTE" || a.Estatus == "PRE-CANCELADO");
                    var otros = totalProgramados - terminados - pendientes;

                    graficasAgencias.Add(new
                    {
                        agencia = agencia.ClaveAgencia,
                        nombreAgencia = agencia.NombreAgencia ?? agencia.ClaveAgencia,
                        totalProgramados,
                        terminados,
                        pendientes,
                        otros = otros > 0 ? otros : 0,
                        tieneDatos = totalProgramados > 0
                    });
                }

                return Json(new { success = true, datosGraficas = graficasAgencias, tipo = "agencias" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener datos de gráficas por agencia y zona");
                return Json(new { success = false, message = $"Error interno: {ex.Message}" });
            }
        }

        public class FiltroZonaModel
        {
            public DateTime FechaInicio { get; set; }
            public DateTime FechaFin { get; set; }
            public string ZonaSeleccionada { get; set; }
        }


        [HttpPost]
        public async Task<IActionResult> ObtenerDatosGraficasPorFecha([FromBody] EnviarCorreoModel model)
        {
            try
            {
                string? claveZonaUsuario = HttpContext.Session.GetString("ClaveZona");
                int claveRol = HttpContext.Session.GetInt32("Rol") ?? 0;

                if (model.FechaInicio == default || model.FechaFin == default)
                {
                    return Json(new { success = false, message = "Las fechas de inicio y fin son requeridas" });
                }

                if (model.FechaInicio > model.FechaFin)
                {
                    return Json(new { success = false, message = "La fecha de inicio no puede ser mayor a la fecha fin" });
                }

                var primerDia = DateOnly.FromDateTime(model.FechaInicio);
                var ultimoDia = DateOnly.FromDateTime(model.FechaFin);

                var estatusExcluidos = new List<string> { "CANCELADO" };

                // Base query
                var baseQuery = _context.Agenda
                    .Include(a => a.NumActFijoNavigation)
                    .Where(a => !estatusExcluidos.Contains(a.Estatus));

                // Aplicar filtro de zona si no es Administrador (Rol 1)
                if (claveRol != 1 && !string.IsNullOrEmpty(claveZonaUsuario))
                {
                    baseQuery = baseQuery.Where(a => a.NumActFijoNavigation != null &&
                                                    a.NumActFijoNavigation.ClaveZona == claveZonaUsuario);
                }

                // Cálculo de datos para gráficas por zona
                var graficasZonas = new List<object>();

                // Obtener todas las zonas
                var zonasQuery = _context.CatZonas.AsQueryable();

                // Si no es administrador, filtrar por su zona
                if (claveRol != 1 && !string.IsNullOrEmpty(claveZonaUsuario))
                {
                    zonasQuery = zonasQuery.Where(z => z.ClaveZona == claveZonaUsuario);
                }

                var zonas = await zonasQuery.ToListAsync();

                foreach (var zona in zonas)
                {
                    var queryZona = baseQuery.Where(a => a.NumActFijoNavigation != null &&
                                                        a.NumActFijoNavigation.ClaveZona == zona.ClaveZona &&
                                                        a.FechaProgramada >= primerDia &&
                                                        a.FechaProgramada <= ultimoDia);

                    var totalProgramados = await queryZona.CountAsync();
                    var terminados = await queryZona.CountAsync(a => a.Estatus == "TERMINADO");
                    var pendientes = await queryZona.CountAsync(a => a.Estatus == "PENDIENTE" || a.Estatus == "PRE-CANCELADO");
                    var otros = totalProgramados - terminados - pendientes;

                    graficasZonas.Add(new
                    {
                        zona = zona.ClaveZona,
                        nombreZona = zona.NombreZona ?? zona.ClaveZona,
                        totalProgramados,
                        terminados,
                        pendientes,
                        otros = otros > 0 ? otros : 0,
                        tieneDatos = totalProgramados > 0
                    });
                }

                return Json(new { success = true, datosGraficas = graficasZonas });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener datos de gráficas por fecha");
                return Json(new { success = false, message = $"Error interno: {ex.Message}" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> ObtenerDatosGraficasAgenciaPorFecha([FromBody] EnviarCorreoModel model)
        {
            try
            {
                string? claveZonaUsuario = HttpContext.Session.GetString("ClaveZona");

                if (model.FechaInicio == default || model.FechaFin == default)
                {
                    return Json(new { success = false, message = "Las fechas de inicio y fin son requeridas" });
                }

                if (model.FechaInicio > model.FechaFin)
                {
                    return Json(new { success = false, message = "La fecha de inicio no puede ser mayor a la fecha fin" });
                }

                var primerDia = DateOnly.FromDateTime(model.FechaInicio);
                var ultimoDia = DateOnly.FromDateTime(model.FechaFin);

                var estatusExcluidos = new List<string> { "CANCELADO" };

                // Base query - FILTRADO POR ZONA DEL USUARIO
                var baseQuery = _context.Agenda
                    .Include(a => a.NumActFijoNavigation)
                    .Where(a => !estatusExcluidos.Contains(a.Estatus));

                // Aplicar filtro de zona del técnico
                if (!string.IsNullOrEmpty(claveZonaUsuario))
                {
                    baseQuery = baseQuery.Where(a => a.NumActFijoNavigation != null &&
                                                    a.NumActFijoNavigation.ClaveZona == claveZonaUsuario);
                }

                // Cálculo de datos para gráficas por AGENCIA
                var graficasAgencias = new List<object>();

                // Obtener todas las agencias de la zona del usuario
                var agenciasQuery = _context.CatAgencia
                    .Where(a => a.ClaveZona == claveZonaUsuario);

                var agencias = await agenciasQuery.ToListAsync();

                foreach (var agencia in agencias)
                {
                    var queryAgencia = baseQuery.Where(a => a.NumActFijoNavigation != null &&
                                                           a.NumActFijoNavigation.ClaveZona == claveZonaUsuario &&
                                                           a.NumActFijoNavigation.ClaveAgencia == agencia.ClaveAgencia &&
                                                           a.FechaProgramada >= primerDia &&
                                                           a.FechaProgramada <= ultimoDia);

                    var totalProgramados = await queryAgencia.CountAsync();
                    var terminados = await queryAgencia.CountAsync(a => a.Estatus == "TERMINADO");
                    var pendientes = await queryAgencia.CountAsync(a => a.Estatus == "PENDIENTE" || a.Estatus == "PRE-CANCELADO");
                    var otros = totalProgramados - terminados - pendientes;

                    graficasAgencias.Add(new
                    {
                        agencia = agencia.ClaveAgencia,
                        nombreAgencia = agencia.NombreAgencia ?? agencia.ClaveAgencia,
                        totalProgramados,
                        terminados,
                        pendientes,
                        otros = otros > 0 ? otros : 0,
                        tieneDatos = totalProgramados > 0
                    });
                }

                return Json(new { success = true, datosGraficas = graficasAgencias });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener datos de gráficas por agencia y fecha");
                return Json(new { success = false, message = $"Error interno: {ex.Message}" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> ObtenerRegistrosPorEstado([FromBody] FiltroRegistrosModel model)
        {
            try
            {
                _logger.LogInformation($"ObtenerRegistrosPorEstado - Tipo: {model.Tipo}, Clave: {model.Clave}, Estado: {model.Estado}, Zona: {model.ZonaSeleccionada}");

                if (model.FechaInicio == default || model.FechaFin == default)
                {
                    return Json(new { success = false, message = "Las fechas de inicio y fin son requeridas" });
                }

                if (model.FechaInicio > model.FechaFin)
                {
                    return Json(new { success = false, message = "La fecha de inicio no puede ser mayor a la fecha fin" });
                }

                var primerDia = DateOnly.FromDateTime(model.FechaInicio);
                var ultimoDia = DateOnly.FromDateTime(model.FechaFin);

                var estatusExcluidos = new List<string> { "CANCELADO" };

                // Base query SIMPLIFICADA
                var baseQuery = _context.Agenda
                    .Where(a => !estatusExcluidos.Contains(a.Estatus) &&
                               a.FechaProgramada >= primerDia &&
                               a.FechaProgramada <= ultimoDia);

                // Aplicar filtro por tipo de equipo específico para EQUIPOS_AC y EQUIPOS_COMPUTO
                if (model.Tipo == "tiposEquipo")
                {
                    if (model.Clave == "CFEMÁTICOS")
                    {
                        baseQuery = baseQuery.Where(a => _context.EquipoCfematicos.Any(ec => ec.NumActFijo == a.NumActFijo));
                    }
                    else if (model.Clave == "EQUIPOS_AC")
                    {
                        // Filtrar por CFECAM (ClaveTipoEquipo == 1) y CFETURNO (ClaveTipoEquipo == 2)
                        baseQuery = baseQuery.Where(a => _context.EquipoAcs.Any(ea => ea.NumActFijo == a.NumActFijo));
                    }
                    else if (model.Clave == "EQUIPOS_COMPUTO")
                    {
                        // Filtrar por PC (ClaveTipoEquipo == 3) y LAPTOP (ClaveTipoEquipo == 4)
                        baseQuery = baseQuery.Where(a => _context.EquipoComputos.Any(ec => ec.NumActFijo == a.NumActFijo));
                    }
                }

                // Aplicar filtro por zona o agencia según el tipo
                if (model.Tipo == "zonas")
                {
                    if (model.ZonaSeleccionada != "TODAS")
                    {
                        baseQuery = baseQuery.Where(a => a.NumActFijoNavigation != null &&
                                                        a.NumActFijoNavigation.ClaveZona == model.Clave);
                    }
                }
                else if (model.Tipo == "agencias")
                {
                    baseQuery = baseQuery.Where(a => a.NumActFijoNavigation != null &&
                                                    a.NumActFijoNavigation.ClaveAgencia == model.Clave);
                }
                else if (model.Tipo == "tiposEquipo")
                {
                    // Solo aplicar filtro de zona si no es "TODAS"
                    if (model.ZonaSeleccionada != "TODAS")
                    {
                        baseQuery = baseQuery.Where(a => a.NumActFijoNavigation != null &&
                                                        a.NumActFijoNavigation.ClaveZona == model.ZonaSeleccionada);
                    }
                }

                // Aplicar filtro por estado
                switch (model.Estado.ToUpper())
                {
                    case "TERMINADOS":
                        baseQuery = baseQuery.Where(a => a.Estatus == "TERMINADO");
                        break;
                    case "PENDIENTES":
                        baseQuery = baseQuery.Where(a => a.Estatus == "PENDIENTE" || a.Estatus == "PRE-CANCELADO");
                        break;
                    case "OTROS":
                        baseQuery = baseQuery.Where(a => a.Estatus != "TERMINADO" &&
                                                       a.Estatus != "PENDIENTE" &&
                                                       a.Estatus != "PRE-CANCELADO" &&
                                                       a.Estatus != "CANCELADO");
                        break;
                }

                // Obtener registros con información adicional
                var registros = await baseQuery
                    .Select(a => new
                    {
                        NumActFijo = a.NumActFijo,
                        Agencia = a.NumActFijoNavigation != null ? a.NumActFijoNavigation.ClaveAgencia : "N/A",
                        Centro = a.NumActFijoNavigation != null ? a.NumActFijoNavigation.ClaveCentro : "N/A",
                        NombreAgencia = a.NumActFijoNavigation != null &&
                                       a.NumActFijoNavigation.CatCentro != null &&
                                       a.NumActFijoNavigation.CatCentro.CatAgencium != null
                                        ? a.NumActFijoNavigation.CatCentro.CatAgencium.NombreAgencia
                                        : "N/A",
                        NombreCentro = a.NumActFijoNavigation != null &&
                                      a.NumActFijoNavigation.CatCentro != null
                                        ? a.NumActFijoNavigation.CatCentro.NombreCentro
                                        : "N/A",
                        FechaProgramada = a.FechaProgramada.ToString("dd/MM/yyyy"),
                        Estatus = a.Estatus,
                        // Determinar tipo de equipo con más detalle
                        TipoEquipo = _context.EquipoComputos.Any(ec => ec.NumActFijo == a.NumActFijo && ec.ClaveTipoEquipo == 3) ? "PC" :
                                    _context.EquipoComputos.Any(ec => ec.NumActFijo == a.NumActFijo && ec.ClaveTipoEquipo == 4) ? "LAPTOP" :
                                    _context.EquipoAcs.Any(ea => ea.NumActFijo == a.NumActFijo && ea.ClaveTipoEquipo == 1) ? "CFECAM" :
                                    _context.EquipoAcs.Any(ea => ea.NumActFijo == a.NumActFijo && ea.ClaveTipoEquipo == 2) ? "CFETURNO" :
                                    _context.EquipoCfematicos.Any(ec => ec.NumActFijo == a.NumActFijo) ? "CFEMÁTICOS" : "DESCONOCIDO",
                        // Obtener número de cajero para CFEMÁTICOS
                        NumCajero = _context.EquipoCfematicos
                            .Where(ec => ec.NumActFijo == a.NumActFijo)
                            .Select(ec => ec.NumCajero)
                            .FirstOrDefault(),
                        // Obtener fecha de atención del mantenimiento
                        FechaAtencion = _context.Mantenimientos
                            .Where(m => m.ClaveAgenda == a.ClaveAgenda)
                            .OrderByDescending(m => m.FechaAtencion)
                            .Select(m => m.FechaAtencion != default ? m.FechaAtencion.ToString("dd/MM/yyyy") : null)
                            .FirstOrDefault(),
                        // Información adicional para debug
                        TieneEquipoAC = _context.EquipoAcs.Any(ea => ea.NumActFijo == a.NumActFijo),
                        TieneEquipoComputo = _context.EquipoComputos.Any(ec => ec.NumActFijo == a.NumActFijo),
                        TieneEquipoCfematico = _context.EquipoCfematicos.Any(ec => ec.NumActFijo == a.NumActFijo)
                    })
                    .OrderBy(a => a.NombreAgencia)
                    .ThenBy(a => a.NombreCentro)
                    .ThenBy(a => a.NumActFijo)
                    .ToListAsync();

                // Log para debug
                _logger.LogInformation($"Registros encontrados: {registros.Count}");
                _logger.LogInformation($"Desglose por tipo: " +
                                      $"CFEMÁTICOS: {registros.Count(r => r.TipoEquipo == "CFEMÁTICOS")}, " +
                                      $"EQUIPOS_AC: {registros.Count(r => r.TipoEquipo == "CFECAM" || r.TipoEquipo == "CFETURNO")}, " +
                                      $"EQUIPOS_COMPUTO: {registros.Count(r => r.TipoEquipo == "PC" || r.TipoEquipo == "LAPTOP")}");

                // Formatear los datos finales
                var registrosFormateados = registros.Select(r => new
                {
                    numActFijo = r.TipoEquipo == "CFEMÁTICOS" && !string.IsNullOrEmpty(r.NumCajero)
                        ? $"{r.NumActFijo} (Cajero: {r.NumCajero})"
                        : r.NumActFijo.ToString(),
                    agencia = r.Agencia,
                    centro = r.Centro,
                    nombreAgencia = r.NombreAgencia,
                    nombreCentro = r.NombreCentro,
                    fechaProgramada = r.FechaProgramada,
                    fechaTerminada = !string.IsNullOrEmpty(r.FechaAtencion) ? r.FechaAtencion : "N/A",
                    estatus = r.Estatus,
                    tipoEquipo = r.TipoEquipo
                }).ToList();

                _logger.LogInformation($"Se encontraron {registrosFormateados.Count} registros formateados");

                return Json(new { success = true, registros = registrosFormateados });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener registros por estado");
                return Json(new { success = false, message = $"Error interno: {ex.Message}" });
            }
        }
    }

    public class FiltroRegistrosModel
    {
        public string Tipo { get; set; }
        public string Clave { get; set; }
        public string Estado { get; set; }
        public DateTime FechaInicio { get; set; }
        public DateTime FechaFin { get; set; }
        public string ZonaSeleccionada { get; set; }
        public string TipoEquipo { get; set; }
    }

    public class DashboardViewModel
    {
        public int PendientesMesAnteriorCount { get; set; }
        public int ProgramadosCount { get; set; }
        public int TerminadosCount { get; set; }
        public int PendientesCount { get; set; }
        public int ProgramadosProximoMesCount { get; set; }
        public List<ZonaChartData> GraficasZonas { get; set; } = new List<ZonaChartData>();
        public List<CatZona> Zonas { get; set; } = new List<CatZona>();

        public List<string> TiposEquipo { get; set; } = new List<string>
    {
        "TODOS", "PC", "LAPTOP", "CFECAM", "CFETURNO", "CFEMÁTICO"
    };
    }

    public class FiltroCompletoModel
    {
        public DateTime FechaInicio { get; set; }
        public DateTime FechaFin { get; set; }
        public string ZonaSeleccionada { get; set; }
        public string TipoEquipo { get; set; }
    }

    public class ZonaChartData
    {
        public string Zona { get; set; }
        public string NombreZona { get; set; }
        public int TotalProgramados { get; set; }
        public int Terminados { get; set; }
        public int Pendientes { get; set; }
        public int Otros { get; set; }
        public bool TieneDatos { get; set; }
    }

    public class EnviarCorreoModel
    {
        public DateTime FechaInicio { get; set; }
        public DateTime FechaFin { get; set; }
        public List<string> Usuarios { get; set; } = new List<string>();
    }

    public class ProgramarEnvioModel
    {
        public string DiaSemana { get; set; }
        public List<string> HorasProgramadas { get; set; } = new List<string>();
    }

    public class DashboardTecnicoViewModel
    {
        public int PendientesMesAnteriorCount { get; set; }
        public int ProgramadosCount { get; set; }
        public int TerminadosCount { get; set; }
        public int PendientesCount { get; set; }
        public int ProgramadosProximoMesCount { get; set; }
        public List<AgenciaChartData> GraficasAgencias { get; set; } = new List<AgenciaChartData>();
        public List<CatAgencium> Agencias { get; set; } = new List<CatAgencium>();
        public List<CatCentro> Centros { get; set; } = new List<CatCentro>();
    }

    public class AgenciaChartData
    {
        public string Agencia { get; set; }
        public string NombreAgencia { get; set; }
        public int TotalProgramados { get; set; }
        public int Terminados { get; set; }
        public int Pendientes { get; set; }
        public int Otros { get; set; }
        public bool TieneDatos { get; set; }
    }

    public class CentroChartData
    {
        public string Centro { get; set; }
        public string NombreCentro { get; set; }
        public int TotalProgramados { get; set; }
        public int Terminados { get; set; }
        public int Pendientes { get; set; }
        public int Otros { get; set; }
        public bool TieneDatos { get; set; }
    }
}