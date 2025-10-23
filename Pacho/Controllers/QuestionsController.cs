using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pacho.Models;

namespace Pacho.Controllers
{
    /// <summary>
    /// Controlador encargado de la gestión completa del banco de preguntas
    /// del sistema experto. Permite al SuperAdmin crear, editar, eliminar,
    /// duplicar y consultar preguntas con sus respuestas asociadas.
    /// Incluye validaciones, control de integridad referencial y operaciones AJAX.
    /// </summary>
    [Authorize(Roles = "SuperAdmin")]
    public class QuestionsController : Controller
    {
        private readonly BrainPachoContext _context;
        private const int MaxQuestionLength = 500;
        private const int MaxAnswerLength = 200;

        public QuestionsController(BrainPachoContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Muestra el listado completo de preguntas disponibles en la base de datos,
        /// incluyendo sus respuestas asociadas. Ordenadas por campo <c>Order</c>.
        /// </summary>
        public async Task<IActionResult> Index()
        {
            var questions = await _context.Questions
                .Include(q => q.Answers)
                .OrderBy(q => q.Order)
                .ToListAsync();

            return View(questions);
        }

        /// <summary>
        /// Muestra los detalles de una pregunta específica junto con sus respuestas.
        /// </summary>
        /// <param name="id">Identificador de la pregunta.</param>
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var question = await _context.Questions
                .Include(q => q.Answers)
                .FirstOrDefaultAsync(q => q.Id == id);

            if (question == null) return NotFound();

            return View(question);
        }

        /// <summary>
        /// Renderiza el formulario para crear una nueva pregunta con sus posibles respuestas.
        /// Inicializa el modelo con 4 espacios de respuesta vacíos.
        /// </summary>
        public IActionResult Create()
        {
            var vm = new QuestionFormViewModel();
            while (vm.Answers.Count < 4) vm.Answers.Add(new AnswerItemVM());
            return View(vm);
        }

        /// <summary>
        /// Procesa la creación de una nueva pregunta con sus respuestas.
        /// Aplica validaciones de texto, unicidad de orden y consistencia lógica.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(QuestionFormViewModel vm)
        {
            vm.QuestionText = (vm.QuestionText ?? string.Empty).Trim();

            // Validación de longitud del texto de la pregunta
            if (vm.QuestionText.Length > MaxQuestionLength)
                ModelState.AddModelError(nameof(vm.QuestionText), $"El texto supera {MaxQuestionLength} caracteres.");

            // Limpieza y validación de respuestas ingresadas
            var posted = vm.Answers
                .Select(a => new AnswerItemVM
                {
                    Id = a.Id,
                    AnswerText = (a.AnswerText ?? string.Empty).Trim(),
                    IsCorrect = a.IsCorrect
                })
                .Where(a => !string.IsNullOrWhiteSpace(a.AnswerText))
                .ToList();

            if (posted.Count < 2)
                ModelState.AddModelError("", "Debe haber al menos 2 respuestas con texto.");
            if (!posted.Any(a => a.IsCorrect))
                ModelState.AddModelError("", "Debe marcar al menos una respuesta como correcta.");
            if (posted.Any(a => a.AnswerText.Length > MaxAnswerLength))
                ModelState.AddModelError("", $"Alguna respuesta supera {MaxAnswerLength} caracteres.");

            // Valida que el orden sea único
            if (await _context.Questions.AnyAsync(q => q.Order == vm.Order))
                ModelState.AddModelError(nameof(vm.Order), "Ya existe una pregunta con este orden.");

            if (!ModelState.IsValid)
            {
                while (vm.Answers.Count < 4) vm.Answers.Add(new AnswerItemVM());
                return View(vm);
            }

            var question = new Question
            {
                QuestionText = vm.QuestionText,
                Order = vm.Order,
                CreatedAt = DateTime.Now
            };

            foreach (var a in posted)
            {
                question.Answers.Add(new Answer
                {
                    AnswerText = a.AnswerText,
                    IsCorrect = a.IsCorrect,
                    IsActive = true
                });
            }

            _context.Questions.Add(question);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Pregunta creada exitosamente con {posted.Count} respuestas.";
            return RedirectToAction(nameof(Index));
        }

        /// <summary>
        /// Renderiza el formulario de edición de una pregunta existente,
        /// cargando sus respuestas activas para su modificación.
        /// </summary>
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var question = await _context.Questions
                .Include(q => q.Answers)
                .FirstOrDefaultAsync(q => q.Id == id);

            if (question == null) return NotFound();

            var vm = new QuestionFormViewModel
            {
                Id = question.Id,
                QuestionText = question.QuestionText,
                Order = question.Order,
                Answers = question.Answers
                    .OrderBy(a => a.Id)
                    .Select(a => new AnswerItemVM
                    {
                        Id = a.Id,
                        AnswerText = a.AnswerText,
                        IsCorrect = a.IsCorrect
                    }).ToList()
            };

            while (vm.Answers.Count < 4) vm.Answers.Add(new AnswerItemVM());
            ViewBag.QuestionId = question.Id;
            ViewBag.CurrentAnswersCount = question.Answers.Count;

            return View(vm);
        }

        /// <summary>
        /// Procesa la actualización de una pregunta y sus respuestas asociadas.
        /// Implementa manejo transaccional y eliminación segura (soft delete) cuando una respuesta
        /// ya ha sido utilizada por un experto.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, QuestionFormViewModel vm)
        {
            if (id != vm.Id) return BadRequest();

            vm.QuestionText = (vm.QuestionText ?? string.Empty).Trim();

            if (vm.QuestionText.Length > MaxQuestionLength)
                ModelState.AddModelError(nameof(vm.QuestionText), $"El texto supera {MaxQuestionLength} caracteres.");

            var posted = vm.Answers
                .Select(a => new AnswerItemVM
                {
                    Id = a.Id,
                    AnswerText = (a.AnswerText ?? string.Empty).Trim(),
                    IsCorrect = a.IsCorrect
                })
                .Where(a => !string.IsNullOrWhiteSpace(a.AnswerText))
                .ToList();

            if (posted.Count < 2)
                ModelState.AddModelError("", "Debe mantener al menos 2 respuestas.");
            if (!posted.Any(a => a.IsCorrect))
                ModelState.AddModelError("", "Debe mantener al menos una respuesta correcta.");

            if (await _context.Questions.AnyAsync(q => q.Order == vm.Order && q.Id != id))
                ModelState.AddModelError(nameof(vm.Order), "Ya existe una pregunta con este orden.");

            if (!ModelState.IsValid)
            {
                while (vm.Answers.Count < 4) vm.Answers.Add(new AnswerItemVM());
                return View(vm);
            }

            using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                var question = await _context.Questions
                    .Include(q => q.Answers)
                    .FirstOrDefaultAsync(q => q.Id == id);

                if (question == null) return NotFound();

                // Actualización de texto y orden
                question.QuestionText = vm.QuestionText;
                question.Order = vm.Order;

                // Mapa de respuestas existentes
                var postedExistingIds = posted.Where(a => a.Id > 0).Select(a => a.Id).ToHashSet();
                var existingById = question.Answers.ToDictionary(a => a.Id, a => a);

                // Elimina o desactiva las respuestas removidas
                foreach (var ans in question.Answers.ToList())
                {
                    bool stillPosted = postedExistingIds.Contains(ans.Id);
                    if (stillPosted) continue;

                    bool used = await _context.ExpertAnswers.AnyAsync(ea => ea.AnswerId == ans.Id);

                    if (used)
                    {
                        ans.IsActive = false; // Soft delete
                        _context.Answers.Update(ans);
                    }
                    else
                    {
                        _context.Answers.Remove(ans); // Hard delete
                    }
                }

                // Actualiza respuestas existentes
                foreach (var aVm in posted.Where(a => a.Id > 0))
                {
                    if (existingById.TryGetValue(aVm.Id, out var ans))
                    {
                        ans.AnswerText = aVm.AnswerText;
                        ans.IsCorrect = aVm.IsCorrect;
                        ans.IsActive = true;
                        _context.Answers.Update(ans);
                    }
                }

                // Agrega nuevas respuestas
                foreach (var aVm in posted.Where(a => a.Id == 0))
                {
                    question.Answers.Add(new Answer
                    {
                        AnswerText = aVm.AnswerText,
                        IsCorrect = aVm.IsCorrect,
                        QuestionId = question.Id,
                        IsActive = true
                    });
                }

                _context.Questions.Update(question);
                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                TempData["SuccessMessage"] = "Pregunta y respuestas actualizadas correctamente.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                ModelState.AddModelError("", $"Error al actualizar: {ex.GetBaseException().Message}");
                return View(vm);
            }
        }

        /// <summary>
        /// Muestra la vista de confirmación de eliminación de pregunta.
        /// </summary>
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var question = await _context.Questions
                .Include(q => q.Answers)
                .FirstOrDefaultAsync(q => q.Id == id);

            if (question == null) return NotFound();

            ViewBag.HasExpertAnswers = await _context.ExpertAnswers.AnyAsync(ea => ea.QuestionId == id);
            return View(question);
        }

        /// <summary>
        /// Elimina definitivamente una pregunta si no posee respuestas asociadas a expertos.
        /// </summary>
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            try
            {
                var question = await _context.Questions
                    .Include(q => q.Answers)
                    .FirstOrDefaultAsync(q => q.Id == id);

                if (question != null)
                {
                    bool hasExpertAnswers = await _context.ExpertAnswers.AnyAsync(ea => ea.QuestionId == id);
                    if (hasExpertAnswers)
                    {
                        TempData["ErrorMessage"] = "No se puede eliminar: tiene respuestas de expertos asociadas.";
                        return RedirectToAction(nameof(Index));
                    }

                    _context.Questions.Remove(question);
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = $"Pregunta #{question.Order} eliminada exitosamente.";
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error al eliminar: " + ex.GetBaseException().Message;
            }

            return RedirectToAction(nameof(Index));
        }

        // ===================== MÉTODOS AJAX =====================

        /// <summary>
        /// Verifica si el número de orden de pregunta ya está en uso.
        /// </summary>
        [HttpGet]
        public async Task<JsonResult> ValidateOrder(int order, int? questionId = null)
        {
            try
            {
                var q = _context.Questions.Where(x => x.Order == order);
                if (questionId.HasValue) q = q.Where(x => x.Id != questionId.Value);
                return Json(!await q.AnyAsync());
            }
            catch { return Json(false); }
        }

        /// <summary>
        /// Devuelve el siguiente número de orden disponible para una nueva pregunta.
        /// </summary>
        [HttpGet]
        public async Task<JsonResult> GetNextOrder()
        {
            try
            {
                var max = await _context.Questions.AnyAsync()
                    ? await _context.Questions.MaxAsync(q => q.Order)
                    : 0;
                return Json(max + 1);
            }
            catch { return Json(1); }
        }

        /// <summary>
        /// Valida la coherencia de las respuestas enviadas mediante AJAX.
        /// Verifica cantidad mínima, existencia de respuesta correcta y longitud.
        /// </summary>
        [HttpPost]
        public JsonResult ValidateAnswers([FromBody] List<AnswerItemVM> answers)
        {
            try
            {
                var withText = answers?
                    .Select(a => new { Text = (a.AnswerText ?? string.Empty).Trim(), a.IsCorrect })
                    .Where(a => !string.IsNullOrWhiteSpace(a.Text))
                    .ToList() ?? new();

                var msgs = new List<string>();
                if (withText.Count < 2) msgs.Add($"Debe haber al menos 2 respuestas.");
                if (!withText.Any(a => a.IsCorrect)) msgs.Add("Debe existir al menos una respuesta correcta.");
                if (withText.Any(a => a.Text.Length > MaxAnswerLength)) msgs.Add($"Alguna respuesta supera {MaxAnswerLength} caracteres.");

                return Json(new
                {
                    isValid = withText.Count >= 2 && withText.Any(a => a.IsCorrect) && msgs.Count == 0,
                    totalAnswers = withText.Count,
                    correctAnswers = withText.Count(a => a.IsCorrect),
                    messages = msgs
                });
            }
            catch
            {
                return Json(new { isValid = false, messages = new[] { "Error al validar respuestas." } });
            }
        }

        /// <summary>
        /// Obtiene estadísticas generales del banco de preguntas.
        /// Incluye totales, promedios y cantidad de preguntas con múltiples respuestas correctas.
        /// </summary>
        [HttpGet]
        public async Task<JsonResult> GetStatistics()
        {
            try
            {
                var questions = await _context.Questions.Include(q => q.Answers).ToListAsync();
                var stats = new
                {
                    totalQuestions = questions.Count,
                    totalAnswers = questions.Sum(q => q.Answers.Count),
                    totalCorrectAnswers = questions.Sum(q => q.Answers.Count(a => a.IsCorrect)),
                    averageAnswersPerQuestion = questions.Count > 0 ? Math.Round((double)questions.Sum(q => q.Answers.Count) / questions.Count, 1) : 0,
                    lastOrder = questions.Any() ? questions.Max(q => q.Order) : 0,
                    questionsWithMultipleCorrect = questions.Count(q => q.Answers.Count(a => a.IsCorrect) > 1)
                };
                return Json(stats);
            }
            catch
            {
                return Json(new { error = "Error al obtener estadísticas." });
            }
        }

        /// <summary>
        /// Realiza una búsqueda textual de preguntas según un término ingresado.
        /// Devuelve un máximo de 10 coincidencias con metadatos resumidos.
        /// </summary>
        [HttpGet]
        public async Task<JsonResult> SearchQuestions(string searchTerm)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(searchTerm))
                    return Json(new List<object>());

                var questions = await _context.Questions
                    .Include(q => q.Answers)
                    .Where(q => q.QuestionText.Contains(searchTerm))
                    .OrderBy(q => q.Order)
                    .Take(10)
                    .Select(q => new
                    {
                        id = q.Id,
                        order = q.Order,
                        questionText = q.QuestionText.Length > 100 ? q.QuestionText.Substring(0, 100) + "..." : q.QuestionText,
                        answersCount = q.Answers.Count,
                        correctAnswersCount = q.Answers.Count(a => a.IsCorrect),
                        createdAt = q.CreatedAt.ToString("dd/MM/yyyy")
                    })
                    .ToListAsync();

                return Json(questions);
            }
            catch
            {
                return Json(new List<object>());
            }
        }

        /// <summary>
        /// Cambia dinámicamente el orden de una pregunta si el nuevo orden no está ocupado.
        /// </summary>
        [HttpPost]
        public async Task<JsonResult> ChangeOrder(int questionId, int newOrder)
        {
            try
            {
                var q = await _context.Questions.FindAsync(questionId);
                if (q == null) return Json(new { success = false, message = "Pregunta no encontrada." });

                bool exists = await _context.Questions.AnyAsync(x => x.Order == newOrder && x.Id != questionId);
                if (exists) return Json(new { success = false, message = "El nuevo orden ya está en uso." });

                var old = q.Order;
                q.Order = newOrder;
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = $"Orden cambiado de {old} a {newOrder} exitosamente." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al cambiar el orden: " + ex.GetBaseException().Message });
            }
        }

        /// <summary>
        /// Duplica una pregunta existente junto con sus respuestas.
        /// Asigna automáticamente un nuevo orden consecutivo.
        /// </summary>

        [HttpPost]
        public async Task<JsonResult> DuplicateQuestion(int questionId)
        {
            try
            {
                var original = await _context.Questions
                    .Include(q => q.Answers)
                    .FirstOrDefaultAsync(q => q.Id == questionId);

                if (original == null)
                    return Json(new { success = false, message = "Pregunta no encontrada." });

                var newOrder = (await _context.Questions.MaxAsync(q => q.Order)) + 1;

                var copy = new Question
                {
                    QuestionText = original.QuestionText + " (Copia)",
                    Order = newOrder,
                    CreatedAt = DateTime.Now
                };
                _context.Questions.Add(copy);
                await _context.SaveChangesAsync();

                foreach (var oa in original.Answers)
                {
                    _context.Answers.Add(new Answer
                    {
                        AnswerText = oa.AnswerText,
                        IsCorrect = oa.IsCorrect,
                        QuestionId = copy.Id,
                        IsActive = true
                    });
                }
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = $"Pregunta duplicada con orden {newOrder}.", newQuestionId = copy.Id });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al duplicar la pregunta: " + ex.GetBaseException().Message });
            }
        }
    }
}
