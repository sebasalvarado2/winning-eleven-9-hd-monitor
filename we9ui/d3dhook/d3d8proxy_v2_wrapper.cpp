// d3d8 proxy v2 - WE9 HD textures.
// - envuelve las texturas A8R8G8B8 / MANAGED / 1 nivel que crea el juego (wrapper COM con la interfaz IDirect3DTexture8)
// - al primer UnlockRect completo calcula una huella (FNV-1a 64) del contenido original;
//     * modo dump: guarda la textura original en d3d8hd\dump\<huella>_<w>x<h>.tga
//     * modo hd:   si existe d3d8hd\hd\<huella>.tga lo carga y reemplaza la textura real por esa version (cualquier tamano)
// - el juego sigue viendo el tamano de DISENO (GetLevelDesc se falsea), asi sus UV normalizados siguen valiendo.
// Config: <carpeta de la dll>\d3d8hd\d3d8hd.ini  [hd] enable=1 dump=0 hd=1 minside=16 maxarea=262144 log=1
#include <windows.h>
#include <stdio.h>
#include <stdlib.h>
#include <stdarg.h>
#include <stdint.h>
#include <intrin.h>
#pragma comment(linker,"/EXPORT:Direct3DCreate8=_Direct3DCreate8@4")

struct LR { INT Pitch; void* pBits; };
typedef HRESULT (WINAPI *CreateDevice_t)(void*,UINT,DWORD,HWND,DWORD,void*,void**);
typedef HRESULT (WINAPI *CreateTexture_t)(void*,UINT,UINT,UINT,DWORD,DWORD,DWORD,void**);
typedef HRESULT (WINAPI *SetTexture_t)(void*,DWORD,void*);
typedef HRESULT (WINAPI *UpdateTexture_t)(void*,void*,void*);

static CreateDevice_t   o_CreateDevice;
static CreateTexture_t  o_CreateTexture;
static SetTexture_t     o_SetTexture;
static UpdateTexture_t  o_UpdateTexture;

static FILE* g_log; static CRITICAL_SECTION g_cs;
static char g_dir[MAX_PATH];       // carpeta de la dll
static char g_hdd[MAX_PATH];       // carpeta d3d8hd
static struct { int enable, dump, hd, minside, maxarea, log; } cfg;
static LONG g_nWrap=0, g_nHD=0, g_nMiss=0, g_nDump=0, g_nSurf=0;

static void L(const char* f,...){ if(!g_log||!cfg.log) return; EnterCriticalSection(&g_cs); va_list a; va_start(a,f); vfprintf(g_log,f,a); va_end(a); fputc('\n',g_log); fflush(g_log); LeaveCriticalSection(&g_cs); }
static void patch(void** vt,int idx,void* nf,void** orig){ DWORD old; VirtualProtect(&vt[idx],4,PAGE_READWRITE,&old); if(orig) *orig=vt[idx]; vt[idx]=nf; VirtualProtect(&vt[idx],4,old,&old); }
static void** RV(void* o){ return *(void***)o; }
static LONG g_dbg=0;
static void* g_rvt[19]; static bool g_rvtSet=false;
static void modName(void* a,char* out,int n){ HMODULE hm=0; out[0]=0; if(GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,(LPCSTR)a,&hm)&&hm){ char p[MAX_PATH]; GetModuleFileNameA(hm,p,MAX_PATH); char* b=strrchr(p,'\\'); lstrcpynA(out,b?b+1:p,n); } else lstrcpynA(out,"?",n); }
static bool okReal(void* r,int idx){ if(!r||IsBadReadPtr(r,4)) return false; return g_rvtSet && g_rvt[idx] && !IsBadReadPtr(g_rvt[idx],1); }

// ---------------- utilidades de archivo / huella ----------------
static uint64_t fnv(uint64_t h,const BYTE* p,size_t n){ for(size_t i=0;i<n;i++){ h^=p[i]; h*=1099511628211ULL; } return h; }
static uint64_t hashTex(const BYTE* bits,INT pitch,UINT w,UINT h){
  uint64_t x=1469598103934665603ULL; x=fnv(x,(const BYTE*)&w,4); x=fnv(x,(const BYTE*)&h,4);
  for(UINT y=0;y<h;y++) x=fnv(x,bits+(size_t)y*pitch,(size_t)w*4); return x; }
static bool exists(const char* p){ return GetFileAttributesA(p)!=INVALID_FILE_ATTRIBUTES; }
static void writeTGA(const char* path,const BYTE* bits,INT pitch,UINT w,UINT h){
  FILE* f=fopen(path,"wb"); if(!f) return; BYTE hd[18]={0}; hd[2]=2; hd[12]=w&255; hd[13]=w>>8; hd[14]=h&255; hd[15]=h>>8; hd[16]=32; hd[17]=0x28; fwrite(hd,1,18,f);
  for(UINT y=0;y<h;y++) fwrite(bits+(size_t)y*pitch,1,(size_t)w*4,f); fclose(f); }
static BYTE* readTGA(const char* path,UINT* W,UINT* H){
  FILE* f=fopen(path,"rb"); if(!f) return 0; BYTE hd[18]; if(fread(hd,1,18,f)!=18||hd[2]!=2||hd[16]!=32){ fclose(f); return 0; }
  UINT w=hd[12]|(hd[13]<<8), h=hd[14]|(hd[15]<<8); if(!w||!h||w>2048||h>2048){ fclose(f); return 0; }
  if(hd[0]) fseek(f,hd[0],SEEK_CUR);
  BYTE* px=(BYTE*)malloc((size_t)w*h*4); if(!px){ fclose(f); return 0; }
  if(fread(px,1,(size_t)w*h*4,f)!=(size_t)w*h*4){ free(px); fclose(f); return 0; } fclose(f);
  if(!(hd[17]&0x20)){ // origen abajo-izq: voltear
    size_t rb=(size_t)w*4; BYTE* t=(BYTE*)malloc(rb); for(UINT y=0;y<h/2;y++){ memcpy(t,px+y*rb,rb); memcpy(px+y*rb,px+(size_t)(h-1-y)*rb,rb); memcpy(px+(size_t)(h-1-y)*rb,t,rb);} free(t); }
  *W=w; *H=h; return px; }

// ---------------- wrapper de textura ----------------
struct WTex { void** vptr; LONG ref; void* real; void* dev; UINT w,h,usage,fmt,pool; INT pitch; BYTE* bits; bool fullLock; bool processed; bool hdApplied; UINT hdW,hdH; BYTE* stage; };
static void* g_wvt[19];
static inline bool isW(void* p){ return p && *(void***)p==(void**)g_wvt; }
#define REAL(t) ((t)->real)

static HRESULT WINAPI W_QI(WTex* t,const void* riid,void** ppv){ if(!ppv) return E_POINTER; *ppv=t; InterlockedIncrement(&t->ref); return S_OK; }
static ULONG WINAPI W_AddRef(WTex* t){ return InterlockedIncrement(&t->ref); }
static ULONG WINAPI W_Release(WTex* t){ if(!isW(t)){ typedef ULONG (WINAPI*R)(void*); return ((R)g_rvt[2])((void*)t); } LONG r=InterlockedDecrement(&t->ref); if(InterlockedIncrement(&g_dbg)<400) L("W_Release t=%p ref=%ld real=%p",t,r,t->real); if(r==0){ typedef ULONG (WINAPI*R)(void*); ((R)g_rvt[2])(t->real); if(t->stage) free(t->stage); HeapFree(GetProcessHeap(),0,t); } return r; }
static HRESULT WINAPI W_GetDevice(WTex* t,void** d){ typedef HRESULT (WINAPI*F)(void*,void**); return ((F)g_rvt[3])(t->real,d); }
static HRESULT WINAPI W_SetPrivateData(WTex* t,const void* g,const void* d,DWORD s,DWORD f){ typedef HRESULT (WINAPI*F)(void*,const void*,const void*,DWORD,DWORD); return ((F)g_rvt[4])(t->real,g,d,s,f); }
static HRESULT WINAPI W_GetPrivateData(WTex* t,const void* g,void* d,DWORD* s){ typedef HRESULT (WINAPI*F)(void*,const void*,void*,DWORD*); return ((F)g_rvt[5])(t->real,g,d,s); }
static HRESULT WINAPI W_FreePrivateData(WTex* t,const void* g){ typedef HRESULT (WINAPI*F)(void*,const void*); return ((F)g_rvt[6])(t->real,g); }
static DWORD   WINAPI W_SetPriority(WTex* t,DWORD p){ typedef DWORD (WINAPI*F)(void*,DWORD); return ((F)g_rvt[7])(t->real,p); }
static DWORD   WINAPI W_GetPriority(WTex* t){ typedef DWORD (WINAPI*F)(void*); return ((F)g_rvt[8])(t->real); }
static void    WINAPI W_PreLoad(WTex* t){ typedef void (WINAPI*F)(void*); ((F)g_rvt[9])(t->real); }
static DWORD   WINAPI W_GetType(WTex* t){ typedef DWORD (WINAPI*F)(void*); return ((F)g_rvt[10])(t->real); }
static DWORD   WINAPI W_SetLOD(WTex* t,DWORD l){ typedef DWORD (WINAPI*F)(void*,DWORD); return ((F)g_rvt[11])(t->real,l); }
static DWORD   WINAPI W_GetLOD(WTex* t){ typedef DWORD (WINAPI*F)(void*); return ((F)g_rvt[12])(t->real); }
static DWORD   WINAPI W_GetLevelCount(WTex* t){ typedef DWORD (WINAPI*F)(void*); return ((F)g_rvt[13])(t->real); }
static HRESULT WINAPI W_GetLevelDesc(WTex* t,UINT lvl,void* desc){
  if(!isW(t)){ typedef HRESULT (WINAPI*F0)(void*,UINT,void*); return ((F0)g_rvt[14])((void*)t,lvl,desc); }
  typedef HRESULT (WINAPI*F)(void*,UINT,void*); HRESULT hr=((F)g_rvt[14])(t->real,lvl,desc);
  if(hr==0 && lvl==0 && t->hdApplied && desc){ DWORD* d=(DWORD*)desc; d[4]=t->w*t->h*4; d[6]=t->w; d[7]=t->h; } return hr; }
static HRESULT WINAPI W_GetSurfaceLevel(WTex* t,UINT lvl,void** s){ if(!isW(t)){ typedef HRESULT (WINAPI*F0)(void*,UINT,void**); return ((F0)g_rvt[15])((void*)t,lvl,s); } if(InterlockedIncrement(&g_nSurf)<=5) L("AVISO: GetSurfaceLevel sobre textura envuelta %ux%u (hd=%d)",t->w,t->h,t->hdApplied); typedef HRESULT (WINAPI*F)(void*,UINT,void**); return ((F)g_rvt[15])(t->real,lvl,s); }
static HRESULT WINAPI W_AddDirtyRect(WTex* t,const RECT* r){ typedef HRESULT (WINAPI*F)(void*,const RECT*); return ((F)g_rvt[18])(t->real,r); }

static HRESULT WINAPI W_LockRect(WTex* t,UINT lvl,LR* lr,const RECT* r,DWORD fl){
  if(!isW(t)){ static LONG n=0; if(InterlockedIncrement(&n)<=20){ char m[64]; modName(_ReturnAddress(),m,64); L("W_LockRect con this NO envuelto %p vptr=%p desde %s",t,*(void**)t,m);} typedef HRESULT (WINAPI*F0)(void*,UINT,LR*,const RECT*,DWORD); return ((F0)g_rvt[16])((void*)t,lvl,lr,r,fl); }
  if(lvl==0 && t->hdApplied){   // ya reemplazada por HD: el juego escribe en un buffer de tamano de diseno, reescalamos al desbloquear
    if(!t->stage) t->stage=(BYTE*)calloc((size_t)t->w*t->h,4);
    lr->Pitch=t->w*4; lr->pBits=t->stage; t->bits=t->stage; t->pitch=t->w*4; t->fullLock=(r==NULL); return S_OK; }
  if(!okReal(t->real,16)){ L("ERROR LockRect: real invalido t=%p real=%p",t,t->real); return E_FAIL; }
  typedef HRESULT (WINAPI*F)(void*,UINT,LR*,const RECT*,DWORD); HRESULT hr=((F)g_rvt[16])(t->real,lvl,lr,r,fl);
  if(InterlockedIncrement(&g_dbg)<400) L("W_LockRect t=%p real=%p lvl=%u rect=%s hr=0x%X bits=%p pitch=%d",t,t->real,lvl,r?"sub":"full",hr,hr==0?lr->pBits:0,hr==0?lr->Pitch:0);
  if(hr==0 && lvl==0){ t->bits=(BYTE*)lr->pBits; t->pitch=lr->Pitch; t->fullLock=(r==NULL); } return hr; }

static void applyHD(WTex* t,uint64_t h);
static HRESULT WINAPI W_UnlockRect(WTex* t,UINT lvl){
  if(!isW(t)){ static LONG n=0; if(InterlockedIncrement(&n)<=20){ char m[64]; modName(_ReturnAddress(),m,64); L("W_UnlockRect con this NO envuelto %p vptr=%p desde %s",t,*(void**)t,m);} typedef HRESULT (WINAPI*F0)(void*,UINT); return ((F0)g_rvt[17])((void*)t,lvl); }
  typedef HRESULT (WINAPI*F)(void*,UINT);
  if(InterlockedIncrement(&g_dbg)<400) L("W_UnlockRect t=%p real=%p lvl=%u bits=%p",t,t->real,lvl,t->bits);
  if(!okReal(t->real,17)){ L("ERROR UnlockRect: real invalido t=%p real=%p ref=%ld",t,t->real,t->ref); t->bits=NULL; return E_FAIL; }
  if(lvl!=0 || !t->bits) return ((F)g_rvt[17])(t->real,lvl);
  BYTE* bits=t->bits; INT pitch=t->pitch; bool full=t->fullLock; t->bits=NULL;
  if(t->hdApplied && bits==t->stage){   // reescalado por vecino mas cercano al tamano HD (sin perder el contenido nuevo)
    LR lr; typedef HRESULT (WINAPI*LK)(void*,UINT,LR*,const RECT*,DWORD); if(((LK)g_rvt[16])(t->real,0,&lr,NULL,0)==0){
      UINT sx=t->hdW/t->w, sy=t->hdH/t->h; if(!sx) sx=1; if(!sy) sy=1;
      for(UINT y=0;y<t->hdH;y++){ UINT* dst=(UINT*)((BYTE*)lr.pBits+(size_t)y*lr.Pitch); const UINT* src=(const UINT*)(bits+(size_t)(y/sy)*pitch); for(UINT x=0;x<t->hdW;x++) dst[x]=src[x/sx]; }
      ((F)g_rvt[17])(t->real,0); }
    return S_OK; }
  uint64_t hash=0; bool first=(!t->processed && full && cfg.enable);
  if(first){ hash=hashTex(bits,pitch,t->w,t->h);
    if(cfg.dump){ char p[MAX_PATH]; sprintf(p,"%s\\dump\\%016llx_%ux%u.tga",g_hdd,(unsigned long long)hash,t->w,t->h); if(!exists(p)){ writeTGA(p,bits,pitch,t->w,t->h); InterlockedIncrement(&g_nDump); } } }
  if(!okReal(t->real,17)){ L("ERROR UnlockRect(2): real invalido t=%p real=%p",t,t->real); return E_FAIL; }
  HRESULT hr=((F)g_rvt[17])(t->real,0);
  if(first){ t->processed=true; if(cfg.hd) applyHD(t,hash); }
  return hr; }

static void applyHD(WTex* t,uint64_t h){
  char p[MAX_PATH]; sprintf(p,"%s\\hd\\%016llx.tga",g_hdd,(unsigned long long)h); if(!exists(p)){ InterlockedIncrement(&g_nMiss); return; }
  UINT W,H; BYTE* px=readTGA(p,&W,&H); if(!px){ L("HD ilegible: %s",p); return; }
  void* nt=0; HRESULT hr=o_CreateTexture(t->dev,W,H,1,t->usage,t->fmt,t->pool,&nt);
  if(hr!=0||!nt){ L("HD: CreateTexture %ux%u fallo hr=0x%X",W,H,hr); free(px); return; }
  LR lr; typedef HRESULT (WINAPI*LK)(void*,UINT,LR*,const RECT*,DWORD); typedef HRESULT (WINAPI*UK)(void*,UINT);
  if(((LK)g_rvt[16])(nt,0,&lr,NULL,0)!=0){ typedef ULONG (WINAPI*R)(void*); ((R)g_rvt[2])(nt); free(px); return; }
  for(UINT y=0;y<H;y++) memcpy((BYTE*)lr.pBits+(size_t)y*lr.Pitch,px+(size_t)y*W*4,(size_t)W*4);
  ((UK)g_rvt[17])(nt,0); free(px);
  void* old=t->real; t->real=nt; t->hdApplied=true; t->hdW=W; t->hdH=H; typedef ULONG (WINAPI*R)(void*); ((R)g_rvt[2])(old);
  InterlockedIncrement(&g_nHD); if(g_nHD<=300) L("HD aplicada %016llx  %ux%u -> %ux%u",(unsigned long long)h,t->w,t->h,W,H); }

// ---------------- hooks del dispositivo ----------------
static HRESULT WINAPI h_CreateTexture(void* dev,UINT w,UINT h,UINT lv,DWORD us,DWORD fmt,DWORD pool,void** out){
  HRESULT hr=o_CreateTexture(dev,w,h,lv,us,fmt,pool,out);
  if(hr==0 && out && *out && cfg.enable && us==0 && fmt==21 && pool==1 && (lv==1||lv==0) && (int)w>=cfg.minside && (int)h>=cfg.minside && (int)(w*h)<=cfg.maxarea){
    if(!g_rvtSet){ void** rv=RV(*out); for(int i=0;i<19;i++){ g_rvt[i]=rv[i]; char m[64]; modName(rv[i],m,64); L("realvt[%d]=%p (%s)",i,rv[i],m); } g_rvtSet=true; }
    WTex* t=(WTex*)HeapAlloc(GetProcessHeap(),HEAP_ZERO_MEMORY,sizeof(WTex)); if(t){ t->vptr=(void**)g_wvt; t->ref=1; t->real=*out; t->dev=dev; t->w=w; t->h=h; t->usage=us; t->fmt=fmt; t->pool=pool; *out=t; LONG n=InterlockedIncrement(&g_nWrap); if(n<=60) L("WRAP #%ld t=%p real=%p realvt=%p %ux%u",n,t,t->real,*(void**)t->real,w,h); } }
  return hr; }
static HRESULT WINAPI h_SetTexture(void* dev,DWORD st,void* tex){ if(isW(tex)) tex=((WTex*)tex)->real; return o_SetTexture(dev,st,tex); }
static HRESULT WINAPI h_UpdateTexture(void* dev,void* src,void* dst){ if(isW(src)) src=((WTex*)src)->real; if(isW(dst)) dst=((WTex*)dst)->real; L("AVISO: UpdateTexture usado"); return o_UpdateTexture(dev,src,dst); }

static bool g_devHooked=false;
static HRESULT WINAPI h_CreateDevice(void* d3d,UINT ad,DWORD ty,HWND hw,DWORD fl,void* pp,void** out){
  HRESULT hr=o_CreateDevice(d3d,ad,ty,hw,fl,pp,out);
  L("CreateDevice hr=0x%X dev=%p",hr,out?*out:0);
  if(hr==0 && out && *out && !g_devHooked){ void** vt=RV(*out);
    patch(vt,20,(void*)h_CreateTexture,(void**)&o_CreateTexture);
    patch(vt,61,(void*)h_SetTexture,(void**)&o_SetTexture);
    patch(vt,29,(void*)h_UpdateTexture,(void**)&o_UpdateTexture);
    g_devHooked=true; L("device vtable parcheada (CreateTexture/SetTexture/UpdateTexture)"); }
  return hr; }

extern "C" void* WINAPI Direct3DCreate8(UINT ver){
  static HMODULE real=0; if(!real){ char p[MAX_PATH]; GetSystemDirectoryA(p,MAX_PATH); lstrcatA(p,"\\d3d8.dll"); real=LoadLibraryA(p); }
  typedef void* (WINAPI *C8)(UINT); C8 c=(C8)GetProcAddress(real,"Direct3DCreate8");
  void* d3d=c?c(ver):0; L("Direct3DCreate8(%u) -> %p",ver,d3d);
  if(d3d){ static bool done=false; if(!done){ patch(RV(d3d),15,(void*)h_CreateDevice,(void**)&o_CreateDevice); done=true; L("IDirect3D8 vtable parcheada (CreateDevice)"); } }
  return d3d; }

BOOL WINAPI DllMain(HINSTANCE h,DWORD r,LPVOID){
  if(r==DLL_PROCESS_ATTACH){
    DisableThreadLibraryCalls(h); InitializeCriticalSection(&g_cs);
    GetModuleFileNameA(h,g_dir,MAX_PATH); char* s=strrchr(g_dir,'\\'); if(s) *s=0;
    sprintf(g_hdd,"%s\\d3d8hd",g_dir); CreateDirectoryA(g_hdd,0); char t[MAX_PATH];
    sprintf(t,"%s\\dump",g_hdd); CreateDirectoryA(t,0); sprintf(t,"%s\\hd",g_hdd); CreateDirectoryA(t,0);
    char ini[MAX_PATH]; sprintf(ini,"%s\\d3d8hd.ini",g_hdd);
    cfg.enable=GetPrivateProfileIntA("hd","enable",1,ini); cfg.dump=GetPrivateProfileIntA("hd","dump",0,ini); cfg.hd=GetPrivateProfileIntA("hd","hd",1,ini);
    cfg.minside=GetPrivateProfileIntA("hd","minside",16,ini); cfg.maxarea=GetPrivateProfileIntA("hd","maxarea",262144,ini); cfg.log=GetPrivateProfileIntA("hd","log",1,ini);
    sprintf(t,"%s\\d3d8hd.log",g_hdd); g_log=fopen(t,"w");
    void* v[19]={ (void*)W_QI,(void*)W_AddRef,(void*)W_Release,(void*)W_GetDevice,(void*)W_SetPrivateData,(void*)W_GetPrivateData,(void*)W_FreePrivateData,(void*)W_SetPriority,(void*)W_GetPriority,(void*)W_PreLoad,(void*)W_GetType,(void*)W_SetLOD,(void*)W_GetLOD,(void*)W_GetLevelCount,(void*)W_GetLevelDesc,(void*)W_GetSurfaceLevel,(void*)W_LockRect,(void*)W_UnlockRect,(void*)W_AddDirtyRect };
    memcpy(g_wvt,v,sizeof(v));
    L("d3d8 proxy v2 cargado enable=%d dump=%d hd=%d minside=%d maxarea=%d",cfg.enable,cfg.dump,cfg.hd,cfg.minside,cfg.maxarea); }
  else if(r==DLL_PROCESS_DETACH){ L("fin: envueltas=%ld hd=%ld sin_hd=%ld volcadas=%ld",g_nWrap,g_nHD,g_nMiss,g_nDump); if(g_log) fclose(g_log); }
  return TRUE; }
