using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Net.Mail;
using System.Net;
using MantenimientosTI.Models;
using Microsoft.EntityFrameworkCore;
using ClosedXML.Excel;

namespace MantenimientosTI.Controllers
{
    [Authorize]
    public class DashboardController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly MantenimientosTIContext _context;

        public DashboardController(IConfiguration configuration, MantenimientosTIContext context)
        {
            _configuration = configuration;
            _context = context;
        }

        public IActionResult Dashboard()
        {
            // Obtener las zonas para el dropdown
            var zonas = _context.CatZonas
                .Select(z => new { z.ClaveZona, z.NombreZona })
                .OrderBy(z => z.NombreZona)
                .ToList();

            ViewBag.Zonas = zonas;
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> EnviarCorreoPrueba(string zonaSeleccionada)
        {
            try
            {
                if (string.IsNullOrEmpty(zonaSeleccionada))
                {
                    return Json(new
                    {
                        success = false,
                        message = "Por favor seleccione una zona"
                    });
                }

                var resultado = await EnviarCorreoConExcel(zonaSeleccionada);

                if (resultado)
                {
                    return Json(new
                    {
                        success = true,
                        message = $"Reporte Excel de la zona {zonaSeleccionada} enviado exitosamente"
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

        private async Task<bool> EnviarCorreoConExcel(string claveZona)
        {
            try
            {
                var smtpServer = _configuration["EmailSettings:SmtpServer"];
                var port = int.Parse(_configuration["EmailSettings:Port"]);
                var username = _configuration["EmailSettings:Username"];
                var password = _configuration["EmailSettings:Password"];
                var fromAddress = _configuration["EmailSettings:FromAddress"];
                var toAddress = _configuration["EmailSettings:TestEmail"];

                // Generar el Excel
                var excelBytes = await GenerarExcelReporte(claveZona);
                var nombreZona = await ObtenerNombreZona(claveZona);
                var mesActual = DateTime.Now.ToString("MMMM yyyy");

                using var client = new SmtpClient(smtpServer, port)
                {
                    Credentials = new NetworkCredential(username, password),
                    EnableSsl = true
                };

                using var message = new MailMessage
                {
                    From = new MailAddress(fromAddress),
                    Subject = $"Reporte de Mantenimientos - Zona {nombreZona} - {mesActual}",
                    Body = $@"Estimado usuario,

Se adjunta el reporte de mantenimientos correspondiente a la zona {nombreZona} para el mes de {mesActual}.

El archivo contiene 3 hojas:
- CFEmáticos
- Equipos de Atención a Clientes  
- Equipos de Cómputo

Saludos,
Sistema ARGOS",
                    IsBodyHtml = false
                };

                message.To.Add(toAddress);

                // Adjuntar el Excel
                using var stream = new MemoryStream(excelBytes);
                var attachment = new Attachment(stream,
                    $"Reporte_Mantenimientos_Zona_{nombreZona}_{DateTime.Now:yyyyMMdd}.xlsx",
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

                message.Attachments.Add(attachment);

                await client.SendMailAsync(message);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error enviando correo: {ex.Message}");
                return false;
            }
        }

        private async Task<byte[]> GenerarExcelReporte(string claveZona)
        {
            using var workbook = new XLWorkbook();

            // HOJA 1: CFEMÁTICOS
            var wsCFE = workbook.Worksheets.Add("CFEMÁTICOS");
            await GenerarHojaCFE(wsCFE, claveZona);

            // HOJA 2: EQUIPOS DE ATENCIÓN A CLIENTES
            var wsAC = workbook.Worksheets.Add("EQUIPOS DE ATENCIÓN A CLIENTES");
            await GenerarHojaAC(wsAC, claveZona);

            // HOJA 3: EQUIPOS DE CÓMPUTO
            var wsComputo = workbook.Worksheets.Add("EQUIPOS DE CÓMPUTO");
            await GenerarHojaComputo(wsComputo, claveZona);

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        private async Task GenerarHojaCFE(IXLWorksheet worksheet, string claveZona)
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
                               where z.ClaveZona == claveZona
                                   && ag.FechaProgramada.Month == mesActual
                                   && ag.FechaProgramada.Year == añoActual
                               orderby ag.FechaProgramada
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
                                   Observaciones = m != null ? m.Observaciones : null
                               }).ToListAsync();

            // Configurar encabezados
            string[] headers = { "ZONA", "AGENCIA", "CENTRO", "NÚMERO DE CAJERO", "FECHA PROGRAMADA",
                               "FECHA DE ATENCIÓN", "FECHA DE INSERCIÓN", "PROBLEMAS", "DIAGNÓSTICO", "OBSERVACIONES" };

            for (int i = 0; i < headers.Length; i++)
            {
                worksheet.Cell(1, i + 1).Value = headers[i];
            }

            // Estilo para encabezados
            var headerRange = worksheet.Range(1, 1, 1, headers.Length);
            headerRange.Style.Fill.BackgroundColor = XLColor.Gray;
            headerRange.Style.Font.Bold = true;

            // Llenar datos
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
                row++;
            }

            worksheet.Columns().AdjustToContents();
        }

        private async Task GenerarHojaAC(IXLWorksheet worksheet, string claveZona)
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
                               where z.ClaveZona == claveZona
                                   && ag.FechaProgramada.Month == mesActual
                                   && ag.FechaProgramada.Year == añoActual
                               orderby ag.FechaProgramada
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
                                   Observaciones = m != null ? m.Observaciones : null
                               }).ToListAsync();

            // Configurar encabezados
            string[] headers = { "ZONA", "AGENCIA", "CENTRO", "NÚMERO DE ACTIVO FIJO", "NÚMERO DE SERIE", "TIPO DE EQUIPO",
                               "FECHA PROGRAMADA", "FECHA DE ATENCIÓN", "FECHA DE INSERCIÓN", "PROBLEMAS", "DIAGNÓSTICO", "OBSERVACIONES" };

            for (int i = 0; i < headers.Length; i++)
            {
                worksheet.Cell(1, i + 1).Value = headers[i];
            }

            // Estilo para encabezados
            var headerRange = worksheet.Range(1, 1, 1, headers.Length);
            headerRange.Style.Fill.BackgroundColor = XLColor.Gray;
            headerRange.Style.Font.Bold = true;

            // Llenar datos
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
                row++;
            }

            worksheet.Columns().AdjustToContents();
        }

        private async Task GenerarHojaComputo(IXLWorksheet worksheet, string claveZona)
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
                               where z.ClaveZona == claveZona
                                   && ag.FechaProgramada.Month == mesActual
                                   && ag.FechaProgramada.Year == añoActual
                               orderby ag.FechaProgramada
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
                                   Observaciones = m != null ? m.Observaciones : null
                               }).ToListAsync();

            // Configurar encabezados
            string[] headers = { "ZONA", "AGENCIA", "CENTRO", "NÚMERO DE ACTIVO FIJO", "NÚMERO DE SERIE PC", "NÚMERO DE SERIE MONITOR",
                               "RPE", "NOMBRE", "TIPO DE EQUIPO", "FECHA PROGRAMADA",
                               "FECHA DE ATENCIÓN", "FECHA DE INSERCIÓN", "PROBLEMAS", "DIAGNÓSTICO", "OBSERVACIONES" };

            for (int i = 0; i < headers.Length; i++)
            {
                worksheet.Cell(1, i + 1).Value = headers[i];
            }

            // Estilo para encabezados
            var headerRange = worksheet.Range(1, 1, 1, headers.Length);
            headerRange.Style.Fill.BackgroundColor = XLColor.Gray;
            headerRange.Style.Font.Bold = true;

            // Llenar datos
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
                row++;
            }

            worksheet.Columns().AdjustToContents();
        }

        private async Task<string> ObtenerNombreZona(string claveZona)
        {
            var zona = await _context.CatZonas
                .Where(z => z.ClaveZona == claveZona)
                .Select(z => z.NombreZona)
                .FirstOrDefaultAsync();

            return zona ?? claveZona;
        }
    }
}