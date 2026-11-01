import struct,numpy as np
def addr4(w,h):
    y,x=np.mgrid[0:h,0:w]
    posY=((y>>3)&1)*4+((y>>2)&1)*2+(y&1)        # bits: y0 -> bit0? (ver abajo)
    # posY teorico = (((y&~3)>>1)+(y&1))&7 = y0 | (y2<<1) | (y3<<2)
    posY=(y&1)|(((y>>2)&1)<<1)|(((y>>3)&1)<<2)
    low=((y>>1)&1)|(((x>>3)&1)<<1)|((x&1)<<2)|(((x>>1)&1)<<3)|((((x>>2)^(y>>1)^(y>>2))&1)<<4)|(((x>>4)&1)<<5)
    # bits x4..x7 -> (x&~0xf)*2 ya incluye x4 en bit5 (x>>4<<5)
    return ((y&~0xf)*w)+((x&~0xf)*2)+posY*w*2+(low&0x1f)
def unsw4(nib,w,h):
    N=addr4(w,h); return nib[N]
def dec4(u):
    """u = textura descomprimida (magic 94728529) 4bpp. Devuelve RGBA np (h,w,4) con alpha*2."""
    w,h=struct.unpack("<HH",u[0x14:0x18]); pal=np.frombuffer(u[0x40:0x80],np.uint8).reshape(16,4)
    data=np.frombuffer(u[0x80:0x80+w*h//2],np.uint8); nib=np.empty(w*h,np.uint8); nib[0::2]=data&15; nib[1::2]=data>>4
    a=pal[unsw4(nib,w,h)].copy(); a[...,3]=np.minimum(255,a[...,3].astype(int)*2); return a
