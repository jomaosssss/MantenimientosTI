using Microsoft.AspNetCore.Mvc;
using MantenimientosTI.Models;
using Microsoft.EntityFrameworkCore;
using MantenimientosTI.Models.ViewModels;
using iTextSharp.text.pdf;
using Microsoft.AspNetCore.Authorization;
using MantenimientosTI.Models;

namespace ProyectoMantenimientos.Controllers
{
    public class HomeController : Controller
    {
        private readonly MantenimientosTIContext _dbocontext;

        private readonly IWebHostEnvironment _env;

        private readonly string _rutaPlantillaCFE = Path.Combine(
            Directory.GetCurrentDirectory(),
            "wwwroot",
            "Plantillas",
            "CFEMATICO.pdf");

        private readonly string _rutaPlantillaCFETURNO = Path.Combine(
            Directory.GetCurrentDirectory(),
            "wwwroot",
            "Plantillas",
            "CFETURNO.pdf");

        private readonly string _rutaPlantillaCFECAM = Path.Combine(
            Directory.GetCurrentDirectory(),
            "wwwroot",
            "Plantillas",
            "CFECAM.pdf");

        private readonly string _rutaPlantillaComputo = Path.Combine(
            Directory.GetCurrentDirectory(),
            "wwwroot",
            "Plantillas",
            "EQUIPODECOMPUTO.pdf");
        public HomeController(MantenimientosTIContext context, IWebHostEnvironment env)
        {
            _dbocontext = context;
            _env = env;
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [Authorize]
        public IActionResult AccessDenied()
        {
            return View();
        }

        [Authorize]
        public IActionResult Dashboard()
        {
            return View();
        }

        [Authorize(Roles = "ADMINISTRADOR,TÉCNICO DE ZONA")]
        public IActionResult Inicio()
        {
            string? claveZonaUsuario = HttpContext.Session.GetString("ClaveZona");
            int claveRol = HttpContext.Session.GetInt32("Rol") ?? 0;
            var hoy = DateOnly.FromDateTime(DateTime.Now);

            // Obtener primer y último día del mes actual
            var primerDiaMes = new DateOnly(hoy.Year, hoy.Month, 1);
            var ultimoDiaMes = new DateOnly(hoy.Year, hoy.Month, DateTime.DaysInMonth(hoy.Year, hoy.Month));

            var primerDiaProximoMes = primerDiaMes.AddMonths(1);
            var ultimoDiaProximoMes = new DateOnly(
                primerDiaProximoMes.Year,
                primerDiaProximoMes.Month,
                DateTime.DaysInMonth(primerDiaProximoMes.Year, primerDiaProximoMes.Month)
            );

            // 1. CONSULTAS PARA LOS CONTADORES DE LAS TARJETAS

            var terminadosQuery = _dbocontext.Agenda
                .Include(a => a.NumActFijoNavigation)
                .ThenInclude(e => e.CatCentro)
                .ThenInclude(c => c.CatAgencium)
                .Where(a => a.Estatus == "TERMINADO" &&
               a.FechaProgramada >= primerDiaMes &&
               a.FechaProgramada <= ultimoDiaMes);

            // Consulta para contar pendientes (SOLO de este mes)
            var pendientesQuery = _dbocontext.Agenda
                .Include(a => a.NumActFijoNavigation)
                    .ThenInclude(e => e.CatCentro)
                        .ThenInclude(c => c.CatAgencium)
                .Where(a => a.Estatus == "PENDIENTE" &&
                       a.FechaProgramada >= primerDiaMes && // <-- AÑADE ESTA LÍNEA
                       a.FechaProgramada <= ultimoDiaMes);

            // NUEVA CONSULTA: Todos los programados este mes (sin importar estatus)
            var programadosQuery = _dbocontext.Agenda
                .Include(a => a.NumActFijoNavigation)
                    .ThenInclude(e => e.CatCentro)
                        .ThenInclude(c => c.CatAgencium)
                .Where(a => a.FechaProgramada >= primerDiaMes &&
                           a.FechaProgramada <= ultimoDiaMes);

            var proximoMesQuery = _dbocontext.Agenda
                .Include(a => a.NumActFijoNavigation)
                    .ThenInclude(e => e.CatCentro)
                        .ThenInclude(c => c.CatAgencium)
                .Where(a => a.FechaProgramada >= primerDiaProximoMes &&
                        a.FechaProgramada <= ultimoDiaProximoMes);

            // Aplicar filtro por zona SOLO si NO es administrador (Rol = 1)
            if (claveRol != 1 && !string.IsNullOrEmpty(claveZonaUsuario))
            {
                terminadosQuery = terminadosQuery
                    .Where(a => a.NumActFijoNavigation != null &&
                               a.NumActFijoNavigation.ClaveZona == claveZonaUsuario);

                pendientesQuery = pendientesQuery
                    .Where(a => a.NumActFijoNavigation != null &&
                               a.NumActFijoNavigation.ClaveZona == claveZonaUsuario);

                programadosQuery = programadosQuery
                    .Where(a => a.NumActFijoNavigation != null &&
                               a.NumActFijoNavigation.ClaveZona == claveZonaUsuario);
                proximoMesQuery = proximoMesQuery
                    .Where(a => a.NumActFijoNavigation != null &&
                        a.NumActFijoNavigation.ClaveZona == claveZonaUsuario);
            }

            // Obtener los conteos
            int terminadosCount = terminadosQuery.Count();
            int pendientesCount = pendientesQuery.Count();
            int programadosCount = programadosQuery.Count();
            int proximoMesCount = proximoMesQuery.Count();

            // 2. CONSULTA PARA LAS TABLAS (SOLO PENDIENTES - mantiene el filtro original)
            var agendaQuery = _dbocontext.Agenda
                .Include(a => a.NumActFijoNavigation)
                    .ThenInclude(e => e.CatCentro)
                        .ThenInclude(c => c.CatAgencium)
                            .ThenInclude(a => a.CatZona)
                .Include(a => a.ClaveTipoMttoNavigation)
                .Where(a => a.Estatus == "PENDIENTE");

            // Filtro por zona si no es administrador
            if (claveRol != 1 && !string.IsNullOrEmpty(claveZonaUsuario))
            {
                agendaQuery = agendaQuery
                    .Where(a => a.NumActFijoNavigation != null &&
                               a.NumActFijoNavigation.ClaveZona == claveZonaUsuario);
            }

            var agenda = agendaQuery.ToList();

            // 3. CLASIFICAR LOS EQUIPOS EN LAS LISTAS CORRESPONDIENTES
            var cfematicos = new List<VMAgendaVista>();
            var atencionClientes = new List<VMAgendaVista>();
            var computo = new List<VMAgendaVista>();

            foreach (var item in agenda)
            {
                string tipo = "CFEMÁTICO";

                // Determinar el tipo de equipo
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

                // Crear ViewModel
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
                    ClaveAgenda = item.ClaveAgenda
                };

                // Clasificar en las listas correspondientes
                if (equipoAc != null)
                    atencionClientes.Add(viewModel);
                else if (equipoComputo != null)
                {
                    // --- LÍNEAS NUEVAS ---
                    // Si el equipo es de cómputo, preparamos la cadena del usuario.
                    string rpe = equipoComputo.Rpe;
                    string nombre = equipoComputo.NombreRpe;

                    // Verificamos si los datos existen para evitar mostrar "- "
                    if (!string.IsNullOrEmpty(rpe) && !string.IsNullOrEmpty(nombre))
                    {
                        viewModel.UsuarioAsignado = $"{rpe} - {nombre}";
                    }
                    else
                    {
                        viewModel.UsuarioAsignado = "No asignado";
                    }

                    computo.Add(viewModel); // <-- Ahora el viewModel lleva el dato extra
                }
                else
                    cfematicos.Add(viewModel);
            }

            // 4. CREAR EL VIEWMODEL FINAL
            var vmInicio = new VMAgendaInicio
            {
                Cfematicos = cfematicos.OrderBy(vm => vm.FechaProgramada).ToList(),
                AtencionClientes = atencionClientes.OrderBy(vm => vm.FechaProgramada).ToList(),
                Computo = computo.OrderBy(vm => vm.FechaProgramada).ToList(),
                TerminadosCount = terminadosCount,
                PendientesCount = pendientesCount,
                ProgramadosCount = programadosCount, // Nueva propiedad
                ProgramadosProximoMesCount = proximoMesCount
            };

            return View(vmInicio);
        }

        // En el archivo: /Controllers/HomeController.cs

        // --- REEMPLAZA TU MÉTODO CON ESTE ---
        [HttpGet]
        // 1. Añadimos 'int claveAgenda' para recibir el dato del JavaScript.
        public IActionResult ObtenerDetallesEquipo(string numActFijo, int claveAgenda)
        {
            // 2. Guardamos el valor recibido en el ViewBag para que las vistas parciales lo usen.
            ViewBag.NumeroDeOrden = claveAgenda;

            // El resto de tu código para buscar el equipo está perfecto y no cambia.
            var cfematico = _dbocontext.EquipoCfematicos
                .Include(e => e.NumActFijoNavigation)
                    .ThenInclude(e => e.CatCentro)
                        .ThenInclude(c => c.CatAgencium)
                            .ThenInclude(a => a.CatZona)
                .FirstOrDefault(e => e.NumActFijo == numActFijo);

            if (cfematico != null)
            {
                return PartialView("_DetallesCFEmatico", cfematico);
            }

            var equipoAC = _dbocontext.EquipoAcs
                .Include(e => e.NumActFijoNavigation)
                    .ThenInclude(e => e.CatCentro)
                        .ThenInclude(c => c.CatAgencium)
                            .ThenInclude(a => a.CatZona)
                .Include(e => e.ClaveTipoEquipoNavigation)
                .FirstOrDefault(e => e.NumActFijo == numActFijo);

            if (equipoAC != null)
            {
                return PartialView("_DetallesEquipoAC", equipoAC);
            }

            var equipoComputo = _dbocontext.EquipoComputos
                .Include(e => e.NumActFijoNavigation)
                    .ThenInclude(e => e.CatCentro)
                        .ThenInclude(c => c.CatAgencium)
                            .ThenInclude(a => a.CatZona)
                .Include(e => e.ClaveTipoEquipoNavigation)
                .FirstOrDefault(e => e.NumActFijo == numActFijo);

            if (equipoComputo != null)
            {
                return PartialView("_DetallesEquipoComputo", equipoComputo);
            }

            return NotFound();
        }

        [HttpPost]
        public IActionResult GenerarHojaServicio(
            string numActFijo,
            string fechaProgramada,
            string responsable = "TECNICO_DE_ZONA",
            string nombreTecnico = null,
            string tipoEquipo = null)
        {
            try
            {
                string nombreUsuario = HttpContext.Session.GetString("NombreUsuario") ?? "Técnico de Zona";
                var fecha = DateOnly.Parse(fechaProgramada);

                // 1) Buscar en CFEmáticos
                var cfData = (
                    from a in _dbocontext.Agenda
                    join b in _dbocontext.EquipoCfematicos
                           on a.NumActFijo equals b.NumActFijo
                    where a.NumActFijo == numActFijo
                        && a.FechaProgramada == fecha
                    select new
                    {
                        Division = b.NumActFijoNavigation.ClaveDivision,
                        Zona = b.NumActFijoNavigation.CatCentro.CatAgencium.CatZona.NombreZona,
                        Agencia = b.NumActFijoNavigation.CatCentro.CatAgencium.NombreAgencia,
                        b.NumCajero,
                        b.NumSerie,
                        b.NumInventario,
                        ClaveAgenda = a.ClaveAgenda.ToString(),
                        TipoMantenimiento = a.ClaveTipoMttoNavigation.ClaveTipoMtto
                    }
                ).FirstOrDefault();

                if (cfData != null)
                {
                    string nombreResponsable = responsable == "TECNICO_DE_ZONA"
                        ? nombreUsuario
                        : nombreTecnico ?? "Técnico Externo";

                    var pdf = GenerarPdfCFE(
                        cfData.Division,
                        cfData.Zona,
                        cfData.Agencia,
                        cfData.NumCajero,
                        cfData.NumSerie,
                        cfData.NumInventario,
                        cfData.ClaveAgenda,
                        fecha,
                        nombreResponsable,
                        cfData.TipoMantenimiento);

                    return File(pdf, "application/pdf", $"HojaServicioCFEmatico_{numActFijo}.pdf");
                }

                // 2) Buscar en Equipos de Atención a Clientes
                var acData = (
                    from a in _dbocontext.Agenda
                    join ac in _dbocontext.EquipoAcs
                            on a.NumActFijo equals ac.NumActFijo
                    where a.NumActFijo == numActFijo
                        && a.FechaProgramada == fecha
                    select new
                    {
                        Division = ac.NumActFijoNavigation.ClaveDivision,
                        Zona = ac.NumActFijoNavigation.CatCentro.CatAgencium.CatZona.NombreZona,
                        Agencia = ac.NumActFijoNavigation.CatCentro.CatAgencium.NombreAgencia,
                        TipoEquipo = ac.ClaveTipoEquipoNavigation.NombreTipoEquipo,
                        Serie = ac.NumSerie,
                        ClaveAgenda = a.ClaveAgenda.ToString()
                    }
                ).FirstOrDefault();

                if (acData != null)
                {
                    byte[] pdf;
                    string fileNamePrefix;

                    // Determinar qué plantilla usar según el tipo de equipo
                    if (acData.TipoEquipo.ToUpper() == "CFETURNO")
                    {
                        pdf = GenerarPdfCFETURNO(
                            acData.Division,
                            acData.Zona,
                            acData.Agencia,
                            acData.TipoEquipo,
                            acData.Serie,
                            acData.ClaveAgenda,
                            fecha,
                            nombreUsuario);
                        fileNamePrefix = "HojaServicioCFETURNO";
                    }
                    else if (acData.TipoEquipo.ToUpper() == "CFECAM")
                    {
                        pdf = GenerarPdfCFECAM(
                            acData.Division,
                            acData.Zona,
                            acData.Agencia,
                            acData.TipoEquipo,
                            acData.Serie,
                            acData.ClaveAgenda,
                            fecha,
                            nombreUsuario);
                        fileNamePrefix = "HojaServicioCFECAM";
                    }
                    else
                    {
                        // Para otros tipos de equipo AC (MONIVENT), usar CFETURNO como default
                        pdf = GenerarPdfCFETURNO(
                            acData.Division,
                            acData.Zona,
                            acData.Agencia,
                            acData.TipoEquipo,
                            acData.Serie,
                            acData.ClaveAgenda,
                            fecha,
                            nombreUsuario);
                        fileNamePrefix = "HojaServicioMONIVENT";
                    }

                    return File(pdf, "application/pdf", $"{fileNamePrefix}_{numActFijo}.pdf");
                }

                // 3) Buscar en Equipos de Cómputo
                var compData = (
                    from a in _dbocontext.Agenda
                    join pc in _dbocontext.EquipoComputos
                        on a.NumActFijo equals pc.NumActFijo
                    join tipo in _dbocontext.CatTipoEquipos
                        on pc.ClaveTipoEquipo equals tipo.ClaveTipoEquipo
                    where a.NumActFijo == numActFijo
                        && a.FechaProgramada == fecha
                    select new
                    {
                        Division = pc.NumActFijoNavigation.ClaveDivision,
                        Zona = pc.NumActFijoNavigation.CatCentro.CatAgencium.CatZona.NombreZona,
                        Agencia = pc.NumActFijoNavigation.CatCentro.CatAgencium.NombreAgencia,
                        SeriePc = pc.NumSeriePc,
                        SerieMon = pc.NumSerieMonitor,
                        Rpe = pc.Rpe,
                        NombreRpe = pc.NombreRpe,
                        ClaveAgenda = a.ClaveAgenda.ToString(),
                        TipoEquipo = tipo.NombreTipoEquipo
                    }
                ).FirstOrDefault();

                if (compData != null)
                {
                    var pdf = GenerarPdfComputo(
                        compData.Division,
                        compData.Zona,
                        compData.Agencia,
                        compData.SeriePc,
                        compData.SerieMon,
                        compData.Rpe,
                        compData.NombreRpe,
                        compData.ClaveAgenda,
                        fecha,
                        nombreUsuario,
                        compData.TipoEquipo,
                        numActFijo);
                    return File(pdf, "application/pdf", $"Computo_HojaServicio_{numActFijo}.pdf");
                }

                return Json(new
                {
                    success = false,
                    message = "No se encontró el equipo en los registros de CFEmáticos, Atención a Clientes o Equipos de Cómputo"
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = $"Error al generar la hoja de servicio: {ex.Message}"
                });
            }
        }

        private byte[] GenerarPdfCFE(
            string division,
            string zona,
            string agencia,
            string numCajero,
            string numSerie,
            string numInventario,
            string claveAgenda,
            DateOnly fechaProgramada,
            string responsable,
            string tipoMantenimiento)
        {
            string fechaImpresion = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");

            using var reader = new PdfReader(_rutaPlantillaCFE);
            using var ms = new MemoryStream();
            using var stamper = new PdfStamper(reader, ms);
            var cb = stamper.GetOverContent(1);
            var bf = BaseFont.CreateFont(BaseFont.HELVETICA, BaseFont.CP1252, BaseFont.NOT_EMBEDDED);

            EscribirTexto(cb, bf, 10, 535f, 734f, claveAgenda);
            EscribirTexto(cb, bf, 10, 240f, 682f, "CFE Sureste");
            EscribirTexto(cb, bf, 10, 115f, 670f, zona);
            EscribirTexto(cb, bf, 10, 390f, 670f, agencia);
            EscribirTexto(cb, bf, 10, 110f, 658f, fechaProgramada.ToString("dd/MM/yyyy"));
            EscribirTexto(cb, bf, 10, 145f, 612f, numCajero);
            EscribirTexto(cb, bf, 10, 290f, 612f, numSerie);
            EscribirTexto(cb, bf, 10, 495f, 612f, numInventario);
            EscribirTexto(cb, bf, 10, 78f, 60f, responsable);
            EscribirTexto(cb, bf, 7, 445f, 110f, $"Fecha de Impresión: {fechaImpresion}");

            if (tipoMantenimiento == "C") // Correctivo
            {
                EscribirTexto(cb, bf, 10, 453.5f, 581f, "X");
            }
            else // Preventivo
            {
                EscribirTexto(cb, bf, 10, 280.5f, 581f, "X");
            }

            stamper.Close();
            return ms.ToArray();
        }

        private byte[] GenerarPdfCFETURNO(
            string division,
            string zona,
            string agencia,
            string tipoEquipo,
            string serie,
            string claveAgenda,
            DateOnly fechaProgramada,
            string responsable)
        {
            string fechaImpresion = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");

            using var reader = new PdfReader(_rutaPlantillaCFETURNO);
            using var ms = new MemoryStream();
            using var stamper = new PdfStamper(reader, ms);
            var cb = stamper.GetOverContent(1);
            var bf = BaseFont.CreateFont(BaseFont.HELVETICA, BaseFont.CP1252, BaseFont.NOT_EMBEDDED);

            EscribirTexto(cb, bf, 10, 535f, 734f, claveAgenda);
            EscribirTexto(cb, bf, 10, 240f, 682f, "CFE Sureste");
            EscribirTexto(cb, bf, 10, 280.5f, 566.5f, "X");
            EscribirTexto(cb, bf, 10, 115f, 670f, zona);
            EscribirTexto(cb, bf, 10, 390f, 670f, agencia);
            EscribirTexto(cb, bf, 10, 110f, 658f, fechaProgramada.ToString("dd/MM/yyyy"));
            EscribirTexto(cb, bf, 10, 290f, 615f, serie);
            EscribirTexto(cb, bf, 10, 378f, 90f, responsable);
            EscribirTexto(cb, bf, 7, 445f, 137.5f, $"Fecha de Impresión: {fechaImpresion}");

            stamper.Close();
            return ms.ToArray();
        }

        private byte[] GenerarPdfCFECAM(
            string division,
            string zona,
            string agencia,
            string tipoEquipo,
            string serie,
            string claveAgenda,
            DateOnly fechaProgramada,
            string responsable)
        {
            string fechaImpresion = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");

            using var reader = new PdfReader(_rutaPlantillaCFECAM);
            using var ms = new MemoryStream();
            using var stamper = new PdfStamper(reader, ms);
            var cb = stamper.GetOverContent(1);
            var bf = BaseFont.CreateFont(BaseFont.HELVETICA, BaseFont.CP1252, BaseFont.NOT_EMBEDDED);

            EscribirTexto(cb, bf, 10, 535f, 734f, claveAgenda);
            EscribirTexto(cb, bf, 10, 240f, 682f, "CFE Sureste");
            EscribirTexto(cb, bf, 10, 280.5f, 587.8f, "X");
            EscribirTexto(cb, bf, 10, 115f, 670f, zona);
            EscribirTexto(cb, bf, 10, 390f, 670f, agencia);
            EscribirTexto(cb, bf, 10, 110f, 658f, fechaProgramada.ToString("dd/MM/yyyy"));
            EscribirTexto(cb, bf, 10, 290f, 614f, serie);
            EscribirTexto(cb, bf, 10, 378f, 90f, responsable);
            EscribirTexto(cb, bf, 7, 445f, 138.5f, $"Fecha de Impresión: {fechaImpresion}");

            stamper.Close();
            return ms.ToArray();
        }

        private void EscribirTexto(
            PdfContentByte cb,
            BaseFont fuente,
            float tamaño,
            float x,
            float y,
            string texto,
            int alineacion = PdfContentByte.ALIGN_LEFT)
        {
            cb.BeginText();
            cb.SetFontAndSize(fuente, tamaño);

            if (alineacion == PdfContentByte.ALIGN_LEFT)
            {
                cb.SetTextMatrix(x, y);
                cb.ShowText(texto);
            }
            else
            {
                cb.ShowTextAligned(alineacion, texto, x, y, 0);
            }

            cb.EndText();
        }

        private byte[] GenerarPdfComputo(
        string division,
        string zona,
        string agencia,
        string seriePc,
        string serieMon,
        string rpe,
        string nombreRpe,
        string claveAgenda,
        DateOnly fechaProgramada,
        string responsable,
        string tipoEquipo,
        string numActFijo)
        {
            string fechaImpresion = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");

            using var reader = new PdfReader(_rutaPlantillaComputo);
            using var ms = new MemoryStream();
            using var stamper = new PdfStamper(reader, ms);
            var cb = stamper.GetOverContent(1);
            // --- LÍNEA MEJORADA ---
            // Construye la ruta a la fuente de forma robusta, sin depender del disco C:
            string fontPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf");
            var bf = BaseFont.CreateFont(fontPath, BaseFont.IDENTITY_H, BaseFont.EMBEDDED);


            EscribirTexto(cb, bf, 10, 505f, 735f, claveAgenda);
            EscribirTexto(cb, bf, 10, 120f, 691f, agencia);
            EscribirTexto(cb, bf, 10, 500f, 721f, fechaProgramada.ToString("dd/MM/yyyy"));
            EscribirTexto(cb, bf, 10, 460f, 647f, seriePc);
            EscribirTexto(cb, bf, 10, 500f, 706f, rpe ?? "N/A");
            EscribirTexto(cb, bf, 10, 120f, 706f, nombreRpe ?? "N/A");
            EscribirTexto(cb, bf, 10, 355f, 140f, responsable);
            EscribirTexto(cb, bf, 10, 100f, 647f, tipoEquipo);
            EscribirTexto(cb, bf, 10, 96f, 662f, numActFijo);
            EscribirTexto(cb, bf, 7, 440f, 106f, $"Fecha de Impresión: {fechaImpresion}");

            stamper.Close();
            return ms.ToArray();
        }

        [HttpGet]
        public IActionResult DescargarHojaServicio(string fileName)
        {
            string filePath = Path.Combine(Path.GetTempPath(), fileName);

            if (!System.IO.File.Exists(filePath))
            {
                return NotFound();
            }

            var fileStream = System.IO.File.OpenRead(filePath);
            return File(fileStream, "application/pdf", fileName);
        }

        [HttpGet]
        public IActionResult ObtenerDatosComputo(string numActFijo)
        {
            var equipo = _dbocontext.EquipoComputos
                .FirstOrDefault(e => e.NumActFijo == numActFijo);

            if (equipo == null)
            {
                return NotFound();
            }

            return Json(new
            {
                rpe = equipo.Rpe ?? "No asignado",
                nombre = equipo.NombreRpe ?? "No asignado"
            });
        }

        [HttpPost]
        public IActionResult GenerarHojaServicioComputo(
        string numActFijo,
        string fechaProgramada,
        string responsable = "TECNICO_DE_ZONA",
        string nombreTecnico = null,
        string rpe = null,
        string nombre = null)
        {
            try
            {
                // El nombre del técnico ahora viene del usuario logueado
                string nombreResponsable = nombreTecnico ?? "Técnico de Zona";
                var fecha = DateOnly.Parse(fechaProgramada);

                var compData = (
                    from a in _dbocontext.Agenda
                    join pc in _dbocontext.EquipoComputos
                        on a.NumActFijo equals pc.NumActFijo
                    join tipo in _dbocontext.CatTipoEquipos
                        on pc.ClaveTipoEquipo equals tipo.ClaveTipoEquipo
                    where a.NumActFijo == numActFijo
                        && a.FechaProgramada == fecha
                    select new
                    {
                        Division = pc.NumActFijoNavigation.ClaveDivision,
                        Zona = pc.NumActFijoNavigation.CatCentro.CatAgencium.CatZona.NombreZona,
                        Agencia = pc.NumActFijoNavigation.CatCentro.CatAgencium.NombreAgencia,
                        SeriePc = pc.NumSeriePc,
                        SerieMon = pc.NumSerieMonitor,
                        Rpe = rpe ?? pc.Rpe,
                        NombreRpe = nombre ?? pc.NombreRpe,
                        ClaveAgenda = a.ClaveAgenda.ToString(),
                        TipoEquipo = tipo.NombreTipoEquipo
                    }
                ).FirstOrDefault();

                if (compData != null)
                {
                    var pdf = GenerarPdfComputo(
                        compData.Division,
                        compData.Zona,
                        compData.Agencia,
                        compData.SeriePc,
                        compData.SerieMon,
                        compData.Rpe,
                        compData.NombreRpe,
                        compData.ClaveAgenda,
                        fecha,
                        nombreResponsable,
                        compData.TipoEquipo,
                        numActFijo);

                    return File(pdf, "application/pdf", $"Computo_HojaServicio_{numActFijo}.pdf");
                }

                return Json(new { success = false, message = "No se encontró el equipo" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }
    }
}