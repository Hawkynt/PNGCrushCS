using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.JovianVi.Tests;

/// <summary>
/// A Jovian VI screen: a header stating where its two parts are, then a palette and an index a pixel.
/// </summary>
/// <remarks>
/// The layout here was not taken from a description — there is none to be had. It was deduced from a
/// file out of a public archive of format samples and then checked against another tool's decode of
/// that same file, which matched to the byte across all 192000 samples. These tests assert the parts
/// of that layout a reader depends on.
/// </remarks>
[TestFixture]
public sealed class JovianViTests {

  private static byte[] _Sample(int width, int height) {
    var data = new byte[JovianViFile.HeaderSize + JovianViFile.PaletteSize + width * height];
    data[0] = (byte)'V';
    data[1] = (byte)'I';
    data[2] = (byte)'0';
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(3), (ushort)width);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(5), (ushort)height);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(12), JovianViFile.HeaderSize);
    BinaryPrimitives.WriteUInt16LittleEndian(
      data.AsSpan(14), JovianViFile.HeaderSize + JovianViFile.PaletteSize);

    // Entry nine is the one whose widening was checked against the reference decode.
    data[JovianViFile.HeaderSize + 9 * 3] = 51;
    data[JovianViFile.HeaderSize + 9 * 3 + 1] = 6;
    data[JovianViFile.HeaderSize + 9 * 3 + 2] = 10;
    data[JovianViFile.HeaderSize + 255 * 3] = 63;
    data[JovianViFile.HeaderSize + 255 * 3 + 1] = 63;
    data[JovianViFile.HeaderSize + 255 * 3 + 2] = 63;

    data[JovianViFile.HeaderSize + JovianViFile.PaletteSize] = 9;
    data[JovianViFile.HeaderSize + JovianViFile.PaletteSize + 1] = 255;
    return data;
  }

  [Test]
  [Category("Unit")]
  public void Read_TakesItsSizeAndItsTwoOffsetsFromTheHeader() {
    var file = JovianViReader.FromBytes(_Sample(320, 200));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(320));
      Assert.That(file.Height, Is.EqualTo(200));
      Assert.That(file.Palette, Has.Length.EqualTo(768));
      Assert.That(file.PixelData, Has.Length.EqualTo(64000));
    });
  }

  [Test]
  [Category("Unit")]
  public void Decoded_ScalesTheSixBitPaletteRatherThanRepeatingItsTopBits() {
    var image = JovianViFile.ToRawImage(JovianViReader.FromBytes(_Sample(320, 200)));

    Assert.Multiple(() => {
      // 51 of 63 is 206 by scaling and 207 by repetition; the reference decode gives 206.
      Assert.That(image.Palette![9 * 3], Is.EqualTo(206));
      Assert.That(image.Palette[9 * 3 + 1], Is.EqualTo(24));
      Assert.That(image.Palette[9 * 3 + 2], Is.EqualTo(40));
      Assert.That(image.Palette[255 * 3], Is.EqualTo(255), "the top of the range reaches white");
    });
  }

  [Test]
  [Category("Unit")]
  public void Read_FollowsTheStatedOffsetsRatherThanAssumingThem() {
    // A file with sixteen spare bytes between the header and the palette still reads, because the
    // header says where each part is.
    var original = _Sample(8, 4);
    var moved = new byte[original.Length + 16];
    original.AsSpan(0, JovianViFile.HeaderSize).CopyTo(moved);
    original.AsSpan(JovianViFile.HeaderSize).CopyTo(moved.AsSpan(JovianViFile.HeaderSize + 16));
    BinaryPrimitives.WriteUInt16LittleEndian(moved.AsSpan(12), JovianViFile.HeaderSize + 16);
    BinaryPrimitives.WriteUInt16LittleEndian(
      moved.AsSpan(14), JovianViFile.HeaderSize + 16 + JovianViFile.PaletteSize);

    var file = JovianViReader.FromBytes(moved);
    Assert.That(file.PixelData[0], Is.EqualTo(9));
  }

  [Test]
  [Category("Unit")]
  public void Read_RefusesSomethingThatIsNotOne()
    => Assert.Throws<InvalidDataException>(() => JovianViReader.FromBytes(new byte[64]));

  [Test]
  [Category("Unit")]
  public void Read_RefusesAFileTooShortForWhatItsHeaderClaims() {
    var data = _Sample(320, 200);
    Assert.Throws<InvalidDataException>(() => JovianViReader.FromBytes(data[..(data.Length - 1)]));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_KeepsThePaletteAndThePixels() {
    var pixels = new byte[64 * 32 * 3];
    for (var i = 0; i < pixels.Length; i += 3) {
      pixels[i] = (byte)(i % 252);
      pixels[i + 1] = (byte)(i % 189);
      pixels[i + 2] = (byte)(i % 126);
    }

    var original = new RawImage { Width = 64, Height = 32, Format = PixelFormat.Rgb24, PixelData = pixels };
    var file = JovianViFile.FromRawImage(original);
    var restored = JovianViReader.FromBytes(JovianViWriter.ToBytes(file));

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(64));
      Assert.That(restored.Height, Is.EqualTo(32));
      Assert.That(restored.Palette, Is.EqualTo(file.Palette));
      Assert.That(restored.PixelData, Is.EqualTo(file.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void Written_KeepsEveryPaletteChannelInsideTheConvertersRange() {
    var pixels = new byte[] { 255, 255, 255, 0, 0, 0 };
    var image = new RawImage { Width = 2, Height = 1, Format = PixelFormat.Rgb24, PixelData = pixels };
    var bytes = JovianViWriter.ToBytes(JovianViFile.FromRawImage(image));

    for (var i = JovianViFile.HeaderSize; i < JovianViFile.HeaderSize + JovianViFile.PaletteSize; ++i)
      Assert.That(bytes[i], Is.LessThanOrEqualTo(JovianViFile.ChannelMax), $"palette byte {i}");
  }
}
