namespace FileFormat.Core;

/// <summary>
/// A tool from outside this repository that has read what one of our writers produced.
/// </summary>
/// <remarks>
/// A writer checked only by our own reader is worth less than no writer, because the two can share a
/// misunderstanding and the round trip will not notice. That has happened here repeatedly — a coder
/// that stored one colour space under another's name, a table of contents nothing else could follow,
/// a predictor wired to the wrong neighbours — and in every case the pair agreed with itself
/// perfectly right up until something else looked at the bytes.
/// <para/>
/// So this names the something else. A member of this enum is a program nobody here wrote, and a
/// format naming one is claiming that that program has read its output. <see cref="None"/> is the
/// honest answer everywhere else, and it is not a defect to record — it is the whole reason the
/// column exists.
/// </remarks>
public enum ConformanceOracle {

  /// <summary>Nothing outside this repository has read what this writer produces.</summary>
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
    _ => null,
  };
}
