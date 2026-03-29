using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Acorn;

/// <summary>Assembles Acorn RISC OS sprite file bytes from an <see cref="AcornFile"/>.</summary>
public static class AcornWriter {

  private const int _AREA_HEADER_SIZE = 12;

  public static byte[] ToBytes(AcornFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var sprites = file.Sprites;
    var spriteCount = sprites.Count;

    // Calculate total size
    var totalSize = _AREA_HEADER_SIZE;
    for (var i = 0; i < spriteCount; ++i)
      totalSize += _CalculateSpriteSize(sprites[i]);

    var result = new byte[totalSize];
    var span = result.AsSpan();

    // Area header: spriteCount, firstSpriteOffset (16 = area header size in the RISC OS sense = 12 + 4 implicit),
    // freeWordOffset (totalSize + 4)
    BinaryPrimitives.WriteInt32LittleEndian(span, spriteCount);
    BinaryPrimitives.WriteInt32LittleEndian(span[4..], 16); // firstSpriteOffset (area start + 16)
    BinaryPrimitives.WriteInt32LittleEndian(span[8..], totalSize + 4); // freeWordOffset

    var offset = _AREA_HEADER_SIZE;
    for (var i = 0; i < spriteCount; ++i) {
      var sprite = sprites[i];
      var spriteSize = _CalculateSpriteSize(sprite);

      _WriteSprite(result, offset, sprite, spriteSize);
      offset += spriteSize;
    }

    return result;
  }

  private static int _CalculateSpriteSize(AcornSprite sprite) {
    var paletteSize = sprite.Palette?.Length ?? 0;
    var imageSize = sprite.PixelData.Length;
    var maskSize = sprite.MaskData?.Length ?? 0;
    return AcornSpriteHeader.StructSize + paletteSize + imageSize + maskSize;
  }

  private static void _WriteSprite(byte[] result, int offset, AcornSprite sprite, int spriteSize) {
    var span = result.AsSpan();
    var bpp = sprite.BitsPerPixel;

    // Calculate word-aligned dimensions for the header
    var bytesPerRow = sprite.PixelData.Length / Math.Max(sprite.Height, 1);
    var widthInWords = bytesPerRow / 4 - 1;
    var lastBitUsed = sprite.Width * bpp - 1 + 0; // assuming FirstBitUsed = 0
    // Clamp lastBitUsed to max 31 within the last word
    var totalPixelBits = sprite.Width * bpp;
    var lastBitInWord = (totalPixelBits - 1) % 32;

    var paletteSize = sprite.Palette?.Length ?? 0;
    var imageOffset = AcornSpriteHeader.StructSize + paletteSize;
    var hasMask = sprite.MaskData is { Length: > 0 };
    var maskOffset = hasMask ? imageOffset + sprite.PixelData.Length : imageOffset;

    var header = new AcornSpriteHeader(
      spriteSize,
      sprite.Name,
      widthInWords,
      sprite.Height - 1,
      0, // FirstBitUsed
      lastBitInWord,
      imageOffset,
      maskOffset,
      sprite.Mode
    );
    header.WriteTo(span[offset..]);

    // Write palette
    if (sprite.Palette is { Length: > 0 })
      sprite.Palette.AsSpan(0, paletteSize).CopyTo(result.AsSpan(offset + AcornSpriteHeader.StructSize));

    // Write image data
    sprite.PixelData.AsSpan(0, sprite.PixelData.Length).CopyTo(result.AsSpan(offset + imageOffset));

    // Write mask data
    if (hasMask)
      sprite.MaskData!.AsSpan(0, sprite.MaskData!.Length).CopyTo(result.AsSpan(offset + maskOffset));
  }
}
