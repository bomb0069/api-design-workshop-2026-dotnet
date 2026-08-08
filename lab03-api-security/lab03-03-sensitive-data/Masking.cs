// Pure masking functions — no I/O, no state, trivially unit-testable.
using System.Text;

public static class Masking
{
    /// <summary>"4111 1111 1111 1234" -> "****1234" (last 4 digits only).</summary>
    public static string MaskCard(string cardNumber)
    {
        var digits = new string(cardNumber.Where(char.IsDigit).ToArray());
        if (digits.Length < 4)
            return "****";
        return "****" + digits[^4..];
    }

    /// <summary>"somchai.jaidee@example.com" -> "s***@example.com" (first char + domain).</summary>
    public static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 0)
            return "***";
        return email[0] + "***" + email[at..];
    }

    /// <summary>
    /// "081-234-5678" -> "08x-xxx-5678": keep the first 2 and last 4 digits,
    /// mask the middle, preserve separators.
    /// </summary>
    public static string MaskPhone(string phone)
    {
        var totalDigits = phone.Count(char.IsDigit);
        if (totalDigits <= 6)
            return new string('x', phone.Length);

        var sb = new StringBuilder(phone.Length);
        var seen = 0;
        foreach (var c in phone)
        {
            if (char.IsDigit(c))
            {
                sb.Append(seen < 2 || seen >= totalDigits - 4 ? c : 'x');
                seen++;
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}
