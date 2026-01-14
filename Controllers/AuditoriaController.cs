using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MantenimientosTI.Models;
using MantenimientosTI.Models.ViewModels;

namespace MantenimientosTI.Controllers
{
    public class AuditoriaController : Controller
    {
        private readonly MantenimientosTIContext _context;

        public AuditoriaController(MantenimientosTIContext context)
        {
            _context = context;
        }

        // GET: Auditoria
        public async Task<IActionResult> Index()
        {
            // 1. Obtener todas las fotos que tengan alguna bandera de alerta encendida
            // Se incluyen las relaciones necesarias para mostrar nombres y ubicaciones
            var fotosConAlertas = await _context.Fotos
                .Include(f => f.Mantenimiento)
                    .ThenInclude(m => m.RpeNavigation) // Para nombre del técnico
                .Include(f => f.Mantenimiento)
                    .ThenInclude(m => m.NumActFijoNavigation) // Para datos del equipo (Ubicación)
                        .ThenInclude(e => e.CatCentro)
                            .ThenInclude(c => c.CatAgencium)
                                .ThenInclude(a => a.CatZona)
                .Where(f => f.EsDuplicadaAntes == true || f.EsDuplicadaDurante == true || f.EsDuplicadaDespues == true ||
                            f.AlertaFechaAntes == true || f.AlertaFechaDurante == true || f.AlertaFechaDespues == true)
                .OrderByDescending(f => f.FechaHora)
                .ToListAsync();

            var listaAuditoria = new List<AuditoriaViewModel>();

            // 2. Procesar cada registro de foto para generar las filas del ViewModel
            // Una sola fila de Foto puede generar hasta 3 alertas (Antes, Durante, Después)
            foreach (var foto in fotosConAlertas)
            {
                // -- Análisis Etapa: ANTES --
                if (foto.EsDuplicadaAntes == true || foto.AlertaFechaAntes == true)
                {
                    var vm = CrearViewModelBase(foto, "Antes");

                    if (foto.EsDuplicadaAntes == true)
                    {
                        vm.TipoAnomalia = "IMAGEN DUPLICADA";
                        // Buscar la foto original basada en el Hash
                        await BuscarYAsignarOriginal(vm, foto.HashAntes, foto.FechaHora);
                    }
                    else if (foto.AlertaFechaAntes == true)
                    {
                        vm.TipoAnomalia = "FECHA ANTIGUA";
                    }
                    listaAuditoria.Add(vm);
                }

                // -- Análisis Etapa: DURANTE --
                if (foto.EsDuplicadaDurante == true || foto.AlertaFechaDurante == true)
                {
                    var vm = CrearViewModelBase(foto, "Durante");

                    if (foto.EsDuplicadaDurante == true)
                    {
                        vm.TipoAnomalia = "IMAGEN DUPLICADA";
                        await BuscarYAsignarOriginal(vm, foto.HashDurante, foto.FechaHora);
                    }
                    else if (foto.AlertaFechaDurante == true)
                    {
                        vm.TipoAnomalia = "FECHA ANTIGUA";
                    }
                    listaAuditoria.Add(vm);
                }

                // -- Análisis Etapa: DESPUÉS --
                if (foto.EsDuplicadaDespues == true || foto.AlertaFechaDespues == true)
                {
                    var vm = CrearViewModelBase(foto, "Después");

                    if (foto.EsDuplicadaDespues == true)
                    {
                        vm.TipoAnomalia = "IMAGEN DUPLICADA";
                        await BuscarYAsignarOriginal(vm, foto.HashDespues, foto.FechaHora);
                    }
                    else if (foto.AlertaFechaDespues == true)
                    {
                        vm.TipoAnomalia = "FECHA ANTIGUA";
                    }
                    listaAuditoria.Add(vm);
                }
            }

            // 3. Generar Estadísticas para el Dashboard
            // Usamos la lista ya procesada para calcular los métricos
            var estadisticas = new EstadisticasAuditoriaViewModel
            {
                TotalAnomalias = listaAuditoria.Count,
                TecnicosInvolucrados = listaAuditoria.Select(x => x.Rpe).Distinct().Count(),
                ZonasAfectadas = listaAuditoria.Select(x => x.Zona).Distinct().Count(),
                // Usamos la propiedad calculada del ViewModel para contar críticos
                Critico = listaAuditoria.Count(x => x.NivelAlerta == NivelAlerta.Critico)
            };

            // Pasamos las estadísticas mediante ViewBag (o podrías crear un modelo contenedor padre)
            ViewBag.Estadisticas = estadisticas;

            return View(listaAuditoria);
        }

        #region Helpers Privados

        // Helper para crear la base del ViewModel con datos comunes
        private AuditoriaViewModel CrearViewModelBase(Foto foto, string etapa)
        {
            var tecnico = foto.Mantenimiento?.RpeNavigation;
            var equipo = foto.Mantenimiento?.NumActFijoNavigation;

            // Navegación segura para obtener Zona y Agencia a través del Equipo -> Centro -> Agencia -> Zona
            string zonaNombre = "No definida";
            string agenciaNombre = "No definida";

            if (equipo?.CatCentro?.CatAgencium != null)
            {
                agenciaNombre = equipo.CatCentro.CatAgencium.NombreAgencia ?? "Sin Nombre";
                if (equipo.CatCentro.CatAgencium.CatZona != null)
                {
                    zonaNombre = equipo.CatCentro.CatAgencium.CatZona.NombreZona ?? "Sin Nombre";
                }
            }

            return new AuditoriaViewModel
            {
                NumOrden = foto.NumOrden,
                Rpe = foto.Mantenimiento?.Rpe ?? "S/D",
                NombreTecnico = tecnico != null ? $"{tecnico.Nombre} {tecnico.ApellidoP} {tecnico.ApellidoM}" : "Desconocido",
                Zona = zonaNombre,
                Agencia = agenciaNombre,
                FechaRegistro = foto.Mantenimiento?.FechaInsercion ?? foto.FechaHora, // Preferencia a fecha inserción mantenimiento
                Etapa = etapa,
                ExisteOriginal = false // Se pone true si se encuentra en BuscarYAsignarOriginal
            };
        }

        // Helper asíncrono para buscar la foto original cuando hay duplicidad
        private async Task BuscarYAsignarOriginal(AuditoriaViewModel vm, string hashBuscado, DateTime fechaActual)
        {
            if (string.IsNullOrEmpty(hashBuscado)) return;

            // Buscamos cualquier foto que tenga ese hash (en cualquiera de las 3 etapas)
            // Y que sea cronológicamente ANTERIOR a la foto actual
            var fotoOriginal = await _context.Fotos
                .Include(f => f.Mantenimiento)
                    .ThenInclude(m => m.RpeNavigation)
                .Include(f => f.Mantenimiento)
                    .ThenInclude(m => m.NumActFijoNavigation)
                        .ThenInclude(e => e.CatCentro)
                            .ThenInclude(c => c.CatAgencium)
                                .ThenInclude(a => a.CatZona)
                .Where(f => (f.HashAntes == hashBuscado || f.HashDurante == hashBuscado || f.HashDespues == hashBuscado)
                            && f.FechaHora < fechaActual) // Importante: debe ser anterior
                .OrderBy(f => f.FechaHora) // Tomamos la más antigua
                .FirstOrDefaultAsync();

            if (fotoOriginal != null)
            {
                var tecnicoOrig = fotoOriginal.Mantenimiento?.RpeNavigation;
                var equipoOrig = fotoOriginal.Mantenimiento?.NumActFijoNavigation;

                string zonaOrig = "No definida";
                string agenciaOrig = "No definida";

                if (equipoOrig?.CatCentro?.CatAgencium != null)
                {
                    agenciaOrig = equipoOrig.CatCentro.CatAgencium.NombreAgencia ?? "Sin Nombre";
                    if (equipoOrig.CatCentro.CatAgencium.CatZona != null)
                    {
                        zonaOrig = equipoOrig.CatCentro.CatAgencium.CatZona.NombreZona ?? "Sin Nombre";
                    }
                }

                vm.ExisteOriginal = true;
                vm.NumOrdenOriginal = fotoOriginal.NumOrden;
                vm.RpeOriginal = fotoOriginal.Mantenimiento?.Rpe;
                vm.NombreOriginal = tecnicoOrig != null ? $"{tecnicoOrig.Nombre} {tecnicoOrig.ApellidoP} {tecnicoOrig.ApellidoM}" : "Desconocido";
                vm.FechaOriginal = fotoOriginal.FechaHora; // O fecha de mantenimiento
                vm.ZonaOriginal = zonaOrig;
                vm.AgenciaOriginal = agenciaOrig;
            }
        }

        #endregion
    }
}