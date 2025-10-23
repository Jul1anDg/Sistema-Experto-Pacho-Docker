using System;
using System.Collections.Generic;

namespace Pacho.Models;

public partial class Role
{
    public int IdRole { get; set; }

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public string? Permits { get; set; }

    public bool Asset { get; set; }

    public virtual ICollection<User> Users { get; set; } = new List<User>();
}
