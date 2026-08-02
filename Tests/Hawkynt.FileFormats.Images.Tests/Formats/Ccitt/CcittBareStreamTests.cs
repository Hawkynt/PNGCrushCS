using System;
using FileFormat.Ccitt;
using FileFormat.Core;

namespace FileFormat.Ccitt.Tests;

/// <summary>
/// Reading a file that is nothing but coding: its width, and which way round its two values go.
/// </summary>
/// <remarks>
/// A bare stream states no size anywhere, so the fax scan line of 1728 was assumed. It need not be:
/// every line's runs add up to exactly the width, and a line ends with a marker that is not a run
/// code, so adding them until the run decoder refuses gives the width the coder actually used.
/// <para/>
/// The two values were also the wrong way about, so every such file came back as its own negative —
/// a page of white ink on black. The coding counts runs of white first and the decoder sets a bit
/// for black, so nought is paper. A CALS raster reverses this and deliberately, which is what made
/// the mistake easy to carry from one to the other.
/// <para/>
/// Checked against ImageMagick on a real Group 3 fax: the measured width is 1728, and every one of
/// its 3678912 pixels matches what ImageMagick draws in those columns.
/// </remarks>
[TestFixture]
public sealed class CcittBareStreamTests {

  /// <summary>Packs a run of bits given as a string into bytes.</summary>
  private static byte[] _Bits(string bits) {
    var padded = bits.PadRight((bits.Length + 7) / 8 * 8, '0');
    var data = new byte[padded.Length / 8];
    for (var i = 0; i < padded.Length; ++i)
      if (padded[i] == '1')
        data[i / 8] |= (byte)(1 << (7 - i % 8));

    return data;
  }

  private const string _Eol = "000000000001";

  [Test]
  [Category("Unit")]
  public void PaperIsWhiteAndInkIsBlack() {
    // One line: a white run of 2 (0111) then a black run of 2 (11), which is a width of four.
    var data = _Bits(_Eol + "0111" + "11" + _Eol);
    var image = CcittFile.ToRawImage(CcittFile.ReadBareStream(data));

    Assert.That(image.Palette, Is.Not.Null);
    Assert.Multiple(() => {
      Assert.That(image.Palette![0], Is.EqualTo(255), "nought is paper");
      Assert.That(image.Palette![1], Is.EqualTo(255));
      Assert.That(image.Palette![2], Is.EqualTo(255));
      Assert.That(image.Palette![3], Is.Zero, "one is ink");
    });
  }

  [Test]
  [Category("Unit")]
  public void TheWidthIsAddedUpFromTheFirstLine() {
    // White 2, black 2: a line four pixels across, not the 1728 that used to be assumed.
    var data = _Bits(_Eol + "0111" + "11" + _Eol + "0111" + "11" + _Eol);

    Assert.That(CcittFile.ReadBareStream(data).Width, Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void AWiderLineIsMeasuredJustTheSame() {
    // White 8 (10011), black 3 (10), white 1 (000111): twelve across.
    var data = _Bits(_Eol + "10011" + "10" + "000111" + _Eol);

    Assert.That(CcittFile.ReadBareStream(data).Width, Is.EqualTo(12));
  }

  [Test]
  [Category("Unit")]
  public void TheDrawnPixelsFollowTheRuns() {
    var data = _Bits(_Eol + "0111" + "11" + _Eol);
    var rgb = CcittFile.ToRawImage(CcittFile.ReadBareStream(data)).ToRgb24();

    Assert.Multiple(() => {
      Assert.That(rgb[0], Is.EqualTo(255), "the first two are white");
      Assert.That(rgb[3], Is.EqualTo(255));
      Assert.That(rgb[6], Is.Zero, "the last two are black");
      Assert.That(rgb[9], Is.Zero);
    });
  }

  [Test]
  [Category("Unit")]
  public void FaxCodingInsideItsOwnContainerIsRefused() {
    // A ZyXEL fax puts a header of its own in front of the coding. Read as though the coding began
    // at the first byte, the header itself decoded to four lines of nothing and reported no trouble.
    var data = new byte[512];
    System.Text.Encoding.ASCII.GetBytes("ZyXEL").CopyTo(data, 0);

    Assert.Throws<System.IO.InvalidDataException>(() => CcittFile.ReadBareStream(data));
  }

  [Test]
  [Category("Unit")]
  public void BareCodingIsStillRead() {
    var data = _Bits(_Eol + "0111" + "11" + _Eol);

    Assert.That(CcittFile.ReadBareStream(data).Width, Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void SomethingWithNoCodingAtAllIsRefused()
    => Assert.Throws<System.IO.InvalidDataException>(() => CcittFile.ReadBareStream(Array.Empty<byte>()));
}
