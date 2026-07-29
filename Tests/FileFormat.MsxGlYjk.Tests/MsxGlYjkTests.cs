using System;
using System.IO;
using FileFormat.Core;
using FileFormat.MsxGlYjk;

namespace FileFormat.MsxGlYjk.Tests;

[TestFixture]
public sealed class MsxGlYjkTests {

  private const int _WIDTH = 64;
  private const int _HEIGHT = 48;

  private static RawImage _Sample(int width = _WIDTH, int height = _HEIGHT) {
    var data = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var o = (y * width + x) * 3;
      data[o] = (byte)(x * 255 / Math.Max(1, width - 1));
      data[o + 1] = (byte)(y * 255 / Math.Max(1, height - 1));
      data[o + 2] = (byte)((x / 4 + y / 4) % 2 == 0 ? 200 : 40);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = data };
  }

  [Test]
  public void Written_IsAFourByteHeaderThenOneBytePerPixel() {
    var bytes = MsxGlYjkWriter.ToBytes(MsxGlYjkFile.FromRawImage(_Sample()));

    Assert.Multiple(() => {
      Assert.That(bytes, Has.Length.EqualTo(MsxGlYjkFile.HeaderSize + _WIDTH * _HEIGHT));
      Assert.That(bytes[0] | (bytes[1] << 8), Is.EqualTo(_WIDTH));
      Assert.That(bytes[2] | (bytes[3] << 8), Is.EqualTo(_HEIGHT));
    });
  }

  [Test]
  public void RoundTrip_PreservesDimensionsAndPixels() {
    var file = MsxGlYjkFile.FromRawImage(_Sample());
    var reread = MsxGlYjkReader.FromBytes(MsxGlYjkWriter.ToBytes(file));

    Assert.Multiple(() => {
      Assert.That(reread.Width, Is.EqualTo(_WIDTH));
      Assert.That(reread.Height, Is.EqualTo(_HEIGHT));
      Assert.That(reread.PixelData, Is.EqualTo(file.PixelData));
    });
  }

  [Test]
  public void Screen12_UsesEveryLumaStep() {
    // Screen 12 has no palette escape, so odd luma values are legal and a gradient should reach some.
    var file = MsxGlYjkFile.FromRawImage(_Sample(), MsxGlYjkMode.Screen12);
    var odd = 0;
    foreach (var b in file.PixelData)
      if (((b >> 3) & 1) != 0)
        ++odd;

    Assert.That(odd, Is.GreaterThan(0));
  }

  [Test]
  public void Screen10_NeverEmitsAnOddLuma() {
    // An odd luma would be read back as a palette index, so the encoder must not produce one.
    var file = MsxGlYjkFile.FromRawImage(_Sample(), MsxGlYjkMode.Screen10);

    foreach (var b in file.PixelData)
      Assert.That((b >> 3) & 1, Is.Zero, "luma escape bit set on a Screen 10 pixel");
  }

  [Test]
  public void ModeFromExtension_FollowsTheTwoFamilies() {
    Assert.Multiple(() => {
      foreach (var e in new[] { ".gla", ".glb", ".sha", ".shb", ".GLA" })
        Assert.That(MsxGlYjkFile.ModeFromExtension(e), Is.EqualTo(MsxGlYjkMode.Screen10), e);

      foreach (var e in new[] { ".glc", ".gls", ".shc" })
        Assert.That(MsxGlYjkFile.ModeFromExtension(e), Is.EqualTo(MsxGlYjkMode.Screen12), e);
    });
  }

  [Test]
  public void Decoded_MatchesTheSourceCloselyEnoughForSharedChroma() {
    var source = _Sample();
    var decoded = MsxGlYjkFile.ToRawImage(MsxGlYjkFile.FromRawImage(source));

    long total = 0;
    for (var i = 0; i < source.PixelData.Length; ++i)
      total += Math.Abs(source.PixelData[i] - decoded.PixelData[i]);

    Assert.That(total / (double)source.PixelData.Length, Is.LessThan(24), "mean channel error");
  }

  [Test]
  public void Reader_RejectsALengthThatDisagreesWithTheHeader() {
    var bytes = MsxGlYjkWriter.ToBytes(MsxGlYjkFile.FromRawImage(_Sample()));

    Assert.Throws<InvalidDataException>(() => MsxGlYjkReader.FromBytes(bytes[..^1]));
  }

  [Test]
  public void Reader_RejectsAnImpossibleHeader() {
    Assert.Throws<InvalidDataException>(() => MsxGlYjkReader.FromBytes([0, 0, 0, 0]));
  }
}
