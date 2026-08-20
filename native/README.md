# rnnoise.dll

Built from the unmodified [xiph/rnnoise](https://github.com/xiph/rnnoise) source
(BSD-3-Clause, see `rnnoise-COPYING.txt`) plus its official pretrained model
(`model_version` hash `0a8755f8e2d834eff6a54714ecc7d75f9932e845df35f8b59bc52a7cfe6e8b37`,
downloaded and checksum-verified from `media.xiph.org`), for real-time microphone noise
suppression (`Audio/RnnoiseProcessor.cs`).

Built with MSVC (VS2022, `cl.exe /LD /O2 /MT`) as a static-CRT x64 DLL -- no dependency
on the VC++ redistributable being installed (confirmed via `dumpbin /DEPENDENTS`: only
imports `KERNEL32.dll`). Source files compiled (from `Makefile.am`'s `RNNOISE_SOURCES`,
portable build, no SSE/AVX runtime dispatch):
`denoise.c rnn.c pitch.c kiss_fft.c celt_lpc.c nnet.c nnet_default.c
parse_lpcnet_weights.c rnnoise_data.c rnnoise_tables.c`.

To rebuild: clone xiph/rnnoise, run `download_model.sh`, then from a VS2022 x64 Native
Tools command prompt:

```
cl /nologo /LD /O2 /MT /DWIN32 /D_WIN32 /DRNNOISE_BUILD /DDLL_EXPORT /I include /I src ^
  src\denoise.c src\rnn.c src\pitch.c src\kiss_fft.c src\celt_lpc.c src\nnet.c ^
  src\nnet_default.c src\parse_lpcnet_weights.c src\rnnoise_data.c src\rnnoise_tables.c ^
  /Fe:rnnoise.dll
```
