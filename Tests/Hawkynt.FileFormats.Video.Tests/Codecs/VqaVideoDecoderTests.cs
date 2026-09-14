using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Codecs.Vqa;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

[TestFixture]
public sealed class VqaVideoDecoderTests {

  [Test]
  [Category("Unit")]
  public void TheCodecIsRegisteredForAllDefinedVqaVersions() {
    Assert.That(VideoFormatRegistry.AllCodecs.Select(c => c.CodecName), Does.Contain("Westwood VQA Video"));
    Assert.That(VideoFormatRegistry.CreateDecoder(_Stream(version: 1)), Is.InstanceOf<VqaVideoDecoder>());
    Assert.That(VideoFormatRegistry.CreateDecoder(_Stream(version: 2)), Is.InstanceOf<VqaVideoDecoder>());
    Assert.That(VideoFormatRegistry.CreateDecoder(_Stream(version: 3, highColour: true)), Is.InstanceOf<VqaVideoDecoder>());
  }

  [Test]
  [Category("Unit")]
  public void VersionTwoSplitTableUsesCodebookAndSolidBlocks() {
    var decoder = VqaVideoDecoder.Create(_Stream(width: 8, height: 2));
    byte[] codebook = [10, 11, 12, 13, 20, 21, 22, 23];
    var table = new byte[] { 0, 7, 0, 0x0f };

    decoder.TryDecode(new(0, _Picture(_Chunk("CBF0", codebook), _Chunk("VPT0", table))), out var picture);

    Assert.Multiple(() => {
      Assert.That(picture.PixelData[..4], Is.EqualTo(new byte[] { 10, 11, 12, 13 }));
      Assert.That(picture.PixelData[4..8], Is.EqualTo(new byte[] { 7, 7, 7, 7 }));
      Assert.That(picture.PixelData[8..12], Is.EqualTo(new byte[] { 20, 21, 22, 23 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void VersionOneUsesSequentialLittleEndianPointersAndInvertedSolidColour() {
    var decoder = VqaVideoDecoder.Create(_Stream(version: 1, width: 8, height: 2));
    byte[] codebook = [1, 2, 3, 4, 5, 6, 7, 8];
    // entry zero, then solid colour 7 => LoVal = 255 - 7 = 248, HiVal = ff
    byte[] table = [0, 0, 248, 0xff];

    decoder.TryDecode(new(0, _Picture(_Chunk("CBF0", codebook), _Chunk("VPT0", table))), out var picture);

    Assert.Multiple(() => {
      Assert.That(picture.PixelData[..4], Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
      Assert.That(picture.PixelData[4..8], Is.EqualTo(new byte[] { 7, 7, 7, 7 }));
      Assert.That(picture.PixelData[8..12], Is.EqualTo(new byte[] { 5, 6, 7, 8 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void FourPixelTallVersionTwoUsesFfAsTheSolidSentinel() {
    var decoder = VqaVideoDecoder.Create(_Stream(width: 4, height: 4, blockHeight: 4));

    decoder.TryDecode(new(0, _Picture(_Chunk("VPT0", new byte[] { 23, 0xff }))), out var picture);

    Assert.That(picture.PixelData, Is.EqualTo(Enumerable.Repeat((byte)23, 16)));
  }

  [Test]
  [Category("Unit")]
  public void PartialCompressedCodebookBecomesCurrentAfterTheCompletingPicture() {
    var decoder = VqaVideoDecoder.Create(_Stream(width: 4, height: 2, codebookParts: 2));
    var oldBook = Enumerable.Repeat((byte)1, 8).ToArray();
    var newBook = Enumerable.Repeat((byte)9, 8).ToArray();
    byte[] table = [0, 0];

    decoder.TryDecode(new(0, _Picture(_Chunk("CBF0", oldBook), _Chunk("VPT0", table))), out _);
    var compressed = VqaFormat80.CompressLiterals(newBook);
    var split = compressed.Length / 2;

    decoder.TryDecode(new(0, _Picture(_Chunk("CBPZ", compressed[..split]), _Chunk("VPT0", table))), out var before);
    decoder.TryDecode(new(0, _Picture(_Chunk("CBPZ", compressed[split..]), _Chunk("VPT0", table))), out var completing);
    decoder.TryDecode(new(0, _Picture(_Chunk("VPT0", table))), out var after);

    Assert.Multiple(() => {
      Assert.That(before.PixelData, Is.EqualTo(Enumerable.Repeat((byte)1, 8)));
      Assert.That(completing.PixelData, Is.EqualTo(Enumerable.Repeat((byte)1, 8)));
      Assert.That(after.PixelData, Is.EqualTo(Enumerable.Repeat((byte)9, 8)));
    });
  }

  [Test]
  [Category("Unit")]
  public void PaletteValuesAreSixBitAndPersist() {
    var decoder = VqaVideoDecoder.Create(_Stream(width: 4, height: 2));
    var palette = new byte[6];
    palette[0] = 63;
    palette[3] = 32;
    byte[] table = [0, 0x0f];

    decoder.TryDecode(new(0, _Picture(_Chunk("CPL0", palette), _Chunk("VPT0", table))), out var picture);

    Assert.Multiple(() => {
      Assert.That(picture.Palette![0], Is.EqualTo(255));
      Assert.That(picture.Palette[3], Is.EqualTo(ChannelScaling.Expand6(32)));
      Assert.That(picture.Palette[6], Is.Zero);
    });
  }

  [Test]
  [Category("Unit")]
  public void HiColorSkipKeepsThePreviousFrameBlock() {
    var decoder = VqaVideoDecoder.Create(_Stream(version: 3, highColour: true, width: 8, height: 2));
    var red = _SolidVector(0x7c00);
    var green = _SolidVector(0x03e0);
    var blue = _SolidVector(0x001f);

    var firstPointers = _Words((3 << 13) | 0, (3 << 13) | 1);
    decoder.TryDecode(new(0, _Picture(_Chunk("CBF0", _Codebook(red, green)), _Chunk("VPTR", firstPointers))), out _);

    // skip the left block, replace the right block with blue
    var secondPointers = _Words(1, (3 << 13) | 0);
    decoder.TryDecode(new(0, _Picture(_Chunk("CBF0", _Codebook(blue)), _Chunk("VPTR", secondPointers))), out var second);

    Assert.Multiple(() => {
      Assert.That(second.PixelData[..3], Is.EqualTo(new byte[] { 255, 0, 0 }));
      Assert.That(second.PixelData[4 * 3..4 * 3 + 3], Is.EqualTo(new byte[] { 0, 0, 255 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void HiColorTypeOneRepeatsAFirst256Vector() {
    var decoder = VqaVideoDecoder.Create(_Stream(version: 3, highColour: true, width: 8, height: 2));
    var pointers = _Words((1 << 13) | 0); // minimum count is two blocks

    decoder.TryDecode(new(0, _Picture(_Chunk("CBF0", _Codebook(_SolidVector(0x7c00))), _Chunk("VPTR", pointers))), out var picture);

    Assert.That(picture.PixelData.Where((_, i) => i % 3 == 0), Is.All.EqualTo(255));
  }

  [Test]
  [Category("Unit")]
  public void HiColorTypeTwoUsesFollowingByteIndices() {
    var decoder = VqaVideoDecoder.Create(_Stream(version: 3, highColour: true, width: 12, height: 2));
    var pointerBytes = new List<byte>(_Words((2 << 13) | 0));
    pointerBytes.Add(1);
    pointerBytes.Add(2);

    decoder.TryDecode(new(0, _Picture(
      _Chunk("CBF0", _Codebook(_SolidVector(0x7c00), _SolidVector(0x03e0), _SolidVector(0x001f))),
      _Chunk("VPTR", pointerBytes.ToArray()))), out var picture);

    Assert.Multiple(() => {
      Assert.That(picture.PixelData[..3], Is.EqualTo(new byte[] { 255, 0, 0 }));
      Assert.That(picture.PixelData[12..15], Is.EqualTo(new byte[] { 0, 255, 0 }));
      Assert.That(picture.PixelData[24..27], Is.EqualTo(new byte[] { 0, 0, 255 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void HiColorTypeFiveRepeatsAnArbitraryVector() {
    var decoder = VqaVideoDecoder.Create(_Stream(version: 3, highColour: true, width: 8, height: 2));
    var pointers = new List<byte>(_Words((5 << 13) | 0));
    pointers.Add(2);

    decoder.TryDecode(new(0, _Picture(_Chunk("CBF0", _Codebook(_SolidVector(0x03e0))), _Chunk("VPTR", pointers.ToArray()))), out var picture);

    Assert.That(picture.PixelData.Where((_, i) => i % 3 == 1), Is.All.EqualTo(255));
  }

  [Test]
  [Category("Unit")]
  public void HiColorAlphaSkipPreservesIndividualPreviousPixels() {
    var decoder = VqaVideoDecoder.Create(_Stream(version: 3, highColour: true, width: 4, height: 2, overlay: true));
    var blue = _SolidVector(0x001f);
    decoder.TryDecode(new(0, _Picture(_Chunk("CBF0", _Codebook(blue)), _Chunk("VPTR", _Words((3 << 13) | 0)))), out _);

    var overlay = _SolidVector(0x7c00);
    overlay[0] |= 0x8000;
    decoder.TryDecode(new(0, _Picture(_Chunk("CBF0", _Codebook(overlay)), _Chunk("VPTR", _Words((4 << 13) | 0)))), out var picture);

    Assert.Multiple(() => {
      Assert.That(picture.PixelData[..3], Is.EqualTo(new byte[] { 0, 0, 255 }), "transparent pixel stays blue");
      Assert.That(picture.PixelData[3..6], Is.EqualTo(new byte[] { 255, 0, 0 }), "opaque pixel becomes red");
    });
  }

  [Test]
  [Category("Unit")]
  public void HiColorVprzUsesNormalFormat80() {
    var decoder = VqaVideoDecoder.Create(_Stream(version: 3, highColour: true, width: 4, height: 2));
    var pointers = _Words((3 << 13) | 0);

    decoder.TryDecode(new(0, _Picture(
      _Chunk("CBF0", _Codebook(_SolidVector(0x7c00))),
      _Chunk("VPRZ", VqaFormat80.CompressLiterals(pointers)))), out var picture);

    Assert.That(picture.PixelData[..3], Is.EqualTo(new byte[] { 255, 0, 0 }));
  }

  [Test]
  [Category("Unit")]
  public void ReservedHiColorPointerTypeRefuses() {
    var decoder = VqaVideoDecoder.Create(_Stream(version: 3, highColour: true, width: 4, height: 2));

    var failure = Assert.Throws<InvalidDataException>(() =>
      decoder.TryDecode(new(0, _Picture(_Chunk("VPTR", _Words(7 << 13)))), out _));

    Assert.That(failure!.Message, Does.Contain("type 7"));
  }

  [Test]
  [Category("Unit")]
  public void ShortUncompressedIndexTableRefusesByName() {
    var decoder = VqaVideoDecoder.Create(_Stream(width: 8, height: 2));

    var failure = Assert.Throws<InvalidDataException>(() =>
      decoder.TryDecode(new(0, _Picture(_Chunk("VPT0", new byte[] { 0 }))), out _));

    Assert.That(failure!.Message, Does.Contain("index table"));
  }

  [Test]
  [Category("Unit")]
  public void UnsupportedGeometryAndContradictoryVersionRefuse() {
    Assert.Throws<NotSupportedException>(() => VqaVideoDecoder.Create(_Stream(width: 9, height: 2)));
    Assert.Throws<NotSupportedException>(() => VqaVideoDecoder.Create(_Stream(version: 1, highColour: true)));
    Assert.Throws<InvalidDataException>(() => VqaVideoDecoder.Create(_Stream(version: 3, highColour: false)));
  }

  private static MediaStreamInfo _Stream(
    int version = 2,
    bool highColour = false,
    int width = 4,
    int height = 2,
    int blockHeight = 2,
    int codebookParts = 8,
    bool overlay = false) {
    var header = new byte[42];
    BinaryPrimitives.WriteUInt16LittleEndian(header, (ushort)version);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(2), (ushort)((highColour ? 0x10 : 0) | (overlay ? 0x04 : 0)));
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6), (ushort)width);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(8), (ushort)height);
    header[10] = 4;
    header[11] = (byte)blockHeight;
    header[12] = 15;
    header[13] = (byte)codebookParts;
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(14), highColour ? (ushort)0 : (ushort)256);
    return new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("WSVQ"),
      Width = width,
      Height = height,
      BitsPerPixel = highColour ? 15 : 8,
      CodecPrivateData = header,
    };
  }

  private static byte[] _Picture(params byte[][] chunks) => chunks.SelectMany(x => x).ToArray();

  private static byte[] _Chunk(string id, byte[] payload) {
    var chunk = new byte[8 + payload.Length + (payload.Length & 1)];
    System.Text.Encoding.ASCII.GetBytes(id).CopyTo(chunk, 0);
    BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(4), (uint)payload.Length);
    payload.CopyTo(chunk, 8);
    return chunk;
  }

  private static ushort[] _SolidVector(ushort colour) => Enumerable.Repeat(colour, 8).ToArray();

  private static byte[] _Codebook(params ushort[][] vectors) {
    var result = new byte[vectors.Sum(v => v.Length) * 2];
    var at = 0;
    foreach (var vector in vectors)
      foreach (var pixel in vector) {
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(at), pixel);
        at += 2;
      }
    return result;
  }

  private static byte[] _Words(params int[] values) {
    var result = new byte[values.Length * 2];
    for (var i = 0; i < values.Length; ++i)
      BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(i * 2), checked((ushort)values[i]));
    return result;
  }
}
