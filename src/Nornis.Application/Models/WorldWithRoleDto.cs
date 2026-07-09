using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

public record WorldWithRoleDto(
    World World,
    WorldRole Role);
