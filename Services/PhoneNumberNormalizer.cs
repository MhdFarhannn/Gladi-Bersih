using System.Text.RegularExpressions;
using Gladi_Bersih.Common;

namespace Gladi_Bersih.Services;

public static partial class PhoneNumberNormalizer
{
    public static string Normalize(string value)
    {
        var input = value.Trim();
        if (!AllowedCharactersRegex().IsMatch(input))
        {
            throw new AppException(StatusCodes.Status400BadRequest, "Format nomor telepon tidak valid.");
        }

        var digits = NonDigitRegex().Replace(input, string.Empty);
        if (digits.StartsWith("62", StringComparison.Ordinal))
        {
            digits = $"0{digits[2..]}";
        }
        else if (digits.StartsWith('8'))
        {
            digits = $"0{digits}";
        }

        if (!digits.StartsWith("08", StringComparison.Ordinal) || digits.Length is < 10 or > 15)
        {
            throw new AppException(
                StatusCodes.Status400BadRequest,
                "Nomor telepon harus berformat Indonesia dan diawali 08.");
        }

        return digits;
    }

    [GeneratedRegex(@"^[0-9+() .-]+$")]
    private static partial Regex AllowedCharactersRegex();

    [GeneratedRegex(@"\D")]
    private static partial Regex NonDigitRegex();
}
