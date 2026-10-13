# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

"""Prints one JSON line per Crashpad minidump: process type, pid, exception code,
the faulting address as module+offset, the faulting thread's name, and the read
address of an access violation.

Usage: python -I dumpsig.py <dump> [<dump> ...]
"""
import json
import struct
import sys


def signature(path):
    data = open(path, "rb").read()
    _sig, _ver, count, directory = struct.unpack_from("<IIII", data, 0)
    streams = {}
    for i in range(count):
        kind, size, rva = struct.unpack_from("<III", data, directory + i * 12)
        streams[kind] = (size, rva)

    def utf16(rva):
        (length,) = struct.unpack_from("<I", data, rva)
        return data[rva + 4: rva + 4 + length].decode("utf-16-le", "replace")

    def utf8(rva):
        (length,) = struct.unpack_from("<I", data, rva)
        return data[rva + 4: rva + 4 + length].decode("utf-8", "replace")

    modules = []
    if 4 in streams:
        _size, rva = streams[4]
        (n,) = struct.unpack_from("<I", data, rva)
        for i in range(n):
            off = rva + 4 + i * 108
            base, size, _cks, _tds, name_rva = struct.unpack_from("<QIIII", data, off)
            modules.append((base, size, utf16(name_rva).split("\\")[-1]))

    def where(address):
        for base, size, name in modules:
            if base <= address < base + size:
                return "%s+0x%x" % (name, address - base)
        return "0x%x" % address

    names = {}
    if 0x18 in streams:
        _size, rva = streams[0x18]
        (n,) = struct.unpack_from("<I", data, rva)
        for i in range(n):
            tid, name_rva = struct.unpack_from("<IQ", data, rva + 4 + i * 12)
            names[tid] = utf16(name_rva)

    out = {"dump": path.replace("\\", "/").split("/")[-1]}
    if 6 in streams:
        _size, rva = streams[6]
        tid, _align = struct.unpack_from("<II", data, rva)
        code, _flags, _rec, address, nparams, _u = struct.unpack_from("<IIQQII", data, rva + 8)
        params = struct.unpack_from("<15Q", data, rva + 8 + 32)[:nparams]
        out.update({
            "code": "0x%08X" % code,
            "at": where(address),
            "thread": names.get(tid, ""),
            "params": ["0x%x" % p for p in params],
        })

    crashpad = 0x43500001
    if crashpad in streams:
        _size, rva = streams[crashpad]
        ml_size, ml_rva = struct.unpack_from("<II", data, rva + 44)
        if ml_rva:
            (n,) = struct.unpack_from("<I", data, ml_rva)
            for i in range(n):
                _idx, _l_size, l_rva = struct.unpack_from("<III", data, ml_rva + 4 + i * 12)
                _mver, _la_size, _la_rva, _sa_size, _sa_rva, _ao_size, ao_rva = struct.unpack_from("<IIIIIII", data, l_rva)
                if not ao_rva:
                    continue
                (c,) = struct.unpack_from("<I", data, ao_rva)
                for j in range(c):
                    n_rva, kind, _r, v_rva = struct.unpack_from("<IHHI", data, ao_rva + 4 + j * 12)
                    key = utf8(n_rva)
                    if key in ("ptype", "pid") and kind == 1:
                        out[key] = utf8(v_rva)
    return out


for argument in sys.argv[1:]:
    try:
        print(json.dumps(signature(argument)))
    except Exception as failure:  # one unreadable dump must not hide the others
        print(json.dumps({"dump": argument, "error": repr(failure)}))
