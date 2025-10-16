using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Net;
using System.Net.Mail;
using MantenimientosTI.Models;
using Microsoft.EntityFrameworkCore;
using ClosedXML.Excel;
using System.Text;
using Microsoft.Extensions.Logging;

namespace MantenimientosTI.Controllers
{
    [Authorize]
    public class DashboardController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly MantenimientosTIContext _context;
        private readonly ILogger<DashboardController> _logger; // Para el envio de correos programados

        public DashboardController(IConfiguration configuration, MantenimientosTIContext context, ILogger<DashboardController> logger = null)
        {
            _configuration = configuration;
            _context = context;
            _logger = logger;
        }

        public IActionResult Dashboard()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> EnviarCorreoPrueba()
        {
            try
            {
                var resultado = await EnviarCorreoConExcel();

                if (resultado)
                {
                    return Json(new
                    {
                        success = true,
                        message = "El reporte ha sido enviado exitosamente"
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

        private async Task<bool> EnviarCorreoConExcel()
        {
            try
            {
                var smtpServer = _configuration["EmailSettings:SmtpServer"];
                var port = int.Parse(_configuration["EmailSettings:Port"]);
                var username = _configuration["EmailSettings:Username"];
                var password = _configuration["EmailSettings:Password"];
                var fromAddress = _configuration["EmailSettings:FromAddress"];

                // OBTENER CORREOS DE USUARIOS CON ClaveRol = 1 Y RecibirReporte = "SI"
                var correosDestinatarios = await _context.Usuarios
                    .Where(u => u.ClaveRol == 1 &&
                               u.RecibirReporte == "SI" &&
                               u.Estatus.ToLower() == "activo")
                    .Select(u => u.Correo)
                    .ToListAsync();

                if (!correosDestinatarios.Any())
                {
                    _logger?.LogWarning("No se encontraron destinatarios válidos");
                    return false;
                }

                var excelBytes = await GenerarExcelReporte();
                var mesActual = DateTime.Now.ToString("MMMM yyyy");

                var reporteCFE = await ObtenerReporteCFE();
                var reporteAC = await ObtenerReporteAC();
                var reporteComputo = await ObtenerReporteComputo();

                var cuerpoHTML = GenerarCuerpoCorreoHTML(mesActual, reporteCFE, reporteAC, reporteComputo);

                using var client = new SmtpClient(smtpServer, port);

                // VERIFICAR SI HAY CONTRASEÑA O NO
                if (!string.IsNullOrEmpty(password))
                {
                    client.Credentials = new NetworkCredential(username, password);
                }
                else
                {
                    _logger?.LogInformation("Intentando envío sin contraseña (autenticación por red)");
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

                foreach (var correo in correosDestinatarios)
                {
                    if (!string.IsNullOrWhiteSpace(correo))
                    {
                        message.Bcc.Add(correo);
                    }
                }

                if (message.Bcc.Count == 0)
                {
                    _logger?.LogWarning("No hay correos válidos para enviar");
                    return false;
                }

                using var stream = new MemoryStream(excelBytes);
                var attachment = new Attachment(stream,
                    $"Reporte_Mantenimientos_{mesActual}.xlsx",
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

                message.Attachments.Add(attachment);

                await client.SendMailAsync(message);

                _logger?.LogInformation($"Reporte enviado exitosamente a {message.Bcc.Count} destinatarios");
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error enviando correo: {Message}", ex.Message);
                return false;
            }
        }

        private string GenerarCuerpoCorreoHTML(string mesActual,
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

            sb.AppendLine(GenerarTablaHTML(reporteCFE, "CFEmáticos"));
            sb.AppendLine($@"<br><h3>AVANCE DE MANTENIMIENTOS DE EQUIPOS DE ATENCIÓN A CLIENTES - {mesActual.ToUpper()}</h3>");
            sb.AppendLine(GenerarTablaHTML(reporteAC, "Equipos AC"));
            sb.AppendLine($@"<br><h3>AVANCE DE MANTENIMIENTOS DE EQUIPOS DE CÓMPUTO - {mesActual.ToUpper()}</h3>");
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

        private async Task<List<ReporteResumen>> ObtenerReporteCFE()
        {
            var mesActual = DateTime.Now.Month;
            var añoActual = DateTime.Now.Year;

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

        private async Task<List<ReporteResumen>> ObtenerReporteAC()
        {
            var mesActual = DateTime.Now.Month;
            var añoActual = DateTime.Now.Year;

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

        private async Task<List<ReporteResumen>> ObtenerReporteComputo()
        {
            var mesActual = DateTime.Now.Month;
            var añoActual = DateTime.Now.Year;

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

        private async Task<byte[]> GenerarExcelReporte()
        {
            using var workbook = new XLWorkbook();

            var wsCFE = workbook.Worksheets.Add("CFEMÁTICOS");
            await GenerarHojaCFE(wsCFE);

            var wsAC = workbook.Worksheets.Add("EQUIPOS DE ATENCIÓN A CLIENTES");
            await GenerarHojaAC(wsAC);

            var wsComputo = workbook.Worksheets.Add("EQUIPOS DE CÓMPUTO");
            await GenerarHojaComputo(wsComputo);

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        private async Task GenerarHojaCFE(IXLWorksheet worksheet)
        {
            var mesActual = DateTime.Now.Month;
            var añoActual = DateTime.Now.Year;

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
                    // Convertir DateOnly a DateTime correctamente
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

        private async Task GenerarHojaAC(IXLWorksheet worksheet)
        {
            var mesActual = DateTime.Now.Month;
            var añoActual = DateTime.Now.Year;

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
                    // Convertir DateOnly a DateTime correctamente
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

        private async Task GenerarHojaComputo(IXLWorksheet worksheet)
        {
            var mesActual = DateTime.Now.Month;
            var añoActual = DateTime.Now.Year;

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
                    // Convertir DateOnly a DateTime correctamente
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

        public async Task<bool> EnviarCorreoProgramado()
        {
            try
            {
                _logger?.LogInformation("Iniciando envío programado de reporte...");

                var resultado = await EnviarCorreoConExcel();

                if (resultado)
                {
                    _logger?.LogInformation("Reporte programado enviado exitosamente.");
                    return true;
                }
                else
                {
                    _logger?.LogError("Error al enviar reporte programado.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Excepción al enviar reporte programado.");
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