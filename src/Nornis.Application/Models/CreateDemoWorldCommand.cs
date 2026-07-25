namespace Nornis.Application.Models;

public record CreateDemoWorldCommand(
    Guid ActingUserId,
    bool TutorialEnabled);
