# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

"""Prints the description of every thread of one process, named by its pid.

Observation only: it opens each thread for limited query and closes it again.
Usage: python -I threadnames_live.py <pid>
"""
import ctypes
import ctypes.wintypes as wt
import json
import sys

TH32CS_SNAPTHREAD = 0x00000004
THREAD_QUERY_LIMITED_INFORMATION = 0x0800
INVALID_HANDLE_VALUE = ctypes.c_void_p(-1).value


class THREADENTRY32(ctypes.Structure):
    _fields_ = [
        ("dwSize", wt.DWORD),
        ("cntUsage", wt.DWORD),
        ("th32ThreadID", wt.DWORD),
        ("th32OwnerProcessID", wt.DWORD),
        ("tpBasePri", wt.LONG),
        ("tpDeltaPri", wt.LONG),
        ("dwFlags", wt.DWORD),
    ]


kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
kernel32.CreateToolhelp32Snapshot.restype = wt.HANDLE
kernel32.CreateToolhelp32Snapshot.argtypes = [wt.DWORD, wt.DWORD]
kernel32.Thread32First.argtypes = [wt.HANDLE, ctypes.POINTER(THREADENTRY32)]
kernel32.Thread32Next.argtypes = [wt.HANDLE, ctypes.POINTER(THREADENTRY32)]
kernel32.OpenThread.restype = wt.HANDLE
kernel32.OpenThread.argtypes = [wt.DWORD, wt.BOOL, wt.DWORD]
kernel32.GetThreadDescription.restype = ctypes.c_long
kernel32.GetThreadDescription.argtypes = [wt.HANDLE, ctypes.POINTER(ctypes.c_wchar_p)]
kernel32.LocalFree.argtypes = [ctypes.c_void_p]
kernel32.CloseHandle.argtypes = [wt.HANDLE]

pid = int(sys.argv[1])
snapshot = kernel32.CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0)
if snapshot == INVALID_HANDLE_VALUE or snapshot is None:
    print(json.dumps({"pid": pid, "error": "snapshot failed %d" % ctypes.get_last_error()}))
    sys.exit(1)

names = []
entry = THREADENTRY32()
entry.dwSize = ctypes.sizeof(THREADENTRY32)
ok = kernel32.Thread32First(snapshot, ctypes.byref(entry))
while ok:
    if entry.th32OwnerProcessID == pid:
        handle = kernel32.OpenThread(THREAD_QUERY_LIMITED_INFORMATION, False, entry.th32ThreadID)
        if handle:
            text = ctypes.c_wchar_p()
            if kernel32.GetThreadDescription(handle, ctypes.byref(text)) >= 0:
                names.append(text.value or "")
                kernel32.LocalFree(ctypes.cast(text, ctypes.c_void_p))
            kernel32.CloseHandle(handle)
    ok = kernel32.Thread32Next(snapshot, ctypes.byref(entry))
kernel32.CloseHandle(snapshot)

print(json.dumps({"pid": pid, "threads": len(names), "names": sorted(set(n for n in names if n))}))
