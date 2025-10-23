using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pacho.Models;

namespace Pacho.Controllers
{
    /// <summary>
    /// Controlador encargado de la gestión de tratamientos registrados por los expertos activos.
    /// Permite crear, editar, consultar y eliminar tratamientos asociados a enfermedades,
    /// vinculando la información del experto autenticado.
    /// </summary>
    [Authorize(Policy = "ActiveExpert")]
    public class TreatmentController : Controller
    {
        private readonly BrainPachoContext _context;

        public TreatmentController(BrainPachoContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Muestra el listado de tratamientos creados por el experto autenticado.
        /// Incluye información estadística básica (total, mes actual y tipos registrados).
        /// </summary>
        public async Task<IActionResult> Index()
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var expert = await _context.Experts.FirstOrDefaultAsync(e => e.UserId == userId);
            if (expert == null)
                return Forbid();

            var tratamientos = await _context.Treatments
                .Where(t => t.ExpertId == expert.IdExpert)
                .Include(t => t.Disease)
                .OrderByDescending(t => t.CreationDate)
                .ToListAsync();

            // Estadísticas para panel del experto
            ViewBag.TotalTreatments = tratamientos.Count;
            ViewBag.ThisMonthTreatments = tratamientos.Count(t =>
                t.CreationDate.Year == DateTime.Now.Year &&
                t.CreationDate.Month == DateTime.Now.Month);
            ViewBag.TreatmentTypes = tratamientos
                .Select(t => t.TreatmentType)
                .Distinct()
                .Count();

            return View(tratamientos);
        }

        /// <summary>
        /// Carga la vista para registrar un nuevo tratamiento.
        /// Presenta un listado de enfermedades activas para asociar el tratamiento.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Create()
        {
            ViewBag.Diseases = await _context.Diseases
                .Where(d => d.Asset)
                .OrderBy(d => d.CommonName)
                .ToListAsync();
            return View();
        }

        /// <summary>
        /// Registra un nuevo tratamiento asociado al experto autenticado.
        /// Incrementa el contador de tratamientos en la enfermedad relacionada.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Treatment treatment)
        {
            if (!ModelState.IsValid)
            {
                // Depuración de errores de modelo
                foreach (var entry in ModelState)
                {
                    foreach (var error in entry.Value.Errors)
                        Console.WriteLine($"ModelState Error: {entry.Key} → {error.ErrorMessage}");
                }

                ViewBag.Diseases = await _context.Diseases
                    .Where(d => d.Asset)
                    .OrderBy(d => d.CommonName)
                    .ToListAsync();
                return View(treatment);
            }

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var expert = await _context.Experts.FirstOrDefaultAsync(e => e.UserId == userId);
            if (expert == null)
            {
                ModelState.AddModelError("", "No se encontró un perfil de experto asociado.");
                ViewBag.Diseases = await _context.Diseases
                    .Where(d => d.Asset)
                    .OrderBy(d => d.CommonName)
                    .ToListAsync();
                return View(treatment);
            }

            // Asignación de valores automáticos
            treatment.ExpertId = expert.IdExpert;
            treatment.CreationDate = DateTime.Now;
            treatment.Status = true;

            _context.Treatments.Add(treatment);

            // Incrementar contador de tratamientos por enfermedad
            var disease = await _context.Diseases.FirstOrDefaultAsync(d => d.IdDisease == treatment.DiseaseId);
            if (disease != null)
                disease.TreatmentsTotal++;

            await _context.SaveChangesAsync();

            TempData["Success"] = "Tratamiento registrado correctamente.";
            return RedirectToAction("Index", "Treatment");
        }

        /// <summary>
        /// Muestra el detalle completo de un tratamiento, validando que pertenezca al experto autenticado.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var tratamiento = await _context.Treatments
                .Include(t => t.Disease)
                .Include(t => t.Expert)
                .FirstOrDefaultAsync(t => t.IdTreatment == id);

            if (tratamiento == null)
                return NotFound();

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var experto = await _context.Experts.FirstOrDefaultAsync(e => e.UserId == userId);
            if (experto == null || tratamiento.ExpertId != experto.IdExpert)
                return Forbid();

            return View(tratamiento);
        }

        /// <summary>
        /// Carga la vista de confirmación para eliminar un tratamiento.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Delete(int id)
        {
            var tratamiento = await _context.Treatments
                .Include(t => t.Disease)
                .FirstOrDefaultAsync(t => t.IdTreatment == id);

            if (tratamiento == null)
                return NotFound();

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var experto = await _context.Experts.FirstOrDefaultAsync(e => e.UserId == userId);
            if (experto == null || tratamiento.ExpertId != experto.IdExpert)
                return Forbid();

            return View(tratamiento);
        }

        /// <summary>
        /// Elimina un tratamiento existente y actualiza el contador en la enfermedad correspondiente.
        /// </summary>
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var tratamiento = await _context.Treatments.FindAsync(id);
            if (tratamiento == null)
                return NotFound();

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var experto = await _context.Experts.FirstOrDefaultAsync(e => e.UserId == userId);
            if (experto == null || tratamiento.ExpertId != experto.IdExpert)
                return Forbid();

            _context.Treatments.Remove(tratamiento);

            var enfermedad = await _context.Diseases.FindAsync(tratamiento.DiseaseId);
            if (enfermedad != null && enfermedad.TreatmentsTotal > 0)
                enfermedad.TreatmentsTotal--;

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        /// <summary>
        /// Carga la vista de edición de un tratamiento, validando la propiedad del registro.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            ViewBag.Diseases = await _context.Diseases
                .Where(d => d.Asset)
                .OrderBy(d => d.CommonName)
                .ToListAsync();

            var treatment = await _context.Treatments.FirstOrDefaultAsync(t => t.IdTreatment == id);
            if (treatment == null)
                return NotFound();

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var experto = await _context.Experts.FirstOrDefaultAsync(e => e.UserId == userId);
            if (experto == null || treatment.ExpertId != experto.IdExpert)
                return Forbid();

            return View(treatment);
        }

        /// <summary>
        /// Actualiza la información de un tratamiento existente,
        /// validando la pertenencia del registro al experto autenticado.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Treatment treatment)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.Diseases = await _context.Diseases
                    .Where(d => d.Asset)
                    .OrderBy(d => d.CommonName)
                    .ToListAsync();
                return View(treatment);
            }

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var experto = await _context.Experts.FirstOrDefaultAsync(e => e.UserId == userId);
            var existing = await _context.Treatments
                .FirstOrDefaultAsync(t => t.IdTreatment == treatment.IdTreatment);

            if (experto == null || existing == null || existing.ExpertId != experto.IdExpert)
                return Forbid();

            // Actualización de campos editables
            existing.DiseaseId = treatment.DiseaseId;
            existing.TreatmentType = treatment.TreatmentType;
            existing.Description = treatment.Description;
            existing.RecommendedProducts = treatment.RecommendedProducts;
            existing.Frequency = treatment.Frequency;
            existing.Precautions = treatment.Precautions;
            existing.WeatherConditions = treatment.WeatherConditions;
            existing.DiasMejoriaVisual = treatment.DiasMejoriaVisual;
            existing.Environment = treatment.Environment;

            await _context.SaveChangesAsync();

            TempData["Success"] = "Tratamiento actualizado correctamente.";
            return RedirectToAction(nameof(Index));
        }
    }
}
