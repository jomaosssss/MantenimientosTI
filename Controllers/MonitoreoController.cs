using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MantenimientosTI.Models;
using MantenimientosTI.Models.ViewModels;
using System.Linq;
using System;

namespace ProyectoMantenimientos.Controllers
{
    public class MonitoreoController : Controller
    {
        private readonly MantenimientosTIContext _dbocontext;

        public MonitoreoController(MantenimientosTIContext context)
        {
            _dbocontext = context;
        }

        [Authorize(Roles = "ADMINISTRADOR")]
        public IActionResult MonitoreoAdmin()
        {
            var zonasConCfematicos = _dbocontext.CatZonas
                .OrderBy(z => z.ClaveZona)  // Ordenar por ClaveZona en lugar de NombreZona
                .Select(zona => new VMMonitoreoAdmin
                {
                    Zona = zona,
                    Cfematicos = _dbocontext.Equipos
                        .Where(e => e.ClaveZona == zona.ClaveZona)
                        .Include(e => e.CatCentro)
                            .ThenInclude(c => c.CatAgencium)
                        .Join(_dbocontext.EquipoCfematicos,
                            equipo => equipo.NumActFijo,
                            cfematico => cfematico.NumActFijo,
                            (equipo, cfematico) => new
                            {
                                Cfematico = cfematico,
                                Agencia = equipo.CatCentro.CatAgencium.NombreAgencia ?? "",
                                Centro = equipo.CatCentro.NombreCentro ?? ""
                            })
                        .OrderBy(x => x.Agencia)
                        .ThenBy(x => x.Centro)
                        .ThenBy(x => x.Cfematico.NumCajero)
                        .Select(x => x.Cfematico)
                        .ToList()
                })
                .ToList();  // Eliminamos el OrderBy anterior ya que ahora ordenamos al principio

            return View(zonasConCfematicos);
        }

        [Authorize(Roles = "ADMINISTRADOR,TÉCNICO DE ZONA")]
        public IActionResult Monitoreo()
        {
            var claveZona = HttpContext.Session.GetString("ClaveZona");

            if (string.IsNullOrEmpty(claveZona))
            {
                return RedirectToAction("Error", "Home");
            }

            var cfematicos = _dbocontext.Equipos
                .Where(e => e.ClaveZona == claveZona)
                .Include(e => e.CatCentro)
                    .ThenInclude(c => c.CatAgencium)
                .Join(_dbocontext.EquipoCfematicos,
                    equipo => equipo.NumActFijo,
                    cfematico => cfematico.NumActFijo,
                    (equipo, cfematico) => new
                    {
                        Cfematico = cfematico,
                        Agencia = equipo.CatCentro.CatAgencium.NombreAgencia ?? "",
                        Centro = equipo.CatCentro.NombreCentro ?? ""
                    })
                .OrderBy(x => x.Agencia)
                .ThenBy(x => x.Centro)
                .ThenBy(x => x.Cfematico.NumCajero)
                .Select(x => x.Cfematico)
                .ToList();

            return View(cfematicos);
        }

        [HttpGet]
        public IActionResult ObtenerDetallesCfematico(string numCajero)
        {
            try
            {
                var cfematico = _dbocontext.EquipoCfematicos
                    .Include(e => e.NumActFijoNavigation)
                        .ThenInclude(e => e.CatCentro)
                            .ThenInclude(c => c.CatAgencium)
                                .ThenInclude(a => a.CatZona)
                    .FirstOrDefault(c => c.NumCajero == numCajero);

                if (cfematico == null)
                {
                    return Json(new { error = "CFEmático no encontrado" });
                }

                // Manejo seguro de propiedades nulas
                return Json(new
                {
                    numCajero = cfematico.NumCajero ?? "-",
                    numActFijo = cfematico.NumActFijo ?? "-",
                    numSerie = cfematico.NumSerie ?? "-",
                    numInventario = cfematico.NumInventario ?? "-",
                    ipCajero = cfematico.IpCajero ?? "-",
                    zona = cfematico.NumActFijoNavigation?.CatCentro?.CatAgencium?.CatZona?.NombreZona ?? "-",
                    agencia = cfematico.NumActFijoNavigation?.CatCentro?.CatAgencium?.NombreAgencia ?? "-",
                    centro = cfematico.NumActFijoNavigation?.CatCentro?.NombreCentro ?? "-"
                });
            }
            catch (Exception ex)
            {
                // Log del error (implementa esto según tu sistema de logging)
                Console.WriteLine($"Error en ObtenerDetallesCfematico: {ex.Message}");
                return Json(new { error = "Error al obtener detalles" });
            }
        }

        [HttpGet]
        public IActionResult ObtenerEstadosCfematicos()
        {
            var estados = _dbocontext.EquipoCfematicos
                .Select(c => c.NumCajero)
                .ToList()
                .ToDictionary(
                    numCajero => numCajero,
                    numCajero => _dbocontext.RegistroEventos
                        .Where(re => re.NumCajero == numCajero)
                        .OrderByDescending(re => re.FechaEvento)
                        .FirstOrDefault()?.ClaveEvento ?? "N"  // "N" si no hay eventos
                );

            return Json(estados);
        }


        //[HttpGet]
        //public IActionResult ObtenerEstadoCfematico(string numCajero)
        //{
        //    // Obtener el evento más reciente para determinar el estado
        //    var ultimoEvento = _dbocontext.RegistroEventos
        //        .Where(re => re.NumCajero == numCajero)
        //        .OrderByDescending(re => re.FechaEvento)
        //        .FirstOrDefault();

        //    if (ultimoEvento == null)
        //    {
        //        return Json("N"); // Si no hay eventos, asumimos normal
        //    }

        //    var evento = _dbocontext.CatEventos
        //        .FirstOrDefault(e => e.ClaveEvento == ultimoEvento.ClaveEvento);

        //    return Json(evento?.ClaveFalla ?? "N");
        //}

        [HttpGet]
        public IActionResult ObtenerEstadisticasEventos(string numCajero, int dias = 7)
        {
            var fechaLimite = DateTime.Now.AddDays(-dias);

            var estadisticas = _dbocontext.RegistroEventos
                .Where(re => re.NumCajero == numCajero && re.FechaEvento >= fechaLimite)
                .Join(_dbocontext.CatEventos,
                    registro => registro.ClaveEvento,
                    evento => evento.ClaveEvento,
                    (registro, evento) => new {
                        evento.Severidad,
                        evento.ClaveFalla // Agregamos ClaveFalla para distinguir entre T y O
                    })
                .GroupBy(x => new { x.Severidad, x.ClaveFalla }) // Agrupamos por Severidad y ClaveFalla
                .Select(g => new {
                    Severidad = g.Key.Severidad,
                    Tipo = g.Key.ClaveFalla, // "T" para técnico, "O" para operativo
                    Cantidad = g.Count()
                })
                .ToList();

            // Inicializamos todas las posibles combinaciones
            var criticosOperativos = estadisticas.FirstOrDefault(x => x.Severidad == 100 && x.Tipo == "O")?.Cantidad ?? 0;
            var criticosTecnicos = estadisticas.FirstOrDefault(x => x.Severidad == 100 && x.Tipo == "T")?.Cantidad ?? 0;
            var altosOperativos = estadisticas.FirstOrDefault(x => x.Severidad == 50 && x.Tipo == "O")?.Cantidad ?? 0;
            var altosTecnicos = estadisticas.FirstOrDefault(x => x.Severidad == 50 && x.Tipo == "T")?.Cantidad ?? 0;
            var bajosOperativos = estadisticas.FirstOrDefault(x => x.Severidad == 0 && x.Tipo == "O")?.Cantidad ?? 0;
            var bajosTecnicos = estadisticas.FirstOrDefault(x => x.Severidad == 0 && x.Tipo == "T")?.Cantidad ?? 0;

            return Json(new
            {
                criticosOperativos,
                criticosTecnicos,
                altosOperativos,
                altosTecnicos,
                bajosOperativos,
                bajosTecnicos,
                total = estadisticas.Sum(x => x.Cantidad)
            });
        }

        [HttpGet]
        public IActionResult ObtenerEventosRecientes(string numCajero, int dias = 7)
        {
            var fechaLimite = DateTime.Now.AddDays(-dias);

            var eventos = _dbocontext.RegistroEventos
                .Where(re => re.NumCajero == numCajero && re.FechaEvento >= fechaLimite)
                .OrderByDescending(re => re.FechaEvento)
                .Take(5)
                .Join(_dbocontext.CatEventos,
                    registro => registro.ClaveEvento,
                    evento => evento.ClaveEvento,
                    (registro, evento) => new {
                        fuente = evento.Fuente,
                        descripcion = evento.Descripcion,
                        severidad = evento.Severidad,
                        fecha = registro.FechaEvento.ToString("g")
                    })
                .ToList();

            return Json(eventos);
        }

        [HttpGet]
        public IActionResult ObtenerEstadisticasAvanzadas(string numCajero, int dias = 7)
        {
            var fechaLimite = DateTime.Now.AddDays(-dias);

            var eventos = _dbocontext.RegistroEventos
                .Where(re => re.NumCajero == numCajero && re.FechaEvento >= fechaLimite)
                .OrderBy(re => re.FechaEvento)
                .Join(_dbocontext.CatEventos,
                    registro => registro.ClaveEvento,
                    evento => evento.ClaveEvento,
                    (registro, evento) => new {
                        registro.FechaEvento,
                        evento.ClaveEvento,
                        evento.Fuente,
                        evento.Descripcion,
                        evento.Severidad
                    })
                .ToList();

            if (!eventos.Any())
            {
                return Json(new
                {
                    mensaje = "No hay suficientes datos para análisis estadístico"
                });
            }

            // Eventos más comunes (top 5)
            var comunes = eventos
                .GroupBy(e => e.ClaveEvento)
                .OrderByDescending(g => g.Count())
                .Take(5)
                .Select(g => new {
                    descripcion = g.First().Descripcion,
                    cantidad = g.Count()
                })
                .ToList();

            // Tiempo promedio entre fallos (en horas)
            double? tiempoPromedio = null;
            if (eventos.Count > 1)
            {
                var diferencias = new List<double>();
                for (int i = 1; i < eventos.Count; i++)
                {
                    diferencias.Add((eventos[i].FechaEvento - eventos[i - 1].FechaEvento).TotalHours);
                }
                tiempoPromedio = diferencias.Any() ? diferencias.Average() : (double?)null;
            }

            return Json(new
            {
                comunes,
                tiempoPromedio,
                totalEventos = eventos.Count
            });
        }

        [HttpGet]
        public IActionResult ObtenerEventosFiltrados(string numCajero, int severidad, string tipo, int dias = 7)
        {
            var fechaLimite = DateTime.Now.AddDays(-dias);

            var eventos = _dbocontext.RegistroEventos
                .Where(re => re.NumCajero == numCajero &&
                             re.FechaEvento >= fechaLimite)
                .Join(_dbocontext.CatEventos,
                    registro => registro.ClaveEvento,
                    evento => evento.ClaveEvento,
                    (registro, evento) => new {
                        registro.FechaEvento,
                        evento.Fuente,
                        evento.Descripcion,
                        evento.Severidad,
                        evento.ClaveFalla
                    })
                .Where(x => x.Severidad == severidad && x.ClaveFalla == tipo)
                .OrderByDescending(x => x.FechaEvento)
                .Select(x => new {
                    fuente = x.Fuente,
                    descripcion = x.Descripcion,
                    severidad = x.Severidad,
                    fecha = x.FechaEvento.ToString("g")
                })
                .ToList();

            return Json(eventos);
        }
    }
}