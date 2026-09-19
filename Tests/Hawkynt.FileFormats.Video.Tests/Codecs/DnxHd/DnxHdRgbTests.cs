using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs.DnxHd.Tests;

[TestFixture]
public sealed class DnxHdRgbTests {

  private const string _DC_ZERO = "1010";
  private const string _DC_TEN_BITS = "1110";
  private const string _EOB = "1011";

  [Test]
  [Category("Unit")]
  public void Cid1256RgbMacroblockIsReturnedAsRgbRatherThanMisreadAsYcbcr() {
    var payload = new DnxHdTestStream()
      .Bits(1, 11).Bits(0, 1); // qsf 1, ACF 0 = RGB mode

    _DcTen(payload, 512); // R0 -> 576 after IDCT level adjustment
    _Flat(payload);       // R1
    _Flat(payload);       // G0 -> 512
    _Flat(payload);       // G1
    _DcTen(payload, 511); // B0 -> -512 correction -> 448 after adjustment
    _Flat(payload);       // B1
    _Flat(payload);       // R2
    _Flat(payload);       // R3
    _Flat(payload);       // G2
    _Flat(payload);       // G3
    _Flat(payload);       // B2
    _Flat(payload);       // B3

    var unit = DnxHdTestStream.Unit(new() {
      CompressionId = 1256,
      HeaderVersion = 2,
      Width = 16,
      Height = 16,
      BitDepthCode = 2,
      SubSampling = 2,
      Rgb = true,
    }, payload);

    var decoder = DnxHdVideoDecoder.Create(DnxHdTestStream.Stream(16, 16));
    Assert.That(decoder.TryDecode(new CodedPacket(0, unit), out var frame), Is.True);

    Assert.Multiple(() => {
      Assert.That(frame.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(frame.PixelData[0], Is.EqualTo(149), "R is Ch1 in RGB mode");
      Assert.That(frame.PixelData[1], Is.EqualTo(130), "G is Ch2 in RGB mode");
      Assert.That(frame.PixelData[2], Is.EqualTo(112), "B is Ch3 in RGB mode");
    });

    for (var at = 0; at < frame.PixelData.Length; at += 3) {
      Assert.That(frame.PixelData[at], Is.EqualTo(149));
      Assert.That(frame.PixelData[at + 1], Is.EqualTo(130));
      Assert.That(frame.PixelData[at + 2], Is.EqualTo(112));
    }
  }

  [Test]
  [Category("Unit")]
  public void Cid1256YcbcrMacroblockStillConvertsToRgb() {
    var payload = new DnxHdTestStream()
      .Bits(1, 11).Bits(1, 1); // qsf 1, ACF 1 = YCbCr mode

    _DcTen(payload, 512); // Y = 576
    _Flat(payload);
    _Flat(payload);       // Cb = 512
    _Flat(payload);
    _Flat(payload);       // Cr = 512
    _Flat(payload);
    _Flat(payload);
    _Flat(payload);
    _Flat(payload);
    _Flat(payload);
    _Flat(payload);
    _Flat(payload);

    var unit = DnxHdTestStream.Unit(new() {
      CompressionId = 1256,
      HeaderVersion = 2,
      Width = 16,
      Height = 16,
      BitDepthCode = 2,
      SubSampling = 2,
      Rgb = true,
    }, payload);

    var decoder = DnxHdVideoDecoder.Create(DnxHdTestStream.Stream(16, 16));
    Assert.That(decoder.TryDecode(new CodedPacket(0, unit), out var frame), Is.True);

    Assert.Multiple(() => {
      Assert.That(frame.PixelData[0], Is.EqualTo(149));
      Assert.That(frame.PixelData[1], Is.EqualTo(149));
      Assert.That(frame.PixelData[2], Is.EqualTo(149));
    });
  }

  [Test]
  [Category("Unit")]
  public void Cid1256AlternateColourTransformIsBt709EvenWhenClvNamesBt2020() {
    var payload = new DnxHdTestStream()
      .Bits(1, 11).Bits(1, 1); // qsf 1, ACF 1 = normative BT.709 alternate transform

    _DcTen(payload, 512); // Y = 576
    _Flat(payload);
    _DcTen(payload, 512); // Cb = 576
    _Flat(payload);
    _DcTen(payload, 511); // Cr = 448
    _Flat(payload);
    _Flat(payload);
    _Flat(payload);
    _Flat(payload);
    _Flat(payload);
    _Flat(payload);
    _Flat(payload);

    var unit = DnxHdTestStream.Unit(new() {
      CompressionId = 1256,
      HeaderVersion = 2,
      Width = 16,
      Height = 16,
      BitDepthCode = 2,
      SubSampling = 2,
      Rgb = true,
      ColorVolume = 1,
    }, payload);

    var decoder = DnxHdVideoDecoder.Create(DnxHdTestStream.Stream(16, 16));
    Assert.That(decoder.TryDecode(new CodedPacket(0, unit), out var frame), Is.True);

    Assert.Multiple(() => {
      Assert.That(frame.PixelData[0], Is.EqualTo(120));
      Assert.That(frame.PixelData[1], Is.EqualTo(154));
      Assert.That(frame.PixelData[2], Is.EqualTo(183));
    });
  }

  [Test]
  [Category("Unit")]
  public void AlternateColourFlagWithoutRgbFormatRulesIsRejected() {
    var payload = new DnxHdTestStream().Bits(1, 11).Bits(1, 1);
    for (var block = 0; block < 12; ++block)
      _Flat(payload);

    var unit = DnxHdTestStream.Unit(new() {
      CompressionId = 1256,
      HeaderVersion = 2,
      Width = 16,
      Height = 16,
      BitDepthCode = 2,
      SubSampling = 2,
      Rgb = false,
    }, payload);

    var decoder = DnxHdVideoDecoder.Create(DnxHdTestStream.Stream(16, 16));
    Assert.That(
      () => decoder.DecodePlanes(unit, out _),
      Throws.TypeOf<InvalidDataException>().With.Message.Contains("ACF"));
  }

  [Test]
  [Category("Unit")]
  public void DnxHrRgbMacroblocksRemainExplicitlyDeferred() {
    var payload = new DnxHdTestStream().Bits(1, 11).Bits(0, 1);
    for (var block = 0; block < 12; ++block)
      _Flat(payload);

    var unit = DnxHdTestStream.Unit(new() {
      CompressionId = 1270,
      HeaderVersion = 3,
      Width = 16,
      Height = 16,
      BitDepthCode = 2,
      SubSampling = 2,
      Rgb = true,
    }, payload);

    var decoder = DnxHdVideoDecoder.Create(DnxHdTestStream.Stream(16, 16, "AVdh"));
    Assert.That(
      () => decoder.DecodePlanes(unit, out _),
      Throws.TypeOf<NotSupportedException>().With.Message.Contains("DNxHR RGB"));
  }

  private static void _Flat(DnxHdTestStream payload)
    => payload.Code(_DC_ZERO).Code(_EOB);

  private static void _DcTen(DnxHdTestStream payload, int rho)
    => payload.Code(_DC_TEN_BITS).Bits(rho, 10).Code(_EOB);
}
