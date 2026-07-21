using System.Reflection;

namespace Nornis.Web;

/// <summary>
/// The running build's version as MAJOR.MINOR.BUILD, read once from the Web assembly. MAJOR and
/// MINOR are set in <c>Directory.Build.props</c>; BUILD is the CI build number (0 in local dev).
/// </summary>
public static class AppVersion
{
    public static string Display { get; } = Compute();

    private static string Compute()
    {
        var version = typeof(AppVersion).Assembly.GetName().Version;
        return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
