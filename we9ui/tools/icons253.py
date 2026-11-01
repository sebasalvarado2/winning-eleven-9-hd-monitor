import sys,struct,os
sys.path.insert(0,r"E:\WE9_UI\tools")
import numpy as np
from epack import *
from afs import entries
from upscale_menu_rest import up2,encode_opaque,encode_alpha,fixhdr,DST
SKIP={19,20,21,24,25}      # ya hechos (texto, fondos, anillo) o 4bpp
for name in("e_text.afs","S_TEXT.AFS"):
    p=os.path.join(DST,name,"menu_253.bin"); d=open(p,"rb").read(); pos,subs=table(d); done=[]
    for k in range(len(subs)):
        if k in SKIP: continue
        u=get_sub(d,k)
        if u[:4]!=b"\x94\x72\x85\x29" or u[0x19]!=0x13: continue
        w,h=struct.unpack("<HH",u[0x14:0x18])
        if (w&(w-1)) or (h&(h-1)) or min(w,h)<32 or max(w,h)>256: continue
        if w*2>512 or w>=512: continue
        im,sw,ps=best_variant(u); a=np.asarray(im)[...,3]; big=up2(im.convert("RGBA"))
        nu=encode_opaque(big,u,sw,ps) if a.min()==255 else encode_alpha(big,u,sw,ps)
        d=rebuild(d,k,fixhdr(nu,w*2,h*2)); done.append((k,w))
    open(p,"wb").write(d); print(name,len(d)//1024,"KB",done)
