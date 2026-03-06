using System;
using System.Buffers.Binary;
using FileFormat.Core;

namespace FileFormat.Pcx;

/// <summary>The 128-byte header at the start of every PCX file.</summary>
internal readonly record struct PcxHeader(
  byte Manufacturer,
  byte Version,
  byte Encoding,
  byte BitsPerPixel,
  short XMin,
  short YMin,
  short XMax,
  short YMax,
  short HDpi,
  short VDpi,
  byte[] EgaPalette,
  byte Reserved,
  byte NumPlanes,
  short BytesPerLine,
  short PaletteInfo,
  short HScreenSize,
  short VScreenSize,
  byte[] Padding
) {

  public const int StructSize = 128;

  public static PcxHeader ReadFrom(ReadOnlySpan<byte> source) => new(
    source[0],
    source[1],
    source[2],
    source[3],
    BinaryPrimitives.ReadInt16LittleEndian(source[4..]),
    BinaryPrimitives.ReadInt16LittleEndian(source[6..]),
    BinaryPrimitives.ReadInt16LittleEndian(source[8..]),
    BinaryPrimitives.ReadInt16LittleEndian(source[10..]),
    BinaryPrimitives.ReadInt16LittleEndian(source[12..]),
    BinaryPrimitives.ReadInt16LittleEndian(source[14..]),
    source.Slice(16, 48).ToArray(),
    source[64],
    source[65],
    BinaryPrimitives.ReadInt16LittleEndian(source[66..]),
    BinaryPrimitives.ReadInt16LittleEndian(source[68..]),
    BinaryPrimitives.ReadInt16LittleEndian(source[70..]),
    BinaryPrimitives.ReadInt16LittleEndian(source[72..]),
    source.Slice(74, 54).ToArray()
  );

  public void WriteTo(Span<byte> destination) {
    destination[0] = this.Manufacturer;
    destination[1] = this.Version;
    destination[2] = this.Encoding;
    destination[3] = this.BitsPerPixel;
    BinaryPrimitives.WriteInt16LittleEndian(destination[4..], this.XMin);
    BinaryPrimitives.WriteInt16LittleEndian(destination[6..], this.YMin);
    BinaryPrimitives.WriteInt16LittleEndian(destination[8..], this.XMax);
    BinaryPrimitives.WriteInt16LittleEndian(destination[10..], this.YMax);
    BinaryPrimitives.WriteInt16LittleEndian(destination[12..], this.HDpi);
    BinaryPrimitives.WriteInt16LittleEndian(destination[14..], this.VDpi);
    this.EgaPalette.AsSpan().CopyTo(destination.Slice(16, 48));
    destination[64] = this.Reserved;
    destination[65] = this.NumPlanes;
    BinaryPrimitives.WriteInt16LittleEndian(destination[66..], this.BytesPerLine);
    BinaryPrimitives.WriteInt16LittleEndian(destination[68..], this.PaletteInfo);
    BinaryPrimitives.WriteInt16LittleEndian(destination[70..], this.HScreenSize);
    BinaryPrimitives.WriteInt16LittleEndian(destination[72..], this.VScreenSize);
    this.Padding.AsSpan().CopyTo(destination.Slice(74, 54));
  }

  public static HeaderFieldDescriptor[] GetFieldMap() => [
    new("Manufacturer", 0, 1),
    new("Version", 1, 1),
    new("Encoding", 2, 1),
    new("BitsPerPixel", 3, 1),
    new("XMin", 4, 2),
    new("YMin", 6, 2),
    new("XMax", 8, 2),
    new("YMax", 10, 2),
    new("HDpi", 12, 2),
    new("VDpi", 14, 2),
    new("EgaPalette", 16, 48),
    new("Reserved", 64, 1),
    new("NumPlanes", 65, 1),
    new("BytesPerLine", 66, 2),
    new("PaletteInfo", 68, 2),
    new("HScreenSize", 70, 2),
    new("VScreenSize", 72, 2),
    new("Padding", 74, 54)
  ];
}
