# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Reads RT_GROUP_ICON id 32512 out of a built binary and prints its length and
# SHA-256. The same four resource calls EmbeddedManifest uses, and for the same
# reason: LOAD_LIBRARY_AS_IMAGE_RESOURCE maps the file for its resources alone.
[CmdletBinding()]
param([Parameter(Mandatory)] [string[]] $Path)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class GroupIconReader
{
    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibraryExW(string file, IntPtr reserved, uint flags);

    [DllImport("kernel32", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr module);

    [DllImport("kernel32", SetLastError = true)]
    private static extern IntPtr FindResourceW(IntPtr module, IntPtr name, IntPtr type);

    [DllImport("kernel32", SetLastError = true)]
    private static extern IntPtr LoadResource(IntPtr module, IntPtr found);

    [DllImport("kernel32")]
    private static extern IntPtr LockResource(IntPtr loaded);

    [DllImport("kernel32", SetLastError = true)]
    private static extern uint SizeofResource(IntPtr module, IntPtr found);

    public static byte[] Read(string path, int type, int id)
    {
        IntPtr module = LoadLibraryExW(path, IntPtr.Zero, 0x20u | 0x40u);
        if (module == IntPtr.Zero) { throw new InvalidOperationException("could not map " + path); }
        try
        {
            IntPtr found = FindResourceW(module, new IntPtr(id), new IntPtr(type));
            if (found == IntPtr.Zero) { return null; }
            uint size = SizeofResource(module, found);
            IntPtr loaded = LoadResource(module, found);
            IntPtr bytes = LockResource(loaded);
            byte[] copy = new byte[size];
            Marshal.Copy(bytes, copy, 0, (int)size);
            return copy;
        }
        finally { FreeLibrary(module); }
    }
}
'@

foreach ($file in $Path) {
    $full = [System.IO.Path]::GetFullPath($file)

    if (-not (Test-Path -LiteralPath $full)) {
        "{0,-70} ABSENT" -f $file
        continue
    }

    # RT_GROUP_ICON is 14; 32512 is IDI_APPLICATION, the id a single
    # ApplicationIcon is written under.
    $group = [GroupIconReader]::Read($full, 14, 32512)

    if ($null -eq $group) {
        "{0,-70} no RT_GROUP_ICON 32512" -f $file
        continue
    }

    $hash = [System.Security.Cryptography.SHA256]::HashData($group)
    $hex = [System.Convert]::ToHexString($hash)

    # The group's own directory: a 6-byte header then 14 bytes per image.
    $count = [BitConverter]::ToUInt16($group, 4)
    $sizes = @()
    for ($i = 0; $i -lt $count; $i++) {
        $at = 6 + (14 * $i)
        $w = if ($group[$at] -eq 0) { 256 } else { $group[$at] }
        $bytes = [BitConverter]::ToUInt32($group, $at + 8)
        $ordinal = [BitConverter]::ToUInt16($group, $at + 12)
        $sizes += ("{0}({1}b,#{2})" -f $w, $bytes, $ordinal)
    }

    "{0,-70} {1} bytes  {2} images [{3}]  sha256 {4}" -f $file, $group.Length, $count, ($sizes -join ' '), $hex
}
