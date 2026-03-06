using System;
using System.Buffers.Binary;
using FileFormat.Core;

namespace FileFormat.Bmp;

/// <summary>The 40-byte BITMAPINFOHEADER following the file header.</summary>
public readonly record struct BitmapInfoHeader(
  int HeaderSize,
  int Width,
  int Height,
  short Planes,
  short BitsPerPixel,
  int Compression,
  int ImageSize,
  int XPixelsPerMeter,
  int YPixelsPerMeter,
  int ColorsUsed,
  int ImportantColors
) {

  public const int StructSize = 40;

  public static BitmapInfoHeader ReadFrom(ReadOnlySpan<byte> source) => new(
    BinaryPrimitives.ReadInt32LittleEndian(source),
    BinaryPrimitives.ReadInt32LittleEndian(source[4..]),
    BinaryPrimitives.ReadInt32LittleEndian(source[8..]),
    BinaryPrimitives.ReadInt16LittleEndian(source[12..]),
    BinaryPrimitives.ReadInt16LittleEndian(source[14..]),
    BinaryPrimitives.ReadInt32LittleEndian(source[16..]),
    BinaryPrimitives.ReadInt32LittleEndian(source[20..]),
    BinaryPrimitives.ReadInt32LittleEndian(source[24..]),
    BinaryPrimitives.ReadInt32LittleEndian(source[28..]),
    BinaryPrimitives.ReadInt32LittleEndian(source[32..]),
    BinaryPrimitives.ReadInt32LittleEndian(source[36..])
  );

  public void WriteTo(Span<byte> destination) {
    BinaryPrimitives.WriteInt32LittleEndian(destination, this.HeaderSize);
    BinaryPrimitives.WriteInt32LittleEndian(destination[4..], this.Width);
    BinaryPrimitives.WriteInt32LittleEndian(destination[8..], this.Height);
    BinaryPrimitives.WriteInt16LittleEndian(destination[12..], this.Planes);
    BinaryPrimitives.WriteInt16LittleEndian(destination[14..], this.BitsPerPixel);
    BinaryPrimitives.WriteInt32LittleEndian(destination[16..], this.Compression);
    BinaryPrimitives.WriteInt32LittleEndian(destination[20..], this.ImageSize);
    BinaryPrimitives.WriteInt32LittleEndian(destination[24..], this.XPixelsPerMeter);
    BinaryPrimitives.WriteInt32LittleEndian(destination[28..], this.YPixelsPerMeter);
    BinaryPrimitives.WriteInt32LittleEndian(destination[32..], this.ColorsUsed);
    BinaryPrimitives.WriteInt32LittleEndian(destination[36..], this.ImportantColors);
  }

  public static HeaderFieldDescriptor[] GetFieldMap() => [
    new("HeaderSize", 0, 4),
    new("Width", 4, 4),
    new("Height", 8, 4),
    new("Planes", 12, 2),
    new("BitsPerPixel", 14, 2),
    new("Compression", 16, 4),
    new("ImageSize", 20, 4),
    new("XPixelsPerMeter", 24, 4),
    new("YPixelsPerMeter", 28, 4),
    new("ColorsUsed", 32, 4),
    new("ImportantColors", 36, 4)
  ];
}
