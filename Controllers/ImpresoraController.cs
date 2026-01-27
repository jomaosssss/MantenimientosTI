using Microsoft.AspNetCore.Mvc;
using MantenimientosTI.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;

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
            [FromForm] IFormFile pdfQueja)
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

                    // Verificar si ya existe un mantenimiento pendiente para esta impresora
                    var mantenimientoPendiente = await _dbContext.Agenda
                        .AnyAsync(a => a.NumActFijo == numActFijo && a.Estatus == "PENDIENTE");

                    if (mantenimientoPendiente)
                    {
                        return Json(new { success = false, message = "Ya existe un mantenimiento pendiente para esta impresora" });
                    }

                    // Verificar si ya existe el folio de atención
                    var folioExistente = await _dbContext.ImpresoraMantenimientos
                        .AnyAsync(i => i.FolioAtencion == folioAtencion);

                    if (folioExistente)
                    {
                        return Json(new { success = false, message = "El folio de atención ya existe" });
                    }

                    // 1. Crear registro en Agenda (Agendum)
                    var agenda = new Agendum
                    {
                        NumActFijo = numActFijo,
                        ClaveTipoMtto = "C", // Siempre 'C' para correctivo
                        FechaProgramada = DateOnly.Parse(fechaReporte),
                        Estatus = "PENDIENTE"
                    };

                    _dbContext.Agenda.Add(agenda);
                    await _dbContext.SaveChangesAsync(); // Guardar para obtener ClaveAgenda

                    // 2. Guardar archivo PDF en carpeta "pdf_quejas"
                    var nombreArchivoPdf = await GuardarPdfQueja(pdfQueja);

                    // 3. Crear registro en ImpresoraMantenimiento
                    var impresoraMantenimiento = new ImpresoraMantenimiento
                    {
                        FolioAtencion = folioAtencion,
                        ClaveAgenda = agenda.ClaveAgenda, // Clave generada automáticamente
                        Rpe = rpe,
                        UsuarioReporta = usuarioReporta,
                        Correo = correo,
                        PdfQueja = nombreArchivoPdf,
                        Problematica = problematica,
                        Observaciones = observaciones,
                        FechaReporte = DateOnly.Parse(fechaReporte),
                        FechaCaptura = DateTime.Now
                    };

                    _dbContext.ImpresoraMantenimientos.Add(impresoraMantenimiento);
                    await _dbContext.SaveChangesAsync();

                    await transaction.CommitAsync();

                    return Json(new
                    {
                        success = true,
                        message = "Mantenimiento agendado correctamente. Folio: " + folioAtencion
                    });
                }
                catch (FormatException ex)
                {
                    await transaction.RollbackAsync();
                    return Json(new { success = false, message = "Formato de fecha inválido. Use formato: AAAA-MM-DD" });
                }
                catch (DbUpdateException ex)
                {
                    await transaction.RollbackAsync();
                    return Json(new { success = false, message = "Error al guardar en la base de datos: " + ex.InnerException?.Message });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    return Json(new { success = false, message = $"Error: {ex.Message}" });
                }
            }
        }

        private async Task<string> GuardarPdfQueja(IFormFile archivoPdf)
        {
            // Crear carpeta si no existe
            var uploadsPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "pdf_quejas");
            if (!Directory.Exists(uploadsPath))
            {
                Directory.CreateDirectory(uploadsPath);
            }

            // Generar nombre único para el archivo
            var extension = Path.GetExtension(archivoPdf.FileName).ToLower();
            if (extension != ".pdf")
            {
                throw new Exception("Solo se permiten archivos PDF");
            }

            var nombreArchivo = $"{Guid.NewGuid()}_{Path.GetFileName(archivoPdf.FileName)}";
            var rutaCompleta = Path.Combine(uploadsPath, nombreArchivo);

            // Guardar archivo
            using (var stream = new FileStream(rutaCompleta, FileMode.Create))
            {
                await archivoPdf.CopyToAsync(stream);
            }

            return nombreArchivo;
        }

        public IActionResult Index()
        {
            return View();
        }
    }
}