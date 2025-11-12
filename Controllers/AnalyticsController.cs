// Controllers/AnalyticsController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MantenimientosTI.Models;
using MantenimientosTI.Models.ViewModels;
using System.Linq;

namespace MantenimientosTI.Controllers
{
    [Authorize(Roles = "ADMINISTRADOR")]
    public class AnalyticsController : Controller
    {
        private readonly MantenimientosTIContext _context;

        public AnalyticsController(MantenimientosTIContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> UnifiedDashboard(
            DateTime? startDate = null,
            DateTime? endDate = null,
            string userId = null)
        {
            startDate ??= DateTime.Today.AddDays(-7);
            endDate ??= DateTime.Today.AddDays(1);

            var model = new UnifiedDashboardViewModel
            {
                StartDate = startDate.Value,
                EndDate = endDate.Value,
                UserId = userId
            };

            // Cargar todos los datos
            await LoadBasicStats(model);
            await LoadAdvancedStats(model);
            await LoadActivityByHour(model);
            await LoadActivityByType(model);
            await LoadTopUsers(model);
            await LoadTopPages(model);
            await LoadSecurityEvents(model);
            await LoadRecentActivities(model);

            return View(model);
        }

        // MÉTODO MEJORADO: Top usuarios con detalles de acciones
        private async Task LoadTopUsers(UnifiedDashboardViewModel model)
        {
            // Primero obtenemos los usuarios más activos
            var topUsers = await _context.RegistroActividad
                .Where(r => r.FechaHora >= model.StartDate && r.FechaHora <= model.EndDate)
                .GroupBy(r => r.Usuario)
                .Select(g => new
                {
                    UserName = g.Key,
                    TotalActivities = g.Count()
                })
                .OrderByDescending(x => x.TotalActivities)
                .Take(10)
                .ToListAsync();

            // Ahora para cada usuario, obtenemos el detalle de sus acciones
            var topUsersWithDetails = new List<TopUser>();

            foreach (var user in topUsers)
            {
                // Obtener los datos básicos de la base de datos
                var userActionsData = await _context.RegistroActividad
                    .Where(r => r.Usuario == user.UserName &&
                               r.FechaHora >= model.StartDate &&
                               r.FechaHora <= model.EndDate)
                    .GroupBy(r => r.Accion)
                    .Select(g => new
                    {
                        ActionType = g.Key,
                        Count = g.Count(),
                        LastPerformed = g.Max(x => x.FechaHora)
                    })
                    .OrderByDescending(x => x.Count)
                    .Take(5)
                    .ToListAsync();

                // Obtener descripciones desde CatAcciones
                var actionTypes = userActionsData.Select(d => d.ActionType).Distinct().ToList();
                var actionDescriptions = await _context.CatAcciones
                    .Where(ca => actionTypes.Contains(ca.ClaveAccion))
                    .ToDictionaryAsync(ca => ca.ClaveAccion, ca => ca.Descripcion);

                // Convertir a UserActionDetail con descripciones desde BD
                var userActionDetails = userActionsData
                    .Select(d => new UserActionDetail
                    {
                        ActionType = d.ActionType,
                        ActionDescription = actionDescriptions.ContainsKey(d.ActionType)
                            ? ExtractFirstSentence(actionDescriptions[d.ActionType])
                            : d.ActionType,
                        Count = d.Count,
                        LastPerformed = d.LastPerformed
                    })
                    .ToList();

                // Obtener rol real desde la base de datos
                var userRole = await GetUserRoleFromDatabase(user.UserName);

                topUsersWithDetails.Add(new TopUser
                {
                    UserName = user.UserName,
                    ActivityCount = user.TotalActivities,
                    Role = userRole,
                    ActionDetails = userActionDetails
                });
            }

            model.TopUsers = topUsersWithDetails;
        }

        // Obtener rol desde la base de datos
        private async Task<string> GetUserRoleFromDatabase(string userName)
        {
            if (string.IsNullOrEmpty(userName)) return "Usuario";

            // Buscar el usuario en la base de datos
            var usuario = await _context.Usuarios
                .Include(u => u.ClaveRolNavigation)
                .FirstOrDefaultAsync(u =>
                    (u.Nombre + " " + u.ApellidoP + " " + u.ApellidoM).Contains(userName) ||
                    u.Rpe == userName);

            return usuario?.ClaveRolNavigation?.Nombre ?? "Usuario";
        }

        // Extraer primera oración de la descripción
        private static string ExtractFirstSentence(string description)
        {
            if (string.IsNullOrEmpty(description)) return "Acción no especificada";

            // Buscar el primer punto, pipe o fin de línea
            var endIndex = description.IndexOf('.');
            if (endIndex == -1) endIndex = description.IndexOf('|');
            if (endIndex == -1) endIndex = description.Length;

            return description.Substring(0, endIndex).Trim();
        }

        // Los demás métodos permanecen igual pero mejorados...
        private async Task LoadBasicStats(UnifiedDashboardViewModel model)
        {
            var query = _context.RegistroActividad
                .Where(r => r.FechaHora >= model.StartDate && r.FechaHora <= model.EndDate);

            if (!string.IsNullOrEmpty(model.UserId))
            {
                query = query.Where(r => r.Usuario.Contains(model.UserId));
            }

            model.TotalActions = await query.CountAsync();

            model.ActiveUsers = await query
                .Select(r => r.Usuario)
                .Distinct()
                .CountAsync();

            model.TotalSessions = await query
                .CountAsync(r => r.Accion.Contains("SESION"));

            model.TotalPageViews = await query
                .CountAsync(r => !r.Accion.Contains("SESION") && !r.Accion.Contains("LOGOUT"));
        }

        private async Task LoadAdvancedStats(UnifiedDashboardViewModel model)
        {
            var query = _context.RegistroActividad
                .Where(r => r.FechaHora >= model.StartDate && r.FechaHora <= model.EndDate);

            // Usar las constantes reales de la base de datos
            model.FailedLogins = await query
                .CountAsync(r => r.Descripcion.Contains("fallido") || r.Descripcion.Contains("incorrect"));

            model.SuccessfulLogins = await query
                .CountAsync(r => r.Accion.Contains("SESION") &&
                               !r.Descripcion.Contains("fallido") &&
                               !r.Descripcion.Contains("incorrect"));

            model.CsvUploads = await query
                .CountAsync(r => r.Accion.Contains("CSV"));

            model.MaintenanceActions = await query
                .CountAsync(r => r.Accion.Contains("MTTO"));
        }

        private async Task LoadActivityByHour(UnifiedDashboardViewModel model)
        {
            var activityData = await _context.RegistroActividad
                .Where(r => r.FechaHora >= model.StartDate && r.FechaHora <= model.EndDate)
                .GroupBy(r => r.FechaHora.Hour)
                .Select(g => new ActivityByHour
                {
                    Hour = g.Key,
                    Count = g.Count()
                })
                .OrderBy(x => x.Hour)
                .ToListAsync();

            for (int hour = 0; hour < 24; hour++)
            {
                if (!activityData.Any(x => x.Hour == hour))
                {
                    activityData.Add(new ActivityByHour { Hour = hour, Count = 0 });
                }
            }

            model.ActivityByHour = activityData.OrderBy(x => x.Hour).ToList();
        }

        private async Task LoadActivityByType(UnifiedDashboardViewModel model)
        {
            var activityTypes = await _context.RegistroActividad
                .Where(r => r.FechaHora >= model.StartDate && r.FechaHora <= model.EndDate)
                .GroupBy(r => r.Accion)
                .Select(g => new ActivityByType
                {
                    ActionType = g.Key,
                    Count = g.Count()
                })
                .OrderByDescending(x => x.Count)
                .Take(8)
                .ToListAsync();

            // Obtener descripciones desde CatAcciones
            var actionTypes = activityTypes.Select(at => at.ActionType).Distinct().ToList();
            var actionDescriptions = await _context.CatAcciones
                .Where(ca => actionTypes.Contains(ca.ClaveAccion))
                .ToDictionaryAsync(ca => ca.ClaveAccion, ca => ca.Descripcion);

            // Asignar descripciones y colores
            var colors = new[] { "#008e60", "#ff6b6b", "#4ecdc4", "#45b7d1", "#96ceb4", "#feca57", "#ff9ff3", "#54a0ff" };

            for (int i = 0; i < activityTypes.Count; i++)
            {
                var actionType = activityTypes[i];
                actionType.Description = actionDescriptions.ContainsKey(actionType.ActionType) ? ExtractFirstSentence(actionDescriptions[actionType.ActionType])
                    : actionType.ActionType;
                actionType.Color = colors[i % colors.Length];
            }

            model.ActivityByType = activityTypes;
        }

        private async Task LoadTopPages(UnifiedDashboardViewModel model)
        {
            // Extraer páginas de las descripciones usando lógica basada en acciones comunes
            var pageData = await _context.RegistroActividad
                .Where(r => r.FechaHora >= model.StartDate && r.FechaHora <= model.EndDate)
                .ToListAsync();

            var pageGroups = pageData
                .Where(r => !string.IsNullOrEmpty(r.Descripcion))
                .GroupBy(r => ExtractPageFromAction(r.Accion, r.Descripcion))
                .Where(g => !string.IsNullOrEmpty(g.Key))
                .Select(g => new PageViewInfo
                {
                    PageName = g.Key,
                    ViewCount = g.Count(),
                    Controller = ExtractControllerFromAction(g.First().Accion)
                })
                .OrderByDescending(x => x.ViewCount)
                .Take(8)
                .ToList();

            model.TopPages = pageGroups;
        }

        private async Task LoadSecurityEvents(UnifiedDashboardViewModel model)
        {
            var securityEvents = await _context.RegistroActividad
                .Where(r => r.FechaHora >= model.StartDate && r.FechaHora <= model.EndDate &&
                           (r.Accion.Contains("ERROR") || r.Descripcion.Contains("fallido") ||
                            r.Descripcion.Contains("error") || r.Descripcion.Contains("incorrecto")))
                .OrderByDescending(r => r.FechaHora)
                .Take(15)
                .Select(r => new SecurityEvent
                {
                    Timestamp = r.FechaHora,
                    UserName = r.Usuario,
                    EventType = r.Accion,
                    Description = r.Descripcion.Length > 100 ? r.Descripcion.Substring(0, 100) + "..." : r.Descripcion,
                    Severity = GetSecuritySeverity(r.Accion, r.Descripcion)
                })
                .ToListAsync();

            model.SecurityEvents = securityEvents;
        }

        private async Task LoadRecentActivities(UnifiedDashboardViewModel model)
        {
            model.RecentActivities = await _context.RegistroActividad
                .Where(r => r.FechaHora >= model.StartDate && r.FechaHora <= model.EndDate)
                .OrderByDescending(r => r.FechaHora)
                .Take(15)
                .Select(r => new RecentActivity
                {
                    Timestamp = r.FechaHora,
                    UserName = r.Usuario,
                    Action = r.Accion,
                    Description = r.Descripcion.Length > 80 ? r.Descripcion.Substring(0, 80) + "..." : r.Descripcion
                })
                .ToListAsync();
        }

        // Métodos auxiliares mejorados
        private static string GetSecuritySeverity(string action, string description)
        {
            var upperDesc = description.ToUpper();
            if (upperDesc.Contains("FALLIDO") || upperDesc.Contains("ERROR") || upperDesc.Contains("INCORRECTO"))
                return "high";
            if (action.Contains("SESION"))
                return "medium";
            return "low";
        }

        private static string ExtractPageFromAction(string action, string description)
        {
            if (action.Contains("USUARIO")) return "Gestión de Usuarios";
            if (action.Contains("CSV")) return "Carga de Archivos CSV";
            if (action.Contains("MTTO")) return "Mantenimientos";
            if (action.Contains("SESION")) return "Autenticación";
            if (description.Contains("bitácora") || description.Contains("bitacora")) return "Bitácora del Sistema";
            if (description.Contains("inicio")) return "Página de Inicio";
            return "Otras Funcionalidades";
        }

        private static string ExtractControllerFromAction(string action)
        {
            if (action.Contains("USUARIO")) return "UsuarioController";
            if (action.Contains("CSV")) return "CargaController";
            if (action.Contains("MTTO")) return "MantenimientoController";
            if (action.Contains("SESION")) return "AutenticacionController";
            return "OtroController";
        }
    }
}