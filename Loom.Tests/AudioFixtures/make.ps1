<#
.SYNOPSIS
Remakes the Ogg Vorbis test files and their reference decodes. Needs ffmpeg built with libvorbis on PATH.

.DESCRIPTION
Each file is encoded by libvorbis, and decoded again by libvorbis (the reference decoder) to 16-bit PCM, which the
decoder's tests compare against. The signals are short and quieter than full scale, so the reference never clips:
- stereo-44k: a tone and a chirp, with clicks every eighth of a second, which make the encoder use short blocks.
- mono-48k-q0: noise under a tone at the lowest quality, for its very different codebooks.
- surround-48k: six channels, each its own tone, for multi-step coupling.
#>
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

function Make([string]$name, [string]$source, [string[]]$encode)
{
	ffmpeg -hide_banner -loglevel error -y -f lavfi -i $source @encode "$name.ogg"
	if ($LASTEXITCODE -ne 0) { throw "encoding $name failed" }
	ffmpeg -hide_banner -loglevel error -y -c:a libvorbis -i "$name.ogg" -f s16le -c:a pcm_s16le "$name.s16"
	if ($LASTEXITCODE -ne 0) { throw "decoding $name failed" }
	"$name.ogg: $((Get-Item "$name.ogg").Length) bytes; reference $((Get-Item "$name.s16").Length) bytes"
}

Make 'stereo-44k' `
	'aevalsrc=0.3*sin(2*PI*440*t)+0.1*sin(2*PI*3000*t)+0.4*lt(mod(t\,0.125)\,0.0004)|0.3*sin(2*PI*(200+2000*t)*t):s=44100:d=0.5' `
	@('-c:a', 'libvorbis', '-q:a', '5')
Make 'mono-48k-q0' `
	'aevalsrc=0.2*(random(0)-0.5)+0.2*sin(2*PI*660*t):s=48000:d=0.4' `
	@('-c:a', 'libvorbis', '-q:a', '0')
Make 'surround-48k' `
	'aevalsrc=0.2*sin(2*PI*300*t)|0.2*sin(2*PI*500*t)|0.2*sin(2*PI*700*t)|0.2*sin(2*PI*90*t)|0.2*sin(2*PI*1100*t)|0.2*sin(2*PI*1300*t):s=48000:d=0.2:c=5.1' `
	@('-c:a', 'libvorbis', '-q:a', '3')
