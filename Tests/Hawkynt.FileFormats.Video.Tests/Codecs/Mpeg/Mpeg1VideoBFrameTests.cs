using System;
using System.Collections.Generic;
using System.Linq;
using FileFormat.Core;

namespace FileFormat.Codecs.Mpeg.Tests;

[TestFixture]
public sealed class Mpeg1VideoBFrameTests {

  [Test]
  [Category("Unit")]
  public void FourDisplayFramesArePacketisedInMpegCodingOrder() {
    var stream = _Stream(32, 16);
    var encoder = Mpeg1VideoEncoder.Create(stream);
    var sources = new[] {
      _Solid(32, 16, 16),
      _Solid(32, 16, 96),
      _Solid(32, 16, 176),
      _Solid(32, 16, 240),
    };
    var packets = new List<CodedPacket>();

    for (var index = 0; index < sources.Length; ++index)
      if (encoder.TryEncode(sources[index], index, out var packet))
        packets.Add(packet);

    packets.AddRange(encoder.Flush());

    Assert.Multiple(() => {
      Assert.That(packets.Count, Is.EqualTo(4));
      Assert.That(packets.Select(_PictureCodingType).ToArray(), Is.EqualTo(new[] {
        MpegPictureDecoder.IntraCoded,
        MpegPictureDecoder.PredictiveCoded,
        MpegPictureDecoder.BidirectionallyCoded,
        MpegPictureDecoder.BidirectionallyCoded,
      }));
      Assert.That(packets.Select(static packet => packet.PresentationTimestamp).ToArray(),
        Is.EqualTo(new long?[] { 0, 3, 1, 2 }));
      Assert.That(packets.Select(static packet => packet.DecodeTimestamp).ToArray(),
        Is.EqualTo(new long?[] { 0, 1, 2, 3 }));
      Assert.That(packets.Select(static packet => packet.IsKeyFrame).ToArray(),
        Is.EqualTo(new[] { true, false, false, false }));
    });

    var decoder = Mpeg1VideoDecoder.Create(stream);
    var decoded = new List<RawImage>();
    foreach (var packet in packets)
      if (decoder.TryDecode(packet, out var frame))
        decoded.Add(frame);

    decoded.AddRange(decoder.Flush());
    Assert.That(decoded.Count, Is.EqualTo(sources.Length));
    for (var index = 0; index < sources.Length; ++index)
      Assert.That(_MeanAbsoluteError(sources[index], decoded[index]), Is.LessThan(8d),
        $"display picture {index} was not reconstructed in display order");
  }

  [Test]
  [Category("Unit")]
  public void BMacroblocksCanChooseBackwardForwardAndBidirectionalReferences() {
    var stream = _Stream(32, 16);
    var encoder = Mpeg1VideoEncoder.Create(stream);
    var frames = new[] {
      _Solid(32, 16, 16),
      _Solid(32, 16, 128),
      _Solid(32, 16, 16),
      _Solid(32, 16, 240),
    };
    var packets = new List<CodedPacket>();

    for (var index = 0; index < frames.Length; ++index)
      if (encoder.TryEncode(frames[index], index, out var packet))
        packets.Add(packet);

    packets.AddRange(encoder.Flush());
    var bPictures = packets.Where(static packet => _PictureCodingType(packet) == MpegPictureDecoder.BidirectionallyCoded).ToArray();
    Assert.That(bPictures.Length, Is.EqualTo(2));

    var firstType = _FirstBMacroblockType(bPictures[0]);
    var secondType = _FirstBMacroblockType(bPictures[1]);

    Assert.Multiple(() => {
      Assert.That(firstType & MpegVlcTables.TypeMotionForward, Is.Not.Zero,
        "the interpolated B picture should use its preceding anchor");
      Assert.That(firstType & MpegVlcTables.TypeMotionBackward, Is.Not.Zero,
        "the interpolated B picture should use its following anchor too");
      Assert.That(secondType & MpegVlcTables.TypeMotionForward, Is.Not.Zero,
        "the B picture matching the preceding anchor should choose forward prediction");
      Assert.That(secondType & MpegVlcTables.TypeMotionBackward, Is.Zero,
        "a strictly better forward prediction should not spend a backward vector");
    });
  }

  private static int _PictureCodingType(CodedPacket packet) {
    var data = packet.Data.Span;
    var picture = _FindStartCode(data, MpegStartCode.Picture);
    Assert.That(picture, Is.GreaterThanOrEqualTo(0), "packet has no MPEG picture start code");
    return (data[picture + 5] >> 3) & 0x07;
  }

  private static int _FirstBMacroblockType(CodedPacket packet) {
    var data = packet.Data.Span;
    var slice = _FindStartCode(data, MpegStartCode.FirstSlice);
    Assert.That(slice, Is.GreaterThanOrEqualTo(0), "B picture has no slice");

    var reader = new MpegBitReader(data[(slice + 4)..]);
    reader.Skip(5);
    while (reader.NextBits(1) == 1) {
      reader.Skip(1);
      reader.Skip(8);
    }

    reader.Skip(1);
    Assert.That(MpegVlcTables.MacroblockAddressIncrement.Read(ref reader), Is.EqualTo(1));
    return MpegVlcTables.BidirectionalMacroblockType.Read(ref reader);
  }

  private static int _FindStartCode(ReadOnlySpan<byte> data, byte code) {
    for (var index = 0; index + 3 < data.Length; ++index)
      if (data[index] == 0 && data[index + 1] == 0 && data[index + 2] == 1 && data[index + 3] == code)
        return index;

    return -1;
  }

  private static double _MeanAbsoluteError(RawImage expected, RawImage actual) {
    var total = 0L;
    for (var index = 0; index < expected.PixelData.Length; ++index)
      total += Math.Abs(expected.PixelData[index] - actual.PixelData[index]);

    return total / (double)expected.PixelData.Length;
  }

  private static MediaStreamInfo _Stream(int width, int height)
    => new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("MPG1"),
      Width = width,
      Height = height,
      TimeBase = new Rational(1, 25),
      FrameRate = new Rational(25, 1),
    };

  private static RawImage _Solid(int width, int height, byte value) {
    var data = new byte[width * height * 3];
    Array.Fill(data, value);
    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = data };
  }
}
