namespace Nornis.Api.Extensions;

public static class EnumParsing
{
    /// <summary>
    /// The one parse for enum-carrying request strings: case-insensitive, by name only.
    /// Bare <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/> accepts any
    /// integer — an out-of-range one yields an undefined value that matches nothing
    /// downstream and renders as an empty result instead of an error (CanonController
    /// found that the hard way), and an in-range one silently couples the wire contract
    /// to enum declaration order. Numerals are rejected outright; no enum name can start
    /// with a digit or a sign.
    /// </summary>
    public static bool TryParseDefined<TEnum>(string value, out TEnum parsed)
        where TEnum : struct, Enum
    {
        if (value.Length > 0 && (char.IsAsciiDigit(value[0]) || value[0] is '-' or '+'))
        {
            parsed = default;
            return false;
        }

        return Enum.TryParse(value, ignoreCase: true, out parsed) && Enum.IsDefined(parsed);
    }
}
