# Crea WE9.exe a partir de WE9_original.exe aplicando WE9_exe_patch.json (NO modifica el original).
import json,sys,os
d=sys.argv[1] if len(sys.argv)>1 else r"E:\KONAMI\Winning Eleven 9"
a=bytearray(open(os.path.join(d,"WE9_original.exe"),"rb").read()); p=json.load(open(os.path.join(os.path.dirname(os.path.abspath(__file__)),"WE9_exe_patch.json")))
assert len(a)==p["size"]
for i,o,n in p["diffs"]:
    assert a[i]==o; a[i]=n
open(os.path.join(d,"WE9.exe"),"wb").write(a); print("WE9.exe creado")
