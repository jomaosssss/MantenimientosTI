using ClosedXML.Excel;
using ClosedXML.Excel;
using MantenimientosTI.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using System.Globalization;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using MantenimientosTI.Services;

namespace MantenimientosTI.Controllers
{
    public class RepositorioController : Controller
    {
        private readonly MantenimientosTIContext _dbocontext;
        private readonly BitacoraService _bitacora;


        public RepositorioController(MantenimientosTIContext context, BitacoraService bitacora)
        {
            _dbocontext = context;
            _bitacora = bitacora;
        }

        [Authorize(Roles = "ADMINISTRADOR,TÉCNICO DE ZONA")]
        public IActionResult TerminarMantenimiento()
        {
            return View();
        }

        [Authorize(Roles = "ADMINISTRADOR,TÉCNICO DE ZONA, CONSULTOR")]
        public IActionResult Repositorio()
        {
            return View();
        }

        [Authorize(Roles = "ADMINISTRADOR,TÉCNICO DE ZONA")]
        public IActionResult InventarioEquipos()
        {
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> BuscarPorNumeroOrden(int claveAgenda)
        {
            try
            {
                var agendaItem = await _dbocontext.Agenda
                    .Include(a => a.NumActFijoNavigation)
                        .ThenInclude(e => e.CatCentro)
                            .ThenInclude(c => c.CatAgencium)
                                .ThenInclude(a => a.CatZona)
                    .Include(a => a.ClaveTipoMttoNavigation)
                    .FirstOrDefaultAsync(a => a.ClaveAgenda == claveAgenda);

                if (agendaItem == null)
                {
                    return Json(new { success = false, message = "No se encontró el número de orden" });
                }

                if (agendaItem.Estatus != "PENDIENTE")
                {
                    return Json(new
                    {
                        success = false,
                        message = $"El mantenimiento ya está {agendaItem.Estatus}",
                        puedeTerminar = false
                    });
                }

                // Determinar el tipo de equipo y obtener número de cajero si es CFEMÁTICO
                string tipoEquipo = "CFEMÁTICO";
                string numCajero = "N/A";

                // PRIMERO: Buscar en Impresoras (NUEVO)
                var impresora = await _dbocontext.Impresoras
                    .Include(i => i.ClaveTipoEquipoNavigation)
                    .FirstOrDefaultAsync(i => i.NumActFijo == agendaItem.NumActFijo);

                // SEGUNDO: Buscar en Equipos AC
                var equipoAC = await _dbocontext.EquipoAcs
                    .Include(e => e.ClaveTipoEquipoNavigation)
                    .FirstOrDefaultAsync(e => e.NumActFijo == agendaItem.NumActFijo);

                // TERCERO: Buscar en Equipos de Computo
                var equipoComputo = await _dbocontext.EquipoComputos
                    .Include(e => e.ClaveTipoEquipoNavigation)
                    .FirstOrDefaultAsync(e => e.NumActFijo == agendaItem.NumActFijo);

                // CUARTO: Buscar en EquipoCfematico si no es Impresora, AC ni Computo
                if (impresora == null && equipoAC == null && equipoComputo == null)
                {
                    var equipoCfematico = await _dbocontext.EquipoCfematicos
                        .FirstOrDefaultAsync(e => e.NumActFijo == agendaItem.NumActFijo);

                    if (equipoCfematico != null)
                    {
                        tipoEquipo = "CFEMÁTICO";
                        numCajero = equipoCfematico.NumCajero ?? "N/A";
                    }
                }
                else if (impresora != null) // PRIORIDAD 1: Impresora
                {
                    tipoEquipo = impresora.ClaveTipoEquipoNavigation?.NombreTipoEquipo ?? "IMPRESORA";
                }
                else if (equipoAC != null) // PRIORIDAD 2: Equipo AC
                {
                    tipoEquipo = equipoAC.ClaveTipoEquipoNavigation?.NombreTipoEquipo ?? "Equipo AC";
                }
                else if (equipoComputo != null) // PRIORIDAD 3: Equipo de Cómputo
                {
                    tipoEquipo = equipoComputo.ClaveTipoEquipoNavigation?.NombreTipoEquipo ?? "Equipo de Cómputo";
                }

                var centro = agendaItem.NumActFijoNavigation?.CatCentro;
                var agencia = centro?.CatAgencium;
                var zona = agencia?.CatZona;

                var rpeUsuario = HttpContext.Session.GetString("Rpe");
                if (string.IsNullOrEmpty(rpeUsuario))
                {
                    return Json(new
                    {
                        success = false,
                        message = "No se encontró el RPE del usuario en sesión"
                    });
                }

                return Json(new
                {
                    success = true,
                    puedeTerminar = true,
                    esCorrectivo = agendaItem.ClaveTipoMtto == "C",
                    data = new
                    {
                        numOrden = agendaItem.ClaveAgenda,
                        numActFijo = agendaItem.NumActFijo,
                        fechaProgramada = agendaItem.FechaProgramada.ToString("dd/MM/yyyy"),
                        tipoMantenimiento = agendaItem.ClaveTipoMttoNavigation?.NombreTipoM,
                        tipoEquipo = tipoEquipo,
                        zona = zona?.NombreZona ?? "No especificado",
                        agencia = agencia?.NombreAgencia ?? "No especificado",
                        centro = centro?.NombreCentro ?? "No especificado",
                        estatus = agendaItem.Estatus,
                        numCajero = numCajero
                    }
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [Authorize(Roles = "ADMINISTRADOR,TÉCNICO DE ZONA")]
        [HttpPost]
        public async Task<IActionResult> TerminarMantenimiento(
            [FromForm] int numOrden,
            [FromForm] string fechaAtencion,
            [FromForm] string problemas,
            [FromForm] string diagnostico,
            [FromForm] string observaciones,
            [FromForm] IFormFile archivoPdf,
            [FromForm] IFormFile? fotoAntes,
            [FromForm] IFormFile? fotoDurante,
            [FromForm] IFormFile? fotoDespues)
        {
            using (var transaction = await _dbocontext.Database.BeginTransactionAsync())
            {
                try
                {
                    if (!DateOnly.TryParseExact(fechaAtencion, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly fechaAtencionParsed))
                    {
                        return Json(new { success = false, message = "Formato de fecha inválida" });
                    }

                    var agendaItem = await _dbocontext.Agenda
                        .FirstOrDefaultAsync(a => a.ClaveAgenda == numOrden);

                    if (agendaItem == null)
                        return Json(new { success = false, message = "Registro no encontrado en la Agenda" });

                    var rpe = HttpContext.Session.GetString("Rpe");
                    if (string.IsNullOrEmpty(rpe))
                        return Json(new { success = false, message = "Sesión inválida, RPE no encontrado" });

                    var mantenimiento = new Mantenimiento
                    {
                        NumOrden = agendaItem.ClaveAgenda,
                        ClaveAgenda = agendaItem.ClaveAgenda,
                        NumActFijo = agendaItem.NumActFijo,
                        ClaveTipoMtto = agendaItem.ClaveTipoMtto,
                        Rpe = rpe,
                        Problemas = problemas ?? string.Empty,
                        Diagnostico = diagnostico ?? string.Empty,
                        Observaciones = observaciones ?? string.Empty,
                        FechaInsercion = DateTime.Now,
                        FechaAtencion = fechaAtencionParsed
                    };

                    if (archivoPdf != null && archivoPdf.Length > 0)
                    {
                        if (archivoPdf.Length > 5 * 1024 * 1024)
                            return Json(new { success = false, message = "La hoja de servicio no debe exceder los 5MB" });

                        if (Path.GetExtension(archivoPdf.FileName).ToLower() != ".pdf")
                            return Json(new { success = false, message = "Solo se permiten archivos PDF" });

                        mantenimiento.EvidenciaHojaServicio = await GuardarArchivo(archivoPdf);
                    }
                    else
                    {
                        return Json(new { success = false, message = "La hoja de servicio es obligatoria" });
                    }

                    _dbocontext.Mantenimientos.Add(mantenimiento);
                    await _dbocontext.SaveChangesAsync();

                    var foto = new Foto
                    {
                        NumOrden = mantenimiento.NumOrden,
                        FechaHora = DateTime.Now
                    };

                    bool seAgregoAlgunaFoto = false;

                    if (fotoAntes != null && fotoAntes.Length > 0)
                    {
                        if (fotoAntes.Length > 5 * 1024 * 1024)
                            return Json(new { success = false, message = "La foto 'Antes' no debe exceder los 5MB" });

                        foto.FotoAntes = await ProcesarImagen(fotoAntes);
                        seAgregoAlgunaFoto = true;
                    }

                    if (fotoDurante != null && fotoDurante.Length > 0)
                    {
                        if (fotoDurante.Length > 5 * 1024 * 1024)
                            return Json(new { success = false, message = "La foto 'Durante' no debe exceder los 5MB" });

                        foto.FotoDurante = await ProcesarImagen(fotoDurante);
                        seAgregoAlgunaFoto = true;
                    }

                    if (fotoDespues != null && fotoDespues.Length > 0)
                    {
                        if (fotoDespues.Length > 5 * 1024 * 1024)
                            return Json(new { success = false, message = "La foto 'Después' no debe exceder los 5MB" });

                        foto.FotoDespues = await ProcesarImagen(fotoDespues);
                        seAgregoAlgunaFoto = true;
                    }

                    // Solo se guarda la entidad Foto si se subió al menos una imagen
                    if (seAgregoAlgunaFoto)
                    {
                        _dbocontext.Fotos.Add(foto);
                    }

                    // Actualizar agenda
                    agendaItem.Estatus = "TERMINADO";

                    // ✅ REGISTRO EN BITÁCORA - AGREGADO
                    var usuario = HttpContext.Session.GetString("NombreUsuario") ?? "Usuario no identificado";
                    var rol = HttpContext.Session.GetString("NombreRol") ?? "N/A";
                    var zona = HttpContext.Session.GetString("NombreZona") ?? "N/A";
                    var centro = HttpContext.Session.GetString("ClaveDivision") ?? "N/A";

                    await _bitacora.RegistrarTerminacionMantenimientoAsync(
                        usuario: usuario,
                        rpe: rpe,
                        rol: rol,
                        zona: zona,
                        centro: centro,
                        claveAgenda: numOrden.ToString(),
                        equipo: agendaItem.NumActFijo
                    );

                    await _dbocontext.SaveChangesAsync();
                    await transaction.CommitAsync();

                    return Json(new
                    {
                        success = true,
                        message = "Mantenimiento Terminado Correctamente."
                    });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    // Devuelve un mensaje de error más detallado para depuración
                    return Json(new
                    {
                        success = false,
                        message = "Error inesperado al procesar la solicitud.",
                        error = ex.Message
                    });
                }
            }
        }

        private async Task<string> ProcesarImagen(IFormFile imagen)
        {
            using (var ms = new MemoryStream())
            {
                await imagen.CopyToAsync(ms);
                return Convert.ToBase64String(ms.ToArray());
            }
        }

        private async Task<string> GuardarArchivo(IFormFile archivo)
        {
            var uploadsPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
            if (!Directory.Exists(uploadsPath))
            {
                Directory.CreateDirectory(uploadsPath);
            }

            var nombreArchivo = $"{Guid.NewGuid()}_{Path.GetFileName(archivo.FileName)}";
            var rutaCompleta = Path.Combine(uploadsPath, nombreArchivo);

            using (var stream = new FileStream(rutaCompleta, FileMode.Create))
            {
                await archivo.CopyToAsync(stream);
            }

            return nombreArchivo;
        }

        private async Task<string> ConvertirImagenAHex(IFormFile imagen)
        {
            using (var memoryStream = new MemoryStream())
            {
                await imagen.CopyToAsync(memoryStream);
                byte[] imageBytes = memoryStream.ToArray();

                // Convertir a hexadecimal y asegurar formato válido
                var hexString = BitConverter.ToString(imageBytes).Replace("-", "");
                if (!hexString.StartsWith("0x"))
                {
                    hexString = "0x" + hexString;
                }

                return hexString;
            }
        }

        [HttpGet]
        public async Task<IActionResult> ObtenerMantenimientos(int? mes = null, int? anio = null)
        {
            try
            {
                // Si no se especifican mes y año, usar el mes actual
                if (!mes.HasValue || !anio.HasValue)
                {
                    mes = DateTime.Now.Month;
                    anio = DateTime.Now.Year;
                }

                // Obtener el rol del usuario desde la sesión
                var claveRolUsuario = HttpContext.Session.GetInt32("Rol");
                var esTecnico = claveRolUsuario == 2;

                // Calcular el rango de fechas para el mes seleccionado
                var fechaInicio = new DateTime(anio.Value, mes.Value, 1);
                var fechaFin = fechaInicio.AddMonths(1).AddDays(-1);

                // Convertir a DateOnly para comparar con FechaProgramada
                var fechaInicioDateOnly = DateOnly.FromDateTime(fechaInicio);
                var fechaFinDateOnly = DateOnly.FromDateTime(fechaFin);

                // Consulta base para mantenimientos terminados
                var query = _dbocontext.Mantenimientos
                    .Include(m => m.Agendum)
                        .ThenInclude(a => a.NumActFijoNavigation)
                            .ThenInclude(e => e.CatCentro)
                                .ThenInclude(c => c.CatAgencium)
                                    .ThenInclude(a => a.CatZona)
                    .Where(m => m.Agendum.Estatus == "TERMINADO");

                // Si no es administrador, filtrar por zona
                if (esTecnico)
                {
                    var claveZonaUsuario = HttpContext.Session.GetString("ClaveZona");
                    if (string.IsNullOrEmpty(claveZonaUsuario))
                    {
                        return Json(new { success = false, message = "No se pudo determinar la zona del usuario" });
                    }
                    query = query.Where(m => m.Agendum.NumActFijoNavigation.ClaveZona == claveZonaUsuario);
                }

                // ✅ CAMBIO: Filtrar por FECHA PROGRAMADA en lugar de FechaInsercion
                query = query.Where(m => m.Agendum.FechaProgramada >= fechaInicioDateOnly &&
                                        m.Agendum.FechaProgramada <= fechaFinDateOnly);

                // CONSULTA PRINCIPAL OPTIMIZADA
                var mantenimientosData = await query
                    .OrderByDescending(m => m.Agendum.FechaProgramada) // ✅ CAMBIO: Ordenar por FechaProgramada
                    .Select(m => new
                    {
                        Mantenimiento = m,
                        Agenda = m.Agendum, // ✅ Incluir Agenda para acceder a FechaProgramada
                        Centro = m.Agendum.NumActFijoNavigation.CatCentro,
                        Agencia = m.Agendum.NumActFijoNavigation.CatCentro.CatAgencium,
                        Zona = m.Agendum.NumActFijoNavigation.CatCentro.CatAgencium.CatZona
                    })
                    .ToListAsync();

                // Si no hay datos, retornar vacío
                if (!mantenimientosData.Any())
                {
                    return Json(new
                    {
                        Cfematicos = new List<object>(),
                        AtencionClientes = new List<object>(),
                        EquiposComputo = new List<object>()
                    });
                }

                // OBTENER TODOS LOS NUM_ACT_FIJO Y NUM_ORDEN DE UNA VEZ
                var numActFijos = mantenimientosData.Select(x => x.Mantenimiento.NumActFijo).Distinct().ToList();
                var numOrdenes = mantenimientosData.Select(x => x.Mantenimiento.NumOrden).Distinct().ToList();

                // CONSULTAS MASIVAS EN LUGAR DE INDIVIDUALES

                // 1. Equipos AC
                var equiposAC = await _dbocontext.EquipoAcs
                    .Include(e => e.ClaveTipoEquipoNavigation)
                    .Where(e => numActFijos.Contains(e.NumActFijo))
                    .ToDictionaryAsync(e => e.NumActFijo);

                // 2. Equipos Computo
                var equiposComputo = await _dbocontext.EquipoComputos
                    .Include(e => e.ClaveTipoEquipoNavigation)
                    .Where(e => numActFijos.Contains(e.NumActFijo))
                    .ToDictionaryAsync(e => e.NumActFijo);

                // 3. Equipos CFEmático
                var equiposCfematico = await _dbocontext.EquipoCfematicos
                    .Where(e => numActFijos.Contains(e.NumActFijo))
                    .ToDictionaryAsync(e => e.NumActFijo);

                // 4. Impresoras
                var impresoras = await _dbocontext.Impresoras
                    .Include(i => i.ClaveTipoEquipoNavigation)
                    .Where(i => numActFijos.Contains(i.NumActFijo))
                    .ToDictionaryAsync(i => i.NumActFijo);

                // 4. Verificar fotos masivamente
                var ordenesConFotos = await _dbocontext.Fotos
                    .Where(f => numOrdenes.Contains(f.NumOrden))
                    .Select(f => f.NumOrden)
                    .Distinct()
                    .ToHashSetAsync();

                // Listas para cada categoría
                var cfematicos = new List<object>();
                var atencionClientes = new List<object>();
                var equiposComputoList = new List<object>();

                // PROCESAMIENTO OPTIMIZADO - Usar diccionarios en memoria
                foreach (var item in mantenimientosData)
                {
                    var m = item.Mantenimiento;
                    var agenda = item.Agenda; // ✅ Usar la agenda que incluye FechaProgramada
                    var centro = item.Centro;
                    var agencia = item.Agencia;
                    var zona = item.Zona;

                    // Verificar fotos usando el HashSet (muy rápido)
                    var tieneFotos = ordenesConFotos.Contains(m.NumOrden);

                    // Determinar tipo de equipo usando diccionarios (muy rápido)
                    equiposAC.TryGetValue(m.NumActFijo, out var equipoAc);
                    equiposComputo.TryGetValue(m.NumActFijo, out var equipoComputoDict);
                    equiposCfematico.TryGetValue(m.NumActFijo, out var equipoCfematico);

                    // Crear el item para la tabla
                    var itemTabla = new
                    {
                        m.NumOrden,
                        FechaProgramada = agenda.FechaProgramada.ToString("dd/MM/yyyy"), // ✅ Usar fecha de la agenda
                        FechaAtencion = m.FechaAtencion.ToString("dd/MM/yyyy"),
                        FechaTerminada = m.FechaInsercion.ToString("dd/MM/yyyy HH:mm"),
                        m.EvidenciaHojaServicio,
                        TieneFotos = tieneFotos,
                        Rpe = m.Rpe,
                        Zona = zona?.NombreZona ?? "No especificado",
                        Agencia = agencia?.NombreAgencia ?? "No especificado",
                        Centro = centro?.NombreCentro ?? "No especificado",
                        NumCajero = equipoCfematico?.NumCajero ?? "N/A",
                        TipoEquipo = equipoAc?.ClaveTipoEquipoNavigation?.NombreTipoEquipo ??
                                    equipoComputoDict?.ClaveTipoEquipoNavigation?.NombreTipoEquipo ??
                                    "CFEmático"
                    };

                    // Clasificación usando los diccionarios
                    if (equipoAc != null)
                    {
                        atencionClientes.Add(itemTabla);
                    }
                    else if (equipoComputoDict != null)
                    {
                        equiposComputoList.Add(itemTabla);
                    }
                    else
                    {
                        cfematicos.Add(itemTabla);
                    }
                }

                return Json(new
                {
                    Cfematicos = cfematicos,
                    AtencionClientes = atencionClientes,
                    EquiposComputo = equiposComputoList
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> ObtenerImagen(int numOrden, string tipo)
        {
            try
            {
                var foto = await _dbocontext.Fotos
                    .AsNoTracking()
                    .Where(f => f.NumOrden == numOrden)
                    .Select(f => new {
                        Imagen = tipo == "antes" ? f.FotoAntes :
                                 tipo == "durante" ? f.FotoDurante :
                                 tipo == "despues" ? f.FotoDespues : null
                    })
                    .FirstOrDefaultAsync();

                if (foto?.Imagen == null)
                    return NotFound();

                // ✅ CONVERSIÓN MÁS RÁPIDA
                byte[] imageBytes = ConvertirBase64Rapido(foto.Imagen);

                return File(imageBytes, "image/jpeg");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error: {ex.Message}");
            }
        }

        // ✅ MÉTODO OPTIMIZADO PARA CONVERSIÓN RÁPIDA
        private byte[] ConvertirBase64Rapido(string base64String)
        {
            // Si tiene prefijo data:image, saltarlo directamente
            if (base64String.StartsWith("data:image"))
            {
                int commaIndex = base64String.IndexOf(',');
                if (commaIndex >= 0)
                {
                    base64String = base64String.Substring(commaIndex + 1);
                }
            }

            // ✅ MEJORA: Usar Span<T> para mejor performance
            return Convert.FromBase64String(base64String);
        }

        // ✅ NUEVO: Método para redimensionar imágenes
        private byte[] RedimensionarImagen(byte[] imageBytes, int maxWidth, int maxHeight, int calidad)
        {
            using (var memoryStream = new MemoryStream(imageBytes))
            using (var image = Image.Load(memoryStream))
            {
                // Calcular nuevo tamaño manteniendo aspecto
                var options = new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(maxWidth, maxHeight)
                };

                image.Mutate(x => x.Resize(options));

                // Guardar con compresión
                using (var outputStream = new MemoryStream())
                {
                    image.Save(outputStream, new JpegEncoder
                    {
                        Quality = calidad
                    });
                    return outputStream.ToArray();
                }
            }
        }

        [HttpGet]
        public IActionResult DescargarHojaServicio(int numOrden)
        {
            try
            {
                var mantenimiento = _dbocontext.Mantenimientos
                    .FirstOrDefault(m => m.NumOrden == numOrden);

                if (mantenimiento == null || string.IsNullOrEmpty(mantenimiento.EvidenciaHojaServicio))
                {
                    return NotFound();
                }

                var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", mantenimiento.EvidenciaHojaServicio);

                if (!System.IO.File.Exists(filePath))
                {
                    return NotFound();
                }

                var fileStream = System.IO.File.OpenRead(filePath);
                return File(fileStream, "application/pdf", $"HojaServicio_{numOrden}.pdf");
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        [HttpGet]
        public async Task<IActionResult> ObtenerDetallesEquipo(int numOrden)
        {
            try
            {
                // Obtener el rol del usuario desde la sesión
                var claveRolUsuario = HttpContext.Session.GetInt32("Rol");
                var esAdministrador = claveRolUsuario == 1;

                // Consulta base
                var query = _dbocontext.Mantenimientos
                    .Include(m => m.Agendum)
                        .ThenInclude(a => a.NumActFijoNavigation)
                            .ThenInclude(e => e.CatCentro)
                                .ThenInclude(c => c.CatAgencium)
                                    .ThenInclude(a => a.CatZona)
                    .Include(m => m.Agendum)
                        .ThenInclude(a => a.ClaveTipoMttoNavigation)
                    .Include(m => m.RpeNavigation)
                    .Where(m => m.NumOrden == numOrden);

                // Si no es administrador, filtrar por zona
                if (!esAdministrador)
                {
                    var claveZonaUsuario = HttpContext.Session.GetString("ClaveZona");
                    if (string.IsNullOrEmpty(claveZonaUsuario))
                    {
                        return Content("<div class='alert alert-danger'>No se pudo determinar la zona del usuario</div>");
                    }
                    query = query.Where(m => m.Agendum.NumActFijoNavigation.ClaveZona == claveZonaUsuario);
                }

                var mantenimiento = await query.FirstOrDefaultAsync();

                if (mantenimiento == null)
                {
                    return Content("<div class='alert alert-danger'>No se encontró el mantenimiento o no tienes permisos para verlo</div>");
                }

                // Determinar tipo de equipo
                string tipoEquipo = "CFEMÁTICO";
                string numCajero = "N/A";

                // NUEVO: Buscar en Impresora
                var impresora = await _dbocontext.Impresoras
                    .Include(i => i.ClaveTipoEquipoNavigation)
                    .FirstOrDefaultAsync(i => i.NumActFijo == mantenimiento.NumActFijo);

                var equipoAC = await _dbocontext.EquipoAcs
                    .Include(e => e.ClaveTipoEquipoNavigation)
                    .FirstOrDefaultAsync(e => e.NumActFijo == mantenimiento.NumActFijo);

                var equipoComputo = await _dbocontext.EquipoComputos
                    .Include(e => e.ClaveTipoEquipoNavigation)
                    .FirstOrDefaultAsync(e => e.NumActFijo == mantenimiento.NumActFijo);

                if (impresora == null && equipoAC == null && equipoComputo == null)
                {
                    var equipoCfematico = await _dbocontext.EquipoCfematicos
                        .FirstOrDefaultAsync(e => e.NumActFijo == mantenimiento.NumActFijo);

                    if (equipoCfematico != null)
                    {
                        tipoEquipo = "CFEMÁTICO";
                        numCajero = equipoCfematico.NumCajero ?? "N/A";
                    }
                }
                else if (impresora != null) // PRIORIDAD 1: Impresora
                {
                    tipoEquipo = impresora.ClaveTipoEquipoNavigation?.NombreTipoEquipo ?? "IMPRESORA";
                }
                else if (equipoAC != null) // PRIORIDAD 2: Equipo AC
                {
                    tipoEquipo = equipoAC.ClaveTipoEquipoNavigation?.NombreTipoEquipo ?? "Equipo AC";
                }
                else if (equipoComputo != null) // PRIORIDAD 3: Equipo de Cómputo
                {
                    tipoEquipo = equipoComputo.ClaveTipoEquipoNavigation?.NombreTipoEquipo ?? "Equipo de Cómputo";
                }

                var centro = mantenimiento.Agendum.NumActFijoNavigation?.CatCentro;
                var agencia = centro?.CatAgencium;
                var zona = agencia?.CatZona;

                var fechaProgramada = mantenimiento.Agendum?.FechaProgramada.ToString("dd/MM/yyyy") ?? "No especificada";
                var nombreUsuario = $"{mantenimiento.RpeNavigation?.Nombre ?? ""} {mantenimiento.RpeNavigation?.ApellidoP ?? ""} {mantenimiento.RpeNavigation?.ApellidoM ?? ""}".Trim();

                // Construir el HTML con los detalles
                var htmlInfoEquipo = @"
                <ul class='list-group list-group-flush'>";

                // Mostrar número de cajero primero si es CFEMÁTICO y tiene valor
                if (tipoEquipo == "CFEMÁTICO" && numCajero != "N/A")
                {
                    htmlInfoEquipo += $@"<li class='list-group-item'><strong>Número de Cajero:</strong> {numCajero}</li>";
                }

                htmlInfoEquipo += $@"
                    <li class='list-group-item'><strong>Número de Activo Fijo:</strong> {mantenimiento.NumActFijo}</li>
                    <li class='list-group-item'><strong>Tipo de Equipo:</strong> {tipoEquipo}</li>
                    <li class='list-group-item'><strong>Zona:</strong> {zona?.NombreZona ?? "No especificado"}</li>
                    <li class='list-group-item'><strong>Agencia:</strong> {agencia?.NombreAgencia ?? "No especificado"}</li>
                    <li class='list-group-item'><strong>Centro:</strong> {centro?.NombreCentro ?? "No especificado"}</li>
                </ul>";

                var html = $@"
                <div class='row'>
                    <div class='col-md-6'>
                        <h5>Información del Equipo</h5>
                        {htmlInfoEquipo}
                    </div>
                    <div class='col-md-6'>
                        <h5>Información del Mantenimiento</h5>
                        <ul class='list-group list-group-flush'>
                            <li class='list-group-item'><strong>Número de Orden:</strong> {mantenimiento.NumOrden}</li>
                            <li class='list-group-item'><strong>Fecha Programada:</strong> {fechaProgramada}</li>
                            <li class='list-group-item'><strong>Fecha de Atención:</strong> {mantenimiento.FechaAtencion.ToString("dd/MM/yyyy")}</li>
                            <li class='list-group-item'><strong>Fecha de Terminación:</strong> {mantenimiento.FechaInsercion.ToString("dd/MM/yyyy HH:mm")}</li>
                            <li class='list-group-item'><strong>Tipo de Mantenimiento:</strong> {mantenimiento.Agendum.ClaveTipoMttoNavigation?.NombreTipoM}</li>
                            <li class='list-group-item'><strong>RPE:</strong> {mantenimiento.Rpe}</li>
                            <li class='list-group-item'><strong>Nombre:</strong> {nombreUsuario}</li>
                        </ul>
                    </div>
                </div>
                <div class='row mt-3'>
                    <div class='col-12'>
                        <h5>Detalles del Mantenimiento</h5>
                        <div class='card'>
                            <div class='card-body'>
                                <p><strong>Problemas reportados:</strong></p>
                                <p>{mantenimiento.Problemas ?? "No especificado"}</p>
                                <p><strong>Diagnóstico:</strong></p>
                                <p>{mantenimiento.Diagnostico ?? "No especificado"}</p>
                                <p><strong>Observaciones:</strong></p>
                                <p>{mantenimiento.Observaciones ?? "No especificado"}</p>
                            </div>
                        </div>
                    </div>
                </div>";

                return Content(html);
            }
            catch (Exception ex)
            {
                return Content($"<div class='alert alert-danger'>Error al obtener los detalles: {ex.Message}</div>");
            }
        }

        [HttpGet]
        public async Task<IActionResult> DescargarInventarioExcel()
        {
            try
            {
                var claveZonaUsuario = HttpContext.Session.GetString("ClaveZona");

                if (string.IsNullOrEmpty(claveZonaUsuario))
                {
                    return Json(new { success = false, message = "No se pudo determinar la zona del usuario" });
                }

                using (var workbook = new XLWorkbook())
                {
                    // Hoja 1: Equipos CFEmático
                    var worksheetCfematico = workbook.Worksheets.Add("CFEmáticos");
                    var cfematicos = await _dbocontext.EquipoCfematicos
                        .Include(ec => ec.NumActFijoNavigation)
                            .ThenInclude(e => e.CatCentro)
                                .ThenInclude(cc => cc.CatAgencium)
                                    .ThenInclude(ca => ca.CatZona)
                        .Where(ec => ec.NumActFijoNavigation.ClaveZona == claveZonaUsuario)
                        .ToListAsync();

                    // Encabezados para CFEmáticos
                    worksheetCfematico.Cell(1, 1).Value = "Zona";
                    worksheetCfematico.Cell(1, 2).Value = "Agencia";
                    worksheetCfematico.Cell(1, 3).Value = "Centro";
                    worksheetCfematico.Cell(1, 4).Value = "Número Activo Fijo";
                    worksheetCfematico.Cell(1, 5).Value = "Número Cajero";
                    worksheetCfematico.Cell(1, 6).Value = "Número Serie";
                    worksheetCfematico.Cell(1, 7).Value = "Número Inventario";
                    worksheetCfematico.Cell(1, 8).Value = "IP Cajero";
                    worksheetCfematico.Cell(1, 9).Value = "Versión";

                    // Datos para CFEmáticos
                    for (int i = 0; i < cfematicos.Count; i++)
                    {
                        var row = i + 2;
                        var equipo = cfematicos[i];
                        var centro = equipo.NumActFijoNavigation?.CatCentro;
                        var agencia = centro?.CatAgencium;
                        var zona = agencia?.CatZona;

                        worksheetCfematico.Cell(row, 1).Value = zona?.NombreZona ?? "N/A";
                        worksheetCfematico.Cell(row, 2).Value = agencia?.NombreAgencia ?? "N/A";
                        worksheetCfematico.Cell(row, 3).Value = centro?.NombreCentro ?? "N/A";
                        worksheetCfematico.Cell(row, 4).Value = equipo.NumActFijo;
                        worksheetCfematico.Cell(row, 5).Value = equipo.NumCajero;
                        worksheetCfematico.Cell(row, 6).Value = equipo.NumSerie;
                        worksheetCfematico.Cell(row, 7).Value = equipo.NumInventario;
                        worksheetCfematico.Cell(row, 8).Value = equipo.IpCajero;
                        worksheetCfematico.Cell(row, 9).Value = equipo.Version;
                    }

                    // Formato de encabezados
                    var headerRangeCfematico = worksheetCfematico.Range(1, 1, 1, 9);
                    headerRangeCfematico.Style.Fill.BackgroundColor = XLColor.LightGray;
                    headerRangeCfematico.Style.Font.Bold = true;
                    worksheetCfematico.Columns().AdjustToContents();

                    // Hoja 2: Equipos AC
                    var worksheetAC = workbook.Worksheets.Add("Equipos de Atencion a Clientes");
                    var equiposAC = await _dbocontext.EquipoAcs
                        .Include(e => e.NumActFijoNavigation)
                            .ThenInclude(e => e.CatCentro)
                                .ThenInclude(cc => cc.CatAgencium)
                                    .ThenInclude(ca => ca.CatZona)
                        .Include(e => e.ClaveTipoEquipoNavigation)
                        .Where(e => e.NumActFijoNavigation.ClaveZona == claveZonaUsuario)
                        .ToListAsync();

                    // Encabezados para Equipos AC
                    worksheetAC.Cell(1, 1).Value = "Zona";
                    worksheetAC.Cell(1, 2).Value = "Agencia";
                    worksheetAC.Cell(1, 3).Value = "Centro";
                    worksheetAC.Cell(1, 4).Value = "Número Activo Fijo";
                    worksheetAC.Cell(1, 5).Value = "Tipo de Equipo";
                    worksheetAC.Cell(1, 6).Value = "Número Serie";

                    // Datos para Equipos AC
                    for (int i = 0; i < equiposAC.Count; i++)
                    {
                        var row = i + 2;
                        var equipo = equiposAC[i];
                        var centro = equipo.NumActFijoNavigation?.CatCentro;
                        var agencia = centro?.CatAgencium;
                        var zona = agencia?.CatZona;

                        worksheetAC.Cell(row, 1).Value = zona?.NombreZona ?? "N/A";
                        worksheetAC.Cell(row, 2).Value = agencia?.NombreAgencia ?? "N/A";
                        worksheetAC.Cell(row, 3).Value = centro?.NombreCentro ?? "N/A";
                        worksheetAC.Cell(row, 4).Value = equipo.NumActFijo;
                        worksheetAC.Cell(row, 5).Value = equipo.ClaveTipoEquipoNavigation?.NombreTipoEquipo;
                        worksheetAC.Cell(row, 6).Value = equipo.NumSerie;
                    }

                    // Formato de encabezados
                    var headerRangeAC = worksheetAC.Range(1, 1, 1, 6);
                    headerRangeAC.Style.Fill.BackgroundColor = XLColor.LightGray;
                    headerRangeAC.Style.Font.Bold = true;
                    worksheetAC.Columns().AdjustToContents();

                    // Hoja 3: Equipos de Cómputo
                    var worksheetComputo = workbook.Worksheets.Add("Equipos de Cómputo");
                    var equiposComputo = await _dbocontext.EquipoComputos
                        .Include(e => e.NumActFijoNavigation)
                            .ThenInclude(e => e.CatCentro)
                                .ThenInclude(cc => cc.CatAgencium)
                                    .ThenInclude(ca => ca.CatZona)
                        .Include(e => e.ClaveTipoEquipoNavigation)
                        .Where(e => e.NumActFijoNavigation.ClaveZona == claveZonaUsuario)
                        .ToListAsync();

                    // Encabezados para Equipos de Cómputo
                    worksheetComputo.Cell(1, 1).Value = "Zona";
                    worksheetComputo.Cell(1, 2).Value = "Agencia";
                    worksheetComputo.Cell(1, 3).Value = "Centro";
                    worksheetComputo.Cell(1, 4).Value = "Número Activo Fijo";
                    worksheetComputo.Cell(1, 5).Value = "Tipo de Equipo";
                    worksheetComputo.Cell(1, 6).Value = "Número Serie PC";
                    worksheetComputo.Cell(1, 7).Value = "Número Serie Monitor";
                    worksheetComputo.Cell(1, 8).Value = "RPE";
                    worksheetComputo.Cell(1, 9).Value = "Nombre RPE";

                    // Datos para Equipos de Cómputo
                    for (int i = 0; i < equiposComputo.Count; i++)
                    {
                        var row = i + 2;
                        var equipo = equiposComputo[i];
                        var centro = equipo.NumActFijoNavigation?.CatCentro;
                        var agencia = centro?.CatAgencium;
                        var zona = agencia?.CatZona;

                        worksheetComputo.Cell(row, 1).Value = zona?.NombreZona ?? "N/A";
                        worksheetComputo.Cell(row, 2).Value = agencia?.NombreAgencia ?? "N/A";
                        worksheetComputo.Cell(row, 3).Value = centro?.NombreCentro ?? "N/A";
                        worksheetComputo.Cell(row, 4).Value = equipo.NumActFijo;
                        worksheetComputo.Cell(row, 5).Value = equipo.ClaveTipoEquipoNavigation?.NombreTipoEquipo;
                        worksheetComputo.Cell(row, 6).Value = equipo.NumSeriePc;
                        worksheetComputo.Cell(row, 7).Value = equipo.NumSerieMonitor;
                        worksheetComputo.Cell(row, 8).Value = equipo.Rpe;
                        worksheetComputo.Cell(row, 9).Value = equipo.NombreRpe;
                    }

                    // Formato de encabezados
                    var headerRangeComputo = worksheetComputo.Range(1, 1, 1, 9);
                    headerRangeComputo.Style.Fill.BackgroundColor = XLColor.LightGray;
                    headerRangeComputo.Style.Font.Bold = true;
                    worksheetComputo.Columns().AdjustToContents();

                    // Crear el archivo en memoria
                    using (var stream = new MemoryStream())
                    {
                        workbook.SaveAs(stream);
                        var content = stream.ToArray();

                        var fileName = $"Inventario_Zona_{claveZonaUsuario}.xlsx";

                        return File(content,
                            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                            fileName);
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error al generar el Excel: {ex.Message}" });
            }
        }
    }
}
