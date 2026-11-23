<#
.SYNOPSIS
Rebuilds libloomaaudio.so, the AAudio callback shim (loom_aaudio.c), for each ABI BinaryVibrance.Sirrow.Android ships, into lib/<abi>/.

.DESCRIPTION
The built libraries are checked in, so building Sirrow needs no NDK; run this only after changing loom_aaudio.c. Finds the
NDK from -Ndk, then ANDROID_NDK_ROOT / ANDROID_NDK_HOME, then the newest under the user's Android SDK.
Linked for 16 KB pages, which Android 15 devices may use and Google Play requires.

.EXAMPLE
pwsh BinaryVibrance.Sirrow.Android/Native/build.ps1
#>
param([string]$Ndk)

$ErrorActionPreference = 'Stop'

if (-not $Ndk) { $Ndk = $env:ANDROID_NDK_ROOT }
if (-not $Ndk) { $Ndk = $env:ANDROID_NDK_HOME }
if (-not $Ndk)
{
	$Ndk = Get-ChildItem "$env:LOCALAPPDATA\Android\Sdk\ndk", "$env:ANDROID_HOME\ndk" -Directory -ErrorAction SilentlyContinue |
		Sort-Object { [version]$_.Name } | Select-Object -Last 1 -ExpandProperty FullName
}
if (-not $Ndk -or -not (Test-Path $Ndk)) { throw 'No Android NDK found: pass -Ndk, or set ANDROID_NDK_ROOT.' }

$hostTag = if ($IsWindows -or $env:OS -eq 'Windows_NT') { 'windows-x86_64' } elseif ($IsMacOS) { 'darwin-x86_64' } else { 'linux-x86_64' }
$bin = Join-Path $Ndk "toolchains/llvm/prebuilt/$hostTag/bin"
$exe = if ($hostTag -eq 'windows-x86_64') { '.exe' } else { '' }
$clang = Join-Path $bin "clang$exe"
$strip = Join-Path $bin "llvm-strip$exe"

# The package's minimum, SupportedOSPlatformVersion in BinaryVibrance.Sirrow.Android.csproj
$api = 29
$targets = @{ 'arm64-v8a' = 'aarch64-linux-android'; 'x86_64' = 'x86_64-linux-android' }
$source = Join-Path $PSScriptRoot 'loom_aaudio.c'

foreach ($abi in $targets.Keys)
{
	$out = Join-Path $PSScriptRoot "lib/$abi/libloomaaudio.so"
	New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null
	& $clang "--target=$($targets[$abi])$api" -shared -fPIC -O2 -fvisibility=hidden -fno-sanitize=cfi `
		'-Wl,-z,max-page-size=16384' '-Wl,--build-id=none' '-Wl,-soname,libloomaaudio.so' -o $out $source
	if ($LASTEXITCODE -ne 0) { throw "clang failed for $abi" }
	& $strip --strip-unneeded $out
	if ($LASTEXITCODE -ne 0) { throw "llvm-strip failed for $abi" }
	"$abi -> $out ($((Get-Item $out).Length) bytes)"
}
