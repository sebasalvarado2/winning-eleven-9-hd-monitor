import sys,struct,random,os
sys.path.insert(0,r"E:\WE9_UI\tools")
import numpy as np
from epack import *
from dump_e import tv
def load(path,k):
    d=open(path,"rb").read(); u=get_sub(d,k); w,h=struct.unpack("<HH",u[0x14:0x18])
    pal=np.frombuffer(u[0x40:0x80],np.uint8).reshape(16,4); data=np.frombuffer(u[0x80:0x80+w*h//2],np.uint8)
    nib=np.empty(w*h,np.uint8); nib[0::2]=data&15; nib[1::2]=data>>4   # nibble stream en orden de direccion
    return w,h,pal,nib
def search(w,h,pal,nib,iters=6000,restarts=6,seed=1):
    rnd=random.Random(seed)
    y,x=np.mgrid[0:h,0:w]; lw=w.bit_length()-1; lh=h.bit_length()-1; nb=lw+lh
    planes=[((x>>i)&1).astype(np.int64) for i in range(lw)]+[((y>>i)&1).astype(np.int64) for i in range(lh)]
    def addr(perm,xr):
        N=np.zeros((h,w),np.int64)
        for i in range(nb):
            b=planes[perm[i]]
            if xr[i]>=0: b=b^planes[xr[i]]
            N|=b<<i
        return N
    def score(perm,xr):
        N=addr(perm,xr)
        if np.bincount(N.ravel(),minlength=w*h).max()>1: return 1e18   # debe ser biyeccion
        return tv(pal[nib[N]])
    best=None
    for r in range(restarts):
        perm=list(range(nb)); xr=[-1]*nb
        if r>0:
            for _ in range(40): i,j=rnd.sample(range(nb),2); perm[i],perm[j]=perm[j],perm[i]
        cur=score(perm,xr)
        for it in range(iters):
            p2=perm[:]; x2=xr[:]
            m=rnd.random()
            if m<0.5: i,j=rnd.sample(range(nb),2); p2[i],p2[j]=p2[j],p2[i]
            elif m<0.8: i=rnd.randrange(nb); x2[i]=rnd.choice([-1]+[c for c in range(nb) if c!=p2[i]])
            else:
                i,j=rnd.sample(range(nb),2); p2[i],p2[j]=p2[j],p2[i]; k=rnd.randrange(nb); x2[k]=rnd.choice([-1]+[c for c in range(nb) if c!=p2[k]])
            s=score(p2,x2)
            if s<cur: perm,xr,cur=p2,x2,s
        print(" restart",r,"TV",cur,flush=True)
        if best is None or cur<best[0]: best=(cur,perm[:],xr[:])
    return best,addr
if __name__=="__main__":
    path,k,out=sys.argv[1],int(sys.argv[2]),sys.argv[3]
    w,h,pal,nib=load(path,k)
    (s,perm,xr),addr=search(w,h,pal,nib)
    print("MEJOR",s,"perm",perm,"xr",xr)
    from PIL import Image
    a=pal[nib[addr(perm,xr)]].copy(); a[...,3]=np.minimum(255,a[...,3].astype(int)*2)
    im=Image.fromarray(a.astype(np.uint8),"RGBA"); bg=Image.new("RGB",im.size,(90,140,90)); bg.paste(im,mask=im.split()[3]); bg.save(out)
