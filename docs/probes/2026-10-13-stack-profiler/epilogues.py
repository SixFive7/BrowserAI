# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

import struct, sys
data = open(sys.argv[1], 'rb').read()
pe = struct.unpack_from('<I', data, 0x3c)[0]
nsec = struct.unpack_from('<H', data, pe + 6)[0]
optsize = struct.unpack_from('<H', data, pe + 20)[0]
sec = pe + 24 + optsize
text = None
for i in range(nsec):
    name = data[sec + i*40: sec + i*40 + 8].rstrip(b'\0')
    vsize, va, rawsize, rawptr = struct.unpack_from('<IIII', data, sec + i*40 + 8)
    if name == b'.text':
        text = (va, rawptr, rawsize)
va, ptr, size = text
body = data[ptr: ptr + size]
def count(pattern):
    n, start = 0, 0
    while True:
        k = body.find(pattern, start)
        if k < 0: return n
        n += 1; start = k + 1
# pop rbp; vzeroupper; ret
print('pop rbp; vzeroupper; ret          ', count(bytes.fromhex('5dc5f877c3')))
# pop r15; vzeroupper; ret (no frame pointer)
print('pop r15; vzeroupper; ret          ', count(bytes.fromhex('415fc5f877c3')))
# any pop reg; vzeroupper; ret
n = 0
for reg in range(0x58, 0x60):
    n += count(bytes([reg]) + bytes.fromhex('c5f877c3'))
    n += count(bytes([0x41, reg]) + bytes.fromhex('c5f877c3'))
print('any pop; vzeroupper; ret          ', n)
# the canonical order: vzeroupper before the pops: vzeroupper; pop rbp... rare to count exactly; count vzeroupper; ret overall
print('vzeroupper; ret (all)             ', count(bytes.fromhex('c5f877c3')))
print('pop rbp; ret                      ', count(bytes.fromhex('5dc3')))
print('text section size', size)
# the crash site check
rva = 0x557d48b
off = ptr + (rva - va)
print('bytes at RVA 0x557d48b:', data[off:off+5].hex())
