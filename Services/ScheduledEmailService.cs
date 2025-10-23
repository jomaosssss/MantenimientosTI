using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using MantenimientosTI.Models;

namespace MantenimientosTI.Services
{
    public class ScheduledEmailService : BackgroundService
    {
        private readonly ILogger<ScheduledEmailService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IConfiguration _configuration;

        public ScheduledEmailService(
            ILogger<ScheduledEmailService> logger,
            IServiceProvider serviceProvider,
            IConfiguration configuration)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _configuration = configuration;
        }

        // Reemplaza TODO el método ExecuteAsync en ScheduledEmailService.cs
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Servicio de correo programado iniciado.");

            // Espera inicial para que la aplicación se inicie completamente
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var context = scope.ServiceProvider.GetRequiredService<MantenimientosTIContext>();

                    // 1. Obtener configuración de la BD
                    var configDiaDb = await context.Configuraciones
                        .FirstOrDefaultAsync(c => c.ClaveConfiguracion == "PROGRAMACION_REPORTE_DIA", stoppingToken);

                    var configHoraDb = await context.Configuraciones
                        .FirstOrDefaultAsync(c => c.ClaveConfiguracion == "PROGRAMACION_REPORTE_HORA", stoppingToken);

                    string diaProgramadoStr;
                    TimeOnly horaProgramada;

                    if (configDiaDb != null && configHoraDb != null && TimeOnly.TryParse(configHoraDb.Valor, out horaProgramada))
                    {
                        // Usar configuración de la Base de Datos
                        diaProgramadoStr = configDiaDb.Valor;
                        _logger.LogInformation($"Usando programación de BD: {diaProgramadoStr} a las {horaProgramada}");
                    }
                    else
                    {
                        // Usar configuración de appsettings.json como respaldo
                        var scheduleConfig = _configuration.GetSection("ReportSchedule");
                        if (!scheduleConfig.GetValue<bool>("Enabled"))
                        {
                            _logger.LogInformation("Servicio de correo programado deshabilitado en appsettings.");
                            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); // Revisar menos seguido
                            continue;
                        }

                        diaProgramadoStr = scheduleConfig.GetValue<string>("DayOfWeek") ?? "Friday";
                        horaProgramada = new TimeOnly(
                            scheduleConfig.GetValue<int>("Hour"),
                            scheduleConfig.GetValue<int>("Minute")
                        );
                        _logger.LogInformation($"Usando programación de appsettings: {diaProgramadoStr} a las {horaProgramada}");
                    }

                    // 2. Convertir string a DayOfWeek (Enum)
                    if (!Enum.TryParse<DayOfWeek>(diaProgramadoStr, true, out var diaProgramado))
                    {
                        _logger.LogWarning($"Valor de día inválido: {diaProgramadoStr}. Usando 'Friday' por defecto.");
                        diaProgramado = DayOfWeek.Friday;
                    }

                    // 3. Verificar si es hora de ejecutar
                    var ahoraLocal = DateTime.Now; // Usamos hora Local del servidor

                    bool esDia = ahoraLocal.DayOfWeek == diaProgramado;
                    bool esHora = ahoraLocal.Hour == horaProgramada.Hour && ahoraLocal.Minute == horaProgramada.Minute;

                    if (esDia && esHora)
                    {
                        _logger.LogInformation("Día y hora programada coinciden. Verificando última ejecución...");

                        // 4. Verificar que no se haya ejecutado HOY
                        var configUltimaEjecucion = await context.Configuraciones
                            .FirstOrDefaultAsync(c => c.ClaveConfiguracion == "PROGRAMACION_REPORTE_ULTIMA_EJECUCION", stoppingToken);

                        DateTime? ultimaEjecucion = null;
                        if (configUltimaEjecucion != null && DateTime.TryParse(configUltimaEjecucion.Valor, out var fechaUltima))
                        {
                            ultimaEjecucion = fechaUltima;
                        }

                        // Si no hay registro o si el registro es de un día anterior
                        if (!ultimaEjecucion.HasValue || ultimaEjecucion.Value.Date < ahoraLocal.Date)
                        {
                            _logger.LogInformation("Ejecutando envío programado de reporte...");
                            await EnviarReporteProgramado();

                            // 5. Guardar la hora de esta ejecución
                            if (configUltimaEjecucion == null)
                            {
                                context.Configuraciones.Add(new Configuracion
                                {
                                    ClaveConfiguracion = "PROGRAMACION_REPORTE_ULTIMA_EJECUCION",
                                    Valor = ahoraLocal.ToString("O"), // Formato ISO
                                    Descripcion = "Última fecha de ejecución del reporte programado"
                                });
                            }
                            else
                            {
                                configUltimaEjecucion.Valor = ahoraLocal.ToString("O");
                            }
                            await context.SaveChangesAsync(stoppingToken);

                            _logger.LogInformation("Envío programado completado y fecha de ejecución actualizada.");
                        }
                        else
                        {
                            _logger.LogInformation("El reporte programado ya se ejecutó hoy.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error en el servicio de correo programado.");
                }

                // 6. Esperar 1 minuto antes de verificar nuevamente
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }

        private async Task VerificarProgramacionAppSettings(IServiceScope scope, DateTime ahoraUtc)
        {
            var scheduleConfig = _configuration.GetSection("ReportSchedule");
            var dayOfWeek = scheduleConfig.GetValue<string>("DayOfWeek") ?? "Friday";
            var hour = scheduleConfig.GetValue<int>("Hour");
            var minute = scheduleConfig.GetValue<int>("Minute");

            var ahoraLocal = DateTime.Now;
            var targetDay = dayOfWeek.ToLower() switch
            {
                "monday" => DayOfWeek.Monday,
                "tuesday" => DayOfWeek.Tuesday,
                "wednesday" => DayOfWeek.Wednesday,
                "thursday" => DayOfWeek.Thursday,
                "friday" => DayOfWeek.Friday,
                "saturday" => DayOfWeek.Saturday,
                "sunday" => DayOfWeek.Sunday,
                _ => DayOfWeek.Friday
            };

            // Verificar si hoy es el día programado y estamos en la hora programada
            if (ahoraLocal.DayOfWeek == targetDay &&
                ahoraLocal.Hour == hour &&
                ahoraLocal.Minute == minute)
            {
                _logger.LogInformation("Ejecutando envío programado desde appsettings...");
                await EnviarReporteProgramado();
            }
        }

        private async Task EnviarReporteProgramado()
        {
            using var scope = _serviceProvider.CreateScope();
            try
            {
                var reporteService = scope.ServiceProvider.GetRequiredService<ReporteService>();
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<ScheduledEmailService>>();

                var ahora = DateTime.Now;
                var primerDiaMesActual = new DateTime(ahora.Year, ahora.Month, 1);
                var ultimoDiaMesActual = primerDiaMesActual.AddMonths(1).AddDays(-1);

                _logger.LogInformation($"Enviando reporte programado del MES ACTUAL: {primerDiaMesActual:dd/MM/yyyy} - {ultimoDiaMesActual:dd/MM/yyyy}");

                var resultado = await reporteService.EnviarCorreoConExcel(primerDiaMesActual, ultimoDiaMesActual);

                if (resultado)
                {
                    logger.LogInformation($"Reporte programado del MES ACTUAL {primerDiaMesActual:dd/MM/yyyy} al {ultimoDiaMesActual:dd/MM/yyyy} enviado exitosamente.");
                }
                else
                {
                    logger.LogError("Error al enviar el reporte programado del mes actual.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error crítico al enviar reporte programado.");
            }
        }

        public override async Task StopAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Servicio de correo programado está deteniéndose...");
            await base.StopAsync(stoppingToken);
        }
    }
}