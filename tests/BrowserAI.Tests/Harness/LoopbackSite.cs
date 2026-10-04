// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Net;
using System.Text;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// Named pages on a loopback port, for the arms whose subject is which pages a
/// browser reopens and what a page it reopens still holds.
/// </summary>
/// <remarks>
/// <para>
/// <b>Loopback and HTTP, never a <c>data:</c> URL</b>: the restore measured on
/// 2026-10-03 was measured against pages served this way, a URL that carries its
/// own content would leave a reader unable to tell a restored page from a
/// navigation, and a cookie needs an origin to belong to.
/// </para>
/// <para>
/// <b>Moved here from <c>SessionCloseTests</c> on 2026-10-04</b>, when the arms that
/// hold the ordering of a close and a reopen needed the same pages; nothing in it
/// changed.
/// </para>
/// </remarks>
internal sealed class LoopbackSite : IDisposable
{
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _stopping = new();
    private readonly string _root;

    private LoopbackSite(HttpListener listener, string root)
    {
        _listener = listener;
        _root = root;
    }

    /// <summary>Binds a loopback port and starts answering.</summary>
    /// <returns>The running site.</returns>
    public static LoopbackSite Start()
    {
        for (var port = 54_300; port < 54_400; port++)
        {
            var root = $"http://127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}/";
            var listener = new HttpListener();
            listener.Prefixes.Add(root);

            try
            {
                listener.Start();
            }
            catch (HttpListenerException)
            {
                listener.Close();
                continue;
            }

            var site = new LoopbackSite(listener, root);
            _ = Task.Run(site.ServeAsync, CancellationToken.None);

            return site;
        }

        throw new InvalidOperationException("no loopback port between 54300 and 54399 could be bound");
    }

    /// <summary>The address of one page.</summary>
    /// <param name="name">The page's name.</param>
    /// <returns>Its URL.</returns>
    public string Url(string name) => $"{_root}{name}";

    /// <inheritdoc />
    public void Dispose()
    {
        _stopping.Cancel();
        _listener.Close();
        _stopping.Dispose();
    }

    private async Task ServeAsync()
    {
        while (!_stopping.IsCancellationRequested)
        {
            HttpListenerContext context;

            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception failure) when (failure is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }

            var name = context.Request.Url?.AbsolutePath.Trim('/') ?? string.Empty;
            var body = Encoding.UTF8.GetBytes($"<!doctype html><title>{name}</title><h1>{name}</h1>");

            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = body.Length;
            await context.Response.OutputStream.WriteAsync(body);
            context.Response.Close();
        }
    }
}
