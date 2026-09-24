import sys, re
path = sys.argv[1]
needle = sys.argv[2].encode('utf-8')
before = int(sys.argv[3]) if len(sys.argv) > 3 else 300
after  = int(sys.argv[4]) if len(sys.argv) > 4 else 600
maxhits = int(sys.argv[5]) if len(sys.argv) > 5 else 10
sys.stdout.reconfigure(encoding='utf-8', errors='replace')
data = open(path,'rb').read()
hits = 0
start = 0
while True:
    i = data.find(needle, start)
    if i < 0: break
    hits += 1
    if hits > maxhits: break
    seg = data[max(0,i-before):i+len(needle)+after]
    txt = seg.decode('utf-8','replace')
    print(f"--- HIT {hits} at offset {i} ---")
    print(txt.replace('\n','\n'))
    print()
    start = i + 1
print(f"TOTAL HITS SCANNED (capped at {maxhits}): {hits}")
