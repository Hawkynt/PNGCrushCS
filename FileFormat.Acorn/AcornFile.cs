using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Acorn;

/// <summary>In-memory representation of an Acorn RISC OS sprite file.</summary>
public sealed class AcornFile : IImageFileFormat<AcornFile> {

  static string IImageFileFormat<AcornFile>.PrimaryExtension => ".spr";
  static string[] IImageFileFormat<AcornFile>.FileExtensions => [".spr"];
  static AcornFile IImageFileFormat<AcornFile>.FromFile(FileInfo file) => AcornReader.FromFile(file);
  static AcornFile IImageFileFormat<AcornFile>.FromBytes(byte[] data) => AcornReader.FromBytes(data);
  static AcornFile IImageFileFormat<AcornFile>.FromStream(Stream stream) => AcornReader.FromStream(stream);
  static byte[] IImageFileFormat<AcornFile>.ToBytes(AcornFile file) => AcornWriter.ToBytes(file);
  /// <summary>Sprites contained in this file.</summary>
  public IReadOnlyList<AcornSprite> Sprites { get; init; } = [];

  // Standard RISC OS 16-color desktop palette (indices 0-15)
  private static readonly byte[] _DefaultPalette4Bpp = [
    255, 255, 255, // 0: white
    221, 221, 221, // 1: light grey 1
    187, 187, 187, // 2: light grey 2
    153, 153, 153, // 3: mid grey 1
    119, 119, 119, // 4: mid grey 2
     85,  85,  85, // 5: dark grey 1
     51,  51,  51, // 6: dark grey 2
      0,   0,   0, // 7: black
      0,   0, 153, // 8: dark blue
    238, 238,   0, // 9: yellow
      0, 187,   0, // 10: green
    221,   0,   0, // 11: red
    238, 238, 187, // 12: cream
     85, 153,   0, // 13: dark green
    255, 187,   0, // 14: orange
      0, 187, 255 // 15: light blue
  ];

  /// <summary>Converts the first sprite in an <see cref="AcornFile"/> to a format-independent <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(AcornFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.Sprites.Count == 0)
      throw new InvalidOperationException("AcornFile contains no sprites.");

    var sprite = file.Sprites[0];
    var width = sprite.Width;
    var height = sprite.Height;
    var bpp = sprite.BitsPerPixel;

    return bpp switch {
      1 or 2 or 4 or 8 => _ConvertIndexedToRawImage(sprite, width, height, bpp),
      16 => _Convert16BppToRawImage(sprite, width, height),
      32 => _Convert32BppToRawImage(sprite, width, height),
      _ => throw new InvalidOperationException($"Unsupported bits per pixel: {bpp}.")
    };
  }

  private static RawImage _ConvertIndexedToRawImage(AcornSprite sprite, int width, int height, int bpp) {
    var pixelsPerByte = 8 / bpp;
    var rawBytesPerRow = (width + pixelsPerByte - 1) / pixelsPerByte;
    var paddedBytesPerRow = (rawBytesPerRow + 3) & ~3; // word-aligned
    var pixels = new byte[width * height];

    for (var y = 0; y < height; ++y) {
      var srcRow = y * paddedBytesPerRow;
      var dstRow = y * width;

      for (var x = 0; x < width; ++x) {
        int index;
        switch (bpp) {
          case 1: {
            var byteIndex = srcRow + x / 8;
            var bitIndex = 7 - (x & 7); // MSB first
            index = (sprite.PixelData[byteIndex] >> bitIndex) & 1;
            break;
          }
          case 2: {
            var byteIndex = srcRow + x / 4;
            var shift = (x & 3) * 2;
            index = (sprite.PixelData[byteIndex] >> shift) & 3;
            break;
          }
          case 4: {
            var byteIndex = srcRow + x / 2;
            index = (x & 1) == 0
              ? sprite.PixelData[byteIndex] & 0x0F        // low nibble first
              : (sprite.PixelData[byteIndex] >> 4) & 0x0F;
            break;
          }
          default: { // 8bpp
            index = sprite.PixelData[srcRow + x];
            break;
          }
        }

        pixels[dstRow + x] = (byte)index;
      }
    }

    var paletteCount = 1 << bpp;
    byte[] palette;

    if (sprite.Palette is { Length: >= 8 }) {
      // Extract RGB from palette: 2 words (8 bytes) per entry, first word layout: byte0=flags, byte1=R, byte2=G, byte3=B
      var entryCount = Math.Min(sprite.Palette.Length / 8, paletteCount);
      palette = new byte[paletteCount * 3];
      for (var i = 0; i < entryCount; ++i) {
        var off = i * 8;
        palette[i * 3] = sprite.Palette[off + 1];     // R
        palette[i * 3 + 1] = sprite.Palette[off + 2]; // G
        palette[i * 3 + 2] = sprite.Palette[off + 3]; // B
      }
    } else {
      palette = _BuildDefaultPalette(bpp, paletteCount);
    }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = palette,
      PaletteCount = paletteCount,
    };
  }

  private static byte[] _BuildDefaultPalette(int bpp, int paletteCount) {
    var palette = new byte[paletteCount * 3];
    switch (bpp) {
      case 1:
        // 0 = white, 1 = black
        palette[0] = 255; palette[1] = 255; palette[2] = 255;
        // index 1 stays 0,0,0
        break;
      case 2:
        // 0 = white, 1 = light grey, 2 = dark grey, 3 = black
        palette[0] = 255; palette[1] = 255; palette[2] = 255;
        palette[3] = 170; palette[4] = 170; palette[5] = 170;
        palette[6] = 85;  palette[7] = 85;  palette[8] = 85;
        // index 3 stays 0,0,0
        break;
      case 4:
        _DefaultPalette4Bpp.AsSpan(0, _DefaultPalette4Bpp.Length).CopyTo(palette);
        break;
      default:
        // 8bpp: grayscale ramp
        for (var i = 0; i < 256; ++i) {
          palette[i * 3] = (byte)i;
          palette[i * 3 + 1] = (byte)i;
          palette[i * 3 + 2] = (byte)i;
        }

        break;
    }

    return palette;
  }

  private static RawImage _Convert16BppToRawImage(AcornSprite sprite, int width, int height) {
    var paddedBytesPerRow = ((width * 2 + 3) & ~3);
    var pixels = new byte[width * height * 3];

    for (var y = 0; y < height; ++y) {
      var srcRow = y * paddedBytesPerRow;
      var dstRow = y * width * 3;
      for (var x = 0; x < width; ++x) {
        var off = srcRow + x * 2;
        var lo = sprite.PixelData[off];
        var hi = sprite.PixelData[off + 1];
        var val = lo | (hi << 8); // little-endian ushort
        var r5 = val & 0x1F;
        var g5 = (val >> 5) & 0x1F;
        var b5 = (val >> 10) & 0x1F;
        var dstOff = dstRow + x * 3;
        pixels[dstOff] = (byte)((r5 << 3) | (r5 >> 2));
        pixels[dstOff + 1] = (byte)((g5 << 3) | (g5 >> 2));
        pixels[dstOff + 2] = (byte)((b5 << 3) | (b5 >> 2));
      }
    }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    };
  }

  private static RawImage _Convert32BppToRawImage(AcornSprite sprite, int width, int height) {
    // 32bpp is already word-aligned (4 bytes/pixel)
    var bytesPerRow = width * 4;
    var pixels = new byte[width * height * 3];

    for (var y = 0; y < height; ++y) {
      var srcRow = y * bytesPerRow;
      var dstRow = y * width * 3;
      for (var x = 0; x < width; ++x) {
        var srcOff = srcRow + x * 4;
        var dstOff = dstRow + x * 3;
        // RISC OS 32bpp: byte0=B, byte1=G, byte2=R, byte3=pad
        pixels[dstOff] = sprite.PixelData[srcOff + 2]; // R
        pixels[dstOff + 1] = sprite.PixelData[srcOff + 1]; // G
        pixels[dstOff + 2] = sprite.PixelData[srcOff]; // B
      }
    }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    };
  }

  /// <summary>Creates an <see cref="AcornFile"/> from a format-independent <see cref="RawImage"/>.</summary>
  public static AcornFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var width = image.Width;
    var height = image.Height;

    AcornSprite sprite;
    switch (image.Format) {
      case PixelFormat.Indexed8: {
        var bpp = image.PaletteCount switch {
          <= 2 => 1,
          <= 4 => 2,
          <= 16 => 4,
          _ => 8
        };

        var paletteCount = image.PaletteCount;
        sprite = _CreateIndexedSprite(image, width, height, bpp, paletteCount);
        break;
      }
      case PixelFormat.Rgb24: {
        sprite = _CreateRgb24Sprite(image, width, height);
        break;
      }
      default:
        throw new ArgumentException($"Unsupported pixel format for Acorn: {image.Format}.", nameof(image));
    }

    return new() { Sprites = [sprite] };
  }

  private static AcornSprite _CreateIndexedSprite(RawImage image, int width, int height, int bpp, int paletteCount) {
    var pixelsPerByte = 8 / bpp;
    var rawBytesPerRow = (width + pixelsPerByte - 1) / pixelsPerByte;
    var paddedBytesPerRow = (rawBytesPerRow + 3) & ~3;
    var pixelData = new byte[paddedBytesPerRow * height];

    for (var y = 0; y < height; ++y) {
      var srcRow = y * width;
      var dstRow = y * paddedBytesPerRow;

      for (var x = 0; x < width; ++x) {
        var index = image.PixelData[srcRow + x];
        switch (bpp) {
          case 1: {
            var byteIndex = dstRow + x / 8;
            var bitIndex = 7 - (x & 7); // MSB first
            pixelData[byteIndex] |= (byte)(index << bitIndex);
            break;
          }
          case 2: {
            var byteIndex = dstRow + x / 4;
            var shift = (x & 3) * 2;
            pixelData[byteIndex] |= (byte)((index & 3) << shift);
            break;
          }
          case 4: {
            var byteIndex = dstRow + x / 2;
            if ((x & 1) == 0)
              pixelData[byteIndex] |= (byte)(index & 0x0F); // low nibble
            else
              pixelData[byteIndex] |= (byte)((index & 0x0F) << 4); // high nibble
            break;
          }
          default:
            pixelData[dstRow + x] = index;
            break;
        }
      }
    }

    // Build Acorn palette: 2 words (8 bytes) per entry
    var acornPaletteSize = paletteCount * 8;
    var acornPalette = new byte[acornPaletteSize];
    var srcPalette = image.Palette;
    for (var i = 0; i < paletteCount; ++i) {
      var off = i * 8;
      byte r = 0, g = 0, b = 0;
      if (srcPalette != null && i * 3 + 2 < srcPalette.Length) {
        r = srcPalette[i * 3];
        g = srcPalette[i * 3 + 1];
        b = srcPalette[i * 3 + 2];
      }

      // Word 0: byte0=flags(0), byte1=R, byte2=G, byte3=B
      acornPalette[off] = 0;
      acornPalette[off + 1] = r;
      acornPalette[off + 2] = g;
      acornPalette[off + 3] = b;
      // Word 1: copy of word 0
      acornPalette[off + 4] = 0;
      acornPalette[off + 5] = r;
      acornPalette[off + 6] = g;
      acornPalette[off + 7] = b;
    }

    // Construct a new-format mode word: bits 27-30 = log2(bpp), bit 0 = 1 (new format flag within a valid mode)
    var log2bpp = bpp switch { 1 => 0, 2 => 1, 4 => 2, 8 => 3, _ => 3 };
    var mode = (log2bpp << 27) | 1;

    return new() {
      Name = "sprite",
      Width = width,
      Height = height,
      BitsPerPixel = bpp,
      Mode = mode,
      PixelData = pixelData,
      Palette = acornPalette,
    };
  }

  private static AcornSprite _CreateRgb24Sprite(RawImage image, int width, int height) {
    // 32bpp: B, G, R, 0 per pixel (already word-aligned)
    var bytesPerRow = width * 4;
    var pixelData = new byte[bytesPerRow * height];

    for (var y = 0; y < height; ++y) {
      var srcRow = y * width * 3;
      var dstRow = y * bytesPerRow;
      for (var x = 0; x < width; ++x) {
        var srcOff = srcRow + x * 3;
        var dstOff = dstRow + x * 4;
        pixelData[dstOff] = image.PixelData[srcOff + 2];     // B
        pixelData[dstOff + 1] = image.PixelData[srcOff + 1]; // G
        pixelData[dstOff + 2] = image.PixelData[srcOff];     // R
        pixelData[dstOff + 3] = 0;                           // pad
      }
    }

    // New-format mode word for 32bpp: log2(32) = 5
    var mode = (5 << 27) | 1;

    return new() {
      Name = "sprite",
      Width = width,
      Height = height,
      BitsPerPixel = 32,
      Mode = mode,
      PixelData = pixelData,
    };
  }
}
