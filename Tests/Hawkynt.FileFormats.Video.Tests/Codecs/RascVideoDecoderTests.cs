using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

[TestFixture]
public sealed class RascVideoDecoderTests {

  [Test]
  [Category("Unit")]
  public void FormatInitializationCreatesABlackCanvas() {
    var decoder = RascVideoDecoder.Create(_Stream());

    Assert.That(decoder.TryDecode(new(0, _Chunk("FINT", _Format32(2, 1))), out var frame), Is.True);
    Assert.That(frame.Width, Is.EqualTo(2));
    Assert.That(frame.Height, Is.EqualTo(1));
    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(frame.PixelData, Is.EqualTo(new byte[6]));
  }

  [Test]
  [Category("Unit")]
  public void KeyframeInflatesBothBottomUpReferenceSurfaces() {
    var decoder = RascVideoDecoder.Create(_Stream());
    Assert.That(decoder.TryDecode(new(0, _Chunk("FINT", _Format32(1, 1))), out _), Is.True);

    // RASC KFRM stores frame2 first and frame1 second. Native 32-bit pixels are B,G,R,unused.
    var native = new byte[] {
      3, 2, 1, 0,
      6, 5, 4, 0,
    };
    var keyframe = _Chunk("KFRM", _Zlib(native));

    Assert.That(decoder.TryDecode(new(0, keyframe), out var frame), Is.True);
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 1, 2, 3 }));
  }

  [Test]
  [Category("Unit")]
  public void RawDeltaTypeSevenReplacesOneBgr0PixelAndPreservesItsPreviousValue() {
    var decoder = RascVideoDecoder.Create(_Stream());
    Assert.That(decoder.TryDecode(new(0, _Chunk("FINT", _Format32(1, 1))), out _), Is.True);

    var commands = new byte[] {
      7, 1,
      3, 2, 1, 0,
    };
    var payload = new byte[40 + commands.Length];
    BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12), checked((uint)commands.Length));
    BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(16), 0); // x
    BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(20), 0); // y
    BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(24), 1); // width
    BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(28), 1); // height
    BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(36), 0); // raw commands
    commands.CopyTo(payload, 40);

    Assert.That(decoder.TryDecode(new(0, _Chunk("DLTA", payload)), out var frame), Is.True);
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 1, 2, 3 }));

    // Type 2 swaps current and previous bytes. Four byte operations therefore restore the black
    // frame preserved by the previous type-7 run.
    var swapCommands = new byte[] { 2, 4 };
    var swap = new byte[40 + swapCommands.Length];
    BinaryPrimitives.WriteUInt32LittleEndian(swap.AsSpan(12), checked((uint)swapCommands.Length));
    BinaryPrimitives.WriteUInt32LittleEndian(swap.AsSpan(24), 1);
    BinaryPrimitives.WriteUInt32LittleEndian(swap.AsSpan(28), 1);
    swapCommands.CopyTo(swap, 40);

    Assert.That(decoder.TryDecode(new(0, _Chunk("DLTA", swap)), out var restored), Is.True);
    Assert.That(restored.PixelData, Is.EqualTo(new byte[] { 0, 0, 0 }));
  }

  [Test]
  [Category("Unit")]
  public void KbndWrapperCarriesANestedRecordHeader() {
    var decoder = RascVideoDecoder.Create(_Stream());
    var format = _Format32(1, 1);
    var wrapped = new byte[12 + format.Length];
    _FourCc("KBND").CopyTo(wrapped, 0);
    _FourCc("FINT").CopyTo(wrapped, 4);
    BinaryPrimitives.WriteUInt32LittleEndian(wrapped.AsSpan(8), checked((uint)format.Length));
    format.CopyTo(wrapped, 12);

    Assert.That(decoder.TryDecode(new(0, wrapped), out var frame), Is.True);
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 0, 0, 0 }));
  }

  [Test]
  [Category("Unit")]
  public void CursorIsCompositedOnlyOnTheReturnedFrame() {
    var decoder = RascVideoDecoder.Create(_Stream());
    Assert.That(decoder.TryDecode(new(0, _Chunk("FINT", _Format32(1, 2))), out _), Is.True);

    // Cursor storage is bottom-up. Its first RGB triplet is also the transparency key, so the
    // bottom cursor pixel is transparent and the second triplet paints the top pixel red.
    var cursorRgb = new byte[] {
      0, 0, 0,
      255, 0, 0,
    };
    var cursorPayload = new byte[32 + _Zlib(cursorRgb).Length];
    BinaryPrimitives.WriteUInt32LittleEndian(cursorPayload.AsSpan(8), 1);
    BinaryPrimitives.WriteUInt32LittleEndian(cursorPayload.AsSpan(12), 2);
    BinaryPrimitives.WriteUInt32LittleEndian(cursorPayload.AsSpan(28), checked((uint)cursorRgb.Length));
    _Zlib(cursorRgb).CopyTo(cursorPayload, 32);

    var positionPayload = new byte[16];
    BinaryPrimitives.WriteUInt32LittleEndian(positionPayload.AsSpan(8), 0);
    BinaryPrimitives.WriteUInt32LittleEndian(positionPayload.AsSpan(12), 0);
    var packet = _Concat(_Chunk("MOUS", cursorPayload), _Chunk("MPOS", positionPayload));

    Assert.That(decoder.TryDecode(new(0, packet), out var frame), Is.True);
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] {
      255, 0, 0,
      0, 0, 0,
    }));

    // An unrelated empty bundle returns no frame rather than burning the cursor into frame2.
    Assert.That(decoder.TryDecode(new(0, _FourCc("EMPT")), out _), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void PictureDataBeforeInitializationRefuses() {
    var decoder = RascVideoDecoder.Create(_Stream());
    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, _Chunk("DLTA", new byte[40])), out _));
  }

  [Test]
  [Category("Unit")]
  public void CodecIsRegistered() {
    var stream = _Stream();
    Assert.That(VideoFormatRegistry.AllCodecs.Select(codec => codec.CodecName),
      Does.Contain("RemotelyAnywhere Screen Capture"));
    Assert.That(VideoFormatRegistry.CanDecode(stream), Is.True);
    Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<RascVideoDecoder>());
  }

  private static MediaStreamInfo _Stream() => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("RASC"),
    Width = 1,
    Height = 1,
    BitsPerPixel = 32,
  };

  private static byte[] _Format32(int width, int height) {
    var payload = new byte[72];
    BinaryPrimitives.WriteUInt32LittleEndian(payload, 0x65);
    BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8), checked((uint)width));
    BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12), checked((uint)height));
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(46), 32);
    return payload;
  }

  private static byte[] _Chunk(string type, ReadOnlySpan<byte> payload) {
    var result = new byte[8 + payload.Length];
    _FourCc(type).CopyTo(result, 0);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), checked((uint)payload.Length));
    payload.CopyTo(result.AsSpan(8));
    return result;
  }

  private static byte[] _FourCc(string value) {
    if (value.Length != 4)
      throw new ArgumentException("FourCC must contain exactly four characters.", nameof(value));
    return new[] { (byte)value[0], (byte)value[1], (byte)value[2], (byte)value[3] };
  }

  private static byte[] _Zlib(ReadOnlySpan<byte> data) {
    using var output = new MemoryStream();
    using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
      zlib.Write(data);
    return output.ToArray();
  }

  private static byte[] _Concat(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second) {
    var result = new byte[first.Length + second.Length];
    first.CopyTo(result);
    second.CopyTo(result.AsSpan(first.Length));
    return result;
  }
}
