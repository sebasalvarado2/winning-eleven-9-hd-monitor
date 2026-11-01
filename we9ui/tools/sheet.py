SRC=__import__("sys").argv[4] if len(__import__("sys").argv)>4 else "png"
import sys,os
from PIL import Image,ImageDraw
lo,hi,out=int(sys.argv[1]),int(sys.argv[2]),sys.argv[3]
fs=[f for f in sorted(os.listdir(SRC)) if lo<=int(f[:5])<=hi]
T=128;C=10;R=(len(fs)+C-1)//C
sh=Image.new("RGB",(C*T,R*(T+12)),(60,60,60)); d=ImageDraw.Draw(sh)
for n,f in enumerate(fs):
    im=Image.open(SRC+"/"+f); im.thumbnail((T,T))
    bg=Image.new("RGB",im.size,(90,140,90)); bg.paste(im,mask=im.split()[3])
    x,y=(n%C)*T,(n//C)*(T+12); sh.paste(bg,(x,y+12)); d.text((x+2,y),f[:-4],fill=(255,255,0))
sh.save(out); print(len(fs),sh.size)
