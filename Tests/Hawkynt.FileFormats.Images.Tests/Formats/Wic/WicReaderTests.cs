using System;
using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Images;

namespace FileFormat.Wic.Tests;

[TestFixture]
public sealed class WicReaderTests {

  private static readonly string[] _Fixtures = [
    "synth_33x17_rgb_t1_g101_q8",
    "synth_33x17_rgb_t0_g100_q8",
    "synth_33x17_grey_t2_g101_q8",
    "synth_33x17_grey_t1_g100_q1",
  ];

  [TestCaseSource(nameof(_Fixtures))]
  [Category("Integration")]
  public void Fixture_DecodesToTheOraclesPixels(string name) {
    var file = WicReader.FromSpan(WicTestSupport.Fixture(name + ".wic"));
    var oraclePath = WicTestSupport.FixturePath(name + ".ppm");
    var oracle = WicTestSupport.ReadPnm(File.ReadAllBytes(File.Exists(oraclePath) ? oraclePath : Path.ChangeExtension(oraclePath, ".pgm")));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(oracle.Width));
      Assert.That(file.Height, Is.EqualTo(oracle.Height));
      Assert.That(file.IsGrayscale, Is.EqualTo(!oracle.Colour));
      Assert.That(file.PixelData, Is.EqualTo(oracle.Pixels));
    });
  }

  [Test]
  [Category("Unit")]
  public void HeaderFields_AreReadFromBothGenerations() {
    var newer = WicReader.FromSpan(WicTestSupport.Fixture("synth_33x17_rgb_t1_g101_q8.wic"));
    var older = WicReader.FromSpan(WicTestSupport.Fixture("synth_33x17_rgb_t0_g100_q8.wic"));
    var haar = WicReader.FromSpan(WicTestSupport.Fixture("synth_33x17_grey_t2_g101_q8.wic"));

    Assert.Multiple(() => {
      Assert.That(newer.Generation, Is.EqualTo(WicGeneration.Version101));
      Assert.That(newer.Transform, Is.EqualTo(WicTransform.Biorthogonal97));
      Assert.That(older.Generation, Is.EqualTo(WicGeneration.Version100));
      Assert.That(older.Transform, Is.EqualTo(WicTransform.Daubechies4));
      Assert.That(haar.Transform, Is.EqualTo(WicTransform.Haar));
      Assert.That(newer.ChromaBudgetScale, Is.EqualTo(25));
      Assert.That(newer.Dpi, Is.Zero);
    });
  }

  [Test]
  [Category("Unit")]
  public void BothMagicForms_AreDetectedAsWic() {
    Assert.Multiple(() => {
      Assert.That(FormatRegistry.DetectFromBytes(WicTestSupport.Fixture("synth_33x17_rgb_t1_g101_q8.wic")), Is.EqualTo(ImageFormat.Wic));
      Assert.That(FormatRegistry.DetectFromBytes(WicTestSupport.Fixture("synth_33x17_rgb_t0_g100_q8.wic")), Is.EqualTo(ImageFormat.Wic));
      Assert.That(FormatRegistry.DetectFromExtension(".wic"), Is.EqualTo(ImageFormat.Wic));
    });
  }

  [Test]
  [Category("Unit")]
  public void RegistryRead_DecodesAFixture() {
    var raw = FormatRegistry.Read(WicTestSupport.Fixture("synth_33x17_rgb_t1_g101_q8.wic"));
    Assert.That(raw, Is.Not.Null);
    Assert.That(raw!.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That((raw.Width, raw.Height), Is.EqualTo((33, 17)));
  }

  [TestCase("synth_33x17_rgb_t1_g101_q8")]
  [TestCase("synth_33x17_grey_t1_g100_q1")]
  [Category("Unit")]
  public void ReadImageInfo_AgreesWithAFullDecode(string name) {
    var data = WicTestSupport.Fixture(name + ".wic");
    var info = WicFile.ReadImageInfo(data.AsSpan(0, 32));
    var file = WicReader.FromSpan(data);
    var raw = WicFile.ToRawImage(file);

    Assert.That(info, Is.Not.Null);
    Assert.Multiple(() => {
      Assert.That(info!.Value.Width, Is.EqualTo(raw.Width));
      Assert.That(info.Value.Height, Is.EqualTo(raw.Height));
      Assert.That(info.Value.BitsPerPixel, Is.EqualTo(raw.Format == PixelFormat.Gray8 ? 8 : 24));
      Assert.That(info.Value.ColorMode, Is.EqualTo(raw.Format.ToString()));
      Assert.That(info.Value.Compression, Does.Contain(file.Transform.ToString()));
      Assert.That(info.Value.FrameCount, Is.EqualTo(1));
    });
  }

  [Test]
  [Category("Unit")]
  public void ForeignPictures_AreRefused([Values("Png", "Bmp", "Jpeg")] string foreign) {
    var picture = new RawImage { Width = 8, Height = 8, Format = PixelFormat.Rgb24, PixelData = new byte[8 * 8 * 3] };
    var bytes = FormatRegistry.Write(picture, Enum.Parse<ImageFormat>(foreign));
    Assert.That(bytes, Is.Not.Null);
    Assert.Multiple(() => {
      Assert.Throws<InvalidDataException>(() => WicReader.FromSpan(bytes));
      Assert.That(WicFile.ReadImageInfo(bytes), Is.Null);
      Assert.That(FormatRegistry.DetectFromBytes(bytes!), Is.Not.EqualTo(ImageFormat.Wic));
    });
  }

  [Test]
  [Category("Unit")]
  public void TruncatedStream_IsRefused() {
    var data = WicTestSupport.Fixture("synth_33x17_rgb_t1_g101_q8.wic");
    Assert.Multiple(() => {
      Assert.Throws<InvalidDataException>(() => WicReader.FromSpan(data.AsSpan(0, data.Length / 2)));
      Assert.Throws<InvalidDataException>(() => WicReader.FromSpan(data.AsSpan(0, 20)));
      Assert.Throws<InvalidDataException>(() => WicReader.FromSpan(data.AsSpan(0, 32)));
    });
  }

  [Test]
  [Category("Unit")]
  public void UnknownEscapeCode_IsRefused() {
    var data = WicTestSupport.Fixture("synth_33x17_rgb_t1_g101_q8.wic");
    data[40] = 0xFF;
    data[41] = 0x07;
    Assert.Throws<InvalidDataException>(() => WicReader.FromSpan(data));
  }

  [Test]
  [Category("Unit")]
  public void OversizedMatrix_IsRefusedBeforeAllocating() {
    var data = WicTestSupport.Fixture("synth_33x17_rgb_t1_g101_q8.wic");
    data[18] = 0xF0; data[19] = 0xFF;
    data[20] = 0xF0; data[21] = 0xFF;
    Assert.Throws<InvalidDataException>(() => WicReader.FromSpan(data));
  }

  [Test]
  [Category("Unit")]
  public void DeepGreyColourModel_IsRefusedByName() {
    var data = WicTestSupport.Fixture("synth_33x17_grey_t1_g100_q1.wic");
    data[16] = 1;
    var exception = Assert.Throws<NotSupportedException>(() => WicReader.FromSpan(data));
    Assert.That(exception!.Message, Does.Contain("colour model 1"));
  }

  [Test]
  [Category("Unit")]
  public void LevelCountDisagreeingWithTheMatrix_IsRefused() {
    var data = WicTestSupport.Fixture("synth_33x17_grey_t1_g100_q1.wic");
    ++data[22];
    Assert.Throws<InvalidDataException>(() => WicReader.FromSpan(data));
  }

  [Test]
  [Category("Unit")]
  public void MatrixSmallerThanThePicture_IsRefused() {
    var data = WicTestSupport.Fixture("synth_33x17_grey_t1_g100_q1.wic");
    data[6] = 0x00; data[7] = 0x01;
    Assert.Throws<InvalidDataException>(() => WicReader.FromSpan(data));
  }

  [Test]
  [Category("Unit")]
  public void UndefinedTransform_IsRefused() {
    var data = WicTestSupport.Fixture("synth_33x17_grey_t1_g100_q1.wic");
    data[17] = 3;
    Assert.Throws<InvalidDataException>(() => WicReader.FromSpan(data));
  }
}
