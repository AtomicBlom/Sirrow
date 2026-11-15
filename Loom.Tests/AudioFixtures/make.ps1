<#
.SYNOPSIS
Remakes the compressed test files: Ogg Vorbis with their reference decodes, and FLAC. Needs ffmpeg built with libvorbis
on PATH.

.DESCRIPTION
Each Vorbis file is encoded by libvorbis, and decoded again by libvorbis (the reference decoder) to 16-bit PCM, which the
decoder's tests compare against. The signals are short and quieter than full scale, so the reference never clips:
- stereo-44k: a tone and a chirp, with clicks every eighth of a second, which make the encoder use short blocks.
- mono-48k-q0: noise under a tone at the lowest quality, for its very different codebooks.
- surround-48k: six channels, each its own tone, for multi-step coupling.

FLAC is lossless and each file carries the MD5 of its samples, which the tests check the decode against, so it needs
no reference. Each file forces a different corner of the format:
- flac-stereo-44k: the encoder's defaults (stereo mode chosen per frame, LPC up to order 8), starting with silence
  (constant subframes).
- flac-24bit-lpc32: 24-bit, right/side stereo, LPC of order 32 at the finest coefficient precision.
- flac-fixed-odd: left/side stereo, the fixed predictors only, in blocks of 1000 (a size the header spells out).
- flac-mid-side: mid/side stereo, with exact Rice parameters.
- flac-noise: mono full-scale noise, which no predictor helps, so it is stored verbatim.
- flac-wasted-bits: 16-bit samples in a 24-bit file, the low 8 bits always zero.
- flac-surround: six channels, each coded on its own.
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

function MakeFlac([string]$name, [string]$source, [string[]]$encode)
{
	ffmpeg -hide_banner -loglevel error -y -f lavfi -i $source -c:a flac -sample_fmt s16 @encode "$name.flac"
	if ($LASTEXITCODE -ne 0) { throw "encoding $name failed" }
	"$name.flac: $((Get-Item "$name.flac").Length) bytes"
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

$music = 'gte(t\,0.1)*(0.3*sin(2*PI*440*t)+0.1*sin(2*PI*3000*t)+0.4*lt(mod(t\,0.125)\,0.0004))|gte(t\,0.1)*0.3*sin(2*PI*(200+2000*t)*t)'
MakeFlac 'flac-stereo-44k' "aevalsrc=${music}:s=44100:d=0.25" @()
MakeFlac 'flac-24bit-lpc32' "aevalsrc=${music}:s=48000:d=0.2" `
	@('-sample_fmt', 's32', '-bits_per_raw_sample', '24', '-ch_mode', 'right_side', '-lpc_type', 'cholesky',
		'-min_prediction_order', '32', '-max_prediction_order', '32', '-lpc_coeff_precision', '15')
MakeFlac 'flac-fixed-odd' "aevalsrc=${music}:s=44100:d=0.2" `
	@('-ch_mode', 'left_side', '-lpc_type', 'fixed', '-prediction_order_method', 'search', '-frame_size', '1000')
MakeFlac 'flac-mid-side' "aevalsrc=${music}:s=22050:d=0.3" @('-ch_mode', 'mid_side', '-exact_rice_parameters', '1')
MakeFlac 'flac-noise' 'aevalsrc=random(0)*2-1:s=44100:d=0.05' @()
MakeFlac 'flac-wasted-bits' "aevalsrc=${music}:s=48000:d=0.2" `
	@('-af', 'aformat=sample_fmts=s16,aformat=sample_fmts=s32', '-sample_fmt', 's32', '-bits_per_raw_sample', '24')
MakeFlac 'flac-surround' `
	'aevalsrc=0.2*sin(2*PI*300*t)|0.2*sin(2*PI*500*t)|0.2*sin(2*PI*700*t)|0.2*sin(2*PI*90*t)|0.2*sin(2*PI*1100*t)|0.2*sin(2*PI*1300*t):s=48000:d=0.1:c=5.1' `
	@('-ch_mode', 'indep')
