using System;
using System.IO;
using System.Linq;
using FileFormat.Core;

namespace FileFormat.AtariAgp.Tests;

/// <summary>
/// The AGP container: one length, a mode byte, nine registers and a full screen of bitmap.
/// </summary>
/// <remarks>
/// These replace a set written against a layout this project invented — a bare bitmap at one of
/// three lengths, with the colour registers nowhere. Those tests passed against the writer that
/// produced the same invention, which is exactly the agreement a format test cannot rely on. So the
/// assertions here are about byte positions the format specifies, not about what our writer emits.
/// </remarks>
[TestFixture]
public sealed class AtariAgpTests {

  private static RawImage _Picture() {
    var pixels = new byte[320 * 192 * 3];
    for (var y = 0; y < 192; ++y)
    for (var x = 0; x < 320; ++x) {
      var at = (y * 320 + x) * 3;
      pixels[at] = (byte)(x * 255 / 319);
      pixels[at + 1] = (byte)(y * 255 / 191);
      pixels[at + 2] = (byte)((x ^ y) & 0xFF);
    }

    return new() { Width = 320, Height = 192, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  [Category("Unit")]
  public void Written_HasTheOneLengthAndLayoutTheFormatHas() {
    var bytes = AtariAgpWriter.ToBytes(AtariAgpFile.FromRawImage(_Picture()));

    Assert.Multiple(() => {
      Assert.That(bytes, Has.Length.EqualTo(7690));
      Assert.That(bytes[0], Is.EqualTo(15), "the mode byte names an ANTIC mode");
      Assert.That(
        bytes.Skip(1).Take(9).All(b => (b & 1) == 0), Is.True,
        "a colour register's low bit does not reach the screen, so it must not be set");
      Assert.That(bytes.Skip(10).Any(b => b != 0), Is.True, "the bitmap starts at ten");
    });
  }

  [Test]
  [Category("Unit")]
  public void Read_TakesTheModeFromTheFileRatherThanItsLength() {
    var bytes = new byte[7690];
    bytes[0] = 9;
    bytes[9] = 0x24;

    var file = AtariAgpReader.FromBytes(bytes);
    Assert.That(file.Mode, Is.EqualTo(AtariAgpMode.Graphics9));
    Assert.That(file.Registers[8], Is.EqualTo(0x24));
  }

  [TestCase((byte)0)]
  [TestCase((byte)7)]
  [TestCase((byte)16)]
  [Category("Unit")]
  public void Read_RefusesAModeTheChipDoesNotHave(byte mode) {
    var bytes = new byte[7690];
    bytes[0] = mode;

    Assert.Throws<InvalidDataException>(() => AtariAgpReader.FromBytes(bytes));
  }

  [TestCase(7680)]
  [TestCase(7689)]
  [TestCase(7691)]
  [Category("Unit")]
  public void Read_RefusesAnyOtherLength(int length)
    => Assert.Throws<InvalidDataException>(() => AtariAgpReader.FromBytes(new byte[length]));

  [Test]
  [Category("Integration")]
  public void RoundTrip_KeepsTheModeTheRegistersAndTheBitmap() {
    var original = AtariAgpFile.FromRawImage(_Picture());
    var restored = AtariAgpReader.FromBytes(AtariAgpWriter.ToBytes(original));

    Assert.Multiple(() => {
      Assert.That(restored.Mode, Is.EqualTo(original.Mode));
      Assert.That(restored.Registers, Is.EqualTo(original.Registers));
      Assert.That(restored.Bitmap, Is.EqualTo(original.Bitmap));
    });
  }

  [Test]
  [Category("Integration")]
  public void Decoded_DrawsOnlyTheColoursItsRegistersHold() {
    var file = AtariAgpFile.FromRawImage(_Picture());
    var image = AtariAgpFile.ToRawImage(file);

    var gtia = Atari8BitGraphics.CreatePalette();
    var allowed = new[] { 4, 5, 6, 8 }
      .Select(i => file.Registers[i] & 254)
      .Select(r => (gtia[r * 3], gtia[r * 3 + 1], gtia[r * 3 + 2]))
      .ToHashSet();

    Assert.That(image.Width, Is.EqualTo(320));
    Assert.That(image.Height, Is.EqualTo(192));
    for (var i = 0; i < image.PixelData.Length; i += 3)
      Assert.That(
        allowed, Does.Contain((image.PixelData[i], image.PixelData[i + 1], image.PixelData[i + 2])),
        $"pixel {i / 3} is not one of the four registers");
  }
}
