namespace FileFormat.Core;

/// <summary>
/// A tool from outside this repository used as an independent conformance oracle.
/// </summary>
/// <remarks>
/// The direction of the check belongs to the evidence using this enum. <see cref="VerifiedByAttribute"/>
/// means the external tool read what our writer produced; reader-conformance surveys may instead ask
/// an external writer to produce bytes and compare our decode with that toolchain's own decode. In
/// either direction the important property is independence: this repository's own reader and writer
/// are never an oracle for each other.
/// </remarks>
public enum ConformanceOracle {

  /// <summary>No external tool has supplied evidence for this implementation.</summary>
  None = 0,

  /// <summary>RECOIL's <c>recoil2png</c>, the reference decoder for the retro-computer formats.</summary>
  Recoil2Png,

  /// <summary>ImageMagick's <c>identify</c> / <c>convert</c>.</summary>
  ImageMagick,

  /// <summary>XnView's <c>nconvert</c>.</summary>
  XnView,

  /// <summary>IrfanView, driven headless.</summary>
  IrfanView,

  /// <summary>Tom's Editor, the online converter, asked only where nothing installed has an opinion.</summary>
  TomsEditor,

  /// <summary>libwebp's <c>dwebp</c>.</summary>
  DWebp,

  /// <summary>libjxl's <c>djxl</c>.</summary>
  Djxl,

  /// <summary>OpenJPEG's <c>opj_decompress</c>.</summary>
  OpjDecompress,

  /// <summary>libde265's <c>dec265</c>.</summary>
  Dec265,

  /// <summary>libheif's <c>heif-dec</c>.</summary>
  HeifDec,

  /// <summary>libavif's <c>avifdec</c>.</summary>
  AvifDec,

  /// <summary>FFmpeg, as <c>ffmpeg</c> or <c>ffprobe</c>.</summary>
  FFmpeg,

  /// <summary>ExifTool.</summary>
  ExifTool,

  /// <summary>pyembroidery.</summary>
  PyEmbroidery,

  /// <summary>LibreOffice, driven headless as <c>soffice</c>.</summary>
  LibreOffice,

  /// <summary>The <c>olefile</c> Python package.</summary>
  Olefile,

  /// <summary>Ghostscript, the PostScript and PDF interpreter, as <c>gs</c>.</summary>
  Ghostscript,

  /// <summary>Deark, the command-line historical image/file-format decoder.</summary>
  Deark,

  /// <summary>Arm's reference-quality ASTC encoder/decoder, <c>astcenc</c>.</summary>
  AstcEnc,

  /// <summary>Khronos KTX-Software's <c>ktx</c> command-line tools.</summary>
  KtxTools,

  /// <summary>DjVuLibre's encoder/decoder command-line tools such as <c>c44</c> and <c>ddjvu</c>.</summary>
  DjVuLibre,

  /// <summary>Fabrice Bellard's libbpg tools, <c>bpgenc</c> and <c>bpgdec</c>.</summary>
  LibBpg,

  /// <summary>The reference FLIF command-line encoder/decoder.</summary>
  Flif,

  /// <summary>Artifex's <c>jbig2dec</c> decoder.</summary>
  Jbig2Dec,

  /// <summary>agl's <c>jbig2</c> encoder from jbig2enc.</summary>
  Jbig2Enc,

  /// <summary>GDAL's raster drivers, normally exercised through <c>gdal_translate</c>.</summary>
  Gdal,

  /// <summary>Convert3D / ITK, normally exercised through <c>c3d</c>.</summary>
  Convert3D,

  /// <summary>The Netpbm converter suite.</summary>
  Netpbm,

  /// <summary>Ansilove's ANSI/ASCII art renderer.</summary>
  AnsiLove,

  /// <summary>LibRaw, normally exercised through <c>dcraw_emu</c>.</summary>
  LibRaw,

  /// <summary>Krita's command-line importer/exporter.</summary>
  Krita,

  /// <summary>The <c>hp2xx</c> HP-GL converter.</summary>
  Hp2xx,

  /// <summary>GhostPDL's PCL interpreter, normally exposed as <c>gpcl6</c>.</summary>
  GhostPcl,

  /// <summary>OFFIS DCMTK's DICOM image tools such as <c>img2dcm</c> and <c>dcm2pnm</c>.</summary>
  Dcmtk,
}

/// <summary>Display names and home pages for <see cref="ConformanceOracle"/>.</summary>
/// <remarks>
/// Kept beside the enum rather than in the documentation that renders it, so that a new oracle
/// arrives with its own name and link and no table has to be edited to learn about it.
/// </remarks>
public static class ConformanceOracles {

  /// <summary>The tool's name as a reader of a support table would recognise it.</summary>
  public static string DisplayName(this ConformanceOracle oracle) => oracle switch {
    ConformanceOracle.None => "none",
    ConformanceOracle.Recoil2Png => "recoil2png",
    ConformanceOracle.ImageMagick => "ImageMagick",
    ConformanceOracle.XnView => "XnView",
    ConformanceOracle.IrfanView => "IrfanView",
    ConformanceOracle.TomsEditor => "Tom's Editor",
    ConformanceOracle.DWebp => "dwebp",
    ConformanceOracle.Djxl => "djxl",
    ConformanceOracle.OpjDecompress => "opj_decompress",
    ConformanceOracle.Dec265 => "dec265",
    ConformanceOracle.HeifDec => "heif-dec",
    ConformanceOracle.AvifDec => "avifdec",
    ConformanceOracle.FFmpeg => "ffmpeg",
    ConformanceOracle.ExifTool => "ExifTool",
    ConformanceOracle.PyEmbroidery => "pyembroidery",
    ConformanceOracle.LibreOffice => "LibreOffice",
    ConformanceOracle.Olefile => "olefile",
    ConformanceOracle.Ghostscript => "Ghostscript",
    ConformanceOracle.Deark => "Deark",
    ConformanceOracle.AstcEnc => "astcenc",
    ConformanceOracle.KtxTools => "KTX-Software",
    ConformanceOracle.DjVuLibre => "DjVuLibre",
    ConformanceOracle.LibBpg => "libbpg",
    ConformanceOracle.Flif => "FLIF",
    ConformanceOracle.Jbig2Dec => "jbig2dec",
    ConformanceOracle.Jbig2Enc => "jbig2enc",
    ConformanceOracle.Gdal => "GDAL",
    ConformanceOracle.Convert3D => "Convert3D",
    ConformanceOracle.Netpbm => "Netpbm",
    ConformanceOracle.AnsiLove => "Ansilove",
    ConformanceOracle.LibRaw => "LibRaw",
    ConformanceOracle.Krita => "Krita",
    ConformanceOracle.Hp2xx => "hp2xx",
    ConformanceOracle.GhostPcl => "GhostPCL",
    ConformanceOracle.Dcmtk => "DCMTK",
    _ => oracle.ToString(),
  };

  /// <summary>Where the tool lives, so a reader can go and get the one that judged us.</summary>
  public static string? HomePage(this ConformanceOracle oracle) => oracle switch {
    ConformanceOracle.None => null,
    ConformanceOracle.Recoil2Png => "https://recoil.sourceforge.net/",
    ConformanceOracle.ImageMagick => "https://imagemagick.org/",
    ConformanceOracle.XnView => "https://www.xnview.com/en/nconvert/",
    ConformanceOracle.IrfanView => "https://www.irfanview.com/",
    ConformanceOracle.TomsEditor => "https://products.aspose.app/imaging/conversion",
    ConformanceOracle.DWebp => "https://developers.google.com/speed/webp/docs/dwebp",
    ConformanceOracle.Djxl => "https://github.com/libjxl/libjxl",
    ConformanceOracle.OpjDecompress => "https://www.openjpeg.org/",
    ConformanceOracle.Dec265 => "https://github.com/strukturag/libde265",
    ConformanceOracle.HeifDec => "https://github.com/strukturag/libheif",
    ConformanceOracle.AvifDec => "https://github.com/AOMediaCodec/libavif",
    ConformanceOracle.FFmpeg => "https://ffmpeg.org/",
    ConformanceOracle.ExifTool => "https://exiftool.org/",
    ConformanceOracle.PyEmbroidery => "https://github.com/EmbroidePy/pyembroidery",
    ConformanceOracle.LibreOffice => "https://www.libreoffice.org/",
    ConformanceOracle.Olefile => "https://github.com/decalage2/olefile",
    ConformanceOracle.Ghostscript => "https://www.ghostscript.com/",
    ConformanceOracle.Deark => "https://github.com/jsummers/deark",
    ConformanceOracle.AstcEnc => "https://github.com/ARM-software/astc-encoder",
    ConformanceOracle.KtxTools => "https://github.com/KhronosGroup/KTX-Software",
    ConformanceOracle.DjVuLibre => "https://djvu.sourceforge.net/",
    ConformanceOracle.LibBpg => "https://bellard.org/bpg/",
    ConformanceOracle.Flif => "https://github.com/FLIF-hub/FLIF",
    ConformanceOracle.Jbig2Dec => "https://github.com/ArtifexSoftware/jbig2dec",
    ConformanceOracle.Jbig2Enc => "https://github.com/agl/jbig2enc",
    ConformanceOracle.Gdal => "https://gdal.org/",
    ConformanceOracle.Convert3D => "https://www.itksnap.org/pmwiki/pmwiki.php?n=Convert3D.Convert3D",
    ConformanceOracle.Netpbm => "https://netpbm.sourceforge.net/",
    ConformanceOracle.AnsiLove => "https://github.com/ansilove/ansilove",
    ConformanceOracle.LibRaw => "https://www.libraw.org/",
    ConformanceOracle.Krita => "https://krita.org/",
    ConformanceOracle.Hp2xx => "https://github.com/dgtlrift/hp2xx",
    ConformanceOracle.GhostPcl => "https://ghostscript.com/",
    ConformanceOracle.Dcmtk => "https://dicom.offis.de/dcmtk.php.en",
    _ => null,
  };
}
