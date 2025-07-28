using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MantenimientosTI.Models;

namespace ProyectoMantenimientos.Controllers
{
    public class RepositorioController : Controller
    {
        private readonly MantenimientosTIContext _dbocontext;

        public RepositorioController(MantenimientosTIContext context)
        {
            _dbocontext = context;
        }
        [Authorize(Roles = "ADMINISTRADOR,TÉCNICO DE ZONA")]
        public IActionResult TerminarMantenimiento()
        {
            return View();
        }
        [Authorize(Roles = "ADMINISTRADOR,TÉCNICO DE ZONA")]
        public IActionResult Repositorio()
        {
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> BuscarPorNumeroOrden(int claveAgenda)
        {
            try
            {
                // Buscar en Agenda en lugar de Mantenimiento
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

                // Determinar tipo de equipo
                string tipoEquipo = "CFEMÁTICO";
                var equipoAC = await _dbocontext.EquipoAcs
                    .Include(e => e.ClaveTipoEquipoNavigation)
                    .FirstOrDefaultAsync(e => e.NumActFijo == agendaItem.NumActFijo);

                var equipoComputo = await _dbocontext.EquipoComputos
                    .Include(e => e.ClaveTipoEquipoNavigation)
                    .FirstOrDefaultAsync(e => e.NumActFijo == agendaItem.NumActFijo);

                if (equipoAC != null)
                    tipoEquipo = equipoAC.ClaveTipoEquipoNavigation?.NombreTipoEquipo ?? "Equipo AC";
                else if (equipoComputo != null)
                    tipoEquipo = equipoComputo.ClaveTipoEquipoNavigation?.NombreTipoEquipo ?? "Equipo de Cómputo";

                var centro = agendaItem.NumActFijoNavigation?.CatCentro;
                var agencia = centro?.CatAgencium;
                var zona = agencia?.CatZona;

                // Antes de crear el mantenimiento
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
                    esCorrectivo = agendaItem.ClaveTipoMtto == "C", // Para mostrar campos de fotos
                    data = new
                    {
                        numOrden = agendaItem.ClaveAgenda, // Ahora usamos ClaveAgenda como "número de orden"
                        numActFijo = agendaItem.NumActFijo,
                        fechaProgramada = agendaItem.FechaProgramada.ToString("dd/MM/yyyy"),
                        tipoMantenimiento = agendaItem.ClaveTipoMttoNavigation?.NombreTipoM,
                        tipoEquipo = tipoEquipo,
                        zona = zona?.NombreZona ?? "No especificado",
                        agencia = agencia?.NombreAgencia ?? "No especificado",
                        centro = centro?.NombreCentro ?? "No especificado",
                        estatus = agendaItem.Estatus
                        // Los demás campos vendrán vacíos porque es nuevo
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
                    // 1. Validar y obtener agenda
                    var agendaItem = await _dbocontext.Agenda
                        .FirstOrDefaultAsync(a => a.ClaveAgenda == numOrden);

                    if (agendaItem == null)
                        return Json(new { success = false, message = "Registro no encontrado en Agenda" });

                    // 2. Validar RPE
                    var rpe = HttpContext.Session.GetString("Rpe");
                    if (string.IsNullOrEmpty(rpe))
                        return Json(new { success = false, message = "Sesión inválida (RPE no encontrado)" });

                    // 3. Crear mantenimiento
                    var mantenimiento = new Mantenimiento
                    {
                        NumOrden = numOrden, // Usar el parámetro recibido (Opción 1)

                        ClaveAgenda = agendaItem.ClaveAgenda,
                        NumActFijo = agendaItem.NumActFijo,
                        FechaProgramada = agendaItem.FechaProgramada,
                        ClaveTipoMtto = agendaItem.ClaveTipoMtto,
                        Rpe = rpe,
                        Problemas = problemas ?? string.Empty,
                        Diagnostico = diagnostico ?? string.Empty,
                        Observaciones = observaciones ?? string.Empty,
                        Fecha = DateTime.Now
                    };

                    // 4. Procesar PDF
                    if (archivoPdf != null && archivoPdf.Length > 0)
                    {
                        mantenimiento.EvidenciaHojaServicio = await GuardarArchivo(archivoPdf);
                    }

                    // 5. Guardar primero el mantenimiento
                    _dbocontext.Mantenimientos.Add(mantenimiento);
                    await _dbocontext.SaveChangesAsync();

                    // 6. Procesar fotos para correctivos
                    if (agendaItem.ClaveTipoMtto == "C")
                    {
                        var foto = new Foto
                        {
                            NumOrden = mantenimiento.NumOrden,
                            FechaHora = DateTime.Now
                        };

                        if (fotoAntes != null && fotoAntes.Length > 0)
                            foto.FotoAntes = await ProcesarImagen(fotoAntes);

                        if (fotoDurante != null && fotoDurante.Length > 0)
                            foto.FotoDurante = await ProcesarImagen(fotoDurante);

                        if (fotoDespues != null && fotoDespues.Length > 0)
                            foto.FotoDespues = await ProcesarImagen(fotoDespues);

                        _dbocontext.Fotos.Add(foto);
                    }

                    // 7. Actualizar agenda
                    agendaItem.Estatus = "TERMINADO";

                    await _dbocontext.SaveChangesAsync();
                    await transaction.CommitAsync();

                    return Json(new { success = true, message = "Mantenimiento registrado correctamente" });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    return Json(new
                    {
                        success = false,
                        message = $"Error: {ex.Message}",
                        inner = ex.InnerException?.Message
                    });
                }
            }
        }

        private async Task<string> ProcesarImagen(IFormFile imagen)
        {
            using (var ms = new MemoryStream())
            {
                await imagen.CopyToAsync(ms);
                // Guardar como Base64 sin prefijo
                return Convert.ToBase64String(ms.ToArray());
            }
        }

        private async Task<string> GuardarArchivo(IFormFile archivo)
        {
            // Crear la carpeta si no existe
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
        public async Task<IActionResult> ObtenerMantenimientos()
        {
            try
            {
                // Obtener el rol del usuario desde la sesión
                var claveRolUsuario = HttpContext.Session.GetInt32("Rol");
                var esAdministrador = claveRolUsuario == 1; // 1 = Administrador

                // Consulta base para mantenimientos terminados
                var query = _dbocontext.Mantenimientos
                    .Include(m => m.Agendum)
                        .ThenInclude(a => a.NumActFijoNavigation)
                            .ThenInclude(e => e.CatCentro)
                                .ThenInclude(c => c.CatAgencium)
                                    .ThenInclude(a => a.CatZona)
                    .Where(m => m.Agendum.Estatus == "TERMINADO");

                // Si no es administrador, filtrar por zona
                if (!esAdministrador)
                {
                    var claveZonaUsuario = HttpContext.Session.GetString("ClaveZona");
                    if (string.IsNullOrEmpty(claveZonaUsuario))
                    {
                        return Json(new { success = false, message = "No se pudo determinar la zona del usuario" });
                    }
                    query = query.Where(m => m.Agendum.NumActFijoNavigation.ClaveZona == claveZonaUsuario);
                }

                var mantenimientos = await query
                    .OrderByDescending(m => m.Fecha)
                    .ToListAsync();

                // Listas para cada categoría
                var cfematicos = new List<object>();
                var atencionClientes = new List<object>();
                var equiposComputo = new List<object>();

                foreach (var m in mantenimientos)
                {
                    // Obtener datos de ubicación
                    var centro = m.Agendum.NumActFijoNavigation?.CatCentro;
                    var agencia = centro?.CatAgencium;
                    var zona = agencia?.CatZona;

                    // Determinar tipo de equipo
                    var equipoAc = await _dbocontext.EquipoAcs
                        .Include(e => e.ClaveTipoEquipoNavigation)
                        .FirstOrDefaultAsync(e => e.NumActFijo == m.NumActFijo);

                    var equipoComputo = await _dbocontext.EquipoComputos
                        .Include(e => e.ClaveTipoEquipoNavigation)
                        .FirstOrDefaultAsync(e => e.NumActFijo == m.NumActFijo);

                    // Buscar en EquipoCfematico solo si no es AC ni Computo
                    var equipoCfematico = (equipoAc == null && equipoComputo == null) ?
                        await _dbocontext.EquipoCfematicos
                            .FirstOrDefaultAsync(e => e.NumActFijo == m.NumActFijo) :
                        null;

                    // Crear el item para la tabla
                    var item = new
                    {
                        m.NumOrden,
                        FechaProgramada = m.FechaProgramada.ToString("dd/MM/yyyy"), // Formato día/mes/año
                        FechaTerminada = m.Fecha.ToString("dd/MM/yyyy HH:mm"),      // Formato día/mes/año hora:minuto
                        m.EvidenciaHojaServicio,
                        TieneFotos = await _dbocontext.Fotos.AnyAsync(f => f.NumOrden == m.NumOrden),
                        Rpe = m.Rpe,
                        Zona = zona?.NombreZona ?? "No especificado",
                        Agencia = agencia?.NombreAgencia ?? "No especificado",
                        Centro = centro?.NombreCentro ?? "No especificado",
                        NumCajero = equipoCfematico?.NumCajero ?? "N/A",
                        TipoEquipo = equipoAc?.ClaveTipoEquipoNavigation?.NombreTipoEquipo ??
                                    equipoComputo?.ClaveTipoEquipoNavigation?.NombreTipoEquipo ??
                                    "CFEmático"
                    };

                    // Clasificación
                    if (equipoAc != null)
                    {
                        atencionClientes.Add(item);
                    }
                    else if (equipoComputo != null)
                    {
                        equiposComputo.Add(item);
                    }
                    else
                    {
                        // CFEmáticos (incluye EquipoCfematico y otros no clasificados)
                        cfematicos.Add(item);
                    }
                }

                return Json(new
                {
                    Cfematicos = cfematicos,
                    AtencionClientes = atencionClientes,
                    EquiposComputo = equiposComputo
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
                    .FirstOrDefaultAsync(f => f.NumOrden == numOrden);

                if (foto == null)
                {
                    return NotFound("No se encontró el registro de fotos");
                }

                string imagenBase64 = tipo switch
                {
                    "antes" => foto.FotoAntes,
                    "durante" => foto.FotoDurante,
                    "despues" => foto.FotoDespues,
                    _ => null
                };

                if (string.IsNullOrEmpty(imagenBase64))
                {
                    return NotFound("No se encontró la imagen solicitada");
                }

                // Limpiar el string Base64 por si tiene prefijos
                var cleanBase64 = imagenBase64.StartsWith("data:image")
                    ? imagenBase64.Split(',')[1]
                    : imagenBase64;

                // Convertir a bytes
                byte[] imageBytes = Convert.FromBase64String(cleanBase64);

                // Determinar el tipo de imagen (puedes guardar esta info en la base de datos si es variable)
                return File(imageBytes, "image/jpeg"); // o "image/png" según corresponda
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error al procesar la imagen: {ex.Message}");
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
                var esAdministrador = claveRolUsuario == 1; // 1 = Administrador

                // Consulta base
                var query = _dbocontext.Mantenimientos
                    .Include(m => m.Agendum)
                        .ThenInclude(a => a.NumActFijoNavigation)
                            .ThenInclude(e => e.CatCentro)
                                .ThenInclude(c => c.CatAgencium)
                                    .ThenInclude(a => a.CatZona)
                    .Include(m => m.Agendum)
                        .ThenInclude(a => a.ClaveTipoMttoNavigation)
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
                var equipoAC = await _dbocontext.EquipoAcs
                    .Include(e => e.ClaveTipoEquipoNavigation)
                    .FirstOrDefaultAsync(e => e.NumActFijo == mantenimiento.NumActFijo);

                var equipoComputo = await _dbocontext.EquipoComputos
                    .Include(e => e.ClaveTipoEquipoNavigation)
                    .FirstOrDefaultAsync(e => e.NumActFijo == mantenimiento.NumActFijo);

                if (equipoAC != null)
                    tipoEquipo = equipoAC.ClaveTipoEquipoNavigation?.NombreTipoEquipo ?? "Equipo AC";
                else if (equipoComputo != null)
                    tipoEquipo = equipoComputo.ClaveTipoEquipoNavigation?.NombreTipoEquipo ?? "Equipo de Cómputo";

                var centro = mantenimiento.Agendum.NumActFijoNavigation?.CatCentro;
                var agencia = centro?.CatAgencium;
                var zona = agencia?.CatZona;

                // Construir el HTML con los detalles
                var html = $@"
                <div class='row'>
                    <div class='col-md-6'>
                        <h5>Información del Equipo</h5>
                        <ul class='list-group list-group-flush'>
                            <li class='list-group-item'><strong>Número de Activo Fijo:</strong> {mantenimiento.NumActFijo}</li>
                            <li class='list-group-item'><strong>Tipo de Equipo:</strong> {tipoEquipo}</li>
                            <li class='list-group-item'><strong>Zona:</strong> {zona?.NombreZona ?? "No especificado"}</li>
                            <li class='list-group-item'><strong>Agencia:</strong> {agencia?.NombreAgencia ?? "No especificado"}</li>
                            <li class='list-group-item'><strong>Centro:</strong> {centro?.NombreCentro ?? "No especificado"}</li>
                        </ul>
                    </div>
                    <div class='col-md-6'>
                        <h5>Información del Mantenimiento</h5>
                        <ul class='list-group list-group-flush'>
                            <li class='list-group-item'><strong>Número de Orden:</strong> {mantenimiento.NumOrden}</li>
                            <li class='list-group-item'><strong>Fecha Programada:</strong> {mantenimiento.FechaProgramada.ToString("dd/MM/yyyy")}</li>
                            <li class='list-group-item'><strong>Fecha de Terminación:</strong> {mantenimiento.Fecha.ToString("dd/MM/yyyy HH:mm")}</li>
                            <li class='list-group-item'><strong>Tipo de Mantenimiento:</strong> {mantenimiento.Agendum.ClaveTipoMttoNavigation?.NombreTipoM}</li>
                            <li class='list-group-item'><strong>Técnico (RPE):</strong> {mantenimiento.Rpe}</li>
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
    }
}
