using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;
using FileFormat.Matroska;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Ffv1.Tests;

/// <summary>
/// The encoder measured against the decoder it shares its helpers with, and against what it says
/// about itself.
/// </summary>
/// <remarks>
/// Every round trip goes through <see cref="VideoFormatRegistry.CreateDecoder"/> on the stream the
/// encoder describes, which is the path a container would take, and compares in the format the
/// decoder hands back: grey and colour come back as themselves, luminance and chrominance come back
/// through the decoder's BT.601 conversion, so the expectation is that conversion applied to the
/// source planes.
/// </remarks>
[TestFixture]
public class Ffv1EncoderTests {

  // ============================================================================================
  // What it says about itself
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheStreamIsDescribedAsFfv1AndTheRecordReadsBackThroughTheDecodersParser() {
    var encoder = Ffv1Encoder.Create(_Stream(64, 48, new Rational(1, 25)), PixelFormat.Yuv420P8, 3, 2);
    var stream = encoder.DescribeStream();

    Assert.Multiple(() => {
      Assert.That(stream.Codec, Is.EqualTo(CodecTag.FromCharacters("FFV1")));
      Assert.That(stream.Handler, Is.EqualTo(CodecTag.FromCharacters("FFV1")));
      Assert.That(stream.CodecId, Is.EqualTo("V_FFV1"));
      Assert.That(stream.Kind, Is.EqualTo(MediaStreamKind.Video));
      Assert.That(stream.Width, Is.EqualTo(64));
      Assert.That(stream.Height, Is.EqualTo(48));
      Assert.That(stream.BitsPerPixel, Is.EqualTo(12));
      Assert.That(stream.TimeBase, Is.EqualTo(new Rational(1, 25)));
      Assert.That(stream.CodecPrivateData.Length, Is.GreaterThan(4));
      Assert.That(Ffv1Crc.Of(stream.CodecPrivateData.Span), Is.Zero, "the record carries its own parity");
      Assert.That(Ffv1Decoder.Accepts(stream), Is.True);
    });

    var (zero, one) = Ffv1StateTransition.Build([]);
    var states = new byte[Ffv1RangeCoder.CONTEXT_SIZE];
    Array.Fill(states, (byte)128);
    var parameters = Ffv1Parameters.Read(new Ffv1RangeCoder(stream.CodecPrivateData[..^4], zero, one), states, true);

    Assert.Multiple(() => {
      Assert.That(parameters.Version, Is.EqualTo(3));
      Assert.That(parameters.MicroVersion, Is.EqualTo(4));
      Assert.That(parameters.CoderType, Is.EqualTo(1));
      Assert.That(parameters.ColourSpaceType, Is.Zero);
      Assert.That(parameters.BitsPerRawSample, Is.EqualTo(8));
      Assert.That(parameters.ChromaPlanes, Is.True);
      Assert.That(parameters.ChromaHorizontalShift, Is.EqualTo(1));
      Assert.That(parameters.ChromaVerticalShift, Is.EqualTo(1));
      Assert.That(parameters.ExtraPlane, Is.False);
      Assert.That(parameters.HorizontalSlices, Is.EqualTo(3));
      Assert.That(parameters.VerticalSlices, Is.EqualTo(2));
      Assert.That(parameters.QuantTableSetCount, Is.EqualTo(1));
      Assert.That(parameters.ContextCount[0], Is.EqualTo(666));
      Assert.That(parameters.ErrorCorrection, Is.EqualTo(1));
      Assert.That(parameters.IntraOnly, Is.True);
      Assert.That(parameters.InitialStates, Is.Null);
    });

    Assert.DoesNotThrow(() => Ffv1Decoder.Create(stream));
  }

  [Test]
  [Category("Unit")]
  public void ColourWithAlphaIsDescribedAsTheTransformWithAnExtraPlane() {
    var stream = Ffv1Encoder.Create(_Stream(8, 8), PixelFormat.Bgra32).DescribeStream();
    var parameters = _Parameters(stream);

    Assert.Multiple(() => {
      Assert.That(stream.BitsPerPixel, Is.EqualTo(32));
      Assert.That(parameters.ColourSpaceType, Is.EqualTo(1));
      Assert.That(parameters.ChromaPlanes, Is.True);
      Assert.That(parameters.ChromaHorizontalShift, Is.Zero);
      Assert.That(parameters.ExtraPlane, Is.True);
      Assert.That(parameters.PlaneCount, Is.EqualTo(4));
    });
  }

  [Test]
  [Category("Unit")]
  public void TheBitsPerPixelOfTheStreamChooseTheCodedFormat() {
    Assert.Multiple(() => {
      Assert.That(_Parameters(Ffv1Encoder.Create(_Stream(8, 8, bitsPerPixel: 8)).DescribeStream()).ChromaPlanes, Is.False);
      Assert.That(_Parameters(Ffv1Encoder.Create(_Stream(8, 8, bitsPerPixel: 12)).DescribeStream()).ChromaVerticalShift, Is.EqualTo(1));
      Assert.That(_Parameters(Ffv1Encoder.Create(_Stream(8, 8, bitsPerPixel: 16)).DescribeStream()).ChromaVerticalShift, Is.Zero);
      Assert.That(_Parameters(Ffv1Encoder.Create(_Stream(8, 8, bitsPerPixel: 24)).DescribeStream()).ColourSpaceType, Is.EqualTo(1));
      Assert.That(_Parameters(Ffv1Encoder.Create(_Stream(8, 8, bitsPerPixel: 32)).DescribeStream()).ExtraPlane, Is.True);
      Assert.That(_Parameters(Ffv1Encoder.Create(_Stream(8, 8)).DescribeStream()).ColourSpaceType, Is.EqualTo(1), "nothing stated is colour");
    });
  }

  [Test]
  [Category("Unit")]
  public void TheDefaultGridIsTheOneFfmpegWouldChoose() {
    Assert.Multiple(() => {
      var small = _Parameters(Ffv1Encoder.Create(_Stream(64, 48), PixelFormat.Yuv420P8).DescribeStream());
      Assert.That((small.HorizontalSlices, small.VerticalSlices), Is.EqualTo((2, 2)));

      var single = _Parameters(Ffv1Encoder.Create(_Stream(1, 1), PixelFormat.Gray8).DescribeStream());
      Assert.That((single.HorizontalSlices, single.VerticalSlices), Is.EqualTo((1, 1)));

      var large = _Parameters(Ffv1Encoder.Create(_Stream(1920, 1080), PixelFormat.Rgb24).DescribeStream());
      Assert.That((long)((1920 + large.HorizontalSlices - 1) / large.HorizontalSlices) * ((1080 + large.VerticalSlices - 1) / large.VerticalSlices), Is.LessThanOrEqualTo(360 * 288));
    });
  }

  // ============================================================================================
  // Round trips
  // ============================================================================================

  private static readonly PixelFormat[] _Formats = [
    PixelFormat.Gray8, PixelFormat.GrayAlpha16,
    PixelFormat.Yuv420P8, PixelFormat.Yuv422P8, PixelFormat.Yuv440P8, PixelFormat.Yuv444P8,
    PixelFormat.Rgb24, PixelFormat.Bgr24, PixelFormat.Rgba32,
  ];

  private static readonly (int Width, int Height, int Horizontal, int Vertical)[] _Geometries = [
    (1, 1, 1, 1),
    (7, 5, 1, 1),
    (33, 17, 1, 1),
    (33, 17, 3, 2),
    (16, 16, 4, 4),
    (64, 48, 2, 2),
    (64, 48, 3, 2),
  ];

  private static IEnumerable<TestCaseData> _RoundTrips() {
    foreach (var format in _Formats)
      foreach (var (width, height, horizontal, vertical) in _Geometries)
        foreach (var gradient in new[] { false, true })
          yield return new TestCaseData(format, width, height, horizontal, vertical, gradient)
            .SetName($"RoundTrip({format},{width}x{height},{horizontal}x{vertical},{(gradient ? "gradient" : "random")})");
  }

  [Test]
  [Category("Unit")]
  [TestCaseSource(nameof(_RoundTrips))]
  public void APictureComesBackThroughTheRegistryDecoderExactly(PixelFormat format, int width, int height, int horizontal, int vertical, bool gradient) {
    var source = gradient ? _Gradient(format, width, height) : _Random(format, width, height, width * 31 + height);
    var encoder = Ffv1Encoder.Create(_Stream(width, height), format, horizontal, vertical);

    Assert.That(encoder.TryEncode(source, 0, out var packet), Is.True);
    Assert.That(packet.IsKeyFrame, Is.True);

    var decoded = _Decode(encoder.DescribeStream(), packet);
    var (expectedFormat, expectedPixels) = _AsTheDecoderReturnsIt(source);

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(width));
      Assert.That(decoded.Height, Is.EqualTo(height));
      Assert.That(decoded.Format, Is.EqualTo(expectedFormat));
      Assert.That(decoded.PixelData, Is.EqualTo(expectedPixels));
    });
  }

  [Test]
  [Category("Unit")]
  public void EveryValueOfEveryChannelSurvivesTheColourTransform() {
    // The transform widens the colour differences to nine bits and folds every coded difference
    // modulo nine; the corners of the cube are where a mistake in either would show.
    var pixels = new byte[256 * 8 * 3];
    for (var i = 0; i < 256 * 8; ++i) {
      var value = (byte)(i & 0xFF);
      var corner = i >> 8;
      pixels[i * 3] = (corner & 1) != 0 ? value : (byte)(255 - value);
      pixels[i * 3 + 1] = (corner & 2) != 0 ? value : (byte)(255 - value);
      pixels[i * 3 + 2] = (corner & 4) != 0 ? (byte)(value ^ 0x55) : (byte)(value * 7);
    }

    var source = new RawImage { Width = 256, Height = 8, Format = PixelFormat.Rgb24, PixelData = pixels };
    var encoder = Ffv1Encoder.Create(_Stream(256, 8), PixelFormat.Rgb24, 2, 1);

    Assert.That(encoder.TryEncode(source, null, out var packet), Is.True);
    Assert.That(_Decode(encoder.DescribeStream(), packet).PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void APictureInAnotherChannelOrderIsCodedAsTheSameColour() {
    var rgb = _Random(PixelFormat.Rgb24, 9, 7, 3);
    var bgr = new byte[rgb.PixelData.Length];
    for (var i = 0; i < bgr.Length; i += 3) {
      bgr[i] = rgb.PixelData[i + 2];
      bgr[i + 1] = rgb.PixelData[i + 1];
      bgr[i + 2] = rgb.PixelData[i];
    }

    var encoder = Ffv1Encoder.Create(_Stream(9, 7), PixelFormat.Rgb24, 1, 1);
    Assert.That(encoder.TryEncode(new() { Width = 9, Height = 7, Format = PixelFormat.Bgr24, PixelData = bgr }, 0, out var packet), Is.True);

    var decoded = _Decode(encoder.DescribeStream(), packet);
    Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(decoded.PixelData, Is.EqualTo(rgb.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void GreyIntoAColourStreamComesBackAsThreeCopiesOfItself() {
    var grey = _Random(PixelFormat.Gray8, 5, 4, 11);
    var encoder = Ffv1Encoder.Create(_Stream(5, 4), PixelFormat.Rgb24, 1, 1);

    Assert.That(encoder.TryEncode(grey, 0, out var packet), Is.True);

    var decoded = _Decode(encoder.DescribeStream(), packet);
    Assert.That(decoded.PixelData, Is.EqualTo(grey.PixelData.SelectMany(static g => new[] { g, g, g }).ToArray()));
  }

  [Test]
  [Category("Unit")]
  public void TheSameFrameCodedTwiceIsTheSameBytes() {
    var source = _Random(PixelFormat.Yuv444P8, 20, 12, 5);
    var encoder = Ffv1Encoder.Create(_Stream(20, 12), PixelFormat.Yuv444P8, 2, 2);

    encoder.TryEncode(source, 0, out var first);
    encoder.TryEncode(source, 1, out var second);

    Assert.That(second.Data.ToArray(), Is.EqualTo(first.Data.ToArray()), "nothing is carried from one frame into the next");
  }

  [Test]
  [Category("Unit")]
  public void EveryFrameOfASequenceIsAKeyframeThatDecodesOnItsOwn() {
    var stream = _Stream(24, 18);
    var encoder = Ffv1Encoder.Create(stream, PixelFormat.Rgb24, 2, 2);
    var sources = Enumerable.Range(0, 5).Select(i => _Random(PixelFormat.Rgb24, 24, 18, 100 + i)).ToArray();
    var packets = new CodedPacket[sources.Length];
    for (var i = 0; i < sources.Length; ++i)
      Assert.That(encoder.TryEncode(sources[i], i * 40, out packets[i]), Is.True);

    Assert.That(((IVideoPacketEncoder)encoder).Flush(), Is.Empty);

    var described = encoder.DescribeStream();
    var inOrder = VideoFormatRegistry.CreateDecoder(described);
    for (var i = 0; i < packets.Length; ++i) {
      Assert.That(packets[i].PresentationTimestamp, Is.EqualTo(i * 40));
      Assert.That(inOrder.TryDecode(packets[i], out var frame), Is.True);
      Assert.That(frame.PixelData, Is.EqualTo(sources[i].PixelData), $"frame {i} in sequence");
    }

    var fromTheMiddle = VideoFormatRegistry.CreateDecoder(described);
    Assert.That(fromTheMiddle.TryDecode(packets[3], out var alone), Is.True);
    Assert.That(alone.PixelData, Is.EqualTo(sources[3].PixelData), "a frame decoded without the ones before it");
  }

  [Test]
  [Category("Unit")]
  public void TheTimestampsGoThroughUntouched() {
    var encoder = Ffv1Encoder.Create(_Stream(4, 4), PixelFormat.Gray8, 1, 1);
    var picture = _Gradient(PixelFormat.Gray8, 4, 4);

    encoder.TryEncode(picture, 1234567890123, out var stamped);
    encoder.TryEncode(picture, null, out var unstamped);

    Assert.Multiple(() => {
      Assert.That(stamped.StreamIndex, Is.EqualTo(3));
      Assert.That(stamped.PresentationTimestamp, Is.EqualTo(1234567890123));
      Assert.That(stamped.DecodeTimestamp, Is.EqualTo(1234567890123));
      Assert.That(stamped.IsKeyFrame, Is.True);
      Assert.That(unstamped.PresentationTimestamp, Is.Null);
      Assert.That(unstamped.DecodeTimestamp, Is.Null);
    });
  }

  [Test]
  [Category("Unit")]
  public void AStreamMuxedIntoMatroskaDecodesThroughTheRegistry() {
    var stream = _Stream(40, 30, new Rational(1, 25));
    var encoder = Ffv1Encoder.Create(stream, PixelFormat.Rgba32, 2, 2);
    var sources = Enumerable.Range(0, 3).Select(i => _Random(PixelFormat.Rgba32, 40, 30, 7 + i)).ToArray();
    var packets = sources.Select((source, i) => {
      encoder.TryEncode(source, i, out var packet);
      return packet;
    }).ToArray();

    var described = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = encoder.DescribeStream().Codec,
      CodecId = encoder.DescribeStream().CodecId,
      Width = 40,
      Height = 30,
      TimeBase = new Rational(1, 25),
      FrameRate = new Rational(25, 1),
      BitsPerPixel = 32,
      CodecPrivateData = encoder.DescribeStream().CodecPrivateData,
    };
    var file = VideoIO.Mux<MatroskaWriter>([described], packets.Select(p => p with { StreamIndex = 0 }));

    var frames = VideoFormatRegistry.DecodeFrames(file).ToList();
    Assert.That(frames, Has.Count.EqualTo(3));
    for (var i = 0; i < frames.Count; ++i) {
      Assert.That(frames[i].Image.Format, Is.EqualTo(PixelFormat.Rgba32));
      Assert.That(frames[i].Image.PixelData, Is.EqualTo(sources[i].PixelData), $"frame {i}");
    }
  }

  // ============================================================================================
  // The range coder, written and read back
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void NumbersWrittenAreTheNumbersRead() {
    int[] values = [0, 1, -1, 2, -2, 7, -8, 127, -128, 128, -255, 255, 256, -256, 511, 1023, -1024, 65535, -100000, 1 << 20, 3];
    var (zero, one) = Ffv1StateTransition.Build([]);
    var encoder = new Ffv1RangeEncoder(zero, one);
    var states = _States();
    foreach (var value in values)
      encoder.Symbol(states, value, true);

    var unsignedStates = _States();
    foreach (var value in values.Where(static v => v >= 0))
      encoder.Symbol(unsignedStates, value, false);

    var bytes = encoder.Terminate(true);

    var decoder = new Ffv1RangeCoder(bytes, zero, one);
    var readStates = _States();
    foreach (var value in values)
      Assert.That(decoder.Symbol(readStates, true), Is.EqualTo(value));

    var readUnsigned = _States();
    foreach (var value in values.Where(static v => v >= 0))
      Assert.That(decoder.Symbol(readUnsigned, false), Is.EqualTo(value));
  }

  [Test]
  [Category("Unit")]
  public void ALongRunOfUnlikelyBitsCarriesCorrectly() {
    // Each improbable bit narrows the range hard and pushes the low end over byte boundaries,
    // which is what the held-back byte and the count of 0xFFs behind it are for.
    var (zero, one) = Ffv1StateTransition.Build([]);
    var encoder = new Ffv1RangeEncoder(zero, one);
    var states = _States();
    var random = new Random(99);
    var bits = new int[4000];
    for (var i = 0; i < bits.Length; ++i) {
      bits[i] = random.Next(100) < 3 ? 1 : 0;
      encoder.Put(states, 0, bits[i]);
    }

    var bytes = encoder.Terminate(false);
    Assert.That(bytes.Length, Is.LessThan(bits.Length / 8), "improbable bits cost less than a bit each");

    var decoder = new Ffv1RangeCoder(bytes, zero, one);
    var readStates = _States();
    for (var i = 0; i < bits.Length; ++i)
      Assert.That(decoder.Get(readStates, 0), Is.EqualTo(bits[i]), $"bit {i}");
  }

  // ============================================================================================
  // What refuses
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AFormatFfv1IsNotWrittenInHereIsRefusedByName() {
    var failure = Assert.Throws<NotSupportedException>(() => Ffv1Encoder.Create(_Stream(8, 8), PixelFormat.Yuv420P10));
    Assert.That(failure!.Message, Does.Contain("Yuv420P10"));

    var deep = Assert.Throws<NotSupportedException>(() => Ffv1Encoder.Create(_Stream(8, 8, bitsPerPixel: 48)));
    Assert.That(deep!.Message, Does.Contain("48 bits"));
  }

  [Test]
  [Category("Unit")]
  public void APictureThatCannotBeCodedLosslesslyAsTheStreamIsRefused() {
    var encoder = Ffv1Encoder.Create(_Stream(8, 8), PixelFormat.Yuv420P8, 1, 1);
    var colour = _Random(PixelFormat.Rgb24, 8, 8, 1);

    var failure = Assert.Throws<NotSupportedException>(() => encoder.TryEncode(colour, 0, out _));
    Assert.That(failure!.Message, Does.Contain("Rgb24").And.Contain("Yuv420P8"));

    var alphaIntoOpaque = Ffv1Encoder.Create(_Stream(8, 8), PixelFormat.Rgb24, 1, 1);
    Assert.Throws<NotSupportedException>(() => alphaIntoOpaque.TryEncode(_Random(PixelFormat.Rgba32, 8, 8, 1), 0, out _));
  }

  [Test]
  [Category("Unit")]
  public void APictureOfAnotherSizeIsRefused() {
    var encoder = Ffv1Encoder.Create(_Stream(8, 8), PixelFormat.Gray8, 1, 1);

    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(_Random(PixelFormat.Gray8, 8, 9, 1), 0, out _));
    Assert.That(failure!.Message, Does.Contain("8x9"));
  }

  [Test]
  [Category("Unit")]
  public void APictureShortOfSamplesIsRefused() {
    var encoder = Ffv1Encoder.Create(_Stream(8, 8), PixelFormat.Gray8, 1, 1);
    var short8 = new RawImage { Width = 8, Height = 8, Format = PixelFormat.Gray8, PixelData = new byte[63] };

    Assert.Throws<InvalidDataException>(() => encoder.TryEncode(short8, 0, out _));
  }

  [Test]
  [Category("Unit")]
  public void AGridThatWouldLeaveChrominanceUncodedIsRefused() {
    // Seven pixels in two slices cut at three: the second slice's chrominance starts at one and is
    // two wide, and the plane's fourth column belongs to nobody.
    var failure = Assert.Throws<NotSupportedException>(() => Ffv1Encoder.Create(_Stream(7, 8), PixelFormat.Yuv420P8, 2, 1));
    Assert.That(failure!.Message, Does.Contain("no slice codes"));

    Assert.DoesNotThrow(() => Ffv1Encoder.Create(_Stream(7, 8), PixelFormat.Yuv444P8, 2, 1), "with nothing subsampled the same grid is fine");
    Assert.DoesNotThrow(() => Ffv1Encoder.Create(_Stream(7, 8), PixelFormat.Yuv420P8, 1, 2), "and so is cutting the other way at an even row");
  }

  [Test]
  [Category("Unit")]
  public void AGridFinerThanThePictureIsRefused() {
    var failure = Assert.Throws<NotSupportedException>(() => Ffv1Encoder.Create(_Stream(4, 4), PixelFormat.Gray8, 5, 1));
    Assert.That(failure!.Message, Does.Contain("narrower than a pixel"));

    Assert.Throws<ArgumentException>(() => Ffv1Encoder.Create(_Stream(4, 4), PixelFormat.Gray8, 2, 0));
  }

  [Test]
  [Category("Unit")]
  public void AStreamWithNoPictureOrNoVideoIsRefused() {
    Assert.Throws<NotSupportedException>(() => Ffv1Encoder.Create(_Stream(0, 8), PixelFormat.Gray8));

    var audio = new MediaStreamInfo { Index = 0, Kind = MediaStreamKind.Audio, Width = 8, Height = 8 };
    Assert.Throws<NotSupportedException>(() => Ffv1Encoder.Create(audio, PixelFormat.Gray8));
  }

  // ============================================================================================

  private static MediaStreamInfo _Stream(int width, int height, Rational? timeBase = null, int bitsPerPixel = 0) => new() {
    Index = 3,
    Kind = MediaStreamKind.Video,
    Width = width,
    Height = height,
    TimeBase = timeBase ?? Rational.Unknown,
    BitsPerPixel = bitsPerPixel,
  };

  private static Ffv1Parameters _Parameters(MediaStreamInfo stream) {
    var (zero, one) = Ffv1StateTransition.Build([]);
    return Ffv1Parameters.Read(new Ffv1RangeCoder(stream.CodecPrivateData[..^4], zero, one), _States(), true);
  }

  private static byte[] _States() {
    var states = new byte[Ffv1RangeCoder.CONTEXT_SIZE];
    Array.Fill(states, (byte)128);
    return states;
  }

  private static RawImage _Decode(MediaStreamInfo stream, CodedPacket packet) {
    var decoder = VideoFormatRegistry.CreateDecoder(stream);
    Assert.That(decoder.TryDecode(packet, out var frame), Is.True);
    return frame;
  }

  private static RawImage _Random(PixelFormat format, int width, int height, int seed) {
    var image = new RawImage { Width = width, Height = height, Format = format, PixelData = [] };
    var pixels = new byte[image.MinimumPixelDataLength];
    new Random(seed).NextBytes(pixels);
    return new() { Width = width, Height = height, Format = format, PixelData = pixels };
  }

  private static RawImage _Gradient(PixelFormat format, int width, int height) {
    var image = new RawImage { Width = width, Height = height, Format = format, PixelData = [] };
    var pixels = new byte[image.MinimumPixelDataLength];

    if (RawImage.IsPlanarYuvFormat(format)) {
      var offset = 0;
      for (var plane = 0; plane < 3; ++plane) {
        var (planeWidth, planeHeight) = image.GetPlaneDimensions(plane);
        for (var y = 0; y < planeHeight; ++y)
          for (var x = 0; x < planeWidth; ++x)
            pixels[offset++] = (byte)(x * 5 + y * 3 + plane * 60);
      }

      return new() { Width = width, Height = height, Format = format, PixelData = pixels };
    }

    var channels = RawImage.BytesPerPixel(format);
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x)
        for (var channel = 0; channel < channels; ++channel)
          pixels[(y * width + x) * channels + channel] = (byte)(x * 4 + y * 2 + channel * 40);

    return new() { Width = width, Height = height, Format = format, PixelData = pixels };
  }

  /// <summary>
  /// The picture as <see cref="Ffv1Decoder"/> hands it back: itself for grey and colour, and for
  /// luminance and chrominance the BT.601 conversion the decoder applies, sample for sample.
  /// </summary>
  private static (PixelFormat Format, byte[] Pixels) _AsTheDecoderReturnsIt(RawImage source) {
    switch (source.Format) {
      case PixelFormat.Gray8:
      case PixelFormat.GrayAlpha16:
      case PixelFormat.Rgb24:
      case PixelFormat.Rgba32:
        return (source.Format, source.PixelData);

      case PixelFormat.Bgr24: {
        var rgb = new byte[source.PixelData.Length];
        for (var i = 0; i < rgb.Length; i += 3) {
          rgb[i] = source.PixelData[i + 2];
          rgb[i + 1] = source.PixelData[i + 1];
          rgb[i + 2] = source.PixelData[i];
        }

        return (PixelFormat.Rgb24, rgb);
      }
    }

    var (horizontal, vertical) = RawImage.YuvSubsampling(source.Format);
    var horizontalShift = horizontal == 2 ? 1 : 0;
    var verticalShift = vertical == 2 ? 1 : 0;
    var luma = source.GetPlaneData(0).ToArray();
    var cb = source.GetPlaneData(1).ToArray();
    var cr = source.GetPlaneData(2).ToArray();
    var (chromaWidth, chromaHeight) = source.GetPlaneDimensions(1);
    var pixels = new byte[source.Width * source.Height * 3];

    for (var y = 0; y < source.Height; ++y) {
      var chromaRow = Math.Min(y >> verticalShift, chromaHeight - 1);
      for (var x = 0; x < source.Width; ++x) {
        var chromaColumn = Math.Min(x >> horizontalShift, chromaWidth - 1);
        var scaledLuma = 298 * (luma[y * source.Width + x] - 16);
        var blueDifference = cb[chromaRow * chromaWidth + chromaColumn] - 128;
        var redDifference = cr[chromaRow * chromaWidth + chromaColumn] - 128;
        var target = (y * source.Width + x) * 3;
        pixels[target] = _Clamp(scaledLuma + 409 * redDifference + 128);
        pixels[target + 1] = _Clamp(scaledLuma - 100 * blueDifference - 208 * redDifference + 128);
        pixels[target + 2] = _Clamp(scaledLuma + 516 * blueDifference + 128);
      }
    }

    return (PixelFormat.Rgb24, pixels);
  }

  private static byte _Clamp(int scaled) {
    var value = scaled >> 8;
    return (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
  }
}
