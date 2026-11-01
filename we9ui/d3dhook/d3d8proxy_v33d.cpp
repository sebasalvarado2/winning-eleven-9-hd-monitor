// d3d8 proxy v3 - WE9 HD textures (sin envoltorios: parches encadenados sobre la vtable real, compatible con kitserver)
// - CreateTexture (device vt[20]): si la textura es elegible (A8R8G8B8, MANAGED, usage 0, 1 nivel, tamano permitido) se crea al SCALE x tamano.
// - LockRect/UnlockRect/GetLevelDesc/Release (texture vt[16]/[17]/[14]/[2]): el juego sigue viendo el tamano de DISENO.
//   Lock -> buffer de diseno (stage). Unlock -> huella del contenido; dump (opcional); version HD d3d8hd\hd\<huella>.tga si existe
//   (debe medir exactamente SCALE*w x SCALE*h) o reescalado por vecino mas cercano; se sube a la textura real.
// Config: <dll>\d3d8hd\d3d8hd.ini  [hd] enable dump hd scale maxarea sizes log   (sizes=* o lista WxH separada por comas)
#include <windows.h>
#include <stdio.h>
#include <stdlib.h>
#include <stdarg.h>
#include <stdint.h>
#include <string.h>
#include <unordered_map>
#include <math.h>
#pragma comment(linker,"/EXPORT:Direct3DCreate8=_Direct3DCreate8@4")

struct LR { INT Pitch; void* pBits; };
typedef HRESULT (WINAPI *CreateDevice_t)(void*,UINT,DWORD,HWND,DWORD,void*,void**);
typedef HRESULT (WINAPI *CreateTexture_t)(void*,UINT,UINT,UINT,DWORD,DWORD,DWORD,void**);
typedef ULONG   (WINAPI *Release_t)(void*);
typedef HRESULT (WINAPI *GetLevelDesc_t)(void*,UINT,void*);
typedef HRESULT (WINAPI *Lock_t)(void*,UINT,LR*,const RECT*,DWORD);
typedef HRESULT (WINAPI *Unlock_t)(void*,UINT);
typedef HRESULT (WINAPI *SetTransform_t)(void*,DWORD,const float*);
typedef HRESULT (WINAPI *SetViewport_t)(void*,const DWORD*);
typedef HRESULT (WINAPI *SetVS_t)(void*,DWORD);
typedef HRESULT (WINAPI *DPUP_t)(void*,DWORD,UINT,const void*,UINT);
static SetTransform_t o_SetTransform; static SetViewport_t o_SetViewport; static SetVS_t o_SetVS; static DPUP_t o_DPUP;
static LONG g_cST=0,g_cDP=0,g_cDPrhw=0,g_cVS=0,g_cVP=0,g_cSTproj=0; static float g_k2d=1.0f,g_k3d=1.0f; static float g_mainW=1024.0f,g_mainH=1024.0f; static DWORD g_fvf=0; static LONG g_nProj=0,g_n2d=0,g_nBg=0;

static CreateDevice_t o_CreateDevice; static CreateTexture_t o_CreateTexture;
static Release_t o_Release; static GetLevelDesc_t o_GetLevelDesc; static Lock_t o_Lock; static Unlock_t o_Unlock;

static FILE* g_log; static CRITICAL_SECTION g_cs;
static char g_dir[MAX_PATH], g_hdd[MAX_PATH];
static struct { int enable,dump,hd,scale,maxarea,log,all,smooth,dumpall,aspect,bgcover; float target3d; int nsz; int sw[64],sh[64],ss[64]; } cfg;
static LONG g_nText=0,g_nReg=0,g_nHD=0,g_nMiss=0,g_nDump=0,g_nUp=0;

static void L(const char* f,...){ if(!g_log||!cfg.log) return; EnterCriticalSection(&g_cs); va_list a; va_start(a,f); vfprintf(g_log,f,a); va_end(a); fputc('\n',g_log); fflush(g_log); LeaveCriticalSection(&g_cs); }
static void patch(void** vt,int idx,void* nf,void** orig){ DWORD old; VirtualProtect(&vt[idx],4,PAGE_READWRITE,&old); if(orig) *orig=vt[idx]; vt[idx]=nf; VirtualProtect(&vt[idx],4,old,&old); }
static void** RV(void* o){ return *(void***)o; }

static uint64_t fnv(uint64_t h,const BYTE* p,size_t n){ for(size_t i=0;i<n;i++){ h^=p[i]; h*=1099511628211ULL; } return h; }
static uint64_t hashTex(const BYTE* bits,UINT w,UINT h){ uint64_t x=1469598103934665603ULL; x=fnv(x,(const BYTE*)&w,4); x=fnv(x,(const BYTE*)&h,4); x=fnv(x,bits,(size_t)w*h*4); return x; }
static bool exists(const char* p){ return GetFileAttributesA(p)!=INVALID_FILE_ATTRIBUTES; }
static void writeTGA(const char* path,const BYTE* bits,UINT w,UINT h){ FILE* f=fopen(path,"wb"); if(!f) return; BYTE hd[18]={0}; hd[2]=2; hd[12]=w&255; hd[13]=w>>8; hd[14]=h&255; hd[15]=h>>8; hd[16]=32; hd[17]=0x28; fwrite(hd,1,18,f); fwrite(bits,1,(size_t)w*h*4,f); fclose(f); }
static BYTE* readTGA(const char* path,UINT* W,UINT* H){
  FILE* f=fopen(path,"rb"); if(!f) return 0; BYTE hd[18]; if(fread(hd,1,18,f)!=18||hd[2]!=2||hd[16]!=32){ fclose(f); return 0; }
  UINT w=hd[12]|(hd[13]<<8), h=hd[14]|(hd[15]<<8); if(!w||!h||w>4096||h>4096){ fclose(f); return 0; }
  if(hd[0]) fseek(f,hd[0],SEEK_CUR);
  BYTE* px=(BYTE*)malloc((size_t)w*h*4); if(!px){ fclose(f); return 0; }
  if(fread(px,1,(size_t)w*h*4,f)!=(size_t)w*h*4){ free(px); fclose(f); return 0; } fclose(f);
  if(!(hd[17]&0x20)){ size_t rb=(size_t)w*4; BYTE* t=(BYTE*)malloc(rb); for(UINT y=0;y<h/2;y++){ memcpy(t,px+y*rb,rb); memcpy(px+y*rb,px+(size_t)(h-1-y)*rb,rb); memcpy(px+(size_t)(h-1-y)*rb,t,rb);} free(t); }
  *W=w; *H=h; return px; }

// ---------- registro de texturas elegidas ----------
struct TE { void* tex; UINT w,h,W,H,S,usage,fmt,pool; BYTE* stage; bool processed,hdApplied,locked,passive; BYTE* pbits; INT ppitch; bool pfull; };
static std::unordered_map<void*,TE*> g_map;
static TE* getTE(void* t){ EnterCriticalSection(&g_cs); auto it=g_map.find(t); TE* r=(it==g_map.end())?0:it->second; LeaveCriticalSection(&g_cs); return r; }

static int sizeScale(UINT w,UINT h){ if((int)(w*h)>cfg.maxarea) return 0; for(int i=0;i<cfg.nsz;i++) if(cfg.sw[i]==(int)w&&cfg.sh[i]==(int)h) return cfg.ss[i]; if(cfg.all) return cfg.scale; return 0; }


// ---------- reescalado suave (Catmull-Rom, alpha premultiplicado) ----------
static void crw(float t,float* w){ float t2=t*t,t3=t2*t; w[0]=-0.5f*t3+t2-0.5f*t; w[1]=1.5f*t3-2.5f*t2+1.0f; w[2]=-1.5f*t3+2.0f*t2+0.5f*t; w[3]=0.5f*t3-0.5f*t2; }
static bool isTextTex(const BYTE* b,UINT w,UINT h){ size_t n=0; for(size_t i=0;i<(size_t)w*h;i++){ const BYTE* p=b+i*4; if(p[3]>64){ n++; if(p[0]<0xE0||p[1]<0xE0||p[2]<0xE0) return false; } } return n>0; }
static void upscaleSmooth(const BYTE* src,UINT w,UINT h,BYTE* dst,UINT S,bool text){
  UINT W=w*S,H=h*S; float* pm=(float*)malloc((size_t)w*h*4*sizeof(float)); float* tmp=(float*)malloc((size_t)W*h*4*sizeof(float)); if(!pm||!tmp){ free(pm); free(tmp); return; }
  for(size_t i=0;i<(size_t)w*h;i++){ const BYTE* p=src+i*4; float a=p[3]/255.0f; pm[i*4+0]=p[0]*a; pm[i*4+1]=p[1]*a; pm[i*4+2]=p[2]*a; pm[i*4+3]=a; }
  float* wx=(float*)malloc(W*4*sizeof(float)); int* ix=(int*)malloc(W*sizeof(int));
  for(UINT x=0;x<W;x++){ float sx=((float)x+0.5f)/S-0.5f; int i0=(int)floorf(sx); crw(sx-i0,wx+x*4); ix[x]=i0; }
  for(UINT y=0;y<h;y++) for(UINT x=0;x<W;x++){ float acc[4]={0,0,0,0}; for(int k=0;k<4;k++){ int xx=ix[x]-1+k; if(xx<0) xx=0; if(xx>=(int)w) xx=w-1; const float* q=pm+((size_t)y*w+xx)*4; float ww=wx[x*4+k]; acc[0]+=q[0]*ww; acc[1]+=q[1]*ww; acc[2]+=q[2]*ww; acc[3]+=q[3]*ww; } float* o=tmp+((size_t)y*W+x)*4; o[0]=acc[0]; o[1]=acc[1]; o[2]=acc[2]; o[3]=acc[3]; }
  float wy[4];
  for(UINT y=0;y<H;y++){ float sy=((float)y+0.5f)/S-0.5f; int j0=(int)floorf(sy); crw(sy-j0,wy);
    for(UINT x=0;x<W;x++){ float acc[4]={0,0,0,0}; for(int k=0;k<4;k++){ int yy=j0-1+k; if(yy<0) yy=0; if(yy>=(int)h) yy=h-1; const float* q=tmp+((size_t)yy*W+x)*4; acc[0]+=q[0]*wy[k]; acc[1]+=q[1]*wy[k]; acc[2]+=q[2]*wy[k]; acc[3]+=q[3]*wy[k]; }
      float a=acc[3]; if(a<0) a=0; if(a>1) a=1; BYTE* d=dst+((size_t)y*W+x)*4;
      if(text){ float a2=(a-0.5f)*2.4f+0.5f; if(a2<0) a2=0; if(a2>1) a2=1; d[0]=d[1]=d[2]=255; d[3]=(BYTE)(a2*255.0f+0.5f); }
      else { if(a>0.002f){ for(int c=0;c<3;c++){ float v=acc[c]/a; if(v<0) v=0; if(v>255) v=255; d[c]=(BYTE)(v+0.5f); } } else { d[0]=d[1]=d[2]=0; } d[3]=(BYTE)(a*255.0f+0.5f); } } }
  free(pm); free(tmp); free(wx); free(ix); }

static void upload(TE* e){
  UINT S=e->S; BYTE* hdpx=0; UINT hw=0,hh=0; uint64_t hash=0;
  if(!e->processed){ e->processed=true; hash=hashTex(e->stage,e->w,e->h);
    if(cfg.dump){ char p[MAX_PATH]; sprintf(p,"%s\\dump\\%016llx_%ux%u.tga",g_hdd,(unsigned long long)hash,e->w,e->h); if(!exists(p)){ writeTGA(p,e->stage,e->w,e->h); InterlockedIncrement(&g_nDump); } }
    if(cfg.hd){ char p[MAX_PATH]; sprintf(p,"%s\\hd\\%016llx.tga",g_hdd,(unsigned long long)hash); if(exists(p)){ hdpx=readTGA(p,&hw,&hh); if(hdpx && (hw!=e->W||hh!=e->H)){ L("HD %016llx mide %ux%u, se esperaba %ux%u: ignorada",(unsigned long long)hash,hw,hh,e->W,e->H); free(hdpx); hdpx=0; } else if(hdpx){ e->hdApplied=true; InterlockedIncrement(&g_nHD); if(g_nHD<=300) L("HD aplicada %016llx %ux%u -> %ux%u",(unsigned long long)hash,e->w,e->h,e->W,e->H); } } else InterlockedIncrement(&g_nMiss); } }
  BYTE* big=hdpx; bool own=false;
  if(!big && cfg.smooth){ big=(BYTE*)malloc((size_t)e->W*e->H*4); if(big){ bool tx=isTextTex(e->stage,e->w,e->h); upscaleSmooth(e->stage,e->w,e->h,big,S,tx); own=true; if(tx) InterlockedIncrement(&g_nText); } }
  LR lr; if(o_Lock(e->tex,0,&lr,NULL,0)!=0){ if(big) free(big); L("upload: Lock real fallo"); return; }
  for(UINT y=0;y<e->H;y++){ UINT* dst=(UINT*)((BYTE*)lr.pBits+(size_t)y*lr.Pitch);
    if(big) memcpy(dst,big+(size_t)y*e->W*4,(size_t)e->W*4);
    else { const UINT* src=(const UINT*)(e->stage+(size_t)(y/S)*e->w*4); for(UINT x=0;x<e->W;x++) dst[x]=src[x/S]; } }
  o_Unlock(e->tex,0); if(big) free(big); (void)own; InterlockedIncrement(&g_nUp); }

static HRESULT WINAPI h_Lock(void* self,UINT lvl,LR* lr,const RECT* r,DWORD fl){
  TE* e=getTE(self); if(!e||lvl!=0) return o_Lock(self,lvl,lr,r,fl);
  if(e->passive){ HRESULT hr=o_Lock(self,lvl,lr,r,fl); if(hr==0){ e->pbits=(BYTE*)lr->pBits; e->ppitch=lr->Pitch; e->pfull=(r==NULL); e->locked=true; } return hr; }
  if(!e->stage) e->stage=(BYTE*)calloc((size_t)e->w*e->h,4); if(!e->stage) return E_OUTOFMEMORY;
  INT pitch=e->w*4; BYTE* p=e->stage; if(r) p+=(size_t)r->top*pitch+(size_t)r->left*4;
  lr->Pitch=pitch; lr->pBits=p; e->locked=true; return S_OK; }
static HRESULT WINAPI h_Unlock(void* self,UINT lvl){
  TE* e=getTE(self); if(!e||lvl!=0) return o_Unlock(self,lvl);
  if(e->passive){ if(e->locked && e->pbits && e->pfull && !e->processed){ e->processed=true; BYTE* tight=(BYTE*)malloc((size_t)e->w*e->h*4); if(tight){ for(UINT y=0;y<e->h;y++) memcpy(tight+(size_t)y*e->w*4,e->pbits+(size_t)y*e->ppitch,(size_t)e->w*4);
        uint64_t hh=hashTex(tight,e->w,e->h); char p[MAX_PATH]; sprintf(p,"%s\\dump\\%016llx_%ux%u.tga",g_hdd,(unsigned long long)hh,e->w,e->h); if(!exists(p)){ writeTGA(p,tight,e->w,e->h); InterlockedIncrement(&g_nDump); } free(tight); } }
    e->locked=false; e->pbits=NULL; return o_Unlock(self,lvl); }
  if(e->locked){ e->locked=false; upload(e); } return S_OK; }
static HRESULT WINAPI h_GetLevelDesc(void* self,UINT lvl,void* desc){
  HRESULT hr=o_GetLevelDesc(self,lvl,desc); TE* e=getTE(self);
  if(hr==0 && e && !e->passive && lvl==0 && desc){ DWORD* d=(DWORD*)desc; d[4]=e->w*e->h*4; d[6]=e->w; d[7]=e->h; } return hr; }
static ULONG WINAPI h_Release(void* self){
  TE* e=getTE(self); ULONG r=o_Release(self);
  if(r==0 && e){ EnterCriticalSection(&g_cs); g_map.erase(self); LeaveCriticalSection(&g_cs); if(e->stage) free(e->stage); free(e); }
  return r; }

static bool g_texHooked=false;
static HRESULT WINAPI h_CreateTexture(void* dev,UINT w,UINT h,UINT lv,DWORD us,DWORD fmt,DWORD pool,void** out){
  bool base = cfg.enable && us==0 && fmt==21 && pool==1 && lv==1;
  UINT S = base ? (UINT)sizeScale(w,h) : 0; bool elig = S>0;
  HRESULT hr=o_CreateTexture(dev,elig?w*S:w,elig?h*S:h,lv,us,fmt,pool,out);
  if(hr==0 && out && *out){
    bool pas = (!elig && base && cfg.dump && cfg.dumpall && (int)(w*h)<=1048576);
    if(elig||pas){ TE* e=(TE*)calloc(1,sizeof(TE)); if(e){ e->tex=*out; e->w=w; e->h=h; e->S=elig?S:1; e->W=w*e->S; e->H=h*e->S; e->usage=us; e->fmt=fmt; e->pool=pool; e->passive=pas;
        EnterCriticalSection(&g_cs); g_map[*out]=e; LeaveCriticalSection(&g_cs); if(elig){ LONG n=InterlockedIncrement(&g_nReg); if(n<=40) L("REG #%ld %p %ux%u -> %ux%u",n,*out,w,h,w*S,h*S); } } }
    if(!g_texHooked){ void** vt=RV(*out);
      patch(vt,2,(void*)h_Release,(void**)&o_Release); patch(vt,14,(void*)h_GetLevelDesc,(void**)&o_GetLevelDesc);
      patch(vt,16,(void*)h_Lock,(void**)&o_Lock); patch(vt,17,(void*)h_Unlock,(void**)&o_Unlock);
      g_texHooked=true; L("texture vtable parcheada (Release/GetLevelDesc/LockRect/UnlockRect)"); } }
  return hr; }


static void modName(void* a,char* out,int n){ HMODULE hm=0; out[0]=0; if(GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,(LPCSTR)a,&hm)&&hm){ char p[MAX_PATH]; GetModuleFileNameA(hm,p,MAX_PATH); char* b=strrchr(p,'\\'); lstrcpynA(out,b?b+1:p,n); } else lstrcpynA(out,"?",n); }
// ---------- correccion de relacion de aspecto (4:3 -> pantalla ancha) ----------
static HRESULT WINAPI h_SetVS(void* dev,DWORD h){ InterlockedIncrement(&g_cVS); g_fvf=h; return o_SetVS(dev,h); }
static void updAspect(){ static LONG c=0; if(!cfg.aspect) return; if((InterlockedIncrement(&c)%300)!=1) return;
  const DWORD* pp=(const DWORD*)0x00DB4DD4; if(IsBadReadPtr(pp,8)) return; float bw=(float)pp[0], bh=(float)pp[1]; if(bw<320||bh<240||bw>16384||bh>16384) return;
  float A=bw/bh; float A3=cfg.target3d>0.1f?cfg.target3d:A; float k2=(4.0f/3.0f)/A; if(k2>1.0f) k2=1.0f; float k3=(4.0f/3.0f)/A3; if(k3>1.0f) k3=1.0f;
  if(k2!=g_k2d||k3!=g_k3d){ g_k2d=k2; g_k3d=k3; L("aspecto actualizado: backbuffer %.0fx%.0f A=%.3f k2d=%.3f k3d=%.3f",bw,bh,A,k2,k3); } }
static HRESULT WINAPI h_SetViewport(void* dev,const DWORD* vp){ updAspect(); { LONG n=InterlockedIncrement(&g_cVP); if(n%2000==0) L("contadores: SetViewport=%ld SetVS=%ld SetTransform=%ld (proy=%ld) DPUP=%ld (rhw=%ld) k2d=%.3f k3d=%.3f fvf=0x%X mainW=%.0f",n,g_cVS,g_cST,g_cSTproj,g_cDP,g_cDPrhw,g_k2d,g_k3d,g_fvf,g_mainW); } if(vp && vp[0]==0 && vp[1]==0 && vp[2]>=512 && vp[3]>=512){ g_mainW=(float)vp[2]; g_mainH=(float)vp[3]; } return o_SetViewport(dev,vp); }
static HRESULT WINAPI h_SetTransform(void* dev,DWORD st,const float* m){ InterlockedIncrement(&g_cST); if(st==3){ InterlockedIncrement(&g_cSTproj); if(m && g_cSTproj<=6) L("SetTransform(PROJ) m11=%.3f m22=%.3f ratio=%.3f k3d=%.3f",m[0],m[5],m[5]!=0?m[0]/m[5]:0,g_k3d); }
  if(cfg.aspect && st==3 && m && g_k3d!=1.0f){ float r=m[0]/m[5]; if(r<-0.74f && r>-0.76f){ float c[16]; memcpy(c,m,64); c[0]*=g_k3d; if(InterlockedIncrement(&g_nProj)<=5) L("proyeccion 4:3 corregida m11 %.3f -> %.3f",m[0],c[0]); return o_SetTransform(dev,st,c); } }
  return o_SetTransform(dev,st,m); }
static HRESULT WINAPI h_DPUP(void* dev,DWORD type,UINT pc,const void* data,UINT stride){ InterlockedIncrement(&g_cDP); if(g_fvf&4) InterlockedIncrement(&g_cDPrhw);
  if(!cfg.aspect || !(g_fvf&4) || g_k2d>=0.999f || !data || stride<16) return o_DPUP(dev,type,pc,data,stride);
  UINT nv = (type==4)?pc*3 : (type==5||type==6)?pc+2 : (type==2)?pc*2 : (type==3)?pc+1 : pc;
  if(nv==0 || nv>4096 || stride>64) return o_DPUP(dev,type,pc,data,stride);
  BYTE buf[4096*0+1]; (void)buf; BYTE* tmp=(BYTE*)malloc((size_t)nv*stride); if(!tmp) return o_DPUP(dev,type,pc,data,stride);
  memcpy(tmp,data,(size_t)nv*stride);
  float x0=1e9f,x1=-1e9f,y0=1e9f,y1=-1e9f; for(UINT i=0;i<nv;i++){ const float* v=(const float*)(tmp+(size_t)i*stride); if(v[0]<x0)x0=v[0]; if(v[0]>x1)x1=v[0]; if(v[1]<y0)y0=v[1]; if(v[1]>y1)y1=v[1]; }
  bool full = (x1-x0)>=0.9f*g_mainW && (y1-y0)>=0.9f*g_mainH;
  int toff=16; if(g_fvf&0x40) toff+=4; if(g_fvf&0x80) toff+=4; bool hasTex=((g_fvf&0xF00)>=0x100) && (toff+8<=(int)stride);
  if(full){ if(cfg.bgcover && hasTex){ for(UINT i=0;i<nv;i++){ float* t=(float*)(tmp+(size_t)i*stride+toff); t[1]=0.5f+(t[1]-0.5f)*g_k2d; } if(InterlockedIncrement(&g_nBg)<=5) L("fondo de pantalla completa recortado (cover) k=%.3f",g_k2d); } }
  else { float cx=g_mainW*0.5f; for(UINT i=0;i<nv;i++){ float* v=(float*)(tmp+(size_t)i*stride); v[0]=cx+(v[0]-cx)*g_k2d; } InterlockedIncrement(&g_n2d); }
  HRESULT hr=o_DPUP(dev,type,pc,tmp,stride); free(tmp); return hr; }


// ---------- hot-patch de cuerpos de funcion (para saltear a quien llame a la funcion original por otra via) ----------
static void* hotpatch(void* fn,void* hook){
  BYTE* f=(BYTE*)fn; if(!f||IsBadReadPtr(f-5,7)) return 0;
  if(!(f[0]==0x8B&&f[1]==0xFF)) return 0;                       // mov edi,edi
  for(int i=1;i<=5;i++) if(f[-i]!=0x90&&f[-i]!=0xCC) return 0;  // relleno previo
  DWORD old; if(!VirtualProtect(f-5,7,PAGE_EXECUTE_READWRITE,&old)) return 0;
  f[-5]=0xE9; *(DWORD*)(f-4)=(DWORD)((BYTE*)hook-(f-5)-5);
  *(WORD*)f=0xF9EB;                                              // jmp short -7 -> salta al jmp rel32
  VirtualProtect(f-5,7,old,&old); FlushInstructionCache(GetCurrentProcess(),f-5,7);
  return f+2; }
static bool g_devHooked=false;
static HRESULT WINAPI h_CreateDevice(void* d3d,UINT ad,DWORD ty,HWND hw,DWORD fl,void* pp,void** out){
  HRESULT hr=o_CreateDevice(d3d,ad,ty,hw,fl,pp,out); L("CreateDevice hr=0x%X dev=%p",hr,out?*out:0);
  if(hr==0 && out && *out && !g_devHooked){ void** dv=RV(*out); patch(dv,20,(void*)h_CreateTexture,(void**)&o_CreateTexture); g_devHooked=true; L("device vtable parcheada (CreateTexture)");
    if(cfg.aspect){ const DWORD* ppp=(const DWORD*)pp; float bw=ppp?(float)ppp[0]:0, bh=ppp?(float)ppp[1]:0; float A=(bw>0&&bh>0)?bw/bh:(16.0f/9.0f);
      float A3=cfg.target3d>0.1f?cfg.target3d:A; g_k2d=(4.0f/3.0f)/A; if(g_k2d>1.0f) g_k2d=1.0f; g_k3d=(4.0f/3.0f)/A3; if(g_k3d>1.0f) g_k3d=1.0f;
      struct { int idx; void* hook; void** orig; const char* nm; } hk[4]={ {37,(void*)h_SetTransform,(void**)&o_SetTransform,"SetTransform"},{40,(void*)h_SetViewport,(void**)&o_SetViewport,"SetViewport"},{72,(void*)h_DPUP,(void**)&o_DPUP,"DrawPrimitiveUP"},{76,(void*)h_SetVS,(void**)&o_SetVS,"SetVertexShader"} };
      for(int i=0;i<4;i++){ void* fn=dv[hk[i].idx]; char m[64]; modName(fn,m,64); void* tr=hotpatch(fn,hk[i].hook);
        if(tr){ *hk[i].orig=tr; L("hotpatch %s OK en %p (%s)",hk[i].nm,fn,m); } else { patch(dv,hk[i].idx,hk[i].hook,hk[i].orig); L("hotpatch %s NO posible (%p en %s): parche de vtable",hk[i].nm,fn,m); } }
      L("aspecto activo: backbuffer %.0fx%.0f A=%.3f k2d=%.3f k3d=%.3f bgcover=%d",bw,bh,A,g_k2d,g_k3d,cfg.bgcover); } }
  return hr; }

extern "C" void* WINAPI Direct3DCreate8(UINT ver){
  static HMODULE real=0; if(!real){ char p[MAX_PATH]; GetSystemDirectoryA(p,MAX_PATH); lstrcatA(p,"\\d3d8.dll"); real=LoadLibraryA(p); }
  typedef void* (WINAPI *C8)(UINT); C8 c=(C8)GetProcAddress(real,"Direct3DCreate8");
  void* d3d=c?c(ver):0; L("Direct3DCreate8(%u) -> %p",ver,d3d);
  if(d3d){ static bool done=false; if(!done){ patch(RV(d3d),15,(void*)h_CreateDevice,(void**)&o_CreateDevice); done=true; L("IDirect3D8 vtable parcheada (CreateDevice)"); } }
  return d3d; }

static void parseSizes(const char* s){ cfg.nsz=0; cfg.all=0; if(!s||!*s) return; if(s[0]=='*'){ cfg.all=1; return; }
  const char* p=s; while(*p && cfg.nsz<64){ int w=0,h=0,sc=0,n=0; if(sscanf(p,"%dx%d%n",&w,&h,&n)>=2){ p+=n; if(*p==':'){ p++; sc=atoi(p); while(*p>='0'&&*p<='9') p++; } if(sc<1||sc>4) sc=cfg.scale; cfg.sw[cfg.nsz]=w; cfg.sh[cfg.nsz]=h; cfg.ss[cfg.nsz]=sc; cfg.nsz++; } else break; while(*p==','||*p==' ') p++; } }

BOOL WINAPI DllMain(HINSTANCE h,DWORD r,LPVOID){
  if(r==DLL_PROCESS_ATTACH){
    DisableThreadLibraryCalls(h); InitializeCriticalSection(&g_cs);
    GetModuleFileNameA(h,g_dir,MAX_PATH); char* s=strrchr(g_dir,'\\'); if(s) *s=0;
    sprintf(g_hdd,"%s\\d3d8hd",g_dir); CreateDirectoryA(g_hdd,0); char t[MAX_PATH];
    sprintf(t,"%s\\dump",g_hdd); CreateDirectoryA(t,0); sprintf(t,"%s\\hd",g_hdd); CreateDirectoryA(t,0);
    char ini[MAX_PATH]; sprintf(ini,"%s\\d3d8hd.ini",g_hdd);
    cfg.enable=GetPrivateProfileIntA("hd","enable",1,ini); cfg.dump=GetPrivateProfileIntA("hd","dump",0,ini); cfg.hd=GetPrivateProfileIntA("hd","hd",1,ini);
    cfg.scale=GetPrivateProfileIntA("hd","scale",4,ini); if(cfg.scale<1||cfg.scale>4) cfg.scale=4;
    cfg.smooth=GetPrivateProfileIntA("hd","smooth",1,ini); cfg.aspect=GetPrivateProfileIntA("aspect","enable",0,ini); cfg.bgcover=GetPrivateProfileIntA("aspect","bgcover",1,ini); { char tb[32]; GetPrivateProfileStringA("aspect","target3d","0",tb,32,ini); cfg.target3d=(float)atof(tb); } cfg.dumpall=GetPrivateProfileIntA("hd","dumpall",0,ini); cfg.maxarea=GetPrivateProfileIntA("hd","maxarea",262144,ini); cfg.log=GetPrivateProfileIntA("hd","log",1,ini);
    char sz[512]; GetPrivateProfileStringA("hd","sizes","128x128,64x64,32x32",sz,512,ini); parseSizes(sz);
    sprintf(t,"%s\\d3d8hd.log",g_hdd); g_log=fopen(t,"w");
    L("d3d8 proxy v3 cargado enable=%d dump=%d hd=%d scale=%d maxarea=%d sizes=%s (%d tamanos, all=%d)",cfg.enable,cfg.dump,cfg.hd,cfg.scale,cfg.maxarea,sz,cfg.nsz,cfg.all); }
  else if(r==DLL_PROCESS_DETACH){ L("fin: registradas=%ld subidas=%ld hd=%ld sin_hd=%ld volcadas=%ld texto=%ld | aspecto: proy=%ld 2d=%ld bg=%ld",g_nReg,g_nUp,g_nHD,g_nMiss,g_nDump,g_nText,g_nProj,g_n2d,g_nBg); if(g_log) fclose(g_log); }
  return TRUE; }
