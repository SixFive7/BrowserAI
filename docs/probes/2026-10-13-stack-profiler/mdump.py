# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

import struct, sys, datetime

path = sys.argv[1]
data = open(path, 'rb').read()

sig, ver, nstreams, dir_rva, checksum, timestamp, flags = struct.unpack_from('<IIIIIIQ', data, 0)
print('signature %08x version %08x streams %d dir_rva %d time %s flags %x' % (sig, ver, nstreams, dir_rva, datetime.datetime.fromtimestamp(timestamp, datetime.UTC), flags))

streams = {}
for i in range(nstreams):
    stype, size, rva = struct.unpack_from('<III', data, dir_rva + i * 12)
    streams.setdefault(stype, []).append((size, rva))
    print('stream type 0x%08x size %d rva %d' % (stype, size, rva))

def read_string(rva):
    if rva == 0:
        return None
    (length,) = struct.unpack_from('<I', data, rva)
    return data[rva + 4: rva + 4 + length].decode('utf-16-le', 'replace')

modules = []
if 4 in streams:
    size, rva = streams[4][0]
    (n,) = struct.unpack_from('<I', data, rva)
    for i in range(n):
        off = rva + 4 + i * 108
        base, msize, cks, tds, name_rva = struct.unpack_from('<QIIII', data, off)
        # VS_FIXEDFILEINFO at off+24, 52 bytes
        vs = struct.unpack_from('<13I', data, off + 24)
        fv_ms, fv_ls = vs[2], vs[3]
        fver = '%d.%d.%d.%d' % (fv_ms >> 16, fv_ms & 0xffff, fv_ls >> 16, fv_ls & 0xffff)
        cv_size, cv_rva = struct.unpack_from('<II', data, off + 76)
        pdb = None
        if cv_rva and cv_size >= 24:
            if data[cv_rva:cv_rva + 4] == b'RSDS':
                guid = data[cv_rva + 4: cv_rva + 20]
                (age,) = struct.unpack_from('<I', data, cv_rva + 20)
                pdbname = data[cv_rva + 24: cv_rva + cv_size].split(b'\0')[0].decode('utf-8', 'replace')
                g = struct.unpack_from('<IHH8B', guid, 0)
                gs = '%08X%04X%04X' % g[:3] + ''.join('%02X' % b for b in g[3:])
                pdb = (pdbname, gs, age)
        modules.append((base, msize, read_string(name_rva), fver, tds, pdb))

def module_of(addr):
    for base, msize, name, fver, tds, pdb in modules:
        if base <= addr < base + msize:
            return name, addr - base
    return None, None

if 6 in streams:
    size, rva = streams[6][0]
    tid, _align = struct.unpack_from('<II', data, rva)
    code, eflags, rec, addr, nparams, _u = struct.unpack_from('<IIQQII', data, rva + 8)
    params = struct.unpack_from('<15Q', data, rva + 8 + 32)
    ctx_size, ctx_rva = struct.unpack_from('<II', data, rva + 8 + 32 + 120)
    print('EXCEPTION thread %d code 0x%08x flags %x address 0x%x params %s' % (tid, code, eflags, addr, [hex(p) for p in params[:nparams]]))
    name, off = module_of(addr)
    print('  in module %s +0x%x' % (name, off if off is not None else -1))
    for p in params[:nparams]:
        n2, o2 = module_of(p)
        if n2:
            print('  param 0x%x is in %s +0x%x' % (p, n2, o2))
    # CONTEXT for amd64: Rip at offset 0xF8, Rsp at 0x98
    if ctx_size >= 0x100:
        ctx = data[ctx_rva: ctx_rva + ctx_size]
        regs = {}
        names = ['Rax', 'Rcx', 'Rdx', 'Rbx', 'Rsp', 'Rbp', 'Rsi', 'Rdi', 'R8', 'R9', 'R10', 'R11', 'R12', 'R13', 'R14', 'R15', 'Rip']
        for i, nm in enumerate(names):
            (regs[nm],) = struct.unpack_from('<Q', ctx, 0x78 + i * 8)
        print('  context: ' + ' '.join('%s=%016x' % (k, v) for k, v in regs.items()))
        print('EXC_TID', tid, 'RSP', hex(regs['Rsp']), 'RIP', hex(regs['Rip']))

if 15 in streams:  # MiscInfoStream
    size, rva = streams[15][0]
    sizeofinfo, flags1, pid, ctime, utime, ktime = struct.unpack_from('<IIIIII', data, rva)
    print('MISC pid %d process create time %s user %ds kernel %ds' % (pid, datetime.datetime.fromtimestamp(ctime, datetime.UTC), utime, ktime))

print('MODULES:')
for base, msize, name, fver, tds, pdb in modules:
    print('  %016x %08x %s %s %s' % (base, msize, fver, name, pdb if pdb else ''))

# Crashpad info stream
CRASHPAD = 0x43500001
def read_utf8(rva):
    (length,) = struct.unpack_from('<I', data, rva)
    return data[rva + 4: rva + 4 + length].decode('utf-8', 'replace')

def simple_dict(loc_size, loc_rva):
    out = {}
    if loc_rva == 0:
        return out
    (count,) = struct.unpack_from('<I', data, loc_rva)
    for i in range(count):
        k_rva, v_rva = struct.unpack_from('<II', data, loc_rva + 4 + i * 8)
        out[read_utf8(k_rva)] = read_utf8(v_rva)
    return out

if CRASHPAD in streams:
    size, rva = streams[CRASHPAD][0]
    version = struct.unpack_from('<I', data, rva)[0]
    report_id = data[rva + 4: rva + 20]
    client_id = data[rva + 20: rva + 36]
    sd_size, sd_rva = struct.unpack_from('<II', data, rva + 36)
    ml_size, ml_rva = struct.unpack_from('<II', data, rva + 44)
    print('CRASHPAD version', version)
    for k, v in simple_dict(sd_size, sd_rva).items():
        print('  simple %s = %s' % (k, v))
    if ml_rva:
        (count,) = struct.unpack_from('<I', data, ml_rva)
        for i in range(count):
            # MinidumpModuleCrashpadInfoLink: minidump_module_list_index (uint32), location (8)
            idx, l_size, l_rva = struct.unpack_from('<III', data, ml_rva + 4 + i * 12)
            # MinidumpModuleCrashpadInfo: version, list_annotations loc, simple_annotations loc, annotation_objects loc
            mver, la_size, la_rva, sa_size, sa_rva, ao_size, ao_rva = struct.unpack_from('<IIIIIII', data, l_rva)
            mname = modules[idx][2] if idx < len(modules) else '?'
            if la_rva:
                (c,) = struct.unpack_from('<I', data, la_rva)
                for j in range(c):
                    (srva,) = struct.unpack_from('<I', data, la_rva + 4 + j * 4)
                    print('  [%s] list: %s' % (mname, read_utf8(srva)))
            for k, v in simple_dict(sa_size, sa_rva).items():
                print('  [%s] simple %s = %s' % (mname, k, v))
            if ao_rva:
                (c,) = struct.unpack_from('<I', data, ao_rva)
                for j in range(c):
                    n_rva, atype, _r, v_rva = struct.unpack_from('<IHHI', data, ao_rva + 4 + j * 12)
                    nm = read_utf8(n_rva)
                    (vlen,) = struct.unpack_from('<I', data, v_rva)
                    raw = data[v_rva + 4: v_rva + 4 + vlen]
                    if atype == 1:
                        val = raw.decode('utf-8', 'replace')
                    else:
                        val = raw[:200].hex()
                    print('  [%s] object %s (type %d) = %s' % (mname, nm, atype, val[:2000]))
