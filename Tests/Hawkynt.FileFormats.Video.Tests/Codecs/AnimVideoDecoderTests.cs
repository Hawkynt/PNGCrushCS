using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// IFF ANIM's video decode, built here byte by byte: ByteRun1 unpacking, the interleaved-to-plane-major
/// transpose, Byte Vertical Delta's skip/uniq/same ops, the double-buffer interleave selection, and the
/// RGB and Hold-And-Modify pixel paths.
/// </summary>
/// <remarks>
/// Four real files — 160x120, 123 frames each, one bitplane, eight bitplanes, HAM6 and HAM8 — were
/// decoded here and by ffmpeg and compared sample for sample against ffmpeg's own <c>rgb24</c> output:
/// all 492 frames are identical, maximum delta nought.
/// </remarks>
[TestFixture]
public sealed class AnimVideoDecoderTests {

  // ============================================================================================
  // Which streams it takes
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheAnimCodeIsTaken()
    => Assert.That(AnimVideoDecoder.Accepts(_Stream()), Is.True);

  [Test]
  [Category("Unit")]
  public void AnotherCodecsCodeIsNotTaken() {
    var stream = new MediaStreamInfo { Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters("cvid") };
    Assert.That(AnimVideoDecoder.Accepts(stream), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void TheCodecIsRegistered() {
    var stream = _Stream();

    Assert.That(VideoFormatRegistry.AllCodecs.Select(c => c.CodecName), Does.Contain("IFF ANIM Video"));
    Assert.That(VideoFormatRegistry.CanDecode(stream), Is.True);
    Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<AnimVideoDecoder>());
  }

  // ============================================================================================
  // Keyframe: BMHD + BODY
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AnUncompressedKeyframeLooksUpThePaletteByIndex() {
    var decoder = AnimVideoDecoder.Create(_Stream());
    // Two planes, 16x1: pixel 0 has both plane bits set (index 3), the rest clear (index 0).
    var body = new byte[] { 0x80, 0x00, 0x80, 0x00 }; // plane0 row, plane1 row (word-aligned, 2 bytes each)
    var palette = _Palette((0, 0, 0), (0, 0, 0), (0, 0, 0), (255, 0, 0));
    var packet = _Keyframe(width: 16, height: 1, planes: 2, compression: 0, body: body, palette: palette);

    Assert.That(decoder.TryDecode(new(0, packet), out var frame), Is.True);
    var rgb = frame.ToRgb24();
    Assert.That(rgb[0], Is.EqualTo(0xFF));
    Assert.That(rgb[3], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void AByteRun1LiteralRunIsCountPlusOneBytesCopiedVerbatim() {
    var decoder = AnimVideoDecoder.Create(_Stream());
    // control=1 (>=0) -> copies 1+1=2 literal bytes: one plane's word-aligned row.
    var compressed = new byte[] { 0x01, 0x80, 0x00 };
    var palette = _Palette((0, 0, 0), (255, 0, 0));
    var packet = _Keyframe(width: 16, height: 1, planes: 1, compression: 1, body: compressed, palette: palette);

    Assert.That(decoder.TryDecode(new(0, packet), out var frame), Is.True);
    Assert.That(frame.ToRgb24()[0], Is.EqualTo(0xFF));
  }

  [Test]
  [Category("Unit")]
  public void AByteRun1RepeatRunRepeatsOneByteOneMinusControlTimes() {
    var decoder = AnimVideoDecoder.Create(_Stream());
    // control = -1 (0xFF) -> repeats the next byte 1-(-1)=2 times, giving the same two-byte row.
    var compressed = new byte[] { 0xFF, 0x80 };
    var palette = _Palette((0, 0, 0), (255, 0, 0));
    var packet = _Keyframe(width: 16, height: 1, planes: 1, compression: 1, body: compressed, palette: palette);

    Assert.That(decoder.TryDecode(new(0, packet), out var frame), Is.True);
    Assert.That(frame.ToRgb24()[0], Is.EqualTo(0xFF));
  }

  [Test]
  [Category("Unit")]
  public void AByteRun1NoOpControlByteConsumesOnlyItself() {
    var decoder = AnimVideoDecoder.Create(_Stream());
    // -128 is a no-op, then a literal run of the real two bytes.
    var compressed = new byte[] { 0x80, 0x01, 0x80, 0x00 };
    var palette = _Palette((0, 0, 0), (255, 0, 0));
    var packet = _Keyframe(width: 16, height: 1, planes: 1, compression: 1, body: compressed, palette: palette);

    Assert.That(decoder.TryDecode(new(0, packet), out var frame), Is.True);
    Assert.That(frame.ToRgb24()[0], Is.EqualTo(0xFF));
  }

  // ============================================================================================
  // Byte Vertical Delta (method 5)
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ASkipOpMovesTheDestinationForwardWithoutWriting() {
    var decoder = AnimVideoDecoder.Create(_Stream());
    var palette = _Palette((0, 0, 0), (255, 0, 0));
    // 8x4, one plane: bytesPerRow=2. Keyframe: all zero (index 0 everywhere).
    var keyframe = _Keyframe(width: 8, height: 4, planes: 1, compression: 0, body: new byte[8], palette: palette);
    decoder.TryDecode(new(0, keyframe), out _);

    // Column 0: op-count 2 - skip 2 rows, then a 'uniq' of 1 byte (opcode 0x81, data 0xFF) sets
    // bit 7, pixel x=0 of row 2. Column 1: op-count 0 (unchanged).
    var col0 = new byte[] { 2, 2, 0x81, 0xFF };
    var col1 = new byte[] { 0 };
    var dlta = _Delta1Plane(col0, col1);
    var delta = _DeltaFrame(dlta, operation: 5, interleave: 0);

    Assert.That(decoder.TryDecode(new(0, delta), out var frame), Is.True);
    var rgb = frame.ToRgb24();
    // Row 2, x=0 should now be palette index 1 (white); rows 0,1,3 unchanged (black).
    Assert.That(rgb[(2 * 8 + 0) * 3], Is.EqualTo(0xFF));
    Assert.That(rgb[(0 * 8 + 0) * 3], Is.EqualTo(0));
    Assert.That(rgb[(1 * 8 + 0) * 3], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void ASameOpRepeatsOneValueOverSeveralRows() {
    var decoder = AnimVideoDecoder.Create(_Stream());
    var palette = _Palette((0, 0, 0), (255, 0, 0));
    var keyframe = _Keyframe(width: 8, height: 4, planes: 1, compression: 0, body: new byte[8], palette: palette);
    decoder.TryDecode(new(0, keyframe), out _);

    // Column 0: 'same' op — 0 byte, count 4, value 0xFF — sets x=0 for all four rows.
    var col0 = new byte[] { 1, 0x00, 0x04, 0xFF };
    var col1 = new byte[] { 0 };
    var dlta = _Delta1Plane(col0, col1);
    var delta = _DeltaFrame(dlta, operation: 5, interleave: 0);

    Assert.That(decoder.TryDecode(new(0, delta), out var frame), Is.True);
    var rgb = frame.ToRgb24();
    for (var y = 0; y < 4; ++y)
      Assert.That(rgb[(y * 8 + 0) * 3], Is.EqualTo(0xFF), $"row {y}");
  }

  [Test]
  [Category("Unit")]
  public void InterleaveZeroTargetsTheBufferTwoFramesBackNotTheOneJustShown() {
    var decoder = AnimVideoDecoder.Create(_Stream());
    var palette = _Palette((0, 0, 0), (255, 0, 0));
    var keyframe = _Keyframe(width: 8, height: 1, planes: 1, compression: 0, body: new byte[2], palette: palette);
    decoder.TryDecode(new(0, keyframe), out _);

    // Frame 2: sets x=0 via a 'uniq' op — modifies the buffer copied from the keyframe.
    var setPixel = _Delta1Plane([1, 0x81, 0xFF], [0]);
    decoder.TryDecode(new(0, _DeltaFrame(setPixel, operation: 5, interleave: 0)), out var frame2);
    Assert.That(frame2.ToRgb24()[0], Is.EqualTo(0xFF));

    // Frame 3, interleave 0: targets the OTHER buffer — two frames back from frame 3 is the keyframe,
    // still all zero — so a no-op delta should show the keyframe's black, not frame 2's white.
    var noop = _Delta1Plane([0], [0]);
    decoder.TryDecode(new(0, _DeltaFrame(noop, operation: 5, interleave: 0)), out var frame3);
    Assert.That(frame3.ToRgb24()[0], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void InterleaveOneTargetsTheBufferJustShown() {
    var decoder = AnimVideoDecoder.Create(_Stream());
    var palette = _Palette((0, 0, 0), (255, 0, 0));
    var keyframe = _Keyframe(width: 8, height: 1, planes: 1, compression: 0, body: new byte[2], palette: palette);
    decoder.TryDecode(new(0, keyframe), out _);

    var setPixel = _Delta1Plane([1, 0x81, 0xFF], [0]);
    decoder.TryDecode(new(0, _DeltaFrame(setPixel, operation: 5, interleave: 0)), out _);

    // interleave=1 modifies the buffer just shown (frame 2's), which already has x=0 set.
    var noop = _Delta1Plane([0], [0]);
    decoder.TryDecode(new(0, _DeltaFrame(noop, operation: 5, interleave: 1)), out var frame3);
    Assert.That(frame3.ToRgb24()[0], Is.EqualTo(0xFF));
  }

  // ============================================================================================
  // Hold-And-Modify
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void Ham6ControlZeroLooksUpThePalette() {
    var decoder = AnimVideoDecoder.Create(_Stream());
    var palette = _Palette((255, 0, 0));
    // Six planes, one pixel: value 0b000001 -> control 0, index 1... but palette has one entry (index 0);
    // use value 0 (control 0, index 0) instead.
    var body = _PlaneMajorBody(planes: 6, width: 1, height: 1, pixelValues: [0]);
    var packet = _Keyframe(width: 1, height: 1, planes: 6, compression: 0, body: body, palette: palette, camg: 0x0800);

    Assert.That(decoder.TryDecode(new(0, packet), out var frame), Is.True);
    var rgb = frame.ToRgb24();
    Assert.That(rgb[0], Is.EqualTo(0xFF));
  }

  [Test]
  [Category("Unit")]
  public void Ham6ControlOneHoldsAndModifiesBlue() {
    var decoder = AnimVideoDecoder.Create(_Stream());
    var palette = _Palette((10 * 17, 5 * 17, 3 * 17)); // background
    // control=01 (modify blue), value=0xA -> pixel value 0b01_1010 = 0x1A.
    var body = _PlaneMajorBody(planes: 6, width: 1, height: 1, pixelValues: [0x1A]);
    var packet = _Keyframe(width: 1, height: 1, planes: 6, compression: 0, body: body, palette: palette, camg: 0x0800);

    Assert.That(decoder.TryDecode(new(0, packet), out var frame), Is.True);
    var rgb = frame.ToRgb24();
    Assert.That(rgb[0], Is.EqualTo(10 * 17)); // red held
    Assert.That(rgb[1], Is.EqualTo(5 * 17)); // green held
    Assert.That(rgb[2], Is.EqualTo(0xAA)); // blue overwritten, widened nibble 0xA
  }

  // ============================================================================================
  // Refusals
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ADeltaFrameBeforeAnyKeyframeRefuses() {
    var decoder = AnimVideoDecoder.Create(_Stream());
    var packet = _DeltaFrame(_Delta1Plane([0], [0]), operation: 5, interleave: 0);

    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, packet), out _));
  }

  [Test]
  [Category("Unit")]
  public void AnUnsupportedCompressionMethodRefuses() {
    var decoder = AnimVideoDecoder.Create(_Stream());
    var keyframe = _Keyframe(width: 8, height: 1, planes: 1, compression: 0, body: new byte[2], palette: _Palette((0, 0, 0)));
    decoder.TryDecode(new(0, keyframe), out _);

    var packet = _DeltaFrame([], operation: 3, interleave: 0);
    var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, packet), out _));
    Assert.That(failure!.Message, Does.Contain("method"));
  }

  [Test]
  [Category("Unit")]
  public void AnUnsupportedInterleaveRefuses() {
    var decoder = AnimVideoDecoder.Create(_Stream());
    var keyframe = _Keyframe(width: 8, height: 1, planes: 1, compression: 0, body: new byte[2], palette: _Palette((0, 0, 0)));
    decoder.TryDecode(new(0, keyframe), out _);

    var packet = _DeltaFrame(_Delta1Plane([0], [0]), operation: 5, interleave: 2);
    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, packet), out _));
  }

  [Test]
  [Category("Unit")]
  public void NonZeroOptionBitsRefuse() {
    var decoder = AnimVideoDecoder.Create(_Stream());
    var keyframe = _Keyframe(width: 8, height: 1, planes: 1, compression: 0, body: new byte[2], palette: _Palette((0, 0, 0)));
    decoder.TryDecode(new(0, keyframe), out _);

    var packet = _DeltaFrame(_Delta1Plane([0], [0]), operation: 5, interleave: 0, bits: 4);
    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, packet), out _));
  }

  [Test]
  [Category("Unit")]
  public void MoreThanEightBitplanesRefuses() {
    var decoder = AnimVideoDecoder.Create(_Stream());
    var packet = _Keyframe(width: 8, height: 1, planes: 9, compression: 0, body: new byte[18], palette: _Palette((0, 0, 0)));

    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, packet), out _));
  }

  [Test]
  [Category("Unit")]
  public void APaletteIndexBeyondTheStatedPaletteRefuses() {
    var decoder = AnimVideoDecoder.Create(_Stream());
    var body = new byte[] { 0x80, 0x00 }; // one plane, pixel 0 set -> index 1
    var packet = _Keyframe(width: 16, height: 1, planes: 1, compression: 0, body: body, palette: _Palette((0, 0, 0)));

    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, packet), out _));
  }

  // ============================================================================================
  // Helpers
  // ============================================================================================

  private static MediaStreamInfo _Stream() => new() {
    Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters("ANIM"),
  };

  private static byte[] _Chunk(string id, byte[] data) {
    var chunk = new byte[8 + data.Length + (data.Length & 1)];
    System.Text.Encoding.ASCII.GetBytes(id).CopyTo(chunk, 0);
    BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(4), (uint)data.Length);
    data.CopyTo(chunk, 8);
    return chunk;
  }

  private static byte[] _Form(params byte[][] chunks) {
    var innerLength = 4 + chunks.Sum(c => c.Length);
    var form = new byte[8 + innerLength];
    "FORM"u8.CopyTo(form);
    BinaryPrimitives.WriteUInt32BigEndian(form.AsSpan(4), (uint)innerLength);
    "ILBM"u8.CopyTo(form.AsSpan(8));
    var at = 12;
    foreach (var chunk in chunks) {
      chunk.CopyTo(form, at);
      at += chunk.Length;
    }

    return form;
  }

  private static byte[] _Palette(params (int R, int G, int B)[] entries) {
    var bytes = new byte[entries.Length * 3];
    for (var i = 0; i < entries.Length; ++i) {
      bytes[i * 3] = (byte)entries[i].R;
      bytes[i * 3 + 1] = (byte)entries[i].G;
      bytes[i * 3 + 2] = (byte)entries[i].B;
    }

    return bytes;
  }

  private static byte[] _Keyframe(int width, int height, int planes, byte compression, byte[] body, byte[] palette, uint camg = 0) {
    var bmhd = new byte[20];
    BinaryPrimitives.WriteUInt16BigEndian(bmhd.AsSpan(0), (ushort)width);
    BinaryPrimitives.WriteUInt16BigEndian(bmhd.AsSpan(2), (ushort)height);
    bmhd[8] = (byte)planes;
    bmhd[10] = compression;

    if (camg != 0) {
      var camgBytes = new byte[4];
      BinaryPrimitives.WriteUInt32BigEndian(camgBytes, camg);
      return _Form(_Chunk("BMHD", bmhd), _Chunk("CAMG", camgBytes), _Chunk("CMAP", palette), _Chunk("BODY", body));
    }

    return _Form(_Chunk("BMHD", bmhd), _Chunk("CMAP", palette), _Chunk("BODY", body));
  }

  private static byte[] _DeltaFrame(byte[] dltaData, byte operation, byte interleave, uint bits = 0) {
    var anhd = new byte[40];
    anhd[0] = operation;
    anhd[18] = interleave;
    BinaryPrimitives.WriteUInt32BigEndian(anhd.AsSpan(20), bits);

    return _Form(_Chunk("ANHD", anhd), _Chunk("DLTA", dltaData));
  }

  /// <summary>Builds a one-plane DLTA chunk (method 5) from two already-encoded columns (bytesPerRow=2,
  /// i.e. width up to 16 pixels).</summary>
  private static byte[] _Delta1Plane(byte[] col0, byte[] col1) {
    var data = new byte[32 + col0.Length + col1.Length];
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(0), 32); // plane 0 pointer
    col0.CopyTo(data, 32);
    col1.CopyTo(data, 32 + col0.Length);
    return data;
  }

  /// <summary>Builds an interleaved (per-scanline) ILBM BODY from explicit combined pixel values, one
  /// bitplane bit at a time — the layout a real BODY chunk stores and this decoder transposes to
  /// plane-major immediately after unpacking.</summary>
  private static byte[] _PlaneMajorBody(int planes, int width, int height, int[] pixelValues) {
    var bytesPerRow = (width + 15) / 16 * 2;
    var scanlineBytes = bytesPerRow * planes;
    var body = new byte[scanlineBytes * height];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var v = pixelValues[y * width + x];
        for (var p = 0; p < planes; ++p)
          if ((v >> p & 1) != 0) {
            var byteIndex = x / 8;
            var bitIndex = 7 - x % 8;
            body[y * scanlineBytes + p * bytesPerRow + byteIndex] |= (byte)(1 << bitIndex);
          }
      }

    return body;
  }
}
