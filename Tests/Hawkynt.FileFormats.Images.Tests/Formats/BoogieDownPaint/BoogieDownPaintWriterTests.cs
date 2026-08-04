using System;
using System.Text;
using FileFormat.BoogieDownPaint;
using FileFormat.Core;

namespace FileFormat.BoogieDownPaint.Tests;

/// <summary>
/// Packing a Boogie Down Paint picture.
/// </summary>
/// <remarks>
/// The format had no writer at all. Of its three encodings this emits the one that names its own
/// escape bytes in a header — the oldest makes every byte a command and cannot hold a literal, and
/// the loader form would mean emitting somebody's machine code. RECOIL reads what this produces and
/// draws the same picture we do.
/// </remarks>
[TestFixture]
public class BoogieDownPaintWriterTests {

  private static RawImage _Picture() {
    var pixels = new byte[BoogieDownPaintFile.Width * BoogieDownPaintFile.Height * 3];
    for (var i = 0; i < pixels.Length; i += 3)
      pixels[i + 1] = 255;                                  // a screen of one colour the machine has

    return new() {
      Width = BoogieDownPaintFile.Width,
      Height = BoogieDownPaintFile.Height,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    };
  }

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => BoogieDownPaintFile.FromRawImage(null!));

  [Test]
  public void FromRawImage_FillsTheWholeScreen() {
    var file = BoogieDownPaintFile.FromRawImage(_Picture());

    Assert.That(file.ScreenData, Has.Length.EqualTo(BoogieDownPaintFile.UnpackedSize));
  }

  [Test]
  public void ToBytes_SaysWhichFormItIs() {
    var bytes = BoogieDownPaintWriter.ToBytes(BoogieDownPaintFile.FromRawImage(_Picture()));

    Assert.That(Encoding.ASCII.GetString(bytes, 2, 8), Is.EqualTo("BDP 5.00"));
  }

  [Test]
  public void ToBytes_ShortensAPictureThatRepeatsItself() {
    // A screen of one colour is nearly all runs, so packing it must cost far less than the ten
    // thousand bytes it unpacks to. A writer emitting literals throughout would still round-trip.
    var bytes = BoogieDownPaintWriter.ToBytes(BoogieDownPaintFile.FromRawImage(_Picture()));

    Assert.That(bytes.Length, Is.LessThan(BoogieDownPaintFile.UnpackedSize / 4));
  }

  [Test]
  public void ToBytes_ScreenOfTheWrongLength_ThrowsArgumentException() {
    var stunted = new BoogieDownPaintFile { ScreenData = new byte[100] };

    Assert.Throws<ArgumentException>(() => BoogieDownPaintWriter.ToBytes(stunted));
  }

  [Test]
  public void RoundTrip_TheScreenComesBackByteForByte() {
    var original = BoogieDownPaintFile.FromRawImage(_Picture());

    var restored = BoogieDownPaintReader.FromBytes(BoogieDownPaintWriter.ToBytes(original));

    Assert.That(restored.ScreenData, Is.EqualTo(original.ScreenData));
  }

  [Test]
  public void RoundTrip_AScreenUsingEveryByteValueSurvives() {
    // The escapes are chosen as the two rarest values, so a screen leaving none unused forces the
    // writer to escape occurrences of its own escape bytes.
    var screen = new byte[BoogieDownPaintFile.UnpackedSize];
    for (var i = 0; i < screen.Length; ++i)
      screen[i] = (byte)i;

    var restored = BoogieDownPaintReader.FromBytes(BoogieDownPaintWriter.ToBytes(new() { ScreenData = screen }));

    Assert.That(restored.ScreenData, Is.EqualTo(screen));
  }

  [Test]
  public void RoundTrip_ARunLongerThanOneCountByteSurvives() {
    // Runs past 256 need the two-byte form, which is a different escape and a different length.
    var screen = new byte[BoogieDownPaintFile.UnpackedSize];
    for (var i = 0; i < 5000; ++i)
      screen[i] = 0x5A;

    var restored = BoogieDownPaintReader.FromBytes(BoogieDownPaintWriter.ToBytes(new() { ScreenData = screen }));

    Assert.That(restored.ScreenData, Is.EqualTo(screen));
  }

  [Test]
  public void RoundTrip_ThroughAPictureKeepsOneFlatColourThroughout() {
    var bytes = BoogieDownPaintWriter.ToBytes(BoogieDownPaintFile.FromRawImage(_Picture()));

    var drawn = BoogieDownPaintFile.ToRawImage(BoogieDownPaintReader.FromBytes(bytes)).ToRgb24();

    // The machine has no pure green, so the flat screen comes back as whichever of its sixteen sits
    // nearest — the fact worth holding is that it is one colour everywhere and one of that sixteen.
    var green = Commodore64Graphics.HexColors[Commodore64Graphics.FindNearestColorIndex(0, 255, 0)];

    Assert.Multiple(() => {
      Assert.That(drawn[0], Is.EqualTo((green >> 16) & 0xFF));
      Assert.That(drawn[1], Is.EqualTo((green >> 8) & 0xFF));
      Assert.That(drawn[2], Is.EqualTo(green & 0xFF));
      Assert.That(drawn[^3..], Is.EqualTo(drawn[..3]), "the far corner is the same colour as the first");
    });
  }
}
