import sys,zlib,struct,re,os
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
src,out=sys.argv[1],sys.argv[2]; os.makedirs(out,exist_ok=True)
f,t=entries(src); n=0; sw=0
for i,(o,s) in enumerate(t):
    f.seek(o); d=f.read(s)
    names=[x.decode() for x in re.findall(rb"[A-Za-z][A-Za-z0-9_]{3,40}(?=\x00)",d[0x20:0x20+0x400])] if d[:4]==b"\x00\x02\x00\x00" else []
    k=0;p=0x20
    while True:
        p=d.find(b"\x78\xda",p)
        if p<0:break
        if p>=0x20 and d[p-0x20]==0 and d[p-0x1e:p-0x1c]==b"\x01\x00":
            try: u=zlib.decompress(d[p:])
            except Exception: p+=2;continue
            if u[:4]==MAG and u[0x19]==0x14:
                w,h=struct.unpack("<HH",u[0x14:0x18])
                if 0<w<=2048 and 0<h<=2048 and len(u)>=0x80+w*h//2:
                    a=lin4(u); b=dec4(u); ta,tb=tv(a),tv(b); pick=b if tb<ta else a; sw+= (tb<ta)
                    nm=names[k] if k<len(names) else ""
                    Image.fromarray(pick.astype(np.uint8),"RGBA").save(f"{out}/{i:03d}_{k:02d}_{nm}{'_sw' if tb<ta else ''}.png"); n+=1
            k+=1
        p+=2
print(n,"texturas 4bpp;",sw,"con swizzle")
