using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// Hand-built MSS1 arithmetic streams covering keyframe construction, persistent interframe state,
/// header validation and registry discovery. The coded packets are deliberately tiny so the tests
/// exercise the adaptive arithmetic/model path without depending on an external sample corpus.
/// </summary>
[TestFixture]
public sealed class Mss1VideoDecoderTests {

  [Test]
  [Category("Unit")]
  public void TheMss1CodeIsTaken()
    => Assert.That(Mss1VideoDecoder.Accepts(_Stream(1, 1)), Is.True);

  [Test]
  [Category("Unit")]
  public void AnotherCodecsCodeIsNotTaken() {
    var stream = _Stream(1, 1);
    stream = new() {
      Index = stream.Index,
      Kind = stream.Kind,
      Codec = CodecTag.FromCharacters("MSS2"),
      Width = stream.Width,
      Height = stream.Height,
      CodecPrivateData = stream.CodecPrivateData,
    };

    Assert.That(Mss1VideoDecoder.Accepts(stream), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void OnePixelKeyframeWalksArithmeticSplitIntraAndPixelModels() {
    var decoder = Mss1VideoDecoder.Create(_Stream(1, 1, firstPaletteColour: [0x12, 0x34, 0x56]));

    // Arithmetic decisions, in order:
    // keyframe=0, split=SPLIT_NONE(2), intra-region=solid(0), cache symbol=0.
    // 0x284C plus zero padding is one concrete interval seed for exactly that decision chain.
    Assert.That(decoder.TryDecode(new(0, new byte[] { 0x28, 0x4C, 0x00, 0x00 }), out var frame), Is.True);

    Assert.That(frame.Width, Is.EqualTo(1));
    Assert.That(frame.Height, Is.EqualTo(1));
    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 0x12, 0x34, 0x56 }));
  }

  [Test]
  [Category("Unit")]
  public void InterframeCanKeepThePersistentPictureFromTheKeyframe() {
    var decoder = Mss1VideoDecoder.Create(_Stream(1, 1, firstPaletteColour: [7, 8, 9]));
    Assert.That(decoder.TryDecode(new(0, new byte[] { 0x28, 0x4C, 0x00, 0x00 }), out _), Is.True);

    // Continuing from the keyframe's adaptive split-model state:
    // interframe=1, split=SPLIT_NONE(2), inter-region=single action(0), cache escape,
    // full-model symbol=0x02. In MSS1 that action leaves the persistent palette picture unchanged.
    Assert.That(decoder.TryDecode(new(0, new byte[] { 0xEA, 0x8B, 0x00, 0x00, 0x00 }), out var frame), Is.True);
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 7, 8, 9 }));
  }

  [Test]
  [Category("Unit")]
  public void PaletteCountArithmeticPathAcceptsZeroChangedColours() {
    var decoder = Mss1VideoDecoder.Create(_Stream(1, 1, freeColours: 1, firstPaletteColour: [0x21, 0x43, 0x65]));

    // keyframe=0, changed-colour count=0 via GetNumber(2), then the same no-split solid pixel path.
    Assert.That(decoder.TryDecode(new(0, new byte[] { 0x14, 0x26, 0x00, 0x00 }), out var frame), Is.True);
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 0x21, 0x43, 0x65 }));
  }

  [Test]
  [Category("Unit")]
  public void InterframeBeforeAKeyframeRefuses() {
    var decoder = Mss1VideoDecoder.Create(_Stream(1, 1));

    // Initial arithmetic value 0x8000 selects the interframe half of the first binary decision.
    Assert.Throws<InvalidDataException>(
      () => decoder.TryDecode(new(0, new byte[] { 0x80, 0x00 }), out _));
  }

  [Test]
  [Category("Unit")]
  public void HeaderMustContainTheMssPaletteAndMetadata() {
    var stream = _Stream(1, 1);
    stream = new() {
      Index = stream.Index,
      Kind = stream.Kind,
      Codec = stream.Codec,
      Width = stream.Width,
      Height = stream.Height,
      CodecPrivateData = new byte[40 + 52 + 256 * 3 - 1],
    };

    Assert.Throws<InvalidDataException>(() => Mss1VideoDecoder.Create(stream));
  }

  [Test]
  [Category("Unit")]
  public void HeaderRejectsMoreThanTwoHundredFiftySixChangeableColours() {
    var stream = _Stream(1, 1);
    var format = stream.CodecPrivateData.ToArray();
    BinaryPrimitives.WriteUInt32BigEndian(format.AsSpan(40 + 48), 257);
    stream = new() {
      Index = stream.Index,
      Kind = stream.Kind,
      Codec = stream.Codec,
      Width = stream.Width,
      Height = stream.Height,
      CodecPrivateData = format,
    };

    Assert.Throws<InvalidDataException>(() => Mss1VideoDecoder.Create(stream));
  }

  [Test]
  [Category("Unit")]
  public void HeaderRejectsMss2EraVersions() {
    var stream = _Stream(1, 1);
    var format = stream.CodecPrivateData.ToArray();
    BinaryPrimitives.WriteUInt32BigEndian(format.AsSpan(40 + 4), 2);
    stream = new() {
      Index = stream.Index,
      Kind = stream.Kind,
      Codec = stream.Codec,
      Width = stream.Width,
      Height = stream.Height,
      CodecPrivateData = format,
    };

    Assert.Throws<InvalidDataException>(() => Mss1VideoDecoder.Create(stream));
  }

  [Test]
  [Category("Unit")]
  public void TheCodecIsRegistered() {
    var stream = _Stream(1, 1);

    Assert.That(VideoFormatRegistry.AllCodecs.Select(codec => codec.CodecName), Does.Contain("MS Screen 1"));
    Assert.That(VideoFormatRegistry.CanDecode(stream), Is.True);
    Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<Mss1VideoDecoder>());
  }

  private static MediaStreamInfo _Stream(
    int width,
    int height,
    int freeColours = 0,
    byte[]? firstPaletteColour = null
  ) {
    const int bitmapHeaderSize = 40;
    const int extraSize = 52 + 256 * 3;
    var format = new byte[bitmapHeaderSize + extraSize];

    BinaryPrimitives.WriteUInt32LittleEndian(format, bitmapHeaderSize);
    BinaryPrimitives.WriteInt32LittleEndian(format.AsSpan(4), width);
    BinaryPrimitives.WriteInt32LittleEndian(format.AsSpan(8), height);
    BinaryPrimitives.WriteInt16LittleEndian(format.AsSpan(12), 1);
    BinaryPrimitives.WriteInt16LittleEndian(format.AsSpan(14), 8);
    "MSS1"u8.CopyTo(format.AsSpan(16));

    var extra = format.AsSpan(bitmapHeaderSize);
    BinaryPrimitives.WriteUInt32BigEndian(extra, extraSize);
    BinaryPrimitives.WriteUInt32BigEndian(extra[4..], 0); // MSS1 header generation
    BinaryPrimitives.WriteUInt32BigEndian(extra[12..], checked((uint)width));
    BinaryPrimitives.WriteUInt32BigEndian(extra[16..], checked((uint)height));
    BinaryPrimitives.WriteUInt32BigEndian(extra[20..], checked((uint)width));
    BinaryPrimitives.WriteUInt32BigEndian(extra[24..], checked((uint)height));
    BinaryPrimitives.WriteUInt32BigEndian(extra[48..], checked((uint)freeColours));

    if (firstPaletteColour is { Length: 3 })
      firstPaletteColour.CopyTo(extra[52..55]);

    return new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("MSS1"),
      Width = width,
      Height = height,
      BitsPerPixel = 8,
      CodecPrivateData = format,
    };
  }
}
