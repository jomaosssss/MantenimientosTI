using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

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

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var scheduleConfig = _configuration.GetSection("ReportSchedule");
            var enabled = scheduleConfig.GetValue<bool>("Enabled");
            var dayOfWeek = scheduleConfig.GetValue<string>("DayOfWeek") ?? "Friday";
            var hour = scheduleConfig.GetValue<int>("Hour");
            var minute = scheduleConfig.GetValue<int>("Minute");

            if (!enabled)
            {
                _logger.LogInformation("Servicio de correo programado DESHABILITADO.");
                return;
            }

            _logger.LogInformation($"Servicio de correo programado iniciado. Configuración: {dayOfWeek} a las {hour:00}:{minute:00}");

            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var now = DateTime.Now;
                    var nextRun = CalculateNextRun(now, dayOfWeek, hour, minute);

                    if (now > nextRun)
                    {
                        nextRun = nextRun.AddDays(7);
                    }

                    var delay = nextRun - now;

                    _logger.LogInformation($"Próximo envío programado para: {nextRun:dd/MM/yyyy HH:mm:ss}");
                    _logger.LogInformation($"Tiempo de espera: {delay.TotalHours:F2} horas");

                    await Task.Delay(delay, stoppingToken);

                    if (!stoppingToken.IsCancellationRequested)
                    {
                        _logger.LogInformation("Ejecutando envío programado de reporte...");
                        await EnviarReporteProgramado();
                        _logger.LogInformation("Envío programado completado.");
                    }
                }
                catch (TaskCanceledException)
                {
                    _logger.LogInformation("Servicio de correo programado cancelado.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error en el servicio de correo programado.");
                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                }
            }
        }

        private DateTime CalculateNextRun(DateTime now, string dayOfWeek, int hour, int minute)
        {
            DayOfWeek targetDay = dayOfWeek.ToLower() switch
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

            var nextRun = now.Date.AddDays(((int)targetDay - (int)now.DayOfWeek + 7) % 7)
                                .AddHours(hour)
                                .AddMinutes(minute);

            return nextRun;
        }

        private async Task EnviarReporteProgramado()
        {
            using var scope = _serviceProvider.CreateScope();
            try
            {
                var reporteService = scope.ServiceProvider.GetRequiredService<ReporteService>();
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<ScheduledEmailService>>();

                // Calcular el mes anterior
                var ahora = DateTime.Now;
                var primerDiaMesAnterior = new DateTime(ahora.Year, ahora.Month, 1).AddMonths(-1);
                var ultimoDiaMesAnterior = new DateTime(ahora.Year, ahora.Month, 1).AddDays(-1);

                var resultado = await reporteService.EnviarCorreoConExcel(primerDiaMesAnterior, ultimoDiaMesAnterior);

                if (resultado)
                {
                    logger.LogInformation($"Reporte programado del {primerDiaMesAnterior:dd/MM/yyyy} al {ultimoDiaMesAnterior:dd/MM/yyyy} enviado exitosamente.");
                }
                else
                {
                    logger.LogError("Error al enviar el reporte programado.");
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