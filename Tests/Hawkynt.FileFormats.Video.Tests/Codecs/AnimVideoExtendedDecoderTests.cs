using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Core;

namespace FileFormat.Codecs.Tests;

[TestFixture]
public sealed class AnimVideoExtendedDecoderTests {

  [Test]
  [Category("Unit")]
  public void OperationOneXorsAFullIlbmBodyWithItsReference() {
    var decoder = AnimVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _Keyframe(16, 1, [0x80, 0x00])), out _);

    var anhd = _Anhd(1, interleave: 1);
    anhd[1] = 1;
    BinaryPrimitives.WriteUInt16BigEndian(anhd.AsSpan(2, 2), 16);
    BinaryPrimitives.WriteUInt16BigEndian(anhd.AsSpan(4, 2), 1);
    var packet = _Form(_Chunk("ANHD", anhd), _Chunk("BODY", [0x80, 0x00]));

    decoder.TryDecode(new(0, packet), out var frame);
    Assert.That(frame.PixelData[0], Is.Zero);
  }

  [Test]
  [Category("Unit")]
  public void OperationTwoLongDeltaWritesAContiguousLongword() {
    var decoder = AnimVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _Keyframe(64, 1, new byte[8])), out _);

    var dlta = new byte[32 + 2 + 4 + 2];
    BinaryPrimitives.WriteUInt32BigEndian(dlta.AsSpan(0, 4), 32);
    BinaryPrimitives.WriteInt16BigEndian(dlta.AsSpan(32, 2), 1);
    BinaryPrimitives.WriteUInt32BigEndian(dlta.AsSpan(34, 4), 0x80000000);
    BinaryPrimitives.WriteUInt16BigEndian(dlta.AsSpan(38, 2), ushort.MaxValue);

    decoder.TryDecode(new(0, _DeltaFrame(2, dlta)), out var frame);
    Assert.That(frame.PixelData[32], Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void OperationThreeShortDeltaWritesAContiguousWord() {
    var decoder = AnimVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _Keyframe(32, 1, new byte[4])), out _);

    var dlta = new byte[32 + 2 + 2 + 2];
    BinaryPrimitives.WriteUInt32BigEndian(dlta.AsSpan(0, 4), 32);
    BinaryPrimitives.WriteInt16BigEndian(dlta.AsSpan(32, 2), 1);
    BinaryPrimitives.WriteUInt16BigEndian(dlta.AsSpan(34, 2), 0x8000);
    BinaryPrimitives.WriteUInt16BigEndian(dlta.AsSpan(36, 2), ushort.MaxValue);

    decoder.TryDecode(new(0, _DeltaFrame(3, dlta)), out var frame);
    Assert.That(frame.PixelData[16], Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void OperationFourNormativeVerticalRlcFormUsesWordPointers() {
    var decoder = AnimVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _Keyframe(16, 1, [0, 0])), out _);

    var dlta = new byte[72];
    BinaryPrimitives.WriteUInt32BigEndian(dlta.AsSpan(0, 4), 32); // WORD pointer -> byte 64
    BinaryPrimitives.WriteUInt32BigEndian(dlta.AsSpan(32, 4), 33); // WORD pointer -> byte 66
    BinaryPrimitives.WriteUInt16BigEndian(dlta.AsSpan(64, 2), 0x8000);
    BinaryPrimitives.WriteUInt16BigEndian(dlta.AsSpan(66, 2), 0); // absolute WORD destination
    BinaryPrimitives.WriteInt16BigEndian(dlta.AsSpan(68, 2), 1); // one unique item
    BinaryPrimitives.WriteUInt16BigEndian(dlta.AsSpan(70, 2), ushort.MaxValue);

    decoder.TryDecode(new(0, _DeltaFrame(4, dlta, bits: 8 | 16)), out var frame);
    Assert.That(frame.PixelData[0], Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void OperationFourLongDataOffsetsAddressLongwords() {
    var decoder = AnimVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _Keyframe(64, 1, new byte[8])), out _);

    var dlta = new byte[74];
    BinaryPrimitives.WriteUInt32BigEndian(dlta.AsSpan(0, 4), 32); // WORD pointer -> byte 64
    BinaryPrimitives.WriteUInt32BigEndian(dlta.AsSpan(32, 4), 34); // WORD pointer -> byte 68
    BinaryPrimitives.WriteUInt32BigEndian(dlta.AsSpan(64, 4), 0x80000000);
    BinaryPrimitives.WriteUInt16BigEndian(dlta.AsSpan(68, 2), 1); // absolute LONG destination
    BinaryPrimitives.WriteInt16BigEndian(dlta.AsSpan(70, 2), 1);
    BinaryPrimitives.WriteUInt16BigEndian(dlta.AsSpan(72, 2), ushort.MaxValue);

    decoder.TryDecode(new(0, _DeltaFrame(4, dlta, bits: 1 | 8 | 16)), out var frame);
    Assert.Multiple(() => {
      Assert.That(frame.PixelData[16], Is.Zero, "a LONG offset must not be mistaken for a WORD offset");
      Assert.That(frame.PixelData[32], Is.EqualTo(1));
    });
  }

  [Test]
  [Category("Unit")]
  public void OperationFourLongInfoSupportsOffsetsBeyondUshortAndSignedSizes() {
    const int width = ushort.MaxValue;
    const int height = 17;
    var bytesPerRow = (width + 15) / 16 * 2;
    var decoder = AnimVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _Keyframe(width, height, new byte[bytesPerRow * height])), out _);

    var dlta = new byte[78];
    BinaryPrimitives.WriteUInt32BigEndian(dlta.AsSpan(0, 4), 32); // data at byte 64
    BinaryPrimitives.WriteUInt32BigEndian(dlta.AsSpan(32, 4), 33); // info at byte 66
    BinaryPrimitives.WriteUInt16BigEndian(dlta.AsSpan(64, 2), 0x8000);
    BinaryPrimitives.WriteUInt32BigEndian(dlta.AsSpan(66, 4), 65536); // cannot fit a short-info offset
    BinaryPrimitives.WriteInt32BigEndian(dlta.AsSpan(70, 4), -1); // signed long RLC count
    BinaryPrimitives.WriteUInt32BigEndian(dlta.AsSpan(74, 4), uint.MaxValue);

    decoder.TryDecode(new(0, _DeltaFrame(4, dlta, bits: 8 | 16 | 32)), out var frame);
    Assert.That(frame.PixelData[16 * width], Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void OperationFourHorizontalTraversalAdvancesByOneDataItem() {
    var decoder = AnimVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _Keyframe(32, 2, new byte[8])), out _);

    var dlta = new byte[74];
    BinaryPrimitives.WriteUInt32BigEndian(dlta.AsSpan(0, 4), 32);
    BinaryPrimitives.WriteUInt32BigEndian(dlta.AsSpan(32, 4), 34);
    BinaryPrimitives.WriteUInt16BigEndian(dlta.AsSpan(64, 2), 0x8000);
    BinaryPrimitives.WriteUInt16BigEndian(dlta.AsSpan(66, 2), 0x8000);
    BinaryPrimitives.WriteUInt16BigEndian(dlta.AsSpan(68, 2), 0);
    BinaryPrimitives.WriteInt16BigEndian(dlta.AsSpan(70, 2), 2);
    BinaryPrimitives.WriteUInt16BigEndian(dlta.AsSpan(72, 2), ushort.MaxValue);

    decoder.TryDecode(new(0, _DeltaFrame(4, dlta, bits: 8)), out var frame);
    Assert.Multiple(() => {
      Assert.That(frame.PixelData[0], Is.EqualTo(1));
      Assert.That(frame.PixelData[16], Is.EqualTo(1));
      Assert.That(frame.PixelData[32], Is.Zero);
      Assert.That(frame.PixelData[48], Is.Zero);
    });
  }

  [Test]
  [Category("Unit")]
  public void OperationFourXorTogglesInsteadOfReplacing() {
    var decoder = AnimVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _Keyframe(16, 1, [0x80, 0x00])), out _);

    var dlta = new byte[72];
    BinaryPrimitives.WriteUInt32BigEndian(dlta.AsSpan(0, 4), 32);
    BinaryPrimitives.WriteUInt32BigEndian(dlta.AsSpan(32, 4), 33);
    BinaryPrimitives.WriteUInt16BigEndian(dlta.AsSpan(64, 2), 0x8000);
    BinaryPrimitives.WriteUInt16BigEndian(dlta.AsSpan(66, 2), 0);
    BinaryPrimitives.WriteInt16BigEndian(dlta.AsSpan(68, 2), 1);
    BinaryPrimitives.WriteUInt16BigEndian(dlta.AsSpan(70, 2), ushort.MaxValue);

    decoder.TryDecode(new(0, _DeltaFrame(4, dlta, bits: 2 | 8 | 16)), out var frame);
    Assert.That(frame.PixelData[0], Is.Zero);
  }

  [Test]
  [Category("Unit")]
  public void OperationFourNonRlcRefusesInsteadOfGuessingItsWireGrammar() {
    var decoder = AnimVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _Keyframe(16, 1, [0, 0])), out _);

    var failure = Assert.Throws<NotSupportedException>(
      () => decoder.TryDecode(new(0, _DeltaFrame(4, new byte[64], bits: 16)), out _));
    Assert.That(failure!.Message, Does.Contain("non-RLC"));
  }

  [Test]
  [Category("Unit")]
  public void OperationFourUndefinedOptionBitsRefuse() {
    var decoder = AnimVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _Keyframe(16, 1, [0, 0])), out _);

    var dlta = new byte[64];
    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, _DeltaFrame(4, dlta, bits: 1u << 6)), out _));
  }

  [Test]
  [Category("Unit")]
  public void AnimBrushMethodFiveXorTogglesInsteadOfStores() {
    var decoder = AnimVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _Keyframe(16, 1, [0x80, 0x00])), out _);

    var dlta = _Method5([1, 0x81, 0x80], [0]);
    decoder.TryDecode(new(0, _DeltaFrame(5, dlta, interleave: 1, bits: 4)), out var frame);
    Assert.That(frame.PixelData[0], Is.Zero);
  }

  [Test]
  [Category("Unit")]
  public void OperationSixUsesFourFramesBack() {
    var decoder = AnimVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _Keyframe(16, 1, [0, 0])), out _);

    var setFirst = _Method5([1, 0x81, 0x80], [0]);
    decoder.TryDecode(new(0, _DeltaFrame(6, setFirst, interleave: 4)), out var second);
    Assert.That(second.PixelData[0], Is.EqualTo(1));

    var noop = _Method5([0], [0]);
    decoder.TryDecode(new(0, _DeltaFrame(6, noop, interleave: 4)), out _);
    decoder.TryDecode(new(0, _DeltaFrame(6, noop, interleave: 4)), out _);
    decoder.TryDecode(new(0, _DeltaFrame(6, noop, interleave: 4)), out _);
    decoder.TryDecode(new(0, _DeltaFrame(6, noop, interleave: 4)), out var sixth);
    Assert.That(sixth.PixelData[0], Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void OperationSevenReadsSeparateOpcodeAndShortDataLists() {
    var decoder = AnimVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _Keyframe(16, 1, [0, 0])), out _);

    var dlta = new byte[68];
    BinaryPrimitives.WriteUInt32BigEndian(dlta.AsSpan(0, 4), 64);
    BinaryPrimitives.WriteUInt32BigEndian(dlta.AsSpan(32, 4), 66);
    dlta[64] = 1;
    dlta[65] = 0x81;
    BinaryPrimitives.WriteUInt16BigEndian(dlta.AsSpan(66, 2), 0x8000);

    decoder.TryDecode(new(0, _DeltaFrame(7, dlta)), out var frame);
    Assert.That(frame.PixelData[0], Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void OperationSevenMayCreateTheFirstFrameFromBlack() {
    var bmhd = _Bmhd(16, 1);
    var dlta = new byte[68];
    BinaryPrimitives.WriteUInt32BigEndian(dlta.AsSpan(0, 4), 64);
    BinaryPrimitives.WriteUInt32BigEndian(dlta.AsSpan(32, 4), 66);
    dlta[64] = 1;
    dlta[65] = 0x81;
    BinaryPrimitives.WriteUInt16BigEndian(dlta.AsSpan(66, 2), 0x8000);
    var packet = _Form(_Chunk("BMHD", bmhd), _Chunk("CMAP", _Palette()), _Chunk("ANHD", _Anhd(7)), _Chunk("DLTA", dlta));

    var decoder = AnimVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, packet), out var frame);
    Assert.That(frame.PixelData[0], Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void OperationEightShortDataUsesItemSizedOpcodes() {
    var decoder = AnimVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _Keyframe(16, 1, [0, 0])), out _);

    var dlta = new byte[70];
    BinaryPrimitives.WriteUInt32BigEndian(dlta.AsSpan(0, 4), 64);
    BinaryPrimitives.WriteUInt16BigEndian(dlta.AsSpan(64, 2), 1);
    BinaryPrimitives.WriteUInt16BigEndian(dlta.AsSpan(66, 2), 0x8001);
    BinaryPrimitives.WriteUInt16BigEndian(dlta.AsSpan(68, 2), 0x8000);

    decoder.TryDecode(new(0, _DeltaFrame(8, dlta)), out var frame);
    Assert.That(frame.PixelData[0], Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void OperationEightLongDataUsesAWordForTheOddTrailingColumn() {
    var decoder = AnimVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _Keyframe(48, 1, new byte[6])), out _);

    var dlta = new byte[74];
    BinaryPrimitives.WriteUInt32BigEndian(dlta.AsSpan(0, 4), 64);
    BinaryPrimitives.WriteUInt32BigEndian(dlta.AsSpan(64, 4), 0); // unchanged LONG column
    BinaryPrimitives.WriteUInt16BigEndian(dlta.AsSpan(68, 2), 1);
    BinaryPrimitives.WriteUInt16BigEndian(dlta.AsSpan(70, 2), 0x8001);
    BinaryPrimitives.WriteUInt16BigEndian(dlta.AsSpan(72, 2), 0x8000);

    decoder.TryDecode(new(0, _DeltaFrame(8, dlta, bits: 1)), out var frame);
    Assert.That(frame.PixelData[32], Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void LaterOperationZeroDoesNotResetTwoFrameHistory() {
    var decoder = AnimVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _Keyframe(16, 1, [0, 0])), out _);

    decoder.TryDecode(new(0, _DeltaFrame(5, _Method5([1, 0x81, 0x80], [0]))), out _);
    decoder.TryDecode(new(0, _DirectFrame(16, 1, [0x40, 0x00])), out _);
    decoder.TryDecode(new(0, _DeltaFrame(5, _Method5([0], [0]))), out var fourth);

    Assert.That(fourth.PixelData[0], Is.EqualTo(1));
    Assert.That(fourth.PixelData[1], Is.Zero);
  }

  private static MediaStreamInfo _Stream() => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("ANIM"),
  };

  private static byte[] _Keyframe(int width, int height, byte[] body)
    => _Form(_Chunk("BMHD", _Bmhd(width, height)), _Chunk("CMAP", _Palette()), _Chunk("BODY", body));

  private static byte[] _DirectFrame(int width, int height, byte[] body)
    => _Form(_Chunk("BMHD", _Bmhd(width, height)), _Chunk("ANHD", _Anhd(0)), _Chunk("BODY", body));

  private static byte[] _DeltaFrame(byte operation, byte[] dlta, byte interleave = 0, uint bits = 0)
    => _Form(_Chunk("ANHD", _Anhd(operation, interleave, bits)), _Chunk("DLTA", dlta));

  private static byte[] _Bmhd(int width, int height) {
    var result = new byte[20];
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(0, 2), (ushort)width);
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(2, 2), (ushort)height);
    result[8] = 1;
    return result;
  }

  private static byte[] _Anhd(byte operation, byte interleave = 0, uint bits = 0) {
    var result = new byte[40];
    result[0] = operation;
    result[18] = interleave;
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(20, 4), bits);
    return result;
  }

  private static byte[] _Palette() => [0, 0, 0, 255, 255, 255];

  private static byte[] _Method5(byte[] column0, byte[] column1) {
    var result = new byte[64 + column0.Length + column1.Length];
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(0, 4), 64);
    column0.CopyTo(result, 64);
    column1.CopyTo(result, 64 + column0.Length);
    return result;
  }

  private static byte[] _Chunk(string id, byte[] data) {
    var result = new byte[8 + data.Length + (data.Length & 1)];
    for (var i = 0; i < 4; ++i)
      result[i] = (byte)id[i];
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(4, 4), (uint)data.Length);
    data.CopyTo(result, 8);
    return result;
  }

  private static byte[] _Form(params byte[][] chunks) {
    var innerLength = 4 + chunks.Sum(c => c.Length);
    var result = new byte[8 + innerLength];
    "FORM"u8.CopyTo(result);
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(4, 4), (uint)innerLength);
    "ILBM"u8.CopyTo(result.AsSpan(8));
    var at = 12;
    foreach (var chunk in chunks) {
      chunk.CopyTo(result, at);
      at += chunk.Length;
    }
    return result;
  }
}
