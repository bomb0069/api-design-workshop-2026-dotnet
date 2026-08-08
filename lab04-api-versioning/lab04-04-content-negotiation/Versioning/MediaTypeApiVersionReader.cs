using Asp.Versioning;
using System.Text.RegularExpressions;

public partial class MediaTypeApiVersionReader : IApiVersionReader
{
    [GeneratedRegex(@"vnd\.workshop\.v(\d+)\+json")]
    private static partial Regex VendorMediaTypeRegex();

    public IReadOnlyList<string> Read(HttpRequest request)
    {
        var accept = request.Headers.Accept.ToString();
        var match = VendorMediaTypeRegex().Match(accept);
        if (match.Success)
        {
            return new[] { match.Groups[1].Value + ".0" };
        }
        return Array.Empty<string>();
    }

    public void AddParameters(IApiVersionParameterDescriptionContext context)
    {
        context.AddParameter("Accept", ApiVersionParameterLocation.Header);
    }
}
