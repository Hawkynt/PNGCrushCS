using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Jpeg;
using FileFormat.Png;

namespace FileFormat.Wrappers;

/// <summary>
/// Finds and decodes the ordinary picture inside a container that only wraps one.
/// </summary>
/// <remarks>
/// A surprising number of formats are a name, some bookkeeping, and then a plain JPEG or PNG. They
/// decode perfectly the moment the wrapper is stepped over, and every one of them was refused before
/// simply because nothing knew to look inside.
/// <para/>
/// Where the picture starts is not fixed even within one format — Photo Line puts it at 70 in one
/// sample and 72 in another, Photo Studio at 128 and 136 — so it is found rather than assumed.
/// </remarks>
internal static class WrappedPicture {

  private static ReadOnlySpan<byte> _PngSignature => [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

  /// <summary>Where the first JPEG or PNG in the file begins, or -1 if there is none.</summary>
  internal static int Find(ReadOnlySpan<byte> data, out bool isPng) {
    var png = data.IndexOf(_PngSignature);
    var jpeg = _FindJpeg(data);

    isPng = png >= 0 && (jpeg < 0 || png < jpeg);
    return isPng ? png : jpeg;
  }

  private static int _FindJpeg(ReadOnlySpan<byte> data) {
    for (var at = 0; at + 2 < data.Length; ++at)
      if (data[at] == 0xFF && data[at + 1] == 0xD8 && data[at + 2] == 0xFF)
        return at;

    return -1;
  }

  /// <summary>Decodes the picture the wrapper carries, given where it starts.</summary>
  internal static RawImage Decode(byte[] embedded, bool isPng) {
    ArgumentNullException.ThrowIfNull(embedded);

    return isPng
      ? PngFile.ToRawImage(PngReader.FromBytes(embedded))
      : JpegFile.ToRawImage(JpegReader.FromBytes(embedded));
  }

  /// <summary>Takes the picture out of a file, having checked it opens the way the format does.</summary>
  internal static (byte[] Embedded, bool IsPng) Extract(ReadOnlySpan<byte> data, ReadOnlySpan<byte> magic, string formatName) {
    if (data.Length <= magic.Length)
      throw new InvalidDataException($"Data too small for {formatName} (got {data.Length} bytes).");

    if (!data[..magic.Length].SequenceEqual(magic))
      throw new InvalidDataException($"Not {formatName}: it does not open the way one does.");

    var at = Find(data, out var isPng);
    if (at < 0)
      throw new InvalidDataException($"{formatName} carries an ordinary picture inside it; this file has none.");

    return (data[at..].ToArray(), isPng);
  }
}
