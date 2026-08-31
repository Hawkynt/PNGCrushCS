using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.DaliST.Tests;

[TestFixture]
public sealed class DaliSTConformanceTests {

  private static byte[] _File() {
    var data = new byte[DaliSTFile.ExpectedFileSize];
    for (var i = 0; i < 16; ++i)
      BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(DaliSTFile.PaletteOffset + i * 2), (short)(i * 0x111 & 0x777));
    for (var i = 0; i < DaliSTFile.ReservedSize; ++i)
      data[DaliSTFile.ReservedOffset + i] = (byte)(i * 37 + 11);
    for (var i = 0; i < DaliSTFile.PlanarDataSize; ++i)
      data[DaliSTFile.HeaderSize + i] = (byte)(i * 19 + 3);
    return data;
  }

  [Test]
  [Category("Unit")]
  public void Reader_RequiresExactPhysicalLength() {
    var valid = _File();
    Assert.Multiple(() => {
      Assert.Throws<InvalidDataException>(() => DaliSTReader.FromBytes(valid[..^1]));
      Assert.Throws<InvalidDataException>(() => DaliSTReader.FromBytes([.. valid, 0]));
    });
  }

  [Test]
  [Category("Unit")]
  public void Reader_RequiresZeroFileIdentifier() {
    var data = _File();
    data[3] = 1;
    Assert.Throws<InvalidDataException>(() => DaliSTReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void Reader_PreservesReservedHeaderBytesAndWriterRestoresThem() {
    var data = _File();
    var parsed = DaliSTReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(parsed.ReservedData, Is.EqualTo(data.AsSpan(DaliSTFile.ReservedOffset, DaliSTFile.ReservedSize).ToArray()));
      Assert.That(DaliSTWriter.ToBytes(parsed), Is.EqualTo(data));
    });
  }

  [TestCase(".sd0", DaliSTResolution.Low, 320, 200)]
  [TestCase(".SD1", DaliSTResolution.Medium, 640, 200)]
  [TestCase(".Sd2", DaliSTResolution.High, 640, 400)]
  [Category("Integration")]
  public void FileExtension_SelectsResolution(string extension, DaliSTResolution resolution, int width, int height) {
    var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + extension);
    try {
      File.WriteAllBytes(path, _File());
      var parsed = DaliSTReader.FromFile(new FileInfo(path));
      Assert.Multiple(() => {
        Assert.That(parsed.Resolution, Is.EqualTo(resolution));
        Assert.That(parsed.Width, Is.EqualTo(width));
        Assert.That(parsed.Height, Is.EqualTo(height));
      });
    } finally {
      if (File.Exists(path))
        File.Delete(path);
    }
  }

  [Test]
  [Category("Unit")]
  public void UnknownExtension_IsRejectedRatherThanSilentlyAssumedLow() {
    var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dat");
    try {
      File.WriteAllBytes(path, _File());
      Assert.Throws<ArgumentException>(() => DaliSTReader.FromFile(new FileInfo(path)));
    } finally {
      if (File.Exists(path))
        File.Delete(path);
    }
  }

  [Test]
  [Category("Unit")]
  public void InvalidResolution_IsRejectedRatherThanSilentlyAssumedLow()
    => Assert.Throws<ArgumentOutOfRangeException>(() => DaliSTReader.FromBytes(_File(), (DaliSTResolution)99));

  [Test]
  [Category("Unit")]
  public void Writer_UsesPublishedHeaderOffsets() {
    var file = DaliSTReader.FromBytes(_File());
    var bytes = DaliSTWriter.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadUInt32BigEndian(bytes), Is.Zero);
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(DaliSTFile.PaletteOffset)), Is.EqualTo(file.Palette[0]));
      Assert.That(bytes[DaliSTFile.ReservedOffset], Is.EqualTo(file.ReservedData![0]));
      Assert.That(bytes[DaliSTFile.HeaderSize], Is.EqualTo(file.PixelData[0]));
      Assert.That(bytes, Has.Length.EqualTo(32_128));
    });
  }

  [Test]
  [Category("Unit")]
  public void Writer_ZeroFillsReservedBytesForLegacyCallers() {
    var file = new DaliSTFile {
      Width = 320,
      Height = 200,
      Resolution = DaliSTResolution.Low,
      Palette = new short[16],
      PixelData = new byte[DaliSTFile.PlanarDataSize],
    };

    var bytes = DaliSTWriter.ToBytes(file);
    Assert.That(bytes.AsSpan(DaliSTFile.ReservedOffset, DaliSTFile.ReservedSize).ToArray(), Is.All.EqualTo((byte)0));
  }

  [Test]
  [Category("Unit")]
  public void Writer_RejectsMalformedModelInsteadOfPaddingOrClipping() {
    var valid = new DaliSTFile {
      Width = 320,
      Height = 200,
      Resolution = DaliSTResolution.Low,
      Palette = new short[16],
      PixelData = new byte[DaliSTFile.PlanarDataSize],
    };

    Assert.Multiple(() => {
      Assert.Throws<ArgumentException>(() => DaliSTWriter.ToBytes(valid with { Width = 640 }));
      Assert.Throws<ArgumentException>(() => DaliSTWriter.ToBytes(valid with { Palette = new short[15] }));
      Assert.Throws<ArgumentException>(() => DaliSTWriter.ToBytes(valid with { ReservedData = new byte[91] }));
      Assert.Throws<ArgumentException>(() => DaliSTWriter.ToBytes(valid with { PixelData = new byte[DaliSTFile.PlanarDataSize - 1] }));
    });
  }

  [Test]
  [Category("Unit")]
  public void HighResolution_UsesMachineMonochromePalette() {
    var file = DaliSTReader.FromBytes(_File(), DaliSTResolution.High) with {
      Palette = [0x0700, 0x0007, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
    };

    var image = DaliSTFile.ToRawImage(file);
    Assert.That(image.Palette, Is.EqualTo(AtariStGraphics.MonochromePalette()));
  }

  [TestCase(".sd0", 320, 200, DaliSTResolution.Low)]
  [TestCase(".sd1", 640, 200, DaliSTResolution.Medium)]
  [TestCase(".sd2", 640, 400, DaliSTResolution.High)]
  [Category("Unit")]
  public void ExtensionAwareAuthoring_SelectsTheNamedMode(string extension, int width, int height, DaliSTResolution resolution) {
    var rgb = new byte[width * height * 3];
    for (var i = 0; i < rgb.Length; i += 3) {
      rgb[i] = (byte)(i * 13);
      rgb[i + 1] = (byte)(i * 7);
      rgb[i + 2] = (byte)(i * 3);
    }

    var image = new RawImage { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
    var file = DaliSTFile.FromRawImage(image, extension);

    Assert.Multiple(() => {
      Assert.That(file.Resolution, Is.EqualTo(resolution));
      Assert.That(file.PixelData, Has.Length.EqualTo(DaliSTFile.PlanarDataSize));
      Assert.That(file.Palette, Has.Length.EqualTo(16));
    });
  }

  [Test]
  [Category("Unit")]
  public void ExtensionAwareAuthoring_RejectsMismatchedGeometry() {
    var image = new RawImage {
      Width = 320,
      Height = 200,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[320 * 200 * 3],
    };

    Assert.Throws<ArgumentException>(() => DaliSTFile.FromRawImage(image, ".sd2"));
  }
}
