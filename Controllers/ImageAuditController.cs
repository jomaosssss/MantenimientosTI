using MantenimientosTI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;

namespace MantenimientosTI.Controllers
{
    [Authorize(Roles = "ADMINISTRADOR,SUPERVISOR")]
    public class ImageAuditController : Controller
    {
        private readonly MantenimientosTIContext _context;

        public ImageAuditController(MantenimientosTIContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Dashboard()
        {
            // ✅ CORREGIDO: Sin Includes que causen problemas
            var alerts = await _context.SuspiciousImageAlerts
                .Where(a => a.Status == "Pending")
                .OrderByDescending(a => a.CreatedAt)
                .ToListAsync();

            var stats = new
            {
                TotalAlerts = await _context.SuspiciousImageAlerts.CountAsync(),
                PendingAlerts = await _context.SuspiciousImageAlerts.CountAsync(a => a.Status == "Pending"),
                HighRiskAlerts = await _context.SuspiciousImageAlerts.CountAsync(a => a.RiskLevel == "High"),
                TotalValidations = await _context.ImageValidationLogs.CountAsync()
            };

            ViewBag.Stats = stats;
            return View(alerts);
        }

        [HttpPost]
        public async Task<IActionResult> ResolveAlert(long alertId, string resolution, string notes)
        {
            var alert = await _context.SuspiciousImageAlerts.FindAsync(alertId);
            if (alert == null) return NotFound();

            alert.Status = "Resolved";
            alert.ResolvedBy = User.Identity?.Name ?? "Sistema";
            alert.ResolvedAt = DateTime.Now;
            alert.ResolutionNotes = notes;

            await _context.SaveChangesAsync();

            TempData["Success"] = "Alerta resuelta exitosamente";
            return RedirectToAction("Dashboard");
        }

        // ✅ NUEVO: Método para obtener información relacionada cuando sea necesario
        public async Task<JsonResult> GetAlertDetails(long alertId)
        {
            var alert = await _context.SuspiciousImageAlerts.FindAsync(alertId);
            if (alert == null) return Json(new { error = "Alerta no encontrada" });

            // Obtener información del mantenimiento por separado
            var mantenimiento = await _context.Mantenimientos
                .Include(m => m.RpeNavigation)
                .Include(m => m.NumActFijoNavigation)
                .FirstOrDefaultAsync(m => m.NumOrden == alert.MantenimientoId);

            return Json(new
            {
                alert,
                mantenimientoInfo = mantenimiento != null ? new
                {
                    mantenimiento.NumOrden,
                    mantenimiento.Rpe,
                    Usuario = mantenimiento.RpeNavigation != null ?
                        $"{mantenimiento.RpeNavigation.Nombre} {mantenimiento.RpeNavigation.ApellidoP}" : "N/A",
                    Equipo = mantenimiento.NumActFijoNavigation?.NumActFijo ?? "N/A"
                } : null
            });
        }
    }
}