"""For each minidump: whether a thread named StackSamplingProfiler exists, and
the chrome.dll version. Usage: python -I dumpthreads.py <dump> [...]"""
import struct
import sys

for path in sys.argv[1:]:
    data = open(path, "rb").read()
    _sig, _ver, count, directory = struct.unpack_from("<IIII", data, 0)
    streams = {}
    for i in range(count):
        kind, size, rva = struct.unpack_from("<III", data, directory + i * 12)
        streams[kind] = (size, rva)

    def utf16(rva):
        (length,) = struct.unpack_from("<I", data, rva)
        return data[rva + 4: rva + 4 + length].decode("utf-16-le", "replace")

    names = []
    if 0x18 in streams:
        _size, rva = streams[0x18]
        (n,) = struct.unpack_from("<I", data, rva)
        for i in range(n):
            _tid, name_rva = struct.unpack_from("<IQ", data, rva + 4 + i * 12)
            names.append(utf16(name_rva))
    version = "?"
    if 4 in streams:
        _size, rva = streams[4]
        (n,) = struct.unpack_from("<I", data, rva)
        for i in range(n):
            off = rva + 4 + i * 108
            _base, _msize, _cks, _tds, name_rva = struct.unpack_from("<QIIII", data, off)
            if utf16(name_rva).lower().endswith("\\chrome.dll"):
                vs = struct.unpack_from("<13I", data, off + 24)
                version = "%d.%d.%d.%d" % (vs[2] >> 16, vs[2] & 0xFFFF, vs[3] >> 16, vs[3] & 0xFFFF)
    print("%s chrome.dll %s threads %d StackSamplingProfiler %s" % (
        path.replace("\\", "/").split("/")[-1], version, len(names), "StackSamplingProfiler" in names))
