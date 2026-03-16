using System;
using System.IO;
using System.Text;

namespace Optimizer.Image;

/// <summary>Detects image format from file magic bytes or extension.</summary>
public static class ImageFormatDetector {

  /// <summary>Detects image format using magic bytes first, extension fallback.</summary>
  public static ImageFormat Detect(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("File not found.", file.FullName);

    var header = new byte[132];
    using (var stream = file.OpenRead()) {
      var bytesRead = stream.Read(header, 0, header.Length);
      if (bytesRead < 2)
        return ImageFormat.Unknown;
    }

    var result = DetectFromSignature(header);
    return result != ImageFormat.Unknown ? result : DetectFromExtension(file);
  }

  /// <summary>Detects image format from magic bytes.</summary>
  public static ImageFormat DetectFromSignature(ReadOnlySpan<byte> header) {
    if (header.Length < 2)
      return ImageFormat.Unknown;

    // PNG: 89 50 4E 47
    if (header.Length >= 4 && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47)
      return ImageFormat.Png;

    // GIF: 47 49 46 38
    if (header.Length >= 4 && header[0] == 0x47 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x38)
      return ImageFormat.Gif;

    // TIFF / BigTIFF / JPEG XR: II (49 49) prefix
    if (header.Length >= 4 && header[0] == 0x49 && header[1] == 0x49) {
      if (header[2] == 0x2A && header[3] == 0x00)
        return ImageFormat.Tiff;
      if (header[2] == 0x2B && header[3] == 0x00)
        return ImageFormat.BigTiff;
      if (header[2] == 0x01 && header[3] == 0xBC)
        return ImageFormat.JpegXr;
    }

    // TIFF/BigTIFF BE: MM (4D 4D) prefix
    if (header.Length >= 4 && header[0] == 0x4D && header[1] == 0x4D) {
      if (header[2] == 0x00 && header[3] == 0x2A)
        return ImageFormat.Tiff;
      if (header[2] == 0x00 && header[3] == 0x2B)
        return ImageFormat.BigTiff;
    }

    // JPEG-LS: FF D8 FF F7 (SOI + SOF55) — must be before generic JPEG check
    if (header.Length >= 4 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF && header[3] == 0xF7)
      return ImageFormat.JpegLs;

    // WSQ: FF A0 (SOI marker)
    if (header.Length >= 2 && header[0] == 0xFF && header[1] == 0xA0)
      return ImageFormat.Wsq;

    // JPEG: FF D8 FF
    if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
      return ImageFormat.Jpeg;

    // BMP: 42 4D
    if (header[0] == 0x42 && header[1] == 0x4D)
      return ImageFormat.Bmp;

    // RIFF-based: WebP or ANI
    if (header.Length >= 12 && header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46) {
      if (header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50)
        return ImageFormat.WebP;
      if (header[8] == 0x41 && header[9] == 0x43 && header[10] == 0x4F && header[11] == 0x4E)
        return ImageFormat.Ani;
    }

    // JPEG XL bare codestream: FF 0A
    if (header.Length >= 2 && header[0] == 0xFF && header[1] == 0x0A)
      return ImageFormat.JpegXl;

    // BPG: 42 50 47 FB
    if (header.Length >= 4 && header[0] == 0x42 && header[1] == 0x50 && header[2] == 0x47 && header[3] == 0xFB)
      return ImageFormat.Bpg;

    // JPEG 2000: 00 00 00 0C 6A 50 (JP2 Signature box)
    if (header.Length >= 6 && header[0] == 0x00 && header[1] == 0x00 && header[2] == 0x00 && header[3] == 0x0C
        && header[4] == 0x6A && header[5] == 0x50)
      return ImageFormat.Jpeg2000;

    // ISOBMFF-based: check for ftyp box at offset 4 ("ftyp" = 66 74 79 70), brand at offset 8
    if (header.Length >= 12 && header[4] == 0x66 && header[5] == 0x74 && header[6] == 0x79 && header[7] == 0x70) {
      // HEIF: brands heic, heix, hevc, mif1
      if ((header[8] == (byte)'h' && header[9] == (byte)'e' && header[10] == (byte)'i' && header[11] == (byte)'c')
          || (header[8] == (byte)'h' && header[9] == (byte)'e' && header[10] == (byte)'i' && header[11] == (byte)'x')
          || (header[8] == (byte)'h' && header[9] == (byte)'e' && header[10] == (byte)'v' && header[11] == (byte)'c')
          || (header[8] == (byte)'m' && header[9] == (byte)'i' && header[10] == (byte)'f' && header[11] == (byte)'1'))
        return ImageFormat.Heif;

      // AVIF: brands avif, avis
      if ((header[8] == (byte)'a' && header[9] == (byte)'v' && header[10] == (byte)'i' && header[11] == (byte)'f')
          || (header[8] == (byte)'a' && header[9] == (byte)'v' && header[10] == (byte)'i' && header[11] == (byte)'s'))
        return ImageFormat.Avif;

      // JPEG XL container: brand "jxl " (6A 78 6C 20)
      if (header[8] == (byte)'j' && header[9] == (byte)'x' && header[10] == (byte)'l' && header[11] == (byte)' ')
        return ImageFormat.JpegXl;
    }

    // ICO: 00 00 01 00
    if (header.Length >= 4 && header[0] == 0x00 && header[1] == 0x00 && header[2] == 0x01 && header[3] == 0x00)
      return ImageFormat.Ico;

    // CUR: 00 00 02 00
    if (header.Length >= 4 && header[0] == 0x00 && header[1] == 0x00 && header[2] == 0x02 && header[3] == 0x00)
      return ImageFormat.Cur;

    // QOI: 71 6F 69 66 ("qoif")
    if (header.Length >= 4 && header[0] == 0x71 && header[1] == 0x6F && header[2] == 0x69 && header[3] == 0x66)
      return ImageFormat.Qoi;

    // Farbfeld: 66 61 72 62 66 65 6C 64 ("farbfeld")
    if (header.Length >= 8
        && header[0] == 0x66 && header[1] == 0x61 && header[2] == 0x72 && header[3] == 0x62
        && header[4] == 0x66 && header[5] == 0x65 && header[6] == 0x6C && header[7] == 0x64)
      return ImageFormat.Farbfeld;

    // DDS: "DDS " (44 44 53 20)
    if (header.Length >= 4 && header[0] == 0x44 && header[1] == 0x44 && header[2] == 0x53 && header[3] == 0x20)
      return ImageFormat.Dds;

    // PSD/PSB: "8BPS" (38 42 50 53) — version 1 = PSD, version 2 = PSB
    if (header.Length >= 6 && header[0] == 0x38 && header[1] == 0x42 && header[2] == 0x50 && header[3] == 0x53) {
      if (header[4] == 0x00 && header[5] == 0x02)
        return ImageFormat.Psb;
      return ImageFormat.Psd;
    }

    // VTF: "VTF\0" (56 54 46 00)
    if (header.Length >= 4 && header[0] == 0x56 && header[1] == 0x54 && header[2] == 0x46 && header[3] == 0x00)
      return ImageFormat.Vtf;

    // KTX1: AB 4B 54 58 20 31 31 BB
    if (header.Length >= 8 && header[0] == 0xAB && header[1] == 0x4B && header[2] == 0x54 && header[3] == 0x58
        && header[4] == 0x20 && header[5] == 0x31 && header[6] == 0x31 && header[7] == 0xBB)
      return ImageFormat.Ktx;

    // KTX2: AB 4B 54 58 20 32 30 BB
    if (header.Length >= 8 && header[0] == 0xAB && header[1] == 0x4B && header[2] == 0x54 && header[3] == 0x58
        && header[4] == 0x20 && header[5] == 0x32 && header[6] == 0x30 && header[7] == 0xBB)
      return ImageFormat.Ktx;

    // EXR: 76 2F 31 01
    if (header.Length >= 4 && header[0] == 0x76 && header[1] == 0x2F && header[2] == 0x31 && header[3] == 0x01)
      return ImageFormat.Exr;

    // ASTC: 13 AB A1 5C (LE magic)
    if (header.Length >= 4 && header[0] == 0x13 && header[1] == 0xAB && header[2] == 0xA1 && header[3] == 0x5C)
      return ImageFormat.Astc;

    // PKM: "PKM " (50 4B 4D 20)
    if (header.Length >= 4 && header[0] == 0x50 && header[1] == 0x4B && header[2] == 0x4D && header[3] == 0x20)
      return ImageFormat.Pkm;

    // PVR: 50 56 52 03 (LE) = 0x03525650
    if (header.Length >= 4 && header[0] == 0x50 && header[1] == 0x56 && header[2] == 0x52 && header[3] == 0x03)
      return ImageFormat.Pvr;

    // SGI: 01 DA
    if (header.Length >= 2 && header[0] == 0x01 && header[1] == 0xDA)
      return ImageFormat.Sgi;

    // Sun Raster: 59 A6 6A 95
    if (header.Length >= 4 && header[0] == 0x59 && header[1] == 0xA6 && header[2] == 0x6A && header[3] == 0x95)
      return ImageFormat.SunRaster;

    // Cineon: 80 2A 5F D7
    if (header.Length >= 4 && header[0] == 0x80 && header[1] == 0x2A && header[2] == 0x5F && header[3] == 0xD7)
      return ImageFormat.Cineon;

    // DPX: "SDPX" or "XPDS"
    if (header.Length >= 4) {
      if (header[0] == 0x53 && header[1] == 0x44 && header[2] == 0x50 && header[3] == 0x58)
        return ImageFormat.Dpx;
      if (header[0] == 0x58 && header[1] == 0x50 && header[2] == 0x44 && header[3] == 0x53)
        return ImageFormat.Dpx;
    }

    // Maya IFF: FOR4 + CIMG at offset 8
    if (header.Length >= 12 && header[0] == 0x46 && header[1] == 0x4F && header[2] == 0x52 && header[3] == 0x34) {
      if (header[8] == 0x43 && header[9] == 0x49 && header[10] == 0x4D && header[11] == 0x47)
        return ImageFormat.MayaIff;
    }

    // IFF-based: "FORM" at offset 0 with type at offset 8
    if (header.Length >= 12 && header[0] == 0x46 && header[1] == 0x4F && header[2] == 0x52 && header[3] == 0x4D) {
      if (header[8] == 0x49 && header[9] == 0x4C && header[10] == 0x42 && header[11] == 0x4D)
        return ImageFormat.Ilbm;
      // IFF ANIM: FORM + "ANIM" at offset 8
      if (header[8] == 0x41 && header[9] == 0x4E && header[10] == 0x49 && header[11] == 0x4D)
        return ImageFormat.IffAnim;
      // IFF PBM: FORM + "PBM " at offset 8
      if (header[8] == 0x50 && header[9] == 0x42 && header[10] == 0x4D && header[11] == 0x20)
        return ImageFormat.IffPbm;
      // IFF ACBM: FORM + "ACBM" at offset 8
      if (header[8] == 0x41 && header[9] == 0x43 && header[10] == 0x42 && header[11] == 0x4D)
        return ImageFormat.IffAcbm;
      // IFF DEEP: FORM + "DEEP" at offset 8
      if (header[8] == 0x44 && header[9] == 0x45 && header[10] == 0x45 && header[11] == 0x50)
        return ImageFormat.IffDeep;
      // IFF RGB8: FORM + "RGB8" at offset 8
      if (header[8] == 0x52 && header[9] == 0x47 && header[10] == 0x42 && header[11] == 0x38)
        return ImageFormat.IffRgb8;
      // IFF RGBN: FORM + "RGBN" at offset 8
      if (header[8] == 0x52 && header[9] == 0x47 && header[10] == 0x42 && header[11] == 0x4E)
        return ImageFormat.IffRgbn;
    }

    // FLI: AF 11 at offset 4 or AF 12 at offset 4
    if (header.Length >= 6 && header[4] == 0x11 && header[5] == 0xAF)
      return ImageFormat.Fli;
    if (header.Length >= 6 && header[4] == 0x12 && header[5] == 0xAF)
      return ImageFormat.Fli;

    // WAD2: "WAD2" (57 41 44 32)
    if (header.Length >= 4 && header[0] == 0x57 && header[1] == 0x41 && header[2] == 0x44 && header[3] == 0x32)
      return ImageFormat.Wad2;

    // WAD3: "WAD3"
    if (header.Length >= 4 && header[0] == 0x57 && header[1] == 0x41 && header[2] == 0x44 && header[3] == 0x33)
      return ImageFormat.Wad3;

    // WAD: "IWAD" or "PWAD"
    if (header.Length >= 4) {
      if (header[0] == 0x49 && header[1] == 0x57 && header[2] == 0x41 && header[3] == 0x44)
        return ImageFormat.Wad;
      if (header[0] == 0x50 && header[1] == 0x57 && header[2] == 0x41 && header[3] == 0x44)
        return ImageFormat.Wad;
    }

    // TIM: 10 00 00 00
    if (header.Length >= 4 && header[0] == 0x10 && header[1] == 0x00 && header[2] == 0x00 && header[3] == 0x00)
      return ImageFormat.Tim;

    // Utah RLE: CC 52
    if (header.Length >= 2 && header[0] == 0xCC && header[1] == 0x52)
      return ImageFormat.UtahRle;

    // MSP v1: 44 61 68 6E ("Dahn")
    if (header.Length >= 4 && header[0] == 0x44 && header[1] == 0x61 && header[2] == 0x68 && header[3] == 0x6E)
      return ImageFormat.Msp;

    // MSP v2: 4C 69 6E 53 ("LinS")
    if (header.Length >= 4 && header[0] == 0x4C && header[1] == 0x69 && header[2] == 0x6E && header[3] == 0x53)
      return ImageFormat.Msp;

    // SFF: "SFFF"
    if (header.Length >= 4 && header[0] == 0x53 && header[1] == 0x46 && header[2] == 0x46 && header[3] == 0x46)
      return ImageFormat.Sff;

    // XCF: "gimp xcf"
    if (header.Length >= 8 && header[0] == 0x67 && header[1] == 0x69 && header[2] == 0x6D && header[3] == 0x70
        && header[4] == 0x20 && header[5] == 0x78 && header[6] == 0x63 && header[7] == 0x66)
      return ImageFormat.Xcf;

    // FITS: "SIMPLE" (starts with "SIMPLE  =")
    if (header.Length >= 6 && header[0] == 0x53 && header[1] == 0x49 && header[2] == 0x4D && header[3] == 0x50
        && header[4] == 0x4C && header[5] == 0x45)
      return ImageFormat.Fits;

    // HDR: "#?" (Radiance)
    if (header.Length >= 2 && header[0] == 0x23 && header[1] == 0x3F)
      return ImageFormat.Hdr;

    // MIFF: "id=ImageMagick"
    if (header.Length >= 14 && header[0] == 0x69 && header[1] == 0x64 && header[2] == 0x3D
        && header[3] == 0x49 && header[4] == 0x6D && header[5] == 0x61 && header[6] == 0x67
        && header[7] == 0x65 && header[8] == 0x4D && header[9] == 0x61 && header[10] == 0x67
        && header[11] == 0x69 && header[12] == 0x63 && header[13] == 0x6B)
      return ImageFormat.Miff;

    // CLP: C3 50 at bytes 0-1 (0xC350 file ID)
    if (header.Length >= 2 && header[0] == 0xC3 && header[1] == 0x50)
      return ImageFormat.Clp;

    // BSAVE: FD magic
    if (header.Length >= 1 && header[0] == 0xFD)
      return ImageFormat.Bsave;

    // DCX: 3ADE68B1 (LE)
    if (header.Length >= 4 && header[0] == 0xB1 && header[1] == 0x68 && header[2] == 0xDE && header[3] == 0x3A)
      return ImageFormat.Dcx;

    // XV Thumbnail: "P7 332" (50 37 20 33 33 32) — must be before Netpbm P1-P7
    if (header.Length >= 6 && header[0] == 0x50 && header[1] == 0x37 && header[2] == 0x20
        && header[3] == 0x33 && header[4] == 0x33 && header[5] == 0x32)
      return ImageFormat.XvThumbnail;

    // Netpbm: P1-P7
    if (header.Length >= 2 && header[0] == 0x50 && header[1] >= 0x31 && header[1] <= 0x37)
      return ImageFormat.Netpbm;

    // MNG: 8A 4D 4E 47
    if (header.Length >= 4 && header[0] == 0x8A && header[1] == 0x4D && header[2] == 0x4E && header[3] == 0x47)
      return ImageFormat.Mng;

    // JNG: 8B 4A 4E 47
    if (header.Length >= 4 && header[0] == 0x8B && header[1] == 0x4A && header[2] == 0x4E && header[3] == 0x47)
      return ImageFormat.Jng;

    // NRRD: "NRRD"
    if (header.Length >= 4 && header[0] == 0x4E && header[1] == 0x52 && header[2] == 0x52 && header[3] == 0x44)
      return ImageFormat.Nrrd;

    // ENVI: "ENVI" + CR/LF at offset 4
    if (header.Length >= 5 && header[0] == 0x45 && header[1] == 0x4E && header[2] == 0x56 && header[3] == 0x49
        && (header[4] == 0x0D || header[4] == 0x0A))
      return ImageFormat.Envi;

    // Interfile: "!INTERFILE" (21 49 4E 54 45 52 46 49 4C 45)
    if (header.Length >= 10 && header[0] == 0x21 && header[1] == 0x49 && header[2] == 0x4E && header[3] == 0x54
        && header[4] == 0x45 && header[5] == 0x52 && header[6] == 0x46 && header[7] == 0x49
        && header[8] == 0x4C && header[9] == 0x45)
      return ImageFormat.Interfile;

    // VIFF: AB magic
    if (header.Length >= 1 && header[0] == 0xAB && (header.Length < 2 || header[1] == 0x01))
      return ImageFormat.Viff;

    // CMU: 14-byte header check via extension only (no unique magic)

    // PFM: "PF" or "Pf"
    if (header.Length >= 2 && header[0] == 0x50 && (header[1] == 0x46 || header[1] == 0x66)) {
      // Make sure it's not Netpbm (P1-P7) which would start with P + digit
      if (header.Length >= 3 && (header[2] == 0x0A || header[2] == 0x0D || header[2] == 0x20))
        return ImageFormat.Pfm;
    }

    // DICOM: "DICM" at offset 128
    if (header.Length >= 132 && header[128] == 0x44 && header[129] == 0x49 && header[130] == 0x43 && header[131] == 0x4D)
      return ImageFormat.Dicom;

    // WPG: FF 57 50 43
    if (header.Length >= 4 && header[0] == 0xFF && header[1] == 0x57 && header[2] == 0x50 && header[3] == 0x43)
      return ImageFormat.Wpg;

    // FBM: "%bitmap\0" (25 62 69 74 6D 61 70 00)
    if (header.Length >= 8 && header[0] == 0x25 && header[1] == 0x62 && header[2] == 0x69 && header[3] == 0x74
        && header[4] == 0x6D && header[5] == 0x61 && header[6] == 0x70 && header[7] == 0x00)
      return ImageFormat.Fbm;

    // LSS16: 3D F3 13 14
    if (header.Length >= 4 && header[0] == 0x3D && header[1] == 0xF3 && header[2] == 0x13 && header[3] == 0x14)
      return ImageFormat.Lss16;

    // ColoRIX: "RIX3" (52 49 58 33)
    if (header.Length >= 4 && header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x58 && header[3] == 0x33)
      return ImageFormat.ColoRix;

    // XYZ: "XYZ1" (58 59 5A 31)
    if (header.Length >= 4 && header[0] == 0x58 && header[1] == 0x59 && header[2] == 0x5A && header[3] == 0x31)
      return ImageFormat.Xyz;

    // GBR: "GIMP" at offset 20 (need 24+ bytes)
    if (header.Length >= 24 && header[20] == 0x47 && header[21] == 0x49 && header[22] == 0x4D && header[23] == 0x50)
      return ImageFormat.Gbr;

    // PAT: "GPAT" at offset 20 (need 24+ bytes)
    if (header.Length >= 24 && header[20] == 0x47 && header[21] == 0x50 && header[22] == 0x41 && header[23] == 0x54)
      return ImageFormat.Pat;

    // CEL: "KiSS" (4B 69 53 53)
    if (header.Length >= 4 && header[0] == 0x4B && header[1] == 0x69 && header[2] == 0x53 && header[3] == 0x53)
      return ImageFormat.Cel;

    // AmigaIcon: E3 10
    if (header.Length >= 2 && header[0] == 0xE3 && header[1] == 0x10)
      return ImageFormat.AmigaIcon;

    // GAF: 00 01 01 00 (Total Annihilation)
    if (header.Length >= 4 && header[0] == 0x00 && header[1] == 0x01 && header[2] == 0x01 && header[3] == 0x00)
      return ImageFormat.Gaf;

    // SunIcon: "/* " (C comment opening — text-based)
    if (header.Length >= 3 && header[0] == 0x2F && header[1] == 0x2A && header[2] == 0x20)
      return ImageFormat.SunIcon;

    // ICNS: "icns" (69 63 6E 73)
    if (header.Length >= 4 && header[0] == 0x69 && header[1] == 0x63 && header[2] == 0x6E && header[3] == 0x73)
      return ImageFormat.Icns;

    // BLP: "BLP2" (42 4C 50 32)
    if (header.Length >= 4 && header[0] == 0x42 && header[1] == 0x4C && header[2] == 0x50 && header[3] == 0x32)
      return ImageFormat.Blp;

    // FSH: "SHPI" (53 48 50 49)
    if (header.Length >= 4 && header[0] == 0x53 && header[1] == 0x48 && header[2] == 0x50 && header[3] == 0x49)
      return ImageFormat.Fsh;

    // PDS: "PDS_" (50 44 53 5F) — text header starting with PDS_VERSION_ID
    if (header.Length >= 4 && header[0] == 0x50 && header[1] == 0x44 && header[2] == 0x53 && header[3] == 0x5F)
      return ImageFormat.Pds;

    // BioRadPic: file_id = 12345 (0x39 0x30 LE) at offset 54
    if (header.Length >= 56 && header[54] == 0x39 && header[55] == 0x30)
      return ImageFormat.BioRadPic;

    // PalmPdb: "Img " at offset 60 (type field in PDB header)
    if (header.Length >= 64 && header[60] == 0x49 && header[61] == 0x6D && header[62] == 0x67 && header[63] == 0x20)
      return ImageFormat.PalmPdb;

    // AWD: "AWD\0" (41 57 44 00)
    if (header.Length >= 4 && header[0] == 0x41 && header[1] == 0x57 && header[2] == 0x44 && header[3] == 0x00)
      return ImageFormat.Awd;

    // Symbian MBM: 37 00 00 10 (UID1 KDirectFileStoreLayoutUid)
    if (header.Length >= 4 && header[0] == 0x37 && header[1] == 0x00 && header[2] == 0x00 && header[3] == 0x10)
      return ImageFormat.SymbianMbm;

    // GD2: "gd2\0" (67 64 32 00)
    if (header.Length >= 4 && header[0] == 0x67 && header[1] == 0x64 && header[2] == 0x32 && header[3] == 0x00)
      return ImageFormat.Gd2;

    // Softimage PIC: 53 80 F6 34 (BE magic)
    if (header.Length >= 4 && header[0] == 0x53 && header[1] == 0x80 && header[2] == 0xF6 && header[3] == 0x34)
      return ImageFormat.SoftImage;

    // Xcursor: "Xcur" (58 63 75 72)
    if (header.Length >= 4 && header[0] == 0x58 && header[1] == 0x63 && header[2] == 0x75 && header[3] == 0x72)
      return ImageFormat.Xcursor;

    // PSP: "Paint Sh" (50 61 69 6E 74 20 53 68) — first 8 bytes of 32-byte magic
    if (header.Length >= 8 && header[0] == 0x50 && header[1] == 0x61 && header[2] == 0x69 && header[3] == 0x6E
        && header[4] == 0x74 && header[5] == 0x20 && header[6] == 0x53 && header[7] == 0x68)
      return ImageFormat.Psp;

    // NITF: "NITF" (4E 49 54 46)
    if (header.Length >= 4 && header[0] == 0x4E && header[1] == 0x49 && header[2] == 0x54 && header[3] == 0x46)
      return ImageFormat.Nitf;

    // UHDR: "UHDR" (55 48 44 52)
    if (header.Length >= 4 && header[0] == 0x55 && header[1] == 0x48 && header[2] == 0x44 && header[3] == 0x52)
      return ImageFormat.Uhdr;

    // PhotoPaint: "CPT\0" (43 50 54 00)
    if (header.Length >= 4 && header[0] == 0x43 && header[1] == 0x50 && header[2] == 0x54 && header[3] == 0x00)
      return ImageFormat.PhotoPaint;

    // PDN: "PDN3" (50 44 4E 33)
    if (header.Length >= 4 && header[0] == 0x50 && header[1] == 0x44 && header[2] == 0x4E && header[3] == 0x33)
      return ImageFormat.Pdn;

    // FPX: "FPX\0" (46 50 58 00)
    if (header.Length >= 4 && header[0] == 0x46 && header[1] == 0x50 && header[2] == 0x58 && header[3] == 0x00)
      return ImageFormat.Fpx;

    // DjVu: "AT&T" (41 54 26 54)
    if (header.Length >= 4 && header[0] == 0x41 && header[1] == 0x54 && header[2] == 0x26 && header[3] == 0x54)
      return ImageFormat.DjVu;

    // JBIG2: 97 4A 42 32
    if (header.Length >= 4 && header[0] == 0x97 && header[1] == 0x4A && header[2] == 0x42 && header[3] == 0x32)
      return ImageFormat.Jbig2;

    // FLIF: "FLIF" (46 4C 49 46)
    if (header.Length >= 4 && header[0] == 0x46 && header[1] == 0x4C && header[2] == 0x49 && header[3] == 0x46)
      return ImageFormat.Flif;

    // EPS: C5 D0 D3 C6 (DOS EPS binary header)
    if (header.Length >= 4 && header[0] == 0xC5 && header[1] == 0xD0 && header[2] == 0xD3 && header[3] == 0xC6)
      return ImageFormat.Eps;

    // WMF: D7 CD C6 9A (Placeable WMF)
    if (header.Length >= 4 && header[0] == 0xD7 && header[1] == 0xCD && header[2] == 0xC6 && header[3] == 0x9A)
      return ImageFormat.Wmf;

    // EMF: first record type = 1 (EMR_HEADER) + " EMF" at offset 40
    if (header.Length >= 44 && header[0] == 0x01 && header[1] == 0x00 && header[2] == 0x00 && header[3] == 0x00
        && header[40] == 0x20 && header[41] == 0x45 && header[42] == 0x4D && header[43] == 0x46)
      return ImageFormat.Emf;

    // QuakeSpr: "IDSP" (49 44 53 50)
    if (header.Length >= 4 && header[0] == 0x49 && header[1] == 0x44 && header[2] == 0x53 && header[3] == 0x50)
      return ImageFormat.QuakeSpr;

    // Analyze 7.5: sizeof_hdr = 348 (5C 01 00 00 LE)
    if (header.Length >= 44 && header[0] == 0x5C && header[1] == 0x01 && header[2] == 0x00 && header[3] == 0x00) {
      // Validate dim[0] at offset 40 is 1-7 (number of dimensions)
      var dim0 = (short)(header[40] | (header[41] << 8));
      if (dim0 >= 1 && dim0 <= 7)
        return ImageFormat.Analyze;
    }

    // PC Paint/Pictor: 34 12 (LE magic 0x1234) + header validation
    if (header.Length >= 12 && header[0] == 0x34 && header[1] == 0x12) {
      var w = (ushort)(header[2] | (header[3] << 8));
      var h = (ushort)(header[4] | (header[5] << 8));
      var planes = header[10];
      var bpp = header[11];
      if (w > 0 && h > 0 && planes >= 1 && planes <= 4 && bpp is 1 or 2 or 4 or 8)
        return ImageFormat.PcPaint;
    }

    // Autodesk CEL: 19 91 (LE magic 0x9119) + header validation
    if (header.Length >= 12 && header[0] == 0x19 && header[1] == 0x91) {
      var w = (ushort)(header[2] | (header[3] << 8));
      var h = (ushort)(header[4] | (header[5] << 8));
      var bpp = (ushort)(header[10] | (header[11] << 8));
      if (w > 0 && h > 0 && bpp == 8)
        return ImageFormat.AutodeskCel;
    }

    // PCX: 0A + version 0-5 (must be after other checks to avoid false positives)
    if (header.Length >= 2 && header[0] == 0x0A && header[1] <= 5)
      return ImageFormat.Pcx;

    return ImageFormat.Unknown;
  }

  /// <summary>Detects image format from file extension.</summary>
  public static ImageFormat DetectFromExtension(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    var ext = file.Extension.ToLowerInvariant();

    // Formats not in FormatRegistry (no IImageFileFormat implementation) or needing override
    var result = ext switch {
      ".gif" => ImageFormat.Gif,
      ".ani" => ImageFormat.Ani,
      ".webp" => ImageFormat.WebP,
      ".sct" => ImageFormat.ScitexCt,
      ".rle" => ImageFormat.UtahRle,
      ".wad" => ImageFormat.Wad,
      ".gun" => ImageFormat.GunPaint,
      // DNG and CameraRaw are TIFF-based (same magic as TIFF), need extension detection
      ".dng" => ImageFormat.Dng,
      ".cr2" or ".nef" or ".arw" or ".orf" or ".rw2" or ".pef" or ".raf" or ".srw" or ".dcs" => ImageFormat.CameraRaw,
      // Wave 9: extension-only or override formats
      ".kra" => ImageFormat.Krita,
      ".mha" or ".mhd" => ImageFormat.MetaImage,
      ".v" or ".vips" => ImageFormat.Vips,
      ".chr" => ImageFormat.NesChr,
      ".2bpp" or ".cgb" => ImageFormat.GameBoyTile,
      ".gr7" or ".gr8" or ".gr9" or ".gr15" or ".hip" or ".mic" or ".int" => ImageFormat.Atari8Bit,
      ".anim" => ImageFormat.IffAnim,
      // Wave 10: extension-only or override formats
      ".maya" => ImageFormat.MayaIff,
      // Wave 11: extension-only or override formats
      ".sfc" or ".snes" => ImageFormat.SnesTile,
      ".gen" or ".sgd" => ImageFormat.SegaGenTile,
      ".pce" => ImageFormat.PcEngineTile,
      ".sms" or ".gg" => ImageFormat.MasterSystemTile,
      ".mbm" => ImageFormat.SymbianMbm,
      ".xv" => ImageFormat.XvThumbnail,
      ".rgbn" => ImageFormat.IffRgbn,
      ".mrc" => ImageFormat.Mrc,
      ".gd2" => ImageFormat.Gd2,
      ".btf" or ".tf8" => ImageFormat.BigTiff,
      ".xcur" or ".cursor" => ImageFormat.Xcursor,
      ".hv" => ImageFormat.Interfile,
      ".ftc" => ImageFormat.AtariFalcon,
      ".hr" => ImageFormat.Trs80,
      _ => FormatRegistry.DetectFromExtension(ext),
    };

    return result;
  }
}
