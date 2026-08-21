using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.FlicVideo.Tests;

/// <summary>
/// The FLIC container's demuxing behaviour: where a frame chunk begins and ends, and the two places
/// that is not "128 bytes in, then every <c>size</c> field in turn."
/// </summary>
/// <remarks>
/// The pixel arithmetic is not tested here at all — <see cref="Codecs.Tests.FlicVideoDecoderTests"/>
/// covers every opcode, and eleven files pulled from ffmpeg's own <c>fli-flc</c> sample corpus were
/// compared frame by frame against ffmpeg's decode of the same files with no differing sample
/// anywhere, across both magic numbers, every documented chunk type but <c>COPY</c> and <c>BLACK</c>
/// (no sample reachable carries either), 250- and 384-frame chains with no drift, and a genuine
/// postage-stamp thumbnail. What is worth a hand-built fixture is what a real file's own shape cannot
/// exercise on demand: a corrupted frame magic, a header whose <c>oframe1</c> points past an
/// undocumented prefix chunk, and the ring frame a real file always happens to have exactly one of.
/// </remarks>
[TestFixture]
public sealed class FliReaderTests {

  private const ushort _MAGIC_FLI = 0xAF11;
  private const ushort _MAGIC_FLC = 0xAF12;

  // ============================================================================================
  // The header
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AnUnrecognisedMagicIsRefusedByName() {
    var file = _Fli(0xAF44, 4, 4, 1, [_Frame([_Brun(4, 4, 1)])]);

    var failure = Assert.Throws<NotSupportedException>(() => FliContainer.FromBytes(file));
    Assert.That(failure!.Message, Does.Contain("0xAF44"));
  }

  [Test]
  [Category("Unit")]
  public void ADepthOtherThanEightIsRefused() {
    var file = _Fli(_MAGIC_FLC, 4, 4, 1, [_Frame([_Brun(4, 4, 1)])], depth: 24);

    var failure = Assert.Throws<NotSupportedException>(() => FliContainer.FromBytes(file));
    Assert.That(failure!.Message, Does.Contain("24 bits per pixel"));
  }

  [Test]
  [Category("Unit")]
  public void AZeroWidthOrHeightIsRefused() {
    var file = _Fli(_MAGIC_FLC, 0, 4, 1, []);

    var failure = Assert.Throws<InvalidOperationException>(() => FliContainer.FromBytes(file));
    Assert.That(failure!.Message, Does.Contain("has no pixels"));
  }

  [Test]
  [Category("Unit")]
  public void AFileShorterThanTheHeaderIsRefused()
    => Assert.Throws<InvalidDataException>(() => FliContainer.FromBytes(new byte[40]));

  // ============================================================================================
  // The ring frame
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheRingFrameIsNotHandedOutAsAPacket() {
    // The header declares one frame; the file carries two chunks. A reader that walked every chunk
    // the data holds rather than stopping at the declared count would hand the ring frame out as an
    // ordinary second frame of the film.
    var realFrame = _Frame([_Brun(4, 2, 5)]);
    var ringFrame = _Frame([_Brun(4, 2, 9)]);
    var file = _Fli(_MAGIC_FLC, 4, 2, declaredFrames: 1, [realFrame, ringFrame]);

    var container = FliContainer.FromBytes(file);
    var packets = FliContainer.ReadPackets(container).ToList();

    Assert.That(packets, Has.Count.EqualTo(1));

    var decoded = _DecodeAll(container).Single();
    Assert.That(decoded.PixelData, Is.All.EqualTo((byte)5), "the ring frame's colour must not appear anywhere");
  }

  [Test]
  [Category("Unit")]
  public void MoreDeclaredFramesThanTheFileHoldsIsRefused() {
    var file = _Fli(_MAGIC_FLC, 4, 2, declaredFrames: 3, [_Frame([_Brun(4, 2, 5)])]);
    var container = FliContainer.FromBytes(file);

    var failure = Assert.Throws<InvalidDataException>(() => FliContainer.ReadPackets(container).ToList());
    Assert.That(failure!.Message, Does.Contain("promises more frames"));
  }

  // ============================================================================================
  // oframe1 and the prefix chunk
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void OFrame1SkipsAnUndocumentedPrefixChunkAheadOfFrameOne() {
    // Autodesk's own PREFIX_TYPE (0xF100) is undocumented and never interpreted here — oframe1 says
    // where frame one actually starts, and the bytes ahead of it are never walked as a frame chunk at
    // all. A reader that assumed "right after the header" would try to read the prefix as one and
    // choke on its magic.
    var prefix = new byte[24];
    BinaryPrimitives.WriteUInt32LittleEndian(prefix, 24);
    BinaryPrimitives.WriteUInt16LittleEndian(prefix.AsSpan(4), 0xF100);
    // The rest is left as zeroes, which is not a valid FRAME_TYPE magic — proof this is never parsed
    // as a frame, since doing so would throw.

    var frame = _Frame([_Brun(4, 2, 7)]);
    var header = _Header(_MAGIC_FLC, 4, 2, declaredFrames: 1, oframe1: (uint)(FliReader.HEADER_SIZE + prefix.Length));
    var file = _Concat(header, prefix, frame);

    var container = FliContainer.FromBytes(file);
    Assert.That(container.FirstFrameOffset, Is.EqualTo(FliReader.HEADER_SIZE + prefix.Length));

    var decoded = _DecodeAll(container).Single();
    Assert.That(decoded.PixelData, Is.All.EqualTo((byte)7));
  }

  [Test]
  [Category("Unit")]
  public void AnFliMagicFileHasNoOFrame1AndStartsRightAfterTheHeader() {
    var file = _Fli(_MAGIC_FLI, 4, 2, declaredFrames: 1, [_Frame([_Brun(4, 2, 3)])]);
    var container = FliContainer.FromBytes(file);

    Assert.That(container.FirstFrameOffset, Is.EqualTo(FliReader.HEADER_SIZE));
  }

  // ============================================================================================
  // A corrupted frame chunk
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ACorruptedFrameMagicIsRefusedByName() {
    var good = _Frame([_Brun(4, 2, 1)]);
    var bad = _Frame([_Brun(4, 2, 2)]);
    // Flip one bit of the second frame's magic — the same single-nibble corruption ffmpeg's own
    // fli-flc/fli-bugs/malev2.fli sample carries at its twenty-first frame chunk.
    bad[4] ^= 0x04;

    var file = _Fli(_MAGIC_FLC, 4, 2, declaredFrames: 2, [good, bad]);
    var container = FliContainer.FromBytes(file);

    var failure = Assert.Throws<InvalidDataException>(() => FliContainer.ReadPackets(container).ToList());
    Assert.That(failure!.Message, Does.Contain("Frame 1"));
  }

  [Test]
  [Category("Unit")]
  public void APictureSizeOverrideMidstreamIsRefused() {
    var frame = _Frame([_Brun(4, 2, 1)]);
    BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(12), 8); // width override

    var file = _Fli(_MAGIC_FLC, 4, 2, declaredFrames: 1, [frame]);
    var container = FliContainer.FromBytes(file);

    var failure = Assert.Throws<NotSupportedException>(() => FliContainer.ReadPackets(container).ToList());
    Assert.That(failure!.Message, Does.Contain("size override"));
  }

  // ============================================================================================
  // The stream this container declares
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheStreamIsDeclaredAsFlic() {
    var file = _Fli(_MAGIC_FLC, 4, 2, declaredFrames: 1, [_Frame([_Brun(4, 2, 1)])]);
    var streams = FliContainer.Streams(FliContainer.FromBytes(file));

    Assert.That(streams, Has.Count.EqualTo(1));
    Assert.That(streams[0].Kind, Is.EqualTo(MediaStreamKind.Video));
    Assert.That(streams[0].Codec.ToString(), Is.EqualTo("FLIC"));
    Assert.That(streams[0].Width, Is.EqualTo(4));
    Assert.That(streams[0].Height, Is.EqualTo(2));
    Assert.That(streams[0].BitsPerPixel, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void FliMagicUsesA1By70SecondTimeBaseAndFlcUsesMilliseconds() {
    var fli = _Fli(_MAGIC_FLI, 4, 2, declaredFrames: 1, [_Frame([_Brun(4, 2, 1)])], speed: 7);
    var flc = _Fli(_MAGIC_FLC, 4, 2, declaredFrames: 1, [_Frame([_Brun(4, 2, 1)])], speed: 7);

    Assert.That(FliContainer.Streams(FliContainer.FromBytes(fli))[0].TimeBase, Is.EqualTo(new Rational(1, 70)));
    Assert.That(FliContainer.Streams(FliContainer.FromBytes(flc))[0].TimeBase, Is.EqualTo(new Rational(1, 1000)));
  }

  // ============================================================================================
  // Fixtures
  // ============================================================================================

  private static byte[] _Header(
    ushort magic, int width, int height, ushort declaredFrames, uint speed = 0, uint oframe1 = 0, ushort depth = 8) {
    var header = new byte[FliReader.HEADER_SIZE];
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), magic);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6), declaredFrames);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(8), (ushort)width);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(10), (ushort)height);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(12), depth);
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16), speed);
    if (magic == _MAGIC_FLC)
      BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(80), oframe1);

    return header;
  }

  private static byte[] _Fli(
    ushort magic, int width, int height, ushort declaredFrames, IReadOnlyList<byte[]> frames,
    uint speed = 0, ushort depth = 8) {
    var header = _Header(magic, width, height, declaredFrames, speed, oframe1: 0, depth: depth);
    return _Concat([header, .. frames]);
  }

  /// <summary>One <c>FRAME_TYPE</c> chunk: the sixteen-byte frame header, then the sub-chunks concatenated.</summary>
  private static byte[] _Frame(IReadOnlyList<byte[]> subChunks) {
    var size = 16 + subChunks.Sum(c => c.Length);
    var frame = new byte[size];
    BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)size);
    BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4), 0xF1FA);
    BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(6), (ushort)subChunks.Count);

    var at = 16;
    foreach (var chunk in subChunks) {
      chunk.CopyTo(frame, at);
      at += chunk.Length;
    }

    return frame;
  }

  /// <summary>A <c>FLI_BRUN</c> sub-chunk painting every row of a picture the same solid colour.</summary>
  private static byte[] _Brun(int width, int height, byte colour) {
    var rows = new List<byte>();
    for (var y = 0; y < height; ++y) {
      rows.Add(1); // packet count, ignored by every reader
      rows.Add((byte)width); // positive count: replicate the next byte
      rows.Add(colour);
    }

    var payload = rows.ToArray();
    var chunk = new byte[6 + payload.Length];
    BinaryPrimitives.WriteUInt32LittleEndian(chunk, (uint)chunk.Length);
    BinaryPrimitives.WriteUInt16LittleEndian(chunk.AsSpan(4), 15);
    payload.CopyTo(chunk, 6);

    return chunk;
  }

  private static byte[] _Concat(params byte[][] parts) {
    var result = new byte[parts.Sum(p => p.Length)];
    var at = 0;
    foreach (var part in parts) {
      part.CopyTo(result, at);
      at += part.Length;
    }

    return result;
  }

  private static IReadOnlyList<RawImage> _DecodeAll(FliContainer container) {
    var stream = FliContainer.Streams(container)[0];
    return VideoIO.Decode(FliContainer.ReadPackets(container), stream, VideoFormatRegistry.CreateDecoder)
      .Select(f => f.Image)
      .ToList();
  }
}
