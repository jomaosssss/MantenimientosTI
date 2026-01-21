using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MantenimientosTI.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System;
using Microsoft.AspNetCore.Http;

namespace MantenimientosTI.Controllers
{
    public class RefaccionesController : Controller
    {
        private readonly MantenimientosTIContext _context;

        public RefaccionesController(MantenimientosTIContext context)
        {
            _context = context;
        }

        public IActionResult Refacciones()
        {
            var claveDivision = HttpContext.Session.GetString("ClaveDivision");
            var claveZona = HttpContext.Session.GetString("ClaveZona");
            var rol = HttpContext.Session.GetString("NombreRol");

            IQueryable<Refaccion> query = _context.Refacciones
                .Include(r => r.CatTipoRefaccion);

            // Si no es ADMIN, filtrar por zona
            if (rol != "ADMINISTRADOR")
            {
                if (string.IsNullOrEmpty(claveDivision) || string.IsNullOrEmpty(claveZona))
                {
                    TempData["Error"] = "No se pudo determinar la zona del usuario.";
                    return RedirectToAction("Login", "Usuario");
                }

                query = query.Where(r => r.ClaveDivision == claveDivision && r.ClaveZona == claveZona);
            }

            var refacciones = query.ToList();

            // Pasar la información de zona a la vista para mostrarla
            ViewBag.ClaveDivision = claveDivision;
            ViewBag.ClaveZona = claveZona;
            ViewBag.NombreZona = HttpContext.Session.GetString("NombreZona") ?? "Sin nombre";

            return View(refacciones);
        }

        [HttpGet]
        public IActionResult ObtenerTiposRefaccion()
        {
            try
            {
                var tipos = _context.CatTipoRefacciones
                    .Select(t => new
                    {
                        t.ClaveTipoRefaccion,
                        t.NombreTipoRefaccion
                    })
                    .OrderBy(t => t.NombreTipoRefaccion)
                    .ToList();

                return Json(new { success = true, data = tipos });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = $"Error al cargar tipos de refacción: {ex.Message}"
                });
            }
        }

        [HttpPost]
        public IActionResult CrearRefaccion([FromBody] Refaccion refaccion)
        {
            try
            {
                var claveDivision = HttpContext.Session.GetString("ClaveDivision");
                var claveZona = HttpContext.Session.GetString("ClaveZona");

                if (string.IsNullOrEmpty(claveDivision) || string.IsNullOrEmpty(claveZona))
                {
                    return Json(new
                    {
                        success = false,
                        message = "No se pudo determinar la zona del usuario. Por favor, inicie sesión nuevamente."
                    });
                }

                // Validar que el tipo de refacción exista
                var tipoExiste = _context.CatTipoRefacciones
                    .Include(t => t.CatTipoUnidadMedida)
                    .FirstOrDefault(t => t.ClaveTipoRefaccion == refaccion.ClaveTipoRefaccion);

                if (tipoExiste == null)
                {
                    return Json(new
                    {
                        success = false,
                        message = "El tipo de refacción seleccionado no existe."
                    });
                }

                // Validaciones según el tipo de unidad de medida
                if (tipoExiste.CatTipoUnidadMedida.NumSerieRequerido == "SI")
                {
                    // Caso 1: Requiere número de serie
                    if (string.IsNullOrEmpty(refaccion.NumeroSerie))
                    {
                        return Json(new
                        {
                            success = false,
                            message = "El número de serie es requerido para este tipo de refacción."
                        });
                    }
                    // No se guarda cantidad
                    refaccion.Cantidad = null;
                }
                else
                {
                    // Caso 2 y 3: No requiere número de serie, requiere cantidad
                    if (!refaccion.Cantidad.HasValue || refaccion.Cantidad <= 0)
                    {
                        return Json(new
                        {
                            success = false,
                            message = "La cantidad es requerida para este tipo de refacción."
                        });
                    }
                    // No se guarda número de serie
                    refaccion.NumeroSerie = null;
                }

                // Asignar automáticamente la división y zona del usuario
                refaccion.ClaveDivision = claveDivision;
                refaccion.ClaveZona = claveZona;
                refaccion.Ocupado = "NO";

                // Validar que exista la zona en CatZona
                var zonaExiste = _context.CatZonas
                    .Any(z => z.ClaveDivision == claveDivision && z.ClaveZona == claveZona);

                if (!zonaExiste)
                {
                    return Json(new
                    {
                        success = false,
                        message = "La zona asignada al usuario no existe en el sistema."
                    });
                }

                _context.Refacciones.Add(refaccion);
                _context.SaveChanges();

                return Json(new
                {
                    success = true,
                    message = "Refacción creada exitosamente",
                    id = refaccion.ClaveRefaccion
                });
            }
            catch (Exception ex)
            {
                string errorMessage = ex.Message;
                if (ex.InnerException != null)
                {
                    errorMessage += $" | Inner: {ex.InnerException.Message}";
                }

                Console.WriteLine($"ERROR CREAR REFACCION: {errorMessage}");

                return Json(new
                {
                    success = false,
                    message = $"Error al crear refacción: {errorMessage}"
                });
            }
        }

        [HttpGet]
        public IActionResult ObtenerRefaccionPorId(int id)
        {
            try
            {
                var claveDivision = HttpContext.Session.GetString("ClaveDivision");
                var claveZona = HttpContext.Session.GetString("ClaveZona");

                var refaccion = _context.Refacciones
                    .Include(r => r.CatTipoRefaccion)
                        .ThenInclude(t => t.CatTipoUnidadMedida)
                    .FirstOrDefault(r => r.ClaveRefaccion == id &&
                                        r.ClaveDivision == claveDivision &&
                                        r.ClaveZona == claveZona);

                if (refaccion == null)
                {
                    return Json(new
                    {
                        success = false,
                        message = "Refacción no encontrada o no tienes permisos para acceder a ella."
                    });
                }

                return Json(new
                {
                    success = true,
                    data = new
                    {
                        claveRefaccion = refaccion.ClaveRefaccion,
                        claveTipoRefaccion = refaccion.ClaveTipoRefaccion,
                        nombreTipoRefaccion = refaccion.CatTipoRefaccion?.NombreTipoRefaccion,
                        claveTipoUnidadMedida = refaccion.CatTipoRefaccion?.ClaveTipoUnidadMedida,
                        numSerieRequerido = refaccion.CatTipoRefaccion?.CatTipoUnidadMedida?.NumSerieRequerido,
                        numeroSerie = refaccion.NumeroSerie,
                        cantidad = refaccion.Cantidad,
                        modelo = refaccion.Modelo,
                        marca = refaccion.Marca,
                        fechaAdquisicion = refaccion.FechaAdquisicion.ToString("yyyy-MM-dd"),
                        numContratoAdquisicion = refaccion.NumContratoAdquisicion,
                        ocupado = refaccion.Ocupado
                    }
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        [HttpGet]
        public IActionResult ObtenerInfoTipoRefaccion(int id)
        {
            try
            {
                var tipoRefaccion = _context.CatTipoRefacciones
                    .Include(t => t.CatTipoUnidadMedida)
                    .Select(t => new
                    {
                        t.ClaveTipoRefaccion,
                        t.NombreTipoRefaccion,
                        t.ClaveTipoUnidadMedida,
                        nombreUnidadMedida = t.CatTipoUnidadMedida.NombreUnidadMedida,
                        numSerieRequerido = t.CatTipoUnidadMedida.NumSerieRequerido
                    })
                    .FirstOrDefault(t => t.ClaveTipoRefaccion == id);

                if (tipoRefaccion == null)
                {
                    return Json(new { success = false, message = "Tipo de refacción no encontrado" });
                }

                return Json(new { success = true, data = tipoRefaccion });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        [HttpPost]
        public IActionResult ActualizarRefaccion([FromBody] Refaccion refaccion)
        {
            try
            {
                var claveDivision = HttpContext.Session.GetString("ClaveDivision");
                var claveZona = HttpContext.Session.GetString("ClaveZona");

                var existingRefaccion = _context.Refacciones
                    .FirstOrDefault(r => r.ClaveRefaccion == refaccion.ClaveRefaccion &&
                                        r.ClaveDivision == claveDivision &&
                                        r.ClaveZona == claveZona);

                if (existingRefaccion == null)
                {
                    return Json(new
                    {
                        success = false,
                        message = "Refacción no encontrada o no tienes permisos para editarla."
                    });
                }

                // Obtener información del tipo de refacción
                var tipoRefaccion = _context.CatTipoRefacciones
                    .Include(t => t.CatTipoUnidadMedida)
                    .FirstOrDefault(t => t.ClaveTipoRefaccion == refaccion.ClaveTipoRefaccion);

                if (tipoRefaccion == null)
                {
                    return Json(new
                    {
                        success = false,
                        message = "El tipo de refacción seleccionado no existe."
                    });
                }

                // Validaciones según el tipo de unidad de medida
                if (tipoRefaccion.CatTipoUnidadMedida.NumSerieRequerido == "SI")
                {
                    // Caso 1: Requiere número de serie
                    if (string.IsNullOrEmpty(refaccion.NumeroSerie))
                    {
                        return Json(new
                        {
                            success = false,
                            message = "El número de serie es requerido para este tipo de refacción."
                        });
                    }
                    existingRefaccion.NumeroSerie = refaccion.NumeroSerie;
                    existingRefaccion.Cantidad = null;
                }
                else
                {
                    // Caso 2 y 3: No requiere número de serie, requiere cantidad
                    if (!refaccion.Cantidad.HasValue || refaccion.Cantidad <= 0)
                    {
                        return Json(new
                        {
                            success = false,
                            message = "La cantidad es requerida para este tipo de refacción."
                        });
                    }
                    existingRefaccion.NumeroSerie = null;
                    existingRefaccion.Cantidad = refaccion.Cantidad;
                }

                existingRefaccion.ClaveTipoRefaccion = refaccion.ClaveTipoRefaccion;
                existingRefaccion.Modelo = refaccion.Modelo;
                existingRefaccion.Marca = refaccion.Marca;
                existingRefaccion.FechaAdquisicion = refaccion.FechaAdquisicion;
                existingRefaccion.NumContratoAdquisicion = refaccion.NumContratoAdquisicion;

                _context.SaveChanges();
                return Json(new
                {
                    success = true,
                    message = "Refacción actualizada exitosamente"
                });
            }
            catch (Exception ex)
            {
                string errorMessage = ex.Message;
                if (ex.InnerException != null)
                {
                    errorMessage += $" | Inner: {ex.InnerException.Message}";
                }
                return Json(new
                {
                    success = false,
                    message = $"Error al actualizar refacción: {errorMessage}"
                });
            }
        }

        [HttpPost]
        public IActionResult EliminarRefaccion(int id)
        {
            try
            {
                // Obtener la división y zona del usuario logueado
                var claveDivision = HttpContext.Session.GetString("ClaveDivision");
                var claveZona = HttpContext.Session.GetString("ClaveZona");

                // Buscar la refacción SOLO si pertenece a la zona del usuario
                var refaccion = _context.Refacciones
                    .FirstOrDefault(r => r.ClaveRefaccion == id &&
                                        r.ClaveDivision == claveDivision &&
                                        r.ClaveZona == claveZona);

                if (refaccion != null)
                {
                    _context.Refacciones.Remove(refaccion);
                    _context.SaveChanges();
                    return Json(new
                    {
                        success = true,
                        message = "Refacción eliminada exitosamente"
                    });
                }

                return Json(new
                {
                    success = false,
                    message = "Refacción no encontrada o no tienes permisos para eliminarla"
                });
            }
            catch (Exception ex)
            {
                string errorMessage = ex.Message;
                if (ex.InnerException != null)
                {
                    errorMessage += $" | Inner: {ex.InnerException.Message}";
                }
                return Json(new
                {
                    success = false,
                    message = $"Error al eliminar refacción: {errorMessage}"
                });
            }
        }

        // Método opcional: Para obtener las zonas disponibles (si necesitas dropdowns)
        [HttpGet]
        public IActionResult ObtenerZonasPorDivision(string claveDivision)
        {
            var zonas = _context.CatZonas
                .Where(z => z.ClaveDivision == claveDivision)
                .Select(z => new { z.ClaveZona, z.NombreZona })
                .ToList();

            return Json(zonas);
        }
    }
}