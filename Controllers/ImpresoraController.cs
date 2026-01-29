using Microsoft.AspNetCore.Mvc;
using MantenimientosTI.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using System.Globalization;
using System.IO; // Añade este using

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
                    .Include(i => i.NumActFijoNavigation)
                    .Where(i => i.NumActFijoNavigation.ClaveDivision == division &&
                               i.NumActFijoNavigation.ClaveZona == zona &&
                               i.NumActFijoNavigation.ClaveAgencia == agencia &&
                               i.NumActFijoNavigation.ClaveCentro == centro)
                    .Select(i => new
                    {
                        value = i.NumActFijo,
                        text = i.NumActFijo
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

        [HttpPost]
        public async Task<IActionResult> AgendarMantenimiento(
            [FromForm] string numActFijo,
            [FromForm] string folioAtencion,
            [FromForm] string fechaReporte,
            [FromForm] string problematica,
            [FromForm] string rpe,
            [FromForm] string usuarioReporta,
            [FromForm] string correo,
            [FromForm] string observaciones,
            [FromForm] IFormFile pdfRptUsuarioImpresora)
        {
            using (var transaction = await _dbContext.Database.BeginTransactionAsync())
            {
                try
                {
                    // Validar que la impresora existe
                    var impresora = await _dbContext.Impresoras
                        .FirstOrDefaultAsync(i => i.NumActFijo == numActFijo);

                    if (impresora == null)
                    {
                        return Json(new { success = false, message = "La impresora seleccionada no existe" });
                    }

                    // Obtener el número de serie de la impresora
                    var numSerieImpresora = impresora.NumSerie; // Asumiendo que existe esta propiedad

                    // Verificar si ya existe el folio de atención
                    var folioExistente = await _dbContext.ImpresoraMantenimientos
                        .AnyAsync(i => i.FolioAtencion == folioAtencion);

                    if (folioExistente)
                    {
                        return Json(new { success = false, message = "El folio de atención ya existe" });
                    }

                    // Convertir la fecha de reporte a DateOnly
                    if (!DateOnly.TryParseExact(fechaReporte, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly fechaReporteParsed))
                    {
                        return Json(new { success = false, message = "Formato de fecha inválido. Use formato: AAAA-MM-DD" });
                    }

                    // Verificar si ya existe un mantenimiento pendiente para esta impresora en la MISMA FECHA
                    var mantenimientoMismaFecha = await _dbContext.Agenda
                        .AnyAsync(a => a.NumActFijo == numActFijo &&
                                      a.Estatus == "PENDIENTE" &&
                                      a.FechaProgramada == fechaReporteParsed);

                    if (mantenimientoMismaFecha)
                    {
                        return Json(new
                        {
                            success = false,
                            message = $"Ya existe un mantenimiento pendiente para esta impresora en la fecha {fechaReporteParsed.ToString("dd/MM/yyyy")}. " +
                                      "Puede agendar para otra fecha o esperar a que el mantenimiento actual sea terminado."
                        });
                    }

                    // 1. Crear registro en Agenda (Agendum)
                    var agenda = new Agendum
                    {
                        NumActFijo = numActFijo,
                        ClaveTipoMtto = "C", // Siempre 'C' para correctivo
                        FechaProgramada = fechaReporteParsed,
                        Estatus = "PENDIENTE"
                    };

                    _dbContext.Agenda.Add(agenda);
                    await _dbContext.SaveChangesAsync(); // Guardar para obtener ClaveAgenda

                    // 2. Guardar archivo PDF en carpeta "rptImpresoras" - PASAR EL NÚMERO DE SERIE
                    var nombreArchivoPdf = await GuardarPdfRptUsuarioImpresora(pdfRptUsuarioImpresora, numSerieImpresora);

                    // 3. Crear registro en ImpresoraMantenimiento
                    var impresoraMantenimiento = new ImpresoraMantenimiento
                    {
                        FolioAtencion = folioAtencion,
                        ClaveAgenda = agenda.ClaveAgenda,
                        Rpe = rpe,
                        UsuarioReporta = usuarioReporta,
                        Correo = correo,
                        PdfRptUsuarioImpresora = nombreArchivoPdf,
                        Problematica = problematica,
                        Observaciones = observaciones,
                        FechaReporte = fechaReporteParsed,
                        FechaCaptura = DateTime.Now
                    };

                    _dbContext.ImpresoraMantenimientos.Add(impresoraMantenimiento);
                    await _dbContext.SaveChangesAsync();

                    await transaction.CommitAsync();

                    return Json(new
                    {
                        success = true,
                        message = $"Mantenimiento agendado correctamente para el {fechaReporteParsed.ToString("dd/MM/yyyy")}. Folio: {folioAtencion}"
                    });
                }
                catch (DbUpdateException ex)
                {
                    await transaction.RollbackAsync();
                    return Json(new { success = false, message = "Error al guardar en la base de datos: " + ex.InnerException?.Message });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    return Json(new { success = false, message = $"Error inesperado: {ex.Message}" });
                }
            }
        }

        private async Task<string> GuardarPdfRptUsuarioImpresora(IFormFile archivoPdf, string numSerieImpresora)
        {
            // Crear carpeta si no existe
            var uploadsPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "rptImpresoras");
            if (!Directory.Exists(uploadsPath))
            {
                Directory.CreateDirectory(uploadsPath);
            }

            // Validar extensión del archivo
            var extension = Path.GetExtension(archivoPdf.FileName).ToLower();
            if (extension != ".pdf")
            {
                throw new Exception("Solo se permiten archivos PDF");
            }

            // Generar un GUID corto (8 caracteres) para el hash
            var guidCorto = Guid.NewGuid().ToString().Substring(0, 8).ToUpper();

            // Limpiar el número de serie (remover caracteres no válidos para nombres de archivo)
            var numSerieLimpio = string.IsNullOrEmpty(numSerieImpresora)
                ? "SinSerie"
                : LimpiarCadenaParaArchivo(numSerieImpresora);

            // Crear el nombre del archivo con el formato: HASH_Rpt_NumSerie.pdf
            var nombreArchivo = $"{guidCorto}_Rpt_{numSerieLimpio}.pdf";

            // Verificar si ya existe un archivo con ese nombre (poco probable pero posible)
            var rutaCompleta = Path.Combine(uploadsPath, nombreArchivo);

            // USAR System.IO.File en lugar de solo File para evitar conflicto con ControllerBase.File
            if (System.IO.File.Exists(rutaCompleta))
            {
                // Si ya existe, añadir un sufijo numérico
                var contador = 1;
                string nuevoNombre;
                do
                {
                    nuevoNombre = $"{guidCorto}_Rpt_{numSerieLimpio}_{contador}.pdf";
                    rutaCompleta = Path.Combine(uploadsPath, nuevoNombre);
                    contador++;
                } while (System.IO.File.Exists(rutaCompleta));
                nombreArchivo = nuevoNombre;
            }

            // Guardar archivo
            using (var stream = new FileStream(rutaCompleta, FileMode.Create))
            {
                await archivoPdf.CopyToAsync(stream);
            }

            return nombreArchivo;
        }

        // Método auxiliar para limpiar cadenas para nombres de archivo
        private string LimpiarCadenaParaArchivo(string texto)
        {
            if (string.IsNullOrEmpty(texto))
                return "SinNombre";

            // Remover caracteres no válidos para nombres de archivo
            var caracteresInvalidos = Path.GetInvalidFileNameChars();
            var textoLimpio = new string(texto
                .Where(c => !caracteresInvalidos.Contains(c))
                .ToArray());

            // Reemplazar espacios por guiones bajos
            textoLimpio = textoLimpio.Replace(' ', '_');

            // Limitar la longitud (max 50 caracteres)
            if (textoLimpio.Length > 50)
            {
                textoLimpio = textoLimpio.Substring(0, 50);
            }

            // Si después de limpiar queda vacío, usar valor por defecto
            if (string.IsNullOrEmpty(textoLimpio))
            {
                textoLimpio = "SinSerie";
            }

            return textoLimpio;
        }

        public IActionResult Index()
        {
            return View();
        }
    }
}