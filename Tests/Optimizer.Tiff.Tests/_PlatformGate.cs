// Auto-included assembly attribute — skips this whole test project on non-Windows.
// The library it tests declares <SupportedOSPlatform>windows</SupportedOSPlatform> because it
// depends on System.Drawing.Common (Bitmap/GDI+), which only works on Windows.
[assembly: NUnit.Framework.Platform("Win")]
