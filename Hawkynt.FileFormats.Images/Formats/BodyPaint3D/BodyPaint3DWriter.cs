using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.BodyPaint3D;

/// <summary>Writes a BodyPaint 3D texture as the tag stream the samples are.</summary>
/// <remarks>
/// The record nesting is the one every sample carries and is written in the same order: the
/// signature, a document record, the <c>BdTx</c> stating the size and the colour mode, and a
/// <c>BdVx</c> whose rectangle is the whole texture followed by one PackBits scanline per row per
/// channel, interleaved by channel. No layers: the samples that have them carry the same bitmap a
/// second time under a layer record, and writing a second copy of the picture would be filling in a
/// structure the document does not have.
/// <para/>
/// The colour mode is 2 for one channel and 4 for three, which is what the samples state for each.
/// The reader does not act on it — it takes the channel count from the bitmap record, which states
/// it outright — so writing it wrong would not show up here, and it is written from the samples for
/// the sake of the program that does read it.
/// </remarks>
public static class BodyPaint3DWriter {

  /// <summary>Class of the record every sample wraps the rest in.</summary>
  private const uint _ClassDocument = 0x000001F6;

  /// <summary>Subtypes the samples carry on the document, the texture header and the bitmap.</summary>
  private const uint _DocumentSubtype = 1, _TextureSubtype = 1, _BitmapSubtype = 2;

  /// <summary>What the texture header states for one channel and for three.</summary>
  private const int _GrayColorMode = 2, _RgbColorMode = 4;

  public static byte[] ToBytes(BodyPaint3DFile file) {
    var width = file.Width;
    var height = file.Height;
    if (width is < 1 or > BodyPaint3DFile.MaxDimension || height is < 1 or > BodyPaint3DFile.MaxDimension)
      throw new ArgumentException($"Invalid BodyPaint 3D texture size: {width}x{height}.", nameof(file));

    var planes = file.Planes;
    if (planes != BodyPaint3DFile.GrayPlanes && planes != BodyPaint3DFile.RgbPlanes)
      throw new ArgumentException($"A BodyPaint 3D texture carries one channel or three, not {planes}.", nameof(file));

    var pixels = file.PixelData ?? new byte[width * height * planes];
    if (pixels.Length < width * height * planes)
      throw new ArgumentException($"A {width}x{height} texture in {planes} channel(s) needs {width * height * planes} bytes and has {pixels.Length}.", nameof(file));

    using var output = new MemoryStream();
    output.Write(BodyPaint3DFile.Magic);

    _Begin(output, _ClassDocument, _DocumentSubtype);

    _Begin(output, BodyPaint3DFile.ClassTexture, _TextureSubtype);
    _Int32(output, width);
    _Int32(output, height);
    _Int32(output, planes == BodyPaint3DFile.GrayPlanes ? _GrayColorMode : _RgbColorMode);
    output.WriteByte(BodyPaint3DFile.TagEnd);

    _Begin(output, BodyPaint3DFile.ClassBitmap, _BitmapSubtype);
    _Int32(output, 0);
    _Int32(output, 0);
    _Int32(output, width);
    _Int32(output, height);
    _Int32(output, planes);

    var line = new byte[width];
    for (var y = 0; y < height; ++y)
      for (var channel = 0; channel < planes; ++channel) {
        var at = y * width * planes + channel;
        for (var x = 0; x < width; ++x, at += planes)
          line[x] = pixels[at];

        _Scanline(output, line);
      }

    output.WriteByte(BodyPaint3DFile.TagEnd);
    output.WriteByte(BodyPaint3DFile.TagEnd);

    return output.ToArray();
  }

  private static void _Begin(Stream output, uint klass, uint subtype) {
    output.WriteByte(BodyPaint3DFile.TagBegin);
    _UInt32(output, klass);
    _UInt32(output, subtype);
  }

  private static void _Int32(Stream output, int value) {
    output.WriteByte(BodyPaint3DFile.TagInt32);
    _UInt32(output, (uint)value);
  }

  private static void _Scanline(Stream output, ReadOnlySpan<byte> row) {
    var packed = PackBits.Pack(row);
    output.WriteByte(BodyPaint3DFile.TagScanline);
    output.WriteByte(BodyPaint3DFile.MethodPackBits);
    output.WriteByte(BodyPaint3DFile.TagByteArray);
    _UInt32(output, (uint)packed.Length);
    output.Write(packed);
  }

  private static void _UInt32(Stream output, uint value) {
    output.WriteByte((byte)(value >> 24));
    output.WriteByte((byte)(value >> 16));
    output.WriteByte((byte)(value >> 8));
    output.WriteByte((byte)value);
  }
}
