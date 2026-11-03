namespace Nornis.Web.Services;

/// <summary>
/// The Prep list's filter chips. An Adventure filed to the Library as a booklet is Published;
/// one sent into Nornis as a source is Exported. One that is both shows under both.
/// </summary>
public enum PrepListFilter
{
    All,
    Exported,
    Published,
    Neither,
}
