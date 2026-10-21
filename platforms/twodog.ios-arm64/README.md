# 2dog.ios-arm64

Experimental iOS device (arm64) runtime for .NET iOS hosts. The meta package
selects one Debug or Release payload using `TwoDogVariant` (default: release)
and links `libgodot.a`, the statically linked Godot engine, into the app with
`ForceLoad`. iOS has no 2dog editor variant.

Reference `2dog.ios` for the host glue and build targets and use the
`ios-arm64` runtime identifier; the Simulator on Apple silicon uses
`2dog.iossimulator-arm64` instead. Building needs a Mac with Xcode.

This is initial iOS support; device validation is still required before
treating it as supported production functionality.
