using System;
using FileFormat.Core;
using Hawkynt.FileFormats.Images;

namespace FileFormat.LuraWave.Tests;

[TestFixture]
public sealed class LuraWaveWriterTests {

  private static RawImage _Noise(int width, int height, PixelFormat format, int seed) {
    var bytesPerPixel = format switch { PixelFormat.Gray8 => 1, PixelFormat.Gray16 => 2, PixelFormat.Rgba32 => 4, _ => 3 };
    var pixels = new byte[width * height * bytesPerPixel];
    new Random(seed).NextBytes(pixels);
    return new() { Width = width, Height = height, Format = format, PixelData = pixels };
  }

  private static RawImage _RoundTrip(RawImage image)
    => LuraWaveFile.ToRawImage(LuraWaveReader.FromBytes(LuraWaveWriter.ToBytes(LuraWaveFile.FromRawImage(image))));

  [TestCase(PixelFormat.Gray8)]
  [TestCase(PixelFormat.Gray16)]
  [TestCase(PixelFormat.Rgb24)]
  [TestCase(PixelFormat.Rgba32)]
  [Category("Unit")]
  public void RoundTrip_IsLossless(PixelFormat format) {
    var image = _Noise(70, 53, format, 1);
    var back = _RoundTrip(image);
    Assert.Multiple(() => {
      Assert.That(back.Format, Is.EqualTo(format));
      Assert.That(back.PixelData, Is.EqualTo(image.PixelData));
    });
  }

  [TestCase(1, 1)]
  [TestCase(1, 9)]
  [TestCase(9, 1)]
  [TestCase(2, 3)]
  [TestCase(3, 5)]
  [TestCase(7, 7)]
  [TestCase(17, 4)]
  [TestCase(65, 33)]
  [TestCase(130, 67)]
  [Category("Unit")]
  public void RoundTrip_EdgeGeometry_IsLossless(int width, int height) {
    var image = _Noise(width, height, PixelFormat.Rgb24, width * 1000 + height);
    Assert.That(_RoundTrip(image).PixelData, Is.EqualTo(image.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_FlatPicture_IsLossless() {
    var image = new RawImage { Width = 40, Height = 30, Format = PixelFormat.Gray8, PixelData = new byte[40 * 30] };
    Array.Fill(image.PixelData, (byte)200);
    Assert.That(_RoundTrip(image).PixelData, Is.EqualTo(image.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_ExtremeSixteenBitValues_AreLossless() {
    var image = _Noise(33, 21, PixelFormat.Gray16, 9);
    for (var i = 0; i < image.PixelData.Length; i += 4) {
      image.PixelData[i] = 0xFF;
      image.PixelData[i + 1] = 0xFF;
    }

    Assert.That(_RoundTrip(image).PixelData, Is.EqualTo(image.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void Alpha_IsKept_AsFourRawChannels() {
    var image = _Noise(12, 10, PixelFormat.Rgba32, 3);
    var file = LuraWaveFile.FromRawImage(image);
    var back = _RoundTrip(image);
    Assert.Multiple(() => {
      Assert.That(file.Mode, Is.EqualTo(LuraWaveColourMode.RawFourChannel));
      Assert.That(back.Format, Is.EqualTo(PixelFormat.Rgba32));
      Assert.That(back.PixelData, Is.EqualTo(image.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void OtherAlphaLayouts_AreKeptAsRgba() {
    var image = _Noise(9, 7, PixelFormat.Rgba32, 4);
    var bgra = image.EnsureFormat(PixelFormat.Bgra32);
    var back = _RoundTrip(bgra);
    Assert.Multiple(() => {
      Assert.That(back.Format, Is.EqualTo(PixelFormat.Rgba32));
      Assert.That(back.PixelData, Is.EqualTo(image.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void Resolution_SurvivesReadWriteRead() {
    var header = LuraWaveHeader.Parse(LuraWaveFixtures.Bytes("writer-rgb.lwf")) with {
      HorizontalDpiFixed = 300 << 16,
      VerticalDpiFixed = (150 << 16) + 0x8000,
    };
    var file = LuraWaveReader.FromBytes(LuraWaveFixtures.Bytes("writer-rgb.lwf")) with { Header = header };
    var again = LuraWaveReader.FromBytes(LuraWaveWriter.ToBytes(file));
    Assert.Multiple(() => {
      Assert.That(again.Header!.HorizontalDpiFixed, Is.EqualTo(300 << 16));
      Assert.That(again.Header.VerticalDpiFixed, Is.EqualTo((150 << 16) + 0x8000));
      Assert.That(again.PixelData, Is.EqualTo(file.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void AFileReadInAnotherColourMode_IsRewrittenWithTheSamePicture() {
    var original = LuraWaveReader.FromBytes(LuraWaveFixtures.Bytes("mode08-polynomial.lwf"));
    var again = LuraWaveReader.FromBytes(LuraWaveWriter.ToBytes(original));
    Assert.Multiple(() => {
      Assert.That(again.Mode, Is.EqualTo(LuraWaveColourMode.ReversibleColour));
      Assert.That(again.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void SignedWideGrey_IsRewrittenAsWideGreyWithTheSamePicture() {
    var original = LuraWaveReader.FromBytes(LuraWaveFixtures.Bytes("mode22-signedgrey.lwf"));
    var again = LuraWaveReader.FromBytes(LuraWaveWriter.ToBytes(original));
    Assert.Multiple(() => {
      Assert.That(again.Mode, Is.EqualTo(LuraWaveColourMode.WideGrey));
      Assert.That(again.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void MismatchedPixelBuffer_IsRefused() {
    var file = new LuraWaveFile { Width = 4, Height = 4, Mode = LuraWaveColourMode.Grey, PixelData = new byte[15] };
    Assert.Throws<ArgumentException>(() => LuraWaveWriter.ToBytes(file));
  }

  [Test]
  [Category("Unit")]
  public void IsWritableThroughTheRegistry() {
    var image = _Noise(20, 14, PixelFormat.Rgb24, 5);
    var bytes = FormatRegistry.Write(image, ImageFormat.LuraWave)!;
    Assert.Multiple(() => {
      Assert.That(FormatRegistry.DetectFromBytes(bytes), Is.EqualTo(ImageFormat.LuraWave));
      Assert.That(FormatRegistry.Read(bytes)!.EnsureFormat(PixelFormat.Rgb24).PixelData, Is.EqualTo(image.PixelData));
    });
  }
}
