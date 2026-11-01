"""hdpipeline.py: convierte d3d8hd\dump\<hash>_<w>x<h>.tga (texturas originales del juego) en d3d8hd\hd\<hash>.tga (2x).
Uso: python hdpipeline.py [carpeta_juego] [--min 16] [--max 512] [--skip hash,hash]"""
import sys,os,re,numpy as np
sys.path.insert(0,r"E:\WE9_UI\tools")
from upscale_menu_rest import up2
from PIL import Image
game=r"E:\KONAMI\Winning Eleven 9"; mn,mx=16,512
a=sys.argv[1:]
if a and not a[0].startswith("--"): game=a[0]; a=a[1:]
for i,x in enumerate(a):
    if x=="--min": mn=int(a[i+1])
    if x=="--max": mx=int(a[i+1])
dump=os.path.join(game,"d3d8hd","dump"); hd=os.path.join(game,"d3d8hd","hd"); os.makedirs(hd,exist_ok=True)
n=0;skip=0
for f in sorted(os.listdir(dump)):
    m=re.match(r"([0-9a-f]{16})_(\d+)x(\d+)\.tga$",f)
    if not m: continue
    h,w,hh=m.group(1),int(m.group(2)),int(m.group(3))
    out=os.path.join(hd,h+".tga")
    if os.path.exists(out): continue
    if min(w,hh)<mn or max(w,hh)>mx: skip+=1; continue
    im=Image.open(os.path.join(dump,f)).convert("RGBA")
    big=up2(im); big.save(out,"TGA"); n+=1
print("convertidas",n,"saltadas por tamano",skip)
