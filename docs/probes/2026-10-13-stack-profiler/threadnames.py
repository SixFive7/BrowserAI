# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

import struct, sys
data = open(sys.argv[1], 'rb').read()
sig, ver, n, dir_rva = struct.unpack_from('<IIII', data, 0)
streams = {}
for i in range(n):
    t, s, r = struct.unpack_from('<III', data, dir_rva + i * 12)
    streams[t] = (s, r)
s, r = streams[0x18]
(count,) = struct.unpack_from('<I', data, r)
for i in range(count):
    tid, name_rva = struct.unpack_from('<IQ', data, r + 4 + i * 12)
    (ln,) = struct.unpack_from('<I', data, name_rva)
    print(tid, hex(tid), data[name_rva + 4: name_rva + 4 + ln].decode('utf-16-le', 'replace'))
# thread list
s, r = streams[3]
(count,) = struct.unpack_from('<I', data, r)
print('threads', count)
for i in range(count):
    off = r + 4 + i * 48
    tid, suspend, pclass, prio, teb, stk_start, stk_size, stk_rva, ctx_size, ctx_rva = struct.unpack_from('<IIIIQQIIII', data, off)
    ctx = data[ctx_rva: ctx_rva + ctx_size]
    rsp, = struct.unpack_from('<Q', ctx, 0x98)
    rip, = struct.unpack_from('<Q', ctx, 0xF8)
    print('tid %6d suspend %d prio %d stack %x..%x (%d bytes) rsp %x rip %x' % (tid, suspend, prio, stk_start, stk_start + stk_size, stk_size, rsp, rip))
