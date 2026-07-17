using Microsoft.AspNetCore.Mvc;
using Nornis.Api.Contracts.Responses;
using Nornis.Domain.Repositories;

namespace Nornis.Api.Controllers;

/// <summary>
/// User directory for member pickers: any authenticated user can list usernames + ids
/// (no emails or auth identifiers) so GMs can add members by name instead of GUID.
/// </summary>
[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly IUserRepository _userRepository;

    public UsersController(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var users = await _userRepository.ListAsync(ct);
        return Ok(users.Select(u => new UserSummaryResponse(u.Id, u.Username)).ToList());
    }
}
