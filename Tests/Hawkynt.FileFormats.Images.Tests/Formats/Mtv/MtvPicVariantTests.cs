using System;
using System.IO;
using System.Text;
using FileFormat.Core;
using FileFormat.Mtv;
using Hawkynt.FileFormats.Images;

namespace FileFormat.Mtv.Tests;

/// <summary>
/// The MTV layout as XnView writes it, including under the <c>.pic</c> name.
/// </summary>
/// <remarks>
/// nconvert lists <c>[ray] Rayshade</c> and <c>[mtv] MTV Ray-Tracer</c> as separate formats, but the
/// two writers emit the same bytes: converting one 61x37 picture both ways gave files that compare
/// equal. So a <c>.pic</c> from <c>-out ray</c> is an MTV raster, not a Softimage PIC, and the
/// 0x5380F634 a Softimage reader looks for is never going to be there — the file opens with the
/// size written out in ASCII.
/// <para/>
/// nconvert also puts one 0x00 after the size line, and insists on it: strip that byte and it
/// answers "Can't read file" for a picture it had just written. The canonical MTV layout has no
/// such byte, so both spellings are read and neither is required.
/// </remarks>
[TestFixture]
public sealed class MtvPicVariantTests {

  /// <summary>Builds the layout nconvert writes: the size, a newline, one 0x00, then RGB.</summary>
  private static byte[] _BuildNconvertMtv(int width, int height, byte[] pixels) {
    var header = Encoding.ASCII.GetBytes($"{width} {height}\n\0");
    var data = new byte[header.Length + pixels.Length];
    Array.Copy(header, data, header.Length);
    Array.Copy(pixels, 0, data, header.Length, pixels.Length);
    return data;
  }

  private static byte[] _Ramp(int count) {
    var pixels = new byte[count];
    for (var i = 0; i < count; ++i)
      pixels[i] = (byte)(i * 11 % 256);

    return pixels;
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_NulAfterSizeLine_IsSkipped() {
    var pixels = new byte[] { 0, 0, 255, 7, 7, 248, 255, 255, 0 };
    var result = MtvReader.FromBytes(_BuildNconvertMtv(3, 1, pixels));

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(3));
      Assert.That(result.Height, Is.EqualTo(1));
      Assert.That(result.PixelData, Is.EqualTo(pixels));
    });
  }

  /// <summary>The 0x00 is nconvert's habit, not the format's: a file without it still reads.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_WithAndWithoutNul_DecodeIdentically() {
    var pixels = _Ramp(7 * 3 * 3);

    var padded = MtvReader.FromBytes(_BuildNconvertMtv(7, 3, pixels));
    var plain = MtvReader.FromBytes(MtvWriter.ToBytes(new MtvFile { Width = 7, Height = 3, PixelData = pixels }));

    Assert.That(padded.PixelData, Is.EqualTo(plain.PixelData));
  }

  /// <summary>
  /// A picture whose first sample is 0x00 must not lose it to the padding test.
  /// </summary>
  /// <remarks>
  /// The byte is only padding when the payload is one byte longer than the stated size; a black
  /// first pixel in an exactly-sized file is a sample.
  /// </remarks>
  [Test]
  [Category("Unit")]
  public void FromBytes_FirstSampleIsZero_IsKept() {
    var pixels = new byte[] { 0, 0, 0, 1, 2, 3 };
    var plain = MtvReader.FromBytes(MtvWriter.ToBytes(new MtvFile { Width = 2, Height = 1, PixelData = pixels }));

    Assert.That(plain.PixelData, Is.EqualTo(pixels));
  }

  /// <summary>A file that cannot fill the size it states is not an MTV raster.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_ShortPayload_IsRefused() {
    var data = _BuildNconvertMtv(8, 8, _Ramp(30));
    Assert.Throws<InvalidDataException>(() => MtvReader.FromBytes(data));
  }

  /// <summary>
  /// The size line carries two numbers and nothing else, which is what keeps this reader off the
  /// other formats that answer to <c>.pic</c>.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_ExtraTokensOnSizeLine_IsRefused() {
    var data = _BuildNconvertMtv(2, 1, _Ramp(6));
    var mangled = Encoding.ASCII.GetBytes("2 1 extra\n\0");
    var combined = new byte[mangled.Length + 6];
    Array.Copy(mangled, combined, mangled.Length);
    Array.Copy(data, data.Length - 6, combined, mangled.Length, 6);

    Assert.Throws<InvalidDataException>(() => MtvReader.FromBytes(combined));
  }

  /// <summary>A Softimage PIC keeps its own reader; MTV must not answer for it.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_SoftimagePicHeader_IsRefused() {
    var data = new byte[128];
    data[0] = 0x53;
    data[1] = 0x80;
    data[2] = 0xF6;
    data[3] = 0x34;

    Assert.Throws<InvalidDataException>(() => MtvReader.FromBytes(data));
  }

  /// <summary>Reading through the registry has to find it under <c>.pic</c> as well as <c>.mtv</c>.</summary>
  [Test]
  [Category("Integration")]
  public void Registry_ReadsNconvertPicture_UnderPicExtension() {
    var pixels = _Ramp(5 * 4 * 3);
    var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pic");
    try {
      File.WriteAllBytes(path, _BuildNconvertMtv(5, 4, pixels));

      var image = FormatRegistry.Read(new FileInfo(path));

      Assert.That(image, Is.Not.Null, "a .pic holding an MTV raster must not go unread");
      Assert.Multiple(() => {
        Assert.That(image!.Width, Is.EqualTo(5));
        Assert.That(image.Height, Is.EqualTo(4));
        Assert.That(image.EnsureFormat(PixelFormat.Rgb24).PixelData, Is.EqualTo(pixels));
      });
    } finally {
      if (File.Exists(path))
        File.Delete(path);
    }
  }
}
