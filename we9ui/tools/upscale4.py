import sys,struct,os
sys.path.insert(0,r"E:\WE9_UI\tools")
import numpy as np
from epack import *
from afs import entries
from psmt4 import dec4
from enc4 import encode4
from upscale_menu_rest import up2,DST
from PIL import Image
ENT=[int(x) for x in sys.argv[2:]]; SC=sys.argv[1]
fS,tS=entries(r"E:\KONAMI\Winning Eleven 9\dat\S_TEXT.AFS")
def base(lang,name,e):
    p=os.path.join(DST,name,"menu_%d.bin"%e)
    if os.path.exists(p): return open(p,"rb").read(),True
    if lang=="e": return open(r"E:\WE9_UI\e_text\%04d.bin"%e,"rb").read(),False
    o,s=tS[e]; fS.seek(o); return fS.read(s),False
for e in ENT:
    for lang,name in(("e","e_text.afs"),("s","S_TEXT.AFS")):
        d,had=base(lang,name,e)
        try: pos,subs=table(d)
        except Exception: print(" sin tabla",e,name); continue
        done=[]
        for k in range(len(subs)):
            u=get_sub(d,k)
            if u[:4]!=b"\x94\x72\x85\x29" or u[0x19]!=0x14: continue
            w,h=struct.unpack("<HH",u[0x14:0x18])
            if (w&(w-1)) or (h&(h-1)) or min(w,h)<64 or max(w,h)>256: continue
            # ya procesada? (si ya mide el doble de lo esperado no se sabe) -> solo procesar si la entrada base es la original (no tenia 4bpp tocado)
            a=dec4(u); im=Image.fromarray(a.astype(np.uint8),"RGBA")
            # elegir lineal vs swizzle como en dump4: si el swizzle da peor TV, saltar (formato distinto)
            from dump_e import tv
            lin=None
            pal=np.frombuffer(u[0x40:0x80],np.uint8).reshape(16,4); data=np.frombuffer(u[0x80:0x80+w*h//2],np.uint8); ii=np.empty(w*h,np.uint8); ii[0::2]=data&15; ii[1::2]=data>>4
            if tv(pal[ii.reshape(h,w)])<tv(a): continue
            big=up2(im); d=rebuild(d,k,encode4(big,u)); done.append((k,w))
        if done:
            os.makedirs(os.path.join(DST,name),exist_ok=True); open(os.path.join(DST,name,"menu_%d.bin"%e),"wb").write(d); print(e,name,len(d)//1024,"KB",done)
