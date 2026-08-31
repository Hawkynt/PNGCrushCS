using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.ArtDirector;
using FileFormat.Core;

namespace FileFormat.ArtDirector.Tests;

[TestFixture]
public sealed class ArtDirectorReaderTests {

  private static byte[] _BuildFile() {
    var data = new byte[ArtDirectorFile.ExpectedFileSize];
    for (var i = 0; i < ArtDirectorFile.PlanarDataSize; ++i)
      data[i] = (byte)(i * 17 + 3);

    for (var palette = 0; palette < ArtDirectorFile.StoredPaletteCount; ++palette)
      for (var color = 0; color < ArtDirectorFile.ColorsPerPalette; ++color)
        BinaryPrimitives.WriteUInt16BigEndian(
          data.AsSpan(ArtDirectorFile.PlanarDataSize + (palette * ArtDirectorFile.ColorsPerPalette + color) * 2),
          (ushort)((palette << 8) | color)
        );

    return data;
  }

  [Test]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ArtDirectorReader.FromBytes(null!));

  [Test]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ArtDirectorReader.FromFile(null!));

  [Test]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".art"));
    Assert.Throws<FileNotFoundException>(() => ArtDirectorReader.FromFile(missing));
  }

  [Test]
  public void Reader_RequiresExactPublished32512ByteLength() {
    Assert.Multiple(() => {
      Assert.Throws<InvalidDataException>(() => ArtDirectorReader.FromBytes(new byte[ArtDirectorFile.ExpectedFileSize - 1]));
      Assert.Throws<InvalidDataException>(() => ArtDirectorReader.FromBytes(new byte[ArtDirectorFile.ExpectedFileSize + 1]));
      Assert.Throws<InvalidDataException>(() => ArtDirectorReader.FromBytes(new byte[32_128]));
    });
  }

  [Test]
  public void Reader_ParsesScreenBeforePalettes() {
    var data = _BuildFile();
    var file = ArtDirectorReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(320));
      Assert.That(file.Height, Is.EqualTo(200));
      Assert.That(file.Resolution, Is.Zero);
      Assert.That(file.PixelData[0], Is.EqualTo(data[0]));
      Assert.That(file.PixelData[^1], Is.EqualTo(data[ArtDirectorFile.PlanarDataSize - 1]));
      Assert.That(file.PaletteCycle, Has.Length.EqualTo(256));
    });
  }

  [Test]
  public void Reader_PreservesEveryStoredPaletteWord() {
    var file = ArtDirectorReader.FromBytes(_BuildFile());

    Assert.That(file.PaletteCycle, Is.Not.Null);
    Assert.Multiple(() => {
      Assert.That(file.PaletteCycle![0], Is.EqualTo(0x0000));
      Assert.That(file.PaletteCycle[15], Is.EqualTo(0x000F));
      Assert.That(file.PaletteCycle[16], Is.EqualTo(0x0100));
      Assert.That(file.PaletteCycle[^1], Is.EqualTo(0x0F0F));
    });
  }

  [Test]
  public void Reader_ExposesEstablishedDisplayedPaletteSlot() {
    var file = ArtDirectorReader.FromBytes(_BuildFile());

    Assert.Multiple(() => {
      Assert.That(ArtDirectorFile.DisplayedPaletteIndex, Is.EqualTo(1));
      Assert.That(file.Palette[0], Is.EqualTo(0x0100));
      Assert.That(file.Palette[15], Is.EqualTo(0x010F));
    });
  }

  [Test]
  public void Writer_ProducesPublishedScreenFirstLayout() {
    var original = ArtDirectorReader.FromBytes(_BuildFile());
    var bytes = ArtDirectorWriter.ToBytes(original);

    Assert.Multiple(() => {
      Assert.That(bytes, Has.Length.EqualTo(32_512));
      Assert.That(bytes[..ArtDirectorFile.PlanarDataSize], Is.EqualTo(original.PixelData));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(ArtDirectorFile.PlanarDataSize)), Is.EqualTo(original.PaletteCycle![0]));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(bytes.Length - 2)), Is.EqualTo(original.PaletteCycle[^1]));
    });
  }

  [Test]
  public void RoundTrip_PreservesAllPaletteSlotsAndScreenMemory() {
    var original = ArtDirectorReader.FromBytes(_BuildFile());
    var restored = ArtDirectorReader.FromBytes(ArtDirectorWriter.ToBytes(original));

    Assert.Multiple(() => {
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
      Assert.That(restored.PaletteCycle, Is.EqualTo(original.PaletteCycle));
      Assert.That(restored.Palette, Is.EqualTo(original.Palette));
    });
  }

  [Test]
  public void Writer_WhenOnlyLegacyPaletteIsProvided_RepeatsItThroughAllSlots() {
    var palette = new short[16];
    for (var i = 0; i < palette.Length; ++i)
      palette[i] = (short)(0x0700 | i);

    var bytes = ArtDirectorWriter.ToBytes(new ArtDirectorFile {
      Palette = palette,
      PixelData = new byte[ArtDirectorFile.PlanarDataSize],
    });
    var restored = ArtDirectorReader.FromBytes(bytes);

    Assert.That(restored.PaletteCycle, Is.Not.Null);
    for (var slot = 0; slot < ArtDirectorFile.StoredPaletteCount; ++slot)
      Assert.That(
        restored.PaletteCycle!.AsSpan(slot * 16, 16).ToArray(),
        Is.EqualTo(palette),
        $"palette slot {slot}"
      );
  }

  [Test]
  public void Writer_ChangingDisplayedPalettePreservesOtherAnimationSlots() {
    var original = ArtDirectorReader.FromBytes(_BuildFile());
    var replacement = new short[16];
    Array.Fill(replacement, (short)0x0777);

    var restored = ArtDirectorReader.FromBytes(ArtDirectorWriter.ToBytes(original with { Palette = replacement }));

    Assert.That(restored.Palette, Is.EqualTo(replacement));
    Assert.Multiple(() => {
      Assert.That(restored.PaletteCycle![0], Is.EqualTo(original.PaletteCycle![0]));
      Assert.That(restored.PaletteCycle[32], Is.EqualTo(original.PaletteCycle[32]));
    });
  }

  [Test]
  public void Writer_RejectsInvalidGeometryResolutionPaletteRasterAndCycle() {
    var valid = new ArtDirectorFile {
      Palette = new short[16],
      PixelData = new byte[ArtDirectorFile.PlanarDataSize],
    };

    Assert.Multiple(() => {
      Assert.Throws<ArgumentException>(() => ArtDirectorWriter.ToBytes(valid with { Width = 640 }));
      Assert.Throws<ArgumentException>(() => ArtDirectorWriter.ToBytes(valid with { Resolution = 1 }));
      Assert.Throws<ArgumentException>(() => ArtDirectorWriter.ToBytes(valid with { Palette = new short[15] }));
      Assert.Throws<ArgumentException>(() => ArtDirectorWriter.ToBytes(valid with { PixelData = new byte[ArtDirectorFile.PlanarDataSize - 1] }));
      Assert.Throws<ArgumentException>(() => ArtDirectorWriter.ToBytes(valid with { PaletteCycle = new short[255] }));
    });
  }

  [Test]
  public void ToRawImage_IsAlwaysLowResolutionFourPlane() {
    var file = ArtDirectorReader.FromBytes(_BuildFile());
    var image = ArtDirectorFile.ToRawImage(file);

    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(320));
      Assert.That(image.Height, Is.EqualTo(200));
      Assert.That(image.Format, Is.EqualTo(PixelFormat.Indexed8));
      Assert.That(image.PaletteCount, Is.EqualTo(16));
    });
  }

  [Test]
  public void StreamReader_UsesCurrentPositionAndExactRemainingLength() {
    var payload = _BuildFile();
    using var stream = new MemoryStream(new byte[payload.Length + 13]);
    stream.Position = 13;
    stream.Write(payload);
    stream.Position = 13;

    var file = ArtDirectorReader.FromStream(stream);

    Assert.That(file.PixelData[0], Is.EqualTo(payload[0]));
  }
}
