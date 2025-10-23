using System;
using System.Collections.Generic;

namespace Pacho.Models;

public partial class User
{
    public int IdUser { get; set; }

    public string Email { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string LastName { get; set; } = null!;

    public int Role { get; set; }

    public int? Status { get; set; }

    public DateTime? RegistrationDate { get; set; }

    public DateTime? LastAccess { get; set; }
    public string? Phone { get; set; }
    public string? RecoveryToken { get; set; }

    public DateTime? RetokenExpirationDate { get; set; }

    public virtual Expert? Expert { get; set; }

    public virtual Role RoleNavigation { get; set; } = null!;
}
