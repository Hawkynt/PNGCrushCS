using System;
using FileFormat.Core;

namespace FileFormat.LuraWave.Tests;

/// <summary>
/// Modes 5, 20 and 21, which LuraTech's C decoder returns only as raw channels: the channels held to that decoder, the
/// pictures drawn from them, and the writer.
/// </summary>
/// <remarks>
/// Every <c>luratech-m05/m20/m21-*.expected.pam</c> is what LuraTech's C decoder (see <see cref="LuraWaveOracleTests"/>)
/// returned for the <c>.lwf</c> of the same name through its raw output types 2, <c>0x23</c> and <c>0x24</c>; the streams
/// come from its encoder, fed synthetic gradients with noise. The <c>writer-*</c> streams are this package's; LuraTech's
/// decoder returned their source channels unchanged.
/// </remarks>
[TestFixture]
public sealed class LuraWaveRawModeTests {

  [TestCase("luratech-m05-s-l3-base", TestName = "Four raw channels, three levels, stripes")]
  [TestCase("luratech-m05-m-roi2", TestName = "Four raw channels, two interest regions, cut to 6000 bytes")]
  [TestCase("luratech-m20-s-l4", TestName = "Wide Lab, lossless")]
  [TestCase("luratech-m20-m-12bit-lossy", TestName = "Wide Lab, 12-bit maximum, cut to 2500 bytes")]
  [TestCase("luratech-m21-s-l3-base", TestName = "Wide YUVK, three levels, stripes")]
  [TestCase("luratech-m21-m-lossy-base", TestName = "Wide YUVK in stripes, cut to 5000 bytes")]
  [TestCase("luratech-m21-m-max40000-lossy", TestName = "Wide YUVK, maximum 40000, cut to 4000 bytes")]
  [Category("Integration")]
  public void ChannelsAreTheVendorDecoders(string name) {
    var image = LuraWaveReader.FromBytes(LuraWaveFixtures.Bytes(name + ".lwf"));
    var expected = LuraWaveFixtures.Pam(LuraWaveFixtures.Bytes(name + ".expected.pam"));
    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(expected.Width));
      Assert.That(image.Height, Is.EqualTo(expected.Height));
      Assert.That(image.Channels, Is.EqualTo(expected.Samples));
    });
  }

  [Test]
  [Category("Integration")]
  public void PasswordUnlocksTheFullQuality() {
    var data = LuraWaveFixtures.Bytes("luratech-m20-m-pw.lwf");
    Assert.Multiple(() => {
      Assert.That(LuraWaveReader.FromBytes(data, "pass+word").Channels, Is.EqualTo(LuraWaveFixtures.Pam(LuraWaveFixtures.Bytes("luratech-m20-m-pw.expected.pam")).Samples));
      Assert.That(LuraWaveReader.FromBytes(data).Channels, Is.EqualTo(LuraWaveFixtures.Pam(LuraWaveFixtures.Bytes("luratech-m20-m-pw.public.pam")).Samples));
    });
  }

  [Test]
  [Category("Unit")]
  public void FourRawChannels_AreRgba() {
    var image = LuraWaveReader.FromBytes(LuraWaveFixtures.Bytes("luratech-m05-s-l3-base.lwf"));
    var channels = image.Channels!;
    var expected = new byte[channels.Length];
    for (var i = 0; i < expected.Length; ++i)
      expected[i] = (byte)channels[i];

    Assert.Multiple(() => {
      Assert.That(image.PixelFormat, Is.EqualTo(PixelFormat.Rgba32));
      Assert.That(image.ChannelCount, Is.EqualTo(4));
      Assert.That(image.PixelData, Is.EqualTo(expected));
    });
  }

  [TestCase(65280, 32768, 32768, 255, 255, 255, TestName = "L* 100 is white")]
  [TestCase(0, 32768, 32768, 0, 0, 0, TestName = "L* 0 is black")]
  [TestCase(32640, 32768, 32768, 119, 119, 119, TestName = "L* 50 is sRGB grey 119")]
  // sRGB primaries as L*a*b* under D65 (53.23, 80.11, 67.22 and so on), in the ICC version 2 16-bit encoding.
  [TestCase(34750, 53276, 49977, 255, 0, 0, TestName = "sRGB red")]
  [TestCase(57275, 10705, 54064, 0, 255, 0, TestName = "sRGB green")]
  [TestCase(21087, 53043, 5158, 0, 0, 255, TestName = "sRGB blue")]
  [Category("Unit")]
  public void WideLab_IsCieLabUnderD65InTheIccVersion2Encoding(int l, int a, int b, int red, int green, int blue) {
    var output = new byte[3];
    LuraWaveColour.SrgbFromWideLab([(ushort)l, (ushort)a, (ushort)b], 0, 8, output, 0);
    Assert.That(output, Is.EqualTo(new[] { (byte)red, (byte)green, (byte)blue }).Using<byte>((x, y) => Math.Abs(x - y) <= 1 ? 0 : 1));
  }

  [TestCase(0xFFFF, 8, 230, 232, 236, TestName = "All channels full is paper white")]
  [TestCase(0x0FFF, 4, 230, 232, 236, TestName = "All channels full at 12 bits is paper white")]
  [TestCase(0, 8, 0, 0, 0, TestName = "All channels empty is black")]
  [Category("Unit")]
  public void WideFourChannel_IsDrawnByTheFourChannelQuadratic(int value, int shift, int red, int green, int blue) {
    var output = new byte[3];
    var sample = (ushort)value;
    LuraWaveColour.RgbFromWideFourChannel([sample, sample, sample, sample], 0, shift, output, 0);
    Assert.That(output, Is.EqualTo(new[] { (byte)red, (byte)green, (byte)blue }));
  }

  [Test]
  [Category("Unit")]
  public void WideFourChannel_MatchesTheEightBitQuadraticOnTheTopBits() {
    // Mode 7's quadratic on c >> 8 is what a mode 21 pixel is drawn with; check it against the 8-bit path, which
    // runs it after undoing the reversible colour transform (y = (c + 2m + y) >> 2 - 128, u = c - m, v = y - m).
    var random = new Random(21);
    for (var n = 0; n < 200; ++n) {
      int c = random.Next(256), m = random.Next(256), y = random.Next(256), k = random.Next(256);
      var wide = new byte[3];
      LuraWaveColour.RgbFromWideFourChannel([(ushort)(c << 8 | random.Next(256)), (ushort)(m << 8), (ushort)(y << 8 | 255), (ushort)(k << 8)], 0, 8, wide, 0);
      var narrow = new byte[3];
      LuraWaveColour.Rgb(LuraWaveColourMode.FourChannel, [((c + 2 * m + y) >> 2) - 128, c - m, y - m, k - 128], 0, 0, narrow, 0);
      Assert.That(wide, Is.EqualTo(narrow), $"CMYK {c} {m} {y} {k}");
    }
  }

  [TestCase("luratech-m05-s-l3-base", "writer-raw4.lwf", TestName = "Four raw channels")]
  [TestCase("luratech-m20-s-l4", "writer-lab16.lwf", TestName = "Wide Lab")]
  [TestCase("luratech-m21-s-l3-base", "writer-yuvk16.lwf", TestName = "Wide YUVK")]
  [Category("Unit")]
  public void ChannelsAreRewrittenInTheirOwnMode(string source, string written) {
    // LuraTech's C decoder returned these channels unchanged from the committed writer streams.
    var original = LuraWaveReader.FromBytes(LuraWaveFixtures.Bytes(source + ".lwf"));
    var bytes = LuraWaveWriter.ToBytes(original);
    var again = LuraWaveReader.FromBytes(bytes);
    Assert.Multiple(() => {
      Assert.That(bytes, Is.EqualTo(LuraWaveFixtures.Bytes(written)));
      Assert.That(again.Mode, Is.EqualTo(original.Mode));
      Assert.That(again.Channels, Is.EqualTo(original.Channels));
      Assert.That(again.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void ChannelsKeepTheirMaximum() {
    var original = LuraWaveReader.FromBytes(LuraWaveFixtures.Bytes("luratech-m20-m-12bit-lossy.lwf"));
    var again = LuraWaveReader.FromBytes(LuraWaveWriter.ToBytes(original));
    Assert.Multiple(() => {
      Assert.That(again.Header!.MaxValue, Is.EqualTo(4095));
      Assert.That(again.Channels, Is.EqualTo(original.Channels));
      Assert.That(again.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void MismatchedChannels_AreRefused() {
    var file = new LuraWaveFile { Width = 4, Height = 4, Mode = LuraWaveColourMode.WideLab, Channels = new ushort[47], PixelData = [] };
    Assert.Throws<ArgumentException>(() => LuraWaveWriter.ToBytes(file));
  }

  [Test]
  [Category("Unit")]
  public void OtherModes_HaveNoChannels()
    => Assert.That(LuraWaveReader.FromBytes(LuraWaveFixtures.Bytes("mode07-fourchannel.lwf")).Channels, Is.Null);
}
