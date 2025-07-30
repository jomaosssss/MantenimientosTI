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
                .OrderBy(z => z.ClaveZona)
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
                .ToList();

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
                        .FirstOrDefault()?.ClaveEvento ?? "N"
                );

            return Json(estados);
        }

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
                        evento.ClaveFalla
                    })
                .GroupBy(x => new { x.Severidad, x.ClaveFalla })
                .Select(g => new {
                    Severidad = g.Key.Severidad,
                    Tipo = g.Key.ClaveFalla,
                    Cantidad = g.Count()
                })
                .ToList();

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

            var comunes = eventos
                .GroupBy(e => e.ClaveEvento)
                .OrderByDescending(g => g.Count())
                .Take(5)
                .Select(g => new {
                    descripcion = g.First().Descripcion,
                    cantidad = g.Count()
                })
                .ToList();

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

        [HttpGet]
        public IActionResult ObtenerTiposFalla()
        {
            var tiposFalla = _dbocontext.CatFallas
                .OrderBy(f => f.Descripcion)
                .Select(f => new {
                    claveFalla = f.ClaveFalla,
                    descripcion = f.Descripcion
                })
                .ToList();

            return Json(tiposFalla);
        }

        [HttpGet]
        public IActionResult ObtenerEstadisticasEventosPorMes(string claveFalla = "", int year = 2025, string numCajero = "")
        {
            var query = _dbocontext.CatEventos
                .Join(_dbocontext.CatFallas,
                    evento => evento.ClaveFalla,
                    falla => falla.ClaveFalla,
                    (evento, falla) => new { evento, falla })
                .GroupJoin(_dbocontext.RegistroEventos,
                    ef => ef.evento.ClaveEvento,
                    registro => registro.ClaveEvento,
                    (ef, registros) => new { ef.evento, ef.falla, registros })
                .SelectMany(
                    x => x.registros.DefaultIfEmpty(),
                    (x, registro) => new {
                        x.evento.Descripcion,
                        x.falla.ClaveFalla,
                        NumCajero = registro != null ? registro.NumCajero : null,
                        FechaEvento = registro != null ? registro.FechaEvento : (DateTime?)null
                    })
                .Where(x => x.FechaEvento != null &&
                           x.FechaEvento.Value.Year == year &&
                           (string.IsNullOrEmpty(numCajero) || x.NumCajero == numCajero));

            if (!string.IsNullOrEmpty(claveFalla))
            {
                query = query.Where(x => x.ClaveFalla == claveFalla);
            }

            var resultados = query
                .AsEnumerable()
                .GroupBy(x => x.Descripcion)
                .Select(g => new {
                    descripcion = g.Key,
                    ene = g.Count(x => x.FechaEvento?.Month == 1),
                    feb = g.Count(x => x.FechaEvento?.Month == 2),
                    mar = g.Count(x => x.FechaEvento?.Month == 3),
                    abr = g.Count(x => x.FechaEvento?.Month == 4),
                    may = g.Count(x => x.FechaEvento?.Month == 5),
                    jun = g.Count(x => x.FechaEvento?.Month == 6),
                    jul = g.Count(x => x.FechaEvento?.Month == 7),
                    ago = g.Count(x => x.FechaEvento?.Month == 8),
                    sep = g.Count(x => x.FechaEvento?.Month == 9),
                    oct = g.Count(x => x.FechaEvento?.Month == 10),
                    nov = g.Count(x => x.FechaEvento?.Month == 11),
                    dic = g.Count(x => x.FechaEvento?.Month == 12)
                })
                .OrderBy(x => x.descripcion)
                .ToList();

            return Json(resultados);
        }

        [HttpGet]
        public IActionResult ObtenerEventosMensuales(string numCajero, string descripcion, int mes, int year, string claveFalla = "")
        {
            var eventos = _dbocontext.RegistroEventos
                .Where(re => re.NumCajero == numCajero &&
                            re.FechaEvento.Year == year &&
                            re.FechaEvento.Month == mes)
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
                .Where(x => x.Descripcion == descripcion &&
                           (string.IsNullOrEmpty(claveFalla) || x.ClaveFalla == claveFalla))
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