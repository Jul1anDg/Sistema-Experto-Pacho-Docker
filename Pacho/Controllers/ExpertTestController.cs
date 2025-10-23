using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;
using Pacho.Models;
using System.Security.Claims;

namespace Pacho.Controllers
{
    /// <summary>
    /// Controlador que gestiona la lógica de las pruebas de aptitud de los expertos.
    /// Solo accesible para usuarios con el rol "Experto".
    /// </summary>
    [Authorize(Roles = "Experto")]
    public class ExpertTestController : Controller
    {
        private readonly BrainPachoContext _context;

        public ExpertTestController(BrainPachoContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Muestra el test de aptitud disponible para el experto autenticado.
        /// Solo se cargan preguntas con al menos 2 respuestas activas y una correcta.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> TakeTest()
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdStr, out var userId)) return Unauthorized();

            var expert = await _context.Experts.AsNoTracking()
                .FirstOrDefaultAsync(e => e.UserId == userId);
            if (expert == null) return Forbid();

            if (!string.Equals(expert.TestState, "habilitado", StringComparison.OrdinalIgnoreCase))
                return View("~/Views/Account/AccessDenied.cshtml");

            var model = await _context.Questions
                .Where(q => q.Answers.Count(a => a.IsActive) >= 2)
                .Where(q => q.Answers.Count(a => a.IsActive && a.IsCorrect) == 1)
                .OrderBy(q => q.Order)
                .Select(q => new Question
                {
                    Id = q.Id,
                    QuestionText = q.QuestionText,
                    Answers = q.Answers
                        .Where(a => a.IsActive)
                        .OrderBy(a => a.Id)
                        .Select(a => new Answer
                        {
                            Id = a.Id,
                            AnswerText = a.AnswerText
                        })
                        .ToList()
                })
                .AsNoTracking()
                .ToListAsync();

            if (model.Count == 0)
            {
                TempData["ErrorMessage"] =
                    "No hay preguntas válidas para el test (verifica que cada pregunta tenga al menos 2 respuestas activas y 1 correcta).";
                return RedirectToAction("Index", "Home");
            }

            return View(model);
        }

        /// <summary>
        /// Procesa y evalúa las respuestas enviadas por el experto.
        /// Calcula la nota final, guarda las respuestas en la base de datos
        /// y actualiza el estado del experto (aprobado o reprobado).
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitTest()
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdStr, out var userId)) return Unauthorized();

            var expert = await _context.Experts.FirstOrDefaultAsync(e => e.UserId == userId);
            if (expert == null) return Forbid();

            var questions = await _context.Questions
                .Where(q => q.Answers.Count(a => a.IsActive) >= 2)
                .Where(q => q.Answers.Count(a => a.IsActive && a.IsCorrect) == 1)
                .Select(q => new { q.Id })
                .ToListAsync();

            var questionIds = questions.Select(q => q.Id).ToList();
            var selected = new Dictionary<int, int>();

            // Recuperar respuestas seleccionadas desde el formulario
            foreach (var qid in questionIds)
            {
                var key = $"respuestas[{qid}]";
                if (Request.Form.TryGetValue(key, out StringValues sv) &&
                    int.TryParse(sv.ToString(), out int ansId))
                {
                    selected[qid] = ansId;
                }
            }

            if (selected.Count != questionIds.Count)
            {
                TempData["ErrorMessage"] = "Debes responder todas las preguntas antes de enviar.";
                return RedirectToAction(nameof(TakeTest));
            }

            // Obtener ID de respuesta correcta por pregunta
            var correctByQ = await _context.Questions
                .Where(q => questionIds.Contains(q.Id))
                .Select(q => new
                {
                    q.Id,
                    CorrectId = q.Answers
                        .Where(a => a.IsActive && a.IsCorrect)
                        .Select(a => a.Id)
                        .FirstOrDefault()
                })
                .ToDictionaryAsync(x => x.Id, x => x.CorrectId);

            int total = questionIds.Count;
            int aciertos = 0;
            var now = DateTime.Now;

            // Guardar respuestas seleccionadas y contar aciertos
            foreach (var kv in selected)
            {
                int qid = kv.Key;
                int answerId = kv.Value;
                int correctId = correctByQ.TryGetValue(qid, out var c) ? c : 0;

                if (correctId > 0 && answerId == correctId)
                    aciertos++;

                _context.ExpertAnswers.Add(new ExpertAnswer
                {
                    ExpertId = expert.IdExpert,
                    QuestionId = qid,
                    AnswerId = answerId,
                    AnsweredAt = now
                });
            }

            await _context.SaveChangesAsync();

            // Cálculo de nota y actualización de estado
            double nota = total > 0 ? (aciertos * 100.0 / total) : 0.0;
            var user = await _context.Users.FirstOrDefaultAsync(u => u.IdUser == expert.UserId);

            expert.TestGrade = Math.Round(nota, 2);

            if (nota >= 60.0)
            {
                expert.TestState = "aprobado";
                expert.ApprovalDate = now;
                expert.ConfidenceLevel ??= "principiante";
                if (user != null) user.Status = 1; // activo
            }
            else
            {
                expert.TestState = "reprobado";
                if (user != null) user.Status = 3; // inactivo
            }

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] =
                $"Test finalizado. Aciertos: {aciertos}/{total} — Nota: {nota:0.##}%";
            return RedirectToAction("Resultado");
        }

        /// <summary>
        /// Muestra el resultado final de la prueba al experto autenticado.
        /// Si no tiene nota registrada, redirige al test.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Resultado()
        {
            var email = User.Identity?.Name;
            if (string.IsNullOrEmpty(email)) return Unauthorized();

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (user == null) return NotFound();

            var expert = await _context.Experts
                .Include(e => e.User)
                .FirstOrDefaultAsync(e => e.UserId == user.IdUser);

            if (expert == null) return NotFound();

            if (expert.TestGrade == null)
            {
                TempData["ErrorMessage"] = "Aún no has finalizado la prueba.";
                return RedirectToAction("TakeTest");
            }

            return View(expert);
        }

        /// <summary>
        /// Vista de acceso denegado (usada cuando el experto no cumple las condiciones para acceder a la prueba).
        /// </summary>
        [AllowAnonymous]
        [HttpGet]
        public IActionResult AccessDenied() => View();
    }
}
