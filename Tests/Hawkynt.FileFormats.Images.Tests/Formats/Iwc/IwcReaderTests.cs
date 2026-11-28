using System;
using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Images;

namespace FileFormat.Iwc.Tests;

[TestFixture]
public sealed class IwcReaderTests {

  [TestCase("synth_37x23")]
  [TestCase("checker_16x12_l1")]
  [TestCase("tiny_2x2")]
  [Category("Integration")]
  public void Fixture_DecodesToTheAppletsPixels(string name) {
    var file = IwcReader.FromSpan(IwcTestSupport.Fixture(name + ".iwc"));
    var applet = IwcTestSupport.ReadPpm(IwcTestSupport.Fixture(name + ".applet.ppm"));

    Assert.Multiple(() => {
      Assert.That((file.Width, file.Height), Is.EqualTo((applet.Width, applet.Height)));
      Assert.That(file.PixelData, Is.EqualTo(applet.Pixels));
    });
  }

  [Test]
  [Category("Unit")]
  public void Header_FollowsTheRecoveredLayoutAndAccountsForEveryByte() {
    var data = IwcTestSupport.Fixture("synth_37x23.iwc");
    var header = IwcHeader.Parse(data);

    Assert.Multiple(() => {
      Assert.That(header.Version, Is.EqualTo(1));
      Assert.That((header.Width, header.Height), Is.EqualTo((37, 23)));
      Assert.That(header.Levels, Is.EqualTo(4));
      Assert.That(header.LevelScales, Has.Length.EqualTo(header.Levels));
      Assert.That(header.ApproximationScales, Has.Length.EqualTo(3));
      Assert.That(header.ApproximationMinima, Has.Length.EqualTo(3));
      Assert.That(IwcHeader.SizeFor(header.Levels), Is.EqualTo(30 + 9 * 4 * header.Levels + 4));
      Assert.That(IwcHeader.SizeFor(header.Levels) + header.PayloadLength, Is.EqualTo(data.Length));
      Assert.That(BitConverter.ToInt32(data, IwcHeader.SizeFor(header.Levels) - 4), Is.EqualTo(header.PayloadLength));
    });
  }

  [Test]
  [Category("Unit")]
  public void Geometry_CountsTheCoefficientsOfEveryBand() {
    // 37x23 → nodes 37x23, 18x11, 9x5, 4x2; the last approximation is 2x1.
    var expected = 3 * (
      19 * 11 + 18 * 12 + 19 * 12
      + 9 * 5 + 9 * 6 + 9 * 6
      + 5 * 2 + 4 * 3 + 5 * 3
      + 2 * 1 + 2 * 1 + 2 * 1
      + 2 * 1);
    Assert.That(IwcCodec.SymbolCount(37, 23, 4), Is.EqualTo(expected));
  }

  [Test]
  [Category("Unit")]
  public void SignatureAndExtension_DetectIwc() {
    Assert.Multiple(() => {
      Assert.That(FormatRegistry.DetectFromBytes(IwcTestSupport.Fixture("synth_37x23.iwc")), Is.EqualTo(ImageFormat.Iwc));
      Assert.That(FormatRegistry.DetectFromExtension(".iwc"), Is.EqualTo(ImageFormat.Iwc));
    });
  }

  [Test]
  [Category("Unit")]
  public void ReadImageInfo_AgreesWithAFullDecode() {
    var data = IwcTestSupport.Fixture("checker_16x12_l1.iwc");
    var info = IwcFile.ReadImageInfo(data.AsSpan(0, 12));
    var raw = IwcFile.ToRawImage(IwcReader.FromSpan(data));

    Assert.That(info, Is.Not.Null);
    Assert.Multiple(() => {
      Assert.That((info!.Value.Width, info.Value.Height), Is.EqualTo((raw.Width, raw.Height)));
      Assert.That(info.Value.BitsPerPixel, Is.EqualTo(24));
      Assert.That(info.Value.ColorMode, Is.EqualTo(raw.Format.ToString()));
      Assert.That(info.Value.Compression, Does.Contain("1 levels"));
      Assert.That(info.Value.FrameCount, Is.EqualTo(1));
    });
  }

  [Test]
  [Category("Unit")]
  public void ForeignPictures_AreRefused([Values("Png", "Bmp", "Jpeg")] string foreign) {
    var picture = new RawImage { Width = 8, Height = 8, Format = PixelFormat.Rgb24, PixelData = new byte[8 * 8 * 3] };
    var bytes = FormatRegistry.Write(picture, Enum.Parse<ImageFormat>(foreign))!;
    Assert.Multiple(() => {
      Assert.Throws<InvalidDataException>(() => IwcReader.FromSpan(bytes));
      Assert.That(IwcFile.ReadImageInfo(bytes), Is.Null);
      Assert.That(FormatRegistry.DetectFromBytes(bytes), Is.Not.EqualTo(ImageFormat.Iwc));
    });
  }

  [Test]
  [Category("Unit")]
  public void TruncatedFile_IsRefused() {
    var data = IwcTestSupport.Fixture("synth_37x23.iwc");
    Assert.Multiple(() => {
      Assert.Throws<InvalidDataException>(() => IwcReader.FromSpan(data.AsSpan(0, data.Length - 1)));
      Assert.Throws<InvalidDataException>(() => IwcReader.FromSpan(data.AsSpan(0, 100)));
      Assert.Throws<InvalidDataException>(() => IwcReader.FromSpan(data.AsSpan(0, 20)));
    });
  }

  [Test]
  [Category("Unit")]
  public void PayloadTooShortForItsCoefficients_IsRefused() {
    var data = IwcTestSupport.Fixture("synth_37x23.iwc");
    var countOffset = IwcHeader.SizeFor(4) - 4;
    var shortened = data.AsSpan(0, data.Length - 200).ToArray();
    BitConverter.GetBytes(BitConverter.ToInt32(shortened, countOffset) - 200).CopyTo(shortened, countOffset);
    Assert.Throws<InvalidDataException>(() => IwcReader.FromSpan(shortened));
  }

  [Test]
  [Category("Unit")]
  public void OversizedPicture_IsRefusedBeforeAllocating() {
    var data = IwcTestSupport.Fixture("synth_37x23.iwc");
    data[4] = 0xFF; data[5] = 0x7F;
    data[6] = 0xFF; data[7] = 0x7F;
    Assert.Throws<InvalidDataException>(() => IwcReader.FromSpan(data));
  }

  [Test]
  [Category("Unit")]
  public void LevelsTheAppletCannotSynthesise_AreRefused() {
    var data = IwcTestSupport.Fixture("tiny_2x2.iwc");
    data[8] = 2;
    Assert.Throws<InvalidDataException>(() => IwcReader.FromSpan(data));
    data[8] = 0;
    Assert.Throws<InvalidDataException>(() => IwcReader.FromSpan(data));
  }

  [Test]
  [Category("Unit")]
  public void UnknownVersion_IsRefusedByName() {
    var data = IwcTestSupport.Fixture("tiny_2x2.iwc");
    data[3] = 2;
    var exception = Assert.Throws<NotSupportedException>(() => IwcReader.FromSpan(data));
    Assert.That(exception!.Message, Does.Contain("version 2"));
  }
}
