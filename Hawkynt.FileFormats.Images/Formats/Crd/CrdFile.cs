using System;
using FileFormat.Core;
using FileFormat.Jpeg;

namespace FileFormat.Crd;

/// <summary>A PowerCard maker document (.crd): a greeting-card file with a JPEG inside it.</summary>
/// <remarks>
/// Nothing published describes this format, so the layout comes from XnView's own reader, the
/// function its 567-entry format table pairs with the name <c>crd</c>. That function reads eleven
/// bytes, insists the first is 9 and the nine behind it are the letters <c>CardMaker</c> — a
/// length-prefixed name, the tenth byte being the count of the letters that follow — and does not
/// look at the eleventh at all. From byte eleven onwards it slides a four-byte window through the
/// rest of the file looking for the letters <c>JFIF</c>, and when it finds them it seeks six bytes
/// back and hands over to a JPEG decoder. Six bytes is exactly the distance from a JPEG's first byte
/// to the identifier inside its JFIF APP0 segment, so what it is really looking for is the start of
/// a JFIF JPEG. If it reaches the end without finding one it reports <c>CRD : No images !</c>.
/// <para/>
/// Every part of that was checked one byte at a time against the converter: a length byte of 8 or a
/// misspelt name is refused, the eleventh byte may be anything, five hundred bytes of padding
/// between the name and the picture change nothing, and a JPEG with its JFIF APP0 segment removed is
/// refused with the same message.
/// <para/>
/// So this reader locates the picture the way the format does — by the payload announcing itself,
/// not by an offset guessed from one sample — and then requires the payload to agree: the four bytes
/// ahead of the identifier have to be a real SOI and APP0 pair, the APP0 length has to cover the
/// identifier, and the JPEG's own marker chain has to run to an EOI that lies inside the file. The
/// stated end of the picture is the picture's own.
/// <para/>
/// Confirmed against XnView's converter: <c>Name : crd, Format : PowerCard maker</c>, the size and
/// depth of the JPEG inside, and pixels identical to what the same JPEG decodes to on its own.
/// </remarks>
[FormatMagicBytes([0x09, (byte)'C', (byte)'a', (byte)'r', (byte)'d', (byte)'M', (byte)'a', (byte)'k', (byte)'e', (byte)'r'])]
public sealed class CrdFile : IImageFormatReader<CrdFile>, IImageToRawImage<CrdFile> {

  /// <summary>The length-prefixed name a file opens with.</summary>
  public static ReadOnlySpan<byte> Magic => [0x09, (byte)'C', (byte)'a', (byte)'r', (byte)'d', (byte)'M', (byte)'a', (byte)'k', (byte)'e', (byte)'r'];

  /// <summary>The name and the one byte behind it that the format's own reader steps over.</summary>
  public const int HeaderSize = 11;

  /// <summary>How far into a JFIF JPEG the identifier stands, which is how the picture is found.</summary>
  public const int JfifIdentifierOffset = 6;

  static string IImageFormatMetadata<CrdFile>.PrimaryExtension => ".crd";
  static string[] IImageFormatMetadata<CrdFile>.FileExtensions => [".crd"];
  static CrdFile IImageFormatReader<CrdFile>.FromSpan(ReadOnlySpan<byte> data) => CrdReader.FromSpan(data);

  /// <summary>Where in the document the picture stands.</summary>
  public int PictureOffset { get; init; }

  /// <summary>The picture, a whole JPEG file, framed by its own markers.</summary>
  public byte[] PictureData { get; init; } = [];

  public static RawImage ToRawImage(CrdFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.PictureData.Length == 0)
      throw new InvalidOperationException("No picture was read.");

    return JpegFile.ToRawImage(JpegReader.FromBytes(file.PictureData));
  }
}
