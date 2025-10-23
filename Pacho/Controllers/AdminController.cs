using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pacho.Models;

namespace Pacho.Controllers
{
    /// <summary>
    /// Controlador encargado de la gestión administrativa del sistema.
    /// Permite al SuperAdmin revisar solicitudes de expertos, habilitar pruebas,
    /// eliminar solicitudes, administrar estados de usuarios y visualizar respuestas.
    /// </summary>
    [Authorize(Roles = "SuperAdmin")]
    public class AdminController : Controller
    {
        private readonly BrainPachoContext _context;

        /// <summary>
        /// Constructor del controlador administrativo.
        /// </summary>
        /// <param name="context">Contexto de base de datos principal de la aplicación.</param>
        public AdminController(BrainPachoContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Muestra la lista de expertos pendientes de aprobación y prueba.
        /// </summary>
        public async Task<IActionResult> PendingExperts()
        {
            var pending = await _context.Experts
                .Where(e => e.User.Status == 2 && e.TestState == "pendiente")
                .Include(e => e.User)
                .ToListAsync();

            return View(pending);
        }

        /// <summary>
        /// Habilita la prueba de aptitud para un experto pendiente.
        /// </summary>
        /// <param name="id">Identificador del experto.</param>
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> EnableTest(int id)
        {
            var expert = await _context.Experts
                .Include(e => e.User)
                .FirstOrDefaultAsync(e => e.IdExpert == id);

            if (expert == null) return NotFound();

            expert.TestState = "habilitado";
            expert.User.Status = 2;
            expert.ApprovalDate = DateTime.Now;

            await _context.SaveChangesAsync();
            TempData["Mensaje"] = "La prueba fue habilitada exitosamente.";

            return RedirectToAction(nameof(PendingExperts));
        }

        /// <summary>
        /// Elimina la solicitud de registro de un experto.
        /// </summary>
        /// <param name="id">Identificador del experto a eliminar.</param>
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteRequest(int id)
        {
            var expert = await _context.Experts
                .Include(e => e.User)
                .FirstOrDefaultAsync(e => e.IdExpert == id);

            if (expert == null) return NotFound();

            _context.Experts.Remove(expert);
            await _context.SaveChangesAsync();

            TempData["Mensaje"] = "La solicitud fue eliminada exitosamente.";
            return RedirectToAction(nameof(PendingExperts));
        }

        /// <summary>
        /// Lista todos los expertos registrados en el sistema.
        /// </summary>
        public async Task<IActionResult> Experts()
        {
            var all = await _context.Experts
                .Include(e => e.User)
                .ToListAsync();

            return View(all);
        }

        /// <summary>
        /// Alterna el estado activo/inactivo de un experto.
        /// </summary>
        /// <param name="id">Identificador del experto.</param>
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleStatus(int id)
        {
            var expert = await _context.Experts
                .Include(e => e.User)
                .FirstOrDefaultAsync(e => e.IdExpert == id);

            if (expert == null) return NotFound();

            expert.User.Status = expert.User.Status == 1 ? 2 : 1;
            await _context.SaveChangesAsync();

            TempData["Mensaje"] = expert.User.Status == 1
                ? "Experto activado correctamente."
                : "Experto desactivado correctamente.";

            return RedirectToAction(nameof(Experts));
        }

        /// <summary>
        /// Visualiza las respuestas dadas por un experto en su prueba de aptitud.
        /// </summary>
        /// <param name="id">Identificador del experto.</param>
        public async Task<IActionResult> ViewAnswers(int id)
        {
            ViewBag.Expert = await _context.Experts
                .Include(e => e.User)
                .FirstOrDefaultAsync(e => e.IdExpert == id);

            var answers = await _context.ExpertAnswers
                .Where(a => a.ExpertId == id)
                .Include(a => a.Question)
                .Include(a => a.Answer)
                .OrderBy(a => a.AnsweredAt)
                .ToListAsync();

            return View(answers);
        }

        /// <summary>
        /// Muestra los detalles completos de un experto.
        /// </summary>
        /// <param name="id">Identificador del experto.</param>
        public async Task<IActionResult> Details(int id)
        {
            var experto = await _context.Experts
                .Include(e => e.User)
                .FirstOrDefaultAsync(e => e.IdExpert == id);

            if (experto == null)
                return NotFound();

            return View(experto);
        }
    }
}
