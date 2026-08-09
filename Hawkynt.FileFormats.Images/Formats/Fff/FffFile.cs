using System;
using System.Text;
using FileFormat.Core;
using FileFormat.Jpeg;

namespace FileFormat.Fff;

/// <summary>A MAGGI Hairstyles &amp; Cosmetics file (.fff): a client record with a JPEG portrait in it.</summary>
/// <remarks>
/// The extension <c>.fff</c> is claimed twice in XnView's format table and the two have nothing to do
/// with each other: one entry is Imacon and Hasselblad's raw format and the other, this one, is the
/// hairdressing and cosmetics package MAGGI. They have separate readers, and this is the second.
/// <para/>
/// Nothing published describes it, so the layout comes from that reader. It steps 452 bytes forward
/// without looking at them, reads 24 and compares them as a C string against
/// <c>hairstyles &amp; cosmetic</c> followed by two spaces — the string sits in the binary's read-only
/// data at 0x37CDD7 and the two trailing spaces are part of it, so the field is 23 letters and a
/// terminating zero, filling the 24 bytes exactly. If they match it seeks to byte 3272 and hands over
/// to a JPEG decoder, labelling what comes back <c>MAGGI Hairstyles &amp; Cosmetics</c>.
/// <para/>
/// Every one of those numbers was checked against the converter one byte at a time: the signature at
/// 451 or at 453 is refused, a capital <c>H</c> is refused, one space instead of two or three instead
/// of two are refused, and a picture at 3271 or at 3273 is refused. What fills the rest of the file
/// makes no difference at all — zeroes and letters both read.
/// <para/>
/// The record states neither the position nor the length of the portrait anywhere this reader can
/// see, so what has to agree is the payload itself: a JPEG has to open at 3272 and its own marker
/// chain has to run to an EOI that lies inside the file. A signature at a fixed place plus a payload
/// that frames itself is the whole of the identification.
/// <para/>
/// Confirmed against XnView's converter: <c>Name : fff, Format : MAGGI Hairstyles &amp; Cosmetics</c>,
/// the size and depth of the JPEG inside, and pixels identical to what the same JPEG decodes to on
/// its own.
/// </remarks>
[FormatMagicBytes([
  (byte)'h', (byte)'a', (byte)'i', (byte)'r', (byte)'s', (byte)'t', (byte)'y', (byte)'l', (byte)'e', (byte)'s',
  (byte)' ', (byte)'&', (byte)' ',
  (byte)'c', (byte)'o', (byte)'s', (byte)'m', (byte)'e', (byte)'t', (byte)'i', (byte)'c', (byte)' ', (byte)' ', 0x00
], SignatureOffset)]
public sealed class FffFile : IImageFormatReader<FffFile>, IImageToRawImage<FffFile> {

  /// <summary>Where the signature stands, 452 bytes into the record.</summary>
  public const int SignatureOffset = 0x1C4;

  /// <summary>How long the signature field is, terminator included.</summary>
  public const int SignatureSize = 24;

  /// <summary>Where the portrait stands, 3272 bytes into the record.</summary>
  public const int PictureOffset = 0xCC8;

  /// <summary>The 24 bytes at <see cref="SignatureOffset"/>, terminator included.</summary>
  public static ReadOnlySpan<byte> Magic => "hairstyles & cosmetic  \0"u8;

  /// <summary>The name the signature spells, without its terminator.</summary>
  public static string SignatureText => Encoding.ASCII.GetString(Magic[..^1]);

  static string IImageFormatMetadata<FffFile>.PrimaryExtension => ".fff";
  static string[] IImageFormatMetadata<FffFile>.FileExtensions => [".fff"];
  static FffFile IImageFormatReader<FffFile>.FromSpan(ReadOnlySpan<byte> data) => FffReader.FromSpan(data);

  /// <summary>The portrait, a whole JPEG file, framed by its own markers.</summary>
  public byte[] PictureData { get; init; } = [];

  public static RawImage ToRawImage(FffFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.PictureData.Length == 0)
      throw new InvalidOperationException("No picture was read.");

    return JpegFile.ToRawImage(JpegReader.FromBytes(file.PictureData));
  }
}
