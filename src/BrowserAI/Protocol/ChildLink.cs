// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace BrowserAI.Protocol;

/// <summary>
/// A decorator over an <see cref="IClientTransport"/> that keeps hold of the
/// live transport the SDK client connects through.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> <c>McpClient.CreateAsync</c> calls
/// <see cref="IClientTransport.ConnectAsync"/> itself and keeps the resulting
/// <see cref="ITransport"/> private, so the proxy has no route to the one object
/// that can see the child's bytes. One decorator, and it has one.
/// </para>
/// <para>
/// ⚠️ <b>Corrected 2026-08-16 (previously
/// <see href="../../../plan/stack.md#nine-places-where-the-sdk-must-be-deviated-from">deviation
/// 7</see>: "observing and forwarding child→caller notifications needs an
/// <c>ITransport</c> decorator (~30 lines)").</b> A decorator is needed, and not
/// for that. Forwarding a <i>named</i> notification is public API —
/// <c>McpSession.RegisterNotificationHandler</c>, which <c>McpClient</c>
/// inherits — so the progress relay needs no decorator at all. What has no
/// public route is the live transport instance, which byte-identical
/// passthrough needs because the raw bytes exist nowhere else. The deviation's
/// underlying claim, that <c>McpClientOptions</c> has no <c>Filters</c>, is
/// unchanged and still true; only *wildcard* observation needs the
/// <see cref="ITransport"/> shape it describes.
/// </para>
/// <para>
/// It <b>refuses</b> a transport it cannot see through rather than degrading.
/// Silently falling back to a re-serialised result would be a proxy that claims
/// byte-identity and does not deliver it, with every signal green — which is the
/// failure class this project exists to eliminate, produced by the code written
/// to remove it.
/// </para>
/// </remarks>
/// <param name="inner">The transport that actually starts and speaks to the child.</param>
internal sealed class ChildLink(IClientTransport inner) : IClientTransport
{
    /// <inheritdoc />
    public string Name => inner.Name;

    /// <summary>The live transport, once the client has connected through it.</summary>
    /// <exception cref="InvalidOperationException">Nothing has connected yet.</exception>
    public JsonLinesTransport Session =>
        Connected ?? throw new InvalidOperationException("The child transport has not been connected yet.");

    private JsonLinesTransport? Connected { get; set; }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">The inner transport is not one whose bytes the proxy can retain.</exception>
    public async Task<ITransport> ConnectAsync(CancellationToken cancellationToken = default)
    {
        var transport = await inner.ConnectAsync(cancellationToken).ConfigureAwait(false);

        if (transport is not JsonLinesTransport session)
        {
            await transport.DisposeAsync().ConfigureAwait(false);

            throw new InvalidOperationException(
                $"'{inner.Name}' connected a {transport.GetType().Name}, which BrowserAI cannot read raw frames from. Byte-identical passthrough needs a {nameof(JsonLinesTransport)}, and answering with a re-serialised result instead would be a silent loss.");
        }

        Connected = session;
        return transport;
    }
}
