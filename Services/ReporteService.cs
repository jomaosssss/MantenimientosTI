using MantenimientosTI.Models;
using Microsoft.EntityFrameworkCore;
using ClosedXML.Excel;
using System.Text;
using System.Net.Mail;
using System.Net;

namespace MantenimientosTI.Services
{
    public class ReporteService
    {
        private readonly MantenimientosTIContext _context;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ReporteService> _logger;

        public ReporteService(MantenimientosTIContext context, IConfiguration configuration, ILogger<ReporteService> logger)
        {
            _context = context;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<List<string>> ObtenerCorreosDestinatarios()
        {
            try
            {
                var correos = await _context.Usuarios
                    .Where(u => u.ClaveRol == 1 &&
                               u.RecibirReporte == "SI" &&
                               u.Estatus.ToLower() == "activo")
                    .Select(u => u.Correo)
                    .ToListAsync();

                _logger.LogInformation($"Se encontraron {correos.Count} destinatarios para el reporte");
                return correos;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener correos destinatarios");
                return new List<string>();
            }
        }

        public async Task<bool> EnviarCorreoConExcel(DateTime fechaInicio, DateTime fechaFin, List<string> usuariosSeleccionados = null)
        {
            try
            {
                _logger.LogInformation($"Iniciando generación y envío de reporte: {fechaInicio:dd/MM/yyyy} - {fechaFin:dd/MM/yyyy}");

                var smtpServer = _configuration["EmailSettings:SmtpServer"];
                var port = int.Parse(_configuration["EmailSettings:Port"]);
                var username = _configuration["EmailSettings:Username"];
                var password = _configuration["EmailSettings:Password"];
                var fromAddress = _configuration["EmailSettings:FromAddress"];

                _logger.LogInformation($"Configuración SMTP: Server={smtpServer}, Port={port}, From={fromAddress}");

                // Obtener destinatarios
                List<string> correosDestinatarios;

                if (usuariosSeleccionados != null && usuariosSeleccionados.Any())
                {
                    // Usar los usuarios específicamente seleccionados desde el Dashboard
                    correosDestinatarios = await _context.Usuarios
                        .Where(u => usuariosSeleccionados.Contains(u.Rpe) &&
                                   u.Estatus.ToLower() == "activo" &&
                                   !string.IsNullOrEmpty(u.Correo))
                        .Select(u => u.Correo)
                        .ToListAsync();

                    _logger.LogInformation($"Enviando a {correosDestinatarios.Count} usuarios seleccionados específicamente");
                }
                else
                {
                    // Usar la lógica original (todos los que tienen reportes habilitados)
                    correosDestinatarios = await ObtenerCorreosDestinatarios();
                    _logger.LogInformation($"Enviando a {correosDestinatarios.Count} usuarios con reportes habilitados");
                }

                if (!correosDestinatarios.Any())
                {
                    _logger.LogWarning("No se encontraron destinatarios válidos para enviar el reporte");
                    return false;
                }

                // El resto del método permanece igual...
                // Generar el archivo Excel
                var excelBytes = await GenerarExcelReporte(fechaInicio, fechaFin);
                if (excelBytes == null || excelBytes.Length == 0)
                {
                    _logger.LogError("No se pudo generar el archivo Excel");
                    return false;
                }

                var rangoFechas = $"del {fechaInicio:dd/MM/yyyy} al {fechaFin:dd/MM/yyyy}";

                // Obtener datos para el cuerpo del correo
                var reporteCFE = await ObtenerReporteCFE(fechaInicio, fechaFin);
                var reporteAC = await ObtenerReporteAC(fechaInicio, fechaFin);
                var reporteComputo = await ObtenerReporteComputo(fechaInicio, fechaFin);

                var cuerpoHTML = GenerarCuerpoCorreoHTML(rangoFechas, reporteCFE, reporteAC, reporteComputo);

                using var client = new SmtpClient(smtpServer, port);

                // Configuración SMTP mejorada
                if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
                {
                    client.Credentials = new NetworkCredential(username, password);
                    _logger.LogInformation("Usando autenticación con credenciales");
                }
                else
                {
                    _logger.LogInformation("Envío sin autenticación (red interna)");
                }

                client.EnableSsl = false;
                client.Timeout = 60000; // 60 segundos timeout
                client.DeliveryMethod = SmtpDeliveryMethod.Network;

                using var message = new MailMessage
                {
                    From = new MailAddress(fromAddress),
                    Subject = $"REPORTE DE MANTENIMIENTOS - {rangoFechas}",
                    Body = cuerpoHTML,
                    IsBodyHtml = true
                };

                foreach (var correo in correosDestinatarios)
                {
                    if (!string.IsNullOrWhiteSpace(correo))
                    {
                        message.Bcc.Add(correo.Trim());
                    }
                }

                if (message.Bcc.Count == 0)
                {
                    _logger.LogWarning("No hay correos válidos para enviar después de la validación");
                    return false;
                }

                // Adjuntar archivo Excel
                using var stream = new MemoryStream(excelBytes);
                var attachment = new Attachment(stream,
                    $"Reporte_Mantenimientos_{fechaInicio:yyyyMMdd}_{fechaFin:yyyyMMdd}.xlsx",
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

                message.Attachments.Add(attachment);

                _logger.LogInformation($"Enviando correo a {message.Bcc.Count} destinatarios...");
                await client.SendMailAsync(message);
                _logger.LogInformation("Correo enviado exitosamente");

                return true;
            }
            catch (SmtpException smtpEx)
            {
                _logger.LogError(smtpEx, "Error SMTP al enviar correo: {Message}", smtpEx.Message);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inesperado al enviar correo: {Message}", ex.Message);
                return false;
            }
        }

        private string GenerarCuerpoCorreoHTML(string rangoFechas,
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
                    <h2>REPORTE DE MANTENIMIENTOS - ARGOS</h2>
                    <div class='info-box'>
                        <p><strong>Periodo:</strong> {rangoFechas}</p>
                    </div>
                    
                    <h3>AVANCE DE MANTENIMIENTOS DE CFEMÁTICOS - {rangoFechas.ToUpper()}</h3>");

            sb.AppendLine(GenerarTablaHTML(reporteCFE, "CFEmáticos"));
            sb.AppendLine($@"<br><h3>AVANCE DE MANTENIMIENTOS DE EQUIPOS DE ATENCIÓN A CLIENTES - {rangoFechas.ToUpper()}</h3>");
            sb.AppendLine(GenerarTablaHTML(reporteAC, "Equipos AC"));
            sb.AppendLine($@"<br><h3>AVANCE DE MANTENIMIENTOS DE EQUIPOS DE CÓMPUTO - {rangoFechas.ToUpper()}</h3>");
            sb.AppendLine(GenerarTablaHTML(reporteComputo, "Equipos Computo"));

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

        private string GenerarTablaHTML(List<ReporteResumen> datos, string tipoEquipo)
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

        public async Task<List<ReporteResumen>> ObtenerReporteCFE(DateTime fechaInicio, DateTime fechaFin)
        {
            try
            {
                // Convertir DateTime a DateOnly
                var fechaInicioDate = DateOnly.FromDateTime(fechaInicio);
                var fechaFinDate = DateOnly.FromDateTime(fechaFin);

                var todasLasZonas = await _context.CatZonas
                    .OrderBy(z => z.ClaveZona)
                    .Select(z => new { z.ClaveZona, z.NombreZona })
                    .ToListAsync();

                var resultados = new List<ReporteResumen>();

                foreach (var zona in todasLasZonas)
                {
                    var datosZona = await (from z in _context.CatZonas
                                           join agen in _context.CatAgencia on
                                               new { z.ClaveDivision, z.ClaveZona } equals
                                               new { agen.ClaveDivision, agen.ClaveZona }
                                           join c in _context.CatCentros on
                                               new { agen.ClaveDivision, agen.ClaveZona, agen.ClaveAgencia } equals
                                               new { c.ClaveDivision, c.ClaveZona, c.ClaveAgencia }
                                           join eq in _context.Equipos on
                                               new { c.ClaveDivision, c.ClaveZona, c.ClaveAgencia, c.ClaveCentro } equals
                                               new { eq.ClaveDivision, eq.ClaveZona, eq.ClaveAgencia, eq.ClaveCentro }
                                           join ecfe in _context.EquipoCfematicos on eq.NumActFijo equals ecfe.NumActFijo
                                           join ag in _context.Agenda on eq.NumActFijo equals ag.NumActFijo
                                           where z.ClaveZona == zona.ClaveZona &&
                                                 ag.FechaProgramada >= fechaInicioDate &&
                                                 ag.FechaProgramada <= fechaFinDate
                                           select new { ag })
                                         .ToListAsync();

                    var programados = datosZona.Count(x => x.ag != null && x.ag.Estatus!="CANCELADO");
                    var terminados = datosZona.Count(x => x.ag != null && x.ag.Estatus == "TERMINADO");
                    var pendientes = datosZona.Count(x => x.ag != null && (x.ag.Estatus == "PENDIENTE" || x.ag.Estatus == "PRE-CANCELADO"));
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener reporte CFE");
                return new List<ReporteResumen>();
            }
        }

        public async Task<List<ReporteResumen>> ObtenerReporteAC(DateTime fechaInicio, DateTime fechaFin)
        {
            try
            {
                // Convertir DateTime a DateOnly
                var fechaInicioDate = DateOnly.FromDateTime(fechaInicio);
                var fechaFinDate = DateOnly.FromDateTime(fechaFin);

                var todasLasZonas = await _context.CatZonas
                    .OrderBy(z => z.ClaveZona)
                    .Select(z => new { z.ClaveZona, z.NombreZona })
                    .ToListAsync();

                var resultados = new List<ReporteResumen>();

                foreach (var zona in todasLasZonas)
                {
                    var datosZona = await (from z in _context.CatZonas
                                           join agen in _context.CatAgencia on
                                               new { z.ClaveDivision, z.ClaveZona } equals
                                               new { agen.ClaveDivision, agen.ClaveZona }
                                           join c in _context.CatCentros on
                                               new { agen.ClaveDivision, agen.ClaveZona, agen.ClaveAgencia } equals
                                               new { c.ClaveDivision, c.ClaveZona, c.ClaveAgencia }
                                           join eq in _context.Equipos on
                                               new { c.ClaveDivision, c.ClaveZona, c.ClaveAgencia, c.ClaveCentro } equals
                                               new { eq.ClaveDivision, eq.ClaveZona, eq.ClaveAgencia, eq.ClaveCentro }
                                           join eac in _context.EquipoAcs on eq.NumActFijo equals eac.NumActFijo
                                           join ag in _context.Agenda on eq.NumActFijo equals ag.NumActFijo
                                           where z.ClaveZona == zona.ClaveZona &&
                                                 ag.FechaProgramada >= fechaInicioDate &&
                                                 ag.FechaProgramada <= fechaFinDate
                                           select new { ag })
                                         .ToListAsync();

                    var programados = datosZona.Count(x => x.ag != null && x.ag.Estatus != "CANCELADO");
                    var terminados = datosZona.Count(x => x.ag != null && x.ag.Estatus == "TERMINADO");
                    var pendientes = datosZona.Count(x => x.ag != null && (x.ag.Estatus == "PENDIENTE" || x.ag.Estatus == "PRE-CANCELADO"));
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener reporte AC");
                return new List<ReporteResumen>();
            }
        }

        public async Task<List<ReporteResumen>> ObtenerReporteComputo(DateTime fechaInicio, DateTime fechaFin)
        {
            try
            {
                // Convertir DateTime a DateOnly
                var fechaInicioDate = DateOnly.FromDateTime(fechaInicio);
                var fechaFinDate = DateOnly.FromDateTime(fechaFin);

                var todasLasZonas = await _context.CatZonas
                    .OrderBy(z => z.ClaveZona)
                    .Select(z => new { z.ClaveZona, z.NombreZona })
                    .ToListAsync();

                var resultados = new List<ReporteResumen>();

                foreach (var zona in todasLasZonas)
                {
                    var datosZona = await (from z in _context.CatZonas
                                           join agen in _context.CatAgencia on
                                               new { z.ClaveDivision, z.ClaveZona } equals
                                               new { agen.ClaveDivision, agen.ClaveZona }
                                           join c in _context.CatCentros on
                                               new { agen.ClaveDivision, agen.ClaveZona, agen.ClaveAgencia } equals
                                               new { c.ClaveDivision, c.ClaveZona, c.ClaveAgencia }
                                           join eq in _context.Equipos on
                                               new { c.ClaveDivision, c.ClaveZona, c.ClaveAgencia, c.ClaveCentro } equals
                                               new { eq.ClaveDivision, eq.ClaveZona, eq.ClaveAgencia, eq.ClaveCentro }
                                           join ec in _context.EquipoComputos on eq.NumActFijo equals ec.NumActFijo
                                           join ag in _context.Agenda on eq.NumActFijo equals ag.NumActFijo
                                           where z.ClaveZona == zona.ClaveZona &&
                                                 ag.FechaProgramada >= fechaInicioDate &&
                                                 ag.FechaProgramada <= fechaFinDate
                                           select new { ag })
                                         .ToListAsync();

                    var programados = datosZona.Count(x => x.ag != null && x.ag.Estatus != "CANCELADO");
                    var terminados = datosZona.Count(x => x.ag != null && x.ag.Estatus == "TERMINADO");
                    var pendientes = datosZona.Count(x => x.ag != null && (x.ag.Estatus == "PENDIENTE" || x.ag.Estatus == "PRE-CANCELADO"));
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener reporte Computo");
                return new List<ReporteResumen>();
            }
        }

        public async Task<byte[]> GenerarExcelReporte(DateTime fechaInicio, DateTime fechaFin)
        {
            try
            {
                using var workbook = new XLWorkbook();

                var wsCFE = workbook.Worksheets.Add("CFEMÁTICOS");
                await GenerarHojaCFE(wsCFE, fechaInicio, fechaFin);

                var wsAC = workbook.Worksheets.Add("EQUIPOS DE ATENCIÓN A CLIENTES");
                await GenerarHojaAC(wsAC, fechaInicio, fechaFin);

                var wsComputo = workbook.Worksheets.Add("EQUIPOS DE CÓMPUTO");
                await GenerarHojaComputo(wsComputo, fechaInicio, fechaFin);

                using var stream = new MemoryStream();
                workbook.SaveAs(stream);
                return stream.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al generar Excel reporte");
                return null;
            }
        }

        private async Task GenerarHojaCFE(IXLWorksheet worksheet, DateTime fechaInicio, DateTime fechaFin)
        {
            try
            {
                // Convertir DateTime a DateOnly
                var fechaInicioDate = DateOnly.FromDateTime(fechaInicio);
                var fechaFinDate = DateOnly.FromDateTime(fechaFin);

                var datos = await (from ag in _context.Agenda
                                   join eq in _context.Equipos on ag.NumActFijo equals eq.NumActFijo
                                   join ecfe in _context.EquipoCfematicos on ag.NumActFijo equals ecfe.NumActFijo
                                   join c in _context.CatCentros on new
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
                                   join agen in _context.CatAgencia on new
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
                                   join z in _context.CatZonas on new
                                   {
                                       agen.ClaveDivision,
                                       agen.ClaveZona
                                   } equals new
                                   {
                                       z.ClaveDivision,
                                       z.ClaveZona
                                   }
                                   join m in _context.Mantenimientos on
                                       new { ag.ClaveAgenda, ag.NumActFijo }
                                       equals new { m.ClaveAgenda, m.NumActFijo } into mantenimientos
                                   from m in mantenimientos.DefaultIfEmpty()
                                   where ag.FechaProgramada >= fechaInicioDate &&
                                         ag.FechaProgramada <= fechaFinDate
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al generar hoja CFE");
            }
        }

        private async Task GenerarHojaAC(IXLWorksheet worksheet, DateTime fechaInicio, DateTime fechaFin)
        {
            try
            {
                // Convertir DateTime a DateOnly
                var fechaInicioDate = DateOnly.FromDateTime(fechaInicio);
                var fechaFinDate = DateOnly.FromDateTime(fechaFin);

                var datos = await (from ag in _context.Agenda
                                   join eq in _context.Equipos on ag.NumActFijo equals eq.NumActFijo
                                   join eac in _context.EquipoAcs on ag.NumActFijo equals eac.NumActFijo
                                   join ct in _context.CatTipoEquipos on eac.ClaveTipoEquipo equals ct.ClaveTipoEquipo
                                   join c in _context.CatCentros on new
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
                                   join agen in _context.CatAgencia on new
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
                                   join z in _context.CatZonas on new
                                   {
                                       agen.ClaveDivision,
                                       agen.ClaveZona
                                   } equals new
                                   {
                                       z.ClaveDivision,
                                       z.ClaveZona
                                   }
                                   join m in _context.Mantenimientos on
                                       new { ag.ClaveAgenda, ag.NumActFijo }
                                       equals new { m.ClaveAgenda, m.NumActFijo } into mantenimientos
                                   from m in mantenimientos.DefaultIfEmpty()
                                   where ag.FechaProgramada >= fechaInicioDate &&
                                         ag.FechaProgramada <= fechaFinDate
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al generar hoja AC");
            }
        }

        private async Task GenerarHojaComputo(IXLWorksheet worksheet, DateTime fechaInicio, DateTime fechaFin)
        {
            try
            {
                // Convertir DateTime a DateOnly
                var fechaInicioDate = DateOnly.FromDateTime(fechaInicio);
                var fechaFinDate = DateOnly.FromDateTime(fechaFin);

                var datos = await (from ag in _context.Agenda
                                   join eq in _context.Equipos on ag.NumActFijo equals eq.NumActFijo
                                   join ec in _context.EquipoComputos on ag.NumActFijo equals ec.NumActFijo
                                   join ct in _context.CatTipoEquipos on ec.ClaveTipoEquipo equals ct.ClaveTipoEquipo
                                   join c in _context.CatCentros on new
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
                                   join agen in _context.CatAgencia on new
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
                                   join z in _context.CatZonas on new
                                   {
                                       agen.ClaveDivision,
                                       agen.ClaveZona
                                   } equals new
                                   {
                                       z.ClaveDivision,
                                       z.ClaveZona
                                   }
                                   join m in _context.Mantenimientos on
                                       new { ag.ClaveAgenda, ag.NumActFijo }
                                       equals new { m.ClaveAgenda, m.NumActFijo } into mantenimientos
                                   from m in mantenimientos.DefaultIfEmpty()
                                   where ag.FechaProgramada >= fechaInicioDate &&
                                         ag.FechaProgramada <= fechaFinDate
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al generar hoja Computo");
            }
        }

        public async Task<bool> EnviarCorreoNotificacionImpresora(
    string destinatario,
    string usuarioReporta,
    string folioAtencion)
        {
            try
            {
                var smtpServer = _configuration["EmailSettings:SmtpServer"];
                var port = int.Parse(_configuration["EmailSettings:Port"]);
                var username = _configuration["EmailSettings:Username"];
                var password = _configuration["EmailSettings:Password"];
                var fromAddress = _configuration["EmailSettings:FromAddress"];

                _logger.LogInformation($"Enviando notificación de impresora a: {destinatario}");

                var asunto = "AVISO DE MANTENIMIENTO A IMPRESORA";
                var cuerpoHTML = $@"
        <html>
        <body style='font-family: Arial, sans-serif; line-height: 1.6;'>
            <div style='max-width: 600px; margin: 0 auto; padding: 20px; border: 1px solid #e0e0e0; border-radius: 5px;'>
                <div style='background-color: #f8f9fa; padding: 15px; border-radius: 5px; margin-bottom: 20px;'>
                    <h2 style='color: #2c3e50; margin: 0;'>AVISO DE MANTENIMIENTO A IMPRESORA</h2>
                </div>
                
                <p>Estimado(a) <strong>{usuarioReporta}</strong>,</p>
                
                <p>Le informamos que su reporte con el folio <strong>{folioAtencion}</strong> está siendo atendido.</p>
                
                <div style='background-color: #e8f4fd; padding: 15px; border-left: 4px solid #007bff; margin: 20px 0;'>
                    <p style='margin: 0;'>
                        <strong>Folio:</strong> {folioAtencion}<br>
                        <strong>Estatus:</strong> En proceso de atención<br>
                        <strong>Fecha de notificación:</strong> {DateTime.Now:dd/MM/yyyy HH:mm}
                    </p>
                </div>
                
                <p>Nuestro equipo de soporte técnico se pondrá en contacto para dar seguimiento a su reporte.</p>
                
                <p>Gracias por su comprensión.</p>
                
                <hr style='border: none; border-top: 1px solid #e0e0e0; margin: 20px 0;'>
                
                <div style='font-size: 12px; color: #666; text-align: center;'>
                    <p>Este correo se envía de manera automática, favor de no responderlo.</p>
                    <p>© Sistema de Mantenimientos TI - CFE</p>
                </div>
            </div>
        </body>
        </html>";

                using var client = new SmtpClient(smtpServer, port);

                // Configuración SMTP (igual que en el método existente)
                if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
                {
                    client.Credentials = new NetworkCredential(username, password);
                    _logger.LogInformation("Usando autenticación con credenciales");
                }
                else
                {
                    _logger.LogInformation("Envío sin autenticación (red interna)");
                }

                client.EnableSsl = false;
                client.Timeout = 60000;
                client.DeliveryMethod = SmtpDeliveryMethod.Network;

                using var message = new MailMessage
                {
                    From = new MailAddress(fromAddress),
                    Subject = asunto,
                    Body = cuerpoHTML,
                    IsBodyHtml = true
                };

                message.To.Add(destinatario.Trim());

                _logger.LogInformation($"Enviando correo de notificación de impresora...");
                await client.SendMailAsync(message);
                _logger.LogInformation("Correo de notificación enviado exitosamente");

                return true;
            }
            catch (SmtpException smtpEx)
            {
                _logger.LogError(smtpEx, "Error SMTP al enviar correo de notificación: {Message}", smtpEx.Message);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inesperado al enviar correo de notificación: {Message}", ex.Message);
                return false;
            }
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