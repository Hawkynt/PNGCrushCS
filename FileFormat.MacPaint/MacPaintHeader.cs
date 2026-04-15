using FileFormat.Core;

namespace FileFormat.MacPaint;

/// <summary>The 512-byte header at the start of every MacPaint file.</summary>
[GenerateSerializer, Endian(Endianness.Big)]
[Filler(4 + MacPaintHeader.PatternsSize, MacPaintHeader.PaddingSize)]
internal readonly partial record struct MacPaintHeader( int Version, byte[] Patterns
) {

 public const int StructSize = 512;
 public const int PatternsSize = 304;
 public const int PaddingSize = 204;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<MacPaintHeader>();
}
