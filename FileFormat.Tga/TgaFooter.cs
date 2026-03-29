using FileFormat.Core;

namespace FileFormat.Tga;

/// <summary>The 26-byte TGA 2.0 footer at the end of a TGA file.</summary>
[GenerateSerializer]
internal readonly partial record struct TgaFooter(
  [property: HeaderField(0, 4)] int ExtensionAreaOffset,
  [property: HeaderField(4, 4)] int DeveloperDirectoryOffset,
  [property: HeaderField(8, 18)] string Signature
) {

  public const int StructSize = 26;
  public const string SignatureString = "TRUEVISION-XFILE.";

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<TgaFooter>();
}
