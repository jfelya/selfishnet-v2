$ErrorActionPreference = "Stop"

function Inspect-Assembly($path) {
    Write-Host "=========================================="
    Write-Host "File: $path"
    $fullPath = (Resolve-Path $path).Path
    try {
        $a = [System.Reflection.Assembly]::LoadFrom($fullPath)
        Write-Host "FullName: $($a.FullName)"
        Write-Host "ImageRuntimeVersion: $($a.ImageRuntimeVersion)"
        Write-Host "Location: $($a.Location)"
        
        Write-Host "`n--- Referenced Assemblies ---"
        foreach ($ref in $a.GetReferencedAssemblies()) {
            Write-Host "  $($ref.FullName)"
        }
        
        Write-Host "`n--- Types and Members ---"
        foreach ($t in $a.GetTypes()) {
            Write-Host "Type: $($t.FullName) (Base: $($t.BaseType))"
            $methods = $t.GetMethods([System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::NonPublic -bor [System.Reflection.BindingFlags]::Instance -bor [System.Reflection.BindingFlags]::Static -bor [System.Reflection.BindingFlags]::DeclaredOnly)
            foreach ($m in $methods) {
                $params = ($m.GetParameters() | ForEach-Object { "$($_.ParameterType.Name) $($_.Name)" }) -join ", "
                Write-Host "   Method: $($m.ReturnType.Name) $($m.Name)($params)"
            }
            $fields = $t.GetFields([System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::NonPublic -bor [System.Reflection.BindingFlags]::Instance -bor [System.Reflection.BindingFlags]::Static -bor [System.Reflection.BindingFlags]::DeclaredOnly)
            foreach ($f in $fields) {
                Write-Host "   Field: $($f.FieldType.Name) $($f.Name)"
            }
        }
    } catch {
        Write-Host "Failed to inspect $path : $_"
    }
}

Inspect-Assembly ".\SelfishNetv0.2-beta_vista.exe"
Inspect-Assembly ".\PcapNet.dll"
Inspect-Assembly ".\ExpandableGridView.dll"
Inspect-Assembly ".\DataGridViewNumericUpDownElements.dll"
