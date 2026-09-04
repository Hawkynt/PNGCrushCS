using System;
using System.IO;
using System.Linq;
using FileFormat.Avi;
using FileFormat.Codecs;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.MagicYuv.Tests;

/// <summary>
/// The MagicYUV encoder, measured against the package's own decoder.
/// </summary>
/// <remarks>
/// The codec is lossless, so the test of every frame is the same: the picture that went in is the
/// picture that comes out, sample for sample. The colour and grey layouts are compared as pixels
/// through the registry; the luminance layouts are compared as the planes the codec codes, because
/// the decoder turns those into pixels under a display convention that is no part of the coding.
/// </remarks>
[TestFixture]
public class MagicYuvEncoderTests {

  private static readonly (int Width, int Height, int Slices)[] _Geometries = [
    (1, 1, 1),
    (7, 5, 1),
    (33, 17, 3),
    (16, 11, 4),
    (64, 40, 8),
  ];

  private static readonly MagicYuvEncoder.Predictor[] _Predictors = [
    MagicYuvEncoder.Predictor.Left,
    MagicYuvEncoder.Predictor.Gradient,
    MagicYuvEncoder.Predictor.Median,
  ];

  // ============================================================================================
  // The description
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void DescribeStreamNamesTheCodeAndTheSizeTheDecoderNeeds() {
    var requested = new MediaStreamInfo {
      Index = 2,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("M8RA"),
      Width = 40,
      Height = 30,
      TimeBase = new Rational(1, 30),
      FrameRate = new Rational(30, 1),
      DeclaredFrameCount = 5,
      Name = "cover",
    };

    var stream = MagicYuvEncoder.Create(requested).DescribeStream();

    Assert.Multiple(() => {
      Assert.That(stream.Index, Is.EqualTo(2));
      Assert.That(stream.Kind, Is.EqualTo(MediaStreamKind.Video));
      Assert.That(stream.Codec, Is.EqualTo(CodecTag.FromCharacters("M8RA")));
      Assert.That(stream.Handler, Is.EqualTo(CodecTag.FromCharacters("M8RA")));
      Assert.That(stream.Width, Is.EqualTo(40));
      Assert.That(stream.Height, Is.EqualTo(30));
      Assert.That(stream.BitsPerPixel, Is.EqualTo(32));
      Assert.That(stream.TimeBase, Is.EqualTo(new Rational(1, 30)));
      Assert.That(stream.FrameRate, Is.EqualTo(new Rational(30, 1)));
      Assert.That(stream.DeclaredFrameCount, Is.EqualTo(5));
      Assert.That(stream.Name, Is.EqualTo("cover"));
      Assert.That(MagicYuvDecoder.Accepts(stream), Is.True);
      Assert.That(VideoFormatRegistry.CanDecode(stream), Is.True);
    });
  }

  [Test]
  [Category("Unit")]
  public void AStreamNamingNoCodeOfItsOwnIsGivenOneByItsDepth() {
    Assert.Multiple(() => {
      Assert.That(_Describe(CodecTag.None, 8).Codec, Is.EqualTo(CodecTag.FromCharacters("M8G0")));
      Assert.That(_Describe(CodecTag.None, 24).Codec, Is.EqualTo(CodecTag.FromCharacters("M8RG")));
      Assert.That(_Describe(CodecTag.None, 32).Codec, Is.EqualTo(CodecTag.FromCharacters("M8RA")));
      Assert.That(_Describe(CodecTag.FromCharacters("avc1"), 0).Codec, Is.EqualTo(CodecTag.FromCharacters("M8RG")));
      Assert.That(MagicYuvEncoder.Codec, Is.EqualTo(CodecTag.FromCharacters("M8RG")));
    });
  }

  [Test]
  [Category("Unit")]
  public void EveryCodeItWritesIsOneTheDecoderReads() {
    foreach (var code in new[] { "M8RG", "M8RA", "M8G0", "M8Y4", "M8Y2", "M8Y0" }) {
      var stream = MagicYuvEncoder.Create(_Stream(code, 8, 8)).DescribeStream();
      Assert.That(stream.Codec, Is.EqualTo(CodecTag.FromCharacters(code)), code);
      Assert.That(VideoFormatRegistry.CanDecode(stream), Is.True, code);
    }
  }

  // ============================================================================================
  // The frame
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheHeaderIsTheOneRealFramesCarry() {
    var encoder = MagicYuvEncoder.Create(_Stream("M8Y0", 37, 23), MagicYuvEncoder.Predictor.Median, 3);
    encoder.TryEncode(_Random(37, 23, PixelFormat.Yuv420P8, 1), 0, out var packet);
    var frame = packet.Data.ToArray();

    Assert.Multiple(() => {
      Assert.That(frame[..4], Is.EqualTo("MAGY"u8.ToArray()));
      Assert.That(BitConverter.ToUInt32(frame, 4), Is.EqualTo(32));
      Assert.That(frame[8], Is.EqualTo(7), "version");
      Assert.That(frame[9], Is.EqualTo(0x69), "format");
      Assert.That(frame[10], Is.EqualTo(12), "longest code");
      Assert.That(frame[11], Is.Zero, "interlace");
      Assert.That(frame[14], Is.EqualTo(0x20), "coder type");
      Assert.That(BitConverter.ToUInt32(frame, 16), Is.EqualTo(37), "width");
      Assert.That(BitConverter.ToUInt32(frame, 20), Is.EqualTo(23), "height");
      Assert.That(BitConverter.ToUInt32(frame, 24), Is.EqualTo(37));
      // 23 rows in 3 slices is 8 a slice, which is already a whole number of chrominance rows
      Assert.That(BitConverter.ToUInt32(frame, 28), Is.EqualTo(8), "slice height");
      // 3 planes, 3 slices, ten offsets, the first of which is where the tables end
      Assert.That(BitConverter.ToUInt32(frame, 32), Is.EqualTo(4 * 10 + 1 + 9 + 3 * 256));
      Assert.That(frame[32 + 40], Is.EqualTo(3), "table count");
    });
  }

  [Test]
  [Category("Unit")]
  public void ThePacketCarriesItsTimestampsAndIsAKeyFrame() {
    var encoder = MagicYuvEncoder.Create(_Stream("M8G0", 4, 4));

    Assert.That(encoder.TryEncode(_Random(4, 4, PixelFormat.Gray8, 3), 12, out var packet), Is.True);
    Assert.Multiple(() => {
      Assert.That(packet.StreamIndex, Is.Zero);
      Assert.That(packet.PresentationTimestamp, Is.EqualTo(12));
      Assert.That(packet.DecodeTimestamp, Is.EqualTo(12));
      Assert.That(packet.IsKeyFrame, Is.True);
    });

    Assert.That(encoder.TryEncode(_Random(4, 4, PixelFormat.Gray8, 4), null, out packet), Is.True);
    Assert.Multiple(() => {
      Assert.That(packet.PresentationTimestamp, Is.Null);
      Assert.That(packet.DecodeTimestamp, Is.Null);
      Assert.That(((IVideoPacketEncoder)encoder).Flush(), Is.Empty);
    });
  }

  [Test]
  [Category("Unit")]
  public void AFlatPictureCodesToAFractionOfItself() {
    var pixels = new byte[64 * 64 * 3];
    Array.Fill(pixels, (byte)0x80);
    var picture = new RawImage { Width = 64, Height = 64, Format = PixelFormat.Rgb24, PixelData = pixels };

    MagicYuvEncoder.Create(_Stream("M8RG", 64, 64)).TryEncode(picture, 0, out var packet);

    // three tables of 256 bytes and a handful of one-bit codes a plane
    Assert.That(packet.Data.Length, Is.LessThan(64 * 64 * 3 / 4));
    Assert.That(_Decode(packet, "M8RG", 64, 64).PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void ASliceTooSmallToCompressIsStoredPlainlyAndStillReadsBack() {
    // Four samples, and every symbol has a code whether it occurs or not, so the four that do are
    // seven bits each at best: four bytes of codes for four bytes of samples, which is ffmpeg's
    // rule for storing the slice as it is. The frame is far larger than the picture either way —
    // the table alone is 256 bytes — which is why real frames this small carry plain slices.
    var picture = _Random(4, 1, PixelFormat.Gray8, 7);
    MagicYuvEncoder.Create(_Stream("M8G0", 4, 1), MagicYuvEncoder.Predictor.Left, 1).TryEncode(picture, 0, out var packet);
    var frame = packet.Data.Span;

    var firstSlice = 32 + (int)BitConverter.ToUInt32(frame[32..36]);
    Assert.That(frame[firstSlice], Is.EqualTo(1), "stored uncompressed");
    Assert.That(frame[firstSlice + 1], Is.EqualTo(1), "left");
    Assert.That(_Decode(packet, "M8G0", 4, 1).PixelData, Is.EqualTo(picture.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void ASliceOfNoiseStillReadsBack() {
    var picture = _Random(64, 64, PixelFormat.Gray8, 7);
    MagicYuvEncoder.Create(_Stream("M8G0", 64, 64), MagicYuvEncoder.Predictor.Left, 1).TryEncode(picture, 0, out var packet);

    Assert.That(_Decode(packet, "M8G0", 64, 64).PixelData, Is.EqualTo(picture.PixelData));
  }

  // ============================================================================================
  // Round trips through the registry's decoder, as pixels
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void GreyReadsBackExactly([ValueSource(nameof(_Predictors))] MagicYuvEncoder.Predictor predictor) {
    foreach (var (width, height, slices) in _Geometries)
      foreach (var picture in _Pictures(width, height, PixelFormat.Gray8))
        _AssertRoundTrip("M8G0", picture, predictor, slices);
  }

  [Test]
  [Category("Unit")]
  public void ColourReadsBackExactly([ValueSource(nameof(_Predictors))] MagicYuvEncoder.Predictor predictor) {
    foreach (var (width, height, slices) in _Geometries)
      foreach (var picture in _Pictures(width, height, PixelFormat.Rgb24))
        _AssertRoundTrip("M8RG", picture, predictor, slices);
  }

  [Test]
  [Category("Unit")]
  public void ColourWithAlphaReadsBackExactly([ValueSource(nameof(_Predictors))] MagicYuvEncoder.Predictor predictor) {
    foreach (var (width, height, slices) in _Geometries)
      foreach (var picture in _Pictures(width, height, PixelFormat.Rgba32))
        _AssertRoundTrip("M8RA", picture, predictor, slices);
  }

  [Test]
  [Category("Unit")]
  public void APictureInAnotherLayoutIsConvertedFirst() {
    // grey handed to a colour stream is colour with three equal channels, and comes back as such
    var grey = _Random(9, 6, PixelFormat.Gray8, 11);
    var decoded = _Encode("M8RG", grey, MagicYuvEncoder.Predictor.Median, 2, "M8RG", 9, 6);

    Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(decoded.PixelData, Is.EqualTo(grey.PixelData.SelectMany(v => new[] { v, v, v }).ToArray()));
  }

  [Test]
  [Category("Unit")]
  public void AsManySlicesAsRowsAndMore() {
    // more slices than rows collapses to one a row; a slice count of one is one slice
    var picture = _Random(5, 3, PixelFormat.Rgb24, 21);
    foreach (var slices in new[] { 1, 2, 3, 7, 200 })
      _AssertRoundTrip("M8RG", picture, MagicYuvEncoder.Predictor.Gradient, slices);
  }

  [Test]
  [Category("Unit")]
  public void ASubsampledFrameIsNeverCutBetweenTheRowsAChrominanceRowCovers() {
    // 4:2:0 with 11 rows in 4 slices: 3 a slice would split a chrominance row, so it is 4
    var picture = _Random(10, 11, PixelFormat.Yuv420P8, 5);
    MagicYuvEncoder.Create(_Stream("M8Y0", 10, 11), MagicYuvEncoder.Predictor.Median, 4).TryEncode(picture, 0, out var packet);

    Assert.That(BitConverter.ToUInt32(packet.Data.Span[28..32]), Is.EqualTo(4));
    _AssertPlanes("M8Y0", picture, packet);
  }

  // ============================================================================================
  // Round trips as planes, for the layouts whose pixels are a convention
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void LuminanceReadsBackExactly(
    [Values("M8Y4", "M8Y2", "M8Y0")] string code,
    [ValueSource(nameof(_Predictors))] MagicYuvEncoder.Predictor predictor) {
    var format = code switch {
      "M8Y4" => PixelFormat.Yuv444P8,
      "M8Y2" => PixelFormat.Yuv422P8,
      _ => PixelFormat.Yuv420P8,
    };

    foreach (var (width, height, slices) in _Geometries)
      foreach (var picture in _Pictures(width, height, format)) {
        var encoder = MagicYuvEncoder.Create(_Stream(code, width, height), predictor, slices);
        Assert.That(encoder.TryEncode(picture, 0, out var packet), Is.True);
        _AssertPlanes(code, picture, packet);
      }
  }

  // ============================================================================================
  // Through a container and back
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AFrameSurvivesAnAviAndTheRegistry() {
    var requested = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("M8RG"),
      Width = 19,
      Height = 13,
      TimeBase = new Rational(1, 25),
      FrameRate = new Rational(25, 1),
      DeclaredFrameCount = 2,
    };
    var encoder = MagicYuvEncoder.Create(requested, MagicYuvEncoder.Predictor.Median, 2);
    var pictures = _Pictures(19, 13, PixelFormat.Rgb24).ToArray();
    var packets = pictures.Select((picture, i) => {
      encoder.TryEncode(picture, i, out var packet);
      return packet;
    }).ToArray();

    var avi = VideoIO.Mux<AviWriter>([encoder.DescribeStream()], packets);
    var decoded = VideoFormatRegistry.DecodeFrames(avi).Select(frame => frame.Image).ToArray();

    Assert.That(decoded, Has.Length.EqualTo(pictures.Length));
    for (var i = 0; i < pictures.Length; ++i) {
      Assert.That(decoded[i].Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(decoded[i].PixelData, Is.EqualTo(pictures[i].PixelData), $"frame {i}");
    }
  }

  // ============================================================================================
  // What refuses
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheLayoutsItCannotWriteAreRefusedByName() {
    foreach (var (code, reason) in new[] {
      ("M8YA", "alpha"),
      ("M8GA", "alpha"),
      ("MAGY", "before it gave each pixel format"),
      ("M0RG", "deeper than eight bits"),
      ("M2RA", "deeper than eight bits"),
      ("M4RG", "deeper than eight bits"),
    }) {
      var failure = Assert.Throws<NotSupportedException>(() => MagicYuvEncoder.Create(_Stream(code, 8, 8)), code);
      Assert.That(failure!.Message, Does.Contain(code).And.Contain(reason), code);
    }
  }

  [Test]
  [Category("Unit")]
  public void AStreamWithoutASizeOrOfAnotherKindIsRefused() {
    Assert.Multiple(() => {
      Assert.Throws<NotSupportedException>(() => MagicYuvEncoder.Create(_Stream("M8RG", 0, 8)));
      Assert.Throws<NotSupportedException>(() => MagicYuvEncoder.Create(_Stream("M8RG", 8, -1)));
      Assert.Throws<NotSupportedException>(() => MagicYuvEncoder.Create(new() { Index = 0, Kind = MediaStreamKind.Audio, Width = 8, Height = 8 }));
      Assert.Throws<ArgumentNullException>(() => MagicYuvEncoder.Create(null!));
      Assert.Throws<ArgumentOutOfRangeException>(() => MagicYuvEncoder.Create(_Stream("M8RG", 8, 8), MagicYuvEncoder.Predictor.Median, 0));
      Assert.Throws<ArgumentOutOfRangeException>(() => MagicYuvEncoder.Create(_Stream("M8RG", 8, 8), (MagicYuvEncoder.Predictor)4, 1));
    });
  }

  [Test]
  [Category("Unit")]
  public void APictureOfAnotherSizeOrShortOfBytesIsRefused() {
    var encoder = MagicYuvEncoder.Create(_Stream("M8RG", 8, 8));
    var wrongSize = _Random(4, 4, PixelFormat.Rgb24, 1);
    var tooShort = new RawImage { Width = 8, Height = 8, Format = PixelFormat.Rgb24, PixelData = new byte[8 * 8 * 3 - 1] };

    Assert.Multiple(() => {
      Assert.Throws<InvalidDataException>(() => encoder.TryEncode(wrongSize, 0, out _));
      Assert.Throws<InvalidDataException>(() => encoder.TryEncode(tooShort, 0, out _));
      Assert.Throws<ArgumentNullException>(() => encoder.TryEncode(null!, 0, out _));
    });
  }

  // ============================================================================================

  private static MediaStreamInfo _Stream(string code, int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters(code),
    Width = width,
    Height = height,
  };

  private static MediaStreamInfo _Describe(CodecTag codec, int bitsPerPixel)
    => MagicYuvEncoder.Create(new() { Index = 0, Kind = MediaStreamKind.Video, Codec = codec, Width = 8, Height = 8, BitsPerPixel = bitsPerPixel }).DescribeStream();

  private static RawImage _Random(int width, int height, PixelFormat format, int seed) {
    var picture = new RawImage { Width = width, Height = height, Format = format, PixelData = [] };
    var pixels = new byte[picture.MinimumPixelDataLength];
    new Random(seed).NextBytes(pixels);
    return new() { Width = width, Height = height, Format = format, PixelData = pixels };
  }

  /// <summary>A smooth ramp, which is what the predictors are for, with the odd sharp edge in it.</summary>
  private static RawImage _Gradient(int width, int height, PixelFormat format) {
    var empty = new RawImage { Width = width, Height = height, Format = format, PixelData = [] };
    var pixels = new byte[empty.MinimumPixelDataLength];
    var channels = RawImage.IsPlanarYuvFormat(format) ? 1 : RawImage.BytesPerPixel(format);
    for (var i = 0; i < pixels.Length; ++i) {
      var pixel = i / channels;
      var channel = i % channels;
      var x = pixel % width;
      var y = pixel / width;
      pixels[i] = (byte)(x * 3 + y * 5 + channel * 70 + ((x / 4 + y / 4) % 2 == 0 ? 0 : 40));
    }

    return new() { Width = width, Height = height, Format = format, PixelData = pixels };
  }

  private static RawImage[] _Pictures(int width, int height, PixelFormat format)
    => [_Random(width, height, format, width * 131 + height), _Gradient(width, height, format)];

  private static RawImage _Encode(string code, RawImage picture, MagicYuvEncoder.Predictor predictor, int slices, string expectedCode, int width, int height) {
    var encoder = MagicYuvEncoder.Create(_Stream(code, width, height), predictor, slices);
    Assert.That(encoder.TryEncode(picture, 0, out var packet), Is.True);
    return _Decode(packet, expectedCode, width, height);
  }

  private static RawImage _Decode(CodedPacket packet, string code, int width, int height) {
    var decoder = VideoFormatRegistry.CreateDecoder(_Stream(code, width, height));
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
    return decoded;
  }

  private static void _AssertRoundTrip(string code, RawImage picture, MagicYuvEncoder.Predictor predictor, int slices) {
    var decoded = _Encode(code, picture, predictor, slices, code, picture.Width, picture.Height);
    var label = $"{code} {picture.Width}x{picture.Height} {predictor} {slices} slices";
    Assert.That(decoded.Format, Is.EqualTo(picture.Format), label);
    Assert.That(decoded.PixelData, Is.EqualTo(picture.PixelData), label);
  }

  private static void _AssertPlanes(string code, RawImage picture, CodedPacket packet) {
    var planes = MagicYuvDecoder.Create(_Stream(code, picture.Width, picture.Height)).DecodePlanes(packet.Data);
    var label = $"{code} {picture.Width}x{picture.Height}";
    Assert.That(planes, Has.Length.EqualTo(3), label);
    for (var plane = 0; plane < 3; ++plane)
      Assert.That(planes[plane], Is.EqualTo(picture.GetPlaneData(plane).ToArray()), $"{label} plane {plane}");
  }
}
