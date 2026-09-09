using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using FileFormat.Pes;
using Hawkynt.FileFormats.Images;
using NUnit.Framework;

namespace FileFormat.Pes.Tests;

/// <summary>Brother PES embroidery files, including stitch serialization and raster digitization.</summary>
[TestFixture]
public sealed class PesTests {

  private static PesFile _Design() {
    var outline = new List<(int X, int Y)>();
    for (var i = 0; i <= 40; ++i)
      outline.Add((10 + i, 10));
    for (var i = 0; i <= 30; ++i)
      outline.Add((50, 10 + i));
    for (var i = 0; i <= 40; ++i)
      outline.Add((50 - i, 40));
    for (var i = 0; i <= 30; ++i)
      outline.Add((10, 40 - i));

    var diagonal = new List<(int X, int Y)>();
    for (var i = 0; i <= 30; ++i)
      diagonal.Add((10 + i, 10 + i));

    return new PesFile {
      Blocks = [
        new PesStitchBlock { ThreadIndex = 5, Color = 0xED171F, Points = outline.ToArray() },
        new PesStitchBlock { ThreadIndex = 2, Color = 0x0A55A3, Points = diagonal.ToArray() },
      ],
    };
  }

  [Test]
  public void EveryStitchComesBackWhereItWasPut() {
    var design = _Design();
    var again = PesReader.FromBytes(PesWriter.ToBytes(design));

    Assert.That(again.Blocks, Has.Count.EqualTo(design.Blocks.Count));
    for (var i = 0; i < design.Blocks.Count; ++i) {
      Assert.That(again.Blocks[i].ThreadIndex, Is.EqualTo(design.Blocks[i].ThreadIndex), $"block {i} thread");
      Assert.That(again.Blocks[i].Points, Is.EqualTo(design.Blocks[i].Points), $"block {i} stitches");
      Assert.That(again.Blocks[i].JumpIndices, Is.EqualTo(design.Blocks[i].JumpIndices), $"block {i} jumps");
    }
  }

  [Test]
  public void WriterUsesAStandardTruncatedPesV1WithEmbeddedPec() {
    var bytes = PesWriter.ToBytes(_Design());

    Assert.That(System.Text.Encoding.ASCII.GetString(bytes, 0, 8), Is.EqualTo("#PES0001"));
    var pecOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8, 4));
    Assert.That(pecOffset, Is.EqualTo(22), "the PES header stores an absolute PEC offset");
    Assert.That(System.Text.Encoding.ASCII.GetString(bytes, pecOffset, 3), Is.EqualTo("LA:"));
    Assert.That(bytes[pecOffset + 48], Is.EqualTo(1), "two colours are stored as N-1");

    var stitchBlock = pecOffset + 512;
    Assert.Multiple(() => {
      Assert.That(bytes[stitchBlock], Is.Zero);
      Assert.That(bytes[stitchBlock + 1], Is.Zero);
      Assert.That(bytes[stitchBlock + 5], Is.EqualTo(0x31));
      Assert.That(bytes[stitchBlock + 6], Is.EqualTo(0xFF));
      Assert.That(bytes[stitchBlock + 7], Is.EqualTo(0xF0));
    });

    var blockLength = bytes[stitchBlock + 2]
      | (bytes[stitchBlock + 3] << 8)
      | (bytes[stitchBlock + 4] << 16);
    Assert.That(blockLength, Is.GreaterThan(16));
    Assert.That(stitchBlock + blockLength, Is.LessThanOrEqualTo(bytes.Length));
  }

  /// <summary>
  /// The colour a block is sewn in is not in PES v1 itself; the PEC index into the thread chart is,
  /// and the display colour comes from that chart.
  /// </summary>
  [Test]
  public void ABlockTakesItsColourFromTheThreadChart() {
    var again = PesReader.FromBytes(PesWriter.ToBytes(_Design()));
    Assert.Multiple(() => {
      Assert.That(again.Blocks[0].Color, Is.EqualTo(0xED171F));
      Assert.That(again.Blocks[1].Color, Is.EqualTo(0x0A55A3));
    });
  }

  [Test]
  public void TheBoundsAreTheStitchesTheDesignReaches() {
    var again = PesReader.FromBytes(PesWriter.ToBytes(_Design()));
    Assert.Multiple(() => {
      Assert.That(again.MinX, Is.EqualTo(10));
      Assert.That(again.MinY, Is.EqualTo(10));
      Assert.That(again.MaxX, Is.EqualTo(50));
      Assert.That(again.MaxY, Is.EqualTo(40));
      Assert.That(again.Width, Is.EqualTo(41));
      Assert.That(again.Height, Is.EqualTo(31));
    });
  }

  [Test]
  public void TheDrawnPathPutsEachBlocksColourOnTheCanvas() {
    var design = _Design();
    var image = PesFile.ToRawImage(PesReader.FromBytes(PesWriter.ToBytes(design)));

    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(41));
      Assert.That(image.Height, Is.EqualTo(31));
      Assert.That(image.Format, Is.EqualTo(PixelFormat.Rgb24));
    });

    var edge = (0 * 41 + 20) * 3;
    Assert.That((image.PixelData[edge], image.PixelData[edge + 1], image.PixelData[edge + 2]),
      Is.EqualTo(((byte)0xED, (byte)0x17, (byte)0x1F)), "the outline's top edge");

    var diagonal = (15 * 41 + 15) * 3;
    Assert.That((image.PixelData[diagonal], image.PixelData[diagonal + 1], image.PixelData[diagonal + 2]),
      Is.EqualTo(((byte)0x0A, (byte)0x55, (byte)0xA3)), "the diagonal");

    var empty = (28 * 41 + 38) * 3;
    Assert.That((image.PixelData[empty], image.PixelData[empty + 1], image.PixelData[empty + 2]),
      Is.EqualTo(((byte)0xFF, (byte)0xFF, (byte)0xFF)), "unsewn ground");
  }

  [Test]
  public void JumpMovesDoNotDrawThreadAcrossDisconnectedRuns() {
    var design = new PesFile {
      Blocks = [
        new PesStitchBlock {
          ThreadIndex = 5,
          Color = 0xED171F,
          Points = [(0, 0), (2, 0), (8, 0), (10, 0)],
          JumpIndices = [0, 2],
        },
      ],
    };

    var again = PesReader.FromBytes(PesWriter.ToBytes(design));
    Assert.That(again.Blocks[0].JumpIndices, Is.EqualTo(new[] { 0, 2 }));

    var image = PesFile.ToRawImage(again);
    var gap = 5 * 3;
    Assert.That((image.PixelData[gap], image.PixelData[gap + 1], image.PixelData[gap + 2]),
      Is.EqualTo(((byte)0xFF, (byte)0xFF, (byte)0xFF)));
  }

  [Test]
  public void RegistryWriterDigitizesRasterRunsAndPreservesTheCanvas() {
    var source = new RawImage {
      Width = 4,
      Height = 2,
      Format = PixelFormat.Rgba32,
      PixelData = [
        0xED, 0x17, 0x1F, 0xFF,  0xED, 0x17, 0x1F, 0xFF,  0, 0, 0, 0,  0, 0, 0, 0,
        0, 0, 0, 0,               0x0A, 0x55, 0xA3, 0xFF,  0, 0, 0, 0,  0, 0, 0, 0,
      ],
    };

    var entry = FormatRegistry.GetEntry(ImageFormat.Pes);
    Assert.That(entry, Is.Not.Null);
    Assert.That(entry!.ConvertFromRawImage, Is.Not.Null);

    var bytes = entry.ConvertFromRawImage!(source);
    var image = PesFile.ToRawImage(PesReader.FromBytes(bytes));

    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(4));
      Assert.That(image.Height, Is.EqualTo(2));
      Assert.That((image.PixelData[0], image.PixelData[1], image.PixelData[2]),
        Is.EqualTo(((byte)0xED, (byte)0x17, (byte)0x1F)));
      var blue = (1 * 4 + 1) * 3;
      Assert.That((image.PixelData[blue], image.PixelData[blue + 1], image.PixelData[blue + 2]),
        Is.EqualTo(((byte)0x0A, (byte)0x55, (byte)0xA3)));
      var empty = (1 * 4 + 3) * 3;
      Assert.That((image.PixelData[empty], image.PixelData[empty + 1], image.PixelData[empty + 2]),
        Is.EqualTo(((byte)0xFF, (byte)0xFF, (byte)0xFF)));
    });
  }

  [Test]
  public void FullyTransparentRasterIsRefusedBecauseThereIsNothingToSew() {
    var source = new RawImage {
      Width = 2,
      Height = 2,
      Format = PixelFormat.Rgba32,
      PixelData = new byte[16],
    };

    Assert.Throws<ArgumentException>(() => PesFile.FromRawImage(source));
  }

  [Test]
  public void SomethingThatDoesNotBeginWithThePesMarkerIsRefused() {
    var bytes = PesWriter.ToBytes(_Design());
    bytes[1] = (byte)'X';
    Assert.Throws<InvalidDataException>(() => PesReader.FromBytes(bytes));
  }

  [Test]
  public void APesPointingItsPecSectionOutsideTheFileIsRefused() {
    var bytes = PesWriter.ToBytes(_Design());
    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8, 4), 1 << 24);
    Assert.Throws<InvalidDataException>(() => PesReader.FromBytes(bytes));
  }

  [Test]
  public void APesWithNoStitchesIsRefused() {
    var bytes = PesWriter.ToBytes(_Design());
    var pecOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8, 4));
    var stitchStart = pecOffset + 512 + 16;
    bytes[stitchStart] = 0xFF;
    bytes[stitchStart + 1] = 0x00;
    Assert.Throws<InvalidDataException>(() => PesReader.FromBytes(bytes));
  }
}
