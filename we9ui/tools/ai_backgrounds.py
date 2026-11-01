"""ai_backgrounds.py <preview_dir>: reconstruye con IA (Real-ESRGAN x4 -> 2x) los FONDOS 512x512 8bpp de las entradas de menu (afs2fs).
Entradas: 251 (4 recursos), 253 (rec 20 y 21 sobre el menu_253.bin actual: conserva texto y anillo), 245, 120-125, 132-147, 264 (rec 0 y 51)."""
import sys,os,struct,hashlib,subprocess,shutil
sys.path.insert(0,r"E:\WE9_UI\tools")
import numpy as np
from epack import *
from afs import entries
from upscale_menu_rest import encode_opaque,encode_alpha,fixhdr,DST
from PIL import Image
SC=sys.argv[1]; WORK=r"E:\WE9_UI\ai\work_bg"
for sub in ("in","out"): shutil.rmtree(os.path.join(WORK,sub),ignore_errors=True); os.makedirs(os.path.join(WORK,sub))
EXE=r"E:\WE9_UI\ai\re2\realesrgan-ncnn-vulkan.exe"; MODELS=r"E:\WE9_UI\ai\re2\models"
fS,tS=entries(r"E:\KONAMI\Winning Eleven 9\dat\S_TEXT.AFS")
def orig(lang,e):
    if lang=="e": return open(r"E:\WE9_UI\e_text\%04d.bin"%e,"rb").read()
    o,s=tS[e]; fS.seek(o); return fS.read(s)
PLAN={251:[0,1,2,3],253:[20,21],245:[4,5,6,7],264:[0,51]}
for e in list(range(120,126))+list(range(132,148)): PLAN[e]=[0]
jobs=[]; imgs={}
for e,subs in PLAN.items():
    for lang,name in(("e","e_text.afs"),("s","S_TEXT.AFS")):
        d=orig(lang,e)
        try: pos,sb=table(d)
        except Exception: continue
        for k in subs:
            if k>=len(sb): continue
            u=get_sub(d,k)
            if u[:4]!=b"\x94\x72\x85\x29" or u[0x19]!=0x13: continue
            w,h=struct.unpack("<HH",u[0x14:0x18])
            if (w,h)!=(512,512): continue
            im,sw,ps=best_variant(u); key=hashlib.md5(np.asarray(im).tobytes()).hexdigest()[:16]
            if key not in imgs: im.convert("RGBA").save(os.path.join(WORK,"in",key+".png")); imgs[key]=True
            jobs.append((e,lang,name,k,key,sw,ps))
print("recursos:",len(jobs),"imagenes unicas:",len(imgs))
subprocess.run([EXE,"-i",os.path.join(WORK,"in"),"-o",os.path.join(WORK,"out"),"-n","realesrgan-x4plus","-s","4","-f","png","-m",MODELS],cwd=WORK,capture_output=True)
print("IA lista:",len(os.listdir(os.path.join(WORK,"out"))))
cache={}
def final(key):
    if key in cache: return cache[key]
    src=Image.open(os.path.join(WORK,"in",key+".png")).convert("RGBA"); ai=Image.open(os.path.join(WORK,"out",key+".png")).convert("RGBA").resize((1024,1024),Image.LANCZOS)
    lz=src.resize((1024,1024),Image.LANCZOS); cache[key]=Image.blend(lz,ai,0.8); return cache[key]
built={}
for (e,lang,name,k,key,sw,ps) in jobs:
    p=os.path.join(DST,name,"menu_%d.bin"%e)
    cur=built.get((e,lang)) or (open(p,"rb").read() if (e==253 and os.path.exists(p)) else orig(lang,e))
    u=get_sub(orig(lang,e),k); big=final(key)
    a=np.asarray(big)[...,3]
    nu=fixhdr(encode_opaque(big,u,sw,ps) if a.min()==255 else encode_alpha(big,u,sw,ps),1024,1024)
    built[(e,lang)]=rebuild(cur,k,nu)
for (e,lang),dd in built.items():
    name="e_text.afs" if lang=="e" else "S_TEXT.AFS"
    os.makedirs(os.path.join(DST,name),exist_ok=True); open(os.path.join(DST,name,"menu_%d.bin"%e),"wb").write(dd)
print("archivos escritos:",len(built))
for key in list(cache)[:6]: cache[key].convert("RGB").resize((400,400)).save(os.path.join(SC,"bgai_%s.png"%key))
