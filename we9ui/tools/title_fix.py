import sys,struct,os
sys.path.insert(0,r"E:\WE9_UI\tools")
import numpy as np
from epack import *
from afs import entries
from upscale_menu_rest import up2,encode_opaque,encode_alpha,fixhdr,DST
from PIL import Image,ImageFilter
E,K=270,0
for lang,name in(("e","e_text.afs"),("s","S_TEXT.AFS")):
    if lang=="e": d=open(r"E:\WE9_UI\e_text\%04d.bin"%E,"rb").read()
    else:
        f,t=entries(r"E:\KONAMI\Winning Eleven 9\dat\S_TEXT.AFS"); o,s=t[E]; f.seek(o); d=f.read(s)
    u=get_sub(d,K); im,sw,ps=best_variant(u); a=np.asarray(im)
    print(name,im.size,"alpha min",a[...,3].min(),"sw/ps",sw,ps)
    big=up2(im.convert("RGBA"))
    # franja de textos (filas >=448): fondo transparente + texto negro -> alpha nitido tipo vector
    A0=np.asarray(im.convert("RGBA"))[448:512,:,3]
    al=np.asarray(Image.fromarray(A0).resize((1024,128),Image.LANCZOS)).astype(np.float32)
    al=np.clip((al-110)*2.0+128,0,255)
    B=np.asarray(big).copy(); B[896:1024,:,:3]=0; B[896:1024,:,3]=al.astype(np.uint8)
    big=Image.fromarray(B,"RGBA")
    nu=fixhdr(encode_alpha(big,u,sw,ps),1024,1024); nd=rebuild(d,K,nu)
    os.makedirs(os.path.join(DST,name),exist_ok=True); open(os.path.join(DST,name,"menu_270.bin"),"wb").write(nd); print(" ",len(nd))
    im2,_,_=best_variant(get_sub(nd,K)); im2.convert("RGB").save(os.path.join(sys.argv[1],"title_%s.png"%lang))
