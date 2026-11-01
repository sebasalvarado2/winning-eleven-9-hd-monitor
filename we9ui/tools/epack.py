import sys,zlib,struct,os
import numpy as np
sys.path.insert(0,os.path.dirname(__file__))
from afs import entries
from dump_e import unsw8,palswz,tv
from PIL import Image
def subs_of(d):
    L=[];p=0x20
    while True:
        p=d.find(b"\x78\xda",p)
        if p<0:break
        if d[p-0x20]==0 and d[p-0x1e:p-0x1c]==b"\x01\x00": L.append(p-0x20)
        p+=2
    return L
def table(d):
    b=d[0x20:]; subs=[x-0x20 for x in subs_of(d)]
    words=struct.unpack("<%dI"%(len(b)//4),b[:len(b)//4*4])
    for i,w in enumerate(words):
        if w==subs[0] and list(words[i:i+len(subs)])==subs: return i*4,subs
    raise Exception("tabla no encontrada")
def get_sub(d,k):
    pos,subs=table(d); s=subs[k]+0x20
    return zlib.decompress(d[s+0x20:])
def best_variant(u):
    w,h=struct.unpack("<HH",u[0x14:0x18]); pal=np.frombuffer(u[0x80:0x480],np.uint8).reshape(256,4); px=np.frombuffer(u[0x480:0x480+w*h],np.uint8)
    best=None
    for sw in(0,1):
        I=(unsw8(px,w,h) if sw else px).reshape(h,w)
        for ps in(0,1):
            a=(palswz(pal) if ps else pal)[I]; s=tv(a)
            if best is None or s<best[0]: best=(s,sw,ps,a)
    a=best[3].copy(); a[...,3]=np.minimum(255,a[...,3].astype(int)*2)
    return Image.fromarray(a.astype(np.uint8),"RGBA"),best[1],best[2]
def swz8(idx,w,h):
    y,x=np.mgrid[0:h,0:w]
    pos=((y&~0xf)*w+(x&~0xf)*2)+((((y&~3)>>1)+(y&1))&7)*w*2+((x+((((y+2)>>2)&1)*4))&7)*4+((y>>1)&1)+((x>>2)&2)
    out=np.empty(w*h,np.uint8); out[pos.ravel()]=idx.ravel(); return out
def encode8(img,template,sw,ps):
    w,h=img.size
    q=img.convert("RGBA").quantize(colors=256,method=Image.Quantize.FASTOCTREE,dither=Image.Dither.NONE)
    pal=np.zeros((256,4),np.uint8); p=np.array(q.getpalette()[:768],np.uint8).reshape(-1,3); pal[:len(p),:3]=p
    a=np.asarray(img.convert("RGBA"))[...,3]; idx=np.asarray(q); 
    # alpha por indice = promedio
    for i in range(256):
        m=idx==i
        pal[i,3]=min(128,int(a[m].mean())//2) if m.any() else 0
    P=palswz(pal) if ps else pal   # palswz es una permutacion involutiva
    px=swz8(idx,w,h) if sw else idx.ravel().astype(np.uint8)
    hdr=bytearray(template[:0x80]); struct.pack_into("<HH",hdr,0x14,w,h)
    # campos de tamano y de log2 de dimensiones
    struct.pack_into("<I",hdr,8,0x80+0x400+w*h-0x0) if False else None
    lw,lh=w.bit_length()-1,h.bit_length()-1; hdr[0x1a]=lw; hdr[0x1b]=lh
    return bytes(hdr)+P.tobytes()+px.tobytes()
def rebuild(d,k,newu):
    pos,subs=table(d); b=d[0x20:]; n=len(subs)
    ends=subs[1:]+[len(b)]
    parts=[]
    for i in range(n):
        seg=b[subs[i]:ends[i]]
        if i==k:
            z=zlib.compress(newu,9); hd=bytearray(seg[:0x20]); struct.pack_into("<II",hd,4,len(z),len(newu))
            seg=bytes(hd)+z; seg+=b"\x00"*((-len(seg))%0x10)
        parts.append(seg)
    nb=bytearray(b[:subs[0]]); off=subs[0]; offs=[]
    for seg in parts: offs.append(off); nb+=seg; off+=len(seg)
    for i,o in enumerate(offs): struct.pack_into("<I",nb,pos+4*i,o)
    out=bytearray(d[:0x20])+nb; struct.pack_into("<I",out,4,len(nb)); return bytes(out)
