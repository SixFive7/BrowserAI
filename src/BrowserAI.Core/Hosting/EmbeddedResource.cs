// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Reflection;

namespace BrowserAI.Hosting;

/// <summary>A resource the build compiled into the binary, as bytes.</summary>
/// <remarks>
/// <para>
/// <b>Copied out of the image's memory, never read through a stream</b>, since 2026-10-11:
/// the maintainer's decisions 54 and 63 ban every synchronous stream read, and a
/// resource compiled into the binary is not a file. The runtime hands it out as an
/// <see cref="UnmanagedMemoryStream"/> over the image the loader mapped, so its bytes are
/// one copy of memory the process already has. <i>Previously each reader copied the
/// stream into a <see cref="MemoryStream"/> with <c>Stream.CopyTo</c>.</i>
/// </para>
/// <para>
/// <b>A resource that is not in memory is refused</b>, by name: this binary embeds its
/// resources, and a stream of another kind would be a build that does not.
/// </para>
/// </remarks>
internal static class EmbeddedResource
{
    /// <summary>A resource's bytes, or <see langword="null"/> when the binary carries no such resource.</summary>
    /// <param name="assembly">The assembly that carries it.</param>
    /// <param name="name">The resource's name, which the project file gives it.</param>
    /// <returns>The bytes.</returns>
    /// <exception cref="InvalidOperationException">The resource is there and is not in the image's memory.</exception>
    public static unsafe byte[]? BytesOf(Assembly assembly, string name)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        using var stream = assembly.GetManifestResourceStream(name);

        return stream switch
        {
            null => null,
            UnmanagedMemoryStream memory => new ReadOnlySpan<byte>(memory.PositionPointer, checked((int)(memory.Length - memory.Position))).ToArray(),
            _ => throw new InvalidOperationException($"The resource '{name}' is not in this binary's memory, so it cannot be copied out of it."),
        };
    }
}
