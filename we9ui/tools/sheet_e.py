import os,sys
from PIL import Image,ImageDraw
src="png_e"
def sheet(sel,out,T=200,C=6):
    R=(len(sel)+C-1)//C; sh=Image.new("RGB",(C*T,R*(T+14)),(60,60,60)); d=ImageDraw.Draw(sh)
    for n,f in enumerate(sel):
        im=Image.open(f"{src}/{f}"); im.thumbnail((T-4,T-4)); bg=Image.new("RGB",im.size,(90,140,90)); bg.paste(im,mask=im.split()[3])
        x,y=(n%C)*T,(n//C)*(T+14); sh.paste(bg,(x+2,y+14)); d.text((x+2,y),f[:-4][:30]+" %dx%d"%Image.open(f"{src}/{f}").size,fill=(255,255,0))
    sh.save(out); print(out,len(sel))
if __name__=="__main__":
    ids=sys.argv[2].split(","); fs=[f for f in sorted(os.listdir(src)) if f[:3] in ids]
    sheet(fs[:int(sys.argv[3]) if len(sys.argv)>3 else 36],sys.argv[1])
