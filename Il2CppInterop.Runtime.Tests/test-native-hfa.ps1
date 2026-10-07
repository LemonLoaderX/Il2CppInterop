[CmdletBinding()]
param(
    [string]$DeviceSerial,
    [string]$RuntimeDirectory,
    [string]$AndroidNdkRoot = $env:ANDROID_NDK_ROOT,
    [string]$AndroidSdkRoot = $env:ANDROID_SDK_ROOT,
    [string]$HostCompiler = 'gcc'
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$output = Join-Path $repository 'obj/native-hfa'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$project = Join-Path $PSScriptRoot 'Il2CppInterop.Runtime.Tests.csproj'
$managed = Join-Path $output 'managed'
& dotnet build $project -c Release -o $managed --nologo
if ($LASTEXITCODE -ne 0) { throw 'Managed fixture build failed.' }

if (!$DeviceSerial) {
    $library = Join-Path $output $(if ($IsWindows) { 'native-hfa.dll' } else { 'libnative-hfa.so' })
    & $HostCompiler -std=c11 -O2 -fPIC -shared (Join-Path $PSScriptRoot 'native-hfa.c') -o $library
    if ($LASTEXITCODE -ne 0) { throw 'Native fixture build failed.' }
    & dotnet (Join-Path $managed 'Il2CppInterop.Runtime.Tests.dll') --native-hfa $library
    if ($LASTEXITCODE -ne 0) { throw 'Native HFA fixture failed.' }
    return
}

foreach ($name in @('libcoreclr.so', 'System.Private.CoreLib.dll')) {
    if (!$RuntimeDirectory -or !(Test-Path -LiteralPath (Join-Path $RuntimeDirectory $name))) {
        throw 'RuntimeDirectory must contain an Android ARM64 CoreCLR runtime (native libraries and framework DLLs).'
    }
}
$adb = Join-Path $AndroidSdkRoot 'platform-tools/adb.exe'
$compiler = Join-Path $AndroidNdkRoot 'toolchains/llvm/prebuilt/windows-x86_64/bin/aarch64-linux-android26-clang.cmd'
$cxx = Join-Path $AndroidNdkRoot 'toolchains/llvm/prebuilt/windows-x86_64/bin/aarch64-linux-android26-clang++.cmd'
$library = Join-Path $output 'libnative-hfa.so'
$runner = Join-Path $output 'native-hfa-host'
& $compiler -std=c11 -O2 -fPIC -shared (Join-Path $PSScriptRoot 'native-hfa.c') -o $library
if ($LASTEXITCODE -ne 0) { throw 'Android native fixture build failed.' }
& $cxx -std=c++17 -O2 -static-libstdc++ (Join-Path $PSScriptRoot 'native-hfa-host.cpp') -ldl -o $runner
if ($LASTEXITCODE -ne 0) { throw 'Android CoreCLR host build failed.' }
$abi = (& $adb -s $DeviceSerial shell getprop ro.product.cpu.abi).Trim()
if ($LASTEXITCODE -ne 0 -or $abi -ne 'arm64-v8a') { throw 'Select a native ARM64 Android device.' }
$remote = "/data/local/tmp/interop-hfa-$([guid]::NewGuid().ToString('N'))"
try {
    & $adb -s $DeviceSerial shell mkdir -p "$remote/runtime" "$remote/managed"
    if ($LASTEXITCODE -ne 0) { throw 'Creating device scratch directories failed.' }
    foreach ($file in Get-ChildItem -LiteralPath $RuntimeDirectory -File | Where-Object { $_.Extension -in '.dll', '.so' }) {
        & $adb -s $DeviceSerial push $file.FullName "$remote/runtime/" | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Pushing $($file.Name) failed." }
    }
    foreach ($file in Get-ChildItem -LiteralPath $managed -Filter '*.dll' -File) {
        & $adb -s $DeviceSerial push $file.FullName "$remote/managed/" | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Pushing $($file.Name) failed." }
    }
    & $adb -s $DeviceSerial push $library "$remote/managed/" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Pushing the native fixture failed.' }
    & $adb -s $DeviceSerial push $runner "$remote/" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Pushing the host failed.' }
    & $adb -s $DeviceSerial shell chmod 700 "$remote/native-hfa-host"
    if ($LASTEXITCODE -ne 0) { throw 'Preparing the host failed.' }
    & $adb -s $DeviceSerial shell "cd $remote && LD_LIBRARY_PATH=$remote/runtime timeout 60 ./native-hfa-host $remote/runtime $remote/managed"
    if ($LASTEXITCODE -ne 0) { throw 'Android HFA fixture failed or timed out.' }
}
finally {
    & $adb -s $DeviceSerial shell rm -rf $remote | Out-Null
}
