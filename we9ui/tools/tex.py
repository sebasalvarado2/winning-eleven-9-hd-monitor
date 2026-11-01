import struct
from PIL import Image
def decode(b, pal_first=False, swz=False):
    assert b[:4]==b"\x94\x72\x85\x29"
    w,h=struct.unpack("<HH",b[0x14:0x18])
    n=w*h
    if pal_first: pal=b[0x80:0x480]; px=b[0x480:0x480+n]
    else: px=b[0x80:0x80+n]; pal=b[0x80+n:0x80+n+0x400]
    P=[tuple(pal[i*4:i*4+4]) for i in range(256)]
    if swz:
        P2=P[:]
        for i in range(256):
            if (i&0x18)==0x08: P2[i]=P[i+8]
            elif (i&0x18)==0x10: P2[i]=P[i-8]
        P=P2
    im=Image.new("RGBA",(w,h))
    im.putdata([(P[p][0],P[p][1],P[p][2],min(255,P[p][3]*2)) for p in px])
    return im
