using System;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Core;
using FileFormat.Gif;
using FileFormat.Jpeg;
using FileFormat.Png;
using FileFormat.Wmf;

namespace FileFormat.EmbeddedPicture;

/// <summary>A picture lifted whole out of somebody else's file, decoded by whatever it turns out to be.</summary>
/// <remarks>
/// Drawing formats that are far too large to interpret often carry a thumbnail so a file chooser
/// has something to show, and they carry it as an ordinary picture file — a GIF in a Xara drawing,
/// a PNG or a Windows bitmap in an AutoCAD drawing. The container states where the run begins and
/// how long it is; what it holds is then a question its own first bytes answer, and this asks them
/// rather than trusting a type code the container may have got wrong.
/// </remarks>
internal static class EmbeddedPictureReader {

  /// <summary>Decodes a run of bytes as whichever picture format its signature names.</summary>
  /// <exception cref="InvalidDataException">The bytes open with nothing recognised.</exception>
  public static RawImage Decode(ReadOnlySpan<byte> data) {
    if (data.Length >= 8 && data[..8].SequenceEqual([(byte)0x89, (byte)'P', (byte)'N', (byte)'G', (byte)'\r', (byte)'\n', (byte)0x1A, (byte)'\n']))
      return PngFile.ToRawImage(PngReader.FromSpan(data));

    if (data.Length >= 6 && data[..3].SequenceEqual("GIF"u8))
      return GifFile.ToRawImage(GifReader.FromSpan(data));

    if (data.Length >= 4 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
      return JpegFile.ToRawImage(JpegReader.FromBytes(data.ToArray()));

    if (data.Length >= 2 && data[0] == (byte)'B' && data[1] == (byte)'M')
      return BmpFile.ToRawImage(BmpReader.FromSpan(data));

    // A metafile has no signature of its own beyond the placeable one, and the standard header
    // begins with a type of 1 or 2 and a header size of nine words.
    if (data.Length >= 18 && (data[0] | (data[1] << 8)) is 1 or 2 && (data[2] | (data[3] << 8)) == 9)
      return WmfFile.ToRawImage(WmfReader.FromSpan(data));

    if (data.Length >= 4 && data[0] == 0xD7 && data[1] == 0xCD && data[2] == 0xC6 && data[3] == 0x9A)
      return WmfFile.ToRawImage(WmfReader.FromSpan(data));

    throw new InvalidDataException("The embedded picture opens with no signature this can read.");
  }

  /// <summary>Whether a run of bytes opens with something <see cref="Decode"/> would take.</summary>
  public static bool IsRecognised(ReadOnlySpan<byte> data)
    => data.Length >= 8 && data[..8].SequenceEqual([(byte)0x89, (byte)'P', (byte)'N', (byte)'G', (byte)'\r', (byte)'\n', (byte)0x1A, (byte)'\n'])
       || data.Length >= 6 && data[..3].SequenceEqual("GIF"u8)
       || data.Length >= 4 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF
       || data.Length >= 2 && data[0] == (byte)'B' && data[1] == (byte)'M'
       || data.Length >= 18 && (data[0] | (data[1] << 8)) is 1 or 2 && (data[2] | (data[3] << 8)) == 9
       || data.Length >= 4 && data[0] == 0xD7 && data[1] == 0xCD && data[2] == 0xC6 && data[3] == 0x9A;
}
