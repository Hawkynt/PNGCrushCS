using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.LuraWave.Tests;

/// <summary>Decodes held to LuraTech's own decoder, on streams from LuraTech's own encoder and from this package's.</summary>
/// <remarks>
/// <para>
/// The oracle is the LuraWave codec built into LuraWave SmartCompress 3.0 (<c>LuraSmrt.exe</c>, 2002), driven under
/// Wine through its internal decoder entry points (header at <c>0x4554C0</c>, set-up at <c>0x4555B0</c>, decode at
/// <c>0x455670</c>) by a harness DLL that stands in for the <c>Lwf153d.dll</c> the program imports. Every
/// <c>*.expected.ppm</c>/<c>*.expected.pgm</c> fixture is its output for the <c>.lwf</c> of the same name: pixel type
/// <c>0x10</c> (RGB) for colour, <c>0</c> for 8-bit grey and <c>0x20</c>/<c>0x25</c> for the wide grey modes. The
/// program is not redistributable, so its output is committed rather than run on CI.
/// </para>
/// <para>
/// The <c>luratech-*</c> streams come from the same program's encoder (entry points <c>0x455730</c> and
/// <c>0x4557C0</c>), fed synthetic gradients with noise: every colour mode it writes that a LuraTech decoder renders,
/// both stripe layouts, two interest regions, byte budgets and a password. The three modes it returns only as raw
/// channels are held to those channels in <see cref="LuraWaveRawModeTests"/>. The other streams were written with this
/// package's encoder from <c>source.ppm</c> (a seeded ImageMagick plasma with a drawn rectangle and line, 45x37 so
/// every dimension needs padding).
/// </para>
/// <para>
/// LuraTech's Java decoder (<c>lwf_jsdk2.6.jar</c>) was the oracle before; it agrees with the C codec on most streams
/// but not all: it reconstructs refined coefficients with a different offset, rounds green in the colour-difference
/// mode, still reads bits for nodes whose thresholds have run out (so it mis-decodes LuraTech's own four-channel and
/// wide-colour streams), resumes after a spent budget, and cannot check passwords (it hashes all but the last
/// character). SPEC.md lists each difference.
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
  [TestCase("luratech-s01-l3-base", "pgm", TestName = "LuraTech: grey, three levels, stripes")]
  [TestCase("luratech-s02-l4", "ppm", TestName = "LuraTech: colour difference")]
  [TestCase("luratech-m02-lossy", "ppm", TestName = "LuraTech: colour difference cut to 3000 bytes")]
  [TestCase("luratech-s04-l3", "ppm", TestName = "LuraTech: raw RGB cut to 300 bytes")]
  [TestCase("luratech-m06-roi2", "ppm", TestName = "LuraTech: reversible colour, two interest regions, cut to 6000 bytes")]
  [TestCase("luratech-m01-roi2", "pgm", TestName = "LuraTech: grey, two interest regions, cut to 2500 bytes")]
  [TestCase("luratech-s07-l3", "ppm", TestName = "LuraTech: CMYK")]
  [TestCase("luratech-s08-l4-lossy", "ppm", TestName = "LuraTech: Lab cut to 400 bytes")]
  [TestCase("luratech-s17-l3", "pgm", TestName = "LuraTech: wide grey, 12-bit maximum")]
  [TestCase("luratech-s18-l3", "ppm", TestName = "LuraTech: wide colour difference")]
  [TestCase("luratech-s19-l4-base", "ppm", TestName = "LuraTech: wide reversible colour in stripes")]
  [TestCase("luratech-s22-l3", "pgm", TestName = "LuraTech: signed wide grey, maximum 32768")]
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

  [Test]
  [Category("Integration")]
  public void PasswordUnlocksTheFullQuality() {
    var data = LuraWaveFixtures.Bytes("luratech-m01-pw.lwf");
    Assert.Multiple(() => {
      Assert.That(LuraWaveReader.FromBytes(data, "secret").PixelData, Is.EqualTo(LuraWaveFixtures.Pnm(LuraWaveFixtures.Bytes("luratech-m01-pw.expected.pgm")).Pixels));
      Assert.That(LuraWaveReader.FromBytes(data).PixelData, Is.EqualTo(LuraWaveFixtures.Pnm(LuraWaveFixtures.Bytes("luratech-m01-pw.public.pgm")).Pixels));
    });
  }

  [Test]
  [Category("Unit")]
  public void WrongPasswordIsRefused()
    => Assert.Throws<UnauthorizedAccessException>(() => LuraWaveReader.FromBytes(LuraWaveFixtures.Bytes("luratech-m01-pw.lwf"), "Secret"));

  [Test]
  [Category("Unit")]
  public void PasswordChecksAreLuraTechs() {
    // The 16-bit value is what LuraTech's encoder put in this file's PW chunk; the 32-bit value is the check word its
    // encoder embedded where the public quality of 600 ends.
    var header = LuraWaveHeader.Parse(LuraWaveFixtures.Bytes("luratech-m01-pw.lwf"));
    Assert.Multiple(() => {
      Assert.That(LuraWavePassword.HeaderCheck("secret"), Is.EqualTo(header.PasswordCheck));
      Assert.That(header.PublicQuality, Is.EqualTo(600));
      Assert.That(LuraWavePassword.StreamCheck("secret"), Is.EqualTo(0x139D2726u));
      Assert.That(LuraWavePassword.HeaderCheck("pass+word"), Is.EqualTo(0xC80E));
      Assert.That(LuraWavePassword.StreamCheck("pass+word"), Is.EqualTo(0x1F5AEABEu));
    });
  }

  [TestCase("writer-grey", "source.pgm", TestName = "8-bit grey")]
  [TestCase("writer-grey16", "source16.pgm", TestName = "16-bit grey")]
  [TestCase("writer-rgb", "source.ppm", TestName = "RGB")]
  [Category("Integration")]
  public void WhatTheWriterProducedDecodesToItsSource(string name, string source) {
    // LuraTech's C decoder and its Java decoder both returned these sources unchanged from the committed streams;
    // this checks ours agrees.
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
