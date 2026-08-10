# Contributing

Thank you for helping XReel OS.

1. Open an issue describing the hardware, Android/One UI version, glasses
   firmware, GlassesControl version and reproduction steps.
2. Keep changes inside the community OS package and scene. Do not add private
   Memory services, user data, API keys or proprietary SDK archives.
3. Preserve Eye-frame privacy: no frame dumps or network upload by default.
4. Build with the documented Android target and run `git diff --check`.
5. For interaction/render/cinema changes, include the hardware gate performed.
6. Never claim compatibility based only on mocks or the Unity editor.

Small, focused pull requests are preferred. Changes to gesture thresholds should
include before/after logs and must test pinch-versus-fist arbitration, palm false
positives and the post-cinema Eye restart.
