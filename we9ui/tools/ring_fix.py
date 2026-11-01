import sys,struct,os
sys.path.insert(0,r"E:\WE9_UI\tools")
import numpy as np
from epack import *
from afs import entries
from PIL import Image
try:
    from scipy.ndimage import gaussian_filter as GF, maximum_filter, minimum_filter
except Exception:
    GF=None
DST=r"E:\KONAMI\Winning Eleven 9\kitserver\hd_menus"
def fixhdr(b,w,h):
    b=bytearray(b); struct.pack_into("<I",b,8,0x480+w*h); struct.pack_into("<I",b,0x20,w*h//16)
    struct.pack_into("<HH",b,0x28,w//2,h//2); struct.pack_into("<HH",b,0x2c,w//128,(w//128)*(h//128)*2); return bytes(b)
def lz(arr,scale=2):
    # Lanczos de un canal float 0..1 via PIL 'F'
    im=Image.fromarray(arr.astype(np.float32),"F").resize((arr.shape[1]*scale,arr.shape[0]*scale),Image.LANCZOS); return np.asarray(im)
def smooth_ring(img):
    A=np.asarray(img.convert("RGBA")).astype(np.float32); a0=A[...,3]/255; rgb0=A[...,:3]
    aup=np.clip(lz(a0),0,1)
    own=np.stack([lz(rgb0[...,c]*a0) for c in range(3)],-1)/np.maximum(aup[...,None],0.02)
    own=np.clip(own,0,255)
    asm=GF(aup,2.2); steep=np.clip((asm-0.5)*2.4+0.5,0,1)
    rng=maximum_filter(aup,7)-minimum_filter(aup,7)
    edge=(rng>0.8)
    a=np.where(edge,steep,aup)
    a=np.where(maximum_filter(a,3)<0.02,0,a)
    band=maximum_filter(edge.astype(np.uint8),9)>0
    core=minimum_filter((a>0.97).astype(np.uint8),5)>0      # interior firme, sin los 2 px del borde
    cw=core.astype(np.float32)
    num=np.stack([GF(own[...,c]*cw,5.0) for c in range(3)],-1); den=GF(cw,5.0)[...,None]
    fill=num/np.maximum(den,1e-4)
    rgb=np.where((band&~core)[...,None],fill,own)
    out=np.concatenate([np.clip(rgb,0,255),a[...,None]*255],-1)
    return Image.fromarray(out.astype(np.uint8),"RGBA")
def encode_ring(img,template,sw,ps,NOP=140,NC=6,LV=14):
    w,h=img.size; A=np.asarray(img.convert("RGBA")); rgb=Image.fromarray(A[...,:3],"RGB"); a=A[...,3]
    op=a>=250; tr=a<6; ed=~op&~tr
    pq=Image.fromarray(A[...,:3][op].reshape(1,-1,3),"RGB").quantize(colors=NOP,method=Image.Quantize.MEDIANCUT,dither=Image.Dither.NONE)
    q=rgb.quantize(palette=pq,dither=Image.Dither.NONE); idx=np.asarray(q).astype(np.int32).copy()
    pal=np.zeros((256,4),np.uint8); p=np.array(pq.getpalette()[:3*NOP],np.uint8).reshape(-1,3); pal[:len(p),:3]=p; pal[:NOP,3]=128
    idx[tr]=NOP; pal[NOP]=(0,0,0,0); levels=np.linspace(16,244,LV)
    eq=Image.fromarray(A[...,:3][ed].reshape(1,-1,3),"RGB").quantize(colors=NC,method=Image.Quantize.MEDIANCUT,dither=Image.Dither.NONE)
    ep=np.array(eq.getpalette()[:3*NC],np.uint8).reshape(-1,3); ci=np.asarray(eq).reshape(-1).astype(np.int32)
    li=np.abs(a[ed][:,None].astype(float)-levels[None,:]).argmin(1); idx[ed]=NOP+1+ci*LV+li
    for c in range(len(ep)):
        for l in range(LV): pal[NOP+1+c*LV+l]=(*ep[c],int(levels[l])//2)
    P=palswz(pal) if ps else pal; px=swz8(idx.astype(np.uint8),w,h) if sw else idx.astype(np.uint8).ravel()
    hdr=bytearray(template[:0x80]); struct.pack_into("<HH",hdr,0x14,w,h); hdr[0x1a]=w.bit_length()-1; hdr[0x1b]=h.bit_length()-1
    return bytes(hdr)+P.tobytes()+px.tobytes()
if __name__=="__main__":
    SC=sys.argv[1]
    for lang,name in(("e","e_text.afs"),("s","S_TEXT.AFS")):
        if lang=="e": od=open(r"E:\WE9_UI\e_text\0253.bin","rb").read()
        else:
            f,t=entries(r"E:\KONAMI\Winning Eleven 9\dat\S_TEXT.AFS"); o,s=t[253]; f.seek(o); od=f.read(s)
        u=get_sub(od,25); im,sw,ps=best_variant(u); big=smooth_ring(im)
        nu=fixhdr(encode_ring(big,u,sw,ps),1024,1024)
        cur=open(os.path.join(DST,name,"menu_253.bin"),"rb").read(); nd=rebuild(cur,25,nu)
        open(os.path.join(DST,name,"menu_253.bin"),"wb").write(nd); print(name,len(nd))
        im2,_,_=best_variant(get_sub(nd,25)); bg=Image.new("RGB",im2.size,(90,140,90)); bg.paste(im2,mask=im2.split()[3]); bg.save(os.path.join(SC,"ring_%s.png"%lang))
