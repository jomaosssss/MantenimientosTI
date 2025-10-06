using Microsoft.AspNetCore.Mvc;
using MantenimientosTI.Models;
using Microsoft.EntityFrameworkCore;
using System.Text;
using Microsoft.AspNetCore.Authorization;

namespace ProyectoMantenimientos.Controllers
{
    public class AgendaController : Controller
    {
        private readonly MantenimientosTIContext _dbocontext;

        public AgendaController(MantenimientosTIContext context)
        {
            _dbocontext = context;
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
        public IActionResult EnviarDatosCsv([FromForm] IFormFile ArchivoCsv)
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

                            // Validar y convertir la fecha - MODIFICADO PARA ACEPTAR DOS FORMATOS
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
                _dbocontext.Agenda.AddRange(lista);
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
        public IActionResult AgendarCorrectivo(string numActFijo, string fechaProgramada)
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
                    ClaveTipoMtto = "C", // Correctivo
                    FechaProgramada = fechaProgramadaDate,
                    Estatus = "PENDIENTE"
                };

                _dbocontext.Agenda.Add(nuevoCorrectivo);
                _dbocontext.SaveChanges();

                // Retornar respuesta exitosa
                return Json(new
                {
                    success = true,
                    message = "Mantenimiento correctivo agendado exitosamente",
                    data = new
                    {
                        numActFijo = nuevoCorrectivo.NumActFijo,
                        fechaProgramada = nuevoCorrectivo.FechaProgramada.ToString("dd/MM/yyyy"),
                        tipo = "Correctivo"
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
