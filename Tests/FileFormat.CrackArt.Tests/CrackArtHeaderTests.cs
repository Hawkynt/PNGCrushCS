using System;
using System.Buffers.Binary;
using FileFormat.CrackArt;

namespace FileFormat.CrackArt.Tests;

[TestFixture]
public sealed class CrackArtHeaderTests {

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesFlagsAndPalette() {
    var palette = new short[16];
    for (var i = 0; i < 16; ++i)
      palette[i] = (short)(i * 0x111 & 0x777);

    var buffer = new byte[CrackArtHeader.GetDataOffset(CrackArtResolution.Low)];
    CrackArtHeader.Write(buffer, isCompressed: true, CrackArtResolution.Low, palette);

    Assert.That(CrackArtHeader.TryRead(buffer, out var isCompressed, out var resolution), Is.True);
    Assert.Multiple(() => {
      Assert.That(isCompressed, Is.True);
      Assert.That(resolution, Is.EqualTo(CrackArtResolution.Low));
      Assert.That(CrackArtHeader.ReadPalette(buffer, resolution), Is.EqualTo(palette));
    });
  }

  [Test]
  [Category("Unit")]
  public void Write_EmitsTheCaSignatureAndFlagBytes() {
    var buffer = new byte[CrackArtHeader.GetDataOffset(CrackArtResolution.Medium)];
    CrackArtHeader.Write(buffer, isCompressed: false, CrackArtResolution.Medium, new short[4]);

    Assert.Multiple(() => {
      Assert.That(buffer[0], Is.EqualTo((byte)'C'));
      Assert.That(buffer[1], Is.EqualTo((byte)'A'));
      Assert.That(buffer[2], Is.EqualTo(0), "compression flag");
      Assert.That(buffer[3], Is.EqualTo((byte)CrackArtResolution.Medium));
    });
  }

  [Test]
  [Category("Unit")]
  public void TryRead_ParsesKnownValues() {
    var data = new byte[CrackArtHeader.GetDataOffset(CrackArtResolution.Medium)];
    data[0] = (byte)'C';
    data[1] = (byte)'A';
    data[2] = 1; // compressed
    data[3] = (byte)CrackArtResolution.Medium;
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(CrackArtHeader.PaletteOffset + 0), 0x777);
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(CrackArtHeader.PaletteOffset + 2), 0x700);
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(CrackArtHeader.PaletteOffset + 4), 0x070);
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(CrackArtHeader.PaletteOffset + 6), 0x007);

    Assert.That(CrackArtHeader.TryRead(data, out var isCompressed, out var resolution), Is.True);
    var palette = CrackArtHeader.ReadPalette(data, resolution);

    Assert.Multiple(() => {
      Assert.That(isCompressed, Is.True);
      Assert.That(resolution, Is.EqualTo(CrackArtResolution.Medium));
      Assert.That(palette[0], Is.EqualTo(0x777));
      Assert.That(palette[1], Is.EqualTo(0x700));
      Assert.That(palette[2], Is.EqualTo(0x070));
      Assert.That(palette[3], Is.EqualTo(0x007));
    });
  }

  [Test]
  [Category("Unit")]
  public void TryRead_RejectsDataWithoutTheCaSignature()
    => Assert.That(CrackArtHeader.TryRead(new byte[64], out _, out _), Is.False);

  /// <summary>Where the bitmap starts depends on how many palette entries the resolution stores.</summary>
  [Test]
  [Category("Unit")]
  public void DataOffset_FollowsResolution() {
    Assert.Multiple(() => {
      Assert.That(CrackArtHeader.GetDataOffset(CrackArtResolution.Low), Is.EqualTo(36));
      Assert.That(CrackArtHeader.GetDataOffset(CrackArtResolution.Medium), Is.EqualTo(12));
      Assert.That(CrackArtHeader.GetDataOffset(CrackArtResolution.High), Is.EqualTo(4));
    });
  }
}
