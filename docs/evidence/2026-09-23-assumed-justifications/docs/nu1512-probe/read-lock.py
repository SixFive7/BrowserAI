import json,sys
d=json.load(open(sys.argv[1]))
for tfm,deps in d['dependencies'].items():
    for k,v in deps.items(): print(f'   {tfm}  {k}  requested={v.get("requested")}  resolved={v.get("resolved")}')
