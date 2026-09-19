extern alias Images;

using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using FileFormat.Core;
using FileFormat.RoqVideo;
using JpegFile = Images::FileFormat.Jpeg.JpegFile;
using JpegWriter = Images::FileFormat.Jpeg.JpegWriter;

namespace FileFormat.Codecs.Tests;

[TestFixture]
public sealed class RoqCompatibilityTests {
  private const ushort _InfoId = 0x1001;
  private const ushort _CodebookId = 0x1002;
  private const ushort _VqId = 0x1011;
  private const ushort _JpegId = 0x1012;
  private const ushort _HangId = 0x1013;
  private const ushort _PacketId = 0x1030;

  [Test]
  [Category("Unit")]
  public void PartialCodebookUpdatePreservesOlderTailEntries() {
    var decoder = RoqVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _Info(16, 16)), out _);
    decoder.TryDecode(new(0, _Codebook(
      [_Cell(20), _Cell(90)],
      [[0, 0, 0, 0], [1, 1, 1, 1]])), out _);

    // Overwrite only index zero. Index one remains valid and is used by the following picture.
    decoder.TryDecode(new(0, _Codebook([_Cell(40)], [[0, 0, 0, 0]])), out _);
    Assert.That(decoder.TryDecode(new(0, _SldFrame(1, 1, 1, 1)), out var picture), Is.True);
    Assert.That(picture.PixelData[0], Is.EqualTo(90));
  }

  [Test]
  [Category("Unit")]
  public void AlphaCodebookProducesRgbaAndKeepsFourIndependentAlphaSamples() {
    var decoder = RoqVideoDecoder.Create(_Stream(1, true));
    decoder.TryDecode(new(0, _Info(16, 16, 1)), out _);
    decoder.TryDecode(new(0, _Codebook(
      [[100, 10, 100, 20, 100, 30, 100, 40, 128, 128]],
      [[0, 0, 0, 0]])), out _);

    Assert.That(decoder.TryDecode(new(0, _SldFrame(0, 0, 0, 0)), out var picture), Is.True);
    Assert.That(picture.Format, Is.EqualTo(PixelFormat.Rgba32));
    Assert.That(_Alpha(picture, 0, 0), Is.EqualTo(10));
    Assert.That(_Alpha(picture, 2, 0), Is.EqualTo(20));
    Assert.That(_Alpha(picture, 0, 2), Is.EqualTo(30));
    Assert.That(_Alpha(picture, 2, 2), Is.EqualTo(40));
  }

  [Test]
  [Category("Unit")]
  public void HangRepeatsDisplayWithoutAdvancingPredictionBuffers() {
    var decoder = RoqVideoDecoder.Create(_Stream(1, true));
    decoder.TryDecode(new(0, _Info(16, 16)), out _);
    decoder.TryDecode(new(0, _Codebook([_Cell(40)], [[0, 0, 0, 0]])), out _);
    decoder.TryDecode(new(0, _SldFrame(0, 0, 0, 0)), out var first);

    Assert.That(decoder.TryDecode(new(0, _Chunk(_HangId, 0, [])), out var repeated), Is.True);
    Assert.That(repeated.PixelData, Is.EqualTo(first.PixelData));

    decoder.TryDecode(new(0, _Codebook([_Cell(90)], [[0, 0, 0, 0]])), out _);
    decoder.TryDecode(new(0, _SldFrame(0, 0, 0, 0)), out _);
    Assert.That(decoder.TryDecode(new(0, _MotFrame()), out var afterHang), Is.True);
    Assert.That(afterHang.PixelData[0], Is.EqualTo(40), "HANG must not consume the old target buffer");
  }

  [Test]
  [Category("Unit")]
  public void ZeroSizedSignatureDoublesMotionOffsets() {
    var decoder = RoqVideoDecoder.Create(_Stream(2, true));
    decoder.TryDecode(new(0, _Info(16, 16)), out _);
    decoder.TryDecode(new(0, _Codebook(
      [_Cell(30), _Cell(120)],
      [[0, 0, 0, 0], [1, 1, 1, 1]])), out _);
    decoder.TryDecode(new(0, _SldFrame(0, 1, 0, 1)), out _);

    // Top-right 8x8 FCC with encoded dx=4. The old signature scales it to 8, so it copies the
    // entire top-left low-luma block. With ordinary scale this block would straddle low and high.
    var body = new byte[] { 0x00, 0x10, 0xC8 };
    decoder.TryDecode(new(0, _Chunk(_VqId, 0, body)), out var picture);
    Assert.That(_Red(picture, 15, 0), Is.EqualTo(30));
  }

  [Test]
  [Category("Unit")]
  public void OneEncodedPicturePacketMayContainInfoCodebookAndVqChunks() {
    var decoder = RoqVideoDecoder.Create(_Stream());
    var packet = _Concat(
      _Info(16, 16),
      _Codebook([_Cell(77)], [[0, 0, 0, 0]]),
      _SldFrame(0, 0, 0, 0));

    Assert.That(decoder.TryDecode(new(0, packet), out var picture), Is.True);
    Assert.That(picture.PixelData[0], Is.EqualTo(77));
  }

  [Test]
  [Category("Unit")]
  public void JpegChunkIsAnIntraPictureInExtendedProfile() {
    var raw = new RawImage {
      Width = 16,
      Height = 16,
      Format = PixelFormat.Rgb24,
      PixelData = Enumerable.Repeat((byte)64, 16 * 16 * 3).ToArray(),
    };
    var jpeg = JpegWriter.ToBytes(JpegFile.FromRawImage(raw));
    var decoder = RoqVideoDecoder.Create(_Stream(1, true));
    decoder.TryDecode(new(0, _Info(16, 16)), out _);

    Assert.That(decoder.TryDecode(new(0, _Chunk(_JpegId, 0, jpeg)), out var picture), Is.True);
    Assert.That(picture.Width, Is.EqualTo(16));
    Assert.That(picture.Height, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void ChunkPayloadLengthPastPacketEndIsRejectedBeforeSlicing() {
    var malformed = new byte[8];
    BinaryPrimitives.WriteUInt16LittleEndian(malformed, _InfoId);
    BinaryPrimitives.WriteUInt32LittleEndian(malformed.AsSpan(2), 100);
    var decoder = RoqVideoDecoder.Create(_Stream());

    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, malformed), out _));
  }

  [Test]
  [Category("Unit")]
  public void ExtendedSignatureAndPacketWrapperArePreservedByDemuxState() {
    var info = _Info(16, 16);
    var vq = _MotFrame();
    var hang = _Chunk(_HangId, 0, []);
    var nested = _Concat(info, vq, hang);
    var wrapper = _Chunk(_PacketId, 0, []);
    BinaryPrimitives.WriteUInt32LittleEndian(wrapper.AsSpan(2), (uint)nested.Length);
    var signature = new byte[] { 0x84, 0x10, 0, 0, 0, 0, 0, 0 };
    var file = _Concat(signature, wrapper, nested);

    var container = RoqContainer.FromBytes(file);
    var stream = RoqContainer.Streams(container)[0];
    var pictures = RoqContainer.ReadPackets(container).Where(p => p.PresentationTimestamp.HasValue).ToArray();

    Assert.That(container.FrameRate, Is.EqualTo(30));
    Assert.That(container.MotionScale, Is.EqualTo(2));
    Assert.That(stream.CodecPrivateData.ToArray(), Is.EqualTo(new byte[] { 2, 1 }));
    Assert.That(stream.DeclaredFrameCount, Is.EqualTo(2));
    Assert.That(pictures, Has.Length.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void StandardSignatureArgumentIsTheDeclaredFrameRate() {
    var signature = new byte[] { 0x84, 0x10, 0xFF, 0xFF, 0xFF, 0xFF, 24, 0 };
    var container = RoqContainer.FromBytes(_Concat(signature, _Info(16, 16)));
    var stream = RoqContainer.Streams(container)[0];

    Assert.That(container.FrameRate, Is.EqualTo(24));
    Assert.That(stream.FrameRate, Is.EqualTo(new Rational(24, 1)));
    Assert.That(stream.TimeBase, Is.EqualTo(new Rational(1, 24)));
  }

  private static MediaStreamInfo _Stream(byte motionScale = 1, bool extended = false) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("RoQV"),
    CodecPrivateData = extended ? new byte[] { motionScale, 1 } : ReadOnlyMemory<byte>.Empty,
  };

  private static byte[] _Info(int width, int height, ushort argument = 0) {
    var payload = new byte[8];
    BinaryPrimitives.WriteUInt16LittleEndian(payload, (ushort)width);
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2), (ushort)height);
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4), 8);
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(6), 4);
    return _Chunk(_InfoId, argument, payload);
  }

  private static byte[] _Cell(byte luma) => [luma, luma, luma, luma, 128, 128];

  private static byte[] _Codebook(byte[][] cb2, byte[][] cb4) {
    var length = cb2.Sum(x => x.Length) + cb4.Sum(x => x.Length);
    var payload = new byte[length];
    var at = 0;
    foreach (var cell in cb2) { cell.CopyTo(payload, at); at += cell.Length; }
    foreach (var cell in cb4) { cell.CopyTo(payload, at); at += cell.Length; }
    var argument = (ushort)(((cb2.Length & 0xFF) << 8) | (cb4.Length & 0xFF));
    return _Chunk(_CodebookId, argument, payload);
  }

  private static byte[] _SldFrame(byte q0, byte q1, byte q2, byte q3)
    => _Chunk(_VqId, 0, [0x00, 0xAA, q0, q1, q2, q3]);

  private static byte[] _MotFrame() => _Chunk(_VqId, 0, [0x00, 0x00]);

  private static byte[] _Chunk(ushort id, ushort argument, byte[] payload) {
    var chunk = new byte[8 + payload.Length];
    BinaryPrimitives.WriteUInt16LittleEndian(chunk, id);
    BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(2), (uint)payload.Length);
    BinaryPrimitives.WriteUInt16LittleEndian(chunk.AsSpan(6), argument);
    payload.CopyTo(chunk, 8);
    return chunk;
  }

  private static byte[] _Concat(params byte[][] pieces) {
    var result = new byte[pieces.Sum(p => p.Length)];
    var at = 0;
    foreach (var piece in pieces) { piece.CopyTo(result, at); at += piece.Length; }
    return result;
  }

  private static byte _Alpha(RawImage image, int x, int y) => image.PixelData[(y * image.Width + x) * 4 + 3];
  private static byte _Red(RawImage image, int x, int y) => image.PixelData[(y * image.Width + x) * (image.Format == PixelFormat.Rgba32 ? 4 : 3)];
}
