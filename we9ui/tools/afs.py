import struct,sys,os
def entries(path):
    f=open(path,"rb"); h=f.read(8); n=struct.unpack("<I",h[4:])[0]
    t=[struct.unpack("<II",f.read(8)) for _ in range(n)]
    # tabla de nombres opcional al final
    names=None
    f.seek(t[-1][0]+t[-1][1]); 
    pos=(t[-1][0]+t[-1][1]+0x7ff)&~0x7ff
    return f,t
if __name__=="__main__":
    src,dst=sys.argv[1],sys.argv[2]
    f,t=entries(src)
    for i,(o,s) in enumerate(t):
        f.seek(o); open(os.path.join(dst,"%04d.bin"%i),"wb").write(f.read(s))
    print(len(t),"extraidos")
