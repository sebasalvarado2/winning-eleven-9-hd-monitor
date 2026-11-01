import sys,struct,os
sys.path.insert(0,r"E:\WE9_UI\tools")
import numpy as np
from epack import *
from afs import entries
from PIL import Image,ImageDraw,ImageFont,ImageFilter
EN=["MATCH","MASTER LEAGUE","LEAGUE","CUP","TRAINING","EDIT","OPTIONS","NETWORK","XBOX LIVE","EXIT"]
ES=["PARTIDO","LIGA MASTER","LIGA","COPA","ENTRENAMIENTO","EDITAR","OPCIONES","DE RED","XBOX LIVE","SALIR"]
FONTS={"verdana":r"C:\Windows\Fonts\verdanab.ttf","segoeblack":r"C:\Windows\Fonts\seguibl.ttf","bahn":r"C:\Windows\Fonts\bahnschrift.ttf"}
def load(lang):
    if lang=="en": d=open(r"E:\WE9_UI\e_text\0253.bin","rb").read()
    else:
        f,t=entries(r"E:\KONAMI\Winning Eleven 9\dat\S_TEXT.AFS"); o,s=t[253]; f.seek(o); d=f.read(s)
    u=get_sub(d,24); im,sw,ps=best_variant(u); return d,u,im,sw,ps
def rows_of(im):
    a=np.asarray(im).astype(int); al=a[...,3]; white=(a[...,:3].min(2)>190)&(al>200)
    ys=np.where((al>20).any(1))[0]; g=[];st=ys[0];pv=ys[0]
    for r in ys[1:]:
        if r-pv>3:g.append((st,pv));st=r
        pv=r
    g.append((st,pv)); out=[]
    for (y0,y1) in g:
        res=[]
        for xs,xe in((0,256),(256,512)):
            m=white[y0:y1+1,xs:xe]; yy,xx=np.where(m)
            res.append((xs+xx.min(),y0+yy.min(),xs+xx.max()+1,y0+yy.max()+1))
        out.append(res)
    return out
def word_mask(txt,fontpath,w,h,bold=0,S=4):
    # alto de mayuscula = h ; ancho final = w
    fs=100; ft=ImageFont.truetype(fontpath,fs)
    if "variable" in dir(ft):
        try: ft.set_variation_by_name("Bold")
        except Exception: pass
    bb=ft.getbbox("H"); capH=bb[3]-bb[1]; fs=int(fs*h*S/capH); ft=ImageFont.truetype(fontpath,fs)
    try: ft.set_variation_by_name("Bold")
    except Exception: pass
    l,t,r,b=ft.getbbox(txt); cb=ft.getbbox("H")
    im=Image.new("L",(r-l+8,(cb[3]-cb[1])+8),0); ImageDraw.Draw(im).text((4-l,4-cb[1]),txt,fill=255,font=ft)
    bbx=im.getbbox(); im=im.crop(bbx)
    im=im.resize((w*S,h*S),Image.LANCZOS).resize((w,h),Image.LANCZOS); return im
def render(lang,font,scale=2):
    d,u,im,sw,ps=load(lang); R=rows_of(im); words=EN if lang=="en" else ES
    W=512*scale; MG=48; canvas=np.zeros((W+2*MG,W+2*MG,4),np.float32)
    def blur(m,r): return np.asarray(Image.fromarray((m*255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(r))).astype(np.float32)/255
    for i,(n,hl) in enumerate(R):
        for col,(x0,y0,x1,y1) in enumerate((n,hl)):
            w=(x1-x0)*scale; h=(y1-y0)*scale
            M=word_mask(words[i],FONTS[font],w,h)
            pad=24*scale//2; big=np.zeros((h+2*pad,w+2*pad),np.float32); big[pad:pad+h,pad:pad+w]=np.asarray(M)/255
            if col==0:
                sh=np.roll(np.roll(big,2*scale,0),2*scale,1); sh=np.clip(blur(sh,1.6*scale)*1.9,0,1)*0.85
                lay_a=sh; lay_c=0.0   # negro
                txt=big
                a=txt+lay_a*(1-txt); c=txt*1.0/np.maximum(a,1e-4)
            else:
                gl=np.clip(blur(big,1.8*scale)*1.0+blur(big,4.5*scale)*0.55,0,1)*0.62
                txt=big; a=txt+gl*(1-txt); c=np.where(a>0,(txt*1.0+gl*(1-txt)*0.96)/np.maximum(a,1e-4),1)
            ox,oy=x0*scale-pad+MG,y0*scale-pad+MG
            sl=(slice(oy,oy+big.shape[0]),slice(ox,ox+big.shape[1]))
            reg=canvas[sl]; reg[...,0]=np.maximum(reg[...,0],c*255*(a>0)); reg[...,1]=reg[...,0]; reg[...,2]=reg[...,0]; reg[...,3]=np.maximum(reg[...,3],a*255)
    canvas=canvas[MG:MG+W,MG:MG+W]
    return Image.fromarray(np.clip(canvas,0,255).astype(np.uint8),"RGBA"),(d,u,sw,ps,im)
if __name__=="__main__":
    SC=r"C:\Users\Sebas\AppData\Local\Temp\claude\C--WINDOWS-system32\45e4f40b-f35d-40d2-9969-2539b2ec2fc3\scratchpad"
    tiles=[]
    for font in FONTS:
        img,(d,u,sw,ps,im)=render("es",font)
        bg=Image.new("RGB",img.size,(90,140,90)); bg.paste(img,mask=img.split()[3]); tiles.append(bg.crop((0,0,1024,400)))
        bg.save(os.path.join(SC,"r_%s.png"%font))
    o=Image.new("RGB",(1024,400),(90,140,90)); o2=im.resize((1024,1024),Image.LANCZOS); o.paste(o2,(0,0),o2); 
    sh=Image.new("RGB",(1024,400*4)); sh.paste(o,(0,0))
    for i,t in enumerate(tiles): sh.paste(t,(0,400*(i+1)))
    sh.save(os.path.join(SC,"cmp.png"))
