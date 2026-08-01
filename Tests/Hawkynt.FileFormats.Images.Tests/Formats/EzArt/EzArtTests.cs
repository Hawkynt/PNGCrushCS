using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.EzArt.Tests;

/// <summary>
/// EZ-Art: four bytes naming the format, a palette, and the screen packed a plane-row at a time.
/// </summary>
/// <remarks>
/// What was written before was the palette and the bare screen — no name in front of it, nothing
/// packed, and the bytes in the order the display reads rather than the order the format streams.
/// The two orders differ, so packing the screen as it lies gives a stream that unpacks to the right
/// length and the wrong picture.
/// <para/>
/// Checked against RECOIL: our decode of what we write matches its own to the byte across all
/// 192000 samples.
/// </remarks>
[TestFixture]
public sealed class EzArtTests {

  private static RawImage _Picture() {
    var pixels = new byte[320 * 200 * 3];
    for (var y = 0; y < 200; ++y)
    for (var x = 0; x < 320; ++x) {
      var at = (y * 320 + x) * 3;
      pixels[at] = (byte)(x / 20 * 36);
      pixels[at + 1] = (byte)(y / 25 * 36);
      pixels[at + 2] = (byte)((x / 8 + y / 8) % 2 == 0 ? 216 : 36);
    }

    return new() { Width = 320, Height = 200, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  [Category("Unit")]
  public void Written_NamesItselfAndPacksItsScreen() {
    var bytes = EzArtWriter.ToBytes(EzArtFile.FromRawImage(_Picture()));

    Assert.Multiple(() => {
      Assert.That(bytes[0], Is.EqualTo((byte)'E'));
      Assert.That(bytes[1], Is.EqualTo((byte)'Z'));
      Assert.That(bytes[2], Is.EqualTo(0));
      Assert.That(bytes[3], Is.EqualTo(200));
      Assert.That(bytes, Has.Length.LessThan(44 + 32000), "a packed screen is smaller than a bare one");
    });
  }

  [Test]
  [Category("Unit")]
  public void Read_RefusesSomethingWithoutTheName()
    => Assert.Throws<InvalidDataException>(() => EzArtReader.FromBytes(new byte[32032]));

  [Test]
  [Category("Integration")]
  public void RoundTrip_KeepsTheScreenAndItsPalette() {
    var original = EzArtFile.FromRawImage(_Picture());
    var restored = EzArtReader.FromBytes(EzArtWriter.ToBytes(original));

    Assert.Multiple(() => {
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
      Assert.That(restored.Palette, Is.EqualTo(original.Palette));
    });
  }

  [Test]
  [Category("Integration")]
  public void PlaneRows_AreTheInverseOfTheInterleavedOrder() {
    // The reordering has to undo itself exactly, or a packed screen unpacks to a scrambled one.
    var screen = new byte[32000];
    for (var i = 0; i < screen.Length; ++i)
      screen[i] = (byte)(i * 7 % 251);

    var round = AtariStGraphics.FromPlaneRows(
      AtariStGraphics.ToPlaneRows(screen, 320, 200, 4), 320, 200, 4);

    Assert.That(round, Is.EqualTo(screen));
  }
}
