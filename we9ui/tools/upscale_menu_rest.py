import sys,struct,os
sys.path.insert(0,r"E:\WE9_UI\tools")
import numpy as np
from epack import *
from afs import entries
from PIL import Image,ImageFilter
DST=r"E:\KONAMI\Winning Eleven 9\kitserver\hd_menus"
def fixhdr(b,w,h):
    b=bytearray(b); struct.pack_into("<I",b,8,0x480+w*h); struct.pack_into("<I",b,0x20,w*h//16)
    struct.pack_into("<HH",b,0x28,w//2,h//2)
    if w>=128 or h>=128: struct.pack_into("<HH",b,0x2c,max(1,w//128),max(1,w//128)*max(1,h//128)*2)
    else: struct.pack_into("<HH",b,0x2c,1,1)
    return bytes(b)
def up2(img,sharp=True):
    a=np.asarray(img.convert("RGBA")).astype(np.float32); al=a[...,3:4]/255
    pm=Image.fromarray(np.concatenate([a[...,:3]*al,a[...,3:4]],-1).astype(np.uint8),"RGBA")
    big=np.asarray(pm.resize((img.width*2,img.height*2),Image.LANCZOS)).astype(np.float32)
    al2=np.maximum(big[...,3:4]/255,1e-3); rgb=np.clip(big[...,:3]/al2,0,255)
    rgbi=Image.fromarray(rgb.astype(np.uint8),"RGB")
    if sharp: rgbi=rgbi.filter(ImageFilter.UnsharpMask(radius=1.6,percent=45,threshold=2))
    out=np.concatenate([np.asarray(rgbi),big[...,3:4].astype(np.uint8)],-1)
    return Image.fromarray(out,"RGBA")
def encode_opaque(img,template,sw,ps):
    w,h=img.size; rgb=img.convert("RGB")
    pq=rgb.quantize(colors=256,method=Image.Quantize.MEDIANCUT,dither=Image.Dither.NONE)
    q=rgb.quantize(palette=pq,dither=Image.Dither.FLOYDSTEINBERG)
    pal=np.zeros((256,4),np.uint8); p=np.array(q.getpalette()[:768],np.uint8).reshape(-1,3); pal[:len(p),:3]=p; pal[:,3]=128
    idx=np.asarray(q); P=palswz(pal) if ps else pal; px=swz8(idx,w,h) if sw else idx.ravel().astype(np.uint8)
    hdr=bytearray(template[:0x80]); struct.pack_into("<HH",hdr,0x14,w,h); hdr[0x1a]=w.bit_length()-1; hdr[0x1b]=h.bit_length()-1
    return bytes(hdr)+P.tobytes()+px.tobytes()
def encode_alpha(img,template,sw,ps):
    w,h=img.size; A=np.asarray(img.convert("RGBA")); rgb=Image.fromarray(A[...,:3],"RGB"); a=A[...,3]
    op=a>=250; tr=a<8; ed=~op&~tr
    NOP=199; LV=14; NC=4
    pq=Image.fromarray(A[...,:3][op].reshape(1,-1,3),"RGB").quantize(colors=NOP,method=Image.Quantize.MEDIANCUT,dither=Image.Dither.NONE)
    q=rgb.quantize(palette=pq,dither=Image.Dither.FLOYDSTEINBERG); idx=np.asarray(q).astype(np.int32).copy()
    pal=np.zeros((256,4),np.uint8); p=np.array(pq.getpalette()[:3*NOP],np.uint8).reshape(-1,3); pal[:len(p),:3]=p; pal[:NOP,3]=128
    idx[tr]=NOP; pal[NOP]=(0,0,0,0)
    levels=np.linspace(20,240,LV)
    if ed.any():
        eq=Image.fromarray(A[...,:3][ed].reshape(1,-1,3),"RGB").quantize(colors=NC,method=Image.Quantize.MEDIANCUT,dither=Image.Dither.NONE)
        ep=np.array(eq.getpalette()[:3*NC],np.uint8).reshape(-1,3); ci=np.asarray(eq).reshape(-1).astype(np.int32)
        li=np.abs(a[ed][:,None].astype(float)-levels[None,:]).argmin(1)
        idx[ed]=NOP+1+ci*LV+li
        for c in range(len(ep)):
            for l in range(LV): pal[NOP+1+c*LV+l]=(*ep[c],int(levels[l])//2)
    idx=idx.astype(np.uint8)
    P=palswz(pal) if ps else pal; px=swz8(idx,w,h) if sw else idx.ravel().astype(np.uint8)
    hdr=bytearray(template[:0x80]); struct.pack_into("<HH",hdr,0x14,w,h); hdr[0x1a]=w.bit_length()-1; hdr[0x1b]=h.bit_length()-1
    return bytes(hdr)+P.tobytes()+px.tobytes()
def process(d,k):
    u=get_sub(d,k); im,sw,ps=best_variant(u); big=up2(im)
    a=np.asarray(im)[...,3]
    nu=encode_opaque(big,u,sw,ps) if a.min()==255 else encode_alpha(big,u,sw,ps)
    nu=fixhdr(nu,big.width,big.height); return rebuild(d,k,nu),big
if __name__=="__main__":
    SC=sys.argv[1]
    for lang,name in(("e","e_text.afs"),("s","S_TEXT.AFS")):
        if lang=="s": f,t=entries(r"E:\KONAMI\Winning Eleven 9\dat\S_TEXT.AFS")
        def orig(e):
            if lang=="e": return open(r"E:\WE9_UI\e_text\%04d.bin"%e,"rb").read()
            o,s=t[e]; f.seek(o); return f.read(s)
        d251=orig(251)
        for k in range(4): d251,_=process(d251,k)
        d253=open(os.path.join(DST,name,"menu_253.bin"),"rb").read()   # ya trae el texto HD (recurso 24)
        for k in (20,21,25): d253,_=process(d253,k)
        open(os.path.join(DST,name,"menu_251.bin"),"wb").write(d251); open(os.path.join(DST,name,"menu_253.bin"),"wb").write(d253)
        print(name,"251:",len(d251),"253:",len(d253))
        for e,dd,ks in((251,d251,range(4)),(253,d253,(20,21,24,25))):
            for k in ks:
                im2,_,_=best_variant(get_sub(dd,k)); bg=Image.new("RGB",im2.size,(90,140,90)); bg.paste(im2,mask=im2.split()[3]); bg.save(os.path.join(SC,"o_%s_%d_%d.png"%(lang,e,k)))
