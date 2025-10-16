using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using MantenimientosTI.Models;
using Microsoft.EntityFrameworkCore;
using System.Net.Mail;
using System.Net;
using ClosedXML.Excel;
using System.Text;

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
            // Leer configuración
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
                var context = scope.ServiceProvider.GetRequiredService<MantenimientosTIContext>();
                var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<ScheduledEmailService>>();

                var resultado = await EnviarCorreoConExcelProgramado(context, configuration, logger);

                if (resultado)
                {
                    _logger.LogInformation("Reporte programado enviado exitosamente.");
                }
                else
                {
                    _logger.LogError("Error al enviar el reporte programado.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error crítico al enviar reporte programado.");
            }
        }

        private async Task<bool> EnviarCorreoConExcelProgramado(MantenimientosTIContext context, IConfiguration configuration, ILogger<ScheduledEmailService> logger)
        {
            try
            {
                var smtpServer = configuration["EmailSettings:SmtpServer"];
                var port = int.Parse(configuration["EmailSettings:Port"]);
                var username = configuration["EmailSettings:Username"];
                var password = configuration["EmailSettings:Password"];
                var fromAddress = configuration["EmailSettings:FromAddress"];

                // Obtener correos de usuarios con ClaveRol = 1 Y RecibirReporte = "SI"
                var correosDestinatarios = await context.Usuarios
                    .Where(u => u.ClaveRol == 1 &&
                               u.RecibirReporte == "SI" &&
                               u.Estatus.ToLower() == "activo")
                    .Select(u => u.Correo)
                    .ToListAsync();

                // Verificar que haya destinatarios
                if (!correosDestinatarios.Any())
                {
                    logger.LogWarning("No se encontraron usuarios con ClaveRol=1 y RecibirReporte=SI para enviar el reporte programado");
                    return false;
                }

                var excelBytes = await GenerarExcelReporteProgramado(context);
                var mesActual = DateTime.Now.ToString("MMMM yyyy");

                var reporteCFE = await ObtenerReporteCFEProgramado(context);
                var reporteAC = await ObtenerReporteACProgramado(context);
                var reporteComputo = await ObtenerReporteComputoProgramado(context);

                var cuerpoHTML = GenerarCuerpoCorreoHTMLProgramado(mesActual, reporteCFE, reporteAC, reporteComputo);

                using var client = new SmtpClient(smtpServer, port);

                // VERIFICAR SI HAY CONTRASEÑA O NO - MODIFICACIÓN PARA CFE
                if (!string.IsNullOrEmpty(password))
                {
                    client.Credentials = new NetworkCredential(username, password);
                }
                else
                {
                    logger.LogInformation("Intentando envío sin contraseña (autenticación por red CFE)");
                    // No establecer credenciales - el servidor puede autenticar por IP
                }

                client.EnableSsl = false; // El puerto 25 normalmente no usa SSL
                client.Timeout = 30000;

                using var message = new MailMessage
                {
                    From = new MailAddress(fromAddress),
                    Subject = $"Reporte de Mantenimientos - {mesActual}",
                    Body = cuerpoHTML,
                    IsBodyHtml = true
                };

                // Agrega todos los correos de usuarios con ClaveRol = 1 Y RecibirReporte = "SI"
                foreach (var correo in correosDestinatarios)
                {
                    if (!string.IsNullOrWhiteSpace(correo))
                    {
                        message.Bcc.Add(correo);
                    }
                }

                if (message.Bcc.Count == 0)
                {
                    logger.LogWarning("No hay correos válidos para usuarios con ClaveRol=1 y RecibirReporte=SI en el reporte programado");
                    return false;
                }

                using var stream = new MemoryStream(excelBytes);
                var attachment = new Attachment(stream,
                    $"Reporte_Mantenimientos_{mesActual}.xlsx",
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

                message.Attachments.Add(attachment);

                await client.SendMailAsync(message);

                logger.LogInformation($"Reporte programado enviado exitosamente a {message.Bcc.Count} destinatarios con ClaveRol=1 y RecibirReporte=SI");
                return true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error enviando correo programado: {Message}", ex.Message);
                return false;
            }
        }

        // ========== MÉTODOS RESTANTES SIN CAMBIOS ==========

        private string GenerarCuerpoCorreoHTMLProgramado(string mesActual,
            List<ReporteResumen> reporteCFE, List<ReporteResumen> reporteAC, List<ReporteResumen> reporteComputo)
        {
            var sb = new StringBuilder();

            sb.AppendLine($@"
            <html>
            <head>
                <style>
                    body {{ font-family: Arial, sans-serif; }}
                    .tabla-reporte {{ 
                        border-collapse: collapse; 
                        width: 100%; 
                        margin-bottom: 20px;
                        border: 1px solid #ddd;
                    }}
                    .tabla-reporte th, .tabla-reporte td {{
                        border: 1px solid #ddd;
                        padding: 8px;
                        text-align: center;
                    }}
                    .tabla-reporte th {{
                        background-color: #f2f2f2;
                        font-weight: bold;
                    }}
                    .tabla-reporte tr:nth-child(even) {{ background-color: #f9f9f9; }}
                    .total-row {{ font-weight: bold; background-color: #e6f3ff !important; }}
                    .titulo-tabla {{ 
                        background-color: #2c3e50; 
                        color: white; 
                        padding: 10px;
                        text-align: center;
                        font-size: 16px;
                    }}
                    .container {{ max-width: 900px; margin: 0 auto; }}
                    .info-box {{ 
                        background-color: #f8f9fa; 
                        border-left: 4px solid #007bff;
                        padding: 10px;
                        margin-bottom: 20px;
                    }}
                </style>
            </head>
            <body>
                <div class='container'>
                    <h2>Reporte de Mantenimientos - ARGOS</h2>
                    <div class='info-box'>
                        <p><strong>Mes:</strong> {mesActual}</p>
                    </div>
                    
                    <h3>AVANCE DE MANTENIMIENTOS DE CFEMÁTICOS - {mesActual.ToUpper()}</h3>");

            sb.AppendLine(GenerarTablaHTMLProgramado(reporteCFE, "CFEmáticos"));
            sb.AppendLine($@"<br><h3>AVANCE DE MANTENIMIENTOS DE EQUIPOS DE ATENCIÓN A CLIENTES - {mesActual.ToUpper()}</h3>");
            sb.AppendLine(GenerarTablaHTMLProgramado(reporteAC, "Equipos AC"));
            sb.AppendLine($@"<br><h3>AVANCE DE MANTENIMIENTOS DE EQUIPOS DE CÓMPUTO - {mesActual.ToUpper()}</h3>");
            sb.AppendLine(GenerarTablaHTMLProgramado(reporteComputo, "Equipos Computo"));

            sb.AppendLine(@"
                    <p><em>Se adjunta el archivo Excel con el detalle completo de los mantenimientos de todas la zonas.</em></p>
                    <p>Saludos.</p>
                    <div class='info-box'>
                        <p>Este correo se envía en automático, favor de no responderlo.</p>
                    </div>
                </div>
            </body>
            </html>");

            return sb.ToString();
        }

        private string GenerarTablaHTMLProgramado(List<ReporteResumen> datos, string tipoEquipo)
        {
            var sb = new StringBuilder();

            sb.AppendLine($@"<table class='tabla-reporte'>
                <tr>
                    <th>ZONA</th>
                    <th>PROGRAMADOS</th>
                    <th>TERMINADOS</th>
                    <th>PENDIENTES</th>
                    <th>AVANCE %</th>
                </tr>");

            foreach (var item in datos)
            {
                var claseFila = item.Zona == "TOTAL" ? "total-row" : "";

                sb.AppendLine($@"
                <tr class='{claseFila}'>
                    <td>{item.Zona}</td>
                    <td>{item.Programados}</td>
                    <td>{item.Terminados}</td>
                    <td>{item.Pendientes}</td>
                    <td>{item.Avance:F2}%</td>
                </tr>");
            }

            sb.AppendLine("</table>");
            return sb.ToString();
        }

        private async Task<List<ReporteResumen>> ObtenerReporteCFEProgramado(MantenimientosTIContext context)
        {
            var mesActual = DateTime.Now.Month;
            var añoActual = DateTime.Now.Year;

            var todasLasZonas = await context.CatZonas
                .OrderBy(z => z.ClaveZona)
                .Select(z => new { z.ClaveZona, z.NombreZona })
                .ToListAsync();

            var resultados = new List<ReporteResumen>();

            foreach (var zona in todasLasZonas)
            {
                var datosZona = await (from z in context.CatZonas
                                       join agen in context.CatAgencia on
                                           new { z.ClaveDivision, z.ClaveZona } equals
                                           new { agen.ClaveDivision, agen.ClaveZona }
                                       join c in context.CatCentros on
                                           new { agen.ClaveDivision, agen.ClaveZona, agen.ClaveAgencia } equals
                                           new { c.ClaveDivision, c.ClaveZona, c.ClaveAgencia }
                                       join eq in context.Equipos on
                                           new { c.ClaveDivision, c.ClaveZona, c.ClaveAgencia, c.ClaveCentro } equals
                                           new { eq.ClaveDivision, eq.ClaveZona, eq.ClaveAgencia, eq.ClaveCentro }
                                       join ecfe in context.EquipoCfematicos on eq.NumActFijo equals ecfe.NumActFijo
                                       join ag in context.Agenda on eq.NumActFijo equals ag.NumActFijo
                                       where z.ClaveZona == zona.ClaveZona &&
                                             ag.FechaProgramada.Month == mesActual &&
                                             ag.FechaProgramada.Year == añoActual
                                       select new { ag })
                                     .ToListAsync();

                var programados = datosZona.Count(x => x.ag != null);
                var terminados = datosZona.Count(x => x.ag != null && x.ag.Estatus == "TERMINADO");
                var pendientes = datosZona.Count(x => x.ag != null && x.ag.Estatus == "PENDIENTE");
                var avance = programados == 0 ? 0 : (terminados * 100.0m) / programados;

                resultados.Add(new ReporteResumen
                {
                    Zona = zona.NombreZona,
                    Programados = programados,
                    Terminados = terminados,
                    Pendientes = pendientes,
                    Avance = avance
                });
            }

            var total = new ReporteResumen
            {
                Zona = "TOTAL",
                Programados = resultados.Sum(x => x.Programados),
                Terminados = resultados.Sum(x => x.Terminados),
                Pendientes = resultados.Sum(x => x.Pendientes),
                Avance = resultados.Sum(x => x.Programados) == 0 ? 0 :
                    (resultados.Sum(x => x.Terminados) * 100.0m) / resultados.Sum(x => x.Programados)
            };

            resultados.Add(total);
            return resultados;
        }

        private async Task<List<ReporteResumen>> ObtenerReporteACProgramado(MantenimientosTIContext context)
        {
            var mesActual = DateTime.Now.Month;
            var añoActual = DateTime.Now.Year;

            var todasLasZonas = await context.CatZonas
                .OrderBy(z => z.ClaveZona)
                .Select(z => new { z.ClaveZona, z.NombreZona })
                .ToListAsync();

            var resultados = new List<ReporteResumen>();

            foreach (var zona in todasLasZonas)
            {
                var datosZona = await (from z in context.CatZonas
                                       join agen in context.CatAgencia on
                                           new { z.ClaveDivision, z.ClaveZona } equals
                                           new { agen.ClaveDivision, agen.ClaveZona }
                                       join c in context.CatCentros on
                                           new { agen.ClaveDivision, agen.ClaveZona, agen.ClaveAgencia } equals
                                           new { c.ClaveDivision, c.ClaveZona, c.ClaveAgencia }
                                       join eq in context.Equipos on
                                           new { c.ClaveDivision, c.ClaveZona, c.ClaveAgencia, c.ClaveCentro } equals
                                           new { eq.ClaveDivision, eq.ClaveZona, eq.ClaveAgencia, eq.ClaveCentro }
                                       join eac in context.EquipoAcs on eq.NumActFijo equals eac.NumActFijo
                                       join ag in context.Agenda on eq.NumActFijo equals ag.NumActFijo
                                       where z.ClaveZona == zona.ClaveZona &&
                                             ag.FechaProgramada.Month == mesActual &&
                                             ag.FechaProgramada.Year == añoActual
                                       select new { ag })
                                     .ToListAsync();

                var programados = datosZona.Count(x => x.ag != null);
                var terminados = datosZona.Count(x => x.ag != null && x.ag.Estatus == "TERMINADO");
                var pendientes = datosZona.Count(x => x.ag != null && x.ag.Estatus == "PENDIENTE");
                var avance = programados == 0 ? 0 : (terminados * 100.0m) / programados;

                resultados.Add(new ReporteResumen
                {
                    Zona = zona.NombreZona,
                    Programados = programados,
                    Terminados = terminados,
                    Pendientes = pendientes,
                    Avance = avance
                });
            }

            var total = new ReporteResumen
            {
                Zona = "TOTAL",
                Programados = resultados.Sum(x => x.Programados),
                Terminados = resultados.Sum(x => x.Terminados),
                Pendientes = resultados.Sum(x => x.Pendientes),
                Avance = resultados.Sum(x => x.Programados) == 0 ? 0 :
                    (resultados.Sum(x => x.Terminados) * 100.0m) / resultados.Sum(x => x.Programados)
            };

            resultados.Add(total);
            return resultados;
        }

        private async Task<List<ReporteResumen>> ObtenerReporteComputoProgramado(MantenimientosTIContext context)
        {
            var mesActual = DateTime.Now.Month;
            var añoActual = DateTime.Now.Year;

            var todasLasZonas = await context.CatZonas
                .OrderBy(z => z.ClaveZona)
                .Select(z => new { z.ClaveZona, z.NombreZona })
                .ToListAsync();

            var resultados = new List<ReporteResumen>();

            foreach (var zona in todasLasZonas)
            {
                var datosZona = await (from z in context.CatZonas
                                       join agen in context.CatAgencia on
                                           new { z.ClaveDivision, z.ClaveZona } equals
                                           new { agen.ClaveDivision, agen.ClaveZona }
                                       join c in context.CatCentros on
                                           new { agen.ClaveDivision, agen.ClaveZona, agen.ClaveAgencia } equals
                                           new { c.ClaveDivision, c.ClaveZona, c.ClaveAgencia }
                                       join eq in context.Equipos on
                                           new { c.ClaveDivision, c.ClaveZona, c.ClaveAgencia, c.ClaveCentro } equals
                                           new { eq.ClaveDivision, eq.ClaveZona, eq.ClaveAgencia, eq.ClaveCentro }
                                       join ec in context.EquipoComputos on eq.NumActFijo equals ec.NumActFijo
                                       join ag in context.Agenda on eq.NumActFijo equals ag.NumActFijo
                                       where z.ClaveZona == zona.ClaveZona &&
                                             ag.FechaProgramada.Month == mesActual &&
                                             ag.FechaProgramada.Year == añoActual
                                       select new { ag })
                                     .ToListAsync();

                var programados = datosZona.Count(x => x.ag != null);
                var terminados = datosZona.Count(x => x.ag != null && x.ag.Estatus == "TERMINADO");
                var pendientes = datosZona.Count(x => x.ag != null && x.ag.Estatus == "PENDIENTE");
                var avance = programados == 0 ? 0 : (terminados * 100.0m) / programados;

                resultados.Add(new ReporteResumen
                {
                    Zona = zona.NombreZona,
                    Programados = programados,
                    Terminados = terminados,
                    Pendientes = pendientes,
                    Avance = avance
                });
            }

            var total = new ReporteResumen
            {
                Zona = "TOTAL",
                Programados = resultados.Sum(x => x.Programados),
                Terminados = resultados.Sum(x => x.Terminados),
                Pendientes = resultados.Sum(x => x.Pendientes),
                Avance = resultados.Sum(x => x.Programados) == 0 ? 0 :
                    (resultados.Sum(x => x.Terminados) * 100.0m) / resultados.Sum(x => x.Programados)
            };

            resultados.Add(total);
            return resultados;
        }

        private async Task<byte[]> GenerarExcelReporteProgramado(MantenimientosTIContext context)
        {
            using var workbook = new XLWorkbook();

            var wsCFE = workbook.Worksheets.Add("CFEMÁTICOS");
            await GenerarHojaCFEProgramado(wsCFE, context);

            var wsAC = workbook.Worksheets.Add("EQUIPOS DE ATENCIÓN A CLIENTES");
            await GenerarHojaACProgramado(wsAC, context);

            var wsComputo = workbook.Worksheets.Add("EQUIPOS DE CÓMPUTO");
            await GenerarHojaComputoProgramado(wsComputo, context);

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        private async Task GenerarHojaCFEProgramado(IXLWorksheet worksheet, MantenimientosTIContext context)
        {
            var mesActual = DateTime.Now.Month;
            var añoActual = DateTime.Now.Year;

            var datos = await (from ag in context.Agenda
                               join eq in context.Equipos on ag.NumActFijo equals eq.NumActFijo
                               join ecfe in context.EquipoCfematicos on ag.NumActFijo equals ecfe.NumActFijo
                               join c in context.CatCentros on new
                               {
                                   eq.ClaveDivision,
                                   eq.ClaveZona,
                                   eq.ClaveAgencia,
                                   eq.ClaveCentro
                               } equals new
                               {
                                   c.ClaveDivision,
                                   c.ClaveZona,
                                   c.ClaveAgencia,
                                   c.ClaveCentro
                               }
                               join agen in context.CatAgencia on new
                               {
                                   c.ClaveDivision,
                                   c.ClaveZona,
                                   c.ClaveAgencia
                               } equals new
                               {
                                   agen.ClaveDivision,
                                   agen.ClaveZona,
                                   agen.ClaveAgencia
                               }
                               join z in context.CatZonas on new
                               {
                                   agen.ClaveDivision,
                                   agen.ClaveZona
                               } equals new
                               {
                                   z.ClaveDivision,
                                   z.ClaveZona
                               }
                               join m in context.Mantenimientos on
                                   new { ag.ClaveAgenda, ag.NumActFijo }
                                   equals new { m.ClaveAgenda, m.NumActFijo } into mantenimientos
                               from m in mantenimientos.DefaultIfEmpty()
                               where ag.FechaProgramada.Month == mesActual
                                   && ag.FechaProgramada.Year == añoActual
                               orderby z.ClaveZona, ag.FechaProgramada
                               select new
                               {
                                   Zona = z.NombreZona,
                                   Agencia = agen.NombreAgencia,
                                   Centro = c.NombreCentro,
                                   Cajero = ecfe.NumCajero,
                                   Fecha_Programada = ag.FechaProgramada,
                                   Fecha_Atencion = m != null ? m.FechaAtencion : (DateOnly?)null,
                                   Fecha_Insercion = m != null ? m.FechaInsercion : (DateTime?)null,
                                   Problemas = m != null ? m.Problemas : null,
                                   Diagnostico = m != null ? m.Diagnostico : null,
                                   Observaciones = m != null ? m.Observaciones : null,
                                   Estatus = ag.Estatus
                               }).ToListAsync();

            string[] headers = { "ZONA", "AGENCIA", "CENTRO", "NÚMERO DE CAJERO", "FECHA PROGRAMADA",
                               "FECHA DE ATENCIÓN", "FECHA DE TERMINACIÓN EN SISTEMA", "PROBLEMAS", "DIAGNÓSTICO", "OBSERVACIONES", "ESTATUS",
                               "DÍAS ENTRE LA FECHA PROGRAMADA Y LA FECHA DE ATENCIÓN" };

            for (int i = 0; i < headers.Length; i++)
            {
                worksheet.Cell(1, i + 1).Value = headers[i];
            }

            var headerRange = worksheet.Range(1, 1, 1, headers.Length);
            headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;
            headerRange.Style.Font.Bold = true;

            int row = 2;
            foreach (var item in datos)
            {
                worksheet.Cell(row, 1).Value = item.Zona;
                worksheet.Cell(row, 2).Value = item.Agencia;
                worksheet.Cell(row, 3).Value = item.Centro;
                worksheet.Cell(row, 4).Value = item.Cajero;
                worksheet.Cell(row, 5).Value = item.Fecha_Programada.ToString("dd/MM/yyyy");
                worksheet.Cell(row, 6).Value = item.Fecha_Atencion?.ToString("dd/MM/yyyy");
                worksheet.Cell(row, 7).Value = item.Fecha_Insercion?.ToString("dd/MM/yyyy HH:mm");
                worksheet.Cell(row, 8).Value = item.Problemas;
                worksheet.Cell(row, 9).Value = item.Diagnostico;
                worksheet.Cell(row, 10).Value = item.Observaciones;
                worksheet.Cell(row, 11).Value = item.Estatus;

                // Calcular días entre fecha programada y fecha de atención
                if (item.Fecha_Atencion.HasValue)
                {
                    var fechaProgramadaDateTime = new DateTime(item.Fecha_Programada.Year, item.Fecha_Programada.Month, item.Fecha_Programada.Day);
                    var fechaAtencionDateTime = new DateTime(item.Fecha_Atencion.Value.Year, item.Fecha_Atencion.Value.Month, item.Fecha_Atencion.Value.Day);

                    var diasDiferencia = (fechaAtencionDateTime - fechaProgramadaDateTime).Days;

                    string textoDias;
                    if (diasDiferencia > 0)
                    {
                        textoDias = $"{diasDiferencia} DÍAS DESPUÉS";
                    }
                    else if (diasDiferencia < 0)
                    {
                        textoDias = $"{Math.Abs(diasDiferencia)} DÍAS ANTES";
                    }
                    else
                    {
                        textoDias = "EL MISMO DÍA";
                    }

                    worksheet.Cell(row, 12).Value = textoDias;
                }
                else
                {
                    worksheet.Cell(row, 12).Value = "";
                }

                row++;
            }

            worksheet.Columns().AdjustToContents();
        }

        private async Task GenerarHojaACProgramado(IXLWorksheet worksheet, MantenimientosTIContext context)
        {
            var mesActual = DateTime.Now.Month;
            var añoActual = DateTime.Now.Year;

            var datos = await (from ag in context.Agenda
                               join eq in context.Equipos on ag.NumActFijo equals eq.NumActFijo
                               join eac in context.EquipoAcs on ag.NumActFijo equals eac.NumActFijo
                               join ct in context.CatTipoEquipos on eac.ClaveTipoEquipo equals ct.ClaveTipoEquipo
                               join c in context.CatCentros on new
                               {
                                   eq.ClaveDivision,
                                   eq.ClaveZona,
                                   eq.ClaveAgencia,
                                   eq.ClaveCentro
                               } equals new
                               {
                                   c.ClaveDivision,
                                   c.ClaveZona,
                                   c.ClaveAgencia,
                                   c.ClaveCentro
                               }
                               join agen in context.CatAgencia on new
                               {
                                   c.ClaveDivision,
                                   c.ClaveZona,
                                   c.ClaveAgencia
                               } equals new
                               {
                                   agen.ClaveDivision,
                                   agen.ClaveZona,
                                   agen.ClaveAgencia
                               }
                               join z in context.CatZonas on new
                               {
                                   agen.ClaveDivision,
                                   agen.ClaveZona
                               } equals new
                               {
                                   z.ClaveDivision,
                                   z.ClaveZona
                               }
                               join m in context.Mantenimientos on
                                   new { ag.ClaveAgenda, ag.NumActFijo }
                                   equals new { m.ClaveAgenda, m.NumActFijo } into mantenimientos
                               from m in mantenimientos.DefaultIfEmpty()
                               where ag.FechaProgramada.Month == mesActual
                                   && ag.FechaProgramada.Year == añoActual
                               orderby z.ClaveZona, ag.FechaProgramada
                               select new
                               {
                                   Zona = z.NombreZona,
                                   Agencia = agen.NombreAgencia,
                                   Centro = c.NombreCentro,
                                   Activo_Fijo = eq.NumActFijo,
                                   Serie = eac.NumSerie,
                                   Tipo_Equipo = ct.NombreTipoEquipo,
                                   Fecha_Programada = ag.FechaProgramada,
                                   Fecha_Atencion = m != null ? m.FechaAtencion : (DateOnly?)null,
                                   Fecha_Insercion = m != null ? m.FechaInsercion : (DateTime?)null,
                                   Problemas = m != null ? m.Problemas : null,
                                   Diagnostico = m != null ? m.Diagnostico : null,
                                   Observaciones = m != null ? m.Observaciones : null,
                                   Estatus = ag.Estatus
                               }).ToListAsync();

            string[] headers = { "ZONA", "AGENCIA", "CENTRO", "NÚMERO DE ACTIVO FIJO", "NÚMERO DE SERIE", "TIPO DE EQUIPO",
                               "FECHA PROGRAMADA", "FECHA DE ATENCIÓN", "FECHA DE TERMINACIÓN EN SISTEMA", "PROBLEMAS", "DIAGNÓSTICO", "OBSERVACIONES", "ESTATUS",
                               "DÍAS ENTRE LA FECHA PROGRAMADA Y LA FECHA DE ATENCIÓN" };

            for (int i = 0; i < headers.Length; i++)
            {
                worksheet.Cell(1, i + 1).Value = headers[i];
            }

            var headerRange = worksheet.Range(1, 1, 1, headers.Length);
            headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;
            headerRange.Style.Font.Bold = true;

            int row = 2;
            foreach (var item in datos)
            {
                worksheet.Cell(row, 1).Value = item.Zona;
                worksheet.Cell(row, 2).Value = item.Agencia;
                worksheet.Cell(row, 3).Value = item.Centro;
                worksheet.Cell(row, 4).Value = item.Activo_Fijo;
                worksheet.Cell(row, 5).Value = item.Serie;
                worksheet.Cell(row, 6).Value = item.Tipo_Equipo;
                worksheet.Cell(row, 7).Value = item.Fecha_Programada.ToString("dd/MM/yyyy");
                worksheet.Cell(row, 8).Value = item.Fecha_Atencion?.ToString("dd/MM/yyyy");
                worksheet.Cell(row, 9).Value = item.Fecha_Insercion?.ToString("dd/MM/yyyy HH:mm");
                worksheet.Cell(row, 10).Value = item.Problemas;
                worksheet.Cell(row, 11).Value = item.Diagnostico;
                worksheet.Cell(row, 12).Value = item.Observaciones;
                worksheet.Cell(row, 13).Value = item.Estatus;

                // Calcular días entre fecha programada y fecha de atención
                if (item.Fecha_Atencion.HasValue)
                {
                    var fechaProgramadaDateTime = new DateTime(item.Fecha_Programada.Year, item.Fecha_Programada.Month, item.Fecha_Programada.Day);
                    var fechaAtencionDateTime = new DateTime(item.Fecha_Atencion.Value.Year, item.Fecha_Atencion.Value.Month, item.Fecha_Atencion.Value.Day);

                    var diasDiferencia = (fechaAtencionDateTime - fechaProgramadaDateTime).Days;

                    string textoDias;
                    if (diasDiferencia > 0)
                    {
                        textoDias = $"{diasDiferencia} DÍAS DESPUÉS";
                    }
                    else if (diasDiferencia < 0)
                    {
                        textoDias = $"{Math.Abs(diasDiferencia)} DÍAS ANTES";
                    }
                    else
                    {
                        textoDias = "EL MISMO DÍA";
                    }

                    worksheet.Cell(row, 14).Value = textoDias;
                }
                else
                {
                    worksheet.Cell(row, 14).Value = "";
                }

                row++;
            }

            worksheet.Columns().AdjustToContents();
        }

        private async Task GenerarHojaComputoProgramado(IXLWorksheet worksheet, MantenimientosTIContext context)
        {
            var mesActual = DateTime.Now.Month;
            var añoActual = DateTime.Now.Year;

            var datos = await (from ag in context.Agenda
                               join eq in context.Equipos on ag.NumActFijo equals eq.NumActFijo
                               join ec in context.EquipoComputos on ag.NumActFijo equals ec.NumActFijo
                               join ct in context.CatTipoEquipos on ec.ClaveTipoEquipo equals ct.ClaveTipoEquipo
                               join c in context.CatCentros on new
                               {
                                   eq.ClaveDivision,
                                   eq.ClaveZona,
                                   eq.ClaveAgencia,
                                   eq.ClaveCentro
                               } equals new
                               {
                                   c.ClaveDivision,
                                   c.ClaveZona,
                                   c.ClaveAgencia,
                                   c.ClaveCentro
                               }
                               join agen in context.CatAgencia on new
                               {
                                   c.ClaveDivision,
                                   c.ClaveZona,
                                   c.ClaveAgencia
                               } equals new
                               {
                                   agen.ClaveDivision,
                                   agen.ClaveZona,
                                   agen.ClaveAgencia
                               }
                               join z in context.CatZonas on new
                               {
                                   agen.ClaveDivision,
                                   agen.ClaveZona
                               } equals new
                               {
                                   z.ClaveDivision,
                                   z.ClaveZona
                               }
                               join m in context.Mantenimientos on
                                   new { ag.ClaveAgenda, ag.NumActFijo }
                                   equals new { m.ClaveAgenda, m.NumActFijo } into mantenimientos
                               from m in mantenimientos.DefaultIfEmpty()
                               where ag.FechaProgramada.Month == mesActual
                                   && ag.FechaProgramada.Year == añoActual
                               orderby z.ClaveZona, ag.FechaProgramada
                               select new
                               {
                                   Zona = z.NombreZona,
                                   Agencia = agen.NombreAgencia,
                                   Centro = c.NombreCentro,
                                   Activo_Fijo = eq.NumActFijo,
                                   Serie_PC = ec.NumSeriePc,
                                   Serie_Monitor = ec.NumSerieMonitor,
                                   RPE_Asignado = ec.Rpe,
                                   Nombre_Responsable = ec.NombreRpe,
                                   Tipo_Equipo = ct.NombreTipoEquipo,
                                   Fecha_Programada = ag.FechaProgramada,
                                   Fecha_Atencion = m != null ? m.FechaAtencion : (DateOnly?)null,
                                   Fecha_Insercion = m != null ? m.FechaInsercion : (DateTime?)null,
                                   Problemas = m != null ? m.Problemas : null,
                                   Diagnostico = m != null ? m.Diagnostico : null,
                                   Observaciones = m != null ? m.Observaciones : null,
                                   Estatus = ag.Estatus
                               }).ToListAsync();

            string[] headers = { "ZONA", "AGENCIA", "CENTRO", "NÚMERO DE ACTIVO FIJO", "NÚMERO DE SERIE PC", "NÚMERO DE SERIE MONITOR",
                               "RPE", "NOMBRE", "TIPO DE EQUIPO", "FECHA PROGRAMADA",
                               "FECHA DE ATENCIÓN", "FECHA DE TERMINACIÓN EN SISTEMA", "PROBLEMAS", "DIAGNÓSTICO", "OBSERVACIONES", "ESTATUS",
                               "DÍAS ENTRE LA FECHA PROGRAMADA Y LA FECHA DE ATENCIÓN" };

            for (int i = 0; i < headers.Length; i++)
            {
                worksheet.Cell(1, i + 1).Value = headers[i];
            }

            var headerRange = worksheet.Range(1, 1, 1, headers.Length);
            headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;
            headerRange.Style.Font.Bold = true;

            int row = 2;
            foreach (var item in datos)
            {
                worksheet.Cell(row, 1).Value = item.Zona;
                worksheet.Cell(row, 2).Value = item.Agencia;
                worksheet.Cell(row, 3).Value = item.Centro;
                worksheet.Cell(row, 4).Value = item.Activo_Fijo;
                worksheet.Cell(row, 5).Value = item.Serie_PC;
                worksheet.Cell(row, 6).Value = item.Serie_Monitor;
                worksheet.Cell(row, 7).Value = item.RPE_Asignado;
                worksheet.Cell(row, 8).Value = item.Nombre_Responsable;
                worksheet.Cell(row, 9).Value = item.Tipo_Equipo;
                worksheet.Cell(row, 10).Value = item.Fecha_Programada.ToString("dd/MM/yyyy");
                worksheet.Cell(row, 11).Value = item.Fecha_Atencion?.ToString("dd/MM/yyyy");
                worksheet.Cell(row, 12).Value = item.Fecha_Insercion?.ToString("dd/MM/yyyy HH:mm");
                worksheet.Cell(row, 13).Value = item.Problemas;
                worksheet.Cell(row, 14).Value = item.Diagnostico;
                worksheet.Cell(row, 15).Value = item.Observaciones;
                worksheet.Cell(row, 16).Value = item.Estatus;

                // Calcular días entre fecha programada y fecha de atención
                if (item.Fecha_Atencion.HasValue)
                {
                    var fechaProgramadaDateTime = new DateTime(item.Fecha_Programada.Year, item.Fecha_Programada.Month, item.Fecha_Programada.Day);
                    var fechaAtencionDateTime = new DateTime(item.Fecha_Atencion.Value.Year, item.Fecha_Atencion.Value.Month, item.Fecha_Atencion.Value.Day);

                    var diasDiferencia = (fechaAtencionDateTime - fechaProgramadaDateTime).Days;

                    string textoDias;
                    if (diasDiferencia > 0)
                    {
                        textoDias = $"{diasDiferencia} DÍAS DESPUÉS";
                    }
                    else if (diasDiferencia < 0)
                    {
                        textoDias = $"{Math.Abs(diasDiferencia)} DÍAS ANTES";
                    }
                    else
                    {
                        textoDias = "EL MISMO DÍA";
                    }

                    worksheet.Cell(row, 17).Value = textoDias;
                }
                else
                {
                    worksheet.Cell(row, 17).Value = "";
                }

                row++;
            }

            worksheet.Columns().AdjustToContents();
        }

        public override async Task StopAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Servicio de correo programado está deteniéndose...");
            await base.StopAsync(stoppingToken);
        }
    }

    public class ReporteResumen
    {
        public string Zona { get; set; }
        public int Programados { get; set; }
        public int Terminados { get; set; }
        public int Pendientes { get; set; }
        public decimal Avance { get; set; }
    }
}