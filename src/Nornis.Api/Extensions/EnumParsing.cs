namespace Nornis.Api.Extensions;

public static class EnumParsing
{
    /// <summary>
    /// The one parse for enum-carrying request strings: case-insensitive, by name only.
    /// Bare <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/> also accepts
    /// numerals ("7"), yielding an undefined value that matches nothing downstream and
    /// renders as an empty result instead of an error — CanonController found that the
    /// hard way. The <see cref="Enum.IsDefined{TEnum}(TEnum)"/> check makes it a 400
    /// everywhere.
    /// </summary>
    public static bool TryParseDefined<TEnum>(string value, out TEnum parsed)
        where TEnum : struct, Enum =>
        Enum.TryParse(value, ignoreCase: true, out parsed) && Enum.IsDefined(parsed);
}
