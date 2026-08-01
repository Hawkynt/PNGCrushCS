using System;
using System.IO;
using System.Text;
using FileFormat.Core;

namespace FileFormat.ScitexCt.Tests;

/// <summary>
/// The Scitex header: a control block, then the file type at eighty, then the parameters at 1024.
/// </summary>
/// <remarks>
/// The tests this replaces asserted an eighty-byte header with the file type at its front — the
/// control block's position, not the type's. Every reader looking at offset 80 found pixels there.
/// </remarks>
[TestFixture]
public sealed class ScitexCtTests {

  private static RawImage _Picture(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < pixels.Length; i += 3) {
      pixels[i] = (byte)(i % 253);
      pixels[i + 1] = (byte)(i % 251);
      pixels[i + 2] = (byte)(i % 247);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  [Category("Unit")]
  public void Written_PutsTheFileTypeAtEightyAndTheSizeAtOneThousandAndTwentyFour() {
    var bytes = ScitexCtWriter.ToBytes(ScitexCtFile.FromRawImage(_Picture(320, 200)));

    Assert.Multiple(() => {
      Assert.That(Encoding.ASCII.GetString(bytes, 80, 2), Is.EqualTo("CT"));
      Assert.That(bytes[1025], Is.EqualTo(3), "three separations for a colour picture");
      Assert.That(Encoding.ASCII.GetString(bytes, 1056, 12).Trim(), Is.EqualTo("200"), "rows");
      Assert.That(Encoding.ASCII.GetString(bytes, 1068, 12).Trim(), Is.EqualTo("320"), "columns");
      Assert.That(bytes, Has.Length.EqualTo(2048 + 320 * 200 * 3));
    });
  }

  [Test]
  [Category("Unit")]
  public void Read_RefusesAFileWhoseTypeFieldSaysSomethingElse() {
    var bytes = ScitexCtWriter.ToBytes(ScitexCtFile.FromRawImage(_Picture(4, 4)));
    bytes[80] = (byte)'L';
    bytes[81] = (byte)'W';

    Assert.Throws<InvalidDataException>(() => ScitexCtReader.FromBytes(bytes));
  }

  [Test]
  [Category("Unit")]
  public void Read_RefusesAFileTooShortForTheHeaderAlone()
    => Assert.Throws<InvalidDataException>(() => ScitexCtReader.FromBytes(new byte[2047]));

  [Test]
  [Category("Unit")]
  public void Read_RefusesAFileTooShortForThePixelsItsSizeClaims() {
    var bytes = ScitexCtWriter.ToBytes(ScitexCtFile.FromRawImage(_Picture(64, 64)));
    Assert.Throws<InvalidDataException>(() => ScitexCtReader.FromBytes(bytes[..(bytes.Length - 1)]));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_KeepsTheSizeTheModeAndThePixels() {
    var original = ScitexCtFile.FromRawImage(_Picture(320, 200));
    var restored = ScitexCtReader.FromBytes(ScitexCtWriter.ToBytes(original));

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(320));
      Assert.That(restored.Height, Is.EqualTo(200));
      Assert.That(restored.ColorMode, Is.EqualTo(ScitexCtColorMode.Rgb));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_KeepsThePicture() {
    var original = _Picture(32, 24);
    var restored = ScitexCtFile.ToRawImage(
      ScitexCtReader.FromBytes(ScitexCtWriter.ToBytes(ScitexCtFile.FromRawImage(original))));

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
