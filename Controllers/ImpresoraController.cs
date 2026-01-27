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
        private readonly IWebHostEnvironment _hostEnvironment;

        public ImpresoraController(MantenimientosTIContext context, IWebHostEnvironment hostEnvironment)
        {
            _dbContext = context;
            _hostEnvironment = hostEnvironment;
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
                    .Include(i => i.Equipo)
                    .Where(i => i.Equipo.ClaveDivision == division &&
                               i.Equipo.ClaveZona == zona &&
                               i.Equipo.ClaveAgencia == agencia &&
                               i.Equipo.ClaveCentro == centro)
                    .Select(i => new
                    {
                        value = i.NumActFijo,
                        text = $"{i.NumActFijo} - {i.Modelo} - {i.NumSerie}",
                        division = i.Equipo.ClaveDivision,
                        zona = i.Equipo.ClaveZona,
                        agencia = i.Equipo.ClaveAgencia,
                        centro = i.Equipo.ClaveCentro
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
        public async Task<IActionResult> AgendarMantenimiento([FromForm] MantenimientoImpresoraViewModel model)
        {
            try
            {
                // Validaciones básicas
                if (string.IsNullOrEmpty(model.NumActFijo))
                {
                    return Json(new { success = false, message = "Debe seleccionar una impresora válida" });
                }

                if (string.IsNullOrEmpty(model.FolioAtencion))
                {
                    return Json(new { success = false, message = "El folio de atención es requerido" });
                }

                if (model.FechaReporte == default(DateTime))
                {
                    return Json(new { success = false, message = "La fecha de reporte es requerida" });
                }

                if (string.IsNullOrEmpty(model.Problematica))
                {
                    return Json(new { success = false, message = "La problemática es requerida" });
                }

                if (string.IsNullOrEmpty(model.Rpe))
                {
                    return Json(new { success = false, message = "El RPE es requerido" });
                }

                if (string.IsNullOrEmpty(model.UsuarioReporta))
                {
                    return Json(new { success = false, message = "El usuario que reporta es requerido" });
                }

                if (string.IsNullOrEmpty(model.Correo))
                {
                    return Json(new { success = false, message = "El correo es requerido" });
                }

                // Obtener el equipo para sacar la ubicación
                var equipo = await _dbContext.Equipos
                    .FirstOrDefaultAsync(e => e.NumActFijo == model.NumActFijo);

                if (equipo == null)
                {
                    return Json(new { success = false, message = "No se encontró el equipo seleccionado" });
                }

                // Procesar archivo PDF si existe
                string? rutaArchivo = null;
                if (model.SoporteDocumental != null && model.SoporteDocumental.Length > 0)
                {
                    // Validar que sea PDF
                    var extension = Path.GetExtension(model.SoporteDocumental.FileName).ToLower();
                    if (extension != ".pdf")
                    {
                        return Json(new { success = false, message = "Solo se permiten archivos PDF" });
                    }

                    // Validar tamaño (5MB)
                    if (model.SoporteDocumental.Length > 5 * 1024 * 1024)
                    {
                        return Json(new { success = false, message = "El archivo no debe exceder los 5MB" });
                    }

                    // Guardar el archivo
                    var uploadsFolder = Path.Combine(_hostEnvironment.WebRootPath, "uploads", "impresoras");
                    if (!Directory.Exists(uploadsFolder))
                    {
                        Directory.CreateDirectory(uploadsFolder);
                    }

                    var uniqueFileName = $"{Guid.NewGuid()}_{model.SoporteDocumental.FileName}";
                    var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await model.SoporteDocumental.CopyToAsync(fileStream);
                    }

                    rutaArchivo = $"/uploads/impresoras/{uniqueFileName}";
                }

                // Obtener el RPE del usuario logueado
                string? rpeUsuarioLogueado = HttpContext.Session.GetString("Rpe");

                using (var transaction = await _dbContext.Database.BeginTransactionAsync())
                {
                    try
                    {
                        // 1. Insertar en tabla Agenda
                        var agenda = new Agendum
                        {
                            NumActFijo = model.NumActFijo,
                            ClaveTipoMtto = "C", // Correctivo para impresoras
                            FechaProgramada = DateOnly.FromDateTime(model.FechaReporte),
                            Estatus = "PENDIENTE"
                        };

                        _dbContext.Agenda.Add(agenda);
                        await _dbContext.SaveChangesAsync();

                        // Obtener el ClaveAgenda generado
                        int claveAgendaGenerada = agenda.ClaveAgenda;

                        // 2. Insertar en tabla ImpresoraMantenimiento
                        var impresoraMantenimiento = new ImpresoraMantenimiento
                        {
                            FolioAtencion = model.FolioAtencion,
                            ClaveAgenda = claveAgendaGenerada,
                            NumActFijo = model.NumActFijo,
                            FechaProgramada = model.FechaReporte,
                            ClaveTipoMtto = "C", // Correctivo
                            UsuarioReporta = model.UsuarioReporta,
                            Correo = model.Correo,
                            PdfQueja = rutaArchivo ?? "", // Usar string vacío si no hay archivo
                            Problematica = model.Problematica,
                            Observaciones = model.Observaciones ?? "",
                            FechaReporte = model.FechaReporte,
                            FechaGeneracion = DateTime.Now,
                            FechaCaptura = DateTime.Now,
                            Rpe = model.Rpe // RPE del usuario que reporta
                        };

                        _dbContext.ImpresoraMantenimientos.Add(impresoraMantenimiento);
                        await _dbContext.SaveChangesAsync();

                        await transaction.CommitAsync();

                        return Json(new
                        {
                            success = true,
                            message = "Mantenimiento agendado exitosamente",
                            folioAtencion = model.FolioAtencion,
                            claveAgenda = claveAgendaGenerada
                        });
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync();

                        // Eliminar el archivo si se subió
                        if (!string.IsNullOrEmpty(rutaArchivo))
                        {
                            var fullPath = Path.Combine(_hostEnvironment.WebRootPath, rutaArchivo.TrimStart('/'));
                            if (System.IO.File.Exists(fullPath))
                            {
                                System.IO.File.Delete(fullPath);
                            }
                        }

                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error al agendar mantenimiento: {ex.Message}" });
            }
        }

        public IActionResult Index()
        {
            return View();
        }
    }

    // ViewModel para recibir los datos del formulario
    public class MantenimientoImpresoraViewModel
    {
        public string NumActFijo { get; set; }
        public string FolioAtencion { get; set; }
        public DateTime FechaReporte { get; set; }
        public string Problematica { get; set; }
        public string Rpe { get; set; }
        public string UsuarioReporta { get; set; }
        public string Correo { get; set; }
        public IFormFile? SoporteDocumental { get; set; }
        public string? Observaciones { get; set; }
    }
}