// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System;
using System.Runtime.InteropServices;
public static class LnkWriter {
  [StructLayout(LayoutKind.Sequential, Pack=4)] public struct PROPERTYKEY { public Guid fmtid; public uint pid; }
  [StructLayout(LayoutKind.Explicit, Size=24)] public struct PROPVARIANT { [FieldOffset(0)] public ushort vt; [FieldOffset(8)] public IntPtr p; }
  [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  public interface IPropertyStore {
    [PreserveSig] int GetCount(out uint c);
    [PreserveSig] int GetAt(uint i, out PROPERTYKEY k);
    [PreserveSig] int GetValue(ref PROPERTYKEY k, out PROPVARIANT v);
    [PreserveSig] int SetValue(ref PROPERTYKEY k, ref PROPVARIANT v);
    [PreserveSig] int Commit();
  }
  [DllImport("shell32.dll", CharSet=CharSet.Unicode)] static extern int SHGetPropertyStoreFromParsingName(string path, IntPtr bc, int flags, ref Guid iid, out IPropertyStore store);
  [DllImport("ole32.dll")] static extern int PropVariantClear(ref PROPVARIANT v);
  static readonly Guid AppUserModel = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");
  // GPS_READWRITE = 2
  public static string Set(string path, string aumid, string activatorClsid) {
    Guid iid = new Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
    IPropertyStore s; int hr = SHGetPropertyStoreFromParsingName(path, IntPtr.Zero, 2, ref iid, out s);
    if (hr != 0) return "open hr=0x" + hr.ToString("X8");
    var result = "";
    if (!string.IsNullOrEmpty(aumid)) {
      var k = new PROPERTYKEY { fmtid = AppUserModel, pid = 5 };
      var v = new PROPVARIANT { vt = 31, p = Marshal.StringToCoTaskMemUni(aumid) };
      hr = s.SetValue(ref k, ref v); PropVariantClear(ref v); result += " setAumid=0x" + hr.ToString("X8");
    }
    if (!string.IsNullOrEmpty(activatorClsid)) {
      var k = new PROPERTYKEY { fmtid = AppUserModel, pid = 26 };
      var g = new Guid(activatorClsid); var mem = Marshal.AllocCoTaskMem(16); Marshal.StructureToPtr(g, mem, false);
      var v = new PROPVARIANT { vt = 72, p = mem };
      hr = s.SetValue(ref k, ref v); PropVariantClear(ref v); result += " setActivator=0x" + hr.ToString("X8");
    }
    hr = s.Commit(); result += " commit=0x" + hr.ToString("X8");
    Marshal.ReleaseComObject(s);
    return result.Trim();
  }
}
