using System;
using System.IO;
using System.Text;
using FileFormat.Avs;
using FileFormat.Cloe;
using FileFormat.Core;
using FileFormat.Hpgl;
using FileFormat.Ilbm;
using FileFormat.PcPaint;
using FileFormat.Psp;
using FileFormat.ScitexCt;

namespace FileFormat.ClaimedExtensions.Tests;

/// <summary>
/// The names claimed because they belong to a format already read here, and the refusals that make
/// claiming them safe.
/// </summary>
/// <remarks>
/// Claiming a name is only worth anything if the reader behind it still decides from the bytes. Each
/// case below checks both halves: that the name is claimed, and that a file of some other format
/// under that name is refused rather than drawn as a picture it is not.
/// </remarks>
[TestFixture]
public sealed class ClaimedExtensionTests {

  private static string[] _Extensions<T>() where T : IImageFormatMetadata<T> => T.FileExtensions;

  /// <summary>A short PostScript program, which is what several of these names usually hold.</summary>
  private static byte[] _PostScript() => Encoding.ASCII.GetBytes(
    "%!PS-Adobe-2.0 EPSF-1.2\n%%BoundingBox: 0 0 100 100\n100 100 moveto 0 0 lineto stroke\nshowpage\n");

  /// <summary>A run of bytes that is no picture format at all, long enough to tempt a reader.</summary>
  private static byte[] _Noise(int length = 8192) {
    var data = new byte[length];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i * 37 + 11);

    return data;
  }

  [Test]
  [Category("Unit")]
  public void Iff_ClaimsBlk_AndStillWantsTheGroupIdentifier() {
    Assert.That(_Extensions<IlbmFile>(), Does.Contain(".blk"));
    Assert.Throws<InvalidDataException>(() => IlbmReader.FromBytes(_Noise()));
    Assert.Throws<InvalidDataException>(() => IlbmReader.FromBytes(_PostScript()));
  }

  [Test]
  [Category("Unit")]
  public void ScitexCt_ClaimsCh_AndStillWantsTheTagAtEighty() {
    Assert.That(_Extensions<ScitexCtFile>(), Does.Contain(".ch"));
    Assert.Throws<InvalidDataException>(() => ScitexCtReader.FromBytes(_Noise()));
  }

  [Test]
  [Category("Unit")]
  public void Avs_ClaimsItsOtherNames_AndStillWantsTheLengthsToAddUp() {
    var extensions = _Extensions<AvsFile>();
    Assert.That(extensions, Does.Contain(".mbfavs"));
    Assert.That(extensions, Does.Contain(".mbfs"));
    Assert.Throws<InvalidDataException>(() => AvsReader.FromBytes(_Noise()));
  }

  [Test]
  [Category("Unit")]
  public void Psp_ClaimsTheShortNames_AndStillWantsItsHeaderString() {
    var extensions = _Extensions<PspFile>();
    Assert.That(extensions, Does.Contain(".pfr"));
    Assert.That(extensions, Does.Contain(".msk"));
    Assert.That(extensions, Does.Contain(".tex"));
    Assert.Throws<InvalidDataException>(() => PspReader.FromBytes(_Noise()));
    Assert.Throws<InvalidDataException>(() => PspReader.FromBytes(_PostScript()));
  }

  [Test]
  [Category("Unit")]
  public void Hpgl_ClaimsTheSpoolNames_AndStillRefusesAPostScriptSpool() {
    var extensions = _Extensions<HpglFile>();
    Assert.That(extensions, Does.Contain(".prn"));
    Assert.That(extensions, Does.Contain(".prt"));
    Assert.Throws<InvalidDataException>(() => HpglReader.FromBytes(_PostScript()));
    Assert.Throws<InvalidDataException>(() => HpglReader.FromBytes(_Noise()));
  }

  [Test]
  [Category("Unit")]
  public void Cloe_ClaimsItsLongName_AndStillWantsTheHeaderToStateTheSize() {
    Assert.That(_Extensions<CloeFile>(), Does.Contain(".cloe"));
    Assert.Throws<InvalidDataException>(() => CloeReader.FromBytes(_PostScript()));

    // The header used to be allowed to state nothing, at which point 320 by 200 was invented and
    // any file long enough was drawn as a picture of a size it never claimed.
    Assert.Throws<InvalidDataException>(() => CloeReader.FromBytes(new byte[8 + 320 * 200 * 3]));
  }

  [Test]
  [Category("Unit")]
  public void PcPaint_ClaimsSim_AndStillWantsItsOwnMarkerWord() {
    Assert.That(_Extensions<PcPaintFile>(), Does.Contain(".sim"));
    Assert.Throws<InvalidDataException>(() => PcPaintReader.FromBytes(_Noise()));
    Assert.Throws<InvalidDataException>(() => PcPaintReader.FromBytes(_PostScript()));
  }
}
