using System;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Core;

namespace FileFormat.Wzl;

/// <summary>In-memory representation of a .wzl picture, which is a Windows bitmap put out of reach.</summary>
/// <remarks>
/// There is no format here to speak of. A .wzl is an ordinary Windows bitmap — file header, info
/// header, palette, rows — with the first 256 bytes of it exclusive-ored with 0x0D and the rest left
/// exactly as it stands. That is enough to stop anything opening it by accident and nothing else.
/// <para/>
/// The key is not guessed. A bitmap opens with <c>BM</c> and these open with <c>O@</c>, and
/// 'B' xor 'O' and 'M' xor '@' are both 0x0D; undoing that gives a stated file length equal to the
/// file's own length to the byte in all sixteen samples. Where the exclusive-or stops is read off the
/// files rather than assumed: the fourth byte of every palette entry is reserved and zero, so those
/// bytes state the key directly, and they say 0x0D up to 256 and nothing after it.
/// <para/>
/// What comes out is handed to the bitmap reader, so every depth, palette form and row order it knows
/// works here — which matters, because the sixteen are not all one shape: 4- and 8-bit, uncompressed
/// and both run-length forms. ImageMagick opens the unscrambled bytes too, and agrees with this on
/// every pixel of all sixteen.
/// </remarks>
[FormatMagicBytes([(byte)'B' ^ 0x0D, (byte)'M' ^ 0x0D])]
public readonly record struct WzlFile
  : IImageFormatReader<WzlFile>, IImageToRawImage<WzlFile>,
    IImageFromRawImage<WzlFile>, IImageFormatWriter<WzlFile> {

  /// <summary>What the first bytes are exclusive-ored with.</summary>
  public const byte Key = 0x0D;

  /// <summary>How many bytes at the front carry it.</summary>
  public const int ScrambledLength = 256;

  /// <summary>A bitmap file header and the shortest info header there is.</summary>
  public const int MinimumLength = 14 + 40;

  static string IImageFormatMetadata<WzlFile>.PrimaryExtension => ".wzl";
  static string[] IImageFormatMetadata<WzlFile>.FileExtensions => [".wzl"];
  static WzlFile IImageFormatReader<WzlFile>.FromSpan(ReadOnlySpan<byte> data) => WzlReader.FromSpan(data);
  static byte[] IImageFormatWriter<WzlFile>.ToBytes(WzlFile file) => WzlWriter.ToBytes(file);

  /// <summary>Whether these bytes carry the scrambled bitmap header, and a stated length that agrees.</summary>
  static bool? IImageFormatMetadata<WzlFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 6 && header[0] == ((byte)'B' ^ Key) && header[1] == ((byte)'M' ^ Key) ? true : null;

  /// <summary>The bitmap as it stands once the scrambling is undone.</summary>
  public byte[] Bitmap { get; init; }

  public static RawImage ToRawImage(WzlFile file)
    => BmpFile.ToRawImage(BmpReader.FromBytes(file.Bitmap ?? throw new InvalidDataException("No bitmap was read.")));

  public static WzlFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    return new() { Bitmap = BmpWriter.ToBytes(BmpFile.FromRawImage(image)) };
  }
}
