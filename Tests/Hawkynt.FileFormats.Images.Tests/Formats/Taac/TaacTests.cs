using System;
using System.IO;
using System.Linq;
using System.Text;
using FileFormat.Core;
using FileFormat.Taac;

namespace FileFormat.Taac.Tests;

/// <summary>
/// The fixtures are built from the header rules xloadimage's <c>vff.c</c> states, and the one that
/// is a real file was checked against the sample: a 640 by 480 single-band picture with a 256-entry
/// colour map, which decodes byte for byte the same as an independent decode of the same rules.
/// </summary>
[TestFixture]
public sealed class TaacTests {

  /// <summary>Builds a file: the four letters, the header fields, the form feed, then the raster.</summary>
  private static byte[] _Build(string fields, byte[] pixels, bool terminate = true) {
    var header = Encoding.ASCII.GetBytes(TaacFile.Magic + "\n" + fields);
    var tail = terminate ? new byte[] { TaacFile.HeaderTerminator, (byte)'\n' } : [];

    return header.Concat(tail).Concat(pixels).ToArray();
  }

  private static byte[] _Ramp(int count) => Enumerable.Range(0, count).Select(i => (byte)i).ToArray();

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => TaacReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_WithoutTheOpeningLettersIsRefused()
    => Assert.Throws<InvalidDataException>(() => TaacReader.FromBytes(Encoding.ASCII.GetBytes("form=1;\frubbish")));

  [Test]
  [Category("Unit")]
  public void FromBytes_WithNoFormFeedIsRefused()
    => Assert.Throws<InvalidDataException>(() => TaacReader.FromBytes(_Build("rank=2;\nsize=2 2;\n", _Ramp(4), terminate: false)));

  /// <summary>
  /// A value runs to its semicolon. One that never reaches it has swallowed the rest of the header,
  /// and everything after it would be read as part of that value.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_AFieldWithNoSemicolonIsRefused()
    => Assert.Throws<InvalidDataException>(() => TaacReader.FromBytes(_Build("rank=2;\nsize=2 2\n", _Ramp(4))));

  [Test]
  [Category("Unit")]
  public void FromBytes_ReadsTheSizeAndTheBandsTheHeaderStates() {
    var file = TaacReader.FromBytes(_Build("rank=2;\nbands=1;\nbits=8;\nsize=4 2;\n", _Ramp(8)));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(4));
      Assert.That(file.Height, Is.EqualTo(2));
      Assert.That(file.Bands, Is.EqualTo(1));
      Assert.That(file.PixelData, Is.EqualTo(_Ramp(8)));
    });
  }

  /// <summary>
  /// The header states the picture's size, so the file has to carry that many bytes. Reading a
  /// short one would show part of a picture as though it were the whole of it.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_AFileTooShortForTheSizeItStatesIsRefused()
    => Assert.Throws<InvalidDataException>(() => TaacReader.FromBytes(_Build("rank=2;\nsize=4 2;\n", _Ramp(7))));

  [Test]
  [Category("Unit")]
  public void FromBytes_AsManyExtentsAsTheRankClaimsOrItIsRefused()
    => Assert.Throws<InvalidDataException>(() => TaacReader.FromBytes(_Build("rank=2;\nsize=4;\n", _Ramp(8))));

  [Test]
  [Category("Unit")]
  public void FromBytes_AVolumeIsRefusedRatherThanReadAsAPicture()
    => Assert.Throws<InvalidDataException>(() => TaacReader.FromBytes(_Build("rank=3;\nsize=4 2 2;\n", _Ramp(16))));

  [Test]
  [Category("Unit")]
  public void FromBytes_ASampleWiderThanEightBitsIsRefused()
    => Assert.Throws<InvalidDataException>(() => TaacReader.FromBytes(_Build("rank=2;\nbits=16;\nsize=4 2;\n", _Ramp(16))));

  [Test]
  [Category("Unit")]
  public void FromBytes_TwoBandsIsRefusedBecauseNothingSaysWhatTheSecondIs()
    => Assert.Throws<InvalidDataException>(() => TaacReader.FromBytes(_Build("rank=2;\nbands=2;\nsize=4 2;\n", _Ramp(16))));

  /// <summary>
  /// The header both lists the colour map and says how many entries it has. The two disagreeing
  /// means one of them does not describe the file that was written.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_AColourMapThatIsNotTheStatedSizeIsRefused()
    => Assert.Throws<InvalidDataException>(() => TaacReader.FromBytes(
      _Build("rank=2;\nsize=4 2;\ncolormapsize=4;\ncolormap=000000 ffffff;\n", _Ramp(8))
    ));

  /// <summary>
  /// A map entry's six digits are blue, green and red in that order. Taken the other way round every
  /// picture comes out with its red and blue channels swapped, which is how the sample was settled.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void ToRawImage_AColourMapEntryIsBlueThenGreenThenRed() {
    var file = TaacReader.FromBytes(_Build(
      "rank=2;\nbands=1;\nbits=8;\nsize=2 1;\ncolormapsize=2;\ncolormap=ff0000 0000ff;\n",
      [0, 1]
    ));

    var image = TaacFile.ToRawImage(file);

    Assert.Multiple(() => {
      Assert.That(image.Format, Is.EqualTo(PixelFormat.Indexed8));
      Assert.That(image.Palette![..3], Is.EqualTo(new byte[] { 0, 0, 255 }), "ff0000 is blue");
      Assert.That(image.Palette![3..6], Is.EqualTo(new byte[] { 255, 0, 0 }), "0000ff is red");
    });
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ASingleBandWithNoColourMapIsGrey() {
    var image = TaacFile.ToRawImage(TaacReader.FromBytes(_Build("rank=2;\nbands=1;\nsize=4 2;\n", _Ramp(8))));

    Assert.That(image.Format, Is.EqualTo(PixelFormat.Gray8));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ThreeBandsAreOnePixelEach() {
    var image = TaacFile.ToRawImage(TaacReader.FromBytes(_Build("rank=2;\nbands=3;\nbits=8;\nsize=2 2;\n", _Ramp(12))));

    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(2));
      Assert.That(image.Height, Is.EqualTo(2));
      Assert.That(image.Format, Is.EqualTo(PixelFormat.Bgr24));
    });
  }

  /// <summary>Trailing bytes past the stated size are whatever the writer left there, not picture.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_ExtraBytesAfterThePictureAreNotPartOfIt() {
    var file = TaacReader.FromBytes(_Build("rank=2;\nbands=1;\nsize=2 2;\n", _Ramp(16)));

    Assert.That(file.PixelData, Has.Length.EqualTo(4));
  }
}
