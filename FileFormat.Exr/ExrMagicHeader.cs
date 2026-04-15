using FileFormat.Core;

namespace FileFormat.Exr;

/// <summary>The 8-byte magic header at the start of every OpenEXR file.</summary>
[GenerateSerializer]
public readonly partial record struct ExrMagicHeader( int Magic, int Version
) {

 public const int StructSize = 8;
 public const int ExpectedMagic = 0x01312F76;
 public const int ExpectedVersion = 2;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<ExrMagicHeader>();
}
