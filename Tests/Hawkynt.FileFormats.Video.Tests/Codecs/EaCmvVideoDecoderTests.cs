using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

/// <summary>Electronic Arts CMV decoding, including both temporal references and chunk sequencing.</summary>
[TestFixture]
public sealed class EaCmvVideoDecoderTests {

  [Test]
  [Category("Unit")]
  public void TheEaCmvCodeIsTaken() {
    var stream = _Stream();

    Assert.Multiple(() => {
      Assert.That(EaCmvVideoDecoder.Accepts(stream), Is.True);
      Assert.That(VideoFormatRegistry.AllCodecs.Select(c => c.CodecName), Does.Contain("Electronic Arts CMV"));
      Assert.That(VideoFormatRegistry.CanDecode(stream), Is.True);
      Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<EaCmvVideoDecoder>());
    });
  }

  [Test]
  [Category("Unit")]
  public void NonCmvOrNonVideoStreamsAreNotTaken() {
    var otherCodec = new MediaStreamInfo { Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters("tgv ") };
    var audio = new MediaStreamInfo { Index = 0, Kind = MediaStreamKind.Audio, Codec = CodecTag.FromCharacters("cmv ") };

    Assert.Multiple(() => {
      Assert.That(EaCmvVideoDecoder.Accepts(otherCodec), Is.False);
      Assert.That(EaCmvVideoDecoder.Accepts(audio), Is.False);
    });
  }

  [Test]
  [Category("Unit")]
  public void AnIntraPictureIsARawRasterOfPaletteIndices() {
    var decoder = EaCmvVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _Header(4, 4)), out _);
    var raster = Enumerable.Range(1, 16).Select(static i => (byte)i).ToArray();

    Assert.That(decoder.TryDecode(new(0, _IntraFrame(raster)), out var picture), Is.True);

    Assert.Multiple(() => {
      Assert.That(picture.PixelData, Is.EqualTo(raster));
      Assert.That(picture.Width, Is.EqualTo(4));
      Assert.That(picture.Height, Is.EqualTo(4));
      Assert.That(picture.Format, Is.EqualTo(PixelFormat.Indexed8));
    });
  }

  [Test]
  [Category("Unit")]
  public void HeaderAndPictureMayShareOnePacket() {
    var decoder = EaCmvVideoDecoder.Create(_Stream());
    var raster = _Fill(4, 4, 37);

    Assert.That(decoder.TryDecode(new(0, _Packet(_Header(4, 4), _IntraFrame(raster))), out var picture), Is.True);

    Assert.That(picture.PixelData, Is.EqualTo(raster));
  }

  [Test]
  [Category("Unit")]
  public void ThePaletteIsPlainEightBitRgbAndPartialHeadersPreserveOtherEntries() {
    var decoder = EaCmvVideoDecoder.Create(_Stream());
    var first = new byte[768];
    first[0] = 10;
    first[1] = 20;
    first[2] = 30;
    first[15] = 111;
    decoder.TryDecode(new(0, _Header(4, 4, 0, 256, first)), out _);

    var partial = new byte[] { 222, 223, 224 };
    decoder.TryDecode(new(0, _Header(4, 4, 5, 1, partial)), out _);
    decoder.TryDecode(new(0, _IntraFrame(_Fill(4, 4, 0))), out var picture);

    Assert.Multiple(() => {
      Assert.That(picture.Palette![0..3], Is.EqualTo(new byte[] { 10, 20, 30 }));
      Assert.That(picture.Palette[15..18], Is.EqualTo(partial));
    });
  }

  [Test]
  [Category("Unit")]
  public void DirectMotionCopiesFromTheLastPicture() {
    var decoder = EaCmvVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _Header(4, 4)), out _);
    decoder.TryDecode(new(0, _IntraFrame(_Fill(4, 4, 9))), out _);

    decoder.TryDecode(new(0, _InterFrame([0x77], [])), out var picture);

    Assert.That(picture.PixelData, Is.EqualTo(_Fill(4, 4, 9)));
  }

  [Test]
  [Category("Unit")]
  public void EscapedMotionCopiesFromTheDistinctSecondLastPicture() {
    var decoder = EaCmvVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _Header(4, 4)), out _);
    decoder.TryDecode(new(0, _IntraFrame(_Fill(4, 4, 1))), out _);
    decoder.TryDecode(new(0, _InterFrame([0xFF], [0xFF, .. _Fill(4, 4, 2)])), out _);

    decoder.TryDecode(new(0, _InterFrame([0xFF], [0x77])), out var picture);

    Assert.That(picture.PixelData, Is.EqualTo(_Fill(4, 4, 1)));
  }

  [Test]
  [Category("Unit")]
  public void ASecondLastReferenceBeforeTwoPicturesRefusesInsteadOfAliasingLast() {
    var decoder = EaCmvVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _Header(4, 4)), out _);
    decoder.TryDecode(new(0, _IntraFrame(_Fill(4, 4, 42))), out _);

    var failure = Assert.Throws<InvalidDataException>(
      () => decoder.TryDecode(new(0, _InterFrame([0xFF], [0x77])), out _));

    Assert.That(failure!.Message, Does.Contain("second-last"));
  }

  [Test]
  [Category("Unit")]
  public void ADoubleEscapeReadsSixteenRawPixelsEvenWithoutAReferencePicture() {
    var decoder = EaCmvVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _Header(4, 4)), out _);
    var raw = Enumerable.Range(100, 16).Select(static i => (byte)i).ToArray();

    Assert.That(decoder.TryDecode(new(0, _InterFrame([0xFF], [0xFF, .. raw])), out var picture), Is.True);

    Assert.That(picture.PixelData, Is.EqualTo(raw));
  }

  [Test]
  [Category("Unit")]
  public void EveryNonZeroFrameTypeUsesTheInterSyntax() {
    var decoder = EaCmvVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _Header(4, 4)), out _);
    decoder.TryDecode(new(0, _IntraFrame(_Fill(4, 4, 5))), out _);

    decoder.TryDecode(new(0, _InterFrame([0x77], [], frameType: 2)), out var picture);

    Assert.That(picture.PixelData, Is.EqualTo(_Fill(4, 4, 5)));
  }

  [Test]
  [Category("Unit")]
  public void AVectorOutsideThePictureReadsZero() {
    var decoder = EaCmvVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _Header(4, 4)), out _);
    decoder.TryDecode(new(0, _IntraFrame(_Fill(4, 4, 9))), out _);

    decoder.TryDecode(new(0, _InterFrame([0x70], [])), out var picture);

    Assert.That(picture.PixelData, Is.EqualTo(_Fill(4, 4, 0)));
  }

  [Test]
  [Category("Unit")]
  public void OddGeometryIsValidForIntraPictures() {
    var decoder = EaCmvVideoDecoder.Create(_Stream());
    var raster = Enumerable.Range(0, 15).Select(static i => (byte)i).ToArray();

    decoder.TryDecode(new(0, _Header(5, 3)), out _);
    decoder.TryDecode(new(0, _IntraFrame(raster)), out var picture);

    Assert.That(picture.PixelData, Is.EqualTo(raster));
  }

  [Test]
  [Category("Unit")]
  public void OddGeometryInterPicturesRefuseBecauseNoEdgeBlockSyntaxIsDefined() {
    var decoder = EaCmvVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _Header(5, 4)), out _);
    decoder.TryDecode(new(0, _IntraFrame(_Fill(5, 4, 1))), out _);

    var failure = Assert.Throws<NotSupportedException>(
      () => decoder.TryDecode(new(0, _InterFrame([0x77], [])), out _));

    Assert.That(failure!.Message, Does.Contain("complete block"));
  }

  [Test]
  [Category("Unit")]
  public void ASizeChangeDropsMotionHistoryAndTheNewIntraPictureDecodes() {
    var decoder = EaCmvVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _Header(4, 4)), out _);
    decoder.TryDecode(new(0, _IntraFrame(_Fill(4, 4, 1))), out _);
    decoder.TryDecode(new(0, _Header(8, 4)), out _);

    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, _InterFrame([0x77, 0x77], [])), out _));

    decoder.TryDecode(new(0, _IntraFrame(_Fill(8, 4, 7))), out var picture);
    Assert.That(picture.PixelData, Is.EqualTo(_Fill(8, 4, 7)));
  }

  [Test]
  [Category("Unit")]
  public void EndResetsGeometryReferencesAndPalette() {
    var decoder = EaCmvVideoDecoder.Create(_Stream());
    var colours = new byte[768];
    colours[0] = 99;
    decoder.TryDecode(new(0, _Header(4, 4, 0, 256, colours)), out _);
    decoder.TryDecode(new(0, _IntraFrame(_Fill(4, 4, 0))), out _);

    Assert.That(decoder.TryDecode(new(0, _Chunk("MVIe", [])), out _), Is.False);
    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, _IntraFrame(_Fill(4, 4, 0))), out _));
  }

  [Test]
  [Category("Unit")]
  public void MalformedPacketsRefuseByName() {
    var decoder = EaCmvVideoDecoder.Create(_Stream());

    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, new byte[4]), out _));

    var tooShort = _Chunk("MVIh", new byte[16]);
    BinaryPrimitives.WriteUInt32LittleEndian(tooShort.AsSpan(4), 1000);
    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, tooShort), out _));

    var unknown = _Chunk("XXXX", []);
    Assert.Throws<NotSupportedException>(() => decoder.TryDecode(new(0, unknown), out _));
  }

  [Test]
  [Category("Unit")]
  public void PictureAndReferenceBufferUnderflowsRefuse() {
    var decoder = EaCmvVideoDecoder.Create(_Stream());
    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, _IntraFrame([0])), out _));

    decoder.TryDecode(new(0, _Header(4, 4)), out _);
    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, _IntraFrame(new byte[4])), out _));
    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, _InterFrame([0x77], [])), out _));

    decoder.TryDecode(new(0, _IntraFrame(_Fill(4, 4, 0))), out _);
    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, _InterFrame([0xFF], [])), out _));
  }

  private static MediaStreamInfo _Stream() => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("cmv "),
  };

  private static byte[] _Fill(int width, int height, byte value) {
    var result = new byte[width * height];
    Array.Fill(result, value);
    return result;
  }

  private static byte[] _Packet(params byte[][] chunks) => chunks.SelectMany(static chunk => chunk).ToArray();

  private static byte[] _Chunk(string fourCc, byte[] payload) {
    var chunk = new byte[8 + payload.Length];
    System.Text.Encoding.ASCII.GetBytes(fourCc).CopyTo(chunk, 0);
    BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(4), checked((uint)chunk.Length));
    payload.CopyTo(chunk, 8);
    return chunk;
  }

  private static byte[] _Header(int width, int height, int palStart = 0, int palCount = 0, byte[]? colours = null) {
    colours ??= [];
    var payload = new byte[0x10 + colours.Length];
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4), checked((ushort)width));
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(6), checked((ushort)height));
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(10), 10);
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(12), checked((ushort)palStart));
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(14), checked((ushort)palCount));
    colours.CopyTo(payload, 0x10);
    return _Chunk("MVIh", payload);
  }

  private static byte[] _IntraFrame(byte[] raster) {
    var payload = new byte[2 + raster.Length];
    raster.CopyTo(payload, 2);
    return _Chunk("MVIf", payload);
  }

  private static byte[] _InterFrame(byte[] motion, byte[] escapes, ushort frameType = 1) {
    var payload = new byte[2 + motion.Length + escapes.Length];
    BinaryPrimitives.WriteUInt16LittleEndian(payload, frameType);
    motion.CopyTo(payload, 2);
    escapes.CopyTo(payload, 2 + motion.Length);
    return _Chunk("MVIf", payload);
  }
}
