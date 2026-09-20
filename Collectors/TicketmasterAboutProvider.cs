using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace DublinEventsCollector.Collectors;

public sealed partial class TicketmasterAboutProvider(IHttpClientFactory httpClientFactory)
{
    private readonly ConcurrentDictionary<string, Task<string?>> cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim requestLimit = new(4);

    public async Task<string?> GetAboutAsync(string? attractionUrl, CancellationToken cancellationToken)
    {
        if (!TryGetTicketmasterIrelandUri(attractionUrl, out var uri))
        {
            return null;
        }

        var task = cache.GetOrAdd(uri.AbsoluteUri, _ => FetchAboutAsync(uri));
        try
        {
            return await task.WaitAsync(cancellationToken);
        }
        catch when (task.IsFaulted || task.IsCanceled)
        {
            cache.TryRemove(uri.AbsoluteUri, out _);
            return null;
        }
    }

    internal static string? ParseAbout(string html)
    {
        var match = AboutParagraphsRegex().Match(html);
        return match.Success ? VenueHtmlParsing.Blurb(match.Groups["value"].Value) : null;
    }

    private async Task<string?> FetchAboutAsync(Uri uri)
    {
        await requestLimit.WaitAsync();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var client = httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (compatible; DublinEventsCollector/1.0)");
            request.Headers.AcceptLanguage.ParseAdd("en-IE,en;q=0.9");

            using var response = await client.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var html = await response.Content.ReadAsStringAsync(timeout.Token);
            return ParseAbout(html);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        finally
        {
            requestLimit.Release();
        }
    }

    private static bool TryGetTicketmasterIrelandUri(string? value, out Uri uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var candidate)
            && candidate.Scheme == Uri.UriSchemeHttps)
        {
            var isIrelandHost = candidate.Host.Equals("ticketmaster.ie", StringComparison.OrdinalIgnoreCase)
                || candidate.Host.EndsWith(".ticketmaster.ie", StringComparison.OrdinalIgnoreCase);
            var isDiscoveryHost = candidate.Host.Equals("ticketmaster.com", StringComparison.OrdinalIgnoreCase)
                || candidate.Host.Equals("www.ticketmaster.com", StringComparison.OrdinalIgnoreCase);

            if (isIrelandHost || isDiscoveryHost)
            {
                uri = new UriBuilder(candidate)
                {
                    Host = "www.ticketmaster.ie",
                    Port = -1,
                    Fragment = string.Empty
                }.Uri;
                return true;
            }
        }

        uri = null!;
        return false;
    }

    [GeneratedRegex("<h2\\b[^>]*>\\s*About\\s*</h2>.*?(?<value>(?:<p\\b[^>]*>.*?</p>\\s*)+)", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex AboutParagraphsRegex();
}
