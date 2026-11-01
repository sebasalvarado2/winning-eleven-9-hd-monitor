import struct,numpy as np
from PIL import Image
from psmt4 import addr4,dec4
def encode4(img,template,sw=True):
    """img RGBA (w,h) -> textura 4bpp (header+paleta16+datos swizzle PSMT4). template = u original (para copiar cabecera)."""
    w,h=img.size; rgba=img.convert("RGBA"); A=np.asarray(rgba)
    q=rgba.quantize(colors=16,method=Image.Quantize.FASTOCTREE,dither=Image.Dither.NONE)
    idx=np.asarray(q).astype(np.uint8)
    pp=q.getpalette(); pp=np.array((pp+[0]*768)[:48],np.uint8).reshape(16,3)
    pal=np.zeros((16,4),np.uint8); pal[:,:3]=pp
    a=A[...,3]
    for i in range(16):
        m=idx==i; pal[i,3]=min(128,int(a[m].mean())//2) if m.any() else 0
    nib=np.zeros(w*h,np.uint8); nib[addr4(w,h)]=idx
    data=(nib[0::2]|(nib[1::2]<<4)).astype(np.uint8)
    hdr=bytearray(template[:0x80]); struct.pack_into("<HH",hdr,0x14,w,h); hdr[0x1a]=w.bit_length()-1; hdr[0x1b]=h.bit_length()-1
    struct.pack_into("<I",hdr,8,0x80+w*h//2); struct.pack_into("<I",hdr,0x20,w*h//32); struct.pack_into("<HH",hdr,0x28,w//2,h//2)
    hdr[0x2c]=max(1,w//128); hdr[0x2d]=2; struct.pack_into("<H",hdr,0x2e,max(1,w//128)*max(1,h//128))
    hdr[0x40:0x80]=pal.tobytes()
    return bytes(hdr)+data.tobytes()
