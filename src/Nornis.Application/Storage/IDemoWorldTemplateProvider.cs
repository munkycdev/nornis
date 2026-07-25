namespace Nornis.Application.Storage;

/// <summary>
/// Supplies the demo-world template package — a world-export zip curated from the master
/// demo campaign (feature 20). The file ships with the deployment; where it lives is an
/// infrastructure concern.
/// </summary>
public interface IDemoWorldTemplateProvider
{
    /// <summary>True when a template package is configured and present.</summary>
    bool IsAvailable { get; }

    /// <summary>Opens the template zip for reading. Throws when <see cref="IsAvailable"/> is false.</summary>
    Stream OpenRead();
}
