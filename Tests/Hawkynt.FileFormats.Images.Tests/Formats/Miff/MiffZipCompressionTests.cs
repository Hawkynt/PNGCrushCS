using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using FileFormat.Core;
using FileFormat.Miff;
using Hawkynt.FileFormats.Images.Tests;

namespace FileFormat.Miff.Tests;

/// <summary>
/// A Zip payload is a zlib stream cut into one length-prefixed chunk per row, not a raw deflate one.
/// </summary>
/// <remarks>
/// The payload was handed straight to a raw <see cref="DeflateStream"/>, which has no zlib header to
/// find, so every Zip-compressed MIFF failed outright — one of eleven reference samples would not
/// decode at all.
/// <para/>
/// It is not a plain zlib stream either, which is the half-answer that looks right. ImageMagick
/// writes <c>version=1.0</c> on the id line, and for any version above zero its reader takes a
/// four-byte big-endian length before each row and inflates that chunk on its own. Its writer
/// flushes the deflater at the end of every row, so each chunk ends <c>00 00 FF FF</c> and the
/// stream is never finished — there is no final block and no Adler-32 trailer to reach.
/// <para/>
/// Measured on ImageMagick's own 61x37 file: the payload is 716 bytes in 37 chunks, one a row, whose
/// concatenation inflates to exactly 13542 bytes — 61 x 37 x 3 samples of two bytes. Handing
/// ImageMagick the three other shapes settles that nothing simpler will do: a plain zlib stream is
/// refused whether or not a version is stated, and chunks without a version are refused too. Only
/// the chunked form with <c>version=1.0</c> is read.
/// </remarks>
[TestFixture]
public sealed class MiffZipCompressionTests {

  private static byte[] _Rgb(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      pixels[at] = (byte)(x * 7);
      pixels[at + 1] = (byte)(y * 20);
      pixels[at + 2] = (byte)(255 - x * 5);
    }

    return pixels;
  }

  /// <summary>Builds the payload the way ImageMagick's writer lays one out.</summary>
  private static byte[] _ChunkedZlib(byte[] pixels, int bytesPerRow) {
    using var output = new MemoryStream();
    using var payload = new MemoryStream();

    using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true)) {
      var taken = 0;
      for (var at = 0; at < pixels.Length; at += bytesPerRow) {
        zlib.Write(pixels, at, Math.Min(bytesPerRow, pixels.Length - at));
        zlib.Flush();

        var produced = output.ToArray();
        var chunk = produced[taken..];
        taken = produced.Length;

        var length = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)chunk.Length);
        payload.Write(length);
        payload.Write(chunk);
      }
    }

    return payload.ToArray();
  }

  private static byte[] _BuildZipMiff(int width, int height, byte[] pixels, string version = " version=1.0") {
    var header = Encoding.ASCII.GetBytes(
      $"id=ImageMagick{version}\n"
      + "class=DirectClass colors=0 alpha-trait=Undefined\n"
      + $"columns={width} rows={height} depth=8\n"
      + "type=TrueColor\ncolorspace=sRGB\ncompression=Zip  quality=0\n"
      + "\f\n:\x1a");

    var payload = _ChunkedZlib(pixels, width * 3);
    var data = new byte[header.Length + payload.Length];
    header.CopyTo(data, 0);
    payload.CopyTo(data, header.Length);
    return data;
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ChunkedZlibPayload_IsInflated() {
    var pixels = _Rgb(9, 4);
    var result = MiffReader.FromBytes(_BuildZipMiff(9, 4, pixels));

    Assert.That(result.PixelData, Is.EqualTo(pixels));
  }

  /// <summary>One chunk a row, so a file of one row is still length-prefixed.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_SingleRow_IsInflated() {
    var pixels = _Rgb(5, 1);
    var result = MiffReader.FromBytes(_BuildZipMiff(5, 1, pixels));

    Assert.That(result.PixelData, Is.EqualTo(pixels));
  }

  /// <summary>A payload that is not a zlib stream is refused, not padded with zeroes.</summary>
  /// <remarks>
  /// The old reading took whatever the deflate stream gave it and copied that into a buffer of the
  /// right size, so a payload it could not read at all would have produced a black picture rather
  /// than a complaint had it not thrown first.
  /// </remarks>
  [Test]
  [Category("Unit")]
  public void FromBytes_PayloadThatIsNotZlib_IsRefused() {
    var header = Encoding.ASCII.GetBytes(
      "id=ImageMagick version=1.0\n"
      + "class=DirectClass colors=0 alpha-trait=Undefined\n"
      + "columns=4 rows=2 depth=8\n"
      + "type=TrueColor\ncolorspace=sRGB\ncompression=Zip\n"
      + "\f\n:\x1a");

    byte[] payload = [0x00, 0x00, 0x00, 0x06, 0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x11];
    var data = new byte[header.Length + payload.Length];
    header.CopyTo(data, 0);
    payload.CopyTo(data, header.Length);

    Assert.That(() => MiffReader.FromBytes(data), Throws.InstanceOf<InvalidDataException>());
  }

  /// <summary>A chunk stating more bytes than the file holds is refused.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_ChunkLongerThanTheFile_IsRefused() {
    var header = Encoding.ASCII.GetBytes(
      "id=ImageMagick version=1.0\n"
      + "class=DirectClass colors=0 alpha-trait=Undefined\n"
      + "columns=4 rows=2 depth=8\n"
      + "type=TrueColor\ncolorspace=sRGB\ncompression=Zip\n"
      + "\f\n:\x1a");

    byte[] payload = [0x7F, 0xFF, 0xFF, 0xFF, 0x78, 0xDA];
    var data = new byte[header.Length + payload.Length];
    header.CopyTo(data, 0);
    payload.CopyTo(data, header.Length);

    Assert.That(() => MiffReader.FromBytes(data), Throws.InstanceOf<InvalidDataException>());
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Zip_IsReadBackByUs() {
    var pixels = _Rgb(23, 9);
    var bytes = MiffWriter.ToBytes(new MiffFile {
      Width = 23, Height = 9, Depth = 8,
      ColorClass = MiffColorClass.DirectClass, Compression = MiffCompression.Zip,
      Colorspace = "sRGB", Type = "TrueColor", PixelData = pixels,
    });

    var restored = MiffReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Compression, Is.EqualTo(MiffCompression.Zip));
      Assert.That(restored.PixelData, Is.EqualTo(pixels));
    });
  }

  /// <summary>What we write must be smaller than what it stands for, or it is not compression.</summary>
  [Test]
  [Category("Unit")]
  public void ToBytes_Zip_IsSmallerThanThePixels() {
    var pixels = new byte[61 * 37 * 3];
    var bytes = MiffWriter.ToBytes(new MiffFile {
      Width = 61, Height = 37, Depth = 8,
      ColorClass = MiffColorClass.DirectClass, Compression = MiffCompression.Zip,
      Colorspace = "sRGB", Type = "TrueColor", PixelData = pixels,
    });

    Assert.That(bytes.Length, Is.LessThan(pixels.Length / 2));
  }

  /// <summary>ImageMagick reads its own file the same way we do — the point of the whole change.</summary>
  [Test]
  [Category("Conformance")]
  public void ImageMagicksOwnZipFile_ReadsTheSameAsImageMagickReadsIt() {
    var directory = Directory.CreateTempSubdirectory("miffzipread");
    try {
      var path = Path.Combine(directory.FullName, "sample.miff");
      var reference = Path.Combine(directory.FullName, "sample.rgb");

      using (var write = ExternalTool.StartOrIgnore("magick", $"-size 61x37 gradient:blue-yellow -compress Zip \"{path}\"")) {
        var complaint = write.StandardError.ReadToEnd().Trim();
        write.WaitForExit();
        if (write.ExitCode != 0)
          Assert.Fail($"ImageMagick would not write a Zip MIFF: {complaint}");
      }

      using (var read = ExternalTool.StartOrIgnore("magick", $"\"{path}\" -depth 8 RGB:\"{reference}\"")) {
        var complaint = read.StandardError.ReadToEnd().Trim();
        read.WaitForExit();
        if (read.ExitCode != 0)
          Assert.Fail($"ImageMagick would not read its own Zip MIFF: {complaint}");
      }

      var ours = MiffFile.ToRawImage(MiffReader.FromFile(new(path)));

      Assert.Multiple(() => {
        Assert.That(ours.Width, Is.EqualTo(61));
        Assert.That(ours.Height, Is.EqualTo(37));
        Assert.That(ours.PixelData, Is.EqualTo(File.ReadAllBytes(reference)));
      });
    } finally {
      try { directory.Delete(recursive: true); } catch { /* best effort */ }
    }
  }

  /// <summary>A palette picture compressed this way is read by ImageMagick too.</summary>
  /// <remarks>
  /// A MiffFile assembled from an indexed image states <c>type=TrueColor</c> beside
  /// <c>class=PseudoClass</c>, which is one sample a pixel and not three. The row is therefore taken
  /// from the payload and the row count rather than counted up from the channels.
  /// <para/>
  /// Chunk boundaries turn out not to be load-bearing: ImageMagick takes the next chunk only when its
  /// inflater has run out of input, not once per row, so it reads this file whichever width the rows
  /// were cut at. Measured — the test passes with the row width three times too large as well. It is
  /// kept because it is the only thing here that asks ImageMagick about a compressed palette file at
  /// all.
  /// </remarks>
  [Test]
  [Category("Conformance")]
  public void SomethingElseReadsTheZipPaletteFileWeWrite() {
    const int WIDTH = 12;
    const int HEIGHT = 9;

    var palette = new byte[] { 255, 0, 0, 0, 255, 0, 0, 0, 255, 128, 128, 128 };
    var indices = new byte[WIDTH * HEIGHT];
    for (var i = 0; i < indices.Length; ++i)
      indices[i] = (byte)(i % 4);

    var bytes = MiffWriter.ToBytes(new MiffFile {
      Width = WIDTH, Height = HEIGHT, Depth = 8,
      ColorClass = MiffColorClass.PseudoClass, Compression = MiffCompression.Zip,
      Colorspace = "sRGB", Type = "TrueColor", PixelData = indices, Palette = palette,
    });

    var expected = new byte[indices.Length * 3];
    for (var i = 0; i < indices.Length; ++i)
      Array.Copy(palette, indices[i] * 3, expected, i * 3, 3);

    var directory = Directory.CreateTempSubdirectory("miffzippal");
    try {
      var path = Path.Combine(directory.FullName, "sample.miff");
      var readBack = Path.Combine(directory.FullName, "sample.ppm");
      File.WriteAllBytes(path, bytes);

      using var magick = ExternalTool.StartOrIgnore("magick", $"\"{path}\" -depth 8 \"{readBack}\"");
      var complaint = magick.StandardError.ReadToEnd().Trim();
      magick.WaitForExit();

      if (magick.ExitCode != 0)
        Assert.Fail($"ImageMagick refused the Zip palette MIFF we wrote: {complaint}");

      var written = File.ReadAllBytes(readBack);
      var header = Encoding.ASCII.GetBytes($"P6\n{WIDTH} {HEIGHT}\n255\n");
      Assert.That(written.Skip(header.Length), Is.EqualTo(expected));
    } finally {
      try { directory.Delete(recursive: true); } catch { /* best effort */ }
    }
  }

  /// <summary>And reads the one we write, which is the only proof it is ImageMagick's layout.</summary>
  [Test]
  [Category("Conformance")]
  public void SomethingElseReadsTheZipFileWeWrite() {
    const int WIDTH = 37;
    const int HEIGHT = 11;

    var pixels = _Rgb(WIDTH, HEIGHT);
    var bytes = MiffWriter.ToBytes(new MiffFile {
      Width = WIDTH, Height = HEIGHT, Depth = 8,
      ColorClass = MiffColorClass.DirectClass, Compression = MiffCompression.Zip,
      Colorspace = "sRGB", Type = "TrueColor", PixelData = pixels,
    });

    var directory = Directory.CreateTempSubdirectory("miffzipwrite");
    try {
      var path = Path.Combine(directory.FullName, "sample.miff");
      var readBack = Path.Combine(directory.FullName, "sample.ppm");
      File.WriteAllBytes(path, bytes);

      using var magick = ExternalTool.StartOrIgnore("magick", $"\"{path}\" -depth 8 \"{readBack}\"");
      var complaint = magick.StandardError.ReadToEnd().Trim();
      magick.WaitForExit();

      if (magick.ExitCode != 0)
        Assert.Fail($"ImageMagick refused the Zip MIFF we wrote: {complaint}");

      var written = File.ReadAllBytes(readBack);
      var header = Encoding.ASCII.GetBytes($"P6\n{WIDTH} {HEIGHT}\n255\n");
      Assert.That(written.Skip(header.Length), Is.EqualTo(pixels), "ImageMagick read our Zip payload as different pixels");
    } finally {
      try { directory.Delete(recursive: true); } catch { /* best effort */ }
    }
  }
}
