"""ai_pipeline.py: dump (TGA originales del juego) -> Real-ESRGAN x4 -> d3d8hd\hd\<huella>.tga (4x).
Salta cachés de glifos (texto blanco con alfa: el modulo las suaviza en tiempo real) y texturas que no sean <=128x128.
Uso: python ai_pipeline.py [carpeta_juego]"""
import sys,os,re,subprocess,shutil,numpy as np
from PIL import Image
game=sys.argv[1] if len(sys.argv)>1 else r"E:\KONAMI\Winning Eleven 9"
dump=os.path.join(game,"d3d8hd","dump"); hd=os.path.join(game,"d3d8hd","hd"); work=r"E:\WE9_UI\ai\work"
EXE=r"E:\WE9_UI\ai\re2\realesrgan-ncnn-vulkan.exe"; MODELS=r"E:\WE9_UI\ai\re2\models"
for d in ("in_plus","in_anime","out_plus","out_anime"): shutil.rmtree(os.path.join(work,d),ignore_errors=True); os.makedirs(os.path.join(work,d))
os.makedirs(hd,exist_ok=True)
def is_text(a):
    m=a[...,3]>64
    return m.any() and (a[...,:3][m]>=0xE0).all()
n_plus=n_anime=skip=0
for f in sorted(os.listdir(dump)):
    m=re.match(r"([0-9a-f]{16})_(\d+)x(\d+)\.tga$",f)
    if not m: continue
    h,w,hh=m.group(1),int(m.group(2)),int(m.group(3))
    Sx=4 if max(w,hh)<=256 else 2
    if os.path.exists(os.path.join(hd,h+".tga")) and Image.open(os.path.join(hd,h+".tga")).size==(w*Sx,hh*Sx): continue
    S=4 if max(w,hh)<=256 else 2
    if (max(w,hh)>256 and (w,hh)!=(512,512)) or (w,hh) in ((256,128),(256,64),(128,64),(64,128),(32,64)): skip+=1; continue
    im=Image.open(os.path.join(dump,f)).convert("RGBA"); a=np.asarray(im)
    if is_text(a): skip+=1; continue
    cols=len(np.unique((a[a[...,3]>0][:,:3]//16),axis=0)) if (a[...,3]>0).any() else 0
    sub="in_anime" if cols<40 else "in_plus"
    im.save(os.path.join(work,sub,h+".png")); n_anime+= sub=="in_anime"; n_plus+= sub=="in_plus"
print("a procesar: general",n_plus,"anime(plano)",n_anime,"saltadas",skip)
for sub,model in (("plus","realesrgan-x4plus"),("anime","realesrgan-x4plus-anime")):
    if os.listdir(os.path.join(work,"in_"+sub)):
        subprocess.run([EXE,"-i",os.path.join(work,"in_"+sub),"-o",os.path.join(work,"out_"+sub),"-n",model,"-s","4","-f","png","-m",MODELS],cwd=work,capture_output=True)
done=0
for sub in ("plus","anime"):
    for f in os.listdir(os.path.join(work,"out_"+sub)):
        h=f[:-4]; ai=Image.open(os.path.join(work,"out_"+sub,f)).convert("RGBA"); src=Image.open(os.path.join(work,"in_"+sub,f)).convert("RGBA")
        sw,shh=src.size; Sx=4 if max(sw,shh)<=256 else 2
        ai=ai.resize((sw*Sx,shh*Sx),Image.LANCZOS) if ai.size!=(sw*Sx,shh*Sx) else ai
        lz=src.resize(ai.size,Image.LANCZOS)
        out=Image.blend(lz,ai,0.8)           # 80% IA + 20% suave (modera artefactos)
        out.save(os.path.join(hd,h+".tga"),"TGA"); done+=1
print("HD 4x generadas:",done)
