using System.Collections.Generic;
using System.Linq;
using FileFormat.Png;

namespace FileFormat.Png.Tests;

[TestFixture]
public sealed class PngChunkReaderTests {

  [Test]
  [Category("Unit")]
  public void Parse_EmptyPng_HasNoChunks() {
    var file = new PngFile {
      Width = 1,
      Height = 1,
      BitDepth = 8,
      ColorType = PngColorType.RGB,
      PixelData = [new byte[3]]
    };

    var bytes = PngWriter.ToBytes(file);
    var reader = PngChunkReader.Parse(bytes);

    Assert.That(reader.HasChunks, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void Parse_WithGama_ClassifiedBeforePlte() {
    var gamaData = new byte[] { 0, 0, 177, 143 }; // gamma 0.45455
    var file = new PngFile {
      Width = 1,
      Height = 1,
      BitDepth = 8,
      ColorType = PngColorType.RGB,
      PixelData = [new byte[3]],
      ChunksBeforePlte = new List<PngChunk> { new("gAMA", gamaData) }
    };

    var bytes = PngWriter.ToBytes(file);
    var reader = PngChunkReader.Parse(bytes);

    Assert.That(reader.BeforePlte, Has.Count.EqualTo(1));
    Assert.That(reader.BeforePlte[0].Type, Is.EqualTo("gAMA"));
    Assert.That(reader.BeforePlte[0].Data, Is.EqualTo(gamaData));
  }

  [Test]
  [Category("Unit")]
  public void Parse_WithText_ClassifiedAfterIdat() {
    var textData = System.Text.Encoding.ASCII.GetBytes("Comment\0Hello");
    var file = new PngFile {
      Width = 1,
      Height = 1,
      BitDepth = 8,
      ColorType = PngColorType.RGB,
      PixelData = [new byte[3]],
      ChunksAfterIdat = new List<PngChunk> { new("tEXt", textData) }
    };

    var bytes = PngWriter.ToBytes(file);
    var reader = PngChunkReader.Parse(bytes);

    Assert.That(reader.AfterIdat, Has.Count.EqualTo(1));
    Assert.That(reader.AfterIdat[0].Type, Is.EqualTo("tEXt"));
    Assert.That(reader.AfterIdat[0].Data, Is.EqualTo(textData));
  }

  [Test]
  [Category("Unit")]
  public void Parse_WithPhys_ClassifiedBetween() {
    var physData = new byte[] { 0, 0, 0x0E, 0xC4, 0, 0, 0x0E, 0xC4, 1 }; // 3780 ppm, meter unit
    var file = new PngFile {
      Width = 1,
      Height = 1,
      BitDepth = 8,
      ColorType = PngColorType.RGB,
      PixelData = [new byte[3]],
      ChunksBetweenPlteAndIdat = new List<PngChunk> { new("pHYs", physData) }
    };

    var bytes = PngWriter.ToBytes(file);
    var reader = PngChunkReader.Parse(bytes);

    Assert.That(reader.BetweenPlteAndIdat, Has.Count.EqualTo(1));
    Assert.That(reader.BetweenPlteAndIdat[0].Type, Is.EqualTo("pHYs"));
    Assert.That(reader.BetweenPlteAndIdat[0].Data, Is.EqualTo(physData));
  }

  [Test]
  [Category("Unit")]
  public void Parse_HasChunks_TrueWhenPresent() {
    var gamaData = new byte[] { 0, 0, 177, 143 };
    var file = new PngFile {
      Width = 1,
      Height = 1,
      BitDepth = 8,
      ColorType = PngColorType.RGB,
      PixelData = [new byte[3]],
      ChunksBeforePlte = new List<PngChunk> { new("gAMA", gamaData) }
    };

    var bytes = PngWriter.ToBytes(file);
    var reader = PngChunkReader.Parse(bytes);

    Assert.That(reader.HasChunks, Is.True);
  }
}
