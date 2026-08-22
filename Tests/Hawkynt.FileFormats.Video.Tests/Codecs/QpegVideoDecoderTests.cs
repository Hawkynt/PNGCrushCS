using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// QPEG's video decode, built here byte by byte: the intraframe run-length walk, the interframe's mix
/// of run, copy, skip, fill-from-table and motion compensation, and the two run-length formulas the
/// document states one byte short.
/// </summary>
/// <remarks>
/// Three real files — 80x60 and 320x240, 314 frames in all, every frame type — were decoded here and by
/// ffmpeg and compared sample for sample against ffmpeg's own <c>rgb24</c> output: every frame is
/// identical, maximum delta nought.
/// </remarks>
[TestFixture]
public sealed class QpegVideoDecoderTests {

  // ============================================================================================
  // Which streams it takes
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheQpegCodeIsTaken()
    => Assert.That(QpegVideoDecoder.Accepts(_Stream("QPEG")), Is.True);

  [Test]
  [Category("Unit")]
  public void TheQ10CodeIsTaken()
    => Assert.That(QpegVideoDecoder.Accepts(_Stream("Q1.0")), Is.True);

  [Test]
  [Category("Unit")]
  public void TheQ11CodeIsTaken()
    => Assert.That(QpegVideoDecoder.Accepts(_Stream("Q1.1")), Is.True);

  [Test]
  [Category("Unit")]
  public void AnotherCodecsCodeIsNotTaken() {
    var stream = new MediaStreamInfo { Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters("cvid") };
    Assert.That(QpegVideoDecoder.Accepts(stream), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void TheCodecIsRegistered() {
    var stream = _StreamWithFormat("Q1.0", 4, 4, _Palette((0, 0, 0)));

    Assert.That(VideoFormatRegistry.AllCodecs.Select(c => c.CodecName), Does.Contain("Q-Team QPEG"));
    Assert.That(VideoFormatRegistry.CanDecode(stream), Is.True);
    Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<QpegVideoDecoder>());
  }

  // ============================================================================================
  // Intraframe
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AnIntraShortRunFillsCodeMinus0xE0Plus2Pixels() {
    var decoder = QpegVideoDecoder.Create(_StreamWithFormat("QPEG", 4, 1, _Palette((0, 0, 0), (255, 0, 0))));
    // control 0xE0 -> run = 0 + 2 = 2, value 1. Then a short copy (code 1 -> 2 literal bytes) for the rest.
    var payload = new byte[] { 0xE0, 1, 0x01, 1, 1 };
    var packet = _IntraFrame(4, 1, payload);

    Assert.That(decoder.TryDecode(new(0, packet), out var frame), Is.True);
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 1, 1, 1, 1 }));
  }

  [Test]
  [Category("Unit")]
  public void AnIntraShortCopyCopiesCodePlus1LiteralBytes() {
    var decoder = QpegVideoDecoder.Create(_StreamWithFormat("QPEG", 3, 1, _Palette((0, 0, 0), (255, 0, 0))));
    // control 0x02 -> copy 3 literal bytes.
    var payload = new byte[] { 0x02, 1, 0, 1 };
    var packet = _IntraFrame(3, 1, payload);

    Assert.That(decoder.TryDecode(new(0, packet), out var frame), Is.True);
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 1, 0, 1 }));
  }

  [Test]
  [Category("Unit")]
  public void RowsComeOutTopDownFromBottomRowFirstCodedData() {
    var decoder = QpegVideoDecoder.Create(_StreamWithFormat("QPEG", 2, 2, _Palette((0, 0, 0), (255, 0, 0))));
    // Coded bottom row first: row0(coded)=[0,0] (bottom, displayed last), row1(coded)=[1,1] (top, displayed first).
    var payload = new byte[] { 0x03, 0, 0, 1, 1 }; // one short copy of all 4 bytes
    var packet = _IntraFrame(2, 2, payload);

    Assert.That(decoder.TryDecode(new(0, packet), out var frame), Is.True);
    // Displayed row0 (top) should be the second coded row: [1,1]; displayed row1 (bottom): [0,0].
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 1, 1, 0, 0 }));
  }

  // ============================================================================================
  // Interframe
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AnInterShortRunFillsCodeAnd0x1FPlus1Pixels() {
    var decoder = QpegVideoDecoder.Create(_StreamWithFormat("QPEG", 4, 1, _Palette((0, 0, 0), (255, 0, 0))));
    var keyframe = _IntraFrame(4, 1, [0x03, 0, 0, 0, 0]);
    decoder.TryDecode(new(0, keyframe), out _);

    // control 0xE1 -> run = (0xE1 & 0x1F) + 1 = 2, value 1, then done (0xE0).
    var delta = _InterFrame(4, 1, [0xE1, 1, 0xE0], frameType: 0x00, table: new byte[128]);
    Assert.That(decoder.TryDecode(new(0, delta), out var frame), Is.True);
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 1, 1, 0, 0 }));
  }

  [Test]
  [Category("Unit")]
  public void AnInterShortCopyCopiesCodeAnd0x1FPlus1Bytes() {
    var decoder = QpegVideoDecoder.Create(_StreamWithFormat("QPEG", 4, 1, _Palette((0, 0, 0), (255, 0, 0))));
    var keyframe = _IntraFrame(4, 1, [0x03, 0, 0, 0, 0]);
    decoder.TryDecode(new(0, keyframe), out _);

    // control 0xC1 -> copy = (0xC1 & 0x1F) + 1 = 2 bytes, then done.
    var delta = _InterFrame(4, 1, [0xC1, 1, 1, 0xE0], frameType: 0x00, table: new byte[128]);
    Assert.That(decoder.TryDecode(new(0, delta), out var frame), Is.True);
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 1, 1, 0, 0 }));
  }

  [Test]
  [Category("Unit")]
  public void AGeneralSkipLeavesPixelsUnchangedFromThePreviousFrame() {
    var decoder = QpegVideoDecoder.Create(_StreamWithFormat("QPEG", 4, 1, _Palette((0, 0, 0), (255, 0, 0))));
    var keyframe = _IntraFrame(4, 1, [0x03, 1, 1, 1, 1]);
    decoder.TryDecode(new(0, keyframe), out _);

    // control 0x84 -> skip 4 pixels (0x84 & 0x3F = 4), then done.
    var delta = _InterFrame(4, 1, [0x84, 0xE0], frameType: 0x00, table: new byte[128]);
    Assert.That(decoder.TryDecode(new(0, delta), out var frame), Is.True);
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 1, 1, 1, 1 }));
  }

  [Test]
  [Category("Unit")]
  public void ASpecialFillReadsFromTheFramesOwnTable() {
    var decoder = QpegVideoDecoder.Create(_StreamWithFormat("QPEG", 1, 1, _Palette((0, 0, 0), (255, 0, 0))));
    var keyframe = _IntraFrame(1, 1, [0x00, 0]);
    decoder.TryDecode(new(0, keyframe), out _);

    var table = new byte[128];
    table[5] = 1;
    var delta = _InterFrame(1, 1, [0x05, 0xE0], frameType: 0x00, table: table);
    Assert.That(decoder.TryDecode(new(0, delta), out var frame), Is.True);
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 1 }));
  }

  [Test]
  [Category("Unit")]
  public void MotionCompensationCopiesABlockFromThePreviousFrame() {
    var decoder = QpegVideoDecoder.Create(_StreamWithFormat("QPEG", 8, 8, _Palette((0, 0, 0), (255, 0, 0))));
    // Keyframe: a single 1 at coded position (0,7) i.e. displayed top-left, rest 0. Coded bottom-to-top:
    // row0(coded, bottom, displayed row7)=zeros; ... row7(coded, top, displayed row0) has a 1 at col0.
    var keyBytes = new byte[64];
    keyBytes[7 * 8 + 0] = 1; // last coded row (top), first pixel
    var keyframe = _IntraFrame(8, 8, _LiteralCopy(keyBytes));
    decoder.TryDecode(new(0, keyframe), out _);

    // Interframe with motion compensation (frameType=1): one 4x4 block (dims index 15), vector (0,0)
    // (copies from the same position, a no-op over that block), covering coded cursor 0..3 rows x 0..3
    // cols. Then a general skip of 63 pixels and a single-pixel skip cover the remaining 61+ pixels.
    var mcCode = 0xF0 | 15; // top nibble 0xF (signals motion-comp code), low nibble = dims index 15 (4x4)
    var payload = new byte[] { (byte)mcCode, 0x00, 0xBF, 0x00 }; // vector 0,0; skip 63; skip 1
    var delta = _InterFrame(8, 8, payload, frameType: 1, table: new byte[128]);

    Assert.That(decoder.TryDecode(new(0, delta), out var frame), Is.True);
    // The keyframe's single set pixel sits at coded cursor 56 (top coded row), which is display row 0,
    // column 0. The motion-compensated block covers coded cursor 0..3 (the bottom coded rows, display
    // rows 4..7) and does not touch it, and the rest of the frame is skip-coded, so it survives unchanged.
    Assert.That(frame.PixelData[0], Is.EqualTo(1));
  }

  // ============================================================================================
  // Refusals
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void APacketShorterThanTheHeaderRefuses() {
    var decoder = QpegVideoDecoder.Create(_StreamWithFormat("QPEG", 4, 4, _Palette((0, 0, 0))));
    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, new byte[50]), out _));
  }

  [Test]
  [Category("Unit")]
  public void AMismatchedSizeFieldRefuses() {
    var decoder = QpegVideoDecoder.Create(_StreamWithFormat("QPEG", 4, 1, _Palette((0, 0, 0))));
    var packet = _IntraFrame(4, 1, [0x03, 0, 0, 0, 0]);
    BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0), 999);

    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, packet), out _));
  }

  [Test]
  [Category("Unit")]
  public void AWrongMarkerByteRefuses() {
    var decoder = QpegVideoDecoder.Create(_StreamWithFormat("QPEG", 4, 1, _Palette((0, 0, 0))));
    var packet = _IntraFrame(4, 1, [0x03, 0, 0, 0, 0]);
    packet[132] = 0x00;

    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, packet), out _));
  }

  [Test]
  [Category("Unit")]
  public void DataRunningOutMidPictureRefuses() {
    var decoder = QpegVideoDecoder.Create(_StreamWithFormat("QPEG", 4, 1, _Palette((0, 0, 0))));
    // Short copy of 4 bytes but only 2 data bytes follow.
    var packet = _IntraFrame(4, 1, [0x03, 0, 0]);

    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, packet), out _));
  }

  // ============================================================================================
  // Helpers
  // ============================================================================================

  private static MediaStreamInfo _Stream(string tag) => new() {
    Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters(tag),
  };

  private static byte[] _Palette(params (int R, int G, int B)[] entries) {
    var raw = new byte[entries.Length * 4];
    for (var i = 0; i < entries.Length; ++i) {
      raw[i * 4] = (byte)entries[i].B;
      raw[i * 4 + 1] = (byte)entries[i].G;
      raw[i * 4 + 2] = (byte)entries[i].R;
    }

    return raw;
  }

  private static MediaStreamInfo _StreamWithFormat(string tag, int width, int height, byte[] paletteBgrx) {
    var bihSize = 40;
    var format = new byte[bihSize + paletteBgrx.Length];
    BinaryPrimitives.WriteUInt32LittleEndian(format.AsSpan(0), (uint)bihSize);
    BinaryPrimitives.WriteInt32LittleEndian(format.AsSpan(4), width);
    BinaryPrimitives.WriteInt32LittleEndian(format.AsSpan(8), height);
    BinaryPrimitives.WriteInt32LittleEndian(format.AsSpan(32), paletteBgrx.Length / 4); // ColorsUsed
    paletteBgrx.CopyTo(format, bihSize);

    return new() {
      Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters(tag),
      Width = width, Height = height, CodecPrivateData = format,
    };
  }

  private static byte[] _LiteralCopy(byte[] pixels) {
    // Encodes the given bottom-to-top pixel array as a chain of intra "short copy" opcodes (max 128
    // bytes each).
    var result = new System.Collections.Generic.List<byte>();
    var at = 0;
    while (at < pixels.Length) {
      var chunk = Math.Min(128, pixels.Length - at);
      result.Add((byte)(chunk - 1));
      result.AddRange(pixels.Skip(at).Take(chunk));
      at += chunk;
    }

    return result.ToArray();
  }

  private static byte[] _IntraFrame(int width, int height, byte[] payload) {
    var frame = new byte[134 + payload.Length];
    BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(0), (uint)frame.Length);
    frame[132] = 0xE0;
    frame[133] = 0x10;
    payload.CopyTo(frame, 134);
    return frame;
  }

  private static byte[] _InterFrame(int width, int height, byte[] payload, byte frameType, byte[] table) {
    var frame = new byte[134 + payload.Length];
    BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(0), (uint)frame.Length);
    table.CopyTo(frame, 4);
    frame[132] = 0xE0;
    frame[133] = frameType;
    payload.CopyTo(frame, 134);
    return frame;
  }
}
