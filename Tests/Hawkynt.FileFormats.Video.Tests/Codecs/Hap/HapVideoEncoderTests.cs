using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Codecs.Hap;
using FileFormat.Core;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// The Hap encoder: what it writes, what comes back untouched, and what it refuses.
/// </summary>
/// <remarks>
/// The encoder as a whole was measured against ffmpeg's own <c>hap</c> encoder and decoder — see
/// <see cref="HapVideoEncoder"/>'s own remarks for the two comparisons and their numbers. What these
/// tests add is the part no oracle states: which pictures the format holds exactly and therefore must
/// come back untouched, the shape of the frame around the texture, and every refusal, none of which a
/// well-formed comparison can be made to reach.
/// </remarks>
[TestFixture]
public class HapVideoEncoderTests {

  private static MediaStreamInfo _Stream(string code, int width, int height, int index = 0) => new() {
    Index = index,
    Kind = MediaStreamKind.Video,
    Codec = code.Length == 0 ? CodecTag.None : CodecTag.FromCharacters(code),
    Width = width,
    Height = height,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };

  private static RawImage _Rgba(int width, int height, byte[] pixels)
    => new() { Width = width, Height = height, Format = PixelFormat.Rgba32, PixelData = pixels };

  private static byte[] _Encode(string code, int width, int height, byte[] rgba) {
    var encoder = HapVideoEncoder.Create(_Stream(code, width, height));
    Assert.That(encoder.TryEncode(_Rgba(width, height, rgba), 0, out var packet), Is.True);
    return packet.Data.ToArray();
  }

  private static RawImage _RoundTrip(string code, int width, int height, byte[] rgba) {
    var encoder = HapVideoEncoder.Create(_Stream(code, width, height));
    Assert.That(encoder.TryEncode(_Rgba(width, height, rgba), 0, out var packet), Is.True);

    var decoder = HapDecoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(packet, out var picture), Is.True);
    return picture;
  }

  /// <summary>Every eight-bit value the block decode's own 5-6-5 expansion can produce.</summary>
  private static (List<byte> Fives, List<byte> Sixes) _Grid() {
    List<byte> fives = [];
    List<byte> sixes = [];
    for (var value = 0; value <= 255; ++value) {
      if (HapBlockDecoding.TryPack565((byte)value, 0, 0, out _))
        fives.Add((byte)value);
      if (HapBlockDecoding.TryPack565(0, (byte)value, 0, out _))
        sixes.Add((byte)value);
    }

    return (fives, sixes);
  }

  /// <summary>A picture whose every 4x4 block holds the colours the chooser names for it.</summary>
  private static byte[] _ByBlock(int width, int height, Func<int, int, int, (byte R, byte G, byte B, byte A)> colour) {
    var pixels = new byte[width * height * 4];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var (r, g, b, a) = colour(x / 4 + y / 4 * (width / 4), x % 4, y % 4);
        var at = (y * width + x) * 4;
        pixels[at] = r;
        pixels[at + 1] = g;
        pixels[at + 2] = b;
        pixels[at + 3] = a;
      }

    return pixels;
  }

  // ============================================================================================
  // What the format holds exactly
  // ============================================================================================

  [TestCase("Hap1")]
  [TestCase("Hap5")]
  [Category("Unit")]
  public void EveryColourTheBlockDecodeCanStateComesBackUntouched(string code) {
    var (fives, sixes) = _Grid();
    var colours = new List<(byte, byte, byte, byte)>();
    foreach (var r in fives)
      foreach (var g in sixes)
        foreach (var b in fives)
          colours.Add((r, g, b, (byte)255));

    const int across = 64;
    var rows = (colours.Count + across - 1) / across;
    var width = across * 4;
    var height = rows * 4;
    var source = _ByBlock(width, height, (block, _, _) => colours[Math.Min(block, colours.Count - 1)]);

    var back = _RoundTrip(code, width, height, source);
    var channels = back.Format == PixelFormat.Rgba32 ? 4 : 3;

    for (var i = 0; i < width * height; ++i)
      for (var c = 0; c < 3; ++c)
        if (back.PixelData[i * channels + c] != source[i * 4 + c])
          Assert.Fail(
            $"pixel {i} channel {c}: {back.PixelData[i * channels + c]} came back for {source[i * 4 + c]}, "
            + "which the block decode's own grid states exactly.");

    Assert.Pass($"{colours.Count} colours, one to a block, all exact.");
  }

  [Test]
  [Category("Unit")]
  public void TwoSuchColoursInOneBlockAlsoComeBackUntouched() {
    var (fives, sixes) = _Grid();
    var first = ((byte)fives[7], (byte)sixes[40], (byte)fives[2], (byte)255);
    var second = ((byte)fives[25], (byte)sixes[3], (byte)fives[30], (byte)255);

    var source = _ByBlock(8, 8, (_, x, y) => (x + y) % 2 == 0 ? first : second);
    var back = _RoundTrip("Hap1", 8, 8, source);

    for (var i = 0; i < 8 * 8; ++i)
      for (var c = 0; c < 3; ++c)
        Assert.That(back.PixelData[i * 3 + c], Is.EqualTo(source[i * 4 + c]), $"pixel {i} channel {c}");
  }

  [Test]
  [Category("Unit")]
  public void EveryAlphaValueSurvivesWhereABlockHoldsOnlyOne() {
    var source = _ByBlock(256, 16, (block, _, _) => ((byte)128, (byte)128, (byte)128, (byte)block));
    var back = _RoundTrip("Hap5", 256, 16, source);

    Assert.That(back.Format, Is.EqualTo(PixelFormat.Rgba32));
    for (var i = 0; i < 256 * 16; ++i)
      Assert.That(back.PixelData[i * 4 + 3], Is.EqualTo(source[i * 4 + 3]), $"alpha of pixel {i}");
  }

  [Test]
  [Category("Unit")]
  public void AGreyOffThatGridLandsWithinTwoOfWhereItStarted() {
    // Only seven of the 256 greys are on the grid at all — a grey needs its value in the image of
    // both the five-bit and the six-bit widening, and those two agree seldom. The rest are what the
    // reference's own endpoint search lands on, which is close and not exact.
    var source = _ByBlock(256, 16, (block, _, _) => ((byte)block, (byte)block, (byte)block, (byte)255));
    var back = _RoundTrip("Hap1", 256, 16, source);

    var exact = 0;
    for (var block = 0; block < 256; ++block) {
      var pixel = block / 64 * 4 * 256 + block % 64 * 4;
      var value = back.PixelData[pixel * 3];
      Assert.That(Math.Abs(value - block), Is.LessThanOrEqualTo(2), $"grey {block}");
      if (value == block && back.PixelData[pixel * 3 + 1] == block && back.PixelData[pixel * 3 + 2] == block)
        ++exact;
    }

    Assert.That(exact, Is.GreaterThanOrEqualTo(7), "at least the greys the grid states outright");
  }

  // ============================================================================================
  // The frame around the texture
  // ============================================================================================

  [TestCase("Hap1", 0x0B, 8)]
  [TestCase("Hap5", 0x0E, 16)]
  [TestCase("HapY", 0x0F, 16)]
  [Category("Unit")]
  public void TheFrameIsOneLongHeaderedSectionNamingItsPixelFormat(string code, int formatNibble, int blockBytes) {
    var random = new Random(7);
    var source = new byte[16 * 16 * 4];
    random.NextBytes(source);

    var frame = _Encode(code, 16, 16, source);

    Assert.Multiple(() => {
      Assert.That(frame[0], Is.Zero, "an eight-byte header states nothing in its first three bytes");
      Assert.That(frame[1], Is.Zero);
      Assert.That(frame[2], Is.Zero);
      Assert.That(frame[3] & 0x0F, Is.EqualTo(formatNibble));
      Assert.That(frame[3] & 0xF0, Is.AnyOf(0xA0, 0xB0));
      Assert.That(frame[4] | (frame[5] << 8) | (frame[6] << 16) | (frame[7] << 24), Is.EqualTo(frame.Length - 8));
    });

    var payload = (frame[3] & 0xF0) == 0xB0
      ? HapSnappyDecoder.Decompress(frame.AsSpan(8))
      : frame[8..];

    Assert.That(payload, Has.Length.EqualTo(16 / 4 * (16 / 4) * blockBytes));
  }

  [Test]
  [Category("Unit")]
  public void APictureSnappyCannotShrinkIsWrittenUncompressed() {
    var random = new Random(11);
    var source = new byte[64 * 64 * 4];
    random.NextBytes(source);

    var frame = _Encode("Hap5", 64, 64, source);

    Assert.That(frame[3] & 0xF0, Is.EqualTo(0xA0));
    Assert.That(frame, Has.Length.EqualTo(8 + 16 * 16 * 16));
  }

  [Test]
  [Category("Unit")]
  public void APictureSnappyShrinksIsWrittenCompressedAndReadsBack() {
    var source = _ByBlock(64, 64, (_, _, _) => (10, 200, 30, 255));

    var frame = _Encode("Hap1", 64, 64, source);

    Assert.That(frame[3] & 0xF0, Is.EqualTo(0xB0), "a texture of one repeated block must compress");
    Assert.That(frame.Length, Is.LessThan(8 + 16 * 16 * 8));
    Assert.That(HapSnappyDecoder.Decompress(frame.AsSpan(8)), Has.Length.EqualTo(16 * 16 * 8));
  }

  [Test]
  [Category("Unit")]
  public void TheSnappyWriterAndReaderAgreeOverEveryShapeOfElement() {
    var random = new Random(3);
    foreach (var length in new[] { 0, 1, 59, 60, 61, 255, 256, 300, 70000 }) {
      var payload = new byte[length];
      random.NextBytes(payload);

      // Half literal, half long runs, so both the copy forms and the long literal length are reached.
      for (var i = length / 2; i < length; ++i)
        payload[i] = (byte)(i % 3);

      var compressed = HapSnappyEncoder.Compress(payload);
      Assert.That(HapSnappyDecoder.Decompress(compressed), Is.EqualTo(payload), $"length {length}");
    }
  }

  // ============================================================================================
  // The stream it describes
  // ============================================================================================

  [TestCase("Hap1", 24)]
  [TestCase("Hap5", 32)]
  [TestCase("HapY", 24)]
  [Category("Unit")]
  public void DescribesAStreamTheDecoderAccepts(string code, int bitsPerPixel) {
    var encoder = HapVideoEncoder.Create(_Stream(code, 32, 16, 2));

    var described = encoder.DescribeStream();

    Assert.Multiple(() => {
      Assert.That(described.Codec, Is.EqualTo(CodecTag.FromCharacters(code)));
      Assert.That(described.Handler, Is.EqualTo(CodecTag.FromCharacters(code)));
      Assert.That(described.Kind, Is.EqualTo(MediaStreamKind.Video));
      Assert.That(described.Index, Is.EqualTo(2));
      Assert.That(described.Width, Is.EqualTo(32));
      Assert.That(described.Height, Is.EqualTo(16));
      Assert.That(described.BitsPerPixel, Is.EqualTo(bitsPerPixel));
      Assert.That(HapDecoder.Accepts(described), Is.True);
    });
  }

  [Test]
  [Category("Unit")]
  public void AStreamNamingNoCodecAtAllIsWrittenAsHap1() {
    var encoder = HapVideoEncoder.Create(_Stream(string.Empty, 8, 8));

    Assert.That(encoder.DescribeStream().Codec, Is.EqualTo(CodecTag.FromCharacters("Hap1")));
    Assert.That(HapVideoEncoder.Codec, Is.EqualTo(CodecTag.FromCharacters("Hap1")));
    Assert.That(HapVideoEncoder.CodecName, Is.EqualTo(HapDecoder.CodecName));
  }

  [Test]
  [Category("Unit")]
  public void TheRegistryRoutesHap1ToThisEncoder() {
    var stream = _Stream("Hap1", 8, 8);

    Assert.That(Hawkynt.FileFormats.Video.VideoFormatRegistry.CanEncode(stream), Is.True);
    Assert.That(
      Hawkynt.FileFormats.Video.VideoFormatRegistry.CreateEncoder(stream),
      Is.TypeOf<HapVideoEncoder>());
  }

  [Test]
  [Category("Unit")]
  public void APacketIsAKeyFrameBecauseEveryHapFrameIs() {
    var encoder = HapVideoEncoder.Create(_Stream("Hap1", 8, 8));
    var source = _ByBlock(8, 8, (_, _, _) => (1, 2, 3, 255));

    Assert.That(encoder.TryEncode(_Rgba(8, 8, source), 42, out var packet), Is.True);
    Assert.Multiple(() => {
      Assert.That(packet.IsKeyFrame, Is.True);
      Assert.That(packet.PresentationTimestamp, Is.EqualTo(42));
      Assert.That(packet.StreamIndex, Is.Zero);
    });
  }

  [Test]
  [Category("Unit")]
  public void AnRgb24PictureIsTakenWithTheAlphaChannelHapWouldHaveDropped() {
    var rgba = _ByBlock(8, 8, (_, x, _) => (x < 2 ? (byte)0 : (byte)255, 128, 64, 255));
    var rgb = new byte[8 * 8 * 3];
    for (var i = 0; i < 8 * 8; ++i) {
      rgb[i * 3] = rgba[i * 4];
      rgb[i * 3 + 1] = rgba[i * 4 + 1];
      rgb[i * 3 + 2] = rgba[i * 4 + 2];
    }

    var encoder = HapVideoEncoder.Create(_Stream("Hap1", 8, 8));
    Assert.That(encoder.TryEncode(new() { Width = 8, Height = 8, Format = PixelFormat.Rgb24, PixelData = rgb }, 0, out var fromRgb), Is.True);
    Assert.That(fromRgb.Data.ToArray(), Is.EqualTo(_Encode("Hap1", 8, 8, rgba)));
  }

  // ============================================================================================
  // Refusals
  // ============================================================================================

  [TestCase("HapM", "Hap Q Alpha")]
  [TestCase("HapA", "Hap Alpha-Only")]
  [TestCase("Hap7", "Hap R")]
  [TestCase("HapH", "Hap HDR")]
  [Category("Unit")]
  public void ThePixelFormatsThisEncoderDoesNotWriteAreRefusedByName(string code, string named) {
    var failure = Assert.Throws<NotSupportedException>(() => HapVideoEncoder.Create(_Stream(code, 8, 8)));

    Assert.That(failure!.Message, Does.Contain(code));
    Assert.That(failure.Message, Does.Contain(named));
  }

  [Test]
  [Category("Unit")]
  public void ACodeThatIsNotHapAtAllIsRefused() {
    var failure = Assert.Throws<NotSupportedException>(() => HapVideoEncoder.Create(_Stream("MJPG", 8, 8)));
    Assert.That(failure!.Message, Does.Contain("not a Hap code"));
  }

  [TestCase(7, 8)]
  [TestCase(8, 7)]
  [TestCase(6, 6)]
  [Category("Unit")]
  public void APictureThatIsNotAWholeNumberOfBlocksIsRefused(int width, int height) {
    var failure = Assert.Throws<NotSupportedException>(() => HapVideoEncoder.Create(_Stream("Hap1", width, height)));
    Assert.That(failure!.Message, Does.Contain("texture blocks"));
  }

  [Test]
  [Category("Unit")]
  public void AnAudioStreamIsRefused() {
    var stream = _Stream("Hap1", 8, 8);
    stream = new() { Index = 0, Kind = MediaStreamKind.Audio, Codec = stream.Codec, Width = 8, Height = 8 };

    Assert.Throws<NotSupportedException>(() => HapVideoEncoder.Create(stream));
  }

  [Test]
  [Category("Unit")]
  public void AStreamWithNoPictureSizeIsRefused()
    => Assert.Throws<InvalidDataException>(() => HapVideoEncoder.Create(_Stream("Hap1", 0, 0)));

  [Test]
  [Category("Unit")]
  public void APictureOfAnotherSizeIsRefused() {
    var encoder = HapVideoEncoder.Create(_Stream("Hap1", 8, 8));
    var source = _ByBlock(12, 8, (_, _, _) => (1, 2, 3, 255));

    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(_Rgba(12, 8, source), 0, out _));
    Assert.That(failure!.Message, Does.Contain("8x8"));
  }

  [Test]
  [Category("Unit")]
  public void APictureInAnotherPixelFormatIsRefusedRatherThanConverted() {
    var encoder = HapVideoEncoder.Create(_Stream("Hap1", 8, 8));
    var grey = new RawImage { Width = 8, Height = 8, Format = PixelFormat.Gray8, PixelData = new byte[8 * 8] };

    var failure = Assert.Throws<NotSupportedException>(() => encoder.TryEncode(grey, 0, out _));
    Assert.That(failure!.Message, Does.Contain("Rgb24 and Rgba32"));
  }
}
