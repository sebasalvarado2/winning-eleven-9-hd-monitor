import sys,random
sys.path.insert(0,r"E:\WE9_UI\tools")
import numpy as np
from psmt4_search import load
from dump_e import tv
from PIL import Image
def run(path,k,out,iters=4000,seed=3):
    w,h,pal,nib=load(path,k); y,x=np.mgrid[0:h,0:w]; lw=w.bit_length()-1; lh=h.bit_length()-1; nb=lw+lh
    planes=[((x>>i)&1).astype(np.int64) for i in range(lw)]+[((y>>i)&1).astype(np.int64) for i in range(lh)]
    X=lambda i:i; Y=lambda i:lw+i
    # fuente teorica: lista de conjuntos de coordenadas que se XORean en cada bit de direccion
    start=[{Y(1)},{X(3)},{X(4)},{X(0)},{X(1)},{X(2),Y(2),Y(1)},{X(5)},{X(6)},{X(7)},{Y(0)},{Y(2)},{Y(3)}]+[{Y(i)} for i in range(4,lh)]
    start=start[:nb]
    def addr(S):
        N=np.zeros((h,w),np.int64)
        for i,s in enumerate(S):
            b=np.zeros((h,w),np.int64)
            for c in s: b^=planes[c]
            N|=b<<i
        return N
    def score(S):
        N=addr(S)
        if np.bincount(N.ravel(),minlength=w*h).max()>1: return 1e18
        return tv(pal[nib[N]])
    cur=score(start); print("teorico:",cur,flush=True); S=[set(s) for s in start]; rnd=random.Random(seed)
    for it in range(iters):
        T=[set(s) for s in S]; m=rnd.random()
        if m<0.4: i,j=rnd.sample(range(nb),2); T[i],T[j]=T[j],T[i]
        elif m<0.8:
            i=rnd.randrange(nb); c=rnd.randrange(nb)
            if c in T[i]: 
                if len(T[i])>1: T[i].discard(c)
            else: T[i].add(c)
        else:
            i=rnd.randrange(nb); T[i]={rnd.randrange(nb)}
        s=score(T)
        if s<cur: S,cur=T,s
    print("final:",cur,[sorted(s) for s in S])
    a=pal[nib[addr(S)]].copy(); a[...,3]=np.minimum(255,a[...,3].astype(int)*2)
    im=Image.fromarray(a.astype(np.uint8),"RGBA"); bg=Image.new("RGB",im.size,(90,140,90)); bg.paste(im,mask=im.split()[3]); bg.save(out)
    return S,cur
if __name__=="__main__": run(sys.argv[1],int(sys.argv[2]),sys.argv[3])
