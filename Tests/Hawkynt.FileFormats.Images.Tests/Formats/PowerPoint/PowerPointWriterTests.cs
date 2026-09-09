using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using FileFormat.Core;
using FileFormat.Png;

namespace FileFormat.PowerPoint.Tests;

[TestFixture]
public sealed class PowerPointWriterTests {

  private const uint _END_OF_CHAIN = 0xFFFFFFFE;
  private const uint _FAT_SECTOR = 0xFFFFFFFD;

  private static RawImage _Picture(int width = 17, int height = 9) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      pixels[at] = (byte)(x * 37 + y * 11);
      pixels[at + 1] = (byte)(x * 13 + y * 43);
      pixels[at + 2] = (byte)(x * 29 + y * 17);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [TestCase("", "31D6CFE0D16AE931B73C59D7E0C089C0")]
  [TestCase("a", "BDE52CB31DE33E46245E05FBDBD6FB24")]
  [TestCase("abc", "A448017AAF21D8525FC10AE87AA6729D")]
  [TestCase("message digest", "D9130A8164549FE818874806E1C7014B")]
  [Category("Unit")]
  public void BlipUid_MatchesRfc1320Md4Vectors(string text, string expected) {
    var actual = PowerPointWriter.ComputeBlipUid(Encoding.ASCII.GetBytes(text));

    Assert.That(Convert.ToHexString(actual), Is.EqualTo(expected));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ArbitraryRgbPicture_IsLossless() {
    var source = _Picture();

    var bytes = PowerPointWriter.ToBytes(PowerPointFile.FromRawImage(source));
    var actual = PowerPointFile.ToRawImage(PowerPointReader.FromBytes(bytes));

    Assert.Multiple(() => {
      Assert.That((actual.Width, actual.Height), Is.EqualTo((source.Width, source.Height)));
      Assert.That(actual.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(actual.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void Writer_OfficeArtRecordIsTheSpecifiedOneUidPngBlip() {
    var source = _Picture(5, 4);
    var expectedPng = PngWriter.ToBytes(PngFile.FromRawImage(source));
    var bytes = PowerPointWriter.ToBytes(PowerPointFile.FromRawImage(source));
    var blip = bytes[PowerPointFile.ScanStart..];

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(blip), Is.EqualTo(PowerPointFile.PngBlipVersionAndInstance));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(blip.AsSpan(2)), Is.EqualTo(PowerPointFile.PngBlipType));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(blip.AsSpan(4)), Is.EqualTo((uint)(expectedPng.Length + PowerPointFile.BlipPrefixSize)));
      Assert.That(blip[PowerPointFile.RecordHeaderSize + 16], Is.EqualTo(0xFF));
      Assert.That(
        blip.AsSpan(PowerPointFile.RecordHeaderSize + PowerPointFile.BlipPrefixSize, expectedPng.Length).ToArray(),
        Is.EqualTo(expectedPng));
    });
  }

  [Test]
  [Category("Unit")]
  public void Writer_CreatesVersion3CompoundFileWithPicturesAsARegularStream() {
    var bytes = PowerPointWriter.ToBytes(PowerPointFile.FromRawImage(_Picture(7, 3)));

    var fatSectorCount = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(44));
    var directorySector = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(48));
    var firstMiniFat = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(60));
    var miniFatCount = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(64));
    var firstFatSector = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(76));
    var directory = bytes.AsSpan(PowerPointFile.ScanStart + checked((int)directorySector) * 512, 512);
    var root = directory[..128].ToArray();
    var pictures = directory.Slice(128, 128).ToArray();
    var streamSize = BinaryPrimitives.ReadUInt64LittleEndian(pictures.AsSpan(120));
    var streamSectors = checked((int)((streamSize + 511) / 512));

    Assert.Multiple(() => {
      Assert.That(bytes.AsSpan(0, PowerPointFile.Signature.Length).SequenceEqual(PowerPointFile.Signature), Is.True);
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(24)), Is.EqualTo(0x003E));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(26)), Is.EqualTo(3));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(28)), Is.EqualTo(0xFFFE));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(30)), Is.EqualTo(9));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(32)), Is.EqualTo(6));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(56)), Is.EqualTo(4096));
      Assert.That(firstMiniFat, Is.EqualTo(_END_OF_CHAIN));
      Assert.That(miniFatCount, Is.Zero);
      Assert.That(fatSectorCount, Is.EqualTo(1));
      Assert.That(_DirectoryName(root), Is.EqualTo("Root Entry"));
      Assert.That(root[66], Is.EqualTo(5));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(root.AsSpan(76)), Is.EqualTo(1));
      Assert.That(_DirectoryName(pictures), Is.EqualTo("Pictures"));
      Assert.That(pictures[66], Is.EqualTo(2));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(pictures.AsSpan(116)), Is.Zero);
      Assert.That(streamSize, Is.GreaterThanOrEqualTo(4096));
      Assert.That(directorySector, Is.EqualTo((uint)streamSectors));
    });

    var fat = bytes.AsSpan(PowerPointFile.ScanStart + checked((int)firstFatSector) * 512, 512).ToArray();
    for (var sector = 0; sector < streamSectors - 1; ++sector)
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(fat.AsSpan(sector * sizeof(uint))), Is.EqualTo((uint)(sector + 1)), $"stream FAT entry {sector}");

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(fat.AsSpan((streamSectors - 1) * sizeof(uint))), Is.EqualTo(_END_OF_CHAIN));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(fat.AsSpan(checked((int)directorySector) * sizeof(uint))), Is.EqualTo(_END_OF_CHAIN));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(fat.AsSpan(checked((int)firstFatSector) * sizeof(uint))), Is.EqualTo(_FAT_SECTOR));
    });
  }

  [Test]
  [Category("Unit")]
  public void Writer_RejectsTruncatedPixelData() {
    var file = new PowerPointFile {
      Width = 4,
      Height = 3,
      PixelData = new byte[4 * 3 * 3 - 1],
    };

    Assert.Throws<InvalidDataException>(() => PowerPointWriter.ToBytes(file));
  }

  private static string _DirectoryName(ReadOnlySpan<byte> entry) {
    var byteLength = BinaryPrimitives.ReadUInt16LittleEndian(entry[64..]);
    return byteLength >= 2
      ? Encoding.Unicode.GetString(entry[..(byteLength - 2)])
      : string.Empty;
  }
}
