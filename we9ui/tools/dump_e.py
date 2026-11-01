import sys,zlib,struct,re,collections,os
import numpy as np
sys.path.insert(0,os.path.dirname(__file__))
from afs import entries
from PIL import Image
MAG=b"\x94\x72\x85\x29"
def unsw8(px,w,h):
    y,x=np.mgrid[0:h,0:w]
    idx=((y&~0xf)*w+(x&~0xf)*2)+((((y&~3)>>1)+(y&1))&7)*w*2+((x+((((y+2)>>2)&1)*4))&7)*4+((y>>1)&1)+((x>>2)&2)
    return px[idx]
def palswz(P):
    P2=P.copy()
    for i in range(256):
        if (i&0x18)==8:P2[i]=P[i+8]
        elif (i&0x18)==0x10:P2[i]=P[i-8]
    return P2
def tv(a):
    a=a[...,:3].astype(int); return abs(np.diff(a,axis=0)).sum()+abs(np.diff(a,axis=1)).sum()
def dec(u):
    w,h=struct.unpack("<HH",u[0x14:0x18]); fm=u[0x18:0x1a]
    if not(0<w<=2048 and 0<h<=2048): return None
    if fm[1]==0x14:
        pal=np.frombuffer(u[0x40:0x80],np.uint8).reshape(16,4); px=np.frombuffer(u[0x80:0x80+w*h//2],np.uint8)
        if len(px)<w*h//2: return None
        idx=np.empty(w*h,np.uint8); idx[0::2]=px&15; idx[1::2]=px>>4
        a=pal[idx.reshape(h,w)].copy()
    else:
        pal=np.frombuffer(u[0x80:0x480],np.uint8).reshape(256,4); px=np.frombuffer(u[0x480:0x480+w*h],np.uint8)
        if len(px)<w*h or len(pal)<256: return None
        best=None
        for sw in(0,1):
            I=(unsw8(px,w,h) if sw else px).reshape(h,w)
            for ps in(0,1):
                a=(palswz(pal) if ps else pal)[I]; s=tv(a)
                if best is None or s<best[0]: best=(s,a.copy())
        a=best[1]
    a=a.copy(); a[...,3]=np.minimum(255,a[...,3].astype(int)*2)
    return Image.fromarray(a.astype(np.uint8),"RGBA")
if __name__=="__main__":
    f,t=entries(sys.argv[1]); out=sys.argv[2]; n=0; unk=collections.Counter()
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
                if u[:4]==MAG:
                    im=dec(u)
                    if im: im.save(f"{out}/{i:03d}_{k:02d}_{names[k] if k<len(names) else ''}.png"); n+=1
                    else: unk["dec"]+=1
                else: unk[u[:4].hex()]+=1
                k+=1
            p+=2
    print(n,unk.most_common(4))
