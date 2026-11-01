r"""upscale_entry.py <carpeta_preview> <min_lado> <entradas...>  (ej: 512 120 121 ...)
Duplica a 2x las texturas 8bpp (potencia de 2, lado>=min_lado, max 512) de cada entrada, en e_text.afs y S_TEXT.AFS.
Escribe kitserver\hd_menus\<afs>\menu_<entrada>.bin partiendo SIEMPRE del original."""
import sys,struct,os
sys.path.insert(0,r"E:\WE9_UI\tools")
import numpy as np
from epack import *
from afs import entries
from upscale_menu_rest import up2,encode_opaque,encode_alpha,fixhdr,DST
from PIL import Image
SC=sys.argv[1]; MIN=int(sys.argv[2]); ENT=[int(x) for x in sys.argv[3:]]
fS,tS=entries(r"E:\KONAMI\Winning Eleven 9\dat\S_TEXT.AFS")
def orig(lang,e):
    if lang=="e": return open(r"E:\WE9_UI\e_text\%04d.bin"%e,"rb").read()
    o,s=tS[e]; fS.seek(o); return fS.read(s)
log=[]
for e in ENT:
    for lang,name in(("e","e_text.afs"),("s","S_TEXT.AFS")):
        d=orig(lang,e)
        try: pos,subs=table(d)
        except Exception: print('  sin tabla, salteada:',e,name); continue
        done=[]
        for k in range(len(subs)):
            u=get_sub(d,k)
            if u[:4]!=b"\x94\x72\x85\x29" or u[0x19]!=0x13: continue
            w,h=struct.unpack("<HH",u[0x14:0x18])
            if (w&(w-1)) or (h&(h-1)) or min(w,h)<MIN or max(w,h)>512: continue
            im,sw,ps=best_variant(u); a=np.asarray(im)[...,3]; big=up2(im.convert("RGBA"))
            nu=encode_opaque(big,u,sw,ps) if a.min()==255 else encode_alpha(big,u,sw,ps)
            d=rebuild(d,k,fixhdr(nu,w*2,h*2)); done.append(k)
        if done:
            os.makedirs(os.path.join(DST,name),exist_ok=True); open(os.path.join(DST,name,"menu_%d.bin"%e),"wb").write(d)
            log.append((e,name,done,len(d)//1024))
            if lang=="e":
                im2,_,_=best_variant(get_sub(d,done[0])); bg=Image.new("RGB",im2.size,(90,140,90)); bg.paste(im2,mask=im2.split()[3]); bg.save(os.path.join(SC,"ue_%d.png"%e))
for r in log: print(r)
