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
EXTRA=[]
try: EXTRA=[l.strip() for l in open(r'E:'+chr(92)+'WE9_UI'+chr(92)+'d3dhook'+chr(92)+'ui_extra_hashes.txt') if l.strip() and not l.startswith('#')]
except Exception: pass
n_plus=n_anime=skip=0
for f in sorted(os.listdir(dump)):
    m=re.match(r"([0-9a-f]{16})_(\d+)x(\d+)\.tga$",f)
    if not m: continue
    h,w,hh=m.group(1),int(m.group(2)),int(m.group(3))
    Sx=4 if max(w,hh)<=256 else 2
    if os.path.exists(os.path.join(hd,h+".tga")) and Image.open(os.path.join(hd,h+".tga")).size==(w*Sx,hh*Sx): continue
    S=4 if max(w,hh)<=256 else 2
    isx=any(h.startswith(x) for x in EXTRA)
    if not isx and ((max(w,hh)>256 and (w,hh)!=(512,512)) or (w,hh) in ((256,128),(256,64),(128,64),(64,128),(32,64))): skip+=1; continue
    im=Image.open(os.path.join(dump,f)).convert("RGBA"); a=np.asarray(im)
    if is_text(a): skip+=1; continue
    cols=len(np.unique((a[a[...,3]>0][:,:3]//16),axis=0)) if (a[...,3]>0).any() else 0
    sub="in_anime" if cols<40 else "in_plus"
    im.save(os.path.join(work,sub,h+".png")); n_anime+= sub=="in_anime"; n_plus+= sub=="in_plus"
print("a procesar: general",n_plus,"anime(plano)",n_anime,"saltadas",skip)
for sub,model in (("plus","realesrgan-x4plus"),("anime","realesrgan-x4plus-anime")):
    if os.listdir(os.path.join(work,"in_"+sub)):
        subprocess.run([EXE,"-i",os.path.join(work,"in_"+sub),"-o",os.path.join(work,"out_"+sub),"-n",model,"-s","4","-f","png","-m",MODELS],cwd=work,capture_output=True)
def run_ai(indir,outdir,model):
    subprocess.run([EXE,"-i",indir,"-o",outdir,"-n",model,"-s","4","-f","png","-m",MODELS],cwd=work,capture_output=True)
def good(ai,src):
    a=np.asarray(ai.convert("RGBA")).astype(float); b=np.asarray(src.resize(ai.size,Image.LANCZOS).convert("RGBA")).astype(float)
    if b[...,3].max()>200 and a[...,3].max()<0.8*b[...,3].max(): return False
    ma=a[...,3]>128; mb=b[...,3]>128
    if mb.sum()==0: return True
    if ma.sum()<0.5*mb.sum(): return False
    d=abs(a[...,:3][ma].mean()-b[...,:3][mb].mean()); return d<35
done=0; fallback=0
for sub,model in (("plus","realesrgan-x4plus"),("anime","realesrgan-x4plus-anime")):
    outd=os.path.join(work,"out_"+sub); ind=os.path.join(work,"in_"+sub)
    # verificar; reintentar los malos una vez
    badl=[f for f in os.listdir(outd) if not good(Image.open(os.path.join(outd,f)),Image.open(os.path.join(ind,f)))]
    if badl:
        rd=os.path.join(work,"retry_in"); rO=os.path.join(work,"retry_out")
        for d_ in (rd,rO): shutil.rmtree(d_,ignore_errors=True); os.makedirs(d_)
        for f in badl: shutil.copy(os.path.join(ind,f),os.path.join(rd,f))
        run_ai(rd,rO,model)
        for f in badl:
            p=os.path.join(rO,f)
            if os.path.exists(p) and good(Image.open(p),Image.open(os.path.join(ind,f))): shutil.copy(p,os.path.join(outd,f))
    for f in os.listdir(outd):
        h=f[:-4]; ai=Image.open(os.path.join(outd,f)).convert("RGBA"); src=Image.open(os.path.join(ind,f)).convert("RGBA")
        sw,shh=src.size; Sx=4 if max(sw,shh)<=256 else 2
        lz=src.resize((sw*Sx,shh*Sx),Image.LANCZOS)
        if good(ai,src):
            ai=ai.resize((sw*Sx,shh*Sx),Image.LANCZOS) if ai.size!=(sw*Sx,shh*Sx) else ai
            out=Image.blend(lz,ai,0.8)
        else: out=lz; fallback+=1            # la IA fallo: solo ampliacion suave (nunca archivo vacio)
        out.save(os.path.join(hd,h+".tga"),"TGA"); done+=1
print("HD generadas:",done,"| con IA fallida (solo suave):",fallback)
