using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace Pacho.Models
{
    public partial class BrainPachoContext : DbContext
    {
        public BrainPachoContext(DbContextOptions<BrainPachoContext> options) : base(options) { }

        public virtual DbSet<Expert> Experts { get; set; } = null!;
        public virtual DbSet<Role> Roles { get; set; } = null!;
        public virtual DbSet<User> Users { get; set; } = null!;
        public virtual DbSet<UsersBot> UsersBots { get; set; } = null!;
        public DbSet<Question> Questions { get; set; } = null!;
        public DbSet<Answer> Answers { get; set; } = null!;
        public DbSet<ExpertAnswer> ExpertAnswers { get; set; } = null!;
        public DbSet<Disease> Diseases { get; set; } = null!;
        public DbSet<Treatment> Treatments { get; set; } = null!;
        public DbSet<DiagnosticQuestion> DiagnosticQuestions { get; set; } = null!;
        public DbSet<DiagnosticAnswer> DiagnosticAnswers { get; set; } = null!;

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                // Fallback local opcional (no pisa lo de Program.cs)
                optionsBuilder.UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=SemillaDocker;Trusted_Connection=True;TrustServerCertificate=True;");
            }
        }


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Expert>(entity =>
            {
                entity.HasKey(e => e.IdExpert).HasName("PK__experts__4C8219E6C15E02A7");
                entity.ToTable("experts");

                entity.HasIndex(e => e.ApprovalDate, "IX_experts_approval_date");
                entity.HasIndex(e => e.TestState, "IX_experts_test_state");
                entity.HasIndex(e => e.UserId, "IX_experts_user_id");
                entity.HasIndex(e => e.UserId, "UQ_experts_user").IsUnique();

                entity.Property(e => e.IdExpert).HasColumnName("id_expert");
                entity.Property(e => e.ApprovalDate).HasColumnName("approval_date");
                entity.Property(e => e.ConfidenceLevel)
                      .HasMaxLength(50)
                      .IsUnicode(false)
                      .HasDefaultValue("nuevo")
                      .HasColumnName("confidence_level");
                entity.Property(e => e.ExperienceType)
                      .HasMaxLength(255)
                      .IsUnicode(false)
                      .HasColumnName("experience_type");
                entity.Property(e => e.ExperienceYears)
                      .HasColumnType("decimal(5, 2)")
                      .HasColumnName("experience_years");
                entity.Property(e => e.PlatformGrade).HasColumnName("platform_grade");
                entity.Property(e => e.TestGrade).HasColumnName("test_grade");
                entity.Property(e => e.TestState)
                      .HasMaxLength(50)
                      .IsUnicode(false)
                      .HasDefaultValue("pendiente")
                      .HasColumnName("test_state");
                entity.Property(e => e.TreatmentsTotal)
                      .HasDefaultValue(0)
                      .HasColumnName("treatments_total");
                entity.Property(e => e.UserId).HasColumnName("user_id");

                entity.HasOne(d => d.User).WithOne(p => p.Expert)
                      .HasForeignKey<Expert>(d => d.UserId)
                      .OnDelete(DeleteBehavior.ClientSetNull)
                      .HasConstraintName("FK_experts_user");
            });

            modelBuilder.Entity<Role>(entity =>
            {
                entity.HasKey(e => e.IdRole).HasName("PK__roles__3D48441DF2A1926F");
                entity.ToTable("roles");

                entity.HasIndex(e => e.Name, "IX_roles_name");

                entity.Property(e => e.IdRole).HasColumnName("id_role");
                entity.Property(e => e.Asset).HasColumnName("asset");
                entity.Property(e => e.Description)
                      .HasMaxLength(500)
                      .IsUnicode(false)
                      .HasColumnName("description");
                entity.Property(e => e.Name)
                      .HasMaxLength(255)
                      .IsUnicode(false)
                      .HasColumnName("name");
                entity.Property(e => e.Permits).HasColumnName("permits")
                       .HasColumnType("nvarchar(max)"); // para JSON
            });

            modelBuilder.Entity<User>(entity =>
            {
                entity.HasKey(e => e.IdUser).HasName("PK__users__D2D1463703FD77E4");
                entity.ToTable("users");

                entity.HasIndex(e => e.Email, "IX_users_email");
                entity.HasIndex(e => e.LastAccess, "IX_users_last_access");
                entity.HasIndex(e => new { e.Name, e.LastName }, "IX_users_name_lastname");
                entity.HasIndex(e => e.RegistrationDate, "IX_users_registration_date");
                entity.HasIndex(e => e.Role, "IX_users_role");
                entity.HasIndex(e => e.Status, "IX_users_status");
                entity.HasIndex(e => e.Email, "UQ__users__AB6E616470D67EA6").IsUnique();

                entity.Property(e => e.IdUser).HasColumnName("id_user");
                entity.Property(e => e.Email)
                      .HasMaxLength(255)
                      .IsUnicode(false)
                      .HasColumnName("email");
                entity.Property(e => e.LastAccess).HasColumnName("last_access");
                entity.Property(e => e.LastName)
                      .HasMaxLength(255)
                      .IsUnicode(false)
                      .HasColumnName("last_name");
                entity.Property(e => e.Name)
                      .HasMaxLength(255)
                      .IsUnicode(false)
                      .HasColumnName("name");
                entity.Property(e => e.PasswordHash)
                      .HasMaxLength(255)
                      .IsUnicode(false)
                      .HasColumnName("password_hash");
                entity.Property(e => e.RecoveryToken)
                      .HasMaxLength(255)
                      .IsUnicode(false)
                      .HasColumnName("recovery_token");
                entity.Property(e => e.RegistrationDate)
                      .HasDefaultValueSql("(getdate())")
                      .HasColumnName("registration_date");
                entity.Property(e => e.RetokenExpirationDate).HasColumnName("retoken_expiration_date");
                entity.Property(e => e.Role).HasColumnName("role");
                entity.Property(e => e.Status)
                      .HasDefaultValue(1)
                      .HasColumnName("status");

                entity.HasOne(d => d.RoleNavigation).WithMany(p => p.Users)
                      .HasForeignKey(d => d.Role)
                      .OnDelete(DeleteBehavior.ClientSetNull)
                      .HasConstraintName("FK_users_role");
            });

            modelBuilder.Entity<UsersBot>(entity =>
            {
                entity.HasKey(e => e.IdUserbot).HasName("PK__users_bo__576DE3F22289DEB1");
                entity.ToTable("users_bot");

                entity.HasIndex(e => e.TelegramId, "IX_users_bot_telegram_id");
                entity.HasIndex(e => e.TelegramId, "UQ__users_bo__0CB40226A236799C").IsUnique();

                entity.Property(e => e.IdUserbot)
                      .HasColumnName("id_userbot")
                      .ValueGeneratedNever(); // 👈 valor manual, EF no intentará autogenerarlo

                entity.Property(e => e.Phone)
                      .HasMaxLength(20)
                      .IsUnicode(false)
                      .HasColumnName("phone");

                entity.Property(e => e.TelegramId)
                      .HasMaxLength(255)
                      .IsUnicode(false)
                      .HasColumnName("telegram_id");

                entity.Property(e => e.TotalDiagnoses)
                      .HasDefaultValue(0)
                      .HasColumnName("total_diagnoses");
            });


            modelBuilder.Entity<Question>(entity =>
            {
                entity.ToTable("questions");

                entity.HasKey(q => q.Id);
                entity.Property(q => q.Id).HasColumnName("id");

                entity.Property(q => q.QuestionText)
                    .IsRequired()
                    .HasColumnName("question_text");

                entity.Property(q => q.CreatedAt)
                    .HasDefaultValueSql("GETDATE()")
                    .HasColumnName("created_at");

                entity.Property(q => q.Order)
                    .IsRequired()
                    .HasColumnName("order_position");

                entity.HasMany(q => q.Answers)
                    .WithOne(a => a.Question)
                    .HasForeignKey(a => a.QuestionId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Answer>(entity =>
            {
              
                entity.ToTable("answers");

                entity.HasKey(a => a.Id);
                entity.Property(a => a.Id).HasColumnName("id");

                entity.Property(a => a.QuestionId)
                    .IsRequired()
                    .HasColumnName("question_id");

                entity.Property(a => a.AnswerText)
                    .IsRequired()
                    .HasColumnName("answer_text");

                entity.Property(a => a.IsCorrect)
                    .IsRequired()
                    .HasColumnName("is_correct");

                entity.Property(a => a.IsActive)
                    .HasDefaultValue(true)
                    .HasColumnName("is_active");
                
                entity.HasOne(a => a.Question)
                      .WithMany(q => q.Answers)
                      .HasForeignKey(a => a.QuestionId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<ExpertAnswer>(entity =>
            {
                entity.ToTable("expert_answers");

                entity.HasKey(ea => ea.Id);
                entity.Property(ea => ea.Id).HasColumnName("id");

                entity.Property(ea => ea.ExpertId).HasColumnName("expert_id");
                entity.Property(ea => ea.QuestionId).HasColumnName("question_id");
                entity.Property(ea => ea.AnswerId).HasColumnName("answer_id");
                entity.Property(ea => ea.AnsweredAt)
                      .HasDefaultValueSql("GETDATE()")
                      .HasColumnName("answered_at");

                entity.HasOne(ea => ea.Expert)
                      .WithMany()
                      .HasForeignKey(ea => ea.ExpertId)
                      .OnDelete(DeleteBehavior.Restrict) 
                      .HasConstraintName("FK_expert_answers_expert");

                entity.HasOne(ea => ea.Question)
                      .WithMany()
                      .HasForeignKey(ea => ea.QuestionId)
                      .OnDelete(DeleteBehavior.Restrict) 
                      .HasConstraintName("FK_expert_answers_question");

                entity.HasOne(ea => ea.Answer)
                                  .WithMany(a => a.ExpertAnswers)
                                  .HasForeignKey(ea => ea.AnswerId)
                                  .OnDelete(DeleteBehavior.Restrict);
            });
            // Configuración de Disease
            modelBuilder.Entity<Disease>(entity =>
            {
                entity.HasKey(e => e.IdDisease).HasName("PK_diseases");
                entity.ToTable("diseases");

                entity.Property(e => e.IdDisease).HasColumnName("id_disease");

                entity.Property(e => e.ScientificName)
                    .IsRequired()
                    .HasMaxLength(200)
                    .IsUnicode(false)
                    .HasColumnName("scientific_name");

                entity.Property(e => e.CommonName)
                    .IsRequired()
                    .HasMaxLength(200)
                    .IsUnicode(false)
                    .HasColumnName("common_name");

                entity.Property(e => e.Description)
                    .HasMaxLength(1000)
                    .IsUnicode(false)
                    .HasColumnName("description");

                entity.Property(e => e.Symptoms)
                    .HasMaxLength(1000)
                    .IsUnicode(false)
                    .HasColumnName("symptoms");

                entity.Property(e => e.Conditions)
                    .HasMaxLength(500)
                    .IsUnicode(false)
                    .HasColumnName("conditions");

                entity.Property(e => e.ReferenceImage)
                    .HasMaxLength(500)
                    .IsUnicode(false)
                    .HasColumnName("reference_image");

                entity.Property(e => e.Asset)
                    .HasDefaultValue(true)
                    .HasColumnName("asset");

                entity.Property(e => e.CreationDate)
                    .HasDefaultValueSql("(getdate())")
                    .HasColumnName("creation_date");

                entity.Property(e => e.TreatmentsTotal)
                    .HasDefaultValue(0)
                    .HasColumnName("treatments_total");

                // Índices para mejorar rendimiento
                entity.HasIndex(e => e.CommonName, "IX_diseases_common_name");
                entity.HasIndex(e => e.Asset, "IX_diseases_asset");
                entity.HasIndex(e => e.CreationDate, "IX_diseases_creation_date");
            });

            // Configuración de Treatment
            modelBuilder.Entity<Treatment>(entity =>
            {
                entity.HasKey(e => e.IdTreatment).HasName("PK_treatments");
                entity.ToTable("treatments");

                entity.Property(e => e.IdTreatment).HasColumnName("id_treatment");

                entity.Property(e => e.DiseaseId)
                    .IsRequired()
                    .HasColumnName("disease_id"); 

                entity.Property(e => e.ExpertId)
                    .IsRequired()
                    .HasColumnName("expert_id"); 

                entity.Property(e => e.TreatmentType)
                    .IsRequired()
                    .HasMaxLength(100)
                    .IsUnicode(false)
                    .HasColumnName("treatment_type");

                entity.Property(e => e.Description)
                    .IsRequired()
                    .HasMaxLength(1000)
                    .IsUnicode(false)
                    .HasColumnName("description");

                entity.Property(e => e.RecommendedProducts)
                    .HasMaxLength(500)
                    .IsUnicode(false)
                    .HasColumnName("recommended_products");

                entity.Property(e => e.Frequency)
                    .HasMaxLength(200)
                    .IsUnicode(false)
                    .HasColumnName("frequency");

                entity.Property(e => e.Precautions)
                    .HasMaxLength(500)
                    .IsUnicode(false)
                    .HasColumnName("precautions");

                entity.Property(e => e.WeatherConditions)
                    .HasMaxLength(200)
                    .IsUnicode(false)
                    .HasColumnName("weather_conditions");

                entity.Property(e => e.DiasMejoriaVisual)
                    .HasColumnName("dias_mejoria_visual");

                entity.Property(e => e.Status)
                    .HasDefaultValue(true)
                    .HasColumnName("status");

                entity.Property(e => e.CreationDate)
                    .HasDefaultValueSql("(getdate())")
                    .HasColumnName("creation_date");

                // Configuración de relaciones
                entity.HasOne(t => t.Disease)
                    .WithMany()
                    .HasForeignKey(t => t.DiseaseId)
                    .OnDelete(DeleteBehavior.Restrict) // No eliminar Disease si tiene Treatments
                    .HasConstraintName("FK_treatments_disease");

                entity.HasOne(t => t.Expert)
                    .WithMany()
                    .HasForeignKey(t => t.ExpertId)
                    .OnDelete(DeleteBehavior.Restrict) // No eliminar Expert si tiene Treatments
                    .HasConstraintName("FK_treatments_expert"); 

                // Índices para mejorar rendimiento
                entity.HasIndex(t => t.DiseaseId, "IX_treatments_disease_id");
                entity.HasIndex(t => t.ExpertId, "IX_treatments_expert_id");
                entity.HasIndex(t => t.Status, "IX_treatments_status");
                entity.HasIndex(t => t.CreationDate, "IX_treatments_creation_date");
                entity.HasIndex(t => new { t.ExpertId, t.Status }, "IX_treatments_expert_status");
            });

            modelBuilder.Entity<DiagnosticQuestion>(entity =>
            {
                entity.ToTable("diagnostic_questions");
                entity.HasKey(q => q.Id).HasName("PK_diagnostic_questions");
                entity.Property(q => q.Id).HasColumnName("id_question");
                entity.Property(q => q.QuestionOrder).HasColumnName("question_order");
                entity.Property(q => q.QuestionText)
                      .IsRequired().HasMaxLength(255).IsUnicode(false)
                      .HasColumnName("question_text");
                entity.Property(q => q.CreatedAt)
                      .HasDefaultValueSql("SYSUTCDATETIME()")
                      .HasColumnName("created_at");

                // Un orden único para que no se dupliquen posiciones 1..10
                entity.HasIndex(q => q.QuestionOrder).IsUnique().HasDatabaseName("UX_diag_questions_order");

                entity.HasMany(q => q.Answers)
                      .WithOne(a => a.Question!)
                      .HasForeignKey(a => a.QuestionId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // ANSWERS
            modelBuilder.Entity<DiagnosticAnswer>(entity =>
            {
                entity.ToTable("diagnostic_answers");
                entity.HasKey(a => a.Id).HasName("PK_diagnostic_answers");
                entity.Property(a => a.Id).HasColumnName("id_answer");
                entity.Property(a => a.QuestionId).HasColumnName("question_id");
                entity.Property(a => a.AnswerOrder).HasColumnName("answer_order");
                entity.Property(a => a.AnswerText).IsRequired().HasMaxLength(200).IsUnicode(false).HasColumnName("answer_text");

                // ✅ Si quieres “Si/No”:
                entity.HasCheckConstraint("CK_diag_answers_text", "[answer_text] IN ('Si','No')");
                entity.HasCheckConstraint("CK_diag_answers_order", "[answer_order] IN (1,2)");
                entity.HasIndex(a => new { a.QuestionId, a.AnswerText }).IsUnique().HasDatabaseName("UX_diag_answers_qid_text");
                entity.HasIndex(a => new { a.QuestionId, a.AnswerOrder }).IsUnique().HasDatabaseName("UX_diag_answers_qid_order");
            });

            // ✅ Semilla consistente con el CHECK anterior
            var seedQuestions = Enumerable.Range(1, 10).Select(i => new DiagnosticQuestion
            {
                Id = i,
                QuestionOrder = i,
                QuestionText = $"Question {i}",
            }).ToArray();
            modelBuilder.Entity<DiagnosticQuestion>().HasData(seedQuestions);

            var seedAnswers = new List<DiagnosticAnswer>();
            for (int i = 1; i <= 10; i++)
            {
                seedAnswers.Add(new DiagnosticAnswer { Id = (i - 1) * 2 + 1, QuestionId = i, AnswerOrder = 1, AnswerText = "Si" });
                seedAnswers.Add(new DiagnosticAnswer { Id = (i - 1) * 2 + 2, QuestionId = i, AnswerOrder = 2, AnswerText = "No" });
            }
            modelBuilder.Entity<DiagnosticAnswer>().HasData(seedAnswers);

            OnModelCreatingPartial(modelBuilder);
        }

        partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
    }
}
