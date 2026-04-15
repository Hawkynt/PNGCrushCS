using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Acorn;

/// <summary>Reads Acorn RISC OS sprite files from bytes, streams, or file paths.</summary>
public static class AcornReader {

  public static AcornFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Acorn sprite file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AcornFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static AcornFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < AcornAreaHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid Acorn sprite file.");

    var span = data;
    var areaHeader = AcornAreaHeader.ReadFrom(span);
    var spriteCount = areaHeader.SpriteCount;
    var firstSpriteOffset = areaHeader.FirstSpriteOffset;

    if (spriteCount < 0)
      throw new InvalidDataException($"Invalid sprite count: {spriteCount}.");

    // firstSpriteOffset is relative to start of sprite area; in file context it's the offset past the area header fields
    // Typically 16 (the area header is 4 words = 16 bytes, but we only read 12 bytes of the area header from the file;
    // the first word is the total size which we skip). Actually in files the area header starts with:
    //   word 0: spriteCount
    //   word 1: firstSpriteOffset (offset from area start, typically 16)
    //   word 2: freeWordOffset
    // Sprites start at firstSpriteOffset - 4 in the data (since the area "start" includes an implicit size word before our data).
    // For simplicity: sprites start at offset (firstSpriteOffset - 4) in our data array.
    var spriteStart = firstSpriteOffset - 4;
    if (spriteStart < AcornAreaHeader.StructSize)
      spriteStart = AcornAreaHeader.StructSize;

    var sprites = new List<AcornSprite>();
    var offset = spriteStart;

    for (var i = 0; i < spriteCount; ++i) {
      if (offset + AcornSpriteHeader.StructSize > data.Length)
        throw new InvalidDataException($"Data too small for sprite header at offset {offset}.");

      var header = AcornSpriteHeader.ReadFrom(span[offset..]);

      var bpp = _GetBitsPerPixel(header.Mode);
      var totalBits = (header.WidthInWords + 1) * 32;
      var usedBits = totalBits - header.FirstBitUsed - (31 - header.LastBitUsed);
      var width = usedBits / bpp;
      var height = header.HeightInScanlines + 1;

      // Palette sits between the sprite header and image data
      byte[]? palette = null;
      var paletteSize = header.ImageOffset - AcornSpriteHeader.StructSize;
      if (paletteSize > 0) {
        var paletteStart = offset + AcornSpriteHeader.StructSize;
        if (paletteStart + paletteSize > data.Length)
          throw new InvalidDataException("Data too small for palette data.");

        palette = new byte[paletteSize];
        data.Slice(paletteStart, paletteSize).CopyTo(palette.AsSpan(0));
      }

      // Image data
      var imageStart = offset + header.ImageOffset;
      var bytesPerRow = (header.WidthInWords + 1) * 4;
      var imageSize = bytesPerRow * height;

      // Determine mask presence and size
      byte[]? maskData = null;
      int spriteDataEnd;
      if (header.MaskOffset != header.ImageOffset) {
        var maskStart = offset + header.MaskOffset;
        var maskSize = imageSize; // mask has same dimensions as image
        spriteDataEnd = maskStart + maskSize;

        if (maskStart + maskSize <= data.Length) {
          maskData = new byte[maskSize];
          data.Slice(maskStart, maskSize).CopyTo(maskData.AsSpan(0));
        }
      } else {
        spriteDataEnd = imageStart + imageSize;
      }

      if (imageStart + imageSize > data.Length)
        throw new InvalidDataException("Data too small for image data.");

      var pixelData = new byte[imageSize];
      data.Slice(imageStart, imageSize).CopyTo(pixelData.AsSpan(0));

      sprites.Add(new AcornSprite {
        Name = header.Name,
        Width = width,
        Height = height,
        BitsPerPixel = bpp,
        Mode = header.Mode,
        PixelData = pixelData,
        MaskData = maskData,
        Palette = palette
      });

      offset += header.NextSpriteOffset;
    }

    return new AcornFile { Sprites = sprites };
    }

  public static AcornFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < AcornAreaHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid Acorn sprite file.");

    var span = data.AsSpan();
    var areaHeader = AcornAreaHeader.ReadFrom(span);
    var spriteCount = areaHeader.SpriteCount;
    var firstSpriteOffset = areaHeader.FirstSpriteOffset;

    if (spriteCount < 0)
      throw new InvalidDataException($"Invalid sprite count: {spriteCount}.");

    // firstSpriteOffset is relative to start of sprite area; in file context it's the offset past the area header fields
    // Typically 16 (the area header is 4 words = 16 bytes, but we only read 12 bytes of the area header from the file;
    // the first word is the total size which we skip). Actually in files the area header starts with:
    //   word 0: spriteCount
    //   word 1: firstSpriteOffset (offset from area start, typically 16)
    //   word 2: freeWordOffset
    // Sprites start at firstSpriteOffset - 4 in the data (since the area "start" includes an implicit size word before our data).
    // For simplicity: sprites start at offset (firstSpriteOffset - 4) in our data array.
    var spriteStart = firstSpriteOffset - 4;
    if (spriteStart < AcornAreaHeader.StructSize)
      spriteStart = AcornAreaHeader.StructSize;

    var sprites = new List<AcornSprite>();
    var offset = spriteStart;

    for (var i = 0; i < spriteCount; ++i) {
      if (offset + AcornSpriteHeader.StructSize > data.Length)
        throw new InvalidDataException($"Data too small for sprite header at offset {offset}.");

      var header = AcornSpriteHeader.ReadFrom(span[offset..]);

      var bpp = _GetBitsPerPixel(header.Mode);
      var totalBits = (header.WidthInWords + 1) * 32;
      var usedBits = totalBits - header.FirstBitUsed - (31 - header.LastBitUsed);
      var width = usedBits / bpp;
      var height = header.HeightInScanlines + 1;

      // Palette sits between the sprite header and image data
      byte[]? palette = null;
      var paletteSize = header.ImageOffset - AcornSpriteHeader.StructSize;
      if (paletteSize > 0) {
        var paletteStart = offset + AcornSpriteHeader.StructSize;
        if (paletteStart + paletteSize > data.Length)
          throw new InvalidDataException("Data too small for palette data.");

        palette = new byte[paletteSize];
        data.AsSpan(paletteStart, paletteSize).CopyTo(palette.AsSpan(0));
      }

      // Image data
      var imageStart = offset + header.ImageOffset;
      var bytesPerRow = (header.WidthInWords + 1) * 4;
      var imageSize = bytesPerRow * height;

      // Determine mask presence and size
      byte[]? maskData = null;
      int spriteDataEnd;
      if (header.MaskOffset != header.ImageOffset) {
        var maskStart = offset + header.MaskOffset;
        var maskSize = imageSize; // mask has same dimensions as image
        spriteDataEnd = maskStart + maskSize;

        if (maskStart + maskSize <= data.Length) {
          maskData = new byte[maskSize];
          data.AsSpan(maskStart, maskSize).CopyTo(maskData.AsSpan(0));
        }
      } else {
        spriteDataEnd = imageStart + imageSize;
      }

      if (imageStart + imageSize > data.Length)
        throw new InvalidDataException("Data too small for image data.");

      var pixelData = new byte[imageSize];
      data.AsSpan(imageStart, imageSize).CopyTo(pixelData.AsSpan(0));

      sprites.Add(new AcornSprite {
        Name = header.Name,
        Width = width,
        Height = height,
        BitsPerPixel = bpp,
        Mode = header.Mode,
        PixelData = pixelData,
        MaskData = maskData,
        Palette = palette
      });

      offset += header.NextSpriteOffset;
    }

    return new AcornFile { Sprites = sprites };
  }

  internal static int _GetBitsPerPixel(int mode) {
    // New-format mode word: if mode >= 256, extract pixel depth from bits 27-30
    if (mode >= 256) {
      var log2bpp = (mode >> 27) & 0xF;
      return 1 << log2bpp;
    }

    // Old-style screen mode number lookup
    return mode switch {
      0 or 4 or 8 or 12 or 25 => 1,
      1 or 5 or 9 or 13 or 26 => 2,
      2 or 10 or 14 or 27 => 4,
      3 or 6 or 7 or 11 or 15 or 16 => 8,
      20 or 21 or 23 or 24 => 16,
      28 or 29 or 30 or 31 => 32,
      _ => 8 // fallback
    };
  }

}
