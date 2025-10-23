using System;
using System.Collections.Generic;

namespace Pacho.Models;

public partial class Expert
{
    public int IdExpert { get; set; }

    public int UserId { get; set; }

    public string? ExperienceType { get; set; }

    public decimal? ExperienceYears { get; set; }

    public string? TestState { get; set; }

    public double? TestGrade { get; set; }

    public DateTime? ApprovalDate { get; set; }

    public double? PlatformGrade { get; set; }

    public int? TreatmentsTotal { get; set; }

    public string? ConfidenceLevel { get; set; }

    public virtual User User { get; set; } = null!;
    public virtual ICollection<ExpertAnswer> ExpertAnswers { get; set; } = new List<ExpertAnswer>();


}
