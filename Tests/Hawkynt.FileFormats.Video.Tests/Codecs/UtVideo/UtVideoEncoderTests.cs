using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Avi;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.UtVideo.Tests;

/// <summary>
/// The Ut Video encoder, held against the package's own decoder.
/// </summary>
/// <remarks>
/// Every convention the decoder measured against files — the word order, the code assignment, the
/// slice boundaries, where the median takes its left neighbour — the encoder has to write the other
/// way round, and the one check that covers all of them at once is that what goes in comes out
/// unchanged. The colour codes are checked as pixels, since the decoder hands those back exactly;
/// the luminance codes are checked as planes through the decoder's plane path, because a
/// subsampled picture's samples do not survive being turned into pixels.
/// </remarks>
[TestFixture]
public class UtVideoEncoderTests {

  private static readonly (int Width, int Height, int Slices)[] _ColourGeometries = [
    (7, 5, 1),
    (16, 17, 3),
    (130, 98, 5),
  ];

  private static readonly (int Width, int Height, int Slices)[] _LuminanceGeometries = [
    (8, 6, 1),
    (32, 18, 4),
    (130, 98, 3),
  ];

  // ============================================================================================
  // The description
  // ============================================================================================

  [Test]
  [Category("Unit")]
  [TestCase("ULRG", 24)]
  [TestCase("ULRA", 32)]
  [TestCase("ULY0", 24)]
  [TestCase("ULY2", 24)]
  [TestCase("ULY4", 24)]
  [TestCase("ULH0", 24)]
  [TestCase("ULH2", 24)]
  [TestCase("ULH4", 24)]
  public void DescribeStreamNamesTheCodeAndWritesTheDescriptionTheDecoderReads(string code, int bitsPerPixel) {
    var encoder = UtVideoEncoder.Create(_Stream(code, 32, 16), UtVideoPredictor.Median, 3);
    var described = encoder.DescribeStream();
    var format = described.CodecPrivateData.ToArray();

    Assert.Multiple(() => {
      Assert.That(described.Codec, Is.EqualTo(CodecTag.FromCharacters(code)));
      Assert.That(described.Handler, Is.EqualTo(CodecTag.FromCharacters(code)));
      Assert.That(described.Width, Is.EqualTo(32));
      Assert.That(described.Height, Is.EqualTo(16));
      Assert.That(described.BitsPerPixel, Is.EqualTo(bitsPerPixel));
      Assert.That(described.TimeBase, Is.EqualTo(new Rational(1, 25)));
      Assert.That(format, Has.Length.EqualTo(56));
      Assert.That(BitConverter.ToInt32(format, 0), Is.EqualTo(56), "biSize counts the sixteen bytes behind the header");
      Assert.That(BitConverter.ToInt32(format, 4), Is.EqualTo(32));
      Assert.That(BitConverter.ToInt32(format, 8), Is.EqualTo(16));
      Assert.That(BitConverter.ToUInt32(format, 16), Is.EqualTo(CodecTag.FromCharacters(code).Value));
      Assert.That(BitConverter.ToUInt32(format, 48), Is.EqualTo(4), "frame info size");
      Assert.That(BitConverter.ToUInt32(format, 52), Is.EqualTo(0x02000001u), "Huffman, progressive, three slices");
      Assert.That(UtVideoDecoder.Accepts(described), Is.True);
      Assert.That(VideoFormatRegistry.CanDecode(described), Is.True);
    });

    Assert.DoesNotThrow(() => UtVideoDecoder.Create(described));
  }

  [Test]
  [Category("Unit")]
  public void AStreamThatNamesNoCodeOrAnotherCodecIsWrittenAsColour() {
    var unnamed = new MediaStreamInfo { Index = 0, Kind = MediaStreamKind.Video, Width = 4, Height = 4 };
    var foreign = _Stream("avc1", 4, 4);

    Assert.Multiple(() => {
      Assert.That(UtVideoEncoder.Create(unnamed).DescribeStream().Codec, Is.EqualTo(CodecTag.FromCharacters("ULRG")));
      Assert.That(UtVideoEncoder.Create(foreign).DescribeStream().Codec, Is.EqualTo(CodecTag.FromCharacters("ULRG")));
      Assert.That(UtVideoEncoder.Codec, Is.EqualTo(CodecTag.FromCharacters("ULRG")));
    });
  }

  [Test]
  [Category("Unit")]
  public void TheAutomaticSliceCountFollowsTheSubsampledHeight() {
    Assert.Multiple(() => {
      Assert.That(_Slices(UtVideoEncoder.Create(_Stream("ULRG", 8, 119))), Is.EqualTo(1));
      Assert.That(_Slices(UtVideoEncoder.Create(_Stream("ULRG", 8, 480))), Is.EqualTo(4));
      Assert.That(_Slices(UtVideoEncoder.Create(_Stream("ULY0", 8, 480))), Is.EqualTo(2));
      Assert.That(_Slices(UtVideoEncoder.Create(_Stream("ULY2", 8, 480))), Is.EqualTo(4));
    });
  }

  // ============================================================================================
  // Round trips
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ColourRoundTripsExactlyThroughTheRegistryDecoder(
    [Values("ULRG", "ULRA")] string code,
    [Values] UtVideoPredictor predictor,
    [Values(0, 1, 2)] int geometry) {
    var (width, height, slices) = _ColourGeometries[geometry];
    var format = code == "ULRA" ? PixelFormat.Rgba32 : PixelFormat.Rgb24;
    var encoder = UtVideoEncoder.Create(_Stream(code, width, height), predictor, slices);
    var decoder = VideoFormatRegistry.CreateDecoder(encoder.DescribeStream());

    foreach (var picture in new[] { _Noise(width, height, format, 7), _Ramp(width, height, format) }) {
      Assert.That(encoder.TryEncode(picture, 0, out var packet), Is.True);
      Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
      Assert.Multiple(() => {
        Assert.That(decoded.Format, Is.EqualTo(format));
        Assert.That(decoded.PixelData, Is.EqualTo(picture.PixelData));
      });
    }
  }

  [Test]
  [Category("Unit")]
  public void LuminanceRoundTripsSampleForSampleThroughTheDecodersPlanes(
    [Values("ULY0", "ULY2", "ULY4", "ULH0", "ULH2", "ULH4")] string code,
    [Values] UtVideoPredictor predictor,
    [Values(0, 1, 2)] int geometry) {
    var (width, height, slices) = _LuminanceGeometries[geometry];
    var format = code[3] switch {
      '0' => PixelFormat.Yuv420P8,
      '2' => PixelFormat.Yuv422P8,
      _ => PixelFormat.Yuv444P8,
    };
    var encoder = UtVideoEncoder.Create(_Stream(code, width, height), predictor, slices);
    var described = encoder.DescribeStream();
    var decoder = UtVideoDecoder.Create(described);

    foreach (var picture in new[] { _Noise(width, height, format, 11), _Ramp(width, height, format) }) {
      Assert.That(encoder.TryEncode(picture, 0, out var packet), Is.True);
      var planes = decoder.DecodePlanes(packet.Data.Span);

      Assert.That(planes, Has.Length.EqualTo(3));
      for (var plane = 0; plane < 3; ++plane)
        Assert.That(planes[plane], Is.EqualTo(picture.GetPlaneData(plane).ToArray()), $"plane {plane}");

      Assert.That(VideoFormatRegistry.CreateDecoder(described).TryDecode(packet, out var pixels), Is.True);
      Assert.Multiple(() => {
        Assert.That(pixels.Width, Is.EqualTo(width));
        Assert.That(pixels.Height, Is.EqualTo(height));
        Assert.That(pixels.Format, Is.EqualTo(PixelFormat.Rgb24));
      });
    }
  }

  [Test]
  [Category("Unit")]
  public void EightBitColourInAnyByteOrderCodesExactly() {
    var picture = _Noise(9, 4, PixelFormat.Bgra32, 3);
    var encoder = UtVideoEncoder.Create(_Stream("ULRG", 9, 4));
    var decoder = VideoFormatRegistry.CreateDecoder(encoder.DescribeStream());

    Assert.That(encoder.TryEncode(picture, 0, out var packet), Is.True);
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);

    Assert.That(decoded.PixelData, Is.EqualTo(picture.ToRgb24()));
  }

  [Test]
  [Category("Unit")]
  public void AFlatPlaneStatesOneSymbolAndCarriesNoBits() {
    // A picture of one colour coded without prediction: every plane is one symbol, the lengths give
    // that symbol nought and every other symbol 255, and each slice ends where the one before it
    // did. With a predictor the same picture has two symbols, since a slice's first difference is
    // the sample less 128, and then the table is real and each slice is one word.
    var pixels = new byte[6 * 4 * 3];
    for (var i = 0; i < pixels.Length; i += 3)
      (pixels[i], pixels[i + 1], pixels[i + 2]) = (200, 30, 90);

    var picture = new RawImage { Width = 6, Height = 4, Format = PixelFormat.Rgb24, PixelData = pixels };
    var encoder = UtVideoEncoder.Create(_Stream("ULRG", 6, 4), UtVideoPredictor.None, 2);

    Assert.That(encoder.TryEncode(picture, 0, out var packet), Is.True);
    var data = packet.Data.ToArray();

    Assert.That(data, Has.Length.EqualTo(3 * (256 + 2 * 4) + 4));
    for (var plane = 0; plane < 3; ++plane) {
      var lengths = data.AsSpan(plane * 264, 256).ToArray();
      Assert.That(lengths.Count(static length => length == 0), Is.EqualTo(1), $"plane {plane}");
      Assert.That(lengths.Count(static length => length == 0xFF), Is.EqualTo(255), $"plane {plane}");
      Assert.That(data.AsSpan(plane * 264 + 256, 8).ToArray(), Is.All.Zero, $"plane {plane}");
    }

    var decoder = VideoFormatRegistry.CreateDecoder(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
    Assert.That(decoded.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void ThePictureSurvivesMuxingIntoAviAndBack() {
    var encoder = UtVideoEncoder.Create(_Stream("ULRA", 20, 14), UtVideoPredictor.Left, 2);
    var first = _Noise(20, 14, PixelFormat.Rgba32, 5);
    var second = _Ramp(20, 14, PixelFormat.Rgba32);
    Assert.That(encoder.TryEncode(first, 0, out var one), Is.True);
    Assert.That(encoder.TryEncode(second, 1, out var two), Is.True);

    var avi = VideoIO.Mux<AviWriter>([encoder.DescribeStream()], [one, two]);
    var container = AviContainer.FromBytes(avi);
    var stream = AviContainer.Streams(container).Single();
    Assert.Multiple(() => {
      Assert.That(stream.Codec, Is.EqualTo(CodecTag.FromCharacters("ULRA")));
      Assert.That(stream.CodecPrivateData.Length, Is.EqualTo(56));
    });

    var decoded = VideoIO.Decode(AviContainer.ReadPackets(container), stream, VideoFormatRegistry.CreateDecoder)
      .Select(static frame => frame.Image.PixelData)
      .ToArray();

    Assert.That(decoded, Has.Length.EqualTo(2));
    Assert.Multiple(() => {
      Assert.That(decoded[0], Is.EqualTo(first.PixelData));
      Assert.That(decoded[1], Is.EqualTo(second.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void TimestampsPassThroughAndEveryPacketIsAKeyFrame() {
    var encoder = UtVideoEncoder.Create(_Stream("ULRG", 4, 4));
    var picture = _Noise(4, 4, PixelFormat.Rgb24, 1);

    Assert.That(encoder.TryEncode(picture, 42, out var stamped), Is.True);
    Assert.That(encoder.TryEncode(picture, null, out var unstamped), Is.True);

    Assert.Multiple(() => {
      Assert.That(stamped.StreamIndex, Is.Zero);
      Assert.That(stamped.PresentationTimestamp, Is.EqualTo(42));
      Assert.That(stamped.DecodeTimestamp, Is.EqualTo(42));
      Assert.That(stamped.IsKeyFrame, Is.True);
      Assert.That(unstamped.PresentationTimestamp, Is.Null);
      Assert.That(unstamped.DecodeTimestamp, Is.Null);
      Assert.That(unstamped.IsKeyFrame, Is.True);
      Assert.That(stamped.Data.ToArray(), Is.EqualTo(unstamped.Data.ToArray()));
    });
  }

  [Test]
  [Category("Unit")]
  public void ColourHandedToALuminanceCodeIsConvertedUnderTheMatrixTheDecoderUses() {
    // Mid grey has no chrominance under any matrix, so the planes are exact: studio-swing
    // luminance of 16 + 219 * 128 / 255 and chrominance at rest. The pixels that come back are
    // checked to within the rounding a conversion there and back costs.
    var pixels = Enumerable.Repeat((byte)128, 8 * 8 * 3).ToArray();
    var picture = new RawImage { Width = 8, Height = 8, Format = PixelFormat.Rgb24, PixelData = pixels };

    foreach (var code in new[] { "ULY4", "ULH2", "ULY0" }) {
      var encoder = UtVideoEncoder.Create(_Stream(code, 8, 8));
      var described = encoder.DescribeStream();
      Assert.That(encoder.TryEncode(picture, 0, out var packet), Is.True, code);

      var planes = UtVideoDecoder.Create(described).DecodePlanes(packet.Data.Span);
      Assert.Multiple(() => {
        Assert.That(planes[0], Is.All.InRange(125, 126), $"{code} luminance");
        Assert.That(planes[1], Is.All.EqualTo(128), $"{code} Cb");
        Assert.That(planes[2], Is.All.EqualTo(128), $"{code} Cr");
      });

      Assert.That(VideoFormatRegistry.CreateDecoder(described).TryDecode(packet, out var decoded), Is.True);
      Assert.That(decoded.PixelData, Is.All.InRange(126, 130), code);
    }
  }

  // ============================================================================================
  // The Huffman tables
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheCodesWrittenAreTheCodesTheDecoderReadsBack() {
    // Skewed counts so that the lengths run from one bit to a dozen: every symbol is written with
    // its code and read back through the decoder's own table and bit reader.
    var counts = new long[256];
    var random = new Random(23);
    for (var symbol = 0; symbol < 256; ++symbol)
      counts[symbol] = symbol % 5 == 0 ? 0 : 1L << random.Next(0, 12);

    var lengths = UtVideoHuffmanBuilder.Lengths(counts);
    var codes = UtVideoHuffmanBuilder.Codes(lengths);
    var writer = new UtVideoBitWriter(512);
    var written = new List<int>();
    for (var symbol = 255; symbol >= 0; --symbol) {
      if (counts[symbol] == 0) {
        Assert.That(lengths[symbol], Is.EqualTo(0xFF), $"symbol {symbol} does not occur");
        continue;
      }

      writer.Write(codes[symbol], lengths[symbol]);
      written.Add(symbol);
    }

    var table = new UtVideoHuffmanTable(lengths, 0);
    var reader = new UtVideoBitReader(writer.End());
    foreach (var symbol in written)
      Assert.That(table.Read(reader), Is.EqualTo(symbol));
  }

  [Test]
  [Category("Unit")]
  public void ALongTailOfRareSymbolsIsFlattenedToTheLengthTheFormatAllows() {
    // Counts that grow like the Fibonacci numbers give a plain Huffman tree one leaf a level, forty
    // levels deep here. The decoder refuses a code longer than twenty-four bits, so the table has
    // to come out shallower and still describe a complete code.
    var counts = new long[256];
    long a = 1, b = 1;
    for (var symbol = 0; symbol < 40; ++symbol) {
      counts[symbol] = a;
      (a, b) = (b, a + b);
    }

    var lengths = UtVideoHuffmanBuilder.Lengths(counts);

    Assert.That(lengths.Where(static length => length != 0xFF).Max(), Is.LessThanOrEqualTo(24));
    Assert.DoesNotThrow(() => new UtVideoHuffmanTable(lengths, 0));
  }

  // ============================================================================================
  // Refusals
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AnOddSizeIsRefusedForTheCodesThatSubsampleAcrossIt() {
    Assert.Multiple(() => {
      Assert.Throws<NotSupportedException>(() => UtVideoEncoder.Create(_Stream("ULY2", 7, 4)));
      Assert.Throws<NotSupportedException>(() => UtVideoEncoder.Create(_Stream("ULH2", 7, 4)));
      Assert.Throws<NotSupportedException>(() => UtVideoEncoder.Create(_Stream("ULY0", 7, 4)));
      Assert.Throws<NotSupportedException>(() => UtVideoEncoder.Create(_Stream("ULY0", 8, 5)));
      Assert.DoesNotThrow(() => UtVideoEncoder.Create(_Stream("ULY2", 8, 5)));
      Assert.DoesNotThrow(() => UtVideoEncoder.Create(_Stream("ULY4", 7, 5)));
      Assert.DoesNotThrow(() => UtVideoEncoder.Create(_Stream("ULRG", 7, 5)));
    });
  }

  [Test]
  [Category("Unit")]
  public void ASliceCountTheFrameCannotBeCutIntoIsRefused() {
    Assert.Multiple(() => {
      Assert.Throws<NotSupportedException>(() => UtVideoEncoder.Create(_Stream("ULY0", 8, 6), UtVideoPredictor.Median, 4));
      Assert.DoesNotThrow(() => UtVideoEncoder.Create(_Stream("ULY0", 8, 6), UtVideoPredictor.Median, 3));
      Assert.Throws<NotSupportedException>(() => UtVideoEncoder.Create(_Stream("ULRG", 8, 6), UtVideoPredictor.Median, 7));
      Assert.Throws<ArgumentOutOfRangeException>(() => UtVideoEncoder.Create(_Stream("ULRG", 8, 600), UtVideoPredictor.Median, 257));
      Assert.Throws<ArgumentOutOfRangeException>(() => UtVideoEncoder.Create(_Stream("ULRG", 8, 6), UtVideoPredictor.Median, -1));
      Assert.Throws<ArgumentOutOfRangeException>(() => UtVideoEncoder.Create(_Stream("ULRG", 8, 6), (UtVideoPredictor)7));
    });
  }

  [Test]
  [Category("Unit")]
  public void TheProAndT2CodesAreRefusedByName() {
    var pro = Assert.Throws<NotSupportedException>(() => UtVideoEncoder.Create(_Stream("UQY2", 8, 6)));
    var t2 = Assert.Throws<NotSupportedException>(() => UtVideoEncoder.Create(_Stream("UMY2", 8, 6)));
    Assert.Multiple(() => {
      Assert.That(pro!.Message, Does.Contain("UQY2").And.Contain("Pro"));
      Assert.That(t2!.Message, Does.Contain("UMY2").And.Contain("T2"));
    });
  }

  [Test]
  [Category("Unit")]
  public void APictureThatCannotBeCodedWithoutChangingASampleIsRefusedByName() {
    var colour = UtVideoEncoder.Create(_Stream("ULRG", 4, 4));
    var luminance = UtVideoEncoder.Create(_Stream("ULY2", 4, 4));

    Assert.Multiple(() => {
      Assert.Throws<NotSupportedException>(() => colour.TryEncode(_Noise(4, 4, PixelFormat.Rgb48, 1), 0, out _));
      Assert.Throws<NotSupportedException>(() => colour.TryEncode(_Noise(4, 4, PixelFormat.RgbF32, 1), 0, out _));
      Assert.Throws<NotSupportedException>(() => colour.TryEncode(_Noise(4, 4, PixelFormat.Yuv444P8, 1), 0, out _));
      Assert.Throws<NotSupportedException>(() => luminance.TryEncode(_Noise(4, 4, PixelFormat.Yuv444P8, 1), 0, out _));
      Assert.Throws<NotSupportedException>(() => luminance.TryEncode(_Noise(4, 4, PixelFormat.Yuv422P10, 1), 0, out _));
      Assert.Throws<NotSupportedException>(() => luminance.TryEncode(_Noise(4, 4, PixelFormat.Rgb48, 1), 0, out _));
    });
  }

  [Test]
  [Category("Unit")]
  public void AGeometryChangeAndAShortBufferAreRefused() {
    var encoder = UtVideoEncoder.Create(_Stream("ULRG", 8, 8));
    var wrongSize = _Noise(4, 4, PixelFormat.Rgb24, 1);
    var tooShort = new RawImage { Width = 8, Height = 8, Format = PixelFormat.Rgb24, PixelData = new byte[8 * 8 * 3 - 1] };

    Assert.Multiple(() => {
      Assert.Throws<InvalidDataException>(() => encoder.TryEncode(wrongSize, 0, out _));
      Assert.Throws<InvalidDataException>(() => encoder.TryEncode(tooShort, 0, out _));
      Assert.Throws<NotSupportedException>(() => UtVideoEncoder.Create(new() { Index = 0, Kind = MediaStreamKind.Audio }));
      Assert.Throws<NotSupportedException>(() => UtVideoEncoder.Create(_Stream("ULRG", 0, 8)));
    });
  }

  // ============================================================================================
  // Helpers
  // ============================================================================================

  private static MediaStreamInfo _Stream(string code, int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters(code),
    Width = width,
    Height = height,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };

  private static int _Slices(UtVideoEncoder encoder)
    => (encoder.DescribeStream().CodecPrivateData.Span[55]) + 1;

  private static RawImage _Noise(int width, int height, PixelFormat format, int seed) {
    var length = _Length(width, height, format);
    var pixels = new byte[length];
    new Random(seed).NextBytes(pixels);
    return new() { Width = width, Height = height, Format = format, PixelData = pixels };
  }

  /// <summary>Smooth content: small differences, so the tables are short and some planes flat.</summary>
  private static RawImage _Ramp(int width, int height, PixelFormat format) {
    var length = _Length(width, height, format);
    var pixels = new byte[length];
    var stride = width * Math.Max(1, RawImage.BytesPerPixel(format));
    for (var i = 0; i < length; ++i) {
      var row = i / stride;
      var column = i % stride;
      pixels[i] = (byte)((column / 3 + row * 2 + (column % 3) * 40) & 0xFF);
    }

    return new() { Width = width, Height = height, Format = format, PixelData = pixels };
  }

  private static int _Length(int width, int height, PixelFormat format)
    => checked((int)new RawImage { Width = width, Height = height, Format = format, PixelData = [] }.MinimumPixelDataLength);
}
