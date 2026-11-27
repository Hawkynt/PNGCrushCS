using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.LuraWave.Tests;

/// <summary>Decodes held to LuraTech's own decoder.</summary>
/// <remarks>
/// <para>
/// The oracle is <c>com.luratech.lwf.lwfDecoder</c> from <c>lwf_jsdk2.6.jar</c> (2002), the licensed decoder that
/// McMaster University's MBF ImageJ bundle redistributes, run on a JVM with the bundle's licence code. Every
/// <c>*.expected.ppm</c>/<c>*.expected.pgm</c> fixture is that decoder's output for the <c>.lwf</c> of the same name:
/// <c>decodeToMemory(pixels, -1, 1024, 0)</c> for colour (ARGB, alpha dropped), <c>decodeToMemoryGray8</c> for 8-bit
/// grey, and <c>decodeToMemoryGray16</c> for the wide grey modes. The decoder is not redistributable, so its output
/// is committed rather than run on CI.
/// </para>
/// <para>
/// The streams themselves were written with this package's encoder from <c>source.ppm</c> (a seeded ImageMagick
/// plasma with a drawn rectangle and line, 45x37 so every dimension needs padding), choosing every colour mode, both
/// stripe layouts, an interest region, the verbatim low-plane tail and a password-limited public quality. The
/// oracle decoding them is what makes the comparison independent of the encoder.
/// </para>
/// </remarks>
[TestFixture]
public sealed class LuraWaveOracleTests {

  [TestCase("mode01-tail", "pgm", TestName = "Grey, low planes sent verbatim")]
  [TestCase("mode02-colourdifference", "ppm", TestName = "Colour-difference mode")]
  [TestCase("mode04-rgb", "ppm", TestName = "Plain RGB mode")]
  [TestCase("mode06-region", "ppm", TestName = "Reversible colour with an interest region")]
  [TestCase("mode06-stripes", "ppm", TestName = "Reversible colour in 16-row stripes")]
  [TestCase("mode06-stripes-password", "ppm", TestName = "Stripes cut short by a password-limited public quality")]
  [TestCase("mode06-stripes-region-tail", "ppm", TestName = "Stripes, interest region and verbatim tail together")]
  [TestCase("mode07-fourchannel", "ppm", TestName = "Four-channel quadratic mode")]
  [TestCase("mode08-polynomial", "ppm", TestName = "Three-channel quadratic mode")]
  [TestCase("mode17-widegrey-12bit", "pgm", TestName = "Wide grey with a 12-bit maximum")]
  [TestCase("mode17-widegrey-nomaximum", "pgm", TestName = "Wide grey without a maximum chunk")]
  [TestCase("mode19-widecolour", "ppm", TestName = "Wide reversible colour with a 16-bit maximum")]
  [TestCase("mode19-widecolour-nomaximum", "ppm", TestName = "Wide reversible colour without a maximum chunk")]
  [TestCase("mode22-signedgrey", "pgm", TestName = "Signed wide grey with a 16-bit maximum")]
  [TestCase("mode22-signedgrey-nomaximum", "pgm", TestName = "Signed wide grey without a maximum chunk")]
  [Category("Integration")]
  public void DecodesExactlyAsTheVendorDecoderDoes(string name, string extension) {
    var image = LuraWaveReader.FromBytes(LuraWaveFixtures.Bytes(name + ".lwf"));
    var expected = LuraWaveFixtures.Pnm(LuraWaveFixtures.Bytes($"{name}.expected.{extension}"));

    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(expected.Width));
      Assert.That(image.Height, Is.EqualTo(expected.Height));
      Assert.That(image.PixelData, Is.EqualTo(expected.Pixels));
    });
  }

  [TestCase("writer-grey", "source.pgm", TestName = "8-bit grey")]
  [TestCase("writer-grey16", "source16.pgm", TestName = "16-bit grey")]
  [TestCase("writer-rgb", "source.ppm", TestName = "RGB")]
  [Category("Integration")]
  public void WhatTheWriterProducedDecodesToItsSource(string name, string source) {
    // The vendor decoder returned these sources unchanged from the committed streams; this checks ours agrees.
    var image = LuraWaveReader.FromBytes(LuraWaveFixtures.Bytes(name + ".lwf"));
    var expected = LuraWaveFixtures.Pnm(LuraWaveFixtures.Bytes(source));
    Assert.That(image.PixelData, Is.EqualTo(expected.Pixels));
  }

  [TestCase("writer-grey", "source.pgm", TestName = "8-bit grey")]
  [TestCase("writer-grey16", "source16.pgm", TestName = "16-bit grey")]
  [TestCase("writer-rgb", "source.ppm", TestName = "RGB")]
  [Category("Unit")]
  public void TheWriterStillProducesTheStreamTheVendorDecoderAccepted(string name, string source) {
    var picture = LuraWaveFixtures.Pnm(LuraWaveFixtures.Bytes(source));
    var format = picture.Channels == 3 ? PixelFormat.Rgb24 : picture.MaxValue > 255 ? PixelFormat.Gray16 : PixelFormat.Gray8;
    var file = LuraWaveFile.FromRawImage(new() { Width = picture.Width, Height = picture.Height, Format = format, PixelData = picture.Pixels });
    Assert.That(LuraWaveWriter.ToBytes(file), Is.EqualTo(LuraWaveFixtures.Bytes(name + ".lwf")));
  }

  /// <summary>
  /// Decodes every <c>.lwf</c> in the directory named by <c>LURAWAVE_CORPUS</c>; where a <c>name.ppm</c> or
  /// <c>name.pgm</c> sits beside one, it must match exactly. The three files LuraTech shipped (<c>fisch.lwf</c>,
  /// <c>karte.lwf</c>, <c>TIGER.LWF</c> from the SmartCompress package, mirrored at
  /// https://sembiance.com/fileFormatSamples/image/luraWave/) are not redistributable; their oracle decodes are
  /// produced the same way as the committed expectations.
  /// </summary>
  [Test]
  [Explicit("Needs LURAWAVE_CORPUS pointing at a directory of .lwf files.")]
  [Category("Integration")]
  public void DecodesTheCorpus() {
    var directory = Environment.GetEnvironmentVariable("LURAWAVE_CORPUS");
    if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
      Assert.Ignore("LURAWAVE_CORPUS is not set to an existing directory.");

    var decoded = 0;
    foreach (var path in Directory.EnumerateFiles(directory!)) {
      if (!path.EndsWith(".lwf", StringComparison.OrdinalIgnoreCase))
        continue;

      var image = LuraWaveReader.FromBytes(File.ReadAllBytes(path));
      ++decoded;
      foreach (var extension in (string[])[".ppm", ".pgm"]) {
        var reference = Path.ChangeExtension(path, extension);
        if (!File.Exists(reference))
          continue;

        var expected = LuraWaveFixtures.Pnm(File.ReadAllBytes(reference));
        Assert.That(image.PixelData, Is.EqualTo(expected.Pixels), $"{Path.GetFileName(path)} differs from {Path.GetFileName(reference)}");
      }
    }

    Assert.That(decoded, Is.GreaterThan(0), "No .lwf file found in the corpus directory.");
  }
}
