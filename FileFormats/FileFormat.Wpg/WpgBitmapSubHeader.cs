using FileFormat.Core;

namespace FileFormat.Wpg;

/// <summary>The 10-byte sub-header inside a BitmapType1 record: width, height, depth, xdpi, ydpi (all LE uint16).</summary>
[GenerateSerializer]
internal readonly partial record struct WpgBitmapSubHeader(
  ushort Width,
  ushort Height,
  ushort Depth,
  ushort XDpi,
  ushort YDpi
) {

 public const int StructSize = 10;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<WpgBitmapSubHeader>();
}
