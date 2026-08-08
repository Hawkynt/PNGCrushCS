using System;
using System.IO;
using System.Text;
using FileFormat.Core;
using FileFormat.Hpgl;

namespace FileFormat.Hpgl.Tests;

[TestFixture]
public sealed class HpglTests {

  private static HpglFile _Read(string plot) => HpglReader.FromBytes(Encoding.ASCII.GetBytes(plot));

  private static RawImage _Draw(string plot) => HpglFile.ToRawImage(_Read(plot));

  /// <summary>How much of the picture is ink, taking the plot as drawn on white.</summary>
  private static double _Coverage(RawImage image) {
    var pixels = image.PixelData;
    var total = 0.0;
    for (var i = 0; i < pixels.Length; i += 4)
      total += (255 - pixels[i]) / 255.0;

    return total / (image.Width * image.Height);
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => HpglReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_ProseThatMovesNoPen_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => _Read("The quick brown fox jumps over the lazy dog."));

  [Test]
  [Category("Unit")]
  public void FromBytes_ReadsTheMnemonicsAndTheirParameters() {
    var file = _Read("IN;SP1;PU0,0;PD100,0,100,100;");

    Assert.Multiple(() => {
      Assert.That(file.Instructions, Has.Count.EqualTo(4));
      Assert.That(file.Instructions[1].Mnemonic, Is.EqualTo("SP"));
      Assert.That(file.Instructions[3].Numbers, Is.EqualTo(new double[] { 100, 0, 100, 100 }));
    });
  }

  /// <summary>
  /// The terminator is optional, and the language says any non-numeric character ends an
  /// instruction. A reader that needed the semicolon would swallow the next mnemonic.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_AnInstructionEndsAtTheNextMnemonicWithoutASemicolon() {
    var file = _Read("IN\nSP1\nPU0,0\nPD100,100\n");

    Assert.Multiple(() => {
      Assert.That(file.Instructions, Has.Count.EqualTo(4));
      Assert.That(file.Instructions[3].Mnemonic, Is.EqualTo("PD"));
      Assert.That(file.Instructions[3].Numbers, Is.EqualTo(new double[] { 100, 100 }));
    });
  }

  /// <summary>
  /// A label runs to its terminator, and what follows it is instructions again. Reading the label
  /// as instructions, or the rest of the file as the label, both lose the drawing.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_ALabelIsTakenWholeAndTheDrawingAfterItIsNot() {
    var file = _Read("SP1;PU0,0;LBPU9999,9999;PD1,1PD100,100;");

    Assert.Multiple(() => {
      Assert.That(file.Instructions[2].Mnemonic, Is.EqualTo("LB"));
      Assert.That(file.Instructions[2].Text, Is.EqualTo("PU9999,9999;PD1,1"));
      Assert.That(file.Instructions[3].Mnemonic, Is.EqualTo("PD"));
      Assert.That(file.Instructions[3].Numbers, Is.EqualTo(new double[] { 100, 100 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ADeviceControlEscapeIsSteppedOverRatherThanReadAsMnemonics() {
    var file = _Read(".(;.I81;;17:SP1;PU0,0;PD100,100;");

    Assert.Multiple(() => {
      Assert.That(file.Instructions[0].Mnemonic, Is.EqualTo("SP"));
      Assert.That(file.Instructions, Has.Count.EqualTo(3));
    });
  }

  /// <summary>
  /// The picture is the ink, so a plot twice the size makes a picture twice the size. A reader
  /// that drew everything at one fixed size would give the same two numbers for both.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void ToRawImage_TheSizeFollowsHowFarThePenWent() {
    var small = _Draw("SP1;PU0,0;PD1000,0,1000,500,0,500,0,0;");
    var large = _Draw("SP1;PU0,0;PD2000,0,2000,1000,0,1000,0,0;");

    Assert.Multiple(() => {
      Assert.That(large.Width, Is.EqualTo(small.Width * 2).Within(4));
      Assert.That(large.Height, Is.EqualTo(small.Height * 2).Within(4));
      Assert.That((double)small.Width / small.Height, Is.EqualTo(2).Within(0.05), "and it keeps the plot's shape");
    });
  }

  /// <summary>
  /// Both plots cover the same ground, so both make a picture of the same size; the difference is
  /// only whether the pen was down when it crossed it.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void ToRawImage_APenUpMoveDrawsNothingAndAPenDownMoveDoes() {
    const string frame = "SP1;PU0,0;PD2000,0,2000,1000,0,1000,0,0;";
    var travelled = _Draw(frame + "PU0,0;PU2000,1000;");
    var drawn = _Draw(frame + "PU0,0;PD2000,1000;");

    Assert.Multiple(() => {
      Assert.That(travelled.Width, Is.EqualTo(drawn.Width), "the same ground was covered either way");
      Assert.That(_Coverage(travelled), Is.LessThan(_Coverage(drawn)), "but only what the pen drew is ink");
    });
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_PenZeroIsNoPenAtAll() {
    var withPen = _Draw("SP1;PU0,0;PD2000,1000;");
    var without = _Draw("SP1;PU0,0;PD2000,1000;SP0;PU0,1000;PD2000,0;");

    Assert.That(_Coverage(without), Is.EqualTo(_Coverage(withPen)).Within(0.01), "the second stroke was drawn with nothing");
  }

  /// <summary>
  /// Scaling maps user coordinates onto the frame, so a shape drawn over the whole of a scaled
  /// range covers the same area as one drawn over the whole frame in plotter units.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void ToRawImage_ScalingMapsUserUnitsOntoTheFrame() {
    var scaled = _Draw("SP1;IP0,0,1000,500;SC0,100,0,50;PU0,0;PD100,0,100,50,0,50,0,0;");
    var plain = _Draw("SP1;IP0,0,1000,500;PU0,0;PD1000,0,1000,500,0,500,0,0;");

    Assert.Multiple(() => {
      Assert.That(scaled.Width, Is.EqualTo(plain.Width).Within(2));
      Assert.That(scaled.Height, Is.EqualTo(plain.Height).Within(2));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_AFilledRectangleCoversWhatItsCornersEnclose() {
    var filled = _Draw("SP1;PU0,0;RA2000,1000;");
    var edged = _Draw("SP1;PU0,0;EA2000,1000;");

    Assert.Multiple(() => {
      Assert.That(_Coverage(filled), Is.GreaterThan(0.9), "the fill covers the box");
      Assert.That(_Coverage(edged), Is.LessThan(0.2), "the edge only outlines it");
    });
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ACircleIsRoundRatherThanSquare() {
    var image = _Draw("SP1;PU1000,1000;CI900;");
    var coverage = _Coverage(image);

    // Only the rim is drawn, so what matters is that the shape is as wide as it is tall.
    Assert.Multiple(() => {
      Assert.That((double)image.Width / image.Height, Is.EqualTo(1).Within(0.05));
      Assert.That(coverage, Is.GreaterThan(0.01).And.LessThan(0.3), "a rim, not a disc");
    });
  }

  /// <summary>
  /// <c>LT</c> with nothing after it means solid, and <c>LT1</c> means dotted. Reading a bare
  /// <c>LT</c> as type one draws every solid line in a file as dots.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void ToRawImage_ABareLineTypeIsSolidAndTypeOneIsNot() {
    var solid = _Draw("SP1;LT;PU0,500;PD4000,500;PU0,0;PD0,1000;");
    var dotted = _Draw("SP1;LT1;PU0,500;PD4000,500;PU0,0;PD0,1000;");

    Assert.That(_Coverage(dotted), Is.LessThan(_Coverage(solid) * 0.75), "the dotted line lays down far less ink");
  }
}
