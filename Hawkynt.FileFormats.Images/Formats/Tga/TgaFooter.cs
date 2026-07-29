using FileFormat.Core;

namespace FileFormat.Tga;

/// <summary>The 26-byte TGA 2.0 footer at the end of a TGA file.</summary>
[GenerateSerializer]
internal readonly partial record struct TgaFooter(
  int ExtensionAreaOffset,
  int DeveloperDirectoryOffset,
  [property: String, SeqField(Size = 18)] string Signature
) {

 public const int StructSize = 26;
 // 17-char string + terminating '\0' fills the 18-byte Signature field.
 public const string SignatureString = "TRUEVISION-XFILE.";

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<TgaFooter>();
}
