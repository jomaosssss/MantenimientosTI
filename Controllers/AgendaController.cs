using Microsoft.AspNetCore.Mvc;
using MantenimientosTI.Models;
using Microsoft.EntityFrameworkCore;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using MantenimientosTI.Models.ViewModels;
using MantenimientosTI.Services;
using Microsoft.Extensions.Logging;


namespace MantenimientosTI.Controllers
{
    public class AgendaController : Controller
    {
        private readonly MantenimientosTIContext _dbocontext;
        private readonly BitacoraService _bitacora;
        private readonly ILogger<AgendaController> _logger;


        public AgendaController(MantenimientosTIContext context, BitacoraService bitacora, ILogger<AgendaController> logger)
        {
            _dbocontext = context;
            _bitacora = bitacora;
            _logger = logger;
        }

        [Authorize(Roles = "ADMINISTRADOR,TÉCNICO DE ZONA")]
        public IActionResult AgendarPreventivos()
        {
            var config = _dbocontext.Configuraciones
                .FirstOrDefault(c => c.ClaveConfiguracion == "AGENDAR_PREVENTIVOS");

            bool estaHabilitado = (config != null && config.Valor == "1");

            ViewBag.EstaHabilitado = estaHabilitado;

            return View();
        }

        [HttpPost]
        public async Task<IActionResult> EnviarDatosCsv([FromForm] IFormFile ArchivoCsv)
        {
            const int MAX_LINEAS = 1000;

            try
            {
                // Obtener la zona del usuario logueado
                string? claveZonaUsuario = HttpContext.Session.GetString("ClaveZona");
                string? claveDivisionUsuario = HttpContext.Session.GetString("ClaveDivision");
                int? claveRolUsuario = HttpContext.Session.GetInt32("Rol");
                string? nombreRolUsuario = HttpContext.Session.GetString("NombreRol");

                if (string.IsNullOrEmpty(claveZonaUsuario) || string.IsNullOrEmpty(claveDivisionUsuario))
                {
                    return Json(new { success = false, message = "No se pudo identificar la zona del usuario. Por favor inicie sesión nuevamente." });
                }

                // Determinar si es administrador
                bool esAdministrador = claveRolUsuario == 1 || (nombreRolUsuario?.ToUpper() == "ADMINISTRADOR");

                if (ArchivoCsv == null || ArchivoCsv.Length == 0)
                {
                    return Json(new { success = false, message = "No se recibió ningún archivo." });
                }

                // Validar tamaño del archivo (max 5MB)
                if (ArchivoCsv.Length > 5 * 1024 * 1024)
                {
                    return Json(new { success = false, message = "El archivo no debe exceder los 5MB." });
                }

                // Validar extensión del archivo
                var extension = Path.GetExtension(ArchivoCsv.FileName).ToLower();
                if (extension != ".csv")
                {
                    return Json(new { success = false, message = "El archivo debe tener extensión .csv" });
                }

                var lista = new List<Agendum>();
                var lineasProcesadasDetalle = new List<object>();
                var errores = new List<string>();
                var activosProcesados = new HashSet<(string, DateOnly)>();

                int lineasProcesadas = 0;
                int lineasConError = 0;
                int lineasDuplicadasBD = 0;
                int lineasConActivoInexistente = 0;
                int lineasConFechaPasada = 0;
                int lineasDeOtraZona = 0;
                int lineasDuplicadasEnArchivo = 0;

                using (var reader = new StreamReader(ArchivoCsv.OpenReadStream(), Encoding.GetEncoding("iso-8859-1")))
                {
                    string? linea;
                    bool esPrimeraLinea = true;
                    int numeroLinea = 0;

                    while ((linea = reader.ReadLine()) != null)
                    {
                        numeroLinea++;
                        var detalleLinea = new
                        {
                            numero = numeroLinea,
                            contenido = linea,
                            estado = "",
                            mensaje = ""
                        };

                        // Validar límite de registros
                        if (numeroLinea > MAX_LINEAS)
                        {
                            detalleLinea = new
                            {
                                numero = numeroLinea,
                                contenido = linea,
                                estado = "error",
                                mensaje = "Límite de registros excedido"
                            };
                            lineasProcesadasDetalle.Add(detalleLinea);
                            errores.Add($"Se procesaron solo las primeras {MAX_LINEAS} registros. El archivo contiene más registros que el límite permitido (1000).");
                            break;
                        }

                        if (esPrimeraLinea)
                        {
                            detalleLinea = new
                            {
                                numero = numeroLinea,
                                contenido = linea,
                                estado = "ignorado",
                                mensaje = "Encabezado Ignorado"
                            };
                            lineasProcesadasDetalle.Add(detalleLinea);
                            esPrimeraLinea = false;
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(linea))
                        {
                            detalleLinea = new
                            {
                                numero = numeroLinea,
                                contenido = linea,
                                estado = "ignorado",
                                mensaje = "Línea vacía ignorada"
                            };
                            lineasProcesadasDetalle.Add(detalleLinea);
                            continue;
                        }

                        var columnas = linea.Split(',');

                        if (columnas.Length < 2)
                        {
                            detalleLinea = new
                            {
                                numero = numeroLinea,
                                contenido = linea,
                                estado = "error",
                                mensaje = "Formato incorrecto. Se esperaban al menos 2 columnas"
                            };
                            lineasProcesadasDetalle.Add(detalleLinea);
                            errores.Add($"Línea {numeroLinea}: Formato incorrecto. Se esperaban al menos 2 columnas.");
                            lineasConError++;
                            continue;
                        }

                        try
                        {
                            var numActFijo = columnas[0].Trim();
                            var fechaStr = columnas[1].Trim();

                            // Validar activo fijo
                            var query = _dbocontext.Equipos.Where(e => e.NumActFijo == numActFijo);

                            if (!esAdministrador)
                            {
                                query = query.Where(e => e.ClaveDivision == claveDivisionUsuario &&
                                                       e.ClaveZona == claveZonaUsuario);
                            }

                            var equipoExistente = query.FirstOrDefault();

                            if (equipoExistente == null)
                            {
                                // Verificar si el equipo existe en otra zona (solo aplica si no es administrador)
                                var existeEnOtraZona = !esAdministrador && _dbocontext.Equipos.Any(e => e.NumActFijo == numActFijo);

                                if (existeEnOtraZona)
                                {
                                    detalleLinea = new
                                    {
                                        numero = numeroLinea,
                                        contenido = linea,
                                        estado = "error",
                                        mensaje = $"El activo {numActFijo} existe pero no pertenece a su zona"
                                    };
                                    lineasProcesadasDetalle.Add(detalleLinea);
                                    errores.Add($"Línea {numeroLinea}: El activo {numActFijo} existe pero no pertenece a su zona.");
                                    lineasDeOtraZona++;
                                }
                                else
                                {
                                    detalleLinea = new
                                    {
                                        numero = numeroLinea,
                                        contenido = linea,
                                        estado = "error",
                                        mensaje = $"El activo fijo {numActFijo} no existe en el sistema"
                                    };
                                    lineasProcesadasDetalle.Add(detalleLinea);
                                    errores.Add($"Línea {numeroLinea}: El activo fijo {numActFijo} no existe en el sistema.");
                                    lineasConActivoInexistente++;
                                }
                                continue;
                            }

                            // Validar y convertir la fecha
                            DateOnly fechaProgramada;
                            try
                            {
                                // Primero intentar con formato con barras DD/MM/YYYY
                                if (fechaStr.Contains('/'))
                                {
                                    fechaProgramada = DateOnly.ParseExact(fechaStr, "dd/MM/yyyy");
                                }
                                // Luego intentar con formato con guiones DD-MM-YYYY
                                else if (fechaStr.Contains('-'))
                                {
                                    fechaProgramada = DateOnly.ParseExact(fechaStr, "dd-MM-yyyy");
                                }
                                else
                                {
                                    throw new FormatException("Formato de fecha no reconocido");
                                }
                            }
                            catch
                            {
                                detalleLinea = new
                                {
                                    numero = numeroLinea,
                                    contenido = linea,
                                    estado = "error",
                                    mensaje = $"Formato de fecha inválido ({fechaStr}). Use el formato DD/MM/YYYY o DD-MM-YYYY."
                                };
                                lineasProcesadasDetalle.Add(detalleLinea);
                                errores.Add($"Línea {numeroLinea}: Formato de fecha inválido ({fechaStr}). Use el formato DD/MM/YYYY o DD-MM-YYYY.");
                                lineasConError++;
                                continue;
                            }

                            // Validar fecha futura
                            //if (fechaProgramada < DateOnly.FromDateTime(DateTime.Now))
                            //{
                            //    detalleLinea = new
                            //    {
                            //        numero = numeroLinea,
                            //        contenido = linea,
                            //        estado = "error",
                            //        mensaje = $"Fecha vencida ({fechaStr})"
                            //    };
                            //    lineasProcesadasDetalle.Add(detalleLinea);
                            //    errores.Add($"Línea {numeroLinea}: La fecha {fechaStr} ya esta vencida.");
                            //    lineasConFechaPasada++;
                            //    continue;
                            //}

                            // Crear clave única para el activo+fecha
                            var claveActivoFecha = (numActFijo, fechaProgramada);

                            // Verificar duplicado en el mismo archivo
                            if (activosProcesados.Contains(claveActivoFecha))
                            {
                                detalleLinea = new
                                {
                                    numero = numeroLinea,
                                    contenido = linea,
                                    estado = "error",
                                    mensaje = $"Registro duplicado en el archivo ({numActFijo},{fechaStr})"
                                };
                                lineasProcesadasDetalle.Add(detalleLinea);
                                errores.Add($"Línea {numeroLinea}: El registro {numActFijo} ya tiene asignada la fecha {fechaStr} en este archivo.");
                                lineasDuplicadasEnArchivo++;
                                continue;
                            }

                            // Verificar duplicado en base de datos
                            var existeDuplicado = _dbocontext.Agenda.Any(a =>
                                a.NumActFijo == numActFijo &&
                                a.FechaProgramada == fechaProgramada &&
                                a.ClaveTipoMtto == "P");

                            if (existeDuplicado)
                            {
                                detalleLinea = new
                                {
                                    numero = numeroLinea,
                                    contenido = linea,
                                    estado = "error",
                                    mensaje = $"Registro duplicado en la agenda del sistema ({numActFijo} con fecha {fechaStr})"
                                };
                                lineasProcesadasDetalle.Add(detalleLinea);
                                errores.Add($"Línea {numeroLinea}: Ya existe un mantenimiento agendado en el sistema para el equipo: {numActFijo} en la fecha {fechaStr}.");
                                lineasDuplicadasBD++;
                                continue;
                            }

                            // Si pasa todas las validaciones
                            detalleLinea = new
                            {
                                numero = numeroLinea,
                                contenido = linea,
                                estado = "exitoso",
                                mensaje = $"El equipo {numActFijo} está programado para el {fechaStr}"
                            };
                            lineasProcesadasDetalle.Add(detalleLinea);

                            activosProcesados.Add(claveActivoFecha);
                            lista.Add(new Agendum
                            {
                                NumActFijo = numActFijo,
                                ClaveTipoMtto = "P",
                                FechaProgramada = fechaProgramada,
                                Estatus = "PENDIENTE"
                            });
                            lineasProcesadas++;
                        }
                        catch (Exception ex)
                        {
                            detalleLinea = new
                            {
                                numero = numeroLinea,
                                contenido = linea,
                                estado = "error",
                                mensaje = $"Error inesperado: {ex.Message}"
                            };
                            lineasProcesadasDetalle.Add(detalleLinea);
                            errores.Add($"Línea {numeroLinea}: Error inesperado - {ex.Message}");
                            lineasConError++;
                        }
                    }
                }

                if (lista.Count == 0)
                {
                    var mensajeError = "El archivo no contiene datos válidos. ";
                    if (lineasConActivoInexistente > 0) mensajeError += $"{lineasConActivoInexistente} números de activo fijo no existentes. ";
                    if (lineasDeOtraZona > 0) mensajeError += $"{lineasDeOtraZona} equipos de otra zona. ";
                    if (lineasConFechaPasada > 0) mensajeError += $"{lineasConFechaPasada} fechas vencidas. ";
                    if (lineasDuplicadasBD > 0) mensajeError += $"{lineasDuplicadasBD} equipos ya programados en el sistema. ";
                    if (lineasDuplicadasEnArchivo > 0) mensajeError += $"{lineasDuplicadasEnArchivo} registros duplicados en el archivo. ";

                    return Json(new
                    {
                        success = false,
                        message = mensajeError,
                        detallesErrores = errores,
                        lineasProcesadasDetalle = lineasProcesadasDetalle,
                        totalErrores = errores.Count,
                        resumen = new
                        {
                            totalLineas = lineasProcesadas + lineasConError + lineasDuplicadasBD +
                                         lineasConActivoInexistente + lineasConFechaPasada +
                                         lineasDeOtraZona + lineasDuplicadasEnArchivo,
                            exitosos = lineasProcesadas,
                            errores = lineasConError,
                            duplicados = lineasDuplicadasBD + lineasDuplicadasEnArchivo,
                            duplicadosBD = lineasDuplicadasBD,
                            duplicadosArchivo = lineasDuplicadasEnArchivo,
                            activosNoExistentes = lineasConActivoInexistente,
                            fechasPasadas = lineasConFechaPasada,
                            activosOtraZona = lineasDeOtraZona
                        }
                    });
                }

                // Guardar solo si hay registros válidos
                // Guardar solo si hay registros válidos
                _dbocontext.Agenda.AddRange(lista);

                // REGISTRO EN BITÁCORA - CARGA CSV EXITOSA
                var usuario = User.Identity?.Name;
                var rpe = HttpContext.Session.GetString("Rpe");
                var rol = HttpContext.Session.GetString("NombreRol");
                var zona = HttpContext.Session.GetString("NombreZona");
                var centro = HttpContext.Session.GetString("ClaveDivision");

                await _bitacora.RegistrarCargaCSVAsync(usuario, rpe, rol, zona, centro, "PREVENTIVOS", lineasProcesadas);

                _dbocontext.SaveChanges();

                return Json(new
                {
                    success = true,
                    message = $"¡Se han agendado {lineasProcesadas} mantenimientos preventivos correctamente!",
                    resumen = new
                    {
                        totalLineas = lineasProcesadas + lineasConError + lineasDuplicadasBD +
                                     lineasConActivoInexistente + lineasConFechaPasada +
                                     lineasDeOtraZona + lineasDuplicadasEnArchivo,
                        exitosos = lineasProcesadas,
                        errores = lineasConError,
                        duplicados = lineasDuplicadasBD + lineasDuplicadasEnArchivo,
                        duplicadosBD = lineasDuplicadasBD,
                        duplicadosArchivo = lineasDuplicadasEnArchivo,
                        activosNoExistentes = lineasConActivoInexistente,
                        fechasPasadas = lineasConFechaPasada,
                        activosOtraZona = lineasDeOtraZona
                    },
                    lineasProcesadasDetalle = lineasProcesadasDetalle,
                    primerosErrores = errores
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = "Error al procesar el archivo: " + ex.Message
                });
            }
        }

        [HttpGet]
        public async Task<IActionResult> ObtenerMotivosCancelacion()
        {
            try
            {
                var motivos = await _dbocontext.CatMotivosCancelacion
                    .Where(m => m.Estatus == "ACTIVO")
                    .OrderBy(m => m.MotivoCancelacion)
                    .Select(m => new
                    {
                        claveMotivo = m.ClaveMotivo,
                        motivoCancelacion = m.MotivoCancelacion
                    })
                    .ToListAsync();

                return Json(motivos);
            }
            catch (Exception ex)
            {
                return Json(new { error = "Error al cargar los motivos: " + ex.Message });
            }
        }

        [Authorize(Roles = "ADMINISTRADOR,TÉCNICO DE ZONA")]
        public IActionResult AgendarCorrectivos()
        {
            // Obtener la zona del usuario logueado
            string? claveZonaUsuario = HttpContext.Session.GetString("ClaveZona");
            string? claveDivisionUsuario = HttpContext.Session.GetString("ClaveDivision");

            if (string.IsNullOrEmpty(claveZonaUsuario) || string.IsNullOrEmpty(claveDivisionUsuario))
            {
                return RedirectToAction("Login", "Account");
            }

            // Obtener la zona actual del usuario
            var zonaUsuario = _dbocontext.CatZonas
                .FirstOrDefault(z => z.ClaveDivision == claveDivisionUsuario &&
                                   z.ClaveZona == claveZonaUsuario);

            // Obtener las agencias de esa zona
            var agencias = _dbocontext.CatAgencia
                .Where(a => a.ClaveDivision == claveDivisionUsuario &&
                           a.ClaveZona == claveZonaUsuario)
                .ToList();

            ViewBag.ZonaUsuario = zonaUsuario?.NombreZona ?? "Zona no identificada";
            ViewBag.Agencias = agencias;

            return View();
        }

        [HttpGet]
        public IActionResult ObtenerTiposEquipoPorCentro(string division, string zona, string agencia, string centro)
        {
            try
            {
                var tipos = new List<string>();

                // Verificar si hay equipos cfematicos en el centro
                var tieneCfematico = _dbocontext.EquipoCfematicos
                    .Any(ec => ec.NumActFijoNavigation.ClaveDivision == division &&
                              ec.NumActFijoNavigation.ClaveZona == zona &&
                              ec.NumActFijoNavigation.ClaveAgencia == agencia &&
                              ec.NumActFijoNavigation.ClaveCentro == centro);

                if (tieneCfematico)
                {
                    tipos.Add("CFEMÁTICO");
                }

                // Obtener tipos de EquipoAc
                var tiposAC = _dbocontext.EquipoAcs
                    .Include(ea => ea.ClaveTipoEquipoNavigation)
                    .Where(ea => ea.NumActFijoNavigation.ClaveDivision == division &&
                                ea.NumActFijoNavigation.ClaveZona == zona &&
                                ea.NumActFijoNavigation.ClaveAgencia == agencia &&
                                ea.NumActFijoNavigation.ClaveCentro == centro)
                    .Select(ea => ea.ClaveTipoEquipoNavigation.NombreTipoEquipo)
                    .Distinct()
                    .ToList();

                tipos.AddRange(tiposAC);

                // Obtener tipos de EquipoComputo
                var tiposComputo = _dbocontext.EquipoComputos
                    .Include(ec => ec.ClaveTipoEquipoNavigation)
                    .Where(ec => ec.NumActFijoNavigation.ClaveDivision == division &&
                                ec.NumActFijoNavigation.ClaveZona == zona &&
                                ec.NumActFijoNavigation.ClaveAgencia == agencia &&
                                ec.NumActFijoNavigation.ClaveCentro == centro)
                    .Select(ec => ec.ClaveTipoEquipoNavigation.NombreTipoEquipo)
                    .Distinct()
                    .ToList();

                tipos.AddRange(tiposComputo);

                // Eliminar duplicados y ordenar
                tipos = tipos.Distinct().OrderBy(t => t).ToList();

                var data = tipos.Select(t => new { value = t, text = t }).ToList();

                return Json(new { success = true, data = data });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public IActionResult ObtenerActivosFijosPorCentroYTipo(string division, string zona, string agencia, string centro, string tipoEquipo)
        {
            try
            {
                List<object> activos = new List<object>();

                if (tipoEquipo == "CFETURNO" || tipoEquipo == "CFECAM")
                {
                    // Buscar en EquipoAc
                    activos = _dbocontext.EquipoAcs
                        .Include(ea => ea.ClaveTipoEquipoNavigation)
                        .Where(ea => ea.NumActFijoNavigation.ClaveDivision == division &&
                                   ea.NumActFijoNavigation.ClaveZona == zona &&
                                   ea.NumActFijoNavigation.ClaveAgencia == agencia &&
                                   ea.NumActFijoNavigation.ClaveCentro == centro &&
                                   ea.ClaveTipoEquipoNavigation.NombreTipoEquipo == tipoEquipo)
                        .Select(ea => new
                        {
                            value = ea.NumActFijo,
                            text = ea.NumActFijo
                        })
                        .ToList<object>();
                }
                else if (tipoEquipo == "PC" || tipoEquipo == "LAPTOP")
                {
                    // Buscar en EquipoComputo
                    activos = _dbocontext.EquipoComputos
                        .Include(ec => ec.ClaveTipoEquipoNavigation)
                        .Where(ec => ec.NumActFijoNavigation.ClaveDivision == division &&
                                   ec.NumActFijoNavigation.ClaveZona == zona &&
                                   ec.NumActFijoNavigation.ClaveAgencia == agencia &&
                                   ec.NumActFijoNavigation.ClaveCentro == centro &&
                                   ec.ClaveTipoEquipoNavigation.NombreTipoEquipo == tipoEquipo)
                        .Select(ec => new
                        {
                            value = ec.NumActFijo,
                            text = ec.NumActFijo
                        })
                        .ToList<object>();
                }

                return Json(new { success = true, data = activos });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
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
                var centros = _dbocontext.CatCentros
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
        public IActionResult ObtenerCajerosPorCentro(string division, string zona, string agencia, string centro)
        {
            try
            {
                var cajeros = _dbocontext.EquipoCfematicos
                    .Include(c => c.NumActFijoNavigation)
                    .Where(c => c.NumActFijoNavigation.ClaveDivision == division &&
                               c.NumActFijoNavigation.ClaveZona == zona &&
                               c.NumActFijoNavigation.ClaveAgencia == agencia &&
                               c.NumActFijoNavigation.ClaveCentro == centro)
                    .Select(c => new
                    {
                        value = c.NumCajero,
                        text = c.NumCajero,
                        numActFijo = c.NumActFijo
                    })
                    .Distinct()
                    .OrderBy(c => c.value)
                    .ToList();

                return Json(new { success = true, data = cajeros });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [Authorize(Roles = "ADMINISTRADOR")]
        public async Task<IActionResult> CancelarDirecto(int idAgenda, int claveMotivo, string justificacion)
        {
            try
            {
                // Validaciones
                if (string.IsNullOrEmpty(justificacion) || justificacion.Length > 500)
                {
                    return Json(new { success = false, message = "La justificación es requerida y no puede exceder 500 caracteres." });
                }

                if (claveMotivo <= 0)
                {
                    return Json(new { success = false, message = "Debe seleccionar un motivo de cancelación válido." });
                }

                var agendaItem = await _dbocontext.Agenda.FindAsync(idAgenda);
                if (agendaItem == null)
                {
                    return Json(new { success = false, message = "No se encontró el mantenimiento en la agenda." });
                }

                // Verificar que el motivo existe en el catálogo
                var motivoExiste = await _dbocontext.CatMotivosCancelacion
                    .AnyAsync(m => m.ClaveMotivo == claveMotivo && m.Estatus == "ACTIVO");

                if (!motivoExiste)
                {
                    return Json(new { success = false, message = "El motivo de cancelación seleccionado no es válido." });
                }

                if (agendaItem.Estatus != "PENDIENTE")
                {
                    return Json(new { success = false, message = $"No se puede cancelar un mantenimiento con estatus '{agendaItem.Estatus}'." });
                }

                // OBTENER RPE DEL USUARIO ACTUAL
                var rpeUsuario = HttpContext.Session.GetString("Rpe") ?? "N/A";

                // Actualizar estatus de la agenda
                agendaItem.Estatus = "CANCELADO";

                // Crear registro en MotivosCancelacion CON RPE Y CLAVE MOTIVO
                var motivoCancelacion = new MotivoCancelacion
                {
                    ClaveAgenda = idAgenda,
                    ClaveMotivo = claveMotivo, // ← NUEVO: Usar clave del catálogo
                    Justificacion = justificacion,
                    UsuarioSolicitud = rpeUsuario, // ← RPE en lugar de nombre
                    FechaSolicitud = DateTime.Now,
                    UsuarioAprobacion = rpeUsuario, // ← RPE en lugar de nombre
                    FechaAprobacion = DateTime.Now
                };

                _dbocontext.MotivosCancelacion.Add(motivoCancelacion);

                // REGISTRO EN BITÁCORA
                var usuario = User.Identity?.Name;
                var rol = HttpContext.Session.GetString("NombreRol");
                var zona = HttpContext.Session.GetString("NombreZona");
                var centro = HttpContext.Session.GetString("ClaveDivision");

                await _bitacora.RegistrarCancelacionDirectaAsync(usuario, rpeUsuario, rol, zona, centro,
                    idAgenda.ToString(), "CFEMático", claveMotivo.ToString(), justificacion);

                await _dbocontext.SaveChangesAsync();

                return Json(new { success = true, message = "El mantenimiento ha sido cancelado directamente." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Ocurrió un error al cancelar el mantenimiento: " + ex.Message });
            }
        }

        [HttpPost]
        [Authorize(Roles = "TÉCNICO DE ZONA")]
        public async Task<IActionResult> PreCancelar(int idAgenda, int claveMotivo, string justificacion)
        {
            try
            {
                // Validaciones
                if (string.IsNullOrEmpty(justificacion) || justificacion.Length > 500)
                {
                    return Json(new { success = false, message = "La justificación es requerida y no puede exceder 500 caracteres." });
                }

                if (claveMotivo <= 0)
                {
                    return Json(new { success = false, message = "Debe seleccionar un motivo de cancelación válido." });
                }

                var agendaItem = await _dbocontext.Agenda.FindAsync(idAgenda);
                if (agendaItem == null)
                {
                    return Json(new { success = false, message = "No se encontró el mantenimiento en la agenda." });
                }

                // Verificar que el motivo existe
                var motivoExiste = await _dbocontext.CatMotivosCancelacion
                    .AnyAsync(m => m.ClaveMotivo == claveMotivo && m.Estatus == "ACTIVO");

                if (!motivoExiste)
                {
                    return Json(new { success = false, message = "El motivo de cancelación seleccionado no es válido." });
                }

                if (agendaItem.Estatus != "PENDIENTE")
                {
                    return Json(new { success = false, message = $"No se puede cancelar un mantenimiento con estatus '{agendaItem.Estatus}'." });
                }

                // OBTENER RPE DEL USUARIO ACTUAL
                var rpeUsuario = HttpContext.Session.GetString("Rpe") ?? "N/A";

                // Actualizar estatus de la agenda
                agendaItem.Estatus = "PRE-CANCELADO";

                // Crear registro en MotivosCancelacion CON RPE Y CLAVE MOTIVO
                var motivoCancelacion = new MotivoCancelacion
                {
                    ClaveAgenda = idAgenda,
                    ClaveMotivo = claveMotivo, // ← NUEVO: Usar clave del catálogo
                    Justificacion = justificacion,
                    UsuarioSolicitud = rpeUsuario, // ← RPE en lugar de nombre
                    FechaSolicitud = DateTime.Now
                    // UsuarioAprobacion y FechaAprobacion se llenan después
                };

                _dbocontext.MotivosCancelacion.Add(motivoCancelacion);

                // REGISTRO EN BITÁCORA
                var usuario = User.Identity?.Name;
                var rol = HttpContext.Session.GetString("NombreRol");
                var zona = HttpContext.Session.GetString("NombreZona");
                var centro = HttpContext.Session.GetString("ClaveDivision");

                await _bitacora.RegistrarSolicitudCancelacionAsync(usuario, rpeUsuario, rol, zona, centro,
                    idAgenda.ToString(), "CFEMático", claveMotivo.ToString(), justificacion);

                await _dbocontext.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    message = "La solicitud de cancelación ha sido enviada. Un administrador debe confirmarla."
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Ocurrió un error: " + ex.Message });
            }
        }


        [HttpPost]
        [Authorize(Roles = "ADMINISTRADOR")]
        public async Task<IActionResult> ConfirmarCancelacion(int idAgenda)
        {
            try
            {
                var agendaItem = await _dbocontext.Agenda
                    .Include(a => a.MotivosCancelacion)
                    .FirstOrDefaultAsync(a => a.ClaveAgenda == idAgenda);

                if (agendaItem == null)
                {
                    return Json(new { success = false, message = "No se encontró el mantenimiento en la agenda." });
                }

                if (agendaItem.Estatus != "PRE-CANCELADO")
                {
                    return Json(new { success = false, message = "Este mantenimiento no está en estatus 'Pre-cancelado'." });
                }

                // Obtener el último motivo de cancelación (el más reciente)
                var ultimoMotivo = agendaItem.MotivosCancelacion
                    .OrderByDescending(m => m.FechaSolicitud)
                    .FirstOrDefault();

                if (ultimoMotivo != null)
                {
                    // OBTENER RPE DEL ADMINISTRADOR ACTUAL
                    var rpeAdmin = HttpContext.Session.GetString("Rpe") ?? "N/A";

                    // Actualizar con RPE del administrador
                    ultimoMotivo.UsuarioAprobacion = rpeAdmin;
                    ultimoMotivo.FechaAprobacion = DateTime.Now;
                }

                // Actualizar estatus de la agenda
                agendaItem.Estatus = "CANCELADO";

                // REGISTRO EN BITÁCORA
                var usuario = User.Identity?.Name;
                var rpe = HttpContext.Session.GetString("Rpe");
                var rol = HttpContext.Session.GetString("NombreRol");
                var zona = HttpContext.Session.GetString("NombreZona");
                var centro = HttpContext.Session.GetString("ClaveDivision");

                await _bitacora.RegistrarConfirmacionCancelacionAsync(usuario, rpe, rol, zona, centro, idAgenda.ToString(), "CFEMático");

                // GUARDAR CAMBIOS CON MANEJO DE ERRORES DETALLADO
                await _dbocontext.SaveChangesAsync();

                return Json(new { success = true, message = "La cancelación del mantenimiento ha sido confirmada." });
            }
            catch (DbUpdateException dbEx)
            {
                // CAPTURAR ERROR DE BASE DE DATOS CON MÁS DETALLES
                var innerMessage = dbEx.InnerException?.Message ?? "Sin detalles adicionales";
                _logger.LogError(dbEx, "Error de base de datos al confirmar cancelación {IdAgenda}: {Message}", idAgenda, innerMessage);

                return Json(new
                {
                    success = false,
                    message = $"Error de base de datos: {innerMessage}"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inesperado al confirmar cancelación {IdAgenda}", idAgenda);
                return Json(new
                {
                    success = false,
                    message = $"Ocurrió un error inesperado: {ex.Message}"
                });
            }
        }

        [HttpPost]
        [Authorize(Roles = "ADMINISTRADOR")] // Permitimos que ambos roles puedan reactivar
        public async Task<IActionResult> ReactivarAgenda(int idAgenda)
        {
            try
            {
                var agendaItem = await _dbocontext.Agenda.FindAsync(idAgenda);

                if (agendaItem == null)
                {
                    return Json(new { success = false, message = "No se encontró la cita en la agenda." });
                }

                // Solo se puede reactivar si está en "PRE-CANCELADO"
                if (agendaItem.Estatus != "PRE-CANCELADO")
                {
                    return Json(new { success = false, message = $"No se puede reactivar una cita con estatus '{agendaItem.Estatus}'." });
                }

                agendaItem.Estatus = "PENDIENTE";

                // REGISTRO EN BITÁCORA - REACTIVACIÓN
                var usuario = User.Identity.Name;
                var rpe = HttpContext.Session.GetString("Rpe");
                var rol = HttpContext.Session.GetString("NombreRol");
                var zona = HttpContext.Session.GetString("NombreZona");
                var centro = HttpContext.Session.GetString("ClaveDivision");

                await _bitacora.RegistrarActividadAsync(usuario, rpe, rol, zona, centro, "REACTIVACION_MTTO",
                    new Dictionary<string, string>
                    {
                        { "ClaveAgenda", idAgenda.ToString() }
                    });

                await _dbocontext.SaveChangesAsync();

                return Json(new { success = true, message = "El mantenimiento ha sido reactivado y ahora está pendiente." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Ocurrió un error: " + ex.Message });
            }
        }

        [Authorize(Roles = "ADMINISTRADOR")]
        public async Task<IActionResult> AprobarCancelaciones()
        {
            string? claveZonaUsuario = HttpContext.Session.GetString("ClaveZona");
            int claveRol = HttpContext.Session.GetInt32("Rol") ?? 0;

            // Consulta base para citas pre-canceladas - INCLUYENDO LOS MOTIVOS DESDE EL CATÁLOGO
            var agendaQuery = _dbocontext.Agenda
                .Include(a => a.NumActFijoNavigation)
                    .ThenInclude(e => e.CatCentro)
                        .ThenInclude(c => c.CatAgencium)
                            .ThenInclude(a => a.CatZona)
                .Include(a => a.ClaveTipoMttoNavigation)
                .Include(a => a.MotivosCancelacion)
                    .ThenInclude(m => m.CatMotivo) // ← NUEVO: Incluir el catálogo de motivos
                .Where(a => a.Estatus == "PRE-CANCELADO");

            // Filtro por zona si no es administrador
            if (claveRol != 1 && !string.IsNullOrEmpty(claveZonaUsuario))
            {
                agendaQuery = agendaQuery
                    .Where(a => a.NumActFijoNavigation != null &&
                               a.NumActFijoNavigation.ClaveZona == claveZonaUsuario);
            }

            var agenda = await agendaQuery.ToListAsync();

            var citasPrecanceladas = new List<VMAgendaVista>();

            foreach (var item in agenda)
            {
                string tipo = "CFEMÁTICO";

                // Determinar el tipo de equipo (código existente)
                var equipoAc = _dbocontext.EquipoAcs
                    .Include(e => e.ClaveTipoEquipoNavigation)
                    .FirstOrDefault(e => e.NumActFijo == item.NumActFijo);

                var equipoComputo = _dbocontext.EquipoComputos
                    .Include(e => e.ClaveTipoEquipoNavigation)
                    .FirstOrDefault(e => e.NumActFijo == item.NumActFijo);

                EquipoCfematico? equipoCfematico = null;
                if (equipoAc == null && equipoComputo == null)
                {
                    equipoCfematico = _dbocontext.EquipoCfematicos
                        .FirstOrDefault(e => e.NumActFijo == item.NumActFijo);
                }

                // Actualizar tipo según el equipo encontrado
                if (equipoAc?.ClaveTipoEquipoNavigation != null)
                    tipo = equipoAc.ClaveTipoEquipoNavigation.NombreTipoEquipo;
                else if (equipoComputo?.ClaveTipoEquipoNavigation != null)
                    tipo = equipoComputo.ClaveTipoEquipoNavigation.NombreTipoEquipo;

                // Obtener datos de ubicación
                var centro = item.NumActFijoNavigation?.CatCentro;
                var nombreCentro = item.NumActFijoNavigation?.CatCentro?.NombreCentro ?? "Sin centro";
                var nombreAgencia = centro?.CatAgencium?.NombreAgencia ?? "Sin agencia";
                var nombreZona = centro?.CatAgencium?.CatZona?.NombreZona ?? "Sin zona";

                // Obtener el último motivo de cancelación CON INFORMACIÓN DEL CATÁLOGO
                var ultimoMotivo = item.MotivosCancelacion
                    .OrderByDescending(m => m.FechaSolicitud)
                    .FirstOrDefault();

                // Obtener el texto del motivo desde el catálogo
                var motivoTexto = ultimoMotivo?.CatMotivo?.MotivoCancelacion ?? "Motivo no especificado";

                // Crear ViewModel CON LOS MOTIVOS DESDE EL CATÁLOGO
                var viewModel = new VMAgendaVista
                {
                    NumActFijo = item.NumActFijo,
                    FechaProgramada = item.FechaProgramada,
                    Zona = nombreZona,
                    Agencia = nombreAgencia,
                    Centro = nombreCentro,
                    Tipo = tipo,
                    Estatus = item.Estatus,
                    NumCajero = equipoCfematico?.NumCajero ?? "N/A",
                    TipoMantenimiento = item.ClaveTipoMttoNavigation?.NombreTipoM ?? "PREVENTIVO",
                    ClaveAgenda = item.ClaveAgenda,
                    UsuarioAsignado = equipoComputo != null ?
                        $"{equipoComputo.Rpe} - {equipoComputo.NombreRpe}" : "N/A",

                    // NUEVOS CAMPOS DE MOTIVOS DESDE EL CATÁLOGO
                    MotivoCancelacion = motivoTexto, // ← Texto desde CatMotivoCancelacion
                    JustificacionCancelacion = ultimoMotivo?.Justificacion,
                    UsuarioSolicitudCancelacion = ultimoMotivo?.UsuarioSolicitud, // ← RPE
                    FechaSolicitudCancelacion = ultimoMotivo?.FechaSolicitud,
                    UsuarioAprobacionCancelacion = ultimoMotivo?.UsuarioAprobacion, // ← RPE
                    FechaAprobacionCancelacion = ultimoMotivo?.FechaAprobacion,

                    // NUEVO: Guardar también la clave del motivo para referencia
                    ClaveMotivo = ultimoMotivo?.ClaveMotivo ?? 0
                };

                citasPrecanceladas.Add(viewModel);
            }

            return View(citasPrecanceladas);
        }

        [HttpPost]
        public async Task<IActionResult> AgendarCorrectivo(string numActFijo, string fechaProgramada, string tipoEquipo = "CFEMatico")
        {
            try
            {
                // Validar parámetros obligatorios
                if (string.IsNullOrWhiteSpace(numActFijo))
                {
                    return Json(new { success = false, message = "El número de activo fijo es requerido" });
                }

                if (string.IsNullOrWhiteSpace(fechaProgramada))
                {
                    return Json(new { success = false, message = "La fecha programada es requerida" });
                }

                // Validar y convertir la fecha
                DateOnly fechaProgramadaDate;
                try
                {
                    fechaProgramadaDate = DateOnly.ParseExact(fechaProgramada, "yyyy-MM-dd");
                }
                catch
                {
                    return Json(new { success = false, message = "Formato de fecha inválido. Use YYYY-MM-DD" });
                }

                // Validar que la fecha no sea en el pasado
                if (fechaProgramadaDate < DateOnly.FromDateTime(DateTime.Now))
                {
                    return Json(new { success = false, message = "No puede agendar mantenimientos para fechas pasadas" });
                }

                // Verificar si el activo fijo existe
                var equipoExistente = _dbocontext.Equipos.Any(e => e.NumActFijo == numActFijo);
                if (!equipoExistente)
                {
                    return Json(new { success = false, message = $"El número de activo fijo: {numActFijo} no existe en el sistema" });
                }

                // Verificar que no exista un mantenimiento duplicado
                var mantenimientoExistente = _dbocontext.Agenda
                    .Any(a => a.NumActFijo == numActFijo &&
                             a.FechaProgramada == fechaProgramadaDate &&
                             a.ClaveTipoMtto == "C");

                if (mantenimientoExistente)
                {
                    return Json(new
                    {
                        success = false,
                        message = $"Ya existe un mantenimiento correctivo programado para este equipo el {fechaProgramadaDate:dd/MM/yyyy}"
                    });
                }

                // Crear y guardar el nuevo registro
                var nuevoCorrectivo = new Agendum
                {
                    NumActFijo = numActFijo,
                    ClaveTipoMtto = "C",
                    FechaProgramada = fechaProgramadaDate,
                    Estatus = "PENDIENTE"
                };

                _dbocontext.Agenda.Add(nuevoCorrectivo);

                // ✅ REGISTRO EN BITÁCORA MEJORADO
                var usuario = HttpContext.Session.GetString("NombreUsuario") ?? "Usuario no identificado";
                var rpe = HttpContext.Session.GetString("Rpe") ?? "N/A";
                var rol = HttpContext.Session.GetString("NombreRol") ?? "N/A";
                var zona = HttpContext.Session.GetString("NombreZona") ?? "N/A";
                var centro = HttpContext.Session.GetString("ClaveDivision") ?? "N/A";

                // Determinar el tipo de equipo para la bitácora
                string tipoEquipoBitacora = tipoEquipo switch
                {
                    "Computo" => "Equipo de Cómputo",
                    "AtencionCliente" => "Equipo de Atención a Clientes",
                    _ => "CFEMático"
                };

                // ✅ REGISTRO EN BITÁCORA CON EL EQUIPO ESPECÍFICO
                await _bitacora.RegistrarMantenimientoCorrectivoAsync(
                    usuario: usuario,
                    rpe: rpe,
                    rol: rol,
                    zona: zona,
                    centro: centro,
                    equipo: $"{numActFijo} ({tipoEquipoBitacora})"
                );

                await _dbocontext.SaveChangesAsync();

                // Determinar el tipo de equipo para el mensaje de respuesta
                string tipoEquipoMensaje = tipoEquipo switch
                {
                    "Computo" => "Equipo de Cómputo",
                    "AtencionCliente" => "Equipo de Atención a Clientes",
                    _ => "CFEMático"
                };
                // Retornar respuesta exitosa
                return Json(new
                {
                    success = true,
                    message = $"Mantenimiento correctivo agendado exitosamente.",
                    data = new
                    {
                        numActFijo = nuevoCorrectivo.NumActFijo,
                        fechaProgramada = nuevoCorrectivo.FechaProgramada.ToString("dd/MM/yyyy"),
                        tipo = "Correctivo",
                        tipoEquipo = tipoEquipoMensaje

                    }
                });
            }
            catch (DbUpdateException dbEx)
            {
                return Json(new
                {
                    success = false,
                    message = "Error al guardar en la base de datos",
                    error = dbEx.InnerException?.Message ?? dbEx.Message
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = "Error inesperado al procesar la solicitud",
                    error = ex.Message
                });
            }
        }

    }
}