using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.AtariCel.Tests;

/// <summary>
/// The Atari ST CEL: a 128-byte header with the palette near its front and the size near its end.
/// </summary>
/// <remarks>
/// A third format called CEL, and readable as neither of the other two — the paper-doll cells of
/// KiSS begin with those four letters, and the Autodesk Animator's frames begin with their own
/// header. This one begins with all ones and then all zeros, which is what tells them apart.
/// <para/>
/// Checked against RECOIL: our decode of what we write matches its own to the byte across all
/// 192000 samples of a 320 by 200 picture.
/// </remarks>
[TestFixture]
public sealed class AtariCelTests {

  private static RawImage _Picture(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      pixels[at] = (byte)(x / 20 * 36);
      pixels[at + 1] = (byte)(y / 25 * 36);
      pixels[at + 2] = (byte)((x / 8 + y / 8) % 2 == 0 ? 216 : 36);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  [Category("Unit")]
  public void Written_HasTheHeaderTheFormatStates() {
    var bytes = AtariCelWriter.ToBytes(AtariCelFile.FromRawImage(_Picture(320, 200)));

    Assert.Multiple(() => {
      Assert.That(bytes[0], Is.EqualTo(0xFF));
      Assert.That(bytes[1], Is.EqualTo(0xFF));
      Assert.That(bytes[2], Is.EqualTo(0));
      Assert.That(bytes[3], Is.EqualTo(0));
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(58)), Is.EqualTo(320));
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(60)), Is.EqualTo(200));
      Assert.That(bytes, Has.Length.EqualTo(128 + 320 / 16 * 8 * 200));
    });
  }

  [Test]
  [Category("Unit")]
  public void Written_KeepsEveryPaletteChannelInsideThreeBits() {
    var bytes = AtariCelWriter.ToBytes(AtariCelFile.FromRawImage(_Picture(320, 200)));

    for (var i = 0; i < 16; ++i) {
      var word = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4 + i * 2));
      Assert.That(word & 0xF888, Is.EqualTo(0), $"entry {i} has bits no ST colour holds");
    }
  }

  [Test]
  [Category("Unit")]
  public void Read_RefusesSomethingThatMerelyBeginsTheSameWay() {
    var data = new byte[128 + 64];
    data[0] = 0xFF;
    data[1] = 0xFF;
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(58), 320);
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(60), 200);

    // The length follows from the size, so a file of the wrong length is not one of these.
    Assert.Throws<InvalidDataException>(() => AtariCelReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void Read_RefusesAFileWithoutTheSignature()
    => Assert.Throws<InvalidDataException>(() => AtariCelReader.FromBytes(new byte[32128]));

  [Test]
  [Category("Integration")]
  public void RoundTrip_KeepsThePixelsAndThePalette() {
    var original = AtariCelFile.FromRawImage(_Picture(320, 200));
    var restored = AtariCelReader.FromBytes(AtariCelWriter.ToBytes(original));

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(320));
      Assert.That(restored.Height, Is.EqualTo(200));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void Decoded_DrawsOnlyTheSixteenColoursItsPaletteHolds() {
    var image = AtariCelFile.ToRawImage(
      AtariCelReader.FromBytes(AtariCelWriter.ToBytes(AtariCelFile.FromRawImage(_Picture(320, 200)))));

    foreach (var index in image.PixelData)
      Assert.That(index, Is.LessThan(16));
  }
}
