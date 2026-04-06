using System;
using FileFormat.Core;

namespace FileFormat.Bsave;

/// <summary>In-memory representation of a BSAVE (IBM PC BSAVE Graphics) screen dump.</summary>
[FormatMagicBytes([0xFD])]
public readonly record struct BsaveFile : IImageFormatReader<BsaveFile>, IImageToRawImage<BsaveFile>, IImageFromRawImage<BsaveFile>, IImageFormatWriter<BsaveFile> {

  static string IImageFormatMetadata<BsaveFile>.PrimaryExtension => ".bsv";
  static string[] IImageFormatMetadata<BsaveFile>.FileExtensions => [".bsv"];
  static BsaveFile IImageFormatReader<BsaveFile>.FromSpan(ReadOnlySpan<byte> data) => BsaveReader.FromSpan(data);
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
      _ => throw new ArgumentOutOfRangeException(nameof(file), file.Mode, "Unknown BSAVE mode.")
    };
  }

  /// <summary>Creates a VGA Mode 13h (320x200x256) BSAVE from a <see cref="RawImage"/>. Input must be Indexed8 with a 256-color palette.</summary>
  public static BsaveFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var bgra = PixelConverter.Convert(image, PixelFormat.Bgra32);
    var width = bgra.Width;
    var height = bgra.Height;
    var src = bgra.PixelData;

    // Convert BGRA to grayscale indices for VGA Mode 13h (320x200x256)
    const int vgaWidth = 320;
    const int vgaHeight = 200;
    var totalPixels = vgaWidth * vgaHeight;
    var pixels = new byte[totalPixels];

    for (var y = 0; y < vgaHeight; ++y)
      for (var x = 0; x < vgaWidth; ++x) {
        if (x < width && y < height) {
          var srcIdx = (y * width + x) * 4;
          var b = src[srcIdx];
          var g = src[srcIdx + 1];
          var r = src[srcIdx + 2];
          // Map to VGA grayscale index (0-255)
          pixels[y * vgaWidth + x] = (byte)((r * 77 + g * 150 + b * 29) >> 8);
        }
      }

    return new() {
      Width = vgaWidth,
      Height = vgaHeight,
      Mode = BsaveMode.Vga320x200x256,
      PixelData = pixels,
    };
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

  // VGA 320x200x256: linear 8bpp -> Indexed8, no palette (hardware-defined)
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
      PaletteCount = 256,
    };
  }
}
