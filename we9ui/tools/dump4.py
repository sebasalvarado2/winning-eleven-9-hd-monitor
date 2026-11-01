import sys,zlib,struct,os
sys.path.insert(0,os.path.dirname(__file__))
from afs import entries
from PIL import Image
MAG=b"\x94\x72\x85\x29"
f,t=entries(sys.argv[1]); out=sys.argv[2]; n=0
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
        fm=u[p+0x18:p+0x1a]
        if fm==b"\x04\x14" or fm==b"\x02\x14":
            w,h=struct.unpack("<HH",u[p+0x14:p+0x18])
            if 0<w<=1024 and 0<h<=1024:
                pal=u[p+0x40:p+0x80]; px=u[p+0x80:p+0x80+w*h//2]
                if len(px)==w*h//2:
                    P=[(pal[j*4],pal[j*4+1],pal[j*4+2],min(255,pal[j*4+3]*2)) for j in range(16)]
                    data=[]
                    for b in px: data+=[P[b&15],P[b>>4]]
                    im=Image.new("RGBA",(w,h)); im.putdata(data); im.save(f"{out}/{i:05d}_{k}.png"); n+=1
        k+=1;p+=4
print(n)
