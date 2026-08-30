namespace MediCore.Identity.Application.Entities;

public class UserRole
{
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

    public User? User { get; set; }
    public Role? Role { get; set; }
}
