using FileFormat.Core;

namespace FileFormat.Ico;

/// <summary>A 16-byte directory entry in an ICO/CUR file.</summary>
/// <param name="Width">Image width in pixels (0 means 256).</param>
/// <param name="Height">Image height in pixels (0 means 256).</param>
/// <param name="ColorCount">Number of palette colors (0 if no palette).</param>
/// <param name="Reserved">Reserved, should be 0.</param>
/// <param name="Field4">Planes (ICO) or HotspotX (CUR).</param>
/// <param name="Field5">BitCount (ICO) or HotspotY (CUR).</param>
/// <param name="DataSize">Size of the image data in bytes.</param>
/// <param name="DataOffset">Offset of the image data from the start of the file.</param>
[GenerateSerializer]
internal readonly partial record struct IcoDirectoryEntry(
  byte Width,
  byte Height,
  byte ColorCount,
  byte Reserved,
  ushort Field4,
  ushort Field5,
  int DataSize,
  int DataOffset
) {

 public const int StructSize = 16;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<IcoDirectoryEntry>();
}
