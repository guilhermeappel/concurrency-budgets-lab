using System.Globalization;
using System.Numerics;

namespace Lab.Summary;

/// <summary>
/// Number formatting for summary.md. It keeps the text the earlier summary script produced
/// (Python format specs <c>:g</c>, <c>.Nf</c> and <c>.1%</c>): a value is rounded from its exact
/// binary value with ties to even, so 0.125 at two decimals is "0.12"; <see cref="General"/> keeps
/// 6 significant digits without trailing zeros and writes exponents as "1e+06".
/// </summary>
public static class Formatting
{
    private const int GeneralPrecision = 6;

    /// <summary>Python <c>f"{value:.{decimals}f}"</c>.</summary>
    public static string Fixed(double value, int decimals)
    {
        RequireFinite(value);
        var digits = RoundScaled(value, decimals).ToString(CultureInfo.InvariantCulture)
            .PadLeft(decimals + 1, '0');
        var text = decimals == 0 ? digits : digits[..^decimals] + "." + digits[^decimals..];
        return Sign(value) + text;
    }

    /// <summary>Python <c>f"{value:.{decimals}%}"</c>.</summary>
    public static string Percent(double value, int decimals) => Fixed(value * 100, decimals) + "%";

    /// <summary>Python <c>f"{value:g}"</c>.</summary>
    public static string General(double value)
    {
        RequireFinite(value);
        if (value == 0)
        {
            return Sign(value) + "0";
        }

        // Exponent of the value after rounding to 6 significant digits.
        var exponent = (int)Math.Floor(Math.Log10(Math.Abs(value)));
        BigInteger digits;
        while (true)
        {
            digits = RoundScaled(value, GeneralPrecision - 1 - exponent);
            if (digits >= BigInteger.Pow(10, GeneralPrecision)) { exponent++; continue; }
            if (digits < BigInteger.Pow(10, GeneralPrecision - 1)) { exponent--; continue; }
            break;
        }

        if (exponent >= -4 && exponent < GeneralPrecision)
        {
            return StripZeros(Fixed(value, GeneralPrecision - 1 - exponent));
        }

        var mantissa = digits.ToString(CultureInfo.InvariantCulture);
        mantissa = StripZeros(mantissa[..1] + "." + mantissa[1..]);
        var exponentText = Math.Abs(exponent).ToString("00", CultureInfo.InvariantCulture);
        return Sign(value) + mantissa + "e" + (exponent < 0 ? "-" : "+") + exponentText;
    }

    /// <summary>|value| × 10^power, rounded to an integer with ties to even, computed exactly.</summary>
    private static BigInteger RoundScaled(double value, int power)
    {
        var bits = BitConverter.DoubleToInt64Bits(value);
        var exponentBits = (int)((bits >> 52) & 0x7FF);
        var mantissa = bits & 0xFFFFFFFFFFFFFL;
        BigInteger significand = exponentBits == 0 ? mantissa : mantissa | (1L << 52);
        var binaryExponent = (exponentBits == 0 ? 1 : exponentBits) - 1075;

        var numerator = significand;
        var denominator = BigInteger.One;
        if (binaryExponent >= 0) numerator <<= binaryExponent; else denominator <<= -binaryExponent;
        if (power >= 0) numerator *= BigInteger.Pow(10, power); else denominator *= BigInteger.Pow(10, -power);

        var quotient = BigInteger.DivRem(numerator, denominator, out var remainder);
        var twice = remainder * 2;
        if (twice > denominator || (twice == denominator && !quotient.IsEven))
        {
            quotient++;
        }

        return quotient;
    }

    private static string StripZeros(string text) =>
        text.Contains('.') ? text.TrimEnd('0').TrimEnd('.') : text;

    private static string Sign(double value) => double.IsNegative(value) ? "-" : "";

    private static void RequireFinite(double value)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Only finite values are formatted.");
        }
    }
}
