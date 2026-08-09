using System;
using System.IO;
using System.Linq;
using FileFormat.AmicaPaint;
using FileFormat.ColoRix;
using FileFormat.Core;
using Hawkynt.FileFormats.Images;

namespace Hawkynt.FileFormats.Images.Tests.GapClosures;

/// <summary>
/// Two rows of the coverage gap whose "extension" is not one.
/// </summary>
/// <remarks>
/// XnView's catalogue gives Amica Paint the extensions <c>ami [b]</c> and ColoRIX the extensions
/// <c>rix sci scx sc?</c>. Neither <c>[b]</c> nor <c>sc?</c> is a filename ending: they are the only
/// two entries out of 554 whose extension list carries a bracket or a wildcard, and the list that
/// produced these rows read both as names to be claimed. <c>[b]</c> is the prefix a Commodore file
/// carries in front of its name and the files themselves are <c>.ami</c>; <c>sc?</c> is <c>sc</c>
/// and any one character, all of which are claimed now.
/// <para/>
/// What makes claiming a wildcard's worth of names harmless is that the bytes still decide. XnView's
/// converter identifies ColoRIX from its signature and reads one under any name at all, so the
/// wildcard is what its file chooser offers rather than anything its reader tests — and the reader
/// here holds to <c>RIX3</c> under every one of the names.
/// </remarks>
[TestFixture]
public sealed class CatalogueNotAnExtensionTests {

  private static string[] _Extensions<T>() where T : IImageFormatMetadata<T> => T.FileExtensions;

  /// <summary>A ColoRIX picture, which is the only thing that reader is meant to take.</summary>
  private static byte[] _ColoRix(int width, int height) {
    var file = new byte[ColoRixFile.HeaderSize + ColoRixFile.PaletteSize + width * height];
    file[0] = (byte)'R';
    file[1] = (byte)'I';
    file[2] = (byte)'X';
    file[3] = (byte)'3';
    file[4] = (byte)(width - 1);
    file[5] = (byte)((width - 1) >> 8);
    file[6] = (byte)(height - 1);
    file[7] = (byte)((height - 1) >> 8);
    file[8] = ColoRixFile.VgaPaletteType;
    file[9] = 0;

    for (var i = 0; i < ColoRixFile.PaletteSize; ++i)
      file[ColoRixFile.HeaderSize + i] = (byte)(i % 64);

    for (var i = 0; i < width * height; ++i)
      file[ColoRixFile.HeaderSize + ColoRixFile.PaletteSize + i] = (byte)(i * 7);

    return file;
  }

  /// <summary>A run of bytes that is no picture format at all.</summary>
  private static byte[] _Noise(int length = 8192) {
    var data = new byte[length];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i * 37 + 11);

    return data;
  }

  /// <summary><c>[b]</c> is a filename prefix, so the only name the row leaves is the real one.</summary>
  [Test]
  [Category("Unit")]
  public void AmicaPaint_ClaimsTheNameAndNotTheBracket() {
    var extensions = _Extensions<AmicaPaintFile>();

    Assert.Multiple(() => {
      Assert.That(extensions, Does.Contain(".ami"));
      Assert.That(extensions.Any(x => x.Contains('[') || x.Contains(']')), Is.False);
    });
  }

  /// <summary>Every name <c>sc?</c> stands for, which is <c>sc</c> and any one character.</summary>
  [Test]
  [Category("Unit")]
  public void ColoRix_ClaimsEveryNameTheWildcardStandsFor() {
    var extensions = _Extensions<ColoRixFile>();

    Assert.Multiple(() => {
      Assert.That(extensions, Does.Contain(".rix"));
      foreach (var tail in "0123456789abcdefghijklmnopqrstuvwxyz")
        Assert.That(extensions, Does.Contain($".sc{tail}"), $"the wildcard covers .sc{tail}");

      Assert.That(extensions.Any(x => x.Contains('?')), Is.False);
    });
  }

  /// <summary>
  /// The names are shared with half a dozen other formats, so what makes claiming them safe is the
  /// signature: a ColoRIX picture is read under any of them and anything else is refused.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void ColoRix_ReadsItsOwnFileAndRefusesAnythingElseUnderTheSameNames() {
    var image = ColoRixFile.ToRawImage(ColoRixReader.FromBytes(_ColoRix(4, 3)));

    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(4));
      Assert.That(image.Height, Is.EqualTo(3));
    });

    Assert.Throws<InvalidDataException>(() => ColoRixReader.FromBytes(_Noise()));

    // A ZX Spectrum screen is 6912 bytes and arrives as .scr, which the wildcard now covers.
    Assert.Throws<InvalidDataException>(() => ColoRixReader.FromBytes(new byte[6912]));
  }

  /// <summary>
  /// VRML2 is in the catalogue with no reader behind it — XnView writes a <c>.wrl</c> and refuses
  /// the file it has just written. Nothing here claims the name either, and the row is a disposition
  /// rather than a gap.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void Vrml_IsClaimedByNothingHere()
    => Assert.That(
      FormatRegistry.AllFormats.SelectMany(entry => entry.AllExtensions ?? []),
      Has.None.EqualTo(".wrl").IgnoreCase);
}
