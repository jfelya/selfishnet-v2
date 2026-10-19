param([string]$targetType, [string]$targetMethod)

$fullPath = (Resolve-Path ".\SelfishNetv0.2-beta_vista.exe").Path
$a = [System.Reflection.Assembly]::LoadFrom($fullPath)
$module = $a.GetModules()[0]

# Build opcode table
$opcodes = @{}
[System.Reflection.Emit.OpCodes].GetFields() | ForEach-Object {
    $op = $_.GetValue($null)
    if ($op -is [System.Reflection.Emit.OpCode]) {
        $opcodes[[int]$op.Value] = $op
    }
}

function Disassemble-Method($m) {
    Write-Host "`n--------------------------------------------------"
    Write-Host "Method: $($m.DeclaringType.Name)::$($m.Name)"
    Write-Host "--------------------------------------------------"
    $body = $m.GetMethodBody()
    if ($body -eq $null) { Write-Host "No body"; return }
    
    $bytes = $body.GetILAsByteArray()
    $i = 0
    while ($i -lt $bytes.Length) {
        $offset = $i
        $opVal = [int]$bytes[$i]
        $i++
        if ($opVal -eq 0xFE) {
            $opVal = 0xFE00 -bor [int]$bytes[$i]
            $i++
        }
        $op = $opcodes[$opVal]
        if ($op -eq $null) {
            Write-Host ("{0:X4}: Unknown opcode 0x{1:X}" -f $offset, $opVal)
            continue
        }
        
        $operandStr = ""
        switch ($op.OperandType) {
            "InlineNone" { }
            "ShortInlineI" {
                $operandStr = [string][sbyte]$bytes[$i]
                $i += 1
            }
            "InlineI" {
                $val = [BitConverter]::ToInt32($bytes, $i)
                $operandStr = [string]$val
                $i += 4
            }
            "InlineI8" {
                $val = [BitConverter]::ToInt64($bytes, $i)
                $operandStr = [string]$val
                $i += 8
            }
            "ShortInlineR" {
                $val = [BitConverter]::ToSingle($bytes, $i)
                $operandStr = [string]$val
                $i += 4
            }
            "InlineR" {
                $val = [BitConverter]::ToDouble($bytes, $i)
                $operandStr = [string]$val
                $i += 8
            }
            "ShortInlineVar" {
                $operandStr = "V_" + [string]$bytes[$i]
                $i += 1
            }
            "InlineVar" {
                $val = [BitConverter]::ToUInt16($bytes, $i)
                $operandStr = "V_" + [string]$val
                $i += 2
            }
            "ShortInlineBrTarget" {
                $b = [int]$bytes[$i]
                if ($b -gt 127) { $b = $b - 256 }
                $target = $i + 1 + $b
                $operandStr = ("IL_{0:X4}" -f $target)
                $i += 1
            }
            "InlineBrTarget" {
                $target = $i + 4 + [BitConverter]::ToInt32($bytes, $i)
                $operandStr = ("IL_{0:X4}" -f $target)
                $i += 4
            }
            "InlineString" {
                $token = [BitConverter]::ToInt32($bytes, $i)
                try {
                    $str = $module.ResolveString($token)
                    $escaped = $str.Replace("`n", "\n").Replace("`r", "\r").Replace("`t", "\t")
                    $operandStr = "`"$escaped`""
                } catch {
                    $operandStr = ("Token 0x{0:X8}" -f $token)
                }
                $i += 4
            }
            "InlineField" {
                $token = [BitConverter]::ToInt32($bytes, $i)
                try {
                    $f = $module.ResolveField($token)
                    $operandStr = "$($f.DeclaringType.Name)::$($f.Name)"
                } catch {
                    $operandStr = ("FieldToken 0x{0:X8}" -f $token)
                }
                $i += 4
            }
            "InlineMethod" {
                $token = [BitConverter]::ToInt32($bytes, $i)
                try {
                    $mb = $module.ResolveMember($token)
                    $operandStr = "$($mb.DeclaringType.Name)::$($mb.Name)"
                } catch {
                    $operandStr = ("MethodToken 0x{0:X8}" -f $token)
                }
                $i += 4
            }
            "InlineType" {
                $token = [BitConverter]::ToInt32($bytes, $i)
                try {
                    $t = $module.ResolveType($token)
                    $operandStr = "$($t.FullName)"
                } catch {
                    $operandStr = ("TypeToken 0x{0:X8}" -f $token)
                }
                $i += 4
            }
            "InlineTok" {
                $token = [BitConverter]::ToInt32($bytes, $i)
                try {
                    $mb = $module.ResolveMember($token)
                    $operandStr = "$($mb.DeclaringType.Name)::$($mb.Name)"
                } catch {
                    $operandStr = ("MemberToken 0x{0:X8}" -f $token)
                }
                $i += 4
            }
            "InlineSwitch" {
                $count = [BitConverter]::ToInt32($bytes, $i)
                $i += 4
                $targets = @()
                $baseOff = $i + ($count * 4)
                for ($k = 0; $k -lt $count; $k++) {
                    $targetOff = $baseOff + [BitConverter]::ToInt32($bytes, $i)
                    $targets += ("IL_{0:X4}" -f $targetOff)
                    $i += 4
                }
                $operandStr = "(" + ($targets -join ", ") + ")"
            }
            default {
                $operandStr = "Unhandled: $($op.OperandType)"
            }
        }
        Write-Host ("  IL_{0:X4}: {1,-12} {2}" -f $offset, $op.Name, $operandStr)
    }
}

foreach ($t in $a.GetTypes()) {
    if ($targetType -and $t.Name -notlike "*$targetType*") { continue }
    $members = @()
    $members += $t.GetConstructors([System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::NonPublic -bor [System.Reflection.BindingFlags]::Instance -bor [System.Reflection.BindingFlags]::Static)
    $members += $t.GetMethods([System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::NonPublic -bor [System.Reflection.BindingFlags]::Instance -bor [System.Reflection.BindingFlags]::Static -bor [System.Reflection.BindingFlags]::DeclaredOnly)
    foreach ($m in $members) {
        if ($targetMethod -and $m.Name -notlike "*$targetMethod*") { continue }
        Disassemble-Method $m
    }
}
