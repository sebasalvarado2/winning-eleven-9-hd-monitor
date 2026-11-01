# Aplica jugadas_extra.txt a WE9.exe.
# Engancha la funcion 0x4DBCF0 (la que decide las jugadas ocultas segun el ID del jugador)
# y le agrega una tabla propia en el hueco libre al final del codigo (0xACD390).
# Uso: aplicar_jugadas.ps1            -> instala/actualiza la tabla
#      aplicar_jugadas.ps1 -Quitar    -> deja la funcion como la original
param([switch]$Quitar)
$ErrorActionPreference = 'Stop'

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$gameDir = Split-Path -Parent $here
$exe = Join-Path $gameDir 'WE9.exe'
$backup = Join-Path $gameDir 'WE9_original.exe'
$listPath = Join-Path $here 'jugadas_extra.txt'

# Hueco libre: ceros entre 0xACD390 y 0xACDFE0 (en 0xACDFE0 hay codigo del juego; se deja margen)
$FUNC = 0x4DBCF0; $CAVE = 0xACD390; $TABLE = 0xACD400; $CAVE_END = 0xACDF80; $CAVE_START = 0xACD390
$BASE = 0x400000   # en .text el offset en archivo es VA - 0x400000
$ORIG = [byte[]](0x0F,0xB7,0xC9,0x81,0xF9,0x44,0x07,0x00,0x00)

if (Get-Process WE9 -ErrorAction SilentlyContinue) { throw 'Cierra el juego (WE9.exe) antes de aplicar los cambios.' }
if (-not (Test-Path $backup)) { Copy-Item $exe $backup; Write-Host "Copia de seguridad: $backup" }

$f = [IO.File]::ReadAllBytes($exe)
function Off($va) { $va - $BASE }
function PutInt($pos, [int]$v) { $b = [BitConverter]::GetBytes($v); for ($k = 0; $k -lt 4; $k++) { $f[$pos + $k] = $b[$k] } }
function PutBytes($pos, [byte[]]$b) { for ($k = 0; $k -lt $b.Length; $k++) { $f[$pos + $k] = $b[$k] } }

# Comprobar que el exe es el que conocemos (original o ya parcheado por este script)
$entry = $f[(Off $FUNC)..((Off $FUNC) + 8)]
$isOrig = -not (Compare-Object $entry $ORIG -SyncWindow 0)
$isHooked = $entry[0] -eq 0xE9 -and ([BitConverter]::ToInt32($f, (Off $FUNC) + 1) + $FUNC + 5) -eq $CAVE
if (-not $isOrig -and -not $isHooked) { throw 'WE9.exe no coincide con la version esperada: no se modifica nada.' }
$caveBytes = $f[(Off $CAVE_START)..((Off $CAVE_END) - 1)]
if ($isOrig -and ($caveBytes | Where-Object { $_ -ne 0x90 -and $_ -ne 0x00 }).Count -gt 0) { throw 'El hueco libre ya esta en uso por otra cosa: no se modifica nada.' }

# El hueco se restaura siempre desde la copia original (relleno de ceros/0x90)
$origFile = [IO.File]::ReadAllBytes($backup)
function RestoreCave { for ($p = Off $CAVE_START; $p -lt (Off $CAVE_END); $p++) { $f[$p] = $origFile[$p] } }

if ($Quitar) {
    PutBytes (Off $FUNC) $ORIG
    RestoreCave
    [IO.File]::WriteAllBytes($exe, $f)
    Write-Host 'Listo: WE9.exe vuelve a tener solo las jugadas originales.'
    return
}

# Leer la lista
$named = @{
    'elastica'     = @(0x20, 0xFF9FFFFF, 0x00200000)
    'elastica2'    = @(0x20, 0xFF9FFFFF, 0x00400000)
    'sin_elastica' = @(0x20, 0xFF9FFFFF, 0x00000000)
}
foreach ($bit in 0x020000, 0x040000, 0x080000, 0x100000) {
    $n = '{0:x6}' -f $bit
    $named["oculta_$n"] = @(0x20, 0xFFFFFFFF, $bit)
    $named["sin_oculta_$n"] = @(0x20, ((-bnot $bit) -band 0xFFFFFFFF), 0)
}
$entries = @()
$lineNo = 0
foreach ($raw in [IO.File]::ReadAllLines($listPath, [Text.Encoding]::UTF8)) {
    $lineNo++
    $line = ($raw -split '#')[0].Trim()
    if ($line -eq '') { continue }
    $parts = $line -split '\|'
    if ($parts.Count -ne 2) { throw "jugadas_extra.txt linea ${lineNo}: formato 'ID | jugada'" }
    $id = [int]$parts[0].Trim()
    $what = $parts[1].Trim().ToLower()
    if ($named.ContainsKey($what)) { $field, $and, $or = $named[$what] }
    elseif ($what -match '^(20|24)([+-])([0-9a-f]{1,8})$') {
        $field = [Convert]::ToInt32($matches[1], 16); $bits = [Convert]::ToUInt32($matches[3], 16)
        if ($matches[2] -eq '+') { $and = 0xFFFFFFFF; $or = $bits } else { $and = (-bnot $bits) -band 0xFFFFFFFF; $or = 0 }
    }
    else { throw "jugadas_extra.txt linea ${lineNo}: jugada desconocida '$what'" }
    if ($id -lt 0 -or $id -ge 0xFFFF) { throw "jugadas_extra.txt linea ${lineNo}: ID invalido $id" }
    # cada entrada: ID(2) relleno(2) and20(4) or20(4) and24(4) or24(4)
    if ($field -eq 0x20) { $entries += ,@($id, $and, $or, 0xFFFFFFFF, 0) }
    else { $entries += ,@($id, 0xFFFFFFFF, 0, $and, $or) }
}
$max = [int](($CAVE_END - $TABLE) / 20) - 1
if ($entries.Count -gt $max) { throw "Demasiadas lineas ($($entries.Count)); el maximo es $max." }

# Codigo nuevo (ver comentarios): corre la funcion original y despues aplica la tabla
$code = [byte[]](
    0x0F,0xB7,0xC9,                 # 00 movzx ecx, cx
    0xE8,0,0,0,0,                   # 03 call STUB (la funcion original)
    0x50,                           # 08 push eax
    0x56,                           # 09 push esi
    0xBE,0,0,0,0,                   # 0A mov esi, TABLE
    0x0F,0xB7,0x06,                 # 0F LOOP: movzx eax, word [esi]
    0x3D,0xFF,0xFF,0x00,0x00,       # 12 cmp eax, 0xFFFF (fin de tabla)
    0x74,0x21,                      # 17 je DONE
    0x3B,0xC1,                      # 19 cmp eax, ecx (es este jugador?)
    0x75,0x18,                      # 1B jne NEXT
    0x8B,0x42,0x20,                 # 1D mov eax, [edx+20]
    0x23,0x46,0x04,                 # 20 and eax, [esi+4]
    0x0B,0x46,0x08,                 # 23 or  eax, [esi+8]
    0x89,0x42,0x20,                 # 26 mov [edx+20], eax
    0x8B,0x42,0x24,                 # 29 mov eax, [edx+24]
    0x23,0x46,0x0C,                 # 2C and eax, [esi+C]
    0x0B,0x46,0x10,                 # 2F or  eax, [esi+10]
    0x89,0x42,0x24,                 # 32 mov [edx+24], eax
    0x83,0xC6,0x14,                 # 35 NEXT: add esi, 20
    0xEB,0xD5,                      # 38 jmp LOOP
    0x5E,                           # 3A DONE: pop esi
    0x58,                           # 3B pop eax (mismo valor que devolvia la original)
    0xC3,                           # 3C ret
    0x81,0xF9,0x44,0x07,0x00,0x00,  # 3D STUB: cmp ecx, 0x744 (instruccion pisada)
    0xE9,0,0,0,0                    # 43 jmp 0x4DBCF9 (sigue la funcion original)
)
$c = Off $CAVE
RestoreCave
PutBytes $c $code
PutInt ($c + 0x04) (($CAVE + 0x3D) - ($CAVE + 0x08))
PutInt ($c + 0x0B) $TABLE
PutInt ($c + 0x44) (($FUNC + 9) - ($CAVE + 0x48))
$t = Off $TABLE
foreach ($e in $entries) {
    $f[$t] = $e[0] -band 0xFF; $f[$t + 1] = ($e[0] -shr 8) -band 0xFF; $f[$t + 2] = 0; $f[$t + 3] = 0
    # -band 0xFFFFFFFFL: PowerShell lee 0xFF9FFFFF como negativo; asi queda como 32 bits sin signo
    for ($k = 1; $k -le 4; $k++) { $b = [BitConverter]::GetBytes([uint32]([int64]$e[$k] -band 0xFFFFFFFFL)); PutBytes ($t + 4 * $k) $b }
    $t += 20
}
$f[$t] = 0xFF; $f[$t + 1] = 0xFF

# Enganche al principio de la funcion original
$f[(Off $FUNC)] = 0xE9
PutInt ((Off $FUNC) + 1) ($CAVE - ($FUNC + 5))
for ($k = 5; $k -lt 9; $k++) { $f[(Off $FUNC) + $k] = 0x90 }

[IO.File]::WriteAllBytes($exe, $f)
Write-Host "Listo: $($entries.Count) jugadas extra aplicadas a WE9.exe."
