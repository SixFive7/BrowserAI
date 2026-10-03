// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace BrowserAI.App.Page;

/// <summary>Why the gate turned a request away.</summary>
/// <remarks>
/// <b>Never sent to the caller.</b> Every refusal is the same bare <c>404</c>, so a
/// caller learns nothing about which check it failed; the reason goes to the log,
/// where the person who owns the machine can read it.
/// </remarks>
internal enum PageRefusal
{
    /// <summary>Admitted.</summary>
    None,

    /// <summary>Not a GET and not a POST.</summary>
    Method,

    /// <summary>Not exactly one <c>Host</c>, or not ours.</summary>
    Host,

    /// <summary>The path does not begin with the token, or the token is wrong.</summary>
    Token,

    /// <summary><c>Sec-Fetch-Site</c> says the request came from another site.</summary>
    FetchSite,

    /// <summary>A write without our <c>Origin</c>, or a read carrying somebody else's.</summary>
    Origin,

    /// <summary>A write whose body is not JSON.</summary>
    ContentType,

    /// <summary>A write whose body is too large, or has no declared length.</summary>
    TooLarge,
}

/// <summary>
/// What one request shows the gate, read off the wire and not yet trusted.
/// </summary>
/// <param name="Method">The request method, as sent.</param>
/// <param name="RawTarget">The request target exactly as it arrived, before any decoding.</param>
/// <param name="Host">Every <c>Host</c> value.</param>
/// <param name="Origin">Every <c>Origin</c> value.</param>
/// <param name="FetchSite">Every <c>Sec-Fetch-Site</c> value.</param>
/// <param name="ContentType">Every <c>Content-Type</c> value.</param>
/// <param name="ContentLength">The declared body length, or <see langword="null"/> when none was declared.</param>
/// <param name="Chunked">Whether the body is sent in chunks, which declares no length.</param>
internal sealed record PageRequest(
    string Method,
    string RawTarget,
    IReadOnlyList<string> Host,
    IReadOnlyList<string> Origin,
    IReadOnlyList<string> FetchSite,
    IReadOnlyList<string> ContentType,
    long? ContentLength,
    bool Chunked);

/// <summary>
/// The one gate every request passes before any route is looked at.
/// </summary>
/// <remarks>
/// <para>
/// <b>Q334 a and Q335 a, the maintainer's words verbatim: <i>"Q334 a"</i> and
/// <i>"Q335 a"</i>.</b> The page proves itself by its address:
/// <c>http://127.0.0.1:&lt;port&gt;/&lt;token&gt;/</c>, with 256 random bits made for
/// each listener and handed over only through the coordinator's pipe, whose DACL
/// admits the current user alone. That secret is the only one: there is no cookie,
/// because every port on <c>127.0.0.1</c> shares one cookie jar, and no check of
/// the connecting process's user, because a page in the person's own browser sends
/// its requests as that person and such a check cannot replace the secret.
/// </para>
/// <para>
/// <b>The rules are the prototype's, which was attacked on 2026-10-01</b>
/// ([kb](../../../kb/windows/loopback-page.md#a-listener-with-one-gate-against-two-browsers----measured-2026-10-01)):
/// GET or POST only; exactly one <c>Host</c>, equal to <c>127.0.0.1:&lt;port&gt;</c>,
/// which is what stops DNS rebinding even with the token leaked; the token compared
/// in constant time; <c>Sec-Fetch-Site</c>, when a browser sends it, same-origin or
/// none; and on a POST our exact <c>Origin</c>, a JSON content type and a declared
/// body of at most 64 KB. A GET that carries an <c>Origin</c> must carry ours.
/// </para>
/// <para>
/// <b>Pure, so that the table of rules is asserted without a socket</b>, and the
/// listener asks exactly this. The raw target is read before any decoding, so
/// <c>/x/../&lt;token&gt;/</c> or a percent-encoded token never reaches a route by a
/// spelling the comparison did not see.
/// </para>
/// </remarks>
internal sealed class PageGate
{
    /// <summary>The largest body a write may declare.</summary>
    public const int MaximumBody = 64 * 1024;

    /// <summary>How many random bytes a token carries: 256 bits.</summary>
    public const int TokenBytes = 32;

    /// <summary>What a token is spelled with: base64url, no padding.</summary>
    public const int TokenCharacters = 43;

    private readonly byte[] _token;

    /// <summary>A gate for one listener.</summary>
    /// <param name="port">The port the listener holds.</param>
    /// <param name="token">The listener's token.</param>
    public PageGate(int port, string token)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(port);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        Port = port;
        Token = token;
        _token = Encoding.ASCII.GetBytes(token);
        Host = string.Create(CultureInfo.InvariantCulture, $"127.0.0.1:{port}");
        Origin = "http://" + Host;
    }

    /// <summary>The port.</summary>
    public int Port { get; }

    /// <summary>The token, which is the secret in every address this gate admits.</summary>
    public string Token { get; }

    /// <summary>The one <c>Host</c> value admitted.</summary>
    public string Host { get; }

    /// <summary>The one <c>Origin</c> value admitted.</summary>
    public string Origin { get; }

    /// <summary>The address of the page's root, with its closing slash.</summary>
    public string Root => $"{Origin}/{Token}/";

    /// <summary>A new token: 256 random bits, base64url with no padding.</summary>
    /// <returns>The token.</returns>
    public static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(TokenBytes))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    /// <summary>Decides one request.</summary>
    /// <param name="request">What the request showed.</param>
    /// <returns><see cref="PageRefusal.None"/> when it is admitted, and otherwise why not.</returns>
    public PageRefusal Admit(PageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var post = string.Equals(request.Method, "POST", StringComparison.Ordinal);

        if (!post && !string.Equals(request.Method, "GET", StringComparison.Ordinal))
        {
            return PageRefusal.Method;
        }

        // Exactly one Host, exactly ours. A page that rebound a name of its own to
        // 127.0.0.1 sends that name here.
        if (Single(request.Host) is not { } host || !string.Equals(host, Host, StringComparison.Ordinal))
        {
            return PageRefusal.Host;
        }

        if (!CarriesTheToken(request.RawTarget))
        {
            return PageRefusal.Token;
        }

        // A browser says where a request came from. "none" is an address typed or
        // opened from outside the browser, which is how the tab is opened.
        var site = Single(request.FetchSite);

        if (request.FetchSite.Count > 1
            || (site is not null && site is not ("same-origin" or "none")))
        {
            return PageRefusal.FetchSite;
        }

        if (post)
        {
            if (Single(request.Origin) is not { } origin
                || !string.Equals(origin, Origin, StringComparison.Ordinal)
                || site is "none")
            {
                return PageRefusal.Origin;
            }

            if (Single(request.ContentType) is not "application/json")
            {
                return PageRefusal.ContentType;
            }

            if (request.Chunked || request.ContentLength is not { } length || length > MaximumBody)
            {
                return PageRefusal.TooLarge;
            }
        }
        else if (request.Origin.Count > 0
            && (Single(request.Origin) is not { } origin || !string.Equals(origin, Origin, StringComparison.Ordinal)))
        {
            return PageRefusal.Origin;
        }

        return PageRefusal.None;
    }

    /// <summary>
    /// The route an admitted target names: what follows <c>/&lt;token&gt;/</c>, up
    /// to the query.
    /// </summary>
    /// <param name="rawTarget">The raw target of a request the gate admitted.</param>
    /// <returns>The route, empty for the page itself.</returns>
    public string RouteOf(string rawTarget)
    {
        ArgumentNullException.ThrowIfNull(rawTarget);

        var rest = rawTarget[(_token.Length + 2)..];
        var query = rest.IndexOf('?', StringComparison.Ordinal);

        return query < 0 ? rest : rest[..query];
    }

    /// <summary>The query an admitted target carries, without its question mark.</summary>
    /// <param name="rawTarget">The raw target of a request the gate admitted.</param>
    /// <returns>The query, empty when there is none.</returns>
    public static string QueryOf(string rawTarget)
    {
        ArgumentNullException.ThrowIfNull(rawTarget);

        var query = rawTarget.IndexOf('?', StringComparison.Ordinal);

        return query < 0 ? string.Empty : rawTarget[(query + 1)..];
    }

    /// <summary>One value of a query, or <see langword="null"/>.</summary>
    /// <param name="query">The query, as <see cref="QueryOf"/> returned it.</param>
    /// <param name="name">The name.</param>
    /// <returns>The value, undecoded, or <see langword="null"/> when the name is absent.</returns>
    public static string? QueryValue(string query, string name)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        foreach (var pair in query.Split('&'))
        {
            var equals = pair.IndexOf('=', StringComparison.Ordinal);

            if (equals > 0 && string.Equals(pair[..equals], name, StringComparison.Ordinal))
            {
                return pair[(equals + 1)..];
            }
        }

        return null;
    }

    /// <summary>
    /// Whether a raw target is <c>/&lt;token&gt;/</c> and more, compared in
    /// constant time over the token.
    /// </summary>
    /// <param name="target">The raw target.</param>
    /// <returns>Whether it carries this gate's token.</returns>
    private bool CarriesTheToken(string target)
    {
        if (target.Length < _token.Length + 2 || target[0] != '/' || target[_token.Length + 1] != '/')
        {
            return false;
        }

        Span<byte> candidate = stackalloc byte[_token.Length];

        for (var index = 0; index < _token.Length; index++)
        {
            var character = target[index + 1];

            // A character outside ASCII is never part of a token; it is folded to a
            // byte no token carries, and the comparison below still runs whole.
            candidate[index] = character < 0x80 ? (byte)character : (byte)0;
        }

        return CryptographicOperations.FixedTimeEquals(candidate, _token);
    }

    /// <summary>The one value a header carries, or <see langword="null"/> when it carries none or several.</summary>
    /// <param name="values">Every value.</param>
    /// <returns>The value.</returns>
    private static string? Single(IReadOnlyList<string> values) => values.Count is 1 ? values[0] : null;
}
