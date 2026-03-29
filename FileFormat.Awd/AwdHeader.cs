using System;
using FileFormat.Core;

namespace FileFormat.Awd;

/// <summary>The 16-byte header at the start of every AWD file: "AWD\0" magic (4 bytes), Version uint16 LE, Width uint32 LE, Height uint32 LE, Reserved uint16 LE.</summary>
[GenerateSerializer]
public readonly partial record struct AwdHeader(
  [property: HeaderField(4, 2)] ushort Version,
  [property: HeaderField(6, 4)] uint Width,
  [property: HeaderField(10, 4)] uint Height,
  [property: HeaderField(14, 2)] ushort Reserved
) {

  public const int StructSize = 16;

  /// <summary>The 4-byte magic identifier: "AWD\0".</summary>
  public static ReadOnlySpan<byte> Magic => "AWD\0"u8;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<AwdHeader>();
}
