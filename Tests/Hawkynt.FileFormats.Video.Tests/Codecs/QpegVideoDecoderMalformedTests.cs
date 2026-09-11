using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs.Tests;

/// <summary>Malformed QPEG cases whose failure must happen before a partial picture is accepted.</summary>
[TestFixture]
public sealed class QpegVideoDecoderMalformedTests {

  [Test]
  [Category("Unit")]
  public void AMotionBlockThatCrossesThePictureEdgeIsRefusedRatherThanClipped() {
    var decoder = QpegVideoDecoder.Create(_Stream(8, 8, 2));
    decoder.TryDecode(new(0, _Frame(0x10, _Literal(new byte[64]))), out _);

    // Move the coded cursor to x=6, then ask for an 8x8 block (dimension index 12). The old decoder
    // copied the two columns that happened to fit and silently dropped the other six.
    var payload = new byte[] {
      0x86,       // skip six pixels
      0xFC, 0x00, // 8x8 motion block, vector 0,0
      0xBA,       // skip the remaining 58 pixels
    };

    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, _Frame(0x01, payload)), out _));
  }

  [Test]
  [Category("Unit")]
  public void AMotionVectorWhoseSourceCrossesThePictureEdgeIsRefused() {
    var decoder = QpegVideoDecoder.Create(_Stream(8, 8, 2));
    decoder.TryDecode(new(0, _Frame(0x10, _Literal(new byte[64]))), out _);

    // A 4x4 block at the lower-left with vector -1,0 reaches one column left of the previous picture.
    var payload = new byte[] {
      0xFF, 0xF0,
      0xBF, 0x00,
    };

    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, _Frame(0x01, payload)), out _));
  }

  [Test]
  [Category("Unit")]
  public void MoreThan256PaletteEntriesAreRefused() {
    var format = new byte[40 + 257 * 4];
    BinaryPrimitives.WriteInt32LittleEndian(format, 40);
    BinaryPrimitives.WriteInt32LittleEndian(format.AsSpan(4), 1);
    BinaryPrimitives.WriteInt32LittleEndian(format.AsSpan(8), 1);
    BinaryPrimitives.WriteInt32LittleEndian(format.AsSpan(32), 257);
    var stream = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("QPEG"),
      Width = 1,
      Height = 1,
      CodecPrivateData = format,
    };

    Assert.Throws<InvalidDataException>(() => QpegVideoDecoder.Create(stream));
  }

  private static MediaStreamInfo _Stream(int width, int height, int paletteEntries) {
    var format = new byte[40 + paletteEntries * 4];
    BinaryPrimitives.WriteInt32LittleEndian(format, 40);
    BinaryPrimitives.WriteInt32LittleEndian(format.AsSpan(4), width);
    BinaryPrimitives.WriteInt32LittleEndian(format.AsSpan(8), height);
    BinaryPrimitives.WriteInt32LittleEndian(format.AsSpan(32), paletteEntries);

    return new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("QPEG"),
      Width = width,
      Height = height,
      CodecPrivateData = format,
    };
  }

  private static byte[] _Frame(byte frameType, byte[] payload) {
    var frame = new byte[134 + payload.Length];
    BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)frame.Length);
    frame[132] = 0xE0;
    frame[133] = frameType;
    payload.CopyTo(frame, 134);
    return frame;
  }

  private static byte[] _Literal(byte[] pixels) {
    using var output = new MemoryStream();
    var at = 0;
    while (at < pixels.Length) {
      var length = System.Math.Min(128, pixels.Length - at);
      output.WriteByte((byte)(length - 1));
      output.Write(pixels, at, length);
      at += length;
    }

    return output.ToArray();
  }
}
