# 2dog.android-arm64

Experimental Android arm64 runtime for .NET Android hosts. The meta package selects
one Debug or Release payload using `TwoDogVariant` (default: release).
It packages `libgodot_android.so` and `libc++_shared.so` as Android native libraries
for `arm64-v8a`; it does not copy these libraries into desktop outputs.

Reference `2dog.android` for the Java Activity host and use an explicit
`android-arm64` runtime identifier. Android has no 2dog editor variant.

This is initial Android support; APK builds and on-device lifecycle validation
are still required before treating it as supported production functionality.
