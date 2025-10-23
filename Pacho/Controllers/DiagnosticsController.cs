using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pacho.Models;

namespace Pacho.Controllers
{
    /// <summary>
    /// Controlador encargado de la gestión de preguntas diagnósticas.
    /// Permite listar y editar preguntas utilizadas para el diagnóstico de enfermedades.
    /// </summary>
    public class DiagnosticsController : Controller
    {
        private readonly BrainPachoContext _context;

        /// <summary>
        /// Inicializa una nueva instancia del controlador de diagnóstico.
        /// </summary>
        /// <param name="context">Contexto de base de datos de la aplicación.</param>
        public DiagnosticsController(BrainPachoContext context) => _context = context;

        /// <summary>
        /// Muestra el listado completo de preguntas diagnósticas junto con sus posibles respuestas.
        /// </summary>
        /// <returns>Vista con la lista de preguntas y respuestas asociadas.</returns>
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var list = await _context.DiagnosticQuestions
                .Include(q => q.Answers.OrderBy(a => a.AnswerOrder))
                .OrderBy(q => q.QuestionOrder)
                .ToListAsync();

            return View(list);
        }

        /// <summary>
        /// Retorna la vista de edición para una pregunta diagnóstica específica.
        /// </summary>
        /// <param name="id">Identificador de la pregunta a editar.</param>
        /// <returns>Vista con el modelo de la pregunta a editar.</returns>
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var q = await _context.DiagnosticQuestions
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id);

            if (q == null) return NotFound();

            var vm = new DiagnosticQuestionEditVM
            {
                Id = q.Id,
                QuestionOrder = q.QuestionOrder,
                QuestionText = q.QuestionText
            };

            return View(vm);
        }

        /// <summary>
        /// Guarda los cambios realizados a una pregunta diagnóstica.
        /// </summary>
        /// <param name="vm">Modelo de vista con la información actualizada de la pregunta.</param>
        /// <returns>Redirección al índice o vista con errores de validación.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(DiagnosticQuestionEditVM vm)
        {
            if (!ModelState.IsValid) return View(vm);

            var q = await _context.DiagnosticQuestions.FirstOrDefaultAsync(x => x.Id == vm.Id);
            if (q == null) return NotFound();

            q.QuestionOrder = vm.QuestionOrder;
            q.QuestionText = vm.QuestionText;

            try
            {
                await _context.SaveChangesAsync();
                TempData["Msg"] = "Pregunta actualizada exitosamente.";
                return RedirectToAction(nameof(Index));
            }
            catch (DbUpdateException ex)
            {
                ModelState.AddModelError(string.Empty, $"No fue posible guardar los cambios: {ex.GetBaseException().Message}");
                return View(vm);
            }
        }
    }

    /// <summary>
    /// Modelo de vista para la edición de preguntas diagnósticas.
    /// Contiene los campos básicos editables de la pregunta.
    /// </summary>
    public class DiagnosticQuestionEditVM
    {
        public int Id { get; set; }

        [System.ComponentModel.DataAnnotations.Required]
        [System.ComponentModel.DataAnnotations.StringLength(255)]
        public string QuestionText { get; set; } = "";

        public int QuestionOrder { get; set; }
    }
}
