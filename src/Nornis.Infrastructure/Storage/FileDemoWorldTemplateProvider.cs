using Microsoft.Extensions.Options;
using Nornis.Application.Configuration;
using Nornis.Application.Storage;

namespace Nornis.Infrastructure.Storage;

/// <summary>
/// Serves the demo-world template zip from a file path configured at
/// <c>DemoWorlds:TemplatePath</c>; relative paths resolve against the app's base directory
/// (the template ships with the deployment). A missing file simply disables demo creation.
/// </summary>
public class FileDemoWorldTemplateProvider : IDemoWorldTemplateProvider
{
    private readonly string? _resolvedPath;

    public FileDemoWorldTemplateProvider(IOptions<DemoWorldOptions> options)
    {
        var configured = options.Value.TemplatePath;
        if (string.IsNullOrWhiteSpace(configured))
        {
            return;
        }

        _resolvedPath = Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(AppContext.BaseDirectory, configured);
    }

    public bool IsAvailable => _resolvedPath is not null && File.Exists(_resolvedPath);

    public Stream OpenRead()
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException("No demo world template is configured.");
        }

        return File.OpenRead(_resolvedPath!);
    }
}
