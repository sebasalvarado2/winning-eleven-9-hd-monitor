import sys,zlib,struct,os
sys.path.insert(0,r"E:\WE9_UI\tools")
import numpy as np
from afs import entries
from psmt4 import dec4
from dump_e import tv
from PIL import Image
MAG=b"\x94\x72\x85\x29"
def lin4(u):
    w,h=struct.unpack("<HH",u[0x14:0x18]); pal=np.frombuffer(u[0x40:0x80],np.uint8).reshape(16,4)
    data=np.frombuffer(u[0x80:0x80+w*h//2],np.uint8); idx=np.empty(w*h,np.uint8); idx[0::2]=data&15; idx[1::2]=data>>4
    a=pal[idx.reshape(h,w)].copy(); a[...,3]=np.minimum(255,a[...,3].astype(int)*2); return a
f,t=entries(sys.argv[1]); out=sys.argv[2]; os.makedirs(out,exist_ok=True); n=0; sw=0
for i,(o,s) in enumerate(t):
    f.seek(o); d=f.read(s)
    try:
        if d[0x20:0x22]==b"\x78\xda": u=zlib.decompress(d[0x20:])
        elif d[:4]==b"\x00\x06\x00\x00": u=d[0x20:]
        else: continue
    except Exception: continue
    p=0;k=0
    while True:
        p=u.find(MAG,p)
        if p<0:break
        v=u[p:]
        if v[0x19]==0x14:
            w,h=struct.unpack("<HH",v[0x14:0x18])
            if 0<w<=1024 and 0<h<=1024 and len(v)>=0x80+w*h//2:
                v=v[:0x80+w*h//2]; a=lin4(v); b=dec4(v); ta,tb=tv(a),tv(b); pick=b if tb<ta else a; sw+=(tb<ta)
                Image.fromarray(pick.astype(np.uint8),"RGBA").save(f"{out}/{i:05d}_{k}{'_sw' if tb<ta else ''}.png"); n+=1
        k+=1;p+=4
print(n,"texturas 4bpp;",sw,"con swizzle")
