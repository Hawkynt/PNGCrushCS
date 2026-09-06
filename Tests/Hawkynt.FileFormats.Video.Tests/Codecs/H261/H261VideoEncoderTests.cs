using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Codecs.H263;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.H261.Tests;

/// <summary>
/// The H.261 encoder, on pictures encoded here and read back through this library's own decoder.
/// </summary>
/// <remarks>
/// A writer checked only against the reader beside it is worth very little — the two can share one
/// misunderstanding and agree perfectly on it — so what the encoder writes was measured against
/// ffmpeg's own H.261 decoder over six streams, and those numbers are in <c>codec-notes.md</c> rather
/// than here, ffmpeg not being present on a build machine. What these tests add is what that comparison
/// cannot state: the refusals, the shape of the packets, and the one property a round trip can prove on
/// its own — that a picture comes back closer to itself than to any other picture of the sequence,
/// which no motion vector read from the wrong place and no prediction taken from the wrong macroblock
/// survives.
/// </remarks>
[TestFixture]
public sealed class H261VideoEncoderTests {

  /// <summary>How far a decoded picture may sit from what went in, as a mean squared error per sample.</summary>
  /// <remarks>
  /// H.261 has no lossless form: every coded block goes through the transform and a quantiser stated
  /// five bits wide, so this bound describes the format rather than the implementation. It is set where
  /// a picture is still plainly the picture that went in — around 34 dB — and far below the distance to
  /// any other picture of a moving sequence, which is what the comparison below actually turns on.
  /// </remarks>
  private const double _WORST_MEAN_SQUARED_ERROR = 32d;

  // ============================================================================================
  // Geometry: QCIF and CIF, and nothing else
  // ============================================================================================

  [TestCase(176, 144)]
  [TestCase(352, 288)]
  [Category("Unit")]
  public void TheTwoFormatsClauseThreePointOneDefinesAreAccepted(int width, int height) {
    var encoder = H261VideoEncoder.Create(_Stream(width, height));

    Assert.Multiple(() => {
      Assert.That(encoder.DescribeStream().Width, Is.EqualTo(width));
      Assert.That(encoder.DescribeStream().Height, Is.EqualTo(height));
    });
  }

  [TestCase(320, 240)]
  [TestCase(352, 240)]
  [TestCase(176, 288)]
  [TestCase(704, 576)]
  [TestCase(178, 144)]
  [TestCase(0, 0)]
  [Category("Unit")]
  public void EveryOtherPictureSizeIsRefusedByName(int width, int height) {
    var refusal = Assert.Throws<NotSupportedException>(() => H261VideoEncoder.Create(_Stream(width, height)));

    Assert.Multiple(() => {
      Assert.That(refusal!.Message, Does.Contain($"{width}x{height}"));
      Assert.That(refusal.Message, Does.Contain("QCIF"));
      Assert.That(refusal.Message, Does.Contain("CIF"));
    });
  }

  [Test]
  [Category("Unit")]
  public void AnAudioStreamIsRefused() {
    var refusal = Assert.Throws<NotSupportedException>(() => H261VideoEncoder.Create(
      new() { Index = 0, Kind = MediaStreamKind.Audio, Codec = CodecTag.FromCharacters("H261"), Width = 176, Height = 144 }));

    Assert.That(refusal!.Message, Does.Contain("video"));
  }

  [Test]
  [Category("Unit")]
  public void APictureOfADifferentSizeFromTheStreamIsRefused() {
    var encoder = H261VideoEncoder.Create(_Stream(176, 144));
    var refusal = Assert.Throws<InvalidDataException>(
      () => encoder.TryEncode(_Picture(352, 288, 0), 0, out _));

    Assert.That(refusal!.Message, Does.Contain("352x288"));
  }

  // ============================================================================================
  // The round trip
  // ============================================================================================

  [TestCase(176, 144)]
  [TestCase(352, 288)]
  [Category("Unit")]
  public void WhatTheEncoderWritesIsWhatTheDecoderReads(int width, int height) {
    const int frames = 15;

    var encoder = H261VideoEncoder.Create(_Stream(width, height));
    var sources = new List<RawImage>();
    var packets = new List<CodedPacket>();

    for (var index = 0; index < frames; ++index) {
      var picture = _Picture(width, height, index);
      sources.Add(picture);

      Assert.That(encoder.TryEncode(picture, index, out var packet), Is.True, $"frame {index}");
      packets.Add(packet);
    }

    Assert.Multiple(() => {
      Assert.That(packets[0].IsKeyFrame, Is.True, "a stream has to begin at an intra picture");
      Assert.That(packets.Skip(1).Take(11).Any(p => p.IsKeyFrame), Is.False, "intra pictures are twelve apart");
      Assert.That(packets[12].IsKeyFrame, Is.True);
    });

    var decoded = _DecodePlanes(packets, width, height);
    Assert.That(decoded, Has.Count.EqualTo(frames));

    for (var index = 0; index < frames; ++index) {
      var error = _MeanSquaredError(sources[index].PixelData, decoded[index]);
      Assert.That(error, Is.LessThan(_WORST_MEAN_SQUARED_ERROR),
        $"frame {index} came back {error:F1} squared levels from what went in");

      // The picture moves three samples a frame, so a frame decoded as one of its neighbours — a
      // vector read from the wrong place, a macroblock address counted wrong — is further from its own
      // source than from that neighbour's, and no bound loose enough to allow the quantiser catches it.
      for (var other = 0; other < frames; ++other)
        if (other != index)
          Assert.That(error, Is.LessThan(_MeanSquaredError(sources[other].PixelData, decoded[index])),
            $"frame {index} came back closer to frame {other} than to itself");
    }
  }

  [Test]
  [Category("Unit")]
  public void APictureThatDoesNotMoveIsNothingButHeaders() {
    var encoder = H261VideoEncoder.Create(_Stream(176, 144));
    var flat = _Flat(176, 144, luminance: 99, chrominance: 128);

    encoder.TryEncode(flat, 0, out var first);
    encoder.TryEncode(flat, 1, out var second);

    Assert.Multiple(() => {
      Assert.That(second.Data.Length, Is.LessThan(first.Data.Length));

      // H.261 has no "coded with nothing" macroblock and no picture-level skip either: what is left of
      // a picture nothing changed in is its own header — the twenty-bit start code, five bits of
      // temporal reference, six of PTYPE and the PEI bit — and one header for each of the three groups
      // a QCIF picture holds, each sixteen bits of start code, four of group number, five of GQUANT and
      // the GEI bit. One hundred and ten bits, padded out to fourteen bytes.
      Assert.That(second.Data.Length, Is.EqualTo(14));
    });
  }

  [Test]
  [Category("Unit")]
  public void AFlatPictureComesBackExactly() {
    // A flat block's transform is its direct current term and nothing else, and the intra term's own
    // step of eight divides it without remainder — so this is the one picture H.261 codes exactly, and
    // anything at all wrong with the scan, the quantiser or the transform shows here first.
    var encoder = H261VideoEncoder.Create(_Stream(176, 144));
    var flat = _Flat(176, 144, luminance: 99, chrominance: 128);

    Assert.That(encoder.TryEncode(flat, 0, out var packet), Is.True);

    var planes = _DecodePlanes([packet], 176, 144).Single();
    Assert.That(planes.Distinct().OrderBy(v => v).ToArray(), Is.EqualTo(new byte[] { 99, 128 }));
  }

  [Test]
  [Category("Unit")]
  public void APictureAlreadyInFourTwoZeroCrossesWithNoColourConversionAtAll() {
    // The encoder takes the planes of a 4:2:0 picture as they stand. Chrominance at 128 and a
    // luminance that divides the intra step exactly therefore reaches the decoder unchanged, which no
    // path through a colour matrix could promise.
    var encoder = H261VideoEncoder.Create(_Stream(176, 144));
    Assert.That(encoder.TryEncode(_Flat(176, 144, luminance: 200, chrominance: 128), 0, out var packet), Is.True);

    var planes = _DecodePlanes([packet], 176, 144).Single();
    Assert.That(planes.Take(176 * 144).Distinct().ToArray(), Is.EqualTo(new byte[] { 200 }));
  }

  // ============================================================================================
  // The stream it describes, and the registry
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheEncoderDescribesAnH261Stream() {
    var stream = H261VideoEncoder.Create(_Stream(352, 288)).DescribeStream();

    Assert.Multiple(() => {
      Assert.That(stream.Codec, Is.EqualTo(CodecTag.FromCharacters("H261")));
      Assert.That(stream.Kind, Is.EqualTo(MediaStreamKind.Video));
      Assert.That(stream.Width, Is.EqualTo(352));
      Assert.That(stream.Height, Is.EqualTo(288));
      Assert.That(stream.CodecPrivateData.Length, Is.GreaterThan(0));
    });
  }

  [Test]
  [Category("Unit")]
  public void WhatTheEncoderDescribesIsWhatTheDecoderAccepts()
    => Assert.That(H261VideoDecoder.Accepts(H261VideoEncoder.Create(_Stream(176, 144)).DescribeStream()), Is.True);

  [Test]
  [Category("Unit")]
  public void APacketIsOnePictureTheDecoderHandsBackWhole() {
    // The whole way round through the two public halves rather than through the picture layer: one
    // packet in, one picture out, at the size the stream states and in the colour the decoder converts
    // to. A packet is padded to a whole number of bytes, and the decoder's start-code walk has to reach
    // the end of it without finding a second picture in the padding.
    var encoder = H261VideoEncoder.Create(_Stream(176, 144));
    var decoder = H261VideoDecoder.Create(encoder.DescribeStream());

    for (var index = 0; index < 4; ++index) {
      Assert.That(encoder.TryEncode(_Picture(176, 144, index), index, out var packet), Is.True);
      Assert.That(decoder.TryDecode(packet, out var frame), Is.True, $"frame {index}");

      Assert.Multiple(() => {
        Assert.That(frame.Width, Is.EqualTo(176));
        Assert.That(frame.Height, Is.EqualTo(144));
        Assert.That(frame.Format, Is.EqualTo(PixelFormat.Rgb24));
      });
    }
  }

  [Test]
  [Category("Unit")]
  public void TheRegistryReachesBothHalvesOfTheCodec() {
    var stream = _Stream(176, 144);

    Assert.Multiple(() => {
      Assert.That(VideoFormatRegistry.CanDecode(stream), Is.True);
      Assert.That(VideoFormatRegistry.CanEncode(stream), Is.True);
      Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<H261VideoDecoder>());
      Assert.That(VideoFormatRegistry.CreateEncoder(stream), Is.InstanceOf<H261VideoEncoder>());
    });
  }

  [Test]
  [Category("Unit")]
  public void NothingIsHeldBackAtTheEnd()
    => Assert.That(H261VideoEncoder.Create(_Stream(176, 144)).Flush(), Is.Empty);

  // ============================================================================================
  // Fixtures
  // ============================================================================================

  private static MediaStreamInfo _Stream(int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("H261"),
    Width = width,
    Height = height,
  };

  /// <summary>A 4:2:0 picture of one luminance and one chrominance value throughout.</summary>
  private static RawImage _Flat(int width, int height, byte luminance, byte chrominance) {
    var planes = new byte[width * height * 3 / 2];
    planes.AsSpan(0, width * height).Fill(luminance);
    planes.AsSpan(width * height).Fill(chrominance);

    return new() { Width = width, Height = height, Format = PixelFormat.Yuv420P8, PixelData = planes };
  }

  /// <summary>
  /// A 4:2:0 picture of a textured background with a bright block moving three samples a frame across
  /// it, which is enough motion for one frame to be plainly a different picture from its neighbours and
  /// little enough for a whole-pixel vector to track it.
  /// </summary>
  private static RawImage _Picture(int width, int height, int frame) {
    var chromaWidth = width / 2;
    var chromaHeight = height / 2;
    var planes = new byte[width * height + 2 * chromaWidth * chromaHeight];

    var left = 3 * frame % (width - 32);
    var top = 2 * frame % (height - 32);

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var inside = x >= left && x < left + 32 && y >= top && y < top + 32;
        planes[y * width + x] = (byte)(inside ? 210 : 40 + ((x >> 3) * 11 + (y >> 3) * 7) % 120);
      }

    for (var y = 0; y < chromaHeight; ++y)
      for (var x = 0; x < chromaWidth; ++x) {
        var inside = 2 * x >= left && 2 * x < left + 32 && 2 * y >= top && 2 * y < top + 32;
        planes[width * height + y * chromaWidth + x] = (byte)(inside ? 96 : 128);
        planes[width * height + chromaWidth * chromaHeight + y * chromaWidth + x] = (byte)(inside ? 160 : 128);
      }

    return new() { Width = width, Height = height, Format = PixelFormat.Yuv420P8, PixelData = planes };
  }

  /// <summary>
  /// The decoded 4:2:0 planes of every packet, taken from the picture buffer rather than from the
  /// colour-converted picture the decoder hands out.
  /// </summary>
  /// <remarks>
  /// Comparing on the planes and not on RGB is deliberate: a comparison after conversion compares two
  /// chrominance upsamplers and a colour matrix as much as it compares two codecs, and the samples that
  /// were actually coded are the planes.
  /// </remarks>
  private static List<byte[]> _DecodePlanes(IReadOnlyList<CodedPacket> packets, int width, int height) {
    var pictures = new List<byte[]>();
    H263Frame? reference = null;

    foreach (var packet in packets) {
      var reader = new H263BitReader(packet.Data.Span);
      Assert.That(reader.ReadBits(H261PictureHeader.StartCodeLength), Is.EqualTo(H261PictureHeader.StartCode),
        "every packet is one picture and starts with its start code");

      var header = H261PictureHeader.Parse(ref reader);
      Assert.Multiple(() => {
        Assert.That(header.Width, Is.EqualTo(width));
        Assert.That(header.Height, Is.EqualTo(height));
      });

      var decoder = H261PictureDecoder.BeginPicture(header, reference);
      decoder.DecodePicture(ref reader);
      reference = decoder.Target;

      var chromaSamples = width * height / 4;
      var planes = new byte[width * height + 2 * chromaSamples];
      for (var y = 0; y < height; ++y)
        Array.Copy(decoder.Target.Luma, y * decoder.Target.LumaWidth, planes, y * width, width);

      for (var y = 0; y < height / 2; ++y) {
        Array.Copy(decoder.Target.Cb, y * decoder.Target.ChromaWidth, planes, width * height + y * width / 2, width / 2);
        Array.Copy(
          decoder.Target.Cr, y * decoder.Target.ChromaWidth,
          planes, width * height + chromaSamples + y * width / 2, width / 2);
      }

      pictures.Add(planes);
    }

    return pictures;
  }

  private static double _MeanSquaredError(byte[] left, byte[] right) {
    var total = 0d;
    for (var i = 0; i < left.Length; ++i) {
      var difference = left[i] - right[i];
      total += (double)difference * difference;
    }

    return total / left.Length;
  }
}
