using System.Globalization;
using System.Text;
using Jellyfin.Extensions;

namespace Jellyfin.Plugin.MagicCollections.Sync;

internal static class JellyfinQueryValues
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static bool TryPositiveInt(string value, out int number) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number) && number > 0;

    public static string CleanMetadataName(string value) => value.RemoveDiacritics().ToLowerInvariant();

    public static string EncodeList(IEnumerable<string> values) =>
        string.Join(',', values.Select(Uri.EscapeDataString));

    public static string[]? DecodeList(string encoded, int maxItems = int.MaxValue, bool allowEncodedPipe = false)
    {
        if (allowEncodedPipe)
        {
            encoded = encoded.Replace("%7C", ",", StringComparison.OrdinalIgnoreCase);
        }
        var parts = encoded.Split(',', maxItems == int.MaxValue ? maxItems : maxItems + 1);
        if (parts.Length > maxItems)
        {
            return null;
        }
        for (var index = 0; index < parts.Length; index++)
        {
            if (DecodeValue(parts[index]) is not { Length: > 0 } part)
            {
                return null;
            }
            parts[index] = part;
        }
        return parts;
    }

    public static string? DecodeValue(string value)
    {
        var decoded = new StringBuilder(value.Length);
        var bytes = new List<byte>();
        for (int index = 0; index < value.Length;)
        {
            var ch = value[index];
            if (ch == '%')
            {
                bytes.Clear();
                while (index < value.Length && value[index] == '%')
                {
                    if (index + 2 >= value.Length || !byte.TryParse(value.AsSpan(index + 1, 2),
                        NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var octet))
                    {
                        return null;
                    }
                    bytes.Add(octet);
                    index += 3;
                }
                try
                {
                    decoded.Append(StrictUtf8.GetString(bytes.ToArray()));
                }
                catch (DecoderFallbackException)
                {
                    return null;
                }
                continue;
            }

            if (!char.IsAsciiLetterOrDigit(ch) && ch is not ('-' or '_' or '.' or '~' or ',' or '+'))
            {
                return null;
            }
            decoded.Append(ch == '+' ? ' ' : ch);
            index++;
        }
        var result = decoded.ToString();
        return result.Any(char.IsControl) ? null : result.Trim();
    }
}
