# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Reads a shortcut's AppUserModel properties READ-ONLY (GPS_DEFAULT) and its target.
param([Parameter(Mandatory)][string]$Path)
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class LnkProps {
  [StructLayout(LayoutKind.Sequential, Pack=4)] public struct PROPERTYKEY { public Guid fmtid; public uint pid; }
  [StructLayout(LayoutKind.Explicit, Size=24)] public struct PROPVARIANT { [FieldOffset(0)] public ushort vt; [FieldOffset(8)] public IntPtr p; [FieldOffset(8)] public Guid dummyGuidNotUsed; }
  [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  public interface IPropertyStore {
    [PreserveSig] int GetCount(out uint c);
    [PreserveSig] int GetAt(uint i, out PROPERTYKEY k);
    [PreserveSig] int GetValue(ref PROPERTYKEY k, out PROPVARIANT v);
    [PreserveSig] int SetValue(ref PROPERTYKEY k, ref PROPVARIANT v);
    [PreserveSig] int Commit();
  }
  [DllImport("shell32.dll", CharSet=CharSet.Unicode)] public static extern int SHGetPropertyStoreFromParsingName(string path, IntPtr bc, int flags, ref Guid iid, out IPropertyStore store);
  [DllImport("ole32.dll")] public static extern int PropVariantClear(ref PROPVARIANT v);
  public static string Read(string path, uint pid) {
    Guid iid = new Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
    IPropertyStore s; int hr = SHGetPropertyStoreFromParsingName(path, IntPtr.Zero, 0 /*GPS_DEFAULT: read-only*/, ref iid, out s);
    if (hr != 0) return "open hr=0x" + hr.ToString("X8");
    var k = new PROPERTYKEY { fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), pid = pid };
    PROPVARIANT v; hr = s.GetValue(ref k, out v);
    if (hr != 0) return "get hr=0x" + hr.ToString("X8");
    string r;
    if (v.vt == 0) r = "VT_EMPTY (not set)";
    else if (v.vt == 31) r = "VT_LPWSTR '" + Marshal.PtrToStringUni(v.p) + "'";
    else if (v.vt == 72) r = "VT_CLSID {" + ((Guid)Marshal.PtrToStructure(v.p, typeof(Guid))).ToString().ToUpperInvariant() + "}";
    else if (v.vt == 11) r = "VT_BOOL " + (v.p.ToInt64() & 0xFFFF);
    else if (v.vt == 19) r = "VT_UI4 " + (v.p.ToInt64() & 0xFFFFFFFF);
    else r = "vt=" + v.vt;
    PropVariantClear(ref v); Marshal.ReleaseComObject(s);
    return r;
  }
  public static string[] Keys(string path) {
    Guid iid = new Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
    IPropertyStore s; int hr = SHGetPropertyStoreFromParsingName(path, IntPtr.Zero, 0, ref iid, out s);
    if (hr != 0) return new[]{"open hr=0x" + hr.ToString("X8")};
    uint c; s.GetCount(out c); var list = new System.Collections.Generic.List<string>();
    for (uint i = 0; i < c; i++) { PROPERTYKEY k; s.GetAt(i, out k); list.Add("{" + k.fmtid.ToString().ToUpperInvariant() + "} " + k.pid); }
    Marshal.ReleaseComObject(s); return list.ToArray();
  }
}
"@
"file: $Path  ($((Get-Item $Path).Length) bytes, modified $((Get-Item $Path).LastWriteTime.ToString('o')))"
"System.AppUserModel.ID                  (pid 5):  " + [LnkProps]::Read($Path, 5)
"System.AppUserModel.ToastActivatorCLSID (pid 26): " + [LnkProps]::Read($Path, 26)
"System.AppUserModel.RelaunchCommand     (pid 2):  " + [LnkProps]::Read($Path, 2)
"System.AppUserModel.PreventPinning      (pid 9):  " + [LnkProps]::Read($Path, 9)
"all keys in the store:"; [LnkProps]::Keys($Path) | ForEach-Object { "  $_" }
$sh = New-Object -ComObject WScript.Shell
$l = $sh.CreateShortcut($Path)
"target: $($l.TargetPath)"; "args: '$($l.Arguments)'"; "workdir: $($l.WorkingDirectory)"; "icon: $($l.IconLocation)"
