# 2dog.iossimulator-arm64

Experimental iOS Simulator (arm64, Apple silicon Macs) runtime for .NET iOS
hosts. The meta package selects one Debug or Release payload using
`TwoDogVariant` (default: release) and links `libgodot.a`, the statically
linked Godot engine, into the app with `ForceLoad`. The Simulator renders with
the Compatibility renderer (GLES3), since Godot's Metal driver is device-only.

Reference `2dog.ios` for the host glue and build targets and use the
`iossimulator-arm64` runtime identifier; devices use `2dog.ios-arm64`.
Building needs a Mac with Xcode.
