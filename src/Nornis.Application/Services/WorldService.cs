using Nornis.Application.Authorization;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

public class WorldService : IWorldService
{
    private readonly IWorldRepository _worldRepository;
    private readonly IWorldMemberRepository _worldMemberRepository;

    public WorldService(
        IWorldRepository worldRepository,
        IWorldMemberRepository worldMemberRepository)
    {
        _worldRepository = worldRepository;
        _worldMemberRepository = worldMemberRepository;
    }

    public async Task<AppResult<World>> CreateAsync(CreateWorldCommand command, CancellationToken ct)
    {
        var nameValidation = ValidateName(command.Name);
        if (nameValidation is not null)
        {
            return AppResult<World>.Fail(nameValidation);
        }

        var now = DateTimeOffset.UtcNow;

        var world = new World
        {
            Id = Guid.NewGuid(),
            Name = command.Name,
            Description = command.Description,
            GameSystem = command.GameSystem,
            CreatedByUserId = command.CreatingUserId,
            CreatedAt = now,
            UpdatedAt = now
        };

        world = await _worldRepository.CreateAsync(world, ct);

        var member = new WorldMember
        {
            Id = Guid.NewGuid(),
            WorldId = world.Id,
            UserId = command.CreatingUserId,
            Role = WorldRole.GM,
            JoinedAt = now
        };

        await _worldMemberRepository.CreateAsync(member, ct);

        return AppResult<World>.Success(world);
    }

    public async Task<AppResult<World>> GetByIdAsync(Guid worldId, Guid requestingUserId, CancellationToken ct)
    {
        var member = await _worldMemberRepository.GetByWorldAndUserAsync(worldId, requestingUserId, ct);

        if (member is null)
        {
            return AppResult<World>.Fail(new AppError(403, "access_denied", "You are not a member of this world."));
        }

        var world = await _worldRepository.GetByIdAsync(worldId, ct);

        if (world is null)
        {
            return AppResult<World>.Fail(new AppError(404, "not_found", "World not found."));
        }

        return AppResult<World>.Success(world);
    }

    public async Task<AppResult<World>> UpdateAsync(UpdateWorldCommand command, CancellationToken ct)
    {
        var member = await _worldMemberRepository.GetByWorldAndUserAsync(command.WorldId, command.ActingUserId, ct);

        if (member is null || !member.Role.IsAtLeast(WorldRole.GM))
        {
            return AppResult<World>.Fail(new AppError(403, "insufficient_role", "Only a GM can update world settings."));
        }

        var world = await _worldRepository.GetByIdAsync(command.WorldId, ct);

        if (world is null)
        {
            return AppResult<World>.Fail(new AppError(404, "not_found", "World not found."));
        }

        if (command.Name is not null)
        {
            var nameValidation = ValidateName(command.Name);
            if (nameValidation is not null)
            {
                return AppResult<World>.Fail(nameValidation);
            }

            world.Name = command.Name;
        }

        if (command.Description is not null)
        {
            world.Description = command.Description;
        }

        if (command.GameSystem is not null)
        {
            world.GameSystem = command.GameSystem;
        }

        if (command.DailyAiBudgetUsd is not null)
        {
            if (command.DailyAiBudgetUsd is < 0.01m or > 100m)
            {
                return AppResult<World>.Fail(new AppError(400, "validation_error",
                    "Daily AI budget must be between $0.01 and $100."));
            }

            world.DailyAiBudgetUsd = command.DailyAiBudgetUsd;
        }
        else if (command.ClearDailyAiBudget)
        {
            world.DailyAiBudgetUsd = null;
        }

        world.UpdatedAt = DateTimeOffset.UtcNow;

        world = await _worldRepository.UpdateAsync(world, ct);

        return AppResult<World>.Success(world);
    }

    public async Task<AppResult<IReadOnlyList<WorldWithRoleDto>>> ListForUserAsync(Guid userId, CancellationToken ct)
    {
        var worlds = await _worldRepository.ListByUserAsync(userId, ct);

        var result = new List<WorldWithRoleDto>();

        foreach (var world in worlds)
        {
            var member = await _worldMemberRepository.GetByWorldAndUserAsync(world.Id, userId, ct);
            if (member is not null)
            {
                result.Add(new WorldWithRoleDto(world, member.Role));
            }
        }

        return AppResult<IReadOnlyList<WorldWithRoleDto>>.Success(result);
    }

    private static AppError? ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return new AppError(400, "validation_error", "World name must not be empty or whitespace.");
        }

        if (name.Length > 100)
        {
            return new AppError(400, "validation_error", "World name must be between 1 and 100 characters.");
        }

        return null;
    }
}
