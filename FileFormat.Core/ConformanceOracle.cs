namespace FileFormat.Core;

/// <summary>
/// A tool from outside this repository used as an independent conformance oracle.
/// </summary>
/// <remarks>
/// The direction of the check belongs to the evidence using this enum. <see cref="VerifiedByAttribute"/>
/// means the external tool read what our writer produced; reader-conformance surveys may instead ask
/// an external writer to produce bytes and compare our decode with that toolchain's own decode. In
/// either direction the important property is independence: this repository's own reader and writer
/// are never an oracle for each other. Merely listing a tool here is not a conformance claim.
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

  /// <summary>Vidvox Hap reference decoder paired with bcdec for BC7/BC6H texture decoding.</summary>
  VidvoxHap,

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

  /// <summary>The official PNG reference library, normally exercised by <c>pngtest</c> or a tiny harness.</summary>
  LibPng,

  /// <summary>libjpeg-turbo / IJG JPEG tools such as <c>cjpeg</c> and <c>djpeg</c>.</summary>
  LibJpegTurbo,

  /// <summary>giflib's native GIF tools such as <c>gif2rgb</c>.</summary>
  GifLib,

  /// <summary>libtiff's native TIFF tools such as <c>tiffinfo</c> and <c>tiffcp</c>.</summary>
  LibTiff,

  /// <summary>The OpenEXR reference implementation and command-line utilities.</summary>
  OpenExr,

  /// <summary>libspng, an implementation independent of libpng, exercised through a small harness.</summary>
  LibSpng,

  /// <summary>Wuffs image decoders, normally exercised through its example decoder harness.</summary>
  Wuffs,

  /// <summary><c>stb_image</c>, exercised through a deliberately minimal standalone harness.</summary>
  StbImage,

  /// <summary>TinyEXR, exercised through its example tools or a minimal standalone harness.</summary>
  TinyExr,

  /// <summary>The QOI reference implementation, normally exercised through <c>qoiconv</c>.</summary>
  QoiReference,

  /// <summary>OpenImageIO, normally exercised through <c>oiiotool</c> or <c>iconvert</c>.</summary>
  OpenImageIo,

  /// <summary>GIMP's import/export stack, driven in batch mode.</summary>
  Gimp,

  /// <summary>libvips, normally exercised through the <c>vips</c> command-line program.</summary>
  LibVips,

  /// <summary>Google/WebM's VP8/VP9 reference SDK, normally <c>vpxdec</c> / <c>vpxenc</c>.</summary>
  LibVpx,

  /// <summary>Alliance for Open Media's AV1 reference codec, normally <c>aomdec</c> / <c>aomenc</c>.</summary>
  LibAom,

  /// <summary>VideoLAN's independent AV1 decoder, <c>dav1d</c>.</summary>
  Dav1d,

  /// <summary>SVT-AV1's encoder application and decoder where available.</summary>
  SvtAv1,

  /// <summary>Xiph's independent AV1 encoder, <c>rav1e</c>.</summary>
  Rav1e,

  /// <summary>The x264 H.264/AVC encoder.</summary>
  X264,

  /// <summary>The x265 HEVC encoder.</summary>
  X265,

  /// <summary>The independent Kvazaar HEVC encoder.</summary>
  Kvazaar,

  /// <summary>Xvid's MPEG-4 Part 2 tools, including <c>xvid_decraw</c>.</summary>
  Xvid,

  /// <summary>Xiph's Theora reference implementation and example tools.</summary>
  LibTheora,

  /// <summary>Cisco's OpenH264 encoder/decoder tools.</summary>
  OpenH264,

  /// <summary>The H.264/AVC Joint Model reference software (JM).</summary>
  JmReference,

  /// <summary>The HEVC Test Model reference software (HM).</summary>
  HmReference,

  /// <summary>The VVC Test Model reference software (VTM).</summary>
  VtmReference,

  /// <summary>Fraunhofer's independent VVC decoder, normally <c>vvdecapp</c>.</summary>
  VvDec,

  /// <summary>Fraunhofer's independent VVC encoder, normally <c>vvencapp</c>.</summary>
  VvEnc,

  /// <summary>GoPro's released CineForm SDK, normally exercised through <c>TestCFHD</c>.</summary>
  CineFormSdk,

  /// <summary>BBC's VC-2 conformance suite, including its bitstream validator and reference decoder.</summary>
  Vc2Conformance,

  /// <summary>GPAC's independent multimedia parser/packager, normally <c>MP4Box</c>.</summary>
  Gpac,

  /// <summary>Bento4's ISO-BMFF inspection tools such as <c>mp4info</c> and <c>mp4dump</c>.</summary>
  Bento4,

  /// <summary>MKVToolNix's Matroska/WebM tools such as <c>mkvinfo</c>, <c>mkvmerge</c> and <c>mkvextract</c>.</summary>
  MkvToolNix,

  /// <summary>WebM's native parser/muxer library and sample tools.</summary>
  LibWebM,

  /// <summary>Google's Shaka Packager for independent ISO-BMFF, WebM and MPEG-2 TS parsing/packaging.</summary>
  ShakaPackager,

  /// <summary>Xiph's Ogg validation tools, normally <c>oggz validate</c>.</summary>
  Oggz,

  /// <summary>MediaArea's independent metadata/container inspector, <c>mediainfo</c>.</summary>
  MediaInfo,

  /// <summary>MediaArea's preservation-oriented conformance checker, <c>mediaconch</c>.</summary>
  MediaConch,
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
    ConformanceOracle.VidvoxHap => "Vidvox Hap + bcdec",
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
    ConformanceOracle.LibPng => "libpng",
    ConformanceOracle.LibJpegTurbo => "libjpeg-turbo",
    ConformanceOracle.GifLib => "giflib",
    ConformanceOracle.LibTiff => "libtiff",
    ConformanceOracle.OpenExr => "OpenEXR",
    ConformanceOracle.LibSpng => "libspng",
    ConformanceOracle.Wuffs => "Wuffs",
    ConformanceOracle.StbImage => "stb_image",
    ConformanceOracle.TinyExr => "TinyEXR",
    ConformanceOracle.QoiReference => "QOI reference",
    ConformanceOracle.OpenImageIo => "OpenImageIO",
    ConformanceOracle.Gimp => "GIMP",
    ConformanceOracle.LibVips => "libvips",
    ConformanceOracle.LibVpx => "libvpx",
    ConformanceOracle.LibAom => "libaom",
    ConformanceOracle.Dav1d => "dav1d",
    ConformanceOracle.SvtAv1 => "SVT-AV1",
    ConformanceOracle.Rav1e => "rav1e",
    ConformanceOracle.X264 => "x264",
    ConformanceOracle.X265 => "x265",
    ConformanceOracle.Kvazaar => "Kvazaar",
    ConformanceOracle.Xvid => "Xvid",
    ConformanceOracle.LibTheora => "libtheora",
    ConformanceOracle.OpenH264 => "OpenH264",
    ConformanceOracle.JmReference => "JM",
    ConformanceOracle.HmReference => "HM",
    ConformanceOracle.VtmReference => "VTM",
    ConformanceOracle.VvDec => "VVdeC",
    ConformanceOracle.VvEnc => "VVenC",
    ConformanceOracle.CineFormSdk => "CineForm SDK",
    ConformanceOracle.Vc2Conformance => "VC-2 conformance suite",
    ConformanceOracle.Gpac => "GPAC / MP4Box",
    ConformanceOracle.Bento4 => "Bento4",
    ConformanceOracle.MkvToolNix => "MKVToolNix",
    ConformanceOracle.LibWebM => "libwebm",
    ConformanceOracle.ShakaPackager => "Shaka Packager",
    ConformanceOracle.Oggz => "oggz",
    ConformanceOracle.MediaInfo => "MediaInfo",
    ConformanceOracle.MediaConch => "MediaConch",
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
    ConformanceOracle.VidvoxHap => "https://github.com/Vidvox/hap",
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
    ConformanceOracle.LibPng => "https://github.com/pnggroup/libpng",
    ConformanceOracle.LibJpegTurbo => "https://github.com/libjpeg-turbo/libjpeg-turbo",
    ConformanceOracle.GifLib => "https://giflib.sourceforge.net/",
    ConformanceOracle.LibTiff => "https://libtiff.gitlab.io/libtiff/",
    ConformanceOracle.OpenExr => "https://openexr.com/",
    ConformanceOracle.LibSpng => "https://github.com/randy408/libspng",
    ConformanceOracle.Wuffs => "https://github.com/google/wuffs",
    ConformanceOracle.StbImage => "https://github.com/nothings/stb",
    ConformanceOracle.TinyExr => "https://github.com/syoyo/tinyexr",
    ConformanceOracle.QoiReference => "https://github.com/phoboslab/qoi",
    ConformanceOracle.OpenImageIo => "https://openimageio.readthedocs.io/",
    ConformanceOracle.Gimp => "https://www.gimp.org/",
    ConformanceOracle.LibVips => "https://www.libvips.org/",
    ConformanceOracle.LibVpx => "https://chromium.googlesource.com/webm/libvpx/",
    ConformanceOracle.LibAom => "https://aomedia.googlesource.com/aom/",
    ConformanceOracle.Dav1d => "https://code.videolan.org/videolan/dav1d/",
    ConformanceOracle.SvtAv1 => "https://gitlab.com/AOMediaCodec/SVT-AV1",
    ConformanceOracle.Rav1e => "https://github.com/xiph/rav1e",
    ConformanceOracle.X264 => "https://www.videolan.org/developers/x264.html",
    ConformanceOracle.X265 => "https://bitbucket.org/multicoreware/x265_git/",
    ConformanceOracle.Kvazaar => "https://github.com/ultravideo/kvazaar",
    ConformanceOracle.Xvid => "https://www.xvid.com/",
    ConformanceOracle.LibTheora => "https://www.theora.org/",
    ConformanceOracle.OpenH264 => "https://github.com/cisco/openh264",
    ConformanceOracle.JmReference => "https://avc.hhi.fraunhofer.de/",
    ConformanceOracle.HmReference => "https://hevc.hhi.fraunhofer.de/",
    ConformanceOracle.VtmReference => "https://vcgit.hhi.fraunhofer.de/jvet/VVCSoftware_VTM",
    ConformanceOracle.VvDec => "https://github.com/fraunhoferhhi/vvdec",
    ConformanceOracle.VvEnc => "https://github.com/fraunhoferhhi/vvenc",
    ConformanceOracle.CineFormSdk => "https://github.com/gopro/cineform-sdk",
    ConformanceOracle.Vc2Conformance => "https://github.com/bbc/vc2_conformance",
    ConformanceOracle.Gpac => "https://gpac.io/",
    ConformanceOracle.Bento4 => "https://www.bento4.com/",
    ConformanceOracle.MkvToolNix => "https://mkvtoolnix.download/",
    ConformanceOracle.LibWebM => "https://www.webmproject.org/code/",
    ConformanceOracle.ShakaPackager => "https://github.com/shaka-project/shaka-packager",
    ConformanceOracle.Oggz => "https://www.xiph.org/oggz/",
    ConformanceOracle.MediaInfo => "https://mediaarea.net/en/MediaInfo",
    ConformanceOracle.MediaConch => "https://mediaarea.net/MediaConch",
    _ => null,
  };
}
