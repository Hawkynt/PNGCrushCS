using System;
using FileFormat.Core;

namespace FileFormat.Bsave;

/// <summary>In-memory representation of a BSAVE (IBM PC BSAVE Graphics) screen dump.</summary>
[FormatMagicBytes([0xFD])]
public readonly record struct BsaveFile : IImageFormatReader<BsaveFile>, IImageToRawImage<BsaveFile>, IImageFromRawImage<BsaveFile>, IImageFormatWriter<BsaveFile> {

  static string IImageFormatMetadata<BsaveFile>.PrimaryExtension => ".bsv";
  static string[] IImageFormatMetadata<BsaveFile>.FileExtensions => [".bsv"];
  static BsaveFile IImageFormatReader<BsaveFile>.FromSpan(ReadOnlySpan<byte> data) => BsaveReader.FromSpan(data);
  // Video modes the BSAVE format historically captured. Listed in approximate order of frequency.
  //   SCREEN 1   → CGA 320x200 4-colour (6 palette variants — Palette 0/1 × low/high, plus "Mode 5")
  //   SCREEN 1c  → SCREEN 1 viewed via NTSC composite (display filter only; on-disk bytes match SCREEN 1)
  //   SCREEN 2   → CGA 640x200 monochrome
  //   SCREEN 6c  → SCREEN 2 viewed via NTSC composite — produces apparent colour bleed from 1bpp data
  //   SCREEN 9   → EGA 640x350 16-colour
  //   SCREEN 13  → VGA Mode 13h 320x200 256-colour
  //   160x100x16 → unofficial CGA tweak mode (40-col text mode with 2-line chars + half-block character)
  // 320x200 appears in two modes (CGA-4 and VGA-256) — disambiguated by palette size at save time.
  // "Composite" variants share the on-disk byte layout of their parent mode; only the display filter differs.
  static VideoMode[] IImageFormatMetadata<BsaveFile>.VideoModes => [
    new("CGA SCREEN 1 (320x200, 4 colours)", [(320, 200)], [4], _CgaScreen1Palettes),
    new("CGA SCREEN 1 composite (NTSC artefact display)", [(320, 200)], [4], _CgaScreen1Palettes,
        displayFilter: DisplayFilter.NtscComposite,
        description: "Same bytes as SCREEN 1 but rendered through an NTSC composite filter — gives the colour-bleed look classic games like King's Quest were authored against."),
    new("CGA SCREEN 2 (640x200, monochrome)", [(640, 200)], [2], _CgaMonoPalettes),
    new("CGA SCREEN 6 composite (640x200, NTSC artefact display)", [(640, 200)], [2], _CgaMonoPalettes,
        displayFilter: DisplayFilter.NtscComposite,
        description: "1bpp data viewed through NTSC composite — dot patterns demodulate to a 16-colour-ish artefact palette on real hardware."),
    new("EGA SCREEN 9 (640x350, 16 colours)", [(640, 350)], [16], _EgaPaletteEntry),
    new("VGA SCREEN 13 (320x200, 256 colours)", [(320, 200)], [256]),
    new("CGA 160x100x16 (text-mode tweak)", [(160, 100)], [16], _CgaRgbiPaletteEntry,
        description: "Unofficial mode produced by reprogramming the CGA into 40-column text mode with 2-line characters; each cell shows two stacked colours from the 16-colour RGBI palette."),
    new("CGA 80x100x1024 (Reenigne composite tweak)", [(80, 100)], [1024], _Cga1024PaletteEntry,
        displayFilter: DisplayFilter.NtscComposite,
        description: "Trixter/Reenigne's CGA 1024-colour mode (int10h.org/blog/2015/04). 80-column text mode with CRTC re-programmed for 2-scanline characters using glyphs 0x55, 0x13, 0xB0, 0xB1 — 4 patterns × 16 fg × 16 bg = 1024 distinct cells. On real composite hardware each cell phase-shifts to a unique colour; the synthesised palette here mixes the RGBI fg/bg by the pattern's dot density and is approximate rather than NTSC-exact (no published LUT exists)."),
  ];

  // ----- CGA palette variants (4 entries each) for SCREEN 1 mode -----

  private static readonly FixedPalette[] _CgaScreen1Palettes = [
    new FixedPalette("Palette 1 high intensity", 0x000000, 0x55FFFF, 0xFF55FF, 0xFFFFFF),
    new FixedPalette("Palette 1 low intensity",  0x000000, 0x00AAAA, 0xAA00AA, 0xAAAAAA),
    new FixedPalette("Palette 0 high intensity", 0x000000, 0x55FF55, 0xFF5555, 0xFFFF55),
    new FixedPalette("Palette 0 low intensity",  0x000000, 0x00AA00, 0xAA0000, 0xAA5500),
    new FixedPalette("Mode 5 high intensity",    0x000000, 0x55FFFF, 0xFF5555, 0xFFFFFF),
    new FixedPalette("Mode 5 low intensity",     0x000000, 0x00AAAA, 0xAA0000, 0xAAAAAA),
  ];

  // ----- CGA monochrome SCREEN 2 — single palette variant; entry exists so the dialog still
  //       shows the chosen foreground colour rather than hardcoding white.
  private static readonly FixedPalette[] _CgaMonoPalettes = [
    new FixedPalette("Black + White",       0x000000, 0xFFFFFF),
    new FixedPalette("Black + Light Cyan",  0x000000, 0x55FFFF),
    new FixedPalette("Black + Light Green", 0x000000, 0x55FF55),
    new FixedPalette("Black + Amber",       0x000000, 0xFFAA00),
  ];

  // ----- Standard EGA 16-colour RGBI palette (also used as default for 160x100x16) -----
  private static readonly FixedPalette[] _EgaPaletteEntry = [
    new FixedPalette("EGA standard 16",
      0x000000, 0x0000AA, 0x00AA00, 0x00AAAA, 0xAA0000, 0xAA00AA, 0xAA5500, 0xAAAAAA,
      0x555555, 0x5555FF, 0x55FF55, 0x55FFFF, 0xFF5555, 0xFF55FF, 0xFFFF55, 0xFFFFFF),
  ];

  private static readonly FixedPalette[] _CgaRgbiPaletteEntry = [
    new FixedPalette("CGA RGBI 16",
      0x000000, 0x0000AA, 0x00AA00, 0x00AAAA, 0xAA0000, 0xAA00AA, 0xAA5500, 0xAAAAAA,
      0x555555, 0x5555FF, 0x55FF55, 0x55FFFF, 0xFF5555, 0xFF55FF, 0xFFFF55, 0xFFFFFF),
  ];

  // ----- Synthesised 1024-entry palette for the CGA 80x100 Reenigne mode.
  //       Layout: index = pattern * 256 + fg * 16 + bg, where:
  //         pattern 0 = glyph 0x55 (50% density, phase A)
  //         pattern 1 = glyph 0x13 (≈37% density, phase B)
  //         pattern 2 = glyph 0xB0 (≈37% density, phase C)
  //         pattern 3 = glyph 0xB1 (≈50% density, phase D)
  //       Approximated by blending RGBI fg/bg at the pattern's dot ratio. The 4 patterns produce
  //       perceptually distinct phase shifts on real composite hardware; here they only vary by
  //       blend weight so the synthesised palette has visible — but not NTSC-exact — variation.
  private static readonly FixedPalette[] _Cga1024PaletteEntry = [
    new FixedPalette("CGA Reenigne 1024 (synthesised)", _Build1024PaletteHex()),
  ];

  internal static readonly byte[] _CgaCharGlyphs1024 = [0x55, 0x13, 0xB0, 0xB1];

  private static uint[] _Build1024PaletteHex() {
    // Approximate fg-density weights (out of 1.0) for the four dot-pattern glyphs (0x55, 0x13, 0xB0, 0xB1).
    // Inlined rather than referencing a static field — this method is called during the static initialiser
    // chain for _Cga1024PaletteEntry, so any sibling static referenced here may still be null.
    double[] patternFgWeight = [0.50, 0.40, 0.60, 0.50];
    // Source 16-colour RGBI palette (matches the RGBI table above).
    uint[] rgbi = [
      0x000000, 0x0000AA, 0x00AA00, 0x00AAAA, 0xAA0000, 0xAA00AA, 0xAA5500, 0xAAAAAA,
      0x555555, 0x5555FF, 0x55FF55, 0x55FFFF, 0xFF5555, 0xFF55FF, 0xFFFF55, 0xFFFFFF,
    ];
    var hex = new uint[1024];
    for (var pattern = 0; pattern < 4; ++pattern) {
      var w = patternFgWeight[pattern];
      for (var fg = 0; fg < 16; ++fg) {
        var fr = (rgbi[fg] >> 16) & 0xFF;
        var fgGrn = (rgbi[fg] >> 8) & 0xFF;
        var fb = rgbi[fg] & 0xFF;
        for (var bg = 0; bg < 16; ++bg) {
          var br = (rgbi[bg] >> 16) & 0xFF;
          var bgGrn = (rgbi[bg] >> 8) & 0xFF;
          var bb = rgbi[bg] & 0xFF;
          var r = (uint)System.Math.Round(fr * w + br * (1 - w));
          var g = (uint)System.Math.Round(fgGrn * w + bgGrn * (1 - w));
          var b = (uint)System.Math.Round(fb * w + bb * (1 - w));
          hex[pattern * 256 + fg * 16 + bg] = (r << 16) | (g << 8) | b;
        }
      }
    }
    return hex;
  }
  static byte[] IImageFormatWriter<BsaveFile>.ToBytes(BsaveFile file) => BsaveWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public BsaveMode Mode { get; init; }

  /// <summary>Raw screen memory bytes.</summary>
  public byte[] PixelData { get; init; }

  // CGA palette 1 high-intensity: black, cyan, magenta, white
  private static readonly byte[] _CgaPalette = [
    0x00, 0x00, 0x00,
    0x55, 0xFF, 0xFF,
    0xFF, 0x55, 0xFF,
    0xFF, 0xFF, 0xFF,
  ];

  // Standard EGA 16-color palette
  private static readonly byte[] _EgaPalette = [
    0x00, 0x00, 0x00, 0x00, 0x00, 0xAA, 0x00, 0xAA, 0x00, 0x00, 0xAA, 0xAA,
    0xAA, 0x00, 0x00, 0xAA, 0x00, 0xAA, 0xAA, 0x55, 0x00, 0xAA, 0xAA, 0xAA,
    0x55, 0x55, 0x55, 0x55, 0x55, 0xFF, 0x55, 0xFF, 0x55, 0x55, 0xFF, 0xFF,
    0xFF, 0x55, 0x55, 0xFF, 0x55, 0xFF, 0xFF, 0xFF, 0x55, 0xFF, 0xFF, 0xFF,
  ];

  /// <summary>Converts this BSAVE screen dump to a platform-independent <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(BsaveFile file) {

    return file.Mode switch {
      BsaveMode.Cga320x200x4 => _Cga4ToRawImage(file),
      BsaveMode.Cga640x200x2 => _Cga2ToRawImage(file),
      BsaveMode.Ega640x350x16 => _Ega16ToRawImage(file),
      BsaveMode.Vga320x200x256 => _Vga256ToRawImage(file),
      BsaveMode.Cga160x100x16 => _Cga160x100ToRawImage(file),
      BsaveMode.Cga80x100x1024 => _Cga80x100x1024ToRawImage(file),
      _ => throw new ArgumentOutOfRangeException(nameof(file), file.Mode, "Unknown BSAVE mode.")
    };
  }

  /// <summary>Creates a BSAVE screen dump from a <see cref="RawImage"/>. Dispatches by
  /// (Width × Height × palette count) to the matching mode encoder. Input must be indexed (1/4/8 bpp)
  /// with a palette small enough for the target mode.</summary>
  public static BsaveFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var paletteCount = image.PaletteCount;
    // Disambiguate 320×200 modes by palette size: 4 → CGA SCREEN 1, ≥256 → VGA SCREEN 13.
    return (image.Width, image.Height) switch {
      (320, 200) when paletteCount > 4 => _ToVga256(image),
      (320, 200) => _ToCga4(image),
      (640, 200) => _ToCga2(image),
      (640, 350) => _ToEga16(image),
      (160, 100) => _ToCga160x100(image),
      (80, 100) => _ToCga80x100x1024(image),
      _ => throw new ArgumentException(
        $"BSAVE supports 320x200 (CGA/VGA), 640x200 (CGA mono), 640x350 (EGA), 160x100 (CGA tweak), or 80x100 (Reenigne 1024). Got {image.Width}x{image.Height}.",
        nameof(image))
    };
  }

  private static BsaveFile _ToVga256(RawImage image) {
    const int width = 320;
    const int height = 200;
    var src = _RequireIndexed(image, maxPalette: 256, modeName: "VGA SCREEN 13");
    var pixels = new byte[width * height];
    src.CopyTo(pixels, 0); // already byte-per-pixel linear
    return new() {
      Width = width, Height = height, Mode = BsaveMode.Vga320x200x256, PixelData = pixels,
    };
  }

  private static BsaveFile _ToCga4(RawImage image) {
    const int width = 320;
    const int height = 200;
    const int bytesPerLine = 80; // 320 / 4 pixels per byte
    var src = _RequireIndexed(image, maxPalette: 4, modeName: "CGA SCREEN 1");
    var pixels = new byte[0x4000]; // 16 KiB CGA frame: two 8000-byte banks
    for (var y = 0; y < height; ++y) {
      var bankOffset = (y & 1) == 0 ? 0 : 0x2000;
      var lineOffset = bankOffset + (y >> 1) * bytesPerLine;
      for (var byteCol = 0; byteCol < bytesPerLine; ++byteCol) {
        byte b = 0;
        for (var px = 0; px < 4; ++px) {
          var x = byteCol * 4 + px;
          var idx = src[y * width + x] & 3;
          b |= (byte)(idx << (6 - px * 2));
        }
        pixels[lineOffset + byteCol] = b;
      }
    }
    return new() {
      Width = width, Height = height, Mode = BsaveMode.Cga320x200x4, PixelData = pixels,
    };
  }

  private static BsaveFile _ToCga2(RawImage image) {
    const int width = 640;
    const int height = 200;
    const int bytesPerLine = 80; // 640 / 8 pixels per byte
    var src = _RequireIndexed(image, maxPalette: 2, modeName: "CGA SCREEN 2");
    var pixels = new byte[0x4000];
    for (var y = 0; y < height; ++y) {
      var bankOffset = (y & 1) == 0 ? 0 : 0x2000;
      var lineOffset = bankOffset + (y >> 1) * bytesPerLine;
      for (var byteCol = 0; byteCol < bytesPerLine; ++byteCol) {
        byte b = 0;
        for (var bit = 0; bit < 8; ++bit) {
          var x = byteCol * 8 + bit;
          var on = (src[y * width + x] & 1) != 0;
          if (on) b |= (byte)(1 << (7 - bit));
        }
        pixels[lineOffset + byteCol] = b;
      }
    }
    return new() {
      Width = width, Height = height, Mode = BsaveMode.Cga640x200x2, PixelData = pixels,
    };
  }

  private static BsaveFile _ToEga16(RawImage image) {
    const int width = 640;
    const int height = 350;
    const int bytesPerLine = 80; // 640 / 8 pixels per byte
    const int planeSize = bytesPerLine * height; // 28000 per plane
    var src = _RequireIndexed(image, maxPalette: 16, modeName: "EGA SCREEN 9");
    var pixels = new byte[planeSize * 4];
    for (var y = 0; y < height; ++y)
      for (var byteCol = 0; byteCol < bytesPerLine; ++byteCol) {
        byte p0 = 0, p1 = 0, p2 = 0, p3 = 0;
        for (var bit = 0; bit < 8; ++bit) {
          var x = byteCol * 8 + bit;
          var idx = src[y * width + x];
          var shift = 7 - bit;
          if ((idx & 1) != 0) p0 |= (byte)(1 << shift);
          if ((idx & 2) != 0) p1 |= (byte)(1 << shift);
          if ((idx & 4) != 0) p2 |= (byte)(1 << shift);
          if ((idx & 8) != 0) p3 |= (byte)(1 << shift);
        }
        var lineByteOffset = y * bytesPerLine + byteCol;
        pixels[lineByteOffset] = p0;
        pixels[lineByteOffset + planeSize] = p1;
        pixels[lineByteOffset + planeSize * 2] = p2;
        pixels[lineByteOffset + planeSize * 3] = p3;
      }
    return new() {
      Width = width, Height = height, Mode = BsaveMode.Ega640x350x16, PixelData = pixels,
    };
  }

  // 160x100x16: nibble-packed 4bpp linear data, 8000 bytes (160 * 100 / 2). High nibble = even col, low nibble = odd col.
  private static BsaveFile _ToCga160x100(RawImage image) {
    const int width = 160;
    const int height = 100;
    var src = _RequireIndexed(image, maxPalette: 16, modeName: "CGA 160x100x16");
    var pixels = new byte[width * height / 2]; // 8000 bytes
    for (var y = 0; y < height; ++y)
      for (var col = 0; col < width / 2; ++col) {
        var hi = src[y * width + col * 2] & 0x0F;
        var lo = src[y * width + col * 2 + 1] & 0x0F;
        pixels[y * (width / 2) + col] = (byte)((hi << 4) | lo);
      }
    return new() {
      Width = width, Height = height, Mode = BsaveMode.Cga160x100x16, PixelData = pixels,
    };
  }

  // 160x100x16 decoder: unpacks 8000 bytes of nibble-packed indices into 160x100 Indexed8.
  private static RawImage _Cga160x100ToRawImage(BsaveFile file) {
    const int width = 160;
    const int height = 100;
    var pixels = new byte[width * height];
    var src = file.PixelData;
    for (var y = 0; y < height; ++y)
      for (var col = 0; col < width / 2; ++col) {
        var srcOffset = y * (width / 2) + col;
        if (srcOffset >= src.Length) continue;
        var b = src[srcOffset];
        pixels[y * width + col * 2] = (byte)((b >> 4) & 0x0F);
        pixels[y * width + col * 2 + 1] = (byte)(b & 0x0F);
      }
    // Build RGB palette from the EGA RGBI 16-entry table.
    var palette = new byte[16 * 3];
    Buffer.BlockCopy(_EgaPalette, 0, palette, 0, 16 * 3);
    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = palette,
      PaletteCount = 16,
    };
  }

  // 80x100x1024 (Reenigne mode): each pixel → text-mode (char, attr) cell.
  //   pixel index = pattern * 256 + fg * 16 + bg  (decomposed from the synthesised 1024 palette)
  //   char_byte   = _CgaCharGlyphs1024[pattern]   (one of {0x55, 0x13, 0xB0, 0xB1})
  //   attr_byte   = (bg << 4) | fg                 (CGA text-mode attribute layout)
  // 80 cols × 100 rows = 8000 cells × 2 bytes = 16000 bytes on disk.
  private static BsaveFile _ToCga80x100x1024(RawImage image) {
    const int width = 80;
    const int height = 100;
    // Reduced rather than refused: asking the caller to hand over an already-indexed picture
    // makes converting into this format someone else's problem, which is the one thing a
    // converter cannot delegate.
    image = image.EnsureIndexedAtMost(16);
      throw new ArgumentException(
        $"BSAVE 80x100x1024 supports at most 1024 palette entries, got {image.PaletteCount}.", nameof(image));

    // Read indices natively at their declared bit-depth so Indexed16 inputs preserve the high pattern
    // bits. For Indexed8 inputs (≤256 colours) only pattern=0 is reachable; for Indexed16 inputs the
    // full 10-bit (pattern × 256 + fg × 16 + bg) space round-trips losslessly.
    var indices = _RequireIndices16(image, maxPalette: 1024, modeName: "80x100x1024");
    var pixels = new byte[width * height * 2]; // 16000 bytes
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var idx = indices[y * width + x];
        var pattern = (idx >> 8) & 3;
        var fg = (idx >> 4) & 0x0F;
        var bg = idx & 0x0F;
        var cellOffset = (y * width + x) * 2;
        pixels[cellOffset] = _CgaCharGlyphs1024[pattern];
        pixels[cellOffset + 1] = (byte)((bg << 4) | fg);
      }
    return new() {
      Width = width, Height = height, Mode = BsaveMode.Cga80x100x1024, PixelData = pixels,
    };
  }

  /// <summary>Returns a flat array of pixel-per-element 16-bit indices regardless of whether the source
  /// image is Indexed1/4/8 (byte-per-pixel) or Indexed16 (two-byte-per-pixel little-endian).</summary>
  private static int[] _RequireIndices16(RawImage image, int maxPalette, string modeName) {
    // Reduced rather than refused: asking the caller to hand over an already-indexed picture
    // makes converting into this format someone else's problem, which is the one thing a
    // converter cannot delegate.
    image = image.EnsureIndexedAtMost(16);
      throw new ArgumentException(
        $"BSAVE {modeName} supports at most {maxPalette} palette entries, got {image.PaletteCount}.", nameof(image));

    var src = image.PixelData ?? [];
    var w = image.Width;
    var h = image.Height;
    var result = new int[w * h];

    if (image.Format == PixelFormat.Indexed16) {
      for (var i = 0; i < result.Length; ++i) {
        var p = i * 2;
        if (p + 1 >= src.Length) break;
        result[i] = src[p] | (src[p + 1] << 8);
      }
      return result;
    }

    var byteIdx = image.Format switch {
      PixelFormat.Indexed8 => src,
      PixelFormat.Indexed4 => _UnpackIndexed4(src, w, h),
      PixelFormat.Indexed1 => _UnpackIndexed1(src, w, h),
      _ => throw new ArgumentException($"Unexpected indexed format {image.Format}.", nameof(image)),
    };
    for (var i = 0; i < result.Length && i < byteIdx.Length; ++i)
      result[i] = byteIdx[i];
    return result;
  }

  // Decoder: 16000-byte text-mode buffer → 80x100 Indexed16 with the synthesised 1024 palette.
  // Indexed16 stores each index as 2 bytes little-endian so the full 10-bit (pattern × 256 + fg × 16 + bg)
  // value survives the round-trip — Indexed8 would mask off the pattern bits.
  private static RawImage _Cga80x100x1024ToRawImage(BsaveFile file) {
    const int width = 80;
    const int height = 100;
    var src = file.PixelData;
    var pixels = new byte[width * height * 2];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var cellOffset = (y * width + x) * 2;
        if (cellOffset + 1 >= src.Length) continue;
        var ch = src[cellOffset];
        var attr = src[cellOffset + 1];
        var fg = attr & 0x0F;
        var bg = (attr >> 4) & 0x0F;
        var pattern = System.Array.IndexOf(_CgaCharGlyphs1024, ch);
        if (pattern < 0) pattern = 0;
        var fullIdx = pattern * 256 + fg * 16 + bg;
        var dst = (y * width + x) * 2;
        pixels[dst]     = (byte)(fullIdx & 0xFF);
        pixels[dst + 1] = (byte)((fullIdx >> 8) & 0xFF);
      }
    var palette = new byte[1024 * 3];
    var palettePacked = _Build1024PaletteHex();
    for (var i = 0; i < 1024; ++i) {
      var c = palettePacked[i];
      palette[i * 3] = (byte)((c >> 16) & 0xFF);
      palette[i * 3 + 1] = (byte)((c >> 8) & 0xFF);
      palette[i * 3 + 2] = (byte)(c & 0xFF);
    }
    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Indexed16,
      PixelData = pixels,
      Palette = palette,
      PaletteCount = 1024,
    };
  }

  /// <summary>Like <see cref="_RequireIndexed"/> but allows up to 1024 palette entries — needed for the Reenigne mode.
  /// Source must already be byte-per-pixel (the dialog produces Indexed8 even when palette is large).</summary>
  private static byte[] _RequireIndexedLarge(RawImage image, int maxPalette, string modeName) {
    // Reduced rather than refused: asking the caller to hand over an already-indexed picture
    // makes converting into this format someone else's problem, which is the one thing a
    // converter cannot delegate.
    image = image.EnsureIndexedAtMost(16);
      throw new ArgumentException(
        $"BSAVE {modeName} supports at most {maxPalette} palette entries, got {image.PaletteCount}.", nameof(image));

    var src = image.PixelData ?? [];
    return image.Format switch {
      PixelFormat.Indexed8 => src,
      PixelFormat.Indexed4 => _UnpackIndexed4(src, image.Width, image.Height),
      PixelFormat.Indexed1 => _UnpackIndexed1(src, image.Width, image.Height),
      _ => throw new ArgumentException($"Unexpected indexed format {image.Format}.", nameof(image)),
    };
  }

  /// <summary>Converts the source to a flat Indexed8 byte array, validating that the palette fits in the target mode.
  /// Unpacks sub-byte indexed formats (Indexed1/Indexed4) inline rather than going through
  /// <see cref="PixelConverter"/> — that path can dispatch back through registered format writers and recurse.</summary>
  private static byte[] _RequireIndexed(RawImage image, int maxPalette, string modeName) {
    // Reduced rather than refused: asking the caller to hand over an already-indexed picture
    // makes converting into this format someone else's problem, which is the one thing a
    // converter cannot delegate.
    image = image.EnsureIndexedAtMost(16);
    if (image.PaletteCount > maxPalette)
      throw new ArgumentException(
        $"BSAVE {modeName} supports at most {maxPalette} palette entries, got {image.PaletteCount}.",
        nameof(image));

    var src = image.PixelData ?? [];
    var w = image.Width;
    var h = image.Height;
    return image.Format switch {
      PixelFormat.Indexed8 => src,
      PixelFormat.Indexed4 => _UnpackIndexed4(src, w, h),
      PixelFormat.Indexed1 => _UnpackIndexed1(src, w, h),
      _ => throw new ArgumentException($"Unexpected indexed format {image.Format}.", nameof(image)),
    };
  }

  private static byte[] _UnpackIndexed4(byte[] src, int width, int height) {
    var stride = (width + 1) / 2;
    var dst = new byte[width * height];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var b = src[y * stride + (x >> 1)];
        dst[y * width + x] = (byte)(((x & 1) == 0 ? b >> 4 : b) & 0x0F);
      }
    return dst;
  }

  private static byte[] _UnpackIndexed1(byte[] src, int width, int height) {
    var stride = (width + 7) / 8;
    var dst = new byte[width * height];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var b = src[y * stride + (x >> 3)];
        dst[y * width + x] = (byte)((b >> (7 - (x & 7))) & 1);
      }
    return dst;
  }

  // CGA 320x200x4: 2bpp, interleaved banks, map to CGA palette -> Rgb24
  private static RawImage _Cga4ToRawImage(BsaveFile file) {
    const int width = 320;
    const int height = 200;
    const int bytesPerLine = 80; // 320 / 4
    var pixels = new byte[width * height * 3];

    for (var y = 0; y < height; ++y) {
      // Even lines at bank 0 (offset 0x0000), odd lines at bank 1 (offset 0x2000)
      var bankOffset = (y & 1) == 0 ? 0 : 0x2000;
      var lineOffset = bankOffset + (y >> 1) * bytesPerLine;

      for (var byteCol = 0; byteCol < bytesPerLine; ++byteCol) {
        var srcOffset = lineOffset + byteCol;
        if (srcOffset >= file.PixelData.Length)
          continue;

        var b = file.PixelData[srcOffset];
        for (var px = 0; px < 4; ++px) {
          var colorIndex = (b >> (6 - px * 2)) & 3;
          var x = byteCol * 4 + px;
          var dstOffset = (y * width + x) * 3;
          pixels[dstOffset] = _CgaPalette[colorIndex * 3];
          pixels[dstOffset + 1] = _CgaPalette[colorIndex * 3 + 1];
          pixels[dstOffset + 2] = _CgaPalette[colorIndex * 3 + 2];
        }
      }
    }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    };
  }

  // CGA 640x200x2: 1bpp, interleaved banks -> Indexed8
  private static RawImage _Cga2ToRawImage(BsaveFile file) {
    const int width = 640;
    const int height = 200;
    const int bytesPerLine = 80; // 640 / 8
    var pixels = new byte[width * height];

    for (var y = 0; y < height; ++y) {
      var bankOffset = (y & 1) == 0 ? 0 : 0x2000;
      var lineOffset = bankOffset + (y >> 1) * bytesPerLine;

      for (var byteCol = 0; byteCol < bytesPerLine; ++byteCol) {
        var srcOffset = lineOffset + byteCol;
        if (srcOffset >= file.PixelData.Length)
          continue;

        var b = file.PixelData[srcOffset];
        var baseX = byteCol * 8;
        for (var bit = 0; bit < 8; ++bit) {
          var x = baseX + bit;
          if (x < width)
            pixels[y * width + x] = (byte)((b >> (7 - bit)) & 1);
        }
      }
    }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = [0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF],
      PaletteCount = 2,
    };
  }

  // EGA 640x350x16: 4 sequential planes, map to EGA palette -> Rgb24
  private static RawImage _Ega16ToRawImage(BsaveFile file) {
    const int width = 640;
    const int height = 350;
    const int bytesPerLine = 80; // 640 / 8
    const int planeSize = bytesPerLine * height; // 28000
    var pixels = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
      for (var byteCol = 0; byteCol < bytesPerLine; ++byteCol) {
        var lineByteOffset = y * bytesPerLine + byteCol;

        // Read the corresponding byte from each of the 4 planes
        byte plane0 = lineByteOffset < file.PixelData.Length ? file.PixelData[lineByteOffset] : (byte)0;
        byte plane1 = lineByteOffset + planeSize < file.PixelData.Length ? file.PixelData[lineByteOffset + planeSize] : (byte)0;
        byte plane2 = lineByteOffset + planeSize * 2 < file.PixelData.Length ? file.PixelData[lineByteOffset + planeSize * 2] : (byte)0;
        byte plane3 = lineByteOffset + planeSize * 3 < file.PixelData.Length ? file.PixelData[lineByteOffset + planeSize * 3] : (byte)0;

        for (var bit = 0; bit < 8; ++bit) {
          var x = byteCol * 8 + bit;
          if (x >= width)
            continue;

          var shift = 7 - bit;
          var colorIndex =
            ((plane0 >> shift) & 1)
            | (((plane1 >> shift) & 1) << 1)
            | (((plane2 >> shift) & 1) << 2)
            | (((plane3 >> shift) & 1) << 3);

          var dstOffset = (y * width + x) * 3;
          pixels[dstOffset] = _EgaPalette[colorIndex * 3];
          pixels[dstOffset + 1] = _EgaPalette[colorIndex * 3 + 1];
          pixels[dstOffset + 2] = _EgaPalette[colorIndex * 3 + 2];
        }
      }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    };
  }

  // VGA 320x200x256: linear 8bpp -> Indexed8 with default VGA palette
  private static RawImage _Vga256ToRawImage(BsaveFile file) {
    const int width = 320;
    const int height = 200;
    const int totalPixels = width * height;
    var pixels = new byte[totalPixels];
    var copyLen = Math.Min(totalPixels, file.PixelData.Length);
    file.PixelData.AsSpan(0, copyLen).CopyTo(pixels);

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = _DefaultVgaPalette,
      PaletteCount = 256,
    };
  }

  // Default VGA Mode 13h palette (256 RGB triplets). Indices 0..15 = EGA palette,
  // 16..31 = grayscale ramp, 32..255 = standard VGA color cube + ramps.
  // This is a synthesized approximation suitable for indexed-image round-tripping —
  // the exact ROM palette is hardware-defined and not stored in BSAVE files.
  private static readonly byte[] _DefaultVgaPalette = _BuildDefaultVgaPalette();

  private static byte[] _BuildDefaultVgaPalette() {
    var palette = new byte[256 * 3];
    // 0..15: EGA palette
    for (var i = 0; i < 16; ++i) {
      palette[i * 3] = _EgaPalette[i * 3];
      palette[i * 3 + 1] = _EgaPalette[i * 3 + 1];
      palette[i * 3 + 2] = _EgaPalette[i * 3 + 2];
    }
    // 16..31: grayscale ramp
    for (var i = 0; i < 16; ++i) {
      var v = (byte)(i * 255 / 15);
      var idx = (16 + i) * 3;
      palette[idx] = v;
      palette[idx + 1] = v;
      palette[idx + 2] = v;
    }
    // 32..255: spread a 6x6x6 color cube + remaining grayscale
    var pos = 32;
    for (var r = 0; r < 6 && pos < 256; ++r)
      for (var g = 0; g < 6 && pos < 256; ++g)
        for (var b = 0; b < 6 && pos < 256; ++b) {
          var idx = pos * 3;
          palette[idx] = (byte)(r * 51);
          palette[idx + 1] = (byte)(g * 51);
          palette[idx + 2] = (byte)(b * 51);
          ++pos;
        }
    // Fill remainder with grayscale ramp
    while (pos < 256) {
      var v = (byte)((pos - 248) * 32);
      var idx = pos * 3;
      palette[idx] = v;
      palette[idx + 1] = v;
      palette[idx + 2] = v;
      ++pos;
    }
    return palette;
  }
}
