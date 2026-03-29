# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run Commands

```bash
# Build entire solution
dotnet build PngCrush.slnx -c Release

# Build individual projects
dotnet build Crush.Png/Crush.Png.csproj -c Release
dotnet build Optimizer.Png/Optimizer.Png.csproj -c Release
dotnet build Compression.Core/Compression.Core.csproj -c Release
dotnet build Optimizer.Gif/Optimizer.Gif.csproj -c Release
dotnet build Crush.Gif/Crush.Gif.csproj -c Release
dotnet build Optimizer.Tiff/Optimizer.Tiff.csproj -c Release
dotnet build Crush.Tiff/Crush.Tiff.csproj -c Release
dotnet build Optimizer.Bmp/Optimizer.Bmp.csproj -c Release
dotnet build Crush.Bmp/Crush.Bmp.csproj -c Release
dotnet build Optimizer.Tga/Optimizer.Tga.csproj -c Release
dotnet build Crush.Tga/Crush.Tga.csproj -c Release
dotnet build Optimizer.Pcx/Optimizer.Pcx.csproj -c Release
dotnet build Crush.Pcx/Crush.Pcx.csproj -c Release
dotnet build Optimizer.Jpeg/Optimizer.Jpeg.csproj -c Release
dotnet build Crush.Jpeg/Crush.Jpeg.csproj -c Release
dotnet build FileFormat.Ico/FileFormat.Ico.csproj -c Release
dotnet build Optimizer.Ico/Optimizer.Ico.csproj -c Release
dotnet build Crush.Ico/Crush.Ico.csproj -c Release
dotnet build FileFormat.Cur/FileFormat.Cur.csproj -c Release
dotnet build Optimizer.Cur/Optimizer.Cur.csproj -c Release
dotnet build Crush.Cur/Crush.Cur.csproj -c Release
dotnet build FileFormat.Riff/FileFormat.Riff.csproj -c Release
dotnet build FileFormat.Bmp/FileFormat.Bmp.csproj -c Release
dotnet build FileFormat.Tga/FileFormat.Tga.csproj -c Release
dotnet build FileFormat.Pcx/FileFormat.Pcx.csproj -c Release
dotnet build FileFormat.Jpeg/FileFormat.Jpeg.csproj -c Release
dotnet build FileFormat.Tiff/FileFormat.Tiff.csproj -c Release
dotnet build FileFormat.Png/FileFormat.Png.csproj -c Release
dotnet build FileFormat.Ani/FileFormat.Ani.csproj -c Release
dotnet build Optimizer.Ani/Optimizer.Ani.csproj -c Release
dotnet build Crush.Ani/Crush.Ani.csproj -c Release
dotnet build FileFormat.WebP/FileFormat.WebP.csproj -c Release
dotnet build Optimizer.WebP/Optimizer.WebP.csproj -c Release
dotnet build Crush.WebP/Crush.WebP.csproj -c Release
dotnet build FileFormat.Wbmp/FileFormat.Wbmp.csproj -c Release
dotnet build FileFormat.Qoi/FileFormat.Qoi.csproj -c Release
dotnet build FileFormat.Farbfeld/FileFormat.Farbfeld.csproj -c Release
dotnet build FileFormat.Netpbm/FileFormat.Netpbm.csproj -c Release
dotnet build FileFormat.Xbm/FileFormat.Xbm.csproj -c Release
dotnet build FileFormat.Xpm/FileFormat.Xpm.csproj -c Release
dotnet build FileFormat.MacPaint/FileFormat.MacPaint.csproj -c Release
dotnet build FileFormat.ZxSpectrum/FileFormat.ZxSpectrum.csproj -c Release
dotnet build FileFormat.Koala/FileFormat.Koala.csproj -c Release
dotnet build FileFormat.Degas/FileFormat.Degas.csproj -c Release
dotnet build FileFormat.Neochrome/FileFormat.Neochrome.csproj -c Release
dotnet build FileFormat.GemImg/FileFormat.GemImg.csproj -c Release
dotnet build FileFormat.AmstradCpc/FileFormat.AmstradCpc.csproj -c Release
dotnet build FileFormat.Pfm/FileFormat.Pfm.csproj -c Release
dotnet build FileFormat.Sgi/FileFormat.Sgi.csproj -c Release
dotnet build FileFormat.SunRaster/FileFormat.SunRaster.csproj -c Release
dotnet build FileFormat.Hdr/FileFormat.Hdr.csproj -c Release
dotnet build FileFormat.UtahRle/FileFormat.UtahRle.csproj -c Release
dotnet build FileFormat.DrHalo/FileFormat.DrHalo.csproj -c Release
dotnet build FileFormat.Iff/FileFormat.Iff.csproj -c Release
dotnet build FileFormat.Ilbm/FileFormat.Ilbm.csproj -c Release
dotnet build FileFormat.Fli/FileFormat.Fli.csproj -c Release
dotnet build FileFormat.Cineon/FileFormat.Cineon.csproj -c Release
dotnet build FileFormat.Dds/FileFormat.Dds.csproj -c Release
dotnet build FileFormat.Vtf/FileFormat.Vtf.csproj -c Release
dotnet build FileFormat.Ktx/FileFormat.Ktx.csproj -c Release
dotnet build FileFormat.Exr/FileFormat.Exr.csproj -c Release
dotnet build FileFormat.Dpx/FileFormat.Dpx.csproj -c Release
dotnet build FileFormat.Fits/FileFormat.Fits.csproj -c Release
dotnet build FileFormat.Ccitt/FileFormat.Ccitt.csproj -c Release
dotnet build FileFormat.BbcMicro/FileFormat.BbcMicro.csproj -c Release
dotnet build FileFormat.C64Multi/FileFormat.C64Multi.csproj -c Release
dotnet build FileFormat.Psd/FileFormat.Psd.csproj -c Release
dotnet build Crush.Core/Crush.Core.csproj -c Release
dotnet build Crush.TestUtilities/Crush.TestUtilities.csproj -c Release
dotnet build FileFormat.Core/FileFormat.Core.csproj -c Release
dotnet build FileFormat.Hrz/FileFormat.Hrz.csproj -c Release
dotnet build FileFormat.Cmu/FileFormat.Cmu.csproj -c Release
dotnet build FileFormat.Mtv/FileFormat.Mtv.csproj -c Release
dotnet build FileFormat.Qrt/FileFormat.Qrt.csproj -c Release
dotnet build FileFormat.Msp/FileFormat.Msp.csproj -c Release
dotnet build FileFormat.Dcx/FileFormat.Dcx.csproj -c Release
dotnet build FileFormat.Astc/FileFormat.Astc.csproj -c Release
dotnet build FileFormat.Pkm/FileFormat.Pkm.csproj -c Release
dotnet build FileFormat.Tim/FileFormat.Tim.csproj -c Release
dotnet build FileFormat.Tim2/FileFormat.Tim2.csproj -c Release
dotnet build FileFormat.Wal/FileFormat.Wal.csproj -c Release
dotnet build FileFormat.Pvr/FileFormat.Pvr.csproj -c Release
dotnet build FileFormat.Wpg/FileFormat.Wpg.csproj -c Release
dotnet build FileFormat.Bsave/FileFormat.Bsave.csproj -c Release
dotnet build FileFormat.Clp/FileFormat.Clp.csproj -c Release
dotnet build FileFormat.Spectrum512/FileFormat.Spectrum512.csproj -c Release
dotnet build FileFormat.Tiny/FileFormat.Tiny.csproj -c Release
dotnet build FileFormat.Sixel/FileFormat.Sixel.csproj -c Release
dotnet build FileFormat.Wad/FileFormat.Wad.csproj -c Release
dotnet build FileFormat.Wad3/FileFormat.Wad3.csproj -c Release
dotnet build FileFormat.Apng/FileFormat.Apng.csproj -c Release
dotnet build FileFormat.Mng/FileFormat.Mng.csproj -c Release
dotnet build FileFormat.Xcf/FileFormat.Xcf.csproj -c Release
dotnet build FileFormat.Pict/FileFormat.Pict.csproj -c Release
dotnet build FileFormat.Dicom/FileFormat.Dicom.csproj -c Release
dotnet build FileFormat.Trs80/FileFormat.Trs80.csproj -c Release
dotnet build FileFormat.Envi/FileFormat.Envi.csproj -c Release
dotnet build FileFormat.SoftImage/FileFormat.SoftImage.csproj -c Release
dotnet build FileFormat.MayaIff/FileFormat.MayaIff.csproj -c Release
dotnet build FileFormat.Xcursor/FileFormat.Xcursor.csproj -c Release
dotnet build FileFormat.IffPbm/FileFormat.IffPbm.csproj -c Release
dotnet build FileFormat.PcPaint/FileFormat.PcPaint.csproj -c Release
dotnet build FileFormat.IffAcbm/FileFormat.IffAcbm.csproj -c Release
dotnet build FileFormat.IffDeep/FileFormat.IffDeep.csproj -c Release
dotnet build FileFormat.IffRgb8/FileFormat.IffRgb8.csproj -c Release
dotnet build FileFormat.IffRgbn/FileFormat.IffRgbn.csproj -c Release
dotnet build FileFormat.Interfile/FileFormat.Interfile.csproj -c Release
dotnet build FileFormat.AtariFalcon/FileFormat.AtariFalcon.csproj -c Release
dotnet build FileFormat.SnesTile/FileFormat.SnesTile.csproj -c Release
dotnet build FileFormat.SegaGenTile/FileFormat.SegaGenTile.csproj -c Release
dotnet build FileFormat.PcEngineTile/FileFormat.PcEngineTile.csproj -c Release
dotnet build FileFormat.MasterSystemTile/FileFormat.MasterSystemTile.csproj -c Release
dotnet build FileFormat.SymbianMbm/FileFormat.SymbianMbm.csproj -c Release
dotnet build FileFormat.XvThumbnail/FileFormat.XvThumbnail.csproj -c Release
dotnet build FileFormat.Mrc/FileFormat.Mrc.csproj -c Release
dotnet build FileFormat.Gd2/FileFormat.Gd2.csproj -c Release
dotnet build FileFormat.BigTiff/FileFormat.BigTiff.csproj -c Release
dotnet build FileFormat.AutodeskCel/FileFormat.AutodeskCel.csproj -c Release
dotnet build FileFormat.Wad2/FileFormat.Wad2.csproj -c Release
dotnet build FileFormat.Pdf/FileFormat.Pdf.csproj -c Release
dotnet build FileFormat.WindowsPe/FileFormat.WindowsPe.csproj -c Release
dotnet build FileFormat.Phm/FileFormat.Phm.csproj -c Release
dotnet build FileFormat.Fl32/FileFormat.Fl32.csproj -c Release
dotnet build FileFormat.Nie/FileFormat.Nie.csproj -c Release
dotnet build FileFormat.FaceSaver/FileFormat.FaceSaver.csproj -c Release
dotnet build FileFormat.DaliST/FileFormat.DaliST.csproj -c Release
dotnet build FileFormat.MultiPalettePicture/FileFormat.MultiPalettePicture.csproj -c Release
dotnet build FileFormat.DrawIt/FileFormat.DrawIt.csproj -c Release
dotnet build FileFormat.DuneGraph/FileFormat.DuneGraph.csproj -c Release
dotnet build FileFormat.PrismPaint/FileFormat.PrismPaint.csproj -c Release
dotnet build FileFormat.Rembrandt/FileFormat.Rembrandt.csproj -c Release
dotnet build FileFormat.PabloPaint/FileFormat.PabloPaint.csproj -c Release
dotnet build FileFormat.QuantumPaint/FileFormat.QuantumPaint.csproj -c Release
dotnet build FileFormat.SinbadSlideshow/FileFormat.SinbadSlideshow.csproj -c Release
dotnet build FileFormat.SyntheticArts/FileFormat.SyntheticArts.csproj -c Release
dotnet build FileFormat.HighresMedium/FileFormat.HighresMedium.csproj -c Release
dotnet build FileFormat.FullscreenKit/FileFormat.FullscreenKit.csproj -c Release
dotnet build FileFormat.FliEditor/FileFormat.FliEditor.csproj -c Release
dotnet build FileFormat.FliDesigner/FileFormat.FliDesigner.csproj -c Release
dotnet build FileFormat.MuifliEditor/FileFormat.MuifliEditor.csproj -c Release
dotnet build FileFormat.CokeAtari/FileFormat.CokeAtari.csproj -c Release
dotnet build FileFormat.AtariFalconXga/FileFormat.AtariFalconXga.csproj -c Release
dotnet build FileFormat.SpookySpritesFalcon/FileFormat.SpookySpritesFalcon.csproj -c Release

# Run all tests
dotnet test Compression.Tests/Compression.Tests.csproj
dotnet test Optimizer.Png.Tests/Optimizer.Png.Tests.csproj
dotnet test FileFormat.Png.Tests/FileFormat.Png.Tests.csproj
dotnet test Optimizer.Gif.Tests/Optimizer.Gif.Tests.csproj
dotnet test Optimizer.Tiff.Tests/Optimizer.Tiff.Tests.csproj
dotnet test FileFormat.Tiff.Tests/FileFormat.Tiff.Tests.csproj
dotnet test Optimizer.Bmp.Tests/Optimizer.Bmp.Tests.csproj
dotnet test FileFormat.Bmp.Tests/FileFormat.Bmp.Tests.csproj
dotnet test Optimizer.Tga.Tests/Optimizer.Tga.Tests.csproj
dotnet test FileFormat.Tga.Tests/FileFormat.Tga.Tests.csproj
dotnet test Optimizer.Pcx.Tests/Optimizer.Pcx.Tests.csproj
dotnet test FileFormat.Pcx.Tests/FileFormat.Pcx.Tests.csproj
dotnet test Optimizer.Jpeg.Tests/Optimizer.Jpeg.Tests.csproj
dotnet test FileFormat.Jpeg.Tests/FileFormat.Jpeg.Tests.csproj
dotnet test Optimizer.Ico.Tests/Optimizer.Ico.Tests.csproj
dotnet test FileFormat.Ico.Tests/FileFormat.Ico.Tests.csproj
dotnet test Optimizer.Cur.Tests/Optimizer.Cur.Tests.csproj
dotnet test FileFormat.Cur.Tests/FileFormat.Cur.Tests.csproj
dotnet test Optimizer.Ani.Tests/Optimizer.Ani.Tests.csproj
dotnet test FileFormat.Ani.Tests/FileFormat.Ani.Tests.csproj
dotnet test Optimizer.WebP.Tests/Optimizer.WebP.Tests.csproj
dotnet test FileFormat.WebP.Tests/FileFormat.WebP.Tests.csproj
dotnet test FileFormat.Riff.Tests/FileFormat.Riff.Tests.csproj
dotnet test FileFormat.Wbmp.Tests/FileFormat.Wbmp.Tests.csproj
dotnet test FileFormat.Qoi.Tests/FileFormat.Qoi.Tests.csproj
dotnet test FileFormat.Farbfeld.Tests/FileFormat.Farbfeld.Tests.csproj
dotnet test FileFormat.Netpbm.Tests/FileFormat.Netpbm.Tests.csproj
dotnet test FileFormat.Xbm.Tests/FileFormat.Xbm.Tests.csproj
dotnet test FileFormat.Xpm.Tests/FileFormat.Xpm.Tests.csproj
dotnet test FileFormat.MacPaint.Tests/FileFormat.MacPaint.Tests.csproj
dotnet test FileFormat.ZxSpectrum.Tests/FileFormat.ZxSpectrum.Tests.csproj
dotnet test FileFormat.Koala.Tests/FileFormat.Koala.Tests.csproj
dotnet test FileFormat.Degas.Tests/FileFormat.Degas.Tests.csproj
dotnet test FileFormat.Neochrome.Tests/FileFormat.Neochrome.Tests.csproj
dotnet test FileFormat.GemImg.Tests/FileFormat.GemImg.Tests.csproj
dotnet test FileFormat.AmstradCpc.Tests/FileFormat.AmstradCpc.Tests.csproj
dotnet test FileFormat.Pfm.Tests/FileFormat.Pfm.Tests.csproj
dotnet test FileFormat.Sgi.Tests/FileFormat.Sgi.Tests.csproj
dotnet test FileFormat.SunRaster.Tests/FileFormat.SunRaster.Tests.csproj
dotnet test FileFormat.Hdr.Tests/FileFormat.Hdr.Tests.csproj
dotnet test FileFormat.UtahRle.Tests/FileFormat.UtahRle.Tests.csproj
dotnet test FileFormat.DrHalo.Tests/FileFormat.DrHalo.Tests.csproj
dotnet test FileFormat.Iff.Tests/FileFormat.Iff.Tests.csproj
dotnet test FileFormat.Ilbm.Tests/FileFormat.Ilbm.Tests.csproj
dotnet test FileFormat.Fli.Tests/FileFormat.Fli.Tests.csproj
dotnet test FileFormat.Cineon.Tests/FileFormat.Cineon.Tests.csproj
dotnet test FileFormat.Dds.Tests/FileFormat.Dds.Tests.csproj
dotnet test FileFormat.Vtf.Tests/FileFormat.Vtf.Tests.csproj
dotnet test FileFormat.Ktx.Tests/FileFormat.Ktx.Tests.csproj
dotnet test FileFormat.Exr.Tests/FileFormat.Exr.Tests.csproj
dotnet test FileFormat.Dpx.Tests/FileFormat.Dpx.Tests.csproj
dotnet test FileFormat.Fits.Tests/FileFormat.Fits.Tests.csproj
dotnet test FileFormat.Ccitt.Tests/FileFormat.Ccitt.Tests.csproj
dotnet test FileFormat.BbcMicro.Tests/FileFormat.BbcMicro.Tests.csproj
dotnet test FileFormat.C64Multi.Tests/FileFormat.C64Multi.Tests.csproj
dotnet test FileFormat.Psd.Tests/FileFormat.Psd.Tests.csproj
dotnet test FileFormat.Hrz.Tests/FileFormat.Hrz.Tests.csproj
dotnet test FileFormat.Cmu.Tests/FileFormat.Cmu.Tests.csproj
dotnet test FileFormat.Mtv.Tests/FileFormat.Mtv.Tests.csproj
dotnet test FileFormat.Qrt.Tests/FileFormat.Qrt.Tests.csproj
dotnet test FileFormat.Msp.Tests/FileFormat.Msp.Tests.csproj
dotnet test FileFormat.Dcx.Tests/FileFormat.Dcx.Tests.csproj
dotnet test FileFormat.Astc.Tests/FileFormat.Astc.Tests.csproj
dotnet test FileFormat.Pkm.Tests/FileFormat.Pkm.Tests.csproj
dotnet test FileFormat.Tim.Tests/FileFormat.Tim.Tests.csproj
dotnet test FileFormat.Tim2.Tests/FileFormat.Tim2.Tests.csproj
dotnet test FileFormat.Wal.Tests/FileFormat.Wal.Tests.csproj
dotnet test FileFormat.Pvr.Tests/FileFormat.Pvr.Tests.csproj
dotnet test FileFormat.Wpg.Tests/FileFormat.Wpg.Tests.csproj
dotnet test FileFormat.Bsave.Tests/FileFormat.Bsave.Tests.csproj
dotnet test FileFormat.Clp.Tests/FileFormat.Clp.Tests.csproj
dotnet test FileFormat.Spectrum512.Tests/FileFormat.Spectrum512.Tests.csproj
dotnet test FileFormat.Tiny.Tests/FileFormat.Tiny.Tests.csproj
dotnet test FileFormat.Sixel.Tests/FileFormat.Sixel.Tests.csproj
dotnet test FileFormat.Wad.Tests/FileFormat.Wad.Tests.csproj
dotnet test FileFormat.Wad3.Tests/FileFormat.Wad3.Tests.csproj
dotnet test FileFormat.Apng.Tests/FileFormat.Apng.Tests.csproj
dotnet test FileFormat.Mng.Tests/FileFormat.Mng.Tests.csproj
dotnet test FileFormat.Xcf.Tests/FileFormat.Xcf.Tests.csproj
dotnet test FileFormat.Pict.Tests/FileFormat.Pict.Tests.csproj
dotnet test FileFormat.Dicom.Tests/FileFormat.Dicom.Tests.csproj
dotnet test Tests/FileFormat.Trs80.Tests/FileFormat.Trs80.Tests.csproj
dotnet test Tests/FileFormat.SoftImage.Tests/FileFormat.SoftImage.Tests.csproj
dotnet test Tests/FileFormat.Envi.Tests/FileFormat.Envi.Tests.csproj
dotnet test Tests/FileFormat.MayaIff.Tests/FileFormat.MayaIff.Tests.csproj
dotnet test Tests/FileFormat.Xcursor.Tests/FileFormat.Xcursor.Tests.csproj
dotnet test Tests/FileFormat.IffPbm.Tests/FileFormat.IffPbm.Tests.csproj
dotnet test Tests/FileFormat.PcPaint.Tests/FileFormat.PcPaint.Tests.csproj
dotnet test Tests/FileFormat.IffAcbm.Tests/FileFormat.IffAcbm.Tests.csproj
dotnet test Tests/FileFormat.IffDeep.Tests/FileFormat.IffDeep.Tests.csproj
dotnet test Tests/FileFormat.IffRgb8.Tests/FileFormat.IffRgb8.Tests.csproj
dotnet test Tests/FileFormat.IffRgbn.Tests/FileFormat.IffRgbn.Tests.csproj
dotnet test Tests/FileFormat.Interfile.Tests/FileFormat.Interfile.Tests.csproj
dotnet test Tests/FileFormat.AtariFalcon.Tests/FileFormat.AtariFalcon.Tests.csproj
dotnet test Tests/FileFormat.SnesTile.Tests/FileFormat.SnesTile.Tests.csproj
dotnet test Tests/FileFormat.SegaGenTile.Tests/FileFormat.SegaGenTile.Tests.csproj
dotnet test Tests/FileFormat.PcEngineTile.Tests/FileFormat.PcEngineTile.Tests.csproj
dotnet test Tests/FileFormat.MasterSystemTile.Tests/FileFormat.MasterSystemTile.Tests.csproj
dotnet test Tests/FileFormat.SymbianMbm.Tests/FileFormat.SymbianMbm.Tests.csproj
dotnet test Tests/FileFormat.XvThumbnail.Tests/FileFormat.XvThumbnail.Tests.csproj
dotnet test Tests/FileFormat.Mrc.Tests/FileFormat.Mrc.Tests.csproj
dotnet test Tests/FileFormat.Gd2.Tests/FileFormat.Gd2.Tests.csproj
dotnet test Tests/FileFormat.BigTiff.Tests/FileFormat.BigTiff.Tests.csproj
dotnet test Tests/FileFormat.AutodeskCel.Tests/FileFormat.AutodeskCel.Tests.csproj
dotnet test Tests/FileFormat.Wad2.Tests/FileFormat.Wad2.Tests.csproj
dotnet test Tests/FileFormat.Pdf.Tests/FileFormat.Pdf.Tests.csproj
dotnet test Tests/FileFormat.WindowsPe.Tests/FileFormat.WindowsPe.Tests.csproj
dotnet test Tests/FileFormat.CameraRaw.Tests/FileFormat.CameraRaw.Tests.csproj
dotnet test Tests/FileFormat.Core.Tests/FileFormat.Core.Tests.csproj
dotnet test Tests/FileFormat.Phm.Tests/FileFormat.Phm.Tests.csproj
dotnet test Tests/FileFormat.Fl32.Tests/FileFormat.Fl32.Tests.csproj
dotnet test Tests/FileFormat.Nie.Tests/FileFormat.Nie.Tests.csproj
dotnet test Tests/FileFormat.FaceSaver.Tests/FileFormat.FaceSaver.Tests.csproj
dotnet test Tests/FileFormat.DaliST.Tests/FileFormat.DaliST.Tests.csproj
dotnet test Tests/FileFormat.MultiPalettePicture.Tests/FileFormat.MultiPalettePicture.Tests.csproj
dotnet test Tests/FileFormat.DrawIt.Tests/FileFormat.DrawIt.Tests.csproj
dotnet test Tests/FileFormat.DuneGraph.Tests/FileFormat.DuneGraph.Tests.csproj
dotnet test Tests/FileFormat.PrismPaint.Tests/FileFormat.PrismPaint.Tests.csproj
dotnet test Tests/FileFormat.Rembrandt.Tests/FileFormat.Rembrandt.Tests.csproj
dotnet test Tests/FileFormat.PabloPaint.Tests/FileFormat.PabloPaint.Tests.csproj
dotnet test Tests/FileFormat.QuantumPaint.Tests/FileFormat.QuantumPaint.Tests.csproj
dotnet test Tests/FileFormat.SinbadSlideshow.Tests/FileFormat.SinbadSlideshow.Tests.csproj
dotnet test Tests/FileFormat.SyntheticArts.Tests/FileFormat.SyntheticArts.Tests.csproj
dotnet test Tests/FileFormat.HighresMedium.Tests/FileFormat.HighresMedium.Tests.csproj
dotnet test Tests/FileFormat.FullscreenKit.Tests/FileFormat.FullscreenKit.Tests.csproj
dotnet test Tests/FileFormat.FliEditor.Tests/FileFormat.FliEditor.Tests.csproj
dotnet test Tests/FileFormat.FliDesigner.Tests/FileFormat.FliDesigner.Tests.csproj
dotnet test Tests/FileFormat.MuifliEditor.Tests/FileFormat.MuifliEditor.Tests.csproj
dotnet test Tests/FileFormat.CokeAtari.Tests/FileFormat.CokeAtari.Tests.csproj
dotnet test Tests/FileFormat.AtariFalconXga.Tests/FileFormat.AtariFalconXga.Tests.csproj
dotnet test Tests/FileFormat.SpookySpritesFalcon.Tests/FileFormat.SpookySpritesFalcon.Tests.csproj

# Run tests with coverage
dotnet test Optimizer.Png.Tests/Optimizer.Png.Tests.csproj --collect:"XPlat Code Coverage"
dotnet test Compression.Tests/Compression.Tests.csproj --collect:"XPlat Code Coverage"

# Run specific test category
dotnet test Optimizer.Png.Tests/Optimizer.Png.Tests.csproj --filter "TestCategory=Regression"
dotnet test Optimizer.Png.Tests/Optimizer.Png.Tests.csproj --filter "TestCategory=Performance"

# Run the CLI tools
dotnet run --project Crush.Png -- -i <input.png> -o <output.png>
dotnet run --project Crush.Gif -- -i <input.gif> -o <output.gif>
dotnet run --project Crush.Tiff -- -i <input.tiff> -o <output.tiff>
dotnet run --project Crush.Bmp -- -i <input.bmp> -o <output.bmp>
dotnet run --project Crush.Tga -- -i <input.tga> -o <output.tga>
dotnet run --project Crush.Pcx -- -i <input.pcx> -o <output.pcx>
dotnet run --project Crush.Jpeg -- -i <input.jpg> -o <output.jpg>
dotnet run --project Crush.Ico -- -i <input.ico> -o <output.ico>
dotnet run --project Crush.Cur -- -i <input.cur> -o <output.cur>
dotnet run --project Crush.Ani -- -i <input.ani> -o <output.ani>
dotnet run --project Crush.WebP -- -i <input.webp> -o <output.webp>
```

## Architecture

~1100-project solution across two repos, organized in slnx solution folders: `/FileFormats/` (542 FileFormat.* + GifFileFormat), `/Optimizers/` (11 Optimizer.*), `/Image/` (Optimizer.Image + Crush.Image), `/Tests/` (~557 test projects + Crush.TestUtilities), and root (Compression.Core, Crush.Core). Includes a compression library, five hundred forty-two format-specific file format libraries (including RIFF/IFF containers, PDF/PE resource extractors, and full pixel codecs for WebP/JPEG2000/JPEGXL/JPEGLS/JPEGXR/HEIF/AVIF/BPG/FLIF/JBIG2/DjVu/ECW), one shared file format core library with attribute-based header mapping and GPU block decoders, eleven format-specific optimizer libraries, one universal image optimizer library, one unified CLI (Crush.Image), shared CLI utilities, and shared test utilities.

**Compression.Core** (net8.0, library) — pure RFC 1951 DEFLATE compression library with no PNG or platform dependencies. Contains the Zopfli-class encoder (`ZopfliDeflater`) reusable by any project needing high-ratio DEFLATE compression. No `System.Drawing.Common` or Windows dependency.

**Crush.Core** (net8.0, library) — shared CLI utilities for all crush tools. Contains `CrushRunner` (shared optimization runner handling input/output validation, cancellation, progress reporting, stopwatch, savings display), `ICrushOptions` (common CLI option interface), `OptimizationProgress` (shared readonly record struct for all optimizers), and `FileFormatting` (file size formatting helper).

**Crush.TestUtilities** (net8.0, library) — shared test helpers. Contains `TestBitmapFactory` (creates reproducible test bitmaps with configurable dimensions, grayscale, and alpha) and `TempFileScope` (IDisposable temp file lifecycle management).

**FileFormat.Core** (net8.0, library) — shared data types for file format header field mapping and pixel conversion. Contains `HeaderFieldDescriptor` (readonly record struct for hex editor field coloring), `PixelConverter` (SIMD-accelerated pixel format conversion: 37 methods covering BGRA hub conversions, direct RGBA32/RGB24/Gray8/band-sequential routes with Vector128/Vector256 shuffle paths, plus 16-bit precision paths: Gray16/Rgb48/Rgba64 hub routes, direct 16-to-16 routes bypassing 8-bit hub, 16-to-8 downscale, 8-to-16 upscale via v*257), `RawImage` (platform-independent pixel data model), `PixelFormat` (enum of supported formats including Gray16, Rgb48, Rgba64), `IImageFileFormat<T>` (generic file format interface with `FromFile`, `FromBytes` (static virtual, default writes to temp file), `FromStream` (static virtual, default reads all bytes), `ToRawImage`, `FromRawImage`, `ToBytes`), `IMultiImageFileFormat<T>` (companion interface for multi-image formats with `ImageCount`, indexed `ToRawImage(file, index)`, default `ToRawImages`), `FormatCapability` (flags enum: VariableResolution=1, MonochromeOnly=2, IndexedOnly=4, HasDedicatedOptimizer=8, MultiImage=16). Block decoders in `BlockDecoders/`: `Bc1Decoder`, `Bc2Decoder`, `Bc3Decoder`, `Bc4Decoder`, `Bc5Decoder`, `Bc6HDecoder` (14-mode HDR signed/unsigned), `Bc7Decoder` (8-mode with 1-3 subsets and 64 partition patterns), `Etc1Decoder`, `Etc2Decoder`, `AstcBlockDecoder`, `PvrtcDecoder` (bilinear blend 2bpp/4bpp). Formats implementing `IMultiImageFileFormat<T>`: IcoFile, CurFile, AniFile, ApngFile, MngFile, FliFile, DcxFile, MpoFile, IcnsFile, TiffFile, BigTiffFile.

**FileFormat.Png** (net8.0, library) — PNG file format reader/writer. Contains `PngReader` (full PNG parser: signature, chunks, IDAT decompress, de-filter, Adam7 de-interlace), `PngWriter` (PNG byte stream assembly: IHDR, PLTE, tRNS, IDAT, CRC32, IEND, with internal fast path accepting pre-filtered/pre-compressed data), `PngFile` (data model), `PngChunkReader` (ancillary chunk extraction), `PngChunk` (chunk record), `PngColorType`, `PngInterlaceMethod`, `PngFilterType` (spec-aligned enums), and `Adam7` (interlace pass definitions). References Compression.Core for DEFLATE and System.IO.Hashing for CRC32.

**Optimizer.Png** (net10.0-windows, library) — the core PNG optimization engine. References `FileFormat.Png` and `FrameworkExtensions.System.Drawing`. Takes a `System.Drawing.Bitmap`, analyzes pixel data via unsafe pointer access, generates all viable `OptimizationCombo` permutations (color mode x bit depth x filter strategy x deflate method x interlace method x optional quantizer/ditherer combo), compresses each in parallel using `SemaphoreSlim`, and returns the smallest valid result as `OptimizationResult`.

**Crush.Png** (net10.0-windows, console app) — thin CLI using `CommandLineParser`. Loads a PNG, constructs `PngOptimizationOptions`, calls `PngOptimizer.OptimizeAsync()`, and writes the result. Uses `CrushRunner.RunAsync` from Crush.Core for common CLI operations (cancellation wiring, progress display, file validation, timing).

**Optimizer.Gif** (net8.0-windows, library) — GIF optimization engine. References `GifFileFormat` from the AnythingToGif repo. Parses input GIF, generates palette reorder/frame optimization combos, tests in parallel, and returns the smallest result.

**Crush.Gif** (net9.0-windows, console app) — async CLI for GIF optimization using `CommandLineParser`. Uses `CrushRunner.RunAsync` from Crush.Core for common CLI operations (cancellation wiring, progress display, file validation, timing).

**FileFormat.Tiff** (net8.0, library) — TIFF file format reader/writer with multi-page support. Contains `TiffReader` (TIFF parser via LibTiff.NET wrapper, reads all IFDs via `ReadDirectory()` loop), `TiffWriter` (TIFF strip/tile assembly with custom raw writing for Zopfli integration, multi-page writing via `WriteDirectory()` per IFD), `TiffFile` (data model implementing `IMultiImageFileFormat<TiffFile>`, with `Pages` collection of `TiffPage` entries), `TiffPage` (single-page data model), `PackBitsCompressor` (PackBits RLE encoder/decoder), `TiffCompression`, `TiffPredictor`, `TiffColorMode` (enums). References BitMiracle.LibTiff.NET and Compression.Core.

**Optimizer.Tiff** (net8.0, library) — TIFF optimization engine. References `FileFormat.Tiff`. Supports PackBits, LZW, DEFLATE (with Zopfli Ultra/Hyper), horizontal differencing predictor, and color mode reduction.

**Crush.Tiff** (net9.0, console app) — async CLI for TIFF optimization using `CommandLineParser`. Uses `CrushRunner.RunAsync` from Crush.Core for common CLI operations (cancellation wiring, progress display, file validation, timing).

**FileFormat.Bmp** (net8.0, library) — BMP file format reader/writer. Contains `BmpReader` (parses BITMAPFILEHEADER+BITMAPINFOHEADER, RLE decompression), `BmpWriter` (BMP byte stream assembly), `BmpFile` (data model), `RleCompressor` (RLE8/RLE4 encoder/decoder), `BmpColorMode`, `BmpCompression`, `BmpRowOrder` (enums).

**Optimizer.Bmp** (net8.0, library) — BMP optimization engine. References `FileFormat.Bmp`. Supports 7 color modes (Original, Rgb24, Rgb16_565, Palette8, Palette4, Palette1, Grayscale8), RLE8/RLE4 compression, top-down/bottom-up row order. Palette frequency sorting for optimal RLE runs.

**Crush.Bmp** (net9.0, console app) — async CLI for BMP optimization using `CommandLineParser`. Uses `CrushRunner.RunAsync` from Crush.Core for common CLI operations (cancellation wiring, progress display, file validation, timing).

**FileFormat.Tga** (net8.0, library) — TGA file format reader/writer. Contains `TgaReader` (parses 18-byte header, color map, pixel data, RLE decompression, TGA 2.0 footer), `TgaWriter` (TGA byte stream assembly), `TgaFile` (data model), `TgaRleCompressor` (pixel-width-aware RLE encoder/decoder), `TgaColorMode`, `TgaCompression`, `TgaOrigin` (enums).

**Optimizer.Tga** (net8.0, library) — TGA optimization engine. References `FileFormat.Tga`. Supports 5 color modes (Original, Rgba32, Rgb24, Grayscale8, Indexed8), pixel-width-aware RLE compression, bottom-left/top-left origin.

**Crush.Tga** (net9.0, console app) — async CLI for TGA optimization using `CommandLineParser`. Uses `CrushRunner.RunAsync` from Crush.Core for common CLI operations (cancellation wiring, progress display, file validation, timing).

**FileFormat.Pcx** (net8.0, library) — PCX file format reader/writer. Contains `PcxReader` (parses 128-byte header, RLE scanlines, VGA palette), `PcxWriter` (PCX byte stream assembly), `PcxFile` (data model), `PcxRleCompressor` (PCX RLE encoder/decoder), `PcxColorMode`, `PcxPlaneConfig` (enums).

**Optimizer.Pcx** (net8.0, library) — PCX optimization engine. References `FileFormat.Pcx`. Supports 5 color modes (Original, Rgb24, Indexed8, Indexed4, Monochrome), single-plane/separate-planes configurations, original/frequency-sorted palette ordering.

**Crush.Pcx** (net9.0, console app) — async CLI for PCX optimization using `CommandLineParser`. Uses `CrushRunner.RunAsync` from Crush.Core for common CLI operations (cancellation wiring, progress display, file validation, timing).

**FileFormat.Jpeg** (net8.0, library) — JPEG file format reader/writer. Contains `JpegReader` (JPEG parser via LibJpeg.NET wrapper), `JpegWriter` (lossless transcode + lossy encode), `JpegFile` (data model), `JpegMode`, `JpegSubsampling` (enums). References BitMiracle.LibJpeg.NET.

**Optimizer.Jpeg** (net8.0, library) — JPEG optimization engine. References `FileFormat.Jpeg`. Lossless mode reads DCT coefficients via `jpeg_read_coefficients()` and rewrites with optimized Huffman tables via `jpeg_write_coefficients()`. Lossy mode decodes to pixels and re-encodes at different quality/subsampling/mode combinations. Supports baseline and progressive scan modes, Huffman table optimization, metadata stripping, 4:4:4 and 4:2:0 chroma subsampling.

**Crush.Jpeg** (net9.0, console app) — async CLI for JPEG optimization using `CommandLineParser`. Uses `CrushRunner.RunAsync` from Crush.Core for common CLI operations (cancellation wiring, progress display, file validation, timing).

**FileFormat.Ico** (net8.0, library) — ICO file format reader/writer. Contains `IcoReader` (parses ICO header, directory entries, detects BMP DIB vs PNG embedding via signature sniffing), `IcoWriter` (ICO byte stream assembly: header, directory entries, image data), `IcoFile` (data model), `IcoImage` (per-entry data model), `IcoImageFormat` (Bmp/Png enum), `IcoFileType` (Icon/Cursor internal enum). References FileFormat.Bmp and FileFormat.Png.

**Optimizer.Ico** (net8.0, library) — ICO optimization engine. References `FileFormat.Ico`. For each image entry in the ICO, tries both BMP DIB and PNG embedding formats. Generates 2^n combinations (capped at 256) and tests each in parallel via SemaphoreSlim. Returns the smallest total file size.

**Crush.Ico** (net9.0, console app) — async CLI for ICO optimization using `CommandLineParser`. Uses `CrushRunner.RunAsync` from Crush.Core for common CLI operations (cancellation wiring, progress display, file validation, timing).

**FileFormat.Cur** (net8.0, library) — CUR (cursor) file format reader/writer. Reuses `FileFormat.Ico` internals via `InternalsVisibleTo`. Contains `CurReader` (parses CUR header with type=2 validation, extracts HotspotX/HotspotY from directory entries), `CurWriter` (CUR byte stream assembly with hotspot field override), `CurFile` (data model), `CurImage` (per-entry data model with hotspot coordinates). References FileFormat.Ico.

**Optimizer.Cur** (net8.0, library) — CUR optimization engine. References `FileFormat.Cur`. Same as IcoOptimizer but uses CurReader/CurWriter and preserves hotspot coordinates through optimization. Generates 2^n format combinations (capped at 256) per entry and tests in parallel.

**Crush.Cur** (net9.0, console app) — async CLI for CUR optimization using `CommandLineParser`. Uses `CrushRunner.RunAsync` from Crush.Core for common CLI operations (cancellation wiring, progress display, file validation, timing).

**FileFormat.Ani** (net8.0, library) — ANI animated cursor file format reader/writer. Contains `AniReader` (parses RIFF ACON container, reads anih header, optional rate/sequence chunks, LIST fram with ICO sub-chunks), `AniWriter` (assembles RIFF ACON with anih, rate, seq, and LIST fram chunks), `AniFile` (data model), `AniHeader` (readonly record struct). References FileFormat.Riff and FileFormat.Ico.

**Optimizer.Ani** (net8.0, library) — ANI optimization engine. References `FileFormat.Ani`. For each image entry across all frames, tries BMP vs PNG format. Preserves ANI structure (rates, sequence, header). Generates 2^n format combinations (capped at 256) and tests in parallel.

**Crush.Ani** (net9.0, console app) — async CLI for ANI optimization using `CommandLineParser`. Uses `CrushRunner.RunAsync` from Crush.Core for common CLI operations (cancellation wiring, progress display, file validation, timing).

**FileFormat.WebP** (net8.0, library) — WebP file format reader/writer with full pixel codecs. VP8L lossless: `Vp8LBitReader` (LSB-first 64-bit buffer), `Vp8LDecoder` (LZ77 + Huffman pipeline with 5 code groups), `Vp8LHuffmanTree` (canonical Huffman), `Vp8LTransform` (14 Predictor modes, Color, SubtractGreen, ColorIndexing), `Vp8LEncoder` (SubtractGreen + Huffman + LZ77). VP8 lossy: `Vp8BoolDecoder` (boolean arithmetic range decoder), `Vp8Decoder` (keyframe macroblock parsing, residual decode, reconstruction), `Vp8Dct` (4x4 integer IDCT + Walsh-Hadamard DC), `Vp8IntraPredictor` (DC/V/H/TrueMotion for 4x4 and 16x16), `Vp8LoopFilter` (normal/simple edge filtering), `Vp8Encoder` (basic keyframe encoder). References FileFormat.Riff.

**Optimizer.WebP** (net8.0, library) — WebP optimization engine. Container-level optimization with metadata stripping. References `FileFormat.WebP` and `Crush.Core`. Reads WebP via `WebPReader`, tries metadata stripping (EXIF, ICCP, XMP removal) and RIFF container rewriting, returns smallest result.

**FileFormat.Pdf** (net8.0, library) — PDF embedded image extractor. Contains `PdfReader` (parses xref tables/streams, finds XObject Image resources), `PdfWriter` (minimal PDF wrapping a single image), `PdfFile` (data model implementing `IImageFileFormat<PdfFile>` + `IMultiImageFileFormat<PdfFile>`), `PdfParser` (PDF object parser: numbers, strings, names, arrays, dicts, streams), `PdfXrefParser` (cross-reference table/stream parser). Stream filters: FlateDecode (zlib), DCTDecode (JPEG pass-through), CCITTFaxDecode, ASCII85Decode, ASCIIHexDecode. References Compression.Core, FileFormat.Core, FileFormat.Jpeg, FileFormat.Ccitt.

**FileFormat.WindowsPe** (net8.0, library) — PE (EXE/DLL) resource image extractor. Contains `PeResourceReader` (parses DOS/PE headers, .rsrc section, RT_ICON/RT_GROUP_ICON/RT_CURSOR/RT_BITMAP resources), `PeResourceFile` (data model implementing `IImageFileFormat<PeResourceFile>` + `IMultiImageFileFormat<PeResourceFile>`). Extracts icons, cursors, bitmaps, and other embedded image resources. References FileFormat.Core, FileFormat.Bmp, FileFormat.Ico.

**Crush.WebP** (net9.0, console app) — async CLI for WebP optimization using `CommandLineParser`. Uses `CrushRunner.RunAsync` from Crush.Core for common CLI operations (cancellation wiring, progress display, file validation, timing).

**FileFormat.Qoi** (net8.0, library) — QOI (Quite OK Image) reader/writer. 14-byte header, 4 opcodes (INDEX/DIFF/LUMA/RUN + RGB/RGBA), 8-byte end marker.

**FileFormat.Farbfeld** (net8.0, library) — Farbfeld reader/writer. 16-byte header ("farbfeld" magic), raw RGBA16 big-endian pixels, zero compression.

**FileFormat.Wbmp** (net8.0, library) — WBMP (Wireless Bitmap) reader/writer. Variable-length header with multi-byte integer encoding, 1bpp monochrome.

**FileFormat.Netpbm** (net8.0, library) — Netpbm (PBM/PGM/PPM/PAM) reader/writer. Supports all 7 sub-formats (P1-P7), text-based headers.

**FileFormat.Xbm** (net8.0, library) — X BitMap reader/writer. C source text format, 1bpp, `#define` dimensions, hex array.

**FileFormat.Xpm** (net8.0, library) — X PixMap reader/writer. C source text format, indexed color, string-based color keys (XPM3).

**FileFormat.MacPaint** (net8.0, library) — MacPaint reader/writer. Fixed 576x720 monochrome, 512-byte brush patterns header, PackBits compression.

**FileFormat.ZxSpectrum** (net8.0, library) — ZX Spectrum screen dump reader/writer. Fixed 6912-byte format, 256x192, 1bpp with 8x8 attribute blocks, character-line interleaved layout.

**FileFormat.Koala** (net8.0, library) — Commodore 64 Koala Painter reader/writer. Fixed 10003-byte format, 160x200 multicolor, 16 C64 colors.

**FileFormat.Degas** (net8.0, library) — Atari ST DEGAS/DEGAS Elite reader/writer. 34-byte header, 16-word palette, raw or PackBits compressed planar data.

**FileFormat.DaliST** (net8.0, library) — Atari ST Dali image format reader/writer. Multi-resolution support: SD0 (320x200, 4 planes, 16 colors), SD1 (640x200, 2 planes, 4 colors), SD2 (640x400, 1 plane, mono). 32-byte palette (16 words BE, 12-bit ST RGB) + 32000 bytes raw planar pixel data = 32032 bytes total.

**FileFormat.MultiPalettePicture** (net8.0, library) — Atari STe Multi Palette Picture (MPP) reader/writer. Per-scanline palette changes, 320x200 low-res, 4 planes. File structure: 200 records of 160 bytes pixel data + 32 bytes palette = 192 bytes per scanline, 38400 bytes total. 12-bit STe RGB palettes (4-bit per channel).

**FileFormat.DrawIt** (net8.0, library) — DrawIt (DIT) indexed color image reader/writer. 4-byte header (width LE uint16, height LE uint16) + 768-byte RGB palette (256 entries x 3 bytes) + 8-bit indexed pixel data. Variable dimensions.

**FileFormat.Neochrome** (net8.0, library) — Atari ST NEOchrome reader/writer. 128-byte header, 320x200, 16 colors, 32000 bytes raw planar.

**FileFormat.GemImg** (net8.0, library) — GEM Raster Image reader/writer. Word-aligned header, scan-line encoding with vertical RLE and pattern replication.

**FileFormat.AmstradCpc** (net8.0, library) — Amstrad CPC screen memory dump reader/writer. CPC memory interleave, pixel packing for Mode 0/1/2.

**FileFormat.Pfm** (net8.0, library) — Portable Float Map reader/writer. Text header, float32 pixel data, endianness indicated by scale sign.

**FileFormat.Sgi** (net8.0, library) — Silicon Graphics Image reader/writer. 512-byte header, channel-plane RLE, big-endian.

**FileFormat.SunRaster** (net8.0, library) — Sun Raster reader/writer. 32-byte header, escape-based RLE (0x80 escape byte), big-endian.

**FileFormat.Hdr** (net8.0, library) — Radiance HDR/RGBE reader/writer. Text header (#?RADIANCE), RGBE pixel encoding, scanline RLE.

**FileFormat.UtahRle** (net8.0, library) — Utah Raster Toolkit reader/writer. Magic 0x52CC, multi-channel support, scanline operations.

**FileFormat.DrHalo** (net8.0, library) — Dr. Halo CUT reader/writer. 8-bit indexed, per-scanline RLE, separate .PAL palette file.

**FileFormat.Iff** (net8.0, library) — IFF container reader/writer. Big-endian chunk-based, FORM/LIST/CAT groups, 2-byte alignment.

**FileFormat.Ilbm** (net8.0, library) — IFF ILBM reader/writer. Planar bitmap, ByteRun1 (PackBits) compression, BMHD/CMAP/CAMG/BODY chunks. HAM6/HAM8 decode via HamDecoder, Extra Half-Brite (EHB) support, CAMG viewport mode bits.

**FileFormat.Fli** (net8.0, library) — Autodesk FLI/FLC animation reader/writer. 128-byte header, frame-differential encoding, COLOR/DELTA/BYTE_RUN chunks.

**FileFormat.Cineon** (net8.0, library) — Kodak Cineon reader/writer. 1024-byte header, 10-bit log film scanning, big-endian.

**FileFormat.Dds** (net8.0, library) — DirectDraw Surface reader/writer. 128-byte header (4 magic + 124 DdsHeader), optional 20-byte DX10 header with DXGI format mapping, GPU textures as raw blocks. Supports DXT1/DXT3/DXT5, BC4/BC5 (ATI1/ATI2), BC6H (HDR unsigned/signed), BC7, and uncompressed RGB/RGBA. FourCC detection for legacy formats plus DX10 DXGI format mapping for modern formats.

**FileFormat.Vtf** (net8.0, library) — Valve Texture Format reader/writer. VTF 7.x, mipmaps, BCn + custom formats, 64-byte header.

**FileFormat.Ktx** (net8.0, library) — KTX1 + KTX2 GPU texture container reader/writer. Auto-detects version from 12-byte identifier, key-value metadata.

**FileFormat.Exr** (net8.0, library) — OpenEXR reader/writer. Single-part scanline, None compression, Half/Float/UInt pixel types.

**FileFormat.Dpx** (net8.0, library) — Digital Picture Exchange reader/writer. 2048-byte header, BE/LE detection from magic, 10-bit packed pixels.

**FileFormat.Fits** (net8.0, library) — FITS (astronomy) reader/writer. 2880-byte block headers with 80-char keyword cards, big-endian multi-dimensional arrays.

**FileFormat.Ccitt** (net8.0, library) — CCITT Group 3/4 fax reader/writer. ITU-T T.4/T.6 Huffman coding, 1bpp bi-level images.

**FileFormat.BbcMicro** (net8.0, library) — BBC Micro screen dump reader/writer. Character-block layout, Mode 0/1/2/4/5, 8x8 pixel blocks.

**FileFormat.C64Multi** (net8.0, library) — C64 multiformat art reader/writer. Art Studio Hires (9009 bytes), Art Studio Multicolor (10018 bytes).

**FileFormat.Psd** (net8.0, library) — Adobe Photoshop reader/writer. Flat composite image, 26-byte header, RLE/Raw compression, 8 color modes.

**FileFormat.Hrz** (net8.0, library) — HRZ (Slow-Scan Television) reader/writer. No header, fixed 256x240, raw RGB, exactly 184,320 bytes.

**FileFormat.Cmu** (net8.0, library) — CMU Window Manager Bitmap reader/writer. 8-byte header, 1bpp packed pixels MSB-first, big-endian dimensions.

**FileFormat.Mtv** (net8.0, library) — MTV Ray Tracer reader/writer. ASCII text "width height\n" header, raw RGB pixel data.

**FileFormat.Qrt** (net8.0, library) — QRT Ray Tracer reader/writer. 10-byte header with LE dimensions and 6 reserved bytes, raw RGB pixels.

**FileFormat.Msp** (net8.0, library) — Microsoft Paint v1/v2 reader/writer. 32-byte header, monochrome 1bpp. V1 uncompressed, V2 per-scanline RLE with scan-line map.

**FileFormat.Dcx** (net8.0, library) — Multi-page PCX container reader/writer. 0x3ADE68B1 magic, up to 1023 page offsets, embeds PCX files. References FileFormat.Pcx.

**FileFormat.Astc** (net8.0, library) — Adaptive Scalable Texture Compression container reader/writer. 16-byte header with 0x5CA1AB13 magic, uint24 LE dimensions, raw 16-byte ASTC blocks.

**FileFormat.Pkm** (net8.0, library) — Ericsson Texture Container reader/writer. 16-byte header with "PKM " magic, version "10"/"20", ETC1/ETC2 format blocks.

**FileFormat.Tim** (net8.0, library) — PlayStation 1 Texture reader/writer. 8-byte header with 0x10 magic, 4/8/16/24-bit modes, optional CLUT palette block, VRAM width conversion.

**FileFormat.Tim2** (net8.0, library) — PlayStation 2/PSP Texture reader/writer. "TIM2" magic, 16-byte file header, 48-byte picture headers, multi-picture support with palettes.

**FileFormat.Wal** (net8.0, library) — Quake 2 Texture reader/writer. 100-byte header, 8-bit indexed with 4 mipmap levels, no embedded palette (uses external Quake 2 palette).

**FileFormat.Pvr** (net8.0, library) — PowerVR Texture v3 reader/writer. 52-byte header with 0x03525650 magic, uint64 pixel format, GPU texture container for PVRTC/ETC/ASTC formats.

**FileFormat.Wpg** (net8.0, library) — WordPerfect Graphics reader/writer. 16-byte header with FF 57 50 43 magic, record-based structure, RLE-compressed bitmap records (Type 1 and Type 2).

**FileFormat.Bsave** (net8.0, library) — IBM PC BSAVE Graphics reader/writer. 7-byte header with 0xFD magic, segment:offset addressing, screen memory dump with automatic video mode detection (CGA/EGA/VGA).

**FileFormat.Clp** (net8.0, library) — Windows Clipboard reader/writer. 4-byte header with 0xC350 file ID, format directory with CF_DIB bitmap format, embedded DIB data.

**FileFormat.Spectrum512** (net8.0, library) — Atari ST 512-color reader/writer. SPU format: 51,104 bytes, 320x199, 4-plane planar data + 48 palette entries per scanline (12-bit STE RGB).

**FileFormat.Tiny** (net8.0, library) — Atari ST Compressed DEGAS reader/writer. Resolution byte + 16-word palette + delta+word-level RLE per bitplane. Supports Low/Medium/High resolutions.

**FileFormat.SyntheticArts** (net8.0, library) — Atari ST Synthetic Arts medium resolution reader/writer. Fixed 32032-byte format: 32-byte palette (16 words BE, only first 4 used) + 32000 bytes word-interleaved 2-plane planar data, 640x200, 4 colors. Contains `SyntheticArtsReader` (validates exact size, FromFile/FromStream/FromBytes), `SyntheticArtsWriter` (returns exactly 32032 bytes), `SyntheticArtsFile` (data model implementing `IImageFileFormat<SyntheticArtsFile>`, Palette short[16], PixelData byte[32000]). ToRawImage: Indexed8 with 4-color palette. FromRawImage: requires Indexed8 640x200. Extensions: .srt. References FileFormat.Core.

**FileFormat.HighresMedium** (net8.0, library) — Atari ST interlaced medium resolution reader/writer. Fixed 64064-byte format: 2 frames of (32-byte palette + 32000 bytes planar), 640x200. Contains `HighresMediumReader` (validates exact size, FromFile/FromStream/FromBytes), `HighresMediumWriter` (returns exactly 64064 bytes), `HighresMediumFile` (data model implementing `IImageFileFormat<HighresMediumFile>`, Palette1/PixelData1/Palette2/PixelData2). ToRawImage: Rgb24 via RGB-average blend of both frames. FromRawImage: requires Rgb24 640x200, quantizes to 4 colors. Extensions: .hrm. References FileFormat.Core.

**FileFormat.FullscreenKit** (net8.0, library) — Atari ST Fullscreen Construction Kit overscan reader/writer. 32-byte palette (16 words BE) + planar pixel data (4 planes). Two supported variants: 416x274 (57024 bytes) and 448x272 (60928 bytes). Auto-detects variant from file size. Contains `FullscreenKitReader` (validates file size against both variants, FromFile/FromStream/FromBytes), `FullscreenKitWriter` (assembles palette + pixel data), `FullscreenKitFile` (data model implementing `IImageFileFormat<FullscreenKitFile>`, Width, Height, Palette, PixelData). ToRawImage: Indexed8 with 16-color palette. FromRawImage: requires Indexed8 416x274 or 448x272. Extensions: .kid. References FileFormat.Core.

**FileFormat.Sixel** (net8.0, library) — DEC Terminal Graphics reader/writer. Text-based ESC P params q sixel-data ESC \ encoding, 6-pixel vertical bands, RLE compression, HLS/RGB color definitions.

**FileFormat.Wad** (net8.0, library) — Doom WAD container reader/writer. "IWAD"/"PWAD" magic, 12-byte header, directory of 16-byte entries with named lumps.

**FileFormat.Wad3** (net8.0, library) — Half-Life WAD3 texture container reader/writer. "WAD3" magic, 12-byte header, 32-byte directory entries, MipTex structures with 4 mipmap levels and embedded 256-color palette.

**FileFormat.Apng** (net8.0, library) — Animated PNG reader/writer. Extends PNG with acTL (animation control), fcTL (frame control), and fdAT (frame data) chunks. Frame 0 uses IDAT, subsequent frames use fdAT. References FileFormat.Png and Compression.Core.

**FileFormat.Mng** (net8.0, library) — Multiple Network Graphics (VLC subset) reader/writer. MNG 8-byte signature, MHDR 28-byte chunk, embedded complete PNG frames, MEND terminator.

**FileFormat.Xcf** (net8.0, library) — GIMP Native image reader/writer (flat composite). "gimp xcf v###\0" magic, big-endian, 64x64 pixel tiles, per-channel RLE or zlib compression. Reads bottom-most visible layer.

**FileFormat.Pict** (net8.0, library) — Apple QuickDraw PICT reader/writer (raster subset). 512-byte preamble, PICT2 opcodes (PackBitsRect for indexed, DirectBitsRect for RGB), PackBits per-component compression.

**FileFormat.Dicom** (net8.0, library) — DICOM medical imaging reader/writer (basic subset). 128-byte preamble + "DICM" magic, Explicit VR Little Endian tag-length-value elements, single-frame uncompressed pixel data.

**FileFormat.Trs80** (net8.0, library) — TRS-80 hi-res graphics screen dump reader/writer (Model I/III). No header, raw pixel data. Fixed 6144-byte format: 128x48 character cells, each byte encodes a 2x3 pixel block (effective resolution 256x144). Bit mapping: bit 0=top-left, bit 1=top-right, bit 2=mid-left, bit 3=mid-right, bit 4=bot-left, bit 5=bot-right (bits 6-7 ignored). Contains `Trs80Reader` (validates exact 6144-byte size, FromFile/FromStream/FromBytes), `Trs80Writer` (returns exactly 6144 bytes), `Trs80File` (data model implementing `IImageFileFormat<Trs80File>`, fixed 256x144, RawData byte[]). ToRawImage: Indexed1 with B&W palette (2 entries), 32 bytes per row. FromRawImage: requires Indexed1 256x144, packs 2x3 pixel blocks into character cells. Extensions: .hr. References FileFormat.Core.

**FileFormat.SoftImage** (net8.0, library) — Softimage PIC 3D texture format reader/writer. 100-byte big-endian header with 0x5380F634 magic, float32 version, 80-byte ASCII comment, uint16 BE width/height. Channel info packets (4 bytes each: chained, size, type, channel bitmask). Mixed RLE pixel data encoding (count < 128: literal pixels, count >= 128: run-length repeat). Contains `SoftImageReader` (validates magic, parses BE header, reads channel info packets, decodes mixed RLE), `SoftImageWriter` (assembles 100-byte header, channel info packets, mixed RLE encoded pixel data), `SoftImageFile` (data model implementing `IImageFileFormat<SoftImageFile>`: Width, Height, PixelData, Comment, HasAlpha, Version). ToRawImage: Rgb24 or Rgba32 based on alpha channel presence. FromRawImage: accepts Rgb24 or Rgba32. Extensions: .pic. References FileFormat.Core.

**Compression.Tests** (net8.0, NUnit 4) — tests for `ZopfliDeflater`: BitWriter, symbol tables, Huffman trees, hash chain, round-trip compression, compression ratios, convergence detection.

**Optimizer.Png.Tests** (net10.0-windows, NUnit 4) — comprehensive test suite with unit, regression, end-to-end, performance, and ditherer expansion tests for PNG optimization. Uses `StressTest.png` from `Fixtures/` as integration test fixture.

**Optimizer.Gif.Tests** (net8.0-windows, NUnit 4) — GIF reader tests (manual GIF construction), LZW round-trip, palette reorderer, frame optimizer, LZW compressor, end-to-end tests.

**Optimizer.Tiff.Tests** (net8.0, NUnit 4) — PackBits round-trip, TIFF optimizer integration, end-to-end tests with LibTiff readback.

**Optimizer.Bmp.Tests** (net8.0, NUnit 4) — RLE8/RLE4 round-trip, compression ratio estimation, optimizer combo generation, color mode detection, E2E with System.Drawing readback.

**Optimizer.Tga.Tests** (net8.0, NUnit 4) — TGA RLE round-trip (1/3/4 byte widths), optimizer combo generation, alpha detection, E2E with custom TGA readback.

**Optimizer.Pcx.Tests** (net8.0, NUnit 4) — PCX RLE round-trip, optimizer combo generation, plane config pruning, E2E with custom PCX readback.

**Optimizer.Jpeg.Tests** (net8.0, NUnit 4) — JpegWriter lossy/lossless encoding, Huffman optimization, optimizer combo generation, E2E with System.Drawing readback, cancellation, input validation.

**Optimizer.Ico.Tests** (net8.0, NUnit 4) — ICO optimizer input validation, combo generation, E2E with IcoReader readback, cancellation, progress reporting.

**FileFormat.Ico.Tests** (net8.0, NUnit 4) — ICO reader validation (null, missing, too small, invalid type), writer header/dimensions, round-trip (single PNG, multiple entries, BMP DIB, 256x256), data type tests.

**FileFormat.Cur.Tests** (net8.0, NUnit 4) — CUR reader validation (null, missing, too small, invalid type), writer header type/hotspot/count, round-trip with hotspot preservation (single, multiple, 256x256, PNG data).

**Optimizer.Cur.Tests** (net8.0, NUnit 4) — CUR optimizer input validation, E2E with CurReader readback, hotspot preservation through optimization, cancellation.

**FileFormat.Ani.Tests** (net8.0, NUnit 4) — ANI reader validation (null, missing, too small, invalid form type), writer output verification (RIFF signature, ACON form type, anih chunk), round-trip tests (single/multiple frames, rates, sequence, header fields), data type tests.

**Optimizer.Ani.Tests** (net8.0, NUnit 4) — ANI optimizer input validation, E2E with single/multiple frames, result validity via AniReader readback, cancellation.

**FileFormat.WebP.Tests** (net8.0, NUnit 4) — 21 tests: reader validation (null, missing, too small, invalid form type, no image chunk), writer output (RIFF signature, WEBP form type, VP8/VP8L chunks, VP8X extended), round-trip (lossless, lossy, metadata preserved, metadata stripped), data type tests, VP8/VP8L dimension parsing.

**Optimizer.WebP.Tests** (net8.0, NUnit 4) — 9 tests: input validation (null file, missing file, null WebPFile), E2E (lossless, lossy, valid WebP readback, metadata stripping, progress reporting, cancellation).

### Key Types in Compression.Core

- `ZopfliDeflater` (sealed partial) — custom Zopfli-class DEFLATE encoder producing standard zlib-wrapped output. Uses direct O(1) lookup tables for length/distance code mapping, distance-aware lazy matching in greedy parse (compares estimated bit cost of emitting current match vs literal+next match using fixed Huffman code lengths via `_EstimateLiteralCost`/`_EstimateMatchCost`), adaptive hash chain depth based on local data entropy (64-byte window diversity thresholds), multi-length DP optimal parsing with ArrayPool-backed DP arrays and count-then-fill traceback, iterative refinement with convergence detection (early exit when parse stabilizes), cached fixed Huffman trees, pre-reversed Huffman codes for fast LSB-first output, cost-only arithmetic block measurement (no throwaway BitWriter passes), ArrayPool-backed greedy parse, cached RLE encoding via `DynamicHeader` struct (computes RLE once in `_BuildDynamicHeader`, reuses for both measurement and writing), and Huffman-cost block splitting with statistical candidate detection (sliding window L1 frequency divergence) plus arithmetic measurement and fixed/dynamic selection per block. Organized as nested types: `BitWriter` (LSB-first bit output with bulk byte-aligned writes and smarter buffer growth), `HashChain` (LZ77 match finder with Knuth multiplicative hash, secondary-byte quick reject to skip ~50% more false positives, and adaptive depth via `EstimateLocalDepth`), `HuffmanTree` (tree-based Package-Merge length-limited Huffman with O(n) item storage instead of O(n^2) coverage arrays, precomputed reversed codes), `OptimalParser` (forward-DP shortest-path with multi-length expansion, pooled DP arrays, and adaptive hash chain depth), `BlockSplitter` (Huffman-tree-cost DP block splitting with statistical candidate detection via sliding window frequency divergence). Ultra mode: 2-pass DP with dual hash chain depths. Hyper mode: parallel hash chain construction, starts from Ultra result, N-iteration refinement with convergence detection + block splitting with per-block reparse (ArrayPool-backed sub-arrays, re-optimizes each block's LZ77 parse using block-specific Huffman trees) + arithmetic measurement, always picks the smallest of (Ultra single-block, Hyper single-block, Hyper block-split).

### Key Types in Optimizer.Png

- `PngOptimizer` (partial, sealed) — main engine. `OptimizeAsync(CancellationToken, IProgress<OptimizationProgress>?)` is the public entry point. Supports cancellation via `CancellationToken` (propagated to `SemaphoreSlim.WaitAsync` and checked between phases) and progress reporting via `IProgress<OptimizationProgress>` (thread-safe combo count and best size tracking with `Interlocked`). Validates constructor input (`ArgumentNullException.ThrowIfNull`). Handles pixel conversion, palette quantization (alpha-aware with two-tier frequency sort), FrameworkExtensions dithering dispatch, tRNS chunk generation (RGB key color and Grayscale key color for binary alpha images). Partial classes: `ArgbPixel` (pixel struct), `PooledMemoryStream` (expandable stream wrapper).
- `MedianCutQuantizer` — median-cut color quantization algorithm. Builds a histogram, splits color-space boxes along the widest axis at the median frequency, and produces a reduced palette. Used when `AllowLossyPalette` is enabled and the image has >256 unique colors.
- `QuantizerDithererCombo` (readonly record struct) — identifies a quantizer/ditherer pair by name for FrameworkExtensions-based lossy quantization.
- `FilterTools` — static implementations of the 5 PNG row filters (None, Sub, Up, Average, Paeth). SIMD-accelerated Sub, Up, Average, and Paeth filters via `System.Numerics.Vector<byte>` (Paeth uses `Vector<ushort>` widening for signed arithmetic) with scalar fallback. Uses `ArrayPool<byte>` for temporary buffers.
- `PngFilterOptimizer` — applies a chosen `FilterStrategy` across all scanlines. Supports `SingleFilter`, `ScanlineAdaptive`, `WeightedContinuity`, `BruteForce` (compression-verified), and `BruteForceAdaptive` (per-scanline with 16-row lookahead compression for ambiguous rows where top-2 scores are within 15%).
- `PngFilterSelector` — heuristic-based per-scanline filter selection. Deflate-aware scoring (`CalculateDeflateAwareScore`) with zero-run quadratic bonus, non-zero run bonus (runLength^1.5 for runs ≥4), byte-pair frequency bonus for LZ77 matching, high-diversity penalty (16-byte windows with >14 unique values), and circular distance for filter residuals. Supports weighted continuity, early break on perfect zero rows, stickiness optimization for spatially consistent content.
- `PngPaletteReorderer` — palette reordering strategies for indexed PNG images: HilbertCurve (3D color-space Z-order), SpatialLocality (first-occurrence order), DeflateOptimized (tries each ordering, compresses 16-row sample, picks smallest).
- `ImageStats` (readonly record struct) — pixel statistics: `UniqueColors`, `UniqueArgbColors`, `HasAlpha`, `IsGrayscale`, `TransparentKeyColor` (RGB key for binary alpha), `TransparentKeyGray` (grayscale key for binary alpha).
- `ImagePartitioner` — content-aware row partitioning for the `PartitionOptimized` filter strategy.
- `OptimizationCombo` (readonly record struct) — one combination of `PngColorType`, bit depth, `FilterStrategy`, `DeflateMethod`, `PngInterlaceMethod`, optional `QuantizerDithererCombo`.
- `OptimizationResult` (readonly record struct) — winning combination's metadata, file bytes, and optional `LossyPaletteCombo`.
- `PngOptimizationOptions` (sealed record) — user-facing configuration (includes `AllowLossyPalette`, `UseDithering`, `QuantizerNames`, `DithererNames`, `IsHighQualityQuantization`, `PreserveAncillaryChunks`, `EnableTwoPhaseOptimization`, `Phase2CandidateCount`, `OptimizePaletteOrder`).

### Key Types in Optimizer.Gif

- `GifOptimizer` — main engine with `FromFile()` (input validation: null check, file existence, corrupt file wrapping), `OptimizeAsync(CancellationToken, IProgress<GifOptimizationProgress>?)`, parallel combo testing. Combo axes include palette strategy, frame differencing, compression-aware disposal, deferred LZW clear codes.
- `GifOptimizationProgress` (readonly record struct) — progress report: `CombosCompleted`, `CombosTotal`, `BestSizeSoFar` (long), `Phase`.
- `PaletteReorderer` — implements 7 palette reorder strategies (Original, FrequencySorted, LuminanceSorted, SpatialLocality, LzwRunAware, HilbertCurve, CompressionOptimized). CompressionOptimized brute-forces all heuristic orderings via actual LZW compression.
- `LzwCompressor` — standalone LZW encoder with deferred clear code support (defers table reset until compression ratio degrades, adaptive check interval starting at 64 doubling to 1024). Uses an 8192-slot open-addressing hash table with generation counter for O(1) table resets.
- `GifFrameDifferencer` — computes pixel differences between consecutive frames, replacing unchanged pixels with transparent index for better LZW compression.
- `GifFrameOptimizer` — frame disposal optimization (including compression-aware greedy forward pass), transparent margin trimming, frame deduplication (palette-aware: resolves effective palettes and compares visual output for frames with different palette orderings or GCT vs LCT), GCT vs LCT selection.
- `GifAssembler` — assembles complete GIF byte stream from optimized components.
- `GifOptimizationOptions` (sealed record) — configuration: palette strategies, disposal optimization, margin trimming, frame differencing, deferred clear codes, deduplication, parallelism, `EnableTwoPhaseOptimization`, `Phase2CandidateCount`.

### Key Types in Optimizer.Tiff

- `TiffOptimizer` — main engine with `FromFile()` (input validation: null check, file existence), `OptimizeAsync(CancellationToken, IProgress<OptimizationProgress>?)`, parallel combo testing. Supports RGB, Grayscale, Palette, BiLevel color modes. Features palette frequency sorting, PackBits cost estimation, dynamic strip sizing, and tiled encoding support.
- `TiffOptimizationOptions` (sealed record) — configuration: compressions, predictors, strip row counts, dynamic strip sizing, tile support, auto color mode, Zopfli iterations, `EnableTwoPhaseOptimization`, `Phase2CandidateCount`.

### Key Types in Optimizer.Bmp

- `BmpOptimizer` — main engine with `FromFile()`, `OptimizeAsync()`, parallel combo testing. Supports 7 color modes with RLE4/RLE8 compression and top-down/bottom-up row order. Combo pruning: RLE8 only with Palette8, RLE4 only with Palette4, palette modes only when colors fit.

### Key Types in Optimizer.Tga

- `TgaOptimizer` — main engine with `FromFile()`, `OptimizeAsync()`, parallel combo testing. Supports 5 color modes with RLE compression and origin variants. Detects alpha channel and grayscale content for combo pruning.

### Key Types in Optimizer.Pcx

- `PcxOptimizer` — main engine with `FromFile()`, `OptimizeAsync()`, parallel combo testing. Supports 5 color modes with single-plane/separate-planes configurations. SeparatePlanes only for RGB24, palette ordering only for indexed modes.

### Key Types in Optimizer.Jpeg

- `JpegOptimizer` — main engine with `FromFile()` (lossless + lossy) and `Bitmap` constructor (lossy only). `OptimizeAsync()` generates lossless combos (mode x Huffman x strip metadata) when input JPEG bytes are available, and lossy combos (mode x quality x subsampling) when `AllowLossy` is enabled.
- `JpegOptimizationOptions` (sealed record) — configuration: modes, qualities, subsamplings, allow lossy, min quality, strip metadata, max parallel tasks.

### Key Types in Optimizer.Ico

- `IcoOptimizer` — main engine with `FromFile()` (input validation: null check, file existence), `OptimizeAsync(CancellationToken, IProgress<OptimizationProgress>?)`, parallel combo testing. Generates 2^n format combinations (BMP vs PNG per entry, capped at 256). Each combo reassembles the ICO with specified formats via IcoWriter.
- `IcoOptimizationOptions` (sealed record) — configuration: max parallel tasks.
- `IcoOptimizationCombo` (readonly record struct) — one combination of `IcoImageFormat[]` specifying BMP or PNG per entry.
- `IcoOptimizationResult` (readonly record struct) — winning combination's metadata, file bytes, and entry formats.

### Key Types in Optimizer.Cur

- `CurOptimizer` — main engine with `FromFile()` (input validation: null check, file existence), `OptimizeAsync(CancellationToken, IProgress<OptimizationProgress>?)`, parallel combo testing. Same as IcoOptimizer but uses CurReader/CurWriter and preserves hotspot coordinates. Generates 2^n format combinations (capped at 256).
- `CurOptimizationOptions` (sealed record) — configuration: max parallel tasks.
- `CurOptimizationCombo` (readonly record struct) — one combination of `IcoImageFormat[]` specifying BMP or PNG per entry.
- `CurOptimizationResult` (readonly record struct) — winning combination's metadata, file bytes, and entry formats.

### Key Types in FileFormat.Ani

- `AniFile` (sealed class) — data model: `Header` (AniHeader), `Frames` (IReadOnlyList of IcoFile), optional `Rates` (int[]), optional `Sequence` (int[]).
- `AniHeader` (readonly record struct) — ANI header: `NumFrames`, `NumSteps`, `Width`, `Height`, `BitCount`, `DisplayRate`, `HasSequence`.
- `AniReader` — static parser with `FromFile(FileInfo)`, `FromStream(Stream)`, `FromBytes(byte[])`. Validates RIFF ACON form type, parses anih header (36 bytes), optional rate/seq chunks, LIST fram with ICO sub-chunks via IcoReader.
- `AniWriter` — static assembler with `ToBytes(AniFile)`. Builds RIFF ACON container with anih chunk, optional rate/seq chunks, LIST fram with ICO frames via IcoWriter.

### Key Types in Optimizer.Ani

- `AniOptimizer` — main engine with `FromFile()` (input validation: null check, file existence), `OptimizeAsync(CancellationToken, IProgress<OptimizationProgress>?)`, parallel combo testing. For each image entry across all frames, tries BMP vs PNG format. Preserves rates, sequence, and header. Generates 2^n format combinations (capped at 256).
- `AniOptimizationOptions` (sealed record) — configuration: max parallel tasks.
- `AniOptimizationCombo` (readonly record struct) — one combination of `IcoImageFormat[]` specifying BMP or PNG per entry across all frames.
- `AniOptimizationResult` (readonly record struct) — winning combination's metadata, file bytes, and entry formats.

### Key Types in Optimizer.WebP

- `WebPOptimizer` — main engine with `FromFile()` (input validation: null check, file existence), `OptimizeAsync(CancellationToken, IProgress<OptimizationProgress>?)`, parallel combo testing. Phase 2: container-level optimization only (no pixel encode/decode). Tries metadata stripping and RIFF container rewriting.
- `WebPOptimizationOptions` (sealed record) — configuration: max parallel tasks, strip metadata flag.
- `WebPOptimizationCombo` (readonly record struct) — one combination: `StripMetadata` (bool).
- `WebPOptimizationResult` (readonly record struct) — winning combination's metadata, file bytes, and metadata stripped flag.

### Key Types in Crush.Core

- `CrushRunner` — shared optimization runner for all CLI tools. Handles input file validation, output directory validation, `CancellationTokenSource` wiring with `Console.CancelKeyPress`, `IProgress<OptimizationProgress>` reporter, `Stopwatch` timing, file size reduction display. Generic `RunAsync<TResult>` method with callbacks for format-specific optimization, result extraction, verbose display, and result formatting.
- `OptimizationProgress` (readonly record struct) — shared progress report used by all optimizers: `CombosCompleted`, `CombosTotal`, `BestSizeSoFar` (long), `Phase`.
- `ICrushOptions` — common CLI option interface: `InputFile`, `OutputFile`, `ParallelTasks`, `Verbose`.
- `FileFormatting` — static helper with `FormatFileSize(long)` for human-readable file sizes (B, KiB, MiB, GiB).

### Key Types in FileFormat Libraries

Each FileFormat library follows a consistent pattern with `{Format}File` (data model), `{Format}Reader` (static parser with `FromFile`/`FromStream`/`FromBytes`), and `{Format}Writer` (static assembler with `ToBytes`).

- `PngFile`, `PngReader`, `PngWriter` — FileFormat.Png. PngWriter also exposes internal `Assemble()` for PngOptimizer's pre-filtered/pre-compressed fast path.
- `PngChunkReader`, `PngChunk` — FileFormat.Png. Chunk parsing and ancillary chunk preservation.
- `PngColorType`, `PngInterlaceMethod`, `PngFilterType` — FileFormat.Png. Spec-aligned enums (formerly ColorMode, InterlaceMethod, FilterType in Optimizer.Png).
- `Adam7` — FileFormat.Png. Adam7 interlace pass definitions.
- `BmpFile`, `BmpReader`, `BmpWriter` — FileFormat.Bmp. Includes `RleCompressor` for RLE8/RLE4.
- `TgaFile`, `TgaReader`, `TgaWriter` — FileFormat.Tga. Includes `TgaRleCompressor` for pixel-width-aware RLE.
- `PcxFile`, `PcxReader`, `PcxWriter` — FileFormat.Pcx. Includes `PcxRleCompressor` for PCX RLE.
- `JpegFile`, `JpegReader`, `JpegWriter` — FileFormat.Jpeg. `JpegWriter` provides `LosslessTranscode()` and `LossyEncode()`.
- `TiffFile`, `TiffReader`, `TiffWriter` — FileFormat.Tiff. `TiffWriter` supports custom raw strip/tile writing for Zopfli integration. Includes `PackBitsCompressor`.
- `IcoFile`, `IcoReader`, `IcoWriter` — FileFormat.Ico. Supports BMP DIB and PNG embedded image formats. `IcoReader` auto-detects format via PNG signature sniffing.
- `CurFile`, `CurReader`, `CurWriter` — FileFormat.Cur. CUR format identical to ICO except type=2 and hotspot fields in directory entries. Reuses IcoReader._Parse and IcoWriter._Assemble via InternalsVisibleTo.
- `AniFile`, `AniReader`, `AniWriter` — FileFormat.Ani. RIFF ACON container with anih header, optional rate/seq chunks, LIST fram with ICO frames. Uses FileFormat.Riff and FileFormat.Ico.
- `WebPFile`, `WebPReader`, `WebPWriter` — FileFormat.WebP. RIFF WEBP container with full VP8L (lossless) and VP8 (lossy) pixel codecs. VP8L: `Vp8LDecoder`, `Vp8LEncoder`, `Vp8LBitReader`, `Vp8LHuffmanTree`, `Vp8LTransform`. VP8: `Vp8Decoder`, `Vp8Encoder`, `Vp8BoolDecoder`, `Vp8Dct`, `Vp8IntraPredictor`, `Vp8LoopFilter`. Uses FileFormat.Riff.
- `WebPFeatures` — FileFormat.WebP. Readonly record struct with Width, Height, HasAlpha, IsLossless, IsAnimated.
- `PdfFile`, `PdfReader`, `PdfWriter` — FileFormat.Pdf. PDF image extractor with xref parsing, XObject Image resources, stream filter decoding (FlateDecode, DCTDecode, CCITTFaxDecode, ASCII85Decode, ASCIIHexDecode). Implements `IMultiImageFileFormat<PdfFile>`.
- `PeResourceFile`, `PeResourceReader` — FileFormat.WindowsPe. PE resource extractor for icons, cursors, bitmaps from EXE/DLL .rsrc sections. Implements `IMultiImageFileFormat<PeResourceFile>`.
- `RiffFile`, `RiffReader`, `RiffWriter` — FileFormat.Riff. Generic RIFF container with `FourCC` four-character codes, recursive LIST/chunk structure, word-aligned offsets.
- `WbmpFile`, `WbmpReader`, `WbmpWriter` — FileFormat.Wbmp. WBMP (Wireless Bitmap) monochrome 1bpp format with variable-length multi-byte integer encoded dimensions. `WbmpMultiByteInt` handles 7-bit encoding with continuation bits.
- `C64MultiFile`, `C64MultiReader`, `C64MultiWriter` — FileFormat.C64Multi. C64 multiformat art program files. Supports Art Studio Hires (9009 bytes, 320x200, 1bpp) and Art Studio Multicolor (10018 bytes, 160x200, 2bpp). Format detection from file size. `C64MultiFormat` enum (ArtStudioHires/ArtStudioMulti/AmicaPaint).

### Optimization Pipeline (PNG)

1. Constructor validates input (`ArgumentNullException.ThrowIfNull`), extracts ARGB pixel data, and computes `ImageStats` (unique colors, unique ARGB colors, alpha presence, grayscale detection, transparent key color for binary alpha). Stores source `Bitmap` reference for dithering.
2. `_GenerateCombinations()` determines which color modes to try. For grayscale images with binary alpha and a single transparent gray value, also generates Grayscale+tRNS combos. For palette mode with >256 colors, generates `QuantizerDithererCombo` entries when `UseDithering` is enabled.
3. `OptimizeAsync()` pre-computes pixel conversions once per `(ColorMode, BitDepth, QuantizerDithererCombo?)` group. For dithered palette combos, `_QuantizeWithFrameworkExtensions` calls `ReduceColors<TQ, TD>` via a two-level type dispatch (quantizer then ditherer), extracts palette and pixel indices from the resulting indexed bitmap. Palette images with `OptimizePaletteOrder` enabled are reordered via `PngPaletteReorderer.DeflateOptimizedSort`.
4. For Adam7 interlaced combos, `_ExtractAdam7SubImages` extracts 7 sub-images from already-converted scanlines.
5. Two-phase optimization (when enabled and expensive methods present): Phase 1 screens all combos with Maximum compression, ranks by size, takes top N candidates. Phase 2 re-tests only those candidates with expensive Ultra/Hyper methods. Phase 1 results are also included as candidates to ensure fast compression is never worse than expensive.
6. Each combo is tested in parallel: filtered, compressed, assembled into a PNG byte stream via PngWriter.
7. Best result selected across all phases.

### Platform Notes

- Optimizer.Png uses `System.Drawing.Common` and `FrameworkExtensions.System.Drawing` which require Windows and the `-windows` TFM.
- Optimizer.Gif uses `System.Drawing.Common` and references GifFileFormat (net8.0-windows).
- Optimizer.Tiff uses `System.Drawing.Common` with `EnableWindowsTargeting` but targets plain `net8.0`.
- Optimizer.Bmp, Optimizer.Tga, Optimizer.Pcx, Optimizer.Jpeg, Optimizer.Ani use `System.Drawing.Common` with `EnableWindowsTargeting` but target plain `net8.0`.
- FileFormat.Jpeg uses `BitMiracle.LibJpeg.NET` for JPEG encoding/decoding. Note: Chroma422 subsampling is not supported by this library.
- Compression.Core has no platform dependencies (pure BCL).
- FileFormat.Ani uses FileFormat.Riff and FileFormat.Ico, targets net8.0 with no platform dependencies.
- All FileFormat libraries target net8.0 with no platform dependencies (except FileFormat.Jpeg and FileFormat.Tiff which use BitMiracle wrapper libraries).

**FileFormat.PabloPaint** (net8.0, library) — Atari ST Pablo Paint monochrome image reader/writer. No header, raw pixel data. Fixed 32000-byte format: 640x400 pixels, 1bpp monochrome, MSB first, 0=white 1=black. Contains `PabloPaintReader`, `PabloPaintWriter`, `PabloPaintFile` (data model implementing `IImageFileFormat<PabloPaintFile>`, Capabilities: MonochromeOnly). ToRawImage: Indexed1 with B&W palette. FromRawImage: requires Indexed1 640x400. Extensions: .pa3. References FileFormat.Core.

**FileFormat.PabloPaint.Tests** (NUnit 4, 34 tests) — reader validation (null, missing, too small, exact size, larger, stream, data copy), writer output (null, size, pixel data, empty), round-trip (preserve, all zeros, all ones, via file, via RawImage, via RawImage all zeros, via RawImage all pixels set), data type tests (width, height, defaults, file size, init, ToRawImage/FromRawImage null/format/dimensions/indexed1/palette/pixel data size/cloning).

**FileFormat.QuantumPaint** (net8.0, library) — Atari ST QuantumPaint image reader/writer. 32-byte palette (16 words BE, 12-bit RGB) + 32000 bytes Atari ST word-interleaved planar data (4 planes). Fixed 320x200, 16 colors. Contains `QuantumPaintReader`, `QuantumPaintWriter`, `QuantumPaintFile` (data model implementing `IImageFileFormat<QuantumPaintFile>`, Capabilities: IndexedOnly). ToRawImage: Indexed8 with ST palette. FromRawImage: requires Indexed8 320x200. Extensions: .pbx. References FileFormat.Core.

**FileFormat.QuantumPaint.Tests** (NUnit 4, 37 tests) — reader validation (null, missing, too small, valid, palette, pixel data, stream, data copy), writer output (null, size, palette BE, pixel data offset, empty), round-trip (all fields, all zeros, random, via file, via RawImage, via RawImage palette), data type tests (width, height, palette, defaults, min file size, init, ToRawImage/FromRawImage null/format/dimensions/indexed8/palette count/pixel data size).

**FileFormat.SinbadSlideshow** (net8.0, library) — Atari ST Sinbad Slideshow single-frame image reader/writer. 32-byte palette (16 words BE, 12-bit RGB) + 32000 bytes Atari ST word-interleaved planar data (4 planes). Fixed 320x200, 16 colors. Contains `SinbadSlideshowReader`, `SinbadSlideshowWriter`, `SinbadSlideshowFile` (data model implementing `IImageFileFormat<SinbadSlideshowFile>`, Capabilities: IndexedOnly). ToRawImage: Indexed8 with ST palette. FromRawImage: requires Indexed8 320x200. Extensions: .ssb. References FileFormat.Core.

**FileFormat.SinbadSlideshow.Tests** (NUnit 4, 37 tests) — reader validation (null, missing, too small, valid, palette, pixel data, stream, data copy), writer output (null, size, palette BE, pixel data offset, empty), round-trip (all fields, all zeros, random, via file, via RawImage, via RawImage palette), data type tests (width, height, palette, defaults, file size, init, ToRawImage/FromRawImage null/format/dimensions/indexed8/palette count/pixel data size).

**FileFormat.CokeAtari** (net8.0, library) — Atari Falcon COKE 16-bit true-color reader/writer. 4-byte header (width BE u16, height BE u16) + RGB565 big-endian pixel data. Variable resolution. Contains `CokeAtariReader` (validates header + pixel data size, FromFile/FromStream/FromBytes), `CokeAtariWriter` (assembles header + pixel data via ToBytes), `CokeAtariFile` (data model implementing `IImageFileFormat<CokeAtariFile>`, Width, Height, PixelData), `CokeAtariHeader` (readonly partial record struct with `[GenerateSerializer]`, 4-byte BE header). ToRawImage: Rgb24 via RGB565 expansion. FromRawImage: requires Rgb24. Extensions: .tg1. References FileFormat.Core.

**FileFormat.CokeAtari.Tests** (NUnit 4, 41 tests) — reader validation (null, missing, too small, truncated pixel data, valid parsing, dimensions, pixel data, stream), writer output (null, header BE dimensions, pixel data, total length), header tests (round-trip, known values, struct size, field map), round-trip (small image, all zeros, large image, via file, via RawImage pure red/green/RGB565 precision), data type tests (defaults, init properties, extensions, ToRawImage/FromRawImage null/format validation/dimensions/pixel data size/cloning).

**FileFormat.AtariFalconXga** (net8.0, library) — Atari Falcon XGA 16-bit true-color reader/writer. 4-byte header (width BE u16, height BE u16) + RGB565 big-endian pixel data. Variable resolution. Contains `AtariFalconXgaReader` (validates header + pixel data size, FromFile/FromStream/FromBytes), `AtariFalconXgaWriter` (assembles header + pixel data via ToBytes), `AtariFalconXgaFile` (data model implementing `IImageFileFormat<AtariFalconXgaFile>`, Width, Height, PixelData), `AtariFalconXgaHeader` (readonly partial record struct with `[GenerateSerializer]`, 4-byte BE header). ToRawImage: Rgb24 via RGB565 expansion. FromRawImage: requires Rgb24. Extensions: .xga. References FileFormat.Core.

**FileFormat.AtariFalconXga.Tests** (NUnit 4, 40 tests) — reader validation (null, missing, too small, truncated pixel data, valid parsing, dimensions, pixel data, stream), writer output (null, header BE dimensions, pixel data, total length), header tests (round-trip, known values, struct size, field map), round-trip (small image, all zeros, large image, via file, via RawImage pure red/green/RGB565 precision), data type tests (defaults, init properties, extensions, ToRawImage/FromRawImage null/format validation/dimensions/pixel data size/cloning).

**FileFormat.SpookySpritesFalcon** (net8.0, library) — Atari Falcon Spooky Sprites compressed 16-bit true-color reader/writer. 4-byte header (width BE u16, height BE u16) + RLE-compressed RGB565 big-endian pixel data. RLE scheme: signed byte count (positive=literal run of N pixels, negative=repeat single pixel |N| times, zero=end marker). Variable resolution. Contains `SpookySpritesFalconReader` (validates header, decompresses RLE, FromFile/FromStream/FromBytes), `SpookySpritesFalconWriter` (compresses pixel data via RLE, assembles header + compressed data via ToBytes), `SpookySpritesFalconFile` (data model implementing `IImageFileFormat<SpookySpritesFalconFile>`, Width, Height, PixelData), `SpookySpritesFalconHeader` (readonly partial record struct with `[GenerateSerializer]`, 4-byte BE header), `SpookySpritesFalconRleCompressor` (internal static class: Compress/Decompress). ToRawImage: Rgb24 via RGB565 expansion. FromRawImage: requires Rgb24. Extensions: .tre. References FileFormat.Core.

**FileFormat.SpookySpritesFalcon.Tests** (NUnit 4, 50 tests) — reader validation (null, missing, too small, valid parsing, dimensions, pixel data, stream), writer output (null, header BE dimensions, end marker, compressed smaller for repeat data, pixel data decode), RLE compressor tests (decompress empty/literal/repeat, round-trip all same/different/mixed/large/single pixel, compress smaller, compress end marker), header tests (round-trip, known values, struct size, field map), round-trip (small image, all zeros, large image, via file, via RawImage pure red/green/RGB565 precision, compressed all same pixels, all max values), data type tests (defaults, init properties, extensions, ToRawImage/FromRawImage null/format validation/dimensions/pixel data size/cloning).

## Test Infrastructure

**Compression.Tests** (NUnit 4, 51 tests) contains:
- `ZopfliDeflaterTests` — BitWriter bit packing, RFC 1951 symbol tables, lookup table verification for all lengths/distances, Huffman tree construction, hash chain matching (including secondary-byte quick reject), round-trip compression (Ultra/Hyper), compression ratio vs .NET SmallestSize, multi-length DP validation, convergence detection, 64KB+ round-trip tests, lazy matching validation (including distance-aware cost comparison), adaptive depth tests, statistical block split candidate detection, block reparse validation, RLE caching verification

**Optimizer.Png.Tests** (NUnit 4, 184 tests) contains:
- `FilterToolsTests` — filter correctness for all 5 filter types, multi-row, edge cases, SIMD Paeth verification
- `DataTypeTests` — enum values match PNG spec, record struct layouts, null bitmap validation
- `PngFilterSelectorTests` — SAD calculation, palette/grayscale heuristics, deflate-aware scoring (byte-pair repetition, high-diversity penalty), early break, stickiness
- `PngFilterOptimizerTests` — all filter strategies including BruteForce and BruteForceAdaptive, palette/grayscale shortcuts
- `PngPaletteReordererTests` — Hilbert sort, spatial locality sort, deflate-optimized sort, identity order
- `ImagePartitionerTests` — partition behavior, filter type ranges
- `Adam7Tests` — Adam7 pass dimension calculations, interlaced PNG generation and readback for RGB/Palette/Grayscale/1x1
- `EndToEndTests` — full optimize->readback with pixel equality, all color modes, interlacing with readback, tRNS transparency (RGB and Grayscale), RGBA-to-RGB+tRNS, Grayscale+tRNS for binary alpha, BruteForce E2E, chunk preservation, two-phase optimization, CancellationToken, IProgress reporting
- `MedianCutQuantizerTests` — median-cut quantization accuracy, palette size, alpha handling, lossy palette E2E
- `DithererExpansionTests` — FrameworkExtensions quantizer/ditherer integration (all 5 quantizers, 5 ditherers, high-quality mode, error handling, E2E readback)
- `RegressionTests` — `[Category("Regression")]` tests for each fixed bug
- `PerformanceTests` — `[Category("Performance")]` throughput and timeout tests

**Optimizer.Gif.Tests** (NUnit 4, 71 tests) contains:
- `ReaderTests` — parse GIF87a/89a, header, frames, LCT/GCT, interlace, transparency (manual GIF construction)
- `LzwRoundTripTests` — encode with LzwCompressor, decode with Reader, pixel-perfect match
- `PaletteReordererTests` — valid permutation, remap+inverse=identity, frequency sort correctness, all 7 strategies including CompressionOptimized
- `GifFrameOptimizerTests` — disposal optimization (including compression-aware), margin trimming, frame differencing, frame deduplication, palette-aware dedup regression tests (swapped palettes, GCT vs LCT)
- `LzwCompressorTests` — compression correctness, deferred clear codes, edge cases, hash table optimization, adaptive clear interval
- `EndToEndTests` — optimize->readback, all strategies, input validation (null/missing/corrupt file), CancellationToken

**Optimizer.Tiff.Tests** (NUnit 4, 36 tests) contains:
- `PackBitsCompressorTests` — round-trip, edge cases, large data, compression ratio estimation
- `TiffOptimizerTests` — combo generation, color mode detection, all compression methods, palette frequency sorting, dynamic strip sizing, tile support
- `EndToEndTests` — optimize->readback via LibTiff, pixel equality, tiled TIFF readback, input validation (null/missing file, null bitmap), CancellationToken

**FileFormat.Bmp.Tests** (NUnit 4, 28 tests) contains:
- `BmpReaderTests` — null, missing, too small, invalid signature, valid RGB24 parsing
- `BmpWriterTests` — BM signature, palette inclusion, RGB565 bitfields, row padding, top-down negative height, file size field
- `RoundTripTests` — Rgb24, Palette8, Palette4, Palette1, Grayscale8, Rle8, Rgb16_565
- `RleCompressorTests` — large run split, round-trip large data, RLE4 output, single byte, mixed ratio
- `DataTypeTests` — BmpColorMode(7), BmpCompression(3), BmpRowOrder(2) enum values

**FileFormat.Tga.Tests** (NUnit 4, 24 tests) contains:
- `TgaReaderTests` — null, missing, too small, valid RGB24 parsing
- `TgaWriterTests` — 18-byte header, alpha descriptor, colormap, origin bit, TGA 2.0 footer
- `RoundTripTests` — Rgb24, Rgba32, Grayscale8, Indexed8, RLE+Rgb24, BottomLeft origin
- `TgaRleCompressorTests` — empty, allSame 3bpp/4bpp, mixed data round-trip, compression ratio
- `DataTypeTests` — TgaColorMode(5), TgaCompression(2), TgaOrigin(2) enum values

**FileFormat.Pcx.Tests** (NUnit 4, 22 tests) contains:
- `PcxReaderTests` — null, missing, too small, invalid manufacturer
- `PcxWriterTests` — header signature, VGA palette, even bytesPerLine, dimensions, EGA palette
- `RoundTripTests` — Rgb24 SeparatePlanes, Indexed8, Indexed4, Monochrome, large image
- `PcxRleCompressorTests` — empty, allSame, mixed round-trip, high-value bytes, compression ratio
- `DataTypeTests` — PcxColorMode(5), PcxPlaneConfig(2) enum values

**FileFormat.Jpeg.Tests** (NUnit 4, 17 tests) contains:
- `JpegReaderTests` — null, missing, too small, invalid signature, valid JPEG parsing, pixel data
- `JpegWriterTests` — lossy RGB/grayscale/progressive, lossless baseline-to-progressive
- `RoundTripTests` — lossy dimensions, grayscale detection, lossless dimensions, raw bytes
- `DataTypeTests` — JpegMode(2), JpegSubsampling(3) enum values

**FileFormat.Tiff.Tests** (NUnit 4, 44 tests) contains:
- `TiffReaderTests` — null, missing, too small, invalid signature
- `TiffWriterTests` — TIFF header, palette colormap, file size, LZW validity
- `RoundTripTests` — Rgb, Grayscale, Palette, PackBits, Lzw, Tiled
- `PackBitsCompressorTests` — empty, allSame, mixed round-trip, allDifferent literal, compression ratio
- `DataTypeTests` — TiffColorMode(5), TiffCompression(6), TiffPredictor(2) enum values
- `MultiPageTests` — ImageCount single/multi-page, Pages property, indexed ToRawImage, multi-page round-trip (grayscale + RGB), single-page backward compatibility

**FileFormat.Png.Tests** (NUnit 4, 32 tests) contains:
- `PngReaderTests` — null, missing, too small, invalid signature, valid RGB parsing
- `PngWriterTests` — PNG signature, PLTE chunk, tRNS chunk, IEND chunk, CRC32 validity
- `RoundTripTests` — Rgb8, Rgba8, Grayscale8, GrayscaleAlpha8, Palette, tRNS preservation
- `PngChunkReaderTests` — empty chunks, gAMA before PLTE, tEXt after IDAT, pHYs between, HasChunks
- `Adam7Tests` — PassCount=7, pass dimensions 8x8 and 1x1, XStart/YStart values
- `DataTypeTests` — PngColorType spec values, PngFilterType(5), PngInterlaceMethod(2), PngChunk

**FileFormat.Riff.Tests** (NUnit 4, 22 tests) contains:
- `FourCCTests` — string constructor, wrong length, toString, implicit conversion, ReadFrom/WriteTo
- `RiffReaderTests` — null, missing, too small, invalid signature, valid RIFF parsing
- `RiffWriterTests` — null, RIFF signature, form type, size field, chunk data
- `RoundTripTests` — empty file, single chunk, odd-sized chunk alignment, nested LIST, multiple chunks/lists

**FileFormat.Ico.Tests** (NUnit 4, 15 tests) contains:
- `IcoReaderTests` — null, missing, too small, invalid type, valid ICO parsing
- `IcoWriterTests` — header fields, multiple entries, PNG data preserved, directory dimensions
- `RoundTripTests` — single PNG entry, multiple entries, BMP entry, 256x256
- `DataTypeTests` — IcoImageFormat(2) enum values

**FileFormat.Cur.Tests** (NUnit 4, 11 tests) contains:
- `CurReaderTests` — null, missing, too small, invalid type
- `CurWriterTests` — cursor type in header, hotspot in directory, entry count
- `RoundTripTests` — hotspot preservation, multiple entries, 256x256, PNG data

**FileFormat.Ani.Tests** (NUnit 4, 15 tests) contains:
- `AniReaderTests` — null, missing file, too small, invalid form type validation
- `AniWriterTests` — RIFF signature, ACON form type, anih chunk presence
- `RoundTripTests` — single/multiple frames, rates, sequence, header fields roundtrip
- `DataTypeTests` — AniHeader record struct field storage, default values

**Optimizer.Ani.Tests** (NUnit 4, 7 tests) contains:
- `AniOptimizerTests` — null file, missing file, null constructor input validation
- `EndToEndTests` — single/multiple frame optimization, result validity via AniReader readback, cancellation

**FileFormat.AmstradCpc.Tests** (NUnit 4, 29 tests) contains:
- `AmstradCpcReaderTests` — null, missing, too small, wrong size, valid parsing for all 3 modes, default mode, stream null
- `AmstradCpcWriterTests` — output size 16384, null, Mode0 output size, interleave verification
- `AmstradCpcPixelPackerTests` — Mode2 unpack all ones/zeros/alternating, Mode2 pack round-trip, Mode0 unpack zero, Mode0 exhaustive pack round-trip, Mode1 unpack zero, Mode1 exhaustive pack round-trip, Mode0 known byte
- `RoundTripTests` — Mode1/Mode0/Mode2 round-trip data preservation, all zeros, writer output size
- `DataTypeTests` — AmstradCpcMode enum values (Mode0=0, Mode1=1, Mode2=2) and count (3)

**FileFormat.C64Multi.Tests** (NUnit 4, 26 tests) contains:
- `C64MultiReaderTests` — null, missing, too small, valid hires parsing (320x200, format, load address, data sections, null ColorData), valid multicolor parsing (160x200, format, load address, data sections, ColorData present), little-endian load address, bitmap data copy verification, stream parsing
- `C64MultiWriterTests` — null, hires output 9009 bytes, multicolor output 10018 bytes, hires/multi load address LE, bitmap/screen/color data offsets, border/background color positions
- `RoundTripTests` — ArtStudioHires all fields preserved, ArtStudioMulti all fields preserved, custom load address, background color preserved, all bytes max value
- `DataTypeTests` — C64MultiFormat enum values (ArtStudioHires=0, ArtStudioMulti=1, AmicaPaint=2) and count (3)

**FileFormat.Wbmp.Tests** (NUnit 4, 33 tests) contains:
- `WbmpReaderTests` — null, missing, too small, invalid type, valid parsing (dimensions + pixel data)
- `WbmpWriterTests` — type byte 0, fixed header 0, single/multi-byte dimension encoding, pixel data presence, total length
- `WbmpMultiByteIntTests` — encode zero, small values (0-127 single byte), large values (128+ multi-byte), decode single/two bytes, round-trip small/large, trailing data, negative value
- `RoundTripTests` — all white/black, checkerboard, non-byte-boundary width, multi-byte width/height/both, single pixel

**FileFormat.Qoi.Tests** (NUnit 4, 43 tests) — reader validation, writer output, QOI codec tests, round-trip, header tests, data type tests.

**FileFormat.Farbfeld.Tests** (NUnit 4, 26 tests) — reader validation, writer output, round-trip, header tests.

**FileFormat.Netpbm.Tests** (NUnit 4, 45 tests) — reader validation per format (P1-P7), writer output, header parser, round-trip.

**FileFormat.Xbm.Tests** (NUnit 4, 37 tests) — reader validation, writer output, text parser, round-trip.

**FileFormat.Xpm.Tests** (NUnit 4, 30 tests) — reader validation, writer output, text parser, round-trip, data types.

**FileFormat.MacPaint.Tests** (NUnit 4, 29 tests) — reader validation, writer output, PackBits, round-trip, header tests.

**FileFormat.ZxSpectrum.Tests** (NUnit 4, 24 tests) — reader validation, writer output, interleave verification, round-trip.

**FileFormat.Koala.Tests** (NUnit 4, 24 tests) — reader validation, writer output, round-trip.

**FileFormat.Degas.Tests** (NUnit 4, 22 tests) — reader validation, writer output, planar conversion, round-trip, header tests.

**FileFormat.DaliST.Tests** (NUnit 4, 29 tests) — reader validation (null, missing, too small, all resolutions, stream, palette/pixel preservation), writer output (null, file size, palette values, pixel data offset, zero palette), round-trip (low/medium/high res, via file, all zeros), data type tests (enum values, constants, defaults).

**FileFormat.MultiPalettePicture.Tests** (NUnit 4, 31 tests) — reader validation (null, missing, too small, valid parsing, per-scanline palette, palette/pixel preservation, stream), writer output (null, file size, palette values, pixel data offsets, second scanline palette), round-trip (pixel data, palettes, all zeros, via file, dimensions), data type tests (constants, defaults).

**FileFormat.DrawIt.Tests** (NUnit 4, 36 tests) — reader validation (null, missing, too small, zero width/height, truncated, valid parsing, palette/pixel preservation, stream), writer output (null, file size, dimensions, palette offset, pixel data offset, small image), header tests (round-trip, known values, struct size, field map), round-trip (all fields, small image, via file, all zeros, large image), data type tests (constants, defaults, init properties).

**FileFormat.Neochrome.Tests** (NUnit 4, 25 tests) — reader validation, writer output, round-trip, header tests.

**FileFormat.GemImg.Tests** (NUnit 4, 21 tests) — reader validation, writer output, compression, round-trip, header tests.

**FileFormat.Pfm.Tests** (NUnit 4, 27 tests) — reader validation, writer output, header parser, round-trip, data types.

**FileFormat.Sgi.Tests** (NUnit 4, 29 tests) — reader validation, writer output, RLE compressor, round-trip, header tests.

**FileFormat.SunRaster.Tests** (NUnit 4, 37 tests) — reader validation, writer output, RLE compressor, round-trip, header tests.

**FileFormat.Hdr.Tests** (NUnit 4, 40 tests) — reader validation, writer output, RGBE codec, header parser, round-trip.

**FileFormat.UtahRle.Tests** (NUnit 4, 29 tests) — reader validation, writer output, decoder/encoder, round-trip, header tests.

**FileFormat.DrHalo.Tests** (NUnit 4, 30 tests) — reader validation, writer output, RLE, round-trip, header tests.

**FileFormat.Iff.Tests** (NUnit 4, 22 tests) — reader validation, writer output, round-trip, chunk header tests.

**FileFormat.Ilbm.Tests** (NUnit 4, 53 tests) — reader validation, writer output, ByteRun1, planar converter, round-trip, header tests, HAM decoder tests, CAMG roundtrip tests.

**FileFormat.Fli.Tests** (NUnit 4, 37 tests) — reader validation, writer output, delta decoder, round-trip, header tests.

**FileFormat.Cineon.Tests** (NUnit 4, 23 tests) — reader validation, writer output, round-trip, header tests.

**FileFormat.Dds.Tests** (NUnit 4, 50 tests) — reader validation, writer output, block info, round-trip, header/pixel format/DX10 header tests.

**FileFormat.Vtf.Tests** (NUnit 4, 23 tests) — reader validation, writer output, round-trip, data types, header tests.

**FileFormat.Ktx.Tests** (NUnit 4, 33 tests) — reader validation, writer output, round-trip, KTX1 + KTX2 header tests.

**FileFormat.Exr.Tests** (NUnit 4, 26 tests) — reader validation, writer output, round-trip, magic header tests.

**FileFormat.Dpx.Tests** (NUnit 4, 32 tests) — reader validation, writer output, round-trip, BE/LE endianness, header tests.

**FileFormat.Fits.Tests** (NUnit 4, 28 tests) — reader validation, writer output, header parser, round-trip, data types.

**FileFormat.Ccitt.Tests** (NUnit 4, 46 tests) — reader validation, writer output, G3/G4 codecs, Huffman tables, round-trip.

**FileFormat.BbcMicro.Tests** (NUnit 4, 34 tests) — reader validation, writer output, layout converter, round-trip, data types.

**FileFormat.Psd.Tests** (NUnit 4, 27 tests) — reader validation, writer output, round-trip, header tests, data types.

**FileFormat.Hrz.Tests** (NUnit 4, 13 tests) — reader validation, writer output, round-trip.

**FileFormat.Cmu.Tests** (NUnit 4, 19 tests) — reader validation, writer output, round-trip, header tests.

**FileFormat.Mtv.Tests** (NUnit 4, 15 tests) — reader validation, writer output, round-trip.

**FileFormat.Qrt.Tests** (NUnit 4, 17 tests) — reader validation, writer output, round-trip, header tests.

**FileFormat.Msp.Tests** (NUnit 4, 26 tests) — reader validation, writer output, RLE compressor, round-trip (v1+v2), header tests.

**FileFormat.Dcx.Tests** (NUnit 4, 15 tests) — reader validation, writer output, round-trip (single/multi page).

**FileFormat.Astc.Tests** (NUnit 4, 21 tests) — reader validation, writer output, round-trip (4x4/8x8/12x12), header tests.

**FileFormat.Pkm.Tests** (NUnit 4, 19 tests) — reader validation, writer output, round-trip, data types, header tests.

**FileFormat.Tim.Tests** (NUnit 4, 26 tests) — reader validation, writer output, round-trip (4/8/16/24bpp), data types, header tests.

**FileFormat.Tim2.Tests** (NUnit 4, 22 tests) — reader validation, writer output, round-trip (single/multi picture), data types, header tests.

**FileFormat.Wal.Tests** (NUnit 4, 21 tests) — reader validation, writer output, round-trip (with mipmaps), header tests.

**FileFormat.Pvr.Tests** (NUnit 4, 24 tests) — reader validation, writer output, round-trip, data types, header tests.

**FileFormat.Wpg.Tests** (NUnit 4, 24 tests) — reader validation, writer output, RLE compressor, round-trip, header tests.

**FileFormat.Bsave.Tests** (NUnit 4, 25 tests) — reader validation, writer output, round-trip (CGA/VGA), data types, header tests.

**FileFormat.Clp.Tests** (NUnit 4, 18 tests) — reader validation, writer output, round-trip, header tests.

**FileFormat.Spectrum512.Tests** (NUnit 4, 13 tests) — reader validation, writer output, round-trip, data types.

**FileFormat.Tiny.Tests** (NUnit 4, 20 tests) — reader validation, writer output, compressor tests, round-trip (low/med/high).

**FileFormat.SyntheticArts.Tests** (NUnit 4, 33 tests) — reader validation (null, missing, too small, valid parsing, palette, pixel data, stream), writer output (null, size, palette, pixel data, unused entries), round-trip (all fields, zeros, random, via file, via RawImage, FromRawImage), data type tests (constants, defaults, extensions).

**FileFormat.HighresMedium.Tests** (NUnit 4, 34 tests) — reader validation (null, missing, too small, valid parsing, both palettes, both frame pixel data, stream), writer output (null, size, frame 1/2 palettes, frame 1/2 pixel data), round-trip (all fields, zeros, random, via file, ToRawImage Rgb24, blend verification, FromRawImage validation), data type tests (constants, defaults).

**FileFormat.FullscreenKit.Tests** (NUnit 4, 41 tests) — reader validation (null, missing, too small, invalid size, primary 416x274, alternate 448x272, palette, pixel data, stream), writer output (null, primary/alternate sizes, palette, pixel data), round-trip (primary, alternate, zeros, random, via file, via RawImage, FromRawImage validation, alternate dimensions), data type tests (constants, calculations).

**FileFormat.Sixel.Tests** (NUnit 4, 22 tests) — reader validation, writer output, codec tests (RLE/bands/colors), round-trip, data types.

**FileFormat.Wad.Tests** (NUnit 4, 29 tests) — reader validation, writer output, round-trip, data types.

**FileFormat.Wad3.Tests** (NUnit 4, 26 tests) — reader validation, writer output, round-trip, data types.

**FileFormat.Apng.Tests** (NUnit 4, 31 tests) — reader validation, writer output, round-trip (single/multi-frame, dispose/blend ops), data types.

**FileFormat.Mng.Tests** (NUnit 4, 23 tests) — reader validation, writer output, round-trip (single/multi frame), header tests, data types.

**FileFormat.Xcf.Tests** (NUnit 4, 27 tests) — reader validation, writer output, tile decoder tests, round-trip (RGB/Gray/Indexed).

**FileFormat.Pict.Tests** (NUnit 4, 16 tests) — reader validation, writer output, round-trip (indexed PackBits, direct RGB).

**FileFormat.Dicom.Tests** (NUnit 4, 23 tests) — reader validation, writer output, tag reader tests, round-trip (8-bit mono, 16-bit mono, RGB).

**FileFormat.Trs80.Tests** (NUnit 4, 41 tests) — reader validation (null, missing, too small, too large, exact size, stream, data copy), writer output (size 6144, null, data preserved, short data padding), round-trip (specific cells, all zeros, all ones, via file, via RawImage, via RawImage all zeros, via RawImage all pixels set, bit mapping top-left/top-right/mid-left/bot-right), data type tests (fixed width 256, fixed height 144, defaults, FileSize/Columns/Rows constants, init properties, extensions, ToRawImage/FromRawImage null/format/dimensions/palette/pixel data size/cloning).

**FileFormat.SoftImage.Tests** (NUnit 4, 53 tests) — reader validation (null, missing, too small, invalid magic, valid RGB/RGBA parsing, dimensions, pixel data, comment, version, stream), writer output (null, magic bytes, header size, dimensions BE, comment field, version field, channel info packets RGB/RGBA, pixel data), round-trip (RGB24, RGBA32, via file, all zeros, gradient, via RawImage RGB24/RGBA32, version preserved, comment preserved, larger image), data type tests (defaults for PixelData/Width/Height/Comment/HasAlpha/Version, init properties, extensions, magic/header/comment constants, ToRawImage/FromRawImage null/format validation/RGB/RGBA/cloning).

**FileFormat.Envi** (net8.0, library) — ENVI remote sensing image format reader/writer. Text header starting with "ENVI\n" magic, keyword = value lines (multiline brace {} support). Key fields: samples (width), lines (height), bands, data type (1=uint8, 2=int16, 4=float32, 12=uint16), interleave (bsq/bip/bil), byte order (0=LE, 1=BE), header offset. Contains `EnviReader`, `EnviWriter`, `EnviFile` (data model implementing `IImageFileFormat<EnviFile>`), `EnviHeaderParser` (internal), `EnviInterleave` (Bsq/Bip/Bil enum). ToRawImage: Gray8 (bands=1, data_type=1), Rgb24 (bands>=3, data_type=1). FromRawImage: Gray8 or Rgb24. Extensions: .hdr. References FileFormat.Core.

**FileFormat.Envi.Tests** (NUnit 4, 74 tests) — reader validation (null, missing, too small, invalid magic, valid grayscale/RGB, stream, pixel data preserved), writer output (null, magic, header fields, pixel data), header parser (dimensions, bands, interleave, data type, multiline values, brace values), round-trip (grayscale, RGB BIP/BSQ/BIL, via file, via RawImage), data type tests (EnviInterleave enum, defaults, extensions, cloning).

**FileFormat.MayaIff** (net8.0, library) — Maya IFF (FOR4/CIMG) image format reader/writer. FOR4 container (4-byte aligned) with CIMG form type, TBHD tile-based header (32 bytes BE: width, height, prnum, prden, flags, bytes, tiles, compression), RGBA/RGB data chunks. Contains `MayaIffReader`, `MayaIffWriter`, `MayaIffFile` (data model implementing `IImageFileFormat<MayaIffFile>`). ToRawImage: Rgba32 or Rgb24. Extensions: .iff, .maya. References FileFormat.Core.

**FileFormat.MayaIff.Tests** (NUnit 4, 49 tests) — reader validation, writer output, round-trip (RGB/RGBA), data type tests.

**FileFormat.Xcursor** (net8.0, library) — X11 cursor theme format reader/writer. "Xcur" 4-byte magic, TOC entries pointing to image chunks with ARGB premultiplied pixels. Contains `XcursorReader`, `XcursorWriter`, `XcursorFile` (data model implementing `IImageFileFormat<XcursorFile>`). ToRawImage: Rgba32 (unpremultiplied). Extensions: .xcur, .cursor. References FileFormat.Core.

**FileFormat.Xcursor.Tests** (NUnit 4, 52 tests) — reader validation, writer output, round-trip, data type tests.

**FileFormat.IffPbm** (net8.0, library) — IFF Packed Bitmap (chunky non-planar) reader/writer. FORM+PBM container, BMHD 20-byte header, CMAP palette, ByteRun1 compressed chunky 8-bit indexed pixels. Contains `IffPbmReader`, `IffPbmWriter`, `IffPbmFile` (data model implementing `IImageFileFormat<IffPbmFile>`). ToRawImage: Indexed8 with CMAP palette. Extensions: .lbm, .pbm. References FileFormat.Iff and FileFormat.Core.

**FileFormat.IffPbm.Tests** (NUnit 4, 54 tests) — reader validation, writer output, round-trip, data type tests.

**FileFormat.PcPaint** (net8.0, library) — PC Paint/Pictor page format reader/writer. 0x1234 LE magic, 16-byte header with dimensions/planes/bpp, VGA palette, per-scanline RLE encoding. Contains `PcPaintReader`, `PcPaintWriter`, `PcPaintFile` (data model implementing `IImageFileFormat<PcPaintFile>`). ToRawImage: Indexed8 with VGA palette. Extensions: .pic, .clp. References FileFormat.Core.

**FileFormat.PcPaint.Tests** (NUnit 4, 62 tests) — reader validation, writer output, round-trip, data type tests.

**FileFormat.IffAcbm** (net8.0, library) — IFF Amiga Contiguous Bitmap reader/writer. FORM+ACBM container, BMHD header, CMAP palette, ABIT chunk with contiguous (non-interleaved) bitplane data. Contains `IffAcbmReader`, `IffAcbmWriter`, `IffAcbmFile` (data model implementing `IImageFileFormat<IffAcbmFile>`). ToRawImage: Indexed8. Extensions: .acbm, .iff. References FileFormat.Iff and FileFormat.Core.

**FileFormat.IffAcbm.Tests** (NUnit 4, 57 tests) — reader validation, writer output, round-trip, data type tests.

**FileFormat.IffDeep** (net8.0, library) — IFF Deep Paint format reader/writer. FORM+DEEP container, DGBL chunk (width/height/compression/numElements), DPEL chunk (element descriptors), DBOD/BODY pixel data with optional ByteRun1 compression. Contains `IffDeepReader`, `IffDeepWriter`, `IffDeepFile` (data model implementing `IImageFileFormat<IffDeepFile>`). ToRawImage: Rgb24 or Rgba32. Extensions: .deep, .iff. References FileFormat.Iff and FileFormat.Core.

**FileFormat.IffDeep.Tests** (NUnit 4, 71 tests) — reader validation, writer output, round-trip, data type tests.

**FileFormat.IffRgb8** (net8.0, library) — IFF 24-bit RGB format reader/writer. FORM+RGB8 container, BMHD header (numPlanes=25), BODY with 4-byte pixel group ByteRun1 compression (R,G,B,pad). Contains `IffRgb8Reader`, `IffRgb8Writer`, `IffRgb8File` (data model implementing `IImageFileFormat<IffRgb8File>`). ToRawImage: Rgb24. Extensions: .rgb8, .iff. References FileFormat.Iff and FileFormat.Core.

**FileFormat.IffRgb8.Tests** (NUnit 4, 49 tests) — reader validation, writer output, round-trip, data type tests.

**FileFormat.IffRgbn** (net8.0, library) — IFF 13-bit RGB + genlock format reader/writer. FORM+RGBN container, BMHD header (numPlanes=13), BODY with 2-byte pixel units (4-bit R, 4-bit G, 4-bit B, 1-bit genlock, 3-bit repeat count). Contains `IffRgbnReader`, `IffRgbnWriter`, `IffRgbnFile` (data model implementing `IImageFileFormat<IffRgbnFile>`). ToRawImage: Rgb24. FromRawImage: quantizes to 4-bit channels, no RLE. Extensions: .rgbn, .iff. References FileFormat.Core.

**FileFormat.IffRgbn.Tests** (NUnit 4, 43 tests) — reader validation, writer output, round-trip (quantization, RLE repeat expansion), data type tests.

**FileFormat.Interfile** (net8.0, library) — Interfile nuclear medicine imaging format reader/writer. Text header starting with "!INTERFILE :=" line, !keyword := value pairs. Key fields: matrix size[1/2] (dimensions), number format, bytes per pixel, data file name. Contains `InterfileReader`, `InterfileWriter`, `InterfileFile` (data model implementing `IImageFileFormat<InterfileFile>`). ToRawImage: Gray8 or Rgb24. Extensions: .hv. References FileFormat.Core.

**FileFormat.Interfile.Tests** (NUnit 4, 54 tests) — reader validation, writer output, round-trip, data type tests.

**FileFormat.AtariFalcon** (net8.0, library) — Atari Falcon true-color screen dump reader/writer. No header, raw pixel data. Fixed 153600-byte format: 320x240 pixels, 16-bit RGB565 big-endian. Contains `AtariFalconReader`, `AtariFalconWriter`, `AtariFalconFile` (data model implementing `IImageFileFormat<AtariFalconFile>`). ToRawImage: Rgb24. Extensions: .ftc. References FileFormat.Core.

**FileFormat.AtariFalcon.Tests** (NUnit 4, 37 tests) — reader validation, writer output, round-trip, data type tests.

**FileFormat.BigTiff** (net8.0, library) — BigTIFF (.btf/.tf8) image format reader/writer. Custom binary parser for 64-bit TIFF variant (version 43, 8-byte offsets). 16-byte header, 20-byte IFD entries with uint64 count/value, IFD chain following for multi-page support. Contains `BigTiffReader` (validates byte order, version, offset size, follows IFD chain, handles inline and external BitsPerSample values), `BigTiffWriter` (assembles single/multi-page BigTIFF with inline BitsPerSample packing and IFD patching), `BigTiffFile` (data model implementing `IImageFileFormat<BigTiffFile>` and `IMultiImageFileFormat<BigTiffFile>`), `BigTiffPage` (per-page data model), `BigTiffFileHeader` (binary header struct). Supports Gray8, Rgb24, Gray16, Rgb48 (8-bit and 16-bit), uncompressed only. Multi-page: `Pages` property, `ImageCount()`, indexed `ToRawImage(file, index)`. Extensions: .btf, .tf8. References FileFormat.Core.

**FileFormat.BigTiff.Tests** (NUnit 4, 81 tests) — reader validation (null, missing, too small, invalid byte order/version/offset size/reserved/IFD offset), writer output (header, IFD entries, pixel data), round-trip (grayscale, RGB, via file, via stream, big-endian, RawImage), multi-page tests (ImageCount, Pages property, indexed ToRawImage, multi-page round-trip grayscale+RGB, single-page backward compat), data type tests (constants, defaults, extensions, header struct).

**FileFormat.CameraRaw.Tests** (NUnit 4, 128 tests) — lossless JPEG decoder tests (DHT parsing, all 7 prediction modes, 12/14/16-bit precision, multi-component, restart markers, Canon CR2 slicing, input validation), Nikon decompressor tests (null/bounds validation, output length, 12/14-bit range, curve application), Sony decompressor tests (null/bounds validation, output length, 12/14-bit range), Bayer demosaic uint16 tests (all 4 patterns, 14-bit input), RAW preprocessor uint16 tests (black level, white balance).

**FileFormat.Core.Tests** (NUnit 4, 278 tests) — multi-image format tests (IMultiImageFileFormat implementations across IcoFile, CurFile, AniFile, ApngFile, MngFile, FliFile, DcxFile, MpoFile, IcnsFile: ImageCount, indexed ToRawImage, null validation, out-of-range, cross-format pixel isolation, FormatCapability.MultiImage flag), block decoder tests (Bc6HDecoder: unsigned/signed modes, full alpha, unknown mode magenta, empty data; Bc7Decoder: mode-6 uniform color, black endpoints, reserved mode, image sizes, insufficient data; PvrtcDecoder: 4bpp/2bpp output sizes, all-zero data, opaque blocks, modulation), pixel format conversion (37 methods including 16-bit Gray16/Rgb48/Rgba64 paths with round-trip verification), header field mapping, format detection.

**Crush.TestUtilities** (net8.0, NUnit helpers) — shared test infrastructure: `TestBitmapFactory` (creates reproducible gradient test bitmaps), `TempFileScope` (IDisposable temp file lifecycle). Used by Optimizer.Bmp.Tests, Optimizer.Tga.Tests, Optimizer.Pcx.Tests, Optimizer.Jpeg.Tests, Optimizer.Tiff.Tests.

Test fixtures: `Optimizer.Png.Tests/Fixtures/StressTest.png` (copied from `Crush.Png/Examples/`).
