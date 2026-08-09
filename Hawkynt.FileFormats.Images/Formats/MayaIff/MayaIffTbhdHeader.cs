using FileFormat.Core;

namespace FileFormat.MayaIff;

/// <summary>Maya IFF TBHD (tile-based header) chunk data -- 24 bytes, big-endian.</summary>
/// <remarks>
/// This used to be declared as 32 bytes, eight of them filler, and the reader refused any chunk
/// shorter than that. A real file states 24 — the eight extra were never in the format — so every
/// file written by anything but this library was refused at its header.
/// </remarks>
[GenerateSerializer, Endian(Endianness.Big)]
public readonly partial record struct MayaIffTbhdHeader( uint Width, uint Height, ushort Prnum, ushort Prden, uint Flags, ushort Bytes, ushort Tiles, uint Compression
) {

  /// <summary>The picture has colour planes.</summary>
  public const uint RgbFlag = 1;

  /// <summary>The picture has an alpha plane as well.</summary>
  public const uint AlphaFlag = 2;

  /// <summary>The picture has a depth plane, which is not pixels and is not read.</summary>
  public const uint ZBufferFlag = 4;

  public const int StructSize = 24;

  public static HeaderFieldDescriptor[] GetFieldMap()
  => HeaderFieldMapper.GetFieldMap<MayaIffTbhdHeader>();
}
