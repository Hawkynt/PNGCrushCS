using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Core;
using FileFormat.Fpx;
using FileFormat.PowerPoint;

namespace FileFormat.PowerPoint.Tests;

[TestFixture]
public sealed class PowerPointBinaryPresentationTests {

  [TestCase(".ppt")]
  [TestCase(".pps")]
  [TestCase(".pot")]
  [Category("Integration")]
  public void Writer_CreatesCompleteBinaryPresentationAndRoundTrips(string extension) {
    var source = _Picture(19, 11);
    var bytes = _Write(PowerPointFile.FromRawImage(source, extension));

    Assert.That(CompoundFile.HasSignature(bytes), Is.True);
    var compound = new CompoundFile(bytes);
    var streams = compound.Streams().Where(pair => pair.Value.Type == CompoundFile.EntryStream).ToArray();
    var document = compound.Read(streams.Single(pair => pair.Key.Equals("/PowerPoint Document", StringComparison.OrdinalIgnoreCase)).Value);
    var currentUser = compound.Read(streams.Single(pair => pair.Key.Equals("/Current User", StringComparison.OrdinalIgnoreCase)).Value);
    var pictures = compound.Read(streams.Single(pair => pair.Key.Equals("/Pictures", StringComparison.OrdinalIgnoreCase)).Value);

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(document), Is.EqualTo(0x000F), "DocumentContainer recVer/instance");
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(document.AsSpan(2)), Is.EqualTo(0x03E8), "DocumentContainer recType");
      Assert.That(currentUser, Is.Not.Empty, "Current User stream");
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(pictures), Is.EqualTo(PowerPointFile.PngBlipVersionAndInstance));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(pictures.AsSpan(2)), Is.EqualTo(PowerPointFile.PngBlipType));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(document.AsSpan(980)), Is.EqualTo((uint)pictures.Length), "persisted picture size");
      Assert.That(document.AsSpan(962, 16).SequenceEqual(pictures.AsSpan(PowerPointFile.RecordHeaderSize, 16)), Is.True, "persisted BLIP UID");
    });

    _AssertSame(source, PowerPointFile.ToRawImage(_Read(bytes)));
  }

  [Test]
  [Category("Integration")]
  public void Writer_FitsPortraitPictureInsideSlideAnchorWithoutChangingPixels() {
    var source = _Picture(3, 101);
    var bytes = _Write(PowerPointFile.FromRawImage(source, ".ppt"));
    var compound = new CompoundFile(bytes);
    var documentEntry = compound.Streams().Single(pair => pair.Key.Equals("/PowerPoint Document", StringComparison.OrdinalIgnoreCase));
    var document = compound.Read(documentEntry.Value);

    var left = BinaryPrimitives.ReadInt16LittleEndian(document.AsSpan(48886));
    var top = BinaryPrimitives.ReadInt16LittleEndian(document.AsSpan(48888));
    var right = BinaryPrimitives.ReadInt16LittleEndian(document.AsSpan(48890));
    var bottom = BinaryPrimitives.ReadInt16LittleEndian(document.AsSpan(48892));
    Assert.Multiple(() => {
      Assert.That(right - left, Is.LessThanOrEqualTo(1152));
      Assert.That(bottom - top, Is.LessThanOrEqualTo(1152));
      Assert.That(bottom, Is.GreaterThan(top));
    });

    _AssertSame(source, PowerPointFile.ToRawImage(_Read(bytes)));
  }

  private static byte[] _Write(PowerPointFile file) => _WriteViaContract(file);

  private static PowerPointFile _Read(ReadOnlySpan<byte> bytes) => _ReadViaContract<PowerPointFile>(bytes);

  private static byte[] _WriteViaContract<T>(T file)
    where T : IImageFormatWriter<T>
    => T.ToBytes(file);

  private static T _ReadViaContract<T>(ReadOnlySpan<byte> bytes)
    where T : IImageFormatReader<T>
    => T.FromSpan(bytes);

  private static RawImage _Picture(int width, int height) {
    var pixels = new byte[checked(width * height * 3)];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      pixels[at] = (byte)(x * 29 + y * 17);
      pixels[at + 1] = (byte)(x * 7 + y * 41);
      pixels[at + 2] = (byte)(x * 31 + y * 13);
    }
    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  private static void _AssertSame(RawImage expected, RawImage actual) {
    Assert.Multiple(() => {
      Assert.That((actual.Width, actual.Height), Is.EqualTo((expected.Width, expected.Height)));
      Assert.That(actual.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(actual.PixelData, Is.EqualTo(expected.PixelData));
    });
  }
}
