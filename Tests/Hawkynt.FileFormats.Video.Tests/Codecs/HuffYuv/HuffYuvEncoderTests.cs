using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Avi;
using FileFormat.Core;
using FileFormat.Matroska;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.HuffYuv.Tests;

/// <summary>
/// The HuffYUV and FFVHUFF encoder, measured against the package's own decoder.
/// </summary>
/// <remarks>
/// Every layout the encoder writes, with every predictor it allows in it, is coded and read back
/// through <see cref="VideoFormatRegistry.CreateDecoder"/> from nothing but the encoder's own stream
/// description, on random and on smooth content at several sizes — and has to come back sample for
/// sample. The luminance-and-chrominance layout comes back through the decoder's stated conversion
/// to colour, so it is compared against that conversion of the source rather than against the
/// source itself.
/// </remarks>
[TestFixture]
public class HuffYuvEncoderTests {

  private const int _BITMAP_INFO_HEADER_SIZE = 40;

  private static readonly (int Width, int Height)[] _Geometries = [(1, 1), (3, 2), (17, 9), (64, 33)];
  private static readonly (int Width, int Height)[] _EvenGeometries = [(2, 1), (4, 3), (10, 7), (64, 33)];
  private static readonly (int Width, int Height)[] _MedianGeometries = [(4, 2), (10, 7), (64, 33)];

  // ============================================================================================
  // The description
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void DescribesAnInterleaved422StreamTheDecoderAccepts() {
    var encoder = HuffYuvEncoder.Create(_Request(16, 8, 16, timeBase: new Rational(1, 25)), HuffYuvPredictionMethod.Median);
    Assert.That(encoder.TryEncode(_Random(PixelFormat.Yuv422P8, 16, 8, 1), 3, out _), Is.True);

    var described = encoder.DescribeStream();
    var format = described.CodecPrivateData.ToArray();
    Assert.Multiple(() => {
      Assert.That(described.Codec, Is.EqualTo(CodecTag.FromCharacters("HFYU")));
      Assert.That(described.Handler, Is.EqualTo(CodecTag.FromCharacters("HFYU")));
      Assert.That(described.CodecId, Is.EqualTo("V_MS/VFW/FOURCC"));
      Assert.That(described.Width, Is.EqualTo(16));
      Assert.That(described.Height, Is.EqualTo(8));
      Assert.That(described.BitsPerPixel, Is.EqualTo(16));
      Assert.That(described.TimeBase, Is.EqualTo(new Rational(1, 25)));
      Assert.That(format.Length, Is.GreaterThan(_BITMAP_INFO_HEADER_SIZE + 4));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(format), Is.EqualTo((uint)format.Length), "biSize spans the description");
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(format[4..]), Is.EqualTo(16));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(format[8..]), Is.EqualTo(8));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(format[14..]), Is.EqualTo(16));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(format[16..]), Is.EqualTo(CodecTag.FromCharacters("HFYU").Value));
      Assert.That(format[_BITMAP_INFO_HEADER_SIZE], Is.EqualTo(2), "median");
      Assert.That(format[_BITMAP_INFO_HEADER_SIZE + 1], Is.EqualTo(16), "bitstream depth");
      Assert.That(format[_BITMAP_INFO_HEADER_SIZE + 2], Is.EqualTo(0x20), "progressive, tables in the stream");
      Assert.That(format[_BITMAP_INFO_HEADER_SIZE + 3], Is.EqualTo(0), "the second form");
      Assert.That(HuffYuvDecoder.Accepts(described), Is.True);
    });

    var tables = HuffYuvHuffmanTable.ReadAll(format[_BITMAP_INFO_HEADER_SIZE..], 4, 3, out var end);
    Assert.Multiple(() => {
      Assert.That(tables, Has.Length.EqualTo(3));
      Assert.That(end, Is.EqualTo(format.Length - _BITMAP_INFO_HEADER_SIZE), "the tables run to the end of the description");
    });
  }

  [Test]
  [Category("Unit")]
  public void DescribesAPlanarStreamInTheThirdFormUnderTheExtensionTag() {
    var encoder = HuffYuvEncoder.Create(_Request(5, 4, 32), HuffYuvPredictionMethod.Gradient, planar: true);
    var described = encoder.DescribeStream();
    var extra = described.CodecPrivateData.ToArray()[_BITMAP_INFO_HEADER_SIZE..];

    Assert.Multiple(() => {
      Assert.That(described.Codec, Is.EqualTo(CodecTag.FromCharacters("FFVH")));
      Assert.That(described.BitsPerPixel, Is.EqualTo(32));
      Assert.That(extra[0], Is.EqualTo(1), "gradient, no decorrelation");
      Assert.That(extra[1], Is.EqualTo(0x70), "eight bits, no subsampling");
      Assert.That(extra[2], Is.EqualTo(0x20 | 0x02 | 0x04), "progressive, green-blue-red, alpha");
      Assert.That(extra[3], Is.EqualTo(1), "the third form");
      Assert.That(HuffYuvDecoder.Accepts(described), Is.True);
      Assert.That(() => HuffYuvDecoder.Create(described), Throws.Nothing);
    });

    HuffYuvHuffmanTable.ReadAll(extra, 4, 4, out var end);
    Assert.That(end, Is.EqualTo(extra.Length));
  }

  [Test]
  [Category("Unit")]
  public void PackedColourStatesDecorrelationAndTheOriginalTag() {
    var encoder = HuffYuvEncoder.Create(_Request(4, 4, 24));
    var extra = encoder.DescribeStream().CodecPrivateData.ToArray()[_BITMAP_INFO_HEADER_SIZE..];

    Assert.Multiple(() => {
      Assert.That(encoder.DescribeStream().Codec, Is.EqualTo(CodecTag.FromCharacters("HFYU")));
      Assert.That(extra[0], Is.EqualTo(0x40), "left, decorrelated");
      Assert.That(extra[1], Is.EqualTo(24));
      Assert.That(extra[3], Is.EqualTo(0));
    });
  }

  [Test]
  [Category("Unit")]
  public void TheRequestedTagIsKeptWhereItIsOneOfTheTwo() {
    Assert.Multiple(() => {
      Assert.That(HuffYuvEncoder.Create(_Request(4, 4, 16, "FFVH")).DescribeStream().Codec, Is.EqualTo(CodecTag.FromCharacters("FFVH")));
      Assert.That(HuffYuvEncoder.Create(_Request(4, 4, 8, "HFYU")).DescribeStream().Codec, Is.EqualTo(CodecTag.FromCharacters("HFYU")));
      Assert.That(HuffYuvEncoder.Create(_Request(4, 4, 8, "MJPG")).DescribeStream().Codec, Is.EqualTo(CodecTag.FromCharacters("FFVH")));
      Assert.That(HuffYuvEncoder.Codec, Is.EqualTo(CodecTag.FromCharacters("HFYU")));
    });
  }

  [Test]
  [Category("Unit")]
  public void ADescriptionReadFromAContainerChoosesTheLayoutAndPredictor() {
    var source = HuffYuvEncoder.Create(_Request(8, 6, 16), HuffYuvPredictionMethod.Median);
    Assert.That(source.TryEncode(_Random(PixelFormat.Yuv422P8, 8, 6, 5), null, out _), Is.True);
    var read = source.DescribeStream();

    var again = HuffYuvEncoder.Create(new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = read.Codec,
      Width = 8,
      Height = 6,
      BitsPerPixel = 0,
      CodecPrivateData = read.CodecPrivateData,
    });
    var frame = _Gradient(PixelFormat.Yuv422P8, 8, 6);
    Assert.That(again.TryEncode(frame, null, out var packet), Is.True);
    var described = again.DescribeStream();
    var extra = described.CodecPrivateData.ToArray()[_BITMAP_INFO_HEADER_SIZE..];

    Assert.Multiple(() => {
      Assert.That(extra[0], Is.EqualTo(2), "median, as the description said");
      Assert.That(extra[1], Is.EqualTo(16), "4:2:2, as the description said");
      Assert.That(_Decode(described, packet), Is.EqualTo(_Expected(frame)));
    });
  }

  [Test]
  [Category("Unit")]
  public void TheFourDescriptionBytesAloneAreReadToo() {
    var encoder = HuffYuvEncoder.Create(new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Width = 6,
      Height = 4,
      CodecPrivateData = new byte[] { 1, 0x70, 0x22, 1 },
    });
    var frame = _Random(PixelFormat.Rgb24, 6, 4, 9);
    Assert.That(encoder.TryEncode(frame, null, out var packet), Is.True);
    var described = encoder.DescribeStream();
    var extra = described.CodecPrivateData.ToArray()[_BITMAP_INFO_HEADER_SIZE..];

    Assert.Multiple(() => {
      Assert.That(extra[0], Is.EqualTo(1), "gradient");
      Assert.That(extra[2], Is.EqualTo(0x22), "planar colour");
      Assert.That(_Decode(described, packet), Is.EqualTo(frame.PixelData));
    });
  }

  // ============================================================================================
  // Round trips
  // ============================================================================================

  [Test]
  [Category("Unit")]
  [TestCase(HuffYuvPredictionMethod.Left)]
  [TestCase(HuffYuvPredictionMethod.Gradient)]
  [TestCase(HuffYuvPredictionMethod.Median)]
  public void Interleaved422RoundTripsExactly(HuffYuvPredictionMethod prediction)
    => _RoundTrips(PixelFormat.Yuv422P8, 16, prediction, planar: false, prediction == HuffYuvPredictionMethod.Median ? _MedianGeometries : _EvenGeometries);

  [Test]
  [Category("Unit")]
  [TestCase(HuffYuvPredictionMethod.Left)]
  [TestCase(HuffYuvPredictionMethod.Gradient)]
  public void PackedColourRoundTripsExactly(HuffYuvPredictionMethod prediction)
    => _RoundTrips(PixelFormat.Rgb24, 24, prediction, planar: false, _Geometries);

  [Test]
  [Category("Unit")]
  [TestCase(HuffYuvPredictionMethod.Left)]
  [TestCase(HuffYuvPredictionMethod.Gradient)]
  public void PackedColourWithAlphaRoundTripsExactly(HuffYuvPredictionMethod prediction)
    => _RoundTrips(PixelFormat.Bgra32, 32, prediction, planar: false, _Geometries);

  [Test]
  [Category("Unit")]
  [TestCase(HuffYuvPredictionMethod.Left)]
  [TestCase(HuffYuvPredictionMethod.Gradient)]
  [TestCase(HuffYuvPredictionMethod.Median)]
  public void GreyRoundTripsExactly(HuffYuvPredictionMethod prediction)
    => _RoundTrips(PixelFormat.Gray8, 8, prediction, planar: true, _Geometries);

  [Test]
  [Category("Unit")]
  [TestCase(HuffYuvPredictionMethod.Left)]
  [TestCase(HuffYuvPredictionMethod.Gradient)]
  [TestCase(HuffYuvPredictionMethod.Median)]
  public void PlanarColourRoundTripsExactly(HuffYuvPredictionMethod prediction)
    => _RoundTrips(PixelFormat.Rgb24, 24, prediction, planar: true, _Geometries);

  [Test]
  [Category("Unit")]
  [TestCase(HuffYuvPredictionMethod.Left)]
  [TestCase(HuffYuvPredictionMethod.Gradient)]
  [TestCase(HuffYuvPredictionMethod.Median)]
  public void PlanarColourWithAlphaRoundTripsExactly(HuffYuvPredictionMethod prediction)
    => _RoundTrips(PixelFormat.Rgba32, 32, prediction, planar: true, _Geometries);

  private static void _RoundTrips(PixelFormat format, int bitsPerPixel, HuffYuvPredictionMethod prediction, bool planar, (int Width, int Height)[] geometries) {
    foreach (var (width, height) in geometries)
      foreach (var (name, frame) in new[] { ("random", _Random(format, width, height, width * 31 + height)), ("gradient", _Gradient(format, width, height)), ("flat", _Flat(format, width, height)) }) {
        var encoder = HuffYuvEncoder.Create(_Request(width, height, bitsPerPixel), prediction, planar);
        Assert.That(encoder.TryEncode(frame, null, out var packet), Is.True);
        Assert.That(packet.IsKeyFrame, Is.True);
        Assert.That(packet.Data.Length % 4, Is.Zero, "a frame is whole words");

        var decoded = _Decode(encoder.DescribeStream(), packet);
        Assert.That(decoded, Is.EqualTo(_Expected(frame)), $"{format} {prediction} {(planar ? "planar" : "packed")} {width}x{height} {name}");
      }
  }

  [Test]
  [Category("Unit")]
  public void ASequenceIsCodedAgainstTheFirstFramesTablesAndStaysExact() {
    var encoder = HuffYuvEncoder.Create(_Request(24, 10, 24), HuffYuvPredictionMethod.Gradient);
    var frames = new List<RawImage> { _Flat(PixelFormat.Rgb24, 24, 10) };
    for (var i = 0; i < 4; ++i)
      frames.Add(_Random(PixelFormat.Rgb24, 24, 10, 100 + i));

    var packets = new List<CodedPacket>();
    foreach (var frame in frames) {
      Assert.That(encoder.TryEncode(frame, packets.Count, out var packet), Is.True);
      packets.Add(packet);
    }

    var first = encoder.DescribeStream();
    Assert.That(encoder.DescribeStream().CodecPrivateData.ToArray(), Is.EqualTo(first.CodecPrivateData.ToArray()), "the description does not change once handed out");

    var decoder = VideoFormatRegistry.CreateDecoder(first);
    for (var i = 0; i < frames.Count; ++i) {
      Assert.That(decoder.TryDecode(packets[i], out var decoded), Is.True);
      Assert.That(decoded.PixelData, Is.EqualTo(frames[i].PixelData), $"frame {i}");
      Assert.That(packets[i].PresentationTimestamp, Is.EqualTo(i));
    }

    Assert.That(((IVideoPacketEncoder)encoder).Flush(), Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void ADescriptionAskedForBeforeAnyPictureStillDecodesEveryPicture() {
    var encoder = HuffYuvEncoder.Create(_Request(12, 6, 8), HuffYuvPredictionMethod.Median);
    var described = encoder.DescribeStream();
    var frame = _Random(PixelFormat.Gray8, 12, 6, 77);

    Assert.That(encoder.TryEncode(frame, null, out var packet), Is.True);
    Assert.Multiple(() => {
      Assert.That(encoder.DescribeStream().CodecPrivateData.ToArray(), Is.EqualTo(described.CodecPrivateData.ToArray()));
      Assert.That(_Decode(described, packet), Is.EqualTo(frame.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void TimestampsPassThroughAndEveryPacketIsAKeyFrame() {
    var encoder = HuffYuvEncoder.Create(_Request(4, 4, 24));
    Assert.That(encoder.TryEncode(_Random(PixelFormat.Rgb24, 4, 4, 3), 1234, out var stamped), Is.True);
    Assert.That(encoder.TryEncode(_Random(PixelFormat.Rgb24, 4, 4, 4), null, out var unstamped), Is.True);

    Assert.Multiple(() => {
      Assert.That(stamped.StreamIndex, Is.Zero);
      Assert.That(stamped.PresentationTimestamp, Is.EqualTo(1234));
      Assert.That(stamped.DecodeTimestamp, Is.EqualTo(1234));
      Assert.That(stamped.IsKeyFrame, Is.True);
      Assert.That(unstamped.PresentationTimestamp, Is.Null);
      Assert.That(unstamped.DecodeTimestamp, Is.Null);
      Assert.That(unstamped.IsKeyFrame, Is.True);
    });
  }

  [Test]
  [Category("Unit")]
  public void APictureInAnotherFormatIsConvertedToTheLayoutFirst() {
    var encoder = HuffYuvEncoder.Create(_Request(6, 5, 24), HuffYuvPredictionMethod.Gradient);
    var frame = _Random(PixelFormat.Bgra32, 6, 5, 8);
    Assert.That(encoder.TryEncode(frame, null, out var packet), Is.True);

    Assert.That(_Decode(encoder.DescribeStream(), packet), Is.EqualTo(frame.ToRgb24()));
  }

  [Test]
  [Category("Unit")]
  public void MuxesIntoAviAndMatroskaAndComesBackThroughTheRegistry() {
    var encoder = HuffYuvEncoder.Create(_Request(8, 4, 16, timeBase: new Rational(1, 25), frameRate: new Rational(25, 1)), HuffYuvPredictionMethod.Median);
    var frames = new[] { _Random(PixelFormat.Yuv422P8, 8, 4, 21), _Gradient(PixelFormat.Yuv422P8, 8, 4) };
    var packets = new List<CodedPacket>();
    for (var i = 0; i < frames.Length; ++i) {
      Assert.That(encoder.TryEncode(frames[i], i, out var packet), Is.True);
      packets.Add(packet with { Duration = 1 });
    }

    var described = encoder.DescribeStream();

    var avi = AviContainer.FromBytes(VideoIO.Mux<AviWriter>([described], packets));
    var aviFrames = VideoIO.Decode(AviContainer.ReadPackets(avi), AviContainer.Streams(avi)[0], VideoFormatRegistry.CreateDecoder).ToList();
    var mkv = MatroskaContainer.FromBytes(VideoIO.Mux<MatroskaWriter>([described], packets));
    var mkvFrames = VideoIO.Decode(MatroskaContainer.ReadPackets(mkv), MatroskaContainer.Streams(mkv)[0], VideoFormatRegistry.CreateDecoder).ToList();

    Assert.Multiple(() => {
      Assert.That(aviFrames, Has.Count.EqualTo(2));
      Assert.That(mkvFrames, Has.Count.EqualTo(2));
      for (var i = 0; i < frames.Length; ++i) {
        Assert.That(aviFrames[i].Image.PixelData, Is.EqualTo(_Expected(frames[i])), $"AVI frame {i}");
        Assert.That(mkvFrames[i].Image.PixelData, Is.EqualTo(_Expected(frames[i])), $"Matroska frame {i}");
      }
    });
  }

  // ============================================================================================
  // Refusals
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void RefusesWhatItDoesNotWrite() {
    Assert.Multiple(() => {
      Assert.That(() => HuffYuvEncoder.Create(_Request(4, 4, 12)), Throws.TypeOf<NotSupportedException>().With.Message.Contains("12 bits"), "an unwritten depth");
      Assert.That(() => HuffYuvEncoder.Create(_Request(5, 4, 16)), Throws.TypeOf<NotSupportedException>().With.Message.Contains("even"), "odd 4:2:2");
      Assert.That(() => HuffYuvEncoder.Create(_Request(4, 4, 24), HuffYuvPredictionMethod.Median), Throws.TypeOf<NotSupportedException>().With.Message.Contains("median"), "median on packed colour");
      Assert.That(() => HuffYuvEncoder.Create(_Request(2, 2, 16), HuffYuvPredictionMethod.Median), Throws.TypeOf<NotSupportedException>().With.Message.Contains("median"), "median 4:2:2 narrower than a group of four");
      Assert.That(() => HuffYuvEncoder.Create(_Request(4, 1, 16), HuffYuvPredictionMethod.Median), Throws.TypeOf<NotSupportedException>().With.Message.Contains("median"), "median 4:2:2 of one row");
      Assert.That(() => HuffYuvEncoder.Create(_Request(4, 4, 16), HuffYuvPredictionMethod.Left, planar: true), Throws.TypeOf<NotSupportedException>().With.Message.Contains("planar"), "planar 4:2:2");
      Assert.That(() => HuffYuvEncoder.Create(_Request(0, 4, 24)), Throws.TypeOf<NotSupportedException>(), "no size");
      Assert.That(() => HuffYuvEncoder.Create(_Request(4, 4, 24), (HuffYuvPredictionMethod)7), Throws.TypeOf<NotSupportedException>(), "no such predictor");
      Assert.That(() => HuffYuvEncoder.Create(new MediaStreamInfo { Index = 0, Kind = MediaStreamKind.Audio, Width = 4, Height = 4 }), Throws.TypeOf<NotSupportedException>(), "not a picture");
    });
  }

  [Test]
  [Category("Unit")]
  public void RefusesDescriptionsOfWhatItDoesNotWrite() {
    Assert.Multiple(() => {
      Assert.That(() => _WithDescription(0, 12, 0x20, 0), Throws.TypeOf<NotSupportedException>().With.Message.Contains("4:2:0"), "4:2:0");
      Assert.That(() => _WithDescription(0, 16, 0x10, 0), Throws.TypeOf<NotSupportedException>().With.Message.Contains("interlaced"), "interlaced");
      Assert.That(() => _WithDescription(0, 16, 0x60, 0), Throws.TypeOf<NotSupportedException>().With.Message.Contains("every frame"), "tables per frame");
      Assert.That(() => _WithDescription(3, 16, 0x20, 0), Throws.TypeOf<NotSupportedException>().With.Message.Contains("method 3"), "an unknown predictor");
      Assert.That(() => _WithDescription(0, 0x71, 0x21, 1), Throws.TypeOf<NotSupportedException>().With.Message.Contains("planes"), "planar 4:2:2");
      Assert.That(() => _WithDescription(0, 0x90, 0x22, 1), Throws.TypeOf<NotSupportedException>().With.Message.Contains("10-bit"), "deeper samples");
      Assert.That(() => _WithDescription(0x40, 24, 0x20, 0, HuffYuvPredictionMethod.Median), Throws.TypeOf<NotSupportedException>(), "median in a packed description");
      Assert.That(() => HuffYuvEncoder.Create(new MediaStreamInfo { Index = 0, Kind = MediaStreamKind.Video, Width = 4, Height = 4, CodecPrivateData = new byte[] { 2, 16 } }), Throws.TypeOf<NotSupportedException>().With.Message.Contains("2-byte"), "a truncated description");
    });
  }

  [Test]
  [Category("Unit")]
  public void RefusesAPictureOfAnotherSizeOrTooFewBytes() {
    var encoder = HuffYuvEncoder.Create(_Request(4, 4, 24));
    var wrongSize = new RawImage { Width = 2, Height = 2, Format = PixelFormat.Rgb24, PixelData = new byte[12] };
    var short_ = new RawImage { Width = 4, Height = 4, Format = PixelFormat.Rgb24, PixelData = new byte[10] };

    Assert.Multiple(() => {
      Assert.That(() => encoder.TryEncode(wrongSize, null, out _), Throws.TypeOf<InvalidDataException>());
      Assert.That(() => encoder.TryEncode(short_, null, out _), Throws.TypeOf<InvalidDataException>());
    });
  }

  private static HuffYuvEncoder _WithDescription(byte method, byte depth, byte flags, byte form, HuffYuvPredictionMethod? forcedMethod = null) {
    var description = new byte[] { forcedMethod == null ? method : (byte)((int)forcedMethod | (method & 0x40)), depth, flags, form };
    return HuffYuvEncoder.Create(new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Width = 4,
      Height = 4,
      BitsPerPixel = 16,
      CodecPrivateData = description,
    });
  }

  // ============================================================================================
  // Helpers
  // ============================================================================================

  private static MediaStreamInfo _Request(int width, int height, int bitsPerPixel, string? tag = null, Rational? timeBase = null, Rational? frameRate = null) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = tag == null ? CodecTag.None : CodecTag.FromCharacters(tag),
    Width = width,
    Height = height,
    BitsPerPixel = bitsPerPixel,
    TimeBase = timeBase ?? Rational.Unknown,
    FrameRate = frameRate ?? Rational.Unknown,
  };

  private static byte[] _Decode(MediaStreamInfo described, CodedPacket packet) {
    var decoder = VideoFormatRegistry.CreateDecoder(described);
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
    return decoded.PixelData;
  }

  private static int _Bytes(PixelFormat format, int width, int height) => format switch {
    PixelFormat.Yuv422P8 => width * height + 2 * ((width + 1) / 2) * height,
    _ => width * height * RawImage.BytesPerPixel(format),
  };

  private static RawImage _Random(PixelFormat format, int width, int height, int seed) {
    var pixels = new byte[_Bytes(format, width, height)];
    new Random(seed).NextBytes(pixels);
    return new() { Width = width, Height = height, Format = format, PixelData = pixels };
  }

  private static RawImage _Gradient(PixelFormat format, int width, int height) {
    var pixels = new byte[_Bytes(format, width, height)];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 255 / Math.Max(1, pixels.Length - 1) + (i % 7));

    return new() { Width = width, Height = height, Format = format, PixelData = pixels };
  }

  private static RawImage _Flat(PixelFormat format, int width, int height) {
    var pixels = new byte[_Bytes(format, width, height)];
    Array.Fill(pixels, (byte)93);
    return new() { Width = width, Height = height, Format = format, PixelData = pixels };
  }

  /// <summary>
  /// What the decoder hands back for a picture: the samples themselves, except that a
  /// luminance-and-chrominance picture comes back as colour through the decoder's stated ITU-R
  /// BT.601 studio-swing conversion, and one that went in as <c>BGRA</c> comes back as <c>RGBA</c>.
  /// </summary>
  private static byte[] _Expected(RawImage frame) {
    switch (frame.Format) {
      case PixelFormat.Bgra32:
        return frame.ToRgba32();
      case PixelFormat.Yuv422P8: {
        var width = frame.Width;
        var chromaWidth = (width + 1) / 2;
        var luma = frame.GetPlaneData(0);
        var cb = frame.GetPlaneData(1);
        var cr = frame.GetPlaneData(2);
        var rgb = new byte[width * frame.Height * 3];
        for (var y = 0; y < frame.Height; ++y)
          for (var x = 0; x < width; ++x) {
            var scaled = 298 * (luma[y * width + x] - 16);
            var blue = cb[y * chromaWidth + x / 2] - 128;
            var red = cr[y * chromaWidth + x / 2] - 128;
            var at = (y * width + x) * 3;
            rgb[at] = _Clamp(scaled + 409 * red + 128);
            rgb[at + 1] = _Clamp(scaled - 100 * blue - 208 * red + 128);
            rgb[at + 2] = _Clamp(scaled + 516 * blue + 128);
          }

        return rgb;
      }
      default:
        return frame.PixelData;
    }
  }

  private static byte _Clamp(int scaled) {
    var value = scaled >> 8;
    return (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
  }
}
