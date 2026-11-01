import sys,zlib,struct,collections,os
sys.path.insert(0,os.path.dirname(__file__))
from afs import entries
from PIL import Image
MAG=b"\x94\x72\x85\x29"
def decode(b):
    w,h=struct.unpack("<HH",b[0x14:0x18]); fmt=b[0x18:0x1c]
    n=w*h
    if w==0 or h==0 or w>2048 or h>2048: return None,(w,h,fmt)
    pal=b[0x80:0x480]; px=b[0x480:0x480+n]
    if len(px)<n or len(pal)<0x400: return None,(w,h,fmt)
    P=[(pal[i*4],pal[i*4+1],pal[i*4+2],min(255,pal[i*4+3]*2)) for i in range(256)]
    im=Image.new("RGBA",(w,h)); im.putdata([P[p] for p in px]); return im,(w,h,fmt)
f,t=entries(sys.argv[1]); out=sys.argv[2]
stats=collections.Counter(); bad=collections.Counter()
for i,(o,s) in enumerate(t):
    f.seek(o); d=f.read(s)
    try:
        if d[:4] in(b"\x00\x06\x01\x00",b"\x00\x0e\x01\x00",b"\x00\x00\x01\x00") and d[0x20:0x22]==b"\x78\xda": u=zlib.decompress(d[0x20:])
        elif d[:4]==b"\x00\x06\x00\x00": u=d[0x20:]
        else: continue
    except Exception: continue
    # buscar magic en todo el buffer alineado a 0x10
    p=0; k=0
    while True:
        p=u.find(MAG,p)
        if p<0: break
        blk=u[p:p+0x80+0x400+2048*2048]
        sz=struct.unpack("<I",blk[8:12])[0] if len(blk)>12 else 0
        im,info=decode(blk)
        if im: im.save(f"{out}/{i:05d}_{k}.png"); stats[(info[0],info[1])]+=1
        else: bad[info]+=1
        k+=1; p+=4
print("ok",sum(stats.values()),"fallidas",sum(bad.values()))
print(stats.most_common(15)); print(bad.most_common(8))
