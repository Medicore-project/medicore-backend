using System.Text.Json;
using MediCore.Contracts.Events.Staff;
using MediCore.Identity.Application.DTOs;
using MediCore.Identity.Application.Entities;
using MediCore.Identity.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MediCore.Identity.Infrastructure.Persistence.Repositories;

public class StaffRepository : IStaffRepository
{
    private const string StaffEventsTopic = "staff-events";
    private readonly IdentityDbContext _context;

    public StaffRepository(IdentityDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<StaffResponse>> GetPagedAsync(
        int page,
        int pageSize,
        string? searchTerm,
        Guid? departmentId,
        string? role,
        bool? isActive,
        CancellationToken cancellationToken = default)
    {
        var query = _context.StaffProfiles
            .Include(s => s.User)
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.Trim().ToLower();
            query = query.Where(s =>
                s.FirstName.ToLower().Contains(term) ||
                s.LastName.ToLower().Contains(term) ||
                (s.User != null && s.User.Email.ToLower().Contains(term)) ||
                (s.Phone != null && s.Phone.Contains(term)) ||
                (s.Specialization != null && s.Specialization.ToLower().Contains(term)));
        }

        if (departmentId.HasValue)
        {
            query = query.Where(s => s.DepartmentId == departmentId.Value);
        }

        if (!string.IsNullOrWhiteSpace(role))
        {
            var roleFilter = role.Trim();
            query = query.Where(s => s.User != null && s.User.Role == roleFilter);
        }

        if (isActive.HasValue)
        {
            query = query.Where(s => s.IsActive == isActive.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(s => s.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new StaffResponse(
                s.Id,
                s.UserId,
                s.User != null ? s.User.Email : string.Empty,
                s.User != null ? s.User.Role : string.Empty,
                s.FirstName,
                s.LastName,
                s.FullName,
                s.Phone,
                s.Specialization,
                s.DepartmentId,
                s.HireDate,
                s.IsActive,
                s.CreatedAt,
                s.UpdatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<StaffResponse>(items, totalCount, page, pageSize);
    }

    public async Task<StaffResponse?> GetByIdAsync(Guid staffId, CancellationToken cancellationToken = default)
    {
        var staff = await _context.StaffProfiles
            .Include(s => s.User)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == staffId, cancellationToken);

        if (staff == null)
            return null;

        return new StaffResponse(
            staff.Id,
            staff.UserId,
            staff.User?.Email ?? string.Empty,
            staff.User?.Role ?? string.Empty,
            staff.FirstName,
            staff.LastName,
            staff.FullName,
            staff.Phone,
            staff.Specialization,
            staff.DepartmentId,
            staff.HireDate,
            staff.IsActive,
            staff.CreatedAt,
            staff.UpdatedAt);
    }

    public async Task<User?> GetUserByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = email.Trim().ToLower();
        return await _context.Users
            .Include(u => u.StaffProfile)
            .FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail, cancellationToken);
    }

    public async Task<StaffResponse> CreateStaffAsync(
        CreateStaffRequest request,
        string passwordHash,
        CancellationToken cancellationToken = default)
    {
        var user = new User
        {
            Email = request.Email.Trim(),
            PasswordHash = passwordHash,
            Role = request.Role.Trim(),
            IsActive = true
        };

        var staff = new StaffProfile
        {
            UserId = user.Id,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Phone = request.Phone?.Trim() ?? string.Empty,
            Specialization = request.Specialization?.Trim() ?? string.Empty,
            DepartmentId = request.DepartmentId,
            HireDate = DateTime.UtcNow,
            IsActive = true
        };

        var createdEvent = new StaffCreatedEvent
        {
            StaffId = staff.Id,
            UserId = user.Id,
            FullName = staff.FullName,
            Email = user.Email,
            Role = user.Role,
            Specialization = staff.Specialization,
            DepartmentId = staff.DepartmentId
        };

        var outboxMessage = new OutboxMessage
        {
            Topic = StaffEventsTopic,
            EventKey = staff.Id.ToString(),
            EventType = createdEvent.EventType,
            Payload = JsonSerializer.Serialize(createdEvent),
            OccurredOnUtc = DateTime.UtcNow
        };

        await _context.Users.AddAsync(user, cancellationToken);
        await _context.StaffProfiles.AddAsync(staff, cancellationToken);
        await _context.OutboxMessages.AddAsync(outboxMessage, cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);

        return new StaffResponse(
            staff.Id,
            user.Id,
            user.Email,
            user.Role,
            staff.FirstName,
            staff.LastName,
            staff.FullName,
            staff.Phone,
            staff.Specialization,
            staff.DepartmentId,
            staff.HireDate,
            staff.IsActive,
            staff.CreatedAt,
            staff.UpdatedAt);
    }

    public async Task<StaffResponse?> UpdateStaffAsync(
        Guid staffId,
        UpdateStaffRequest request,
        CancellationToken cancellationToken = default)
    {
        var staff = await _context.StaffProfiles
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => s.Id == staffId, cancellationToken);

        if (staff == null)
            return null;

        staff.FirstName = request.FirstName.Trim();
        staff.LastName = request.LastName.Trim();
        staff.Phone = request.Phone?.Trim() ?? string.Empty;
        staff.Specialization = request.Specialization?.Trim() ?? string.Empty;
        staff.DepartmentId = request.DepartmentId;
        staff.IsActive = request.IsActive;

        if (staff.User != null)
        {
            staff.User.IsActive = request.IsActive;
        }

        var updatedEvent = new StaffUpdatedEvent
        {
            StaffId = staff.Id,
            FullName = staff.FullName,
            Specialization = staff.Specialization,
            DepartmentId = staff.DepartmentId
        };

        var outboxMessage = new OutboxMessage
        {
            Topic = StaffEventsTopic,
            EventKey = staff.Id.ToString(),
            EventType = updatedEvent.EventType,
            Payload = JsonSerializer.Serialize(updatedEvent),
            OccurredOnUtc = DateTime.UtcNow
        };

        await _context.OutboxMessages.AddAsync(outboxMessage, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return new StaffResponse(
            staff.Id,
            staff.UserId,
            staff.User?.Email ?? string.Empty,
            staff.User?.Role ?? string.Empty,
            staff.FirstName,
            staff.LastName,
            staff.FullName,
            staff.Phone,
            staff.Specialization,
            staff.DepartmentId,
            staff.HireDate,
            staff.IsActive,
            staff.CreatedAt,
            staff.UpdatedAt);
    }

    public async Task<bool> DeactivateStaffAsync(Guid staffId, CancellationToken cancellationToken = default)
    {
        var staff = await _context.StaffProfiles
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => s.Id == staffId, cancellationToken);

        if (staff == null)
            return false;

        staff.IsActive = false;
        staff.IsDeleted = true;

        if (staff.User != null)
        {
            staff.User.IsActive = false;
            staff.User.IsDeleted = true;
        }

        var deactivatedEvent = new StaffDeactivatedEvent
        {
            StaffId = staff.Id
        };

        var outboxMessage = new OutboxMessage
        {
            Topic = StaffEventsTopic,
            EventKey = staff.Id.ToString(),
            EventType = deactivatedEvent.EventType,
            Payload = JsonSerializer.Serialize(deactivatedEvent),
            OccurredOnUtc = DateTime.UtcNow
        };

        await _context.OutboxMessages.AddAsync(outboxMessage, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }
}
