using System;
using System.Buffers.Binary;
using FileFormat.Core;
using FileFormat.EmbeddedDib;

namespace FileFormat.Cmx.Tests;

[TestFixture]
public sealed class CmxReaderTests {

  [TestCase((byte)'1')]
  [TestCase((byte)'2')]
  [Category("Unit")]
  public void MatchesSignature_UsesPronomAsciiVersionMarker(byte version) {
    var data = new byte[75];
    _Ascii4("RIFF", data);
    _Ascii4("CMX1", data.AsSpan(8));
    data[74] = version;

    Assert.That(CmxReader.MatchesSignature(data), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void MatchesSignature_BinaryPrecisionMarkerFromOldReader_IsNotCmxSignature() {
    var data = new byte[75];
    _Ascii4("RIFF", data);
    _Ascii4("CMX1", data.AsSpan(8));
    data[74] = 1;

    Assert.That(CmxReader.MatchesSignature(data), Is.False);
  }

  [TestCase(false, 1, 2, 16)]
  [TestCase(true, 2, 4, 32)]
  [Category("Unit")]
  public void FromSpan_ContHeaderAndReferencedDisp_AreReadStructurally(
    bool bigEndian,
    int version,
    int coordinateBytes,
    int precision
  ) {
    var expected = _Image(17);
    var decoy = _Image(201);
    var data = _Cmx(bigEndian, version, coordinateBytes, expected, decoy);

    var file = CmxReader.FromSpan(data);
    var actualRgba = file.Preview.ToRgba32();
    var expectedRgba = expected.ToRgba32();

    Assert.Multiple(() => {
      Assert.That(file.IsBigEndian, Is.EqualTo(bigEndian));
      Assert.That(file.InternalVersion, Is.EqualTo(version));
      Assert.That(file.CoordinatePrecisionBits, Is.EqualTo(precision));
      Assert.That((file.Preview.Width, file.Preview.Height), Is.EqualTo((expected.Width, expected.Height)));
      Assert.That(actualRgba, Is.EqualTo(expectedRgba), "the reader followed a decoy DIB instead of the DISP offset");
      Assert.That(file.PreviewOffset, Is.GreaterThan(20));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromSpan_ThumbnailOffsetThatDoesNotPointToDisp_IsRefused() {
    var data = _Cmx(false, 1, 2, _Image(17));
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(112), 12);

    Assert.That(() => CmxReader.FromSpan(data), Throws.TypeOf<System.IO.InvalidDataException>());
  }

  [Test]
  [Category("Unit")]
  public void FromSpan_ByteOrderMarkerDisagreeingWithRiff_IsRefused() {
    var data = _Cmx(false, 1, 2, _Image(17));
    data[68] = (byte)'4'; // cont payload offset 48, absolute 20 + 48

    Assert.That(() => CmxReader.FromSpan(data), Throws.TypeOf<System.IO.InvalidDataException>());
  }

  [Test]
  [Category("Unit")]
  public void FromSpan_WrongOuterContainer_IsRefusedEvenWithCmxForm() {
    var data = _Cmx(false, 1, 2, _Image(17));
    data[0] = (byte)'N';

    Assert.That(() => CmxReader.FromSpan(data), Throws.TypeOf<System.IO.InvalidDataException>());
  }

  private static byte[] _Cmx(
    bool bigEndian,
    int version,
    int coordinateBytes,
    RawImage image,
    RawImage? decoy = null
  ) {
    var header = new byte[coordinateBytes == 2 ? 104 : 112];
    _AsciiPadded("Corel Binary Metafile", header.AsSpan(0, 32));
    _AsciiPadded("Windows", header.AsSpan(32, 16));
    _AsciiPadded(bigEndian ? "4" : "2", header.AsSpan(48, 4));
    _AsciiPadded(coordinateBytes.ToString(), header.AsSpan(52, 2));
    _AsciiPadded(version.ToString(), header.AsSpan(54, 4));
    _AsciiPadded("0", header.AsSpan(58, 4));
    _WriteUInt32(header.AsSpan(84), uint.MaxValue, bigEndian);
    _WriteUInt32(header.AsSpan(88), uint.MaxValue, bigEndian);

    var decoyChunk = decoy is null ? [] : _Chunk("JUNK", EmbeddedDibWriter.ToBytes(decoy), bigEndian);
    var dib = EmbeddedDibWriter.ToBytes(image);
    var displayPayload = new byte[4 + dib.Length];
    dib.CopyTo(displayPayload.AsSpan(4));

    var headerChunkSize = _ChunkSize(header.Length);
    var displayOffset = 12 + headerChunkSize + decoyChunk.Length;
    _WriteUInt32(header.AsSpan(92), checked((uint)displayOffset), bigEndian);

    var headerChunk = _Chunk("cont", header, bigEndian);
    var displayChunk = _Chunk("DISP", displayPayload, bigEndian);
    var result = new byte[12 + headerChunk.Length + decoyChunk.Length + displayChunk.Length];
    _Ascii4(bigEndian ? "RIFX" : "RIFF", result);
    _WriteUInt32(result.AsSpan(4), checked((uint)(result.Length - 8)), bigEndian);
    _Ascii4("CMX1", result.AsSpan(8));

    var at = 12;
    headerChunk.CopyTo(result, at);
    at += headerChunk.Length;
    decoyChunk.CopyTo(result, at);
    at += decoyChunk.Length;
    displayChunk.CopyTo(result, at);
    return result;
  }

  private static byte[] _Chunk(string id, ReadOnlySpan<byte> payload, bool bigEndian) {
    var result = new byte[_ChunkSize(payload.Length)];
    _Ascii4(id, result);
    _WriteUInt32(result.AsSpan(4), checked((uint)payload.Length), bigEndian);
    payload.CopyTo(result.AsSpan(8));
    return result;
  }

  private static int _ChunkSize(int payloadLength) => 8 + payloadLength + (payloadLength & 1);

  private static RawImage _Image(byte seed) => new() {
    Width = 2,
    Height = 2,
    Format = PixelFormat.Bgr24,
    PixelData = [
      seed, (byte)(seed + 1), (byte)(seed + 2),
      (byte)(seed + 3), (byte)(seed + 4), (byte)(seed + 5),
      (byte)(seed + 6), (byte)(seed + 7), (byte)(seed + 8),
      (byte)(seed + 9), (byte)(seed + 10), (byte)(seed + 11),
    ],
  };

  private static void _WriteUInt32(Span<byte> destination, uint value, bool bigEndian) {
    if (bigEndian)
      BinaryPrimitives.WriteUInt32BigEndian(destination, value);
    else
      BinaryPrimitives.WriteUInt32LittleEndian(destination, value);
  }

  private static void _Ascii4(string value, Span<byte> destination) {
    for (var i = 0; i < 4; ++i)
      destination[i] = checked((byte)value[i]);
  }

  private static void _AsciiPadded(string value, Span<byte> destination) {
    destination.Clear();
    for (var i = 0; i < Math.Min(value.Length, destination.Length); ++i)
      destination[i] = checked((byte)value[i]);
  }
}
