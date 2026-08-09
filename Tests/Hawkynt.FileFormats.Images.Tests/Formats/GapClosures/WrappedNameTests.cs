using System;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Images;

namespace Hawkynt.FileFormats.Images.Tests.GapClosures;

/// <summary>
/// Four of XnView's format names whose readers are, in its own binary, readers this library already
/// has. Its 567-entry format table pairs each name with the address of the function that reads it,
/// and four of the names in the coverage gap share their address with a mainstream format: "AAA
/// logo" (<c>.bpr</c>) with CompuServe GIF, "Optigraphics" (<c>.ctf</c>) with TIFF — as does its
/// neighbouring "Optigraphics Tiled" — "PhotoFrame" (<c>.frm</c>) with JPEG, and "Album"
/// (<c>.frm</c> again) with PNG. Sharing the function means the behaviour is not merely similar but
/// identical, and renaming a file of each format to each name confirmed it: the converter reports
/// the picture and names the reader <c>bpr</c>, <c>cft</c>, <c>frm</c> and <c>frm2</c>.
/// <para/>
/// Claiming a name is only honest if the reader behind it refuses a file of some other format
/// arriving under it, so that is what these insist on. <c>.frm</c> is claimed twice over, which is
/// what the catalogue says, and the signature decides which of the two reads a given file.
/// </summary>
[TestFixture]
public sealed class WrappedNameTests {

  private static RawImage _Picture(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var at = (y * width + x) * 3;
        pixels[at] = (byte)(x * 29 % 251);
        pixels[at + 1] = (byte)(y * 37 % 251);
        pixels[at + 2] = (byte)((x + y) * 11 % 251);
      }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  private static byte[] _Gif(int width, int height)
    => FileFormat.Gif.GifWriter.ToBytes(FileFormat.Gif.GifFile.FromRawImage(_Picture(width, height)));

  private static byte[] _Tiff(int width, int height)
    => FileFormat.Tiff.TiffWriter.ToBytes(FileFormat.Tiff.TiffFile.FromRawImage(_Picture(width, height)));

  private static byte[] _Jpeg(int width, int height)
    => FileFormat.Jpeg.JpegWriter.ToBytes(FileFormat.Jpeg.JpegFile.FromRawImage(_Picture(width, height)));

  private static byte[] _Png(int width, int height)
    => FileFormat.Png.PngWriter.ToBytes(FileFormat.Png.PngFile.FromRawImage(_Picture(width, height)));

  private static void _With(byte[] data, string extension, Action<FileInfo> check) {
    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + extension);
    File.WriteAllBytes(path, data);
    try {
      check(new FileInfo(path));
    } finally {
      File.Delete(path);
    }
  }

  private static FormatEntry[] _Claiming(string extension)
    => FormatRegistry.AllFormats
      .Where(entry => entry.AllExtensions?.Any(x => string.Equals(x, extension, StringComparison.OrdinalIgnoreCase)) == true)
      .ToArray();

  private static void _ReadsAt(byte[] data, string extension, int width, int height)
    => _With(data, extension, file => {
      Assert.That(_Claiming(extension), Is.Not.Empty, $"nothing claims {extension}");
      var image = FormatRegistry.Read(file);
      Assert.That(image, Is.Not.Null, $"{extension} should have been read");
      Assert.Multiple(() => {
        Assert.That(image!.Width, Is.EqualTo(width));
        Assert.That(image.Height, Is.EqualTo(height));
      });
    });

  private static void _EveryClaimantRefuses(byte[] data, string extension, string what)
    => _With(data, extension, file => {
      foreach (var entry in _Claiming(extension))
        Assert.Throws<InvalidDataException>(() => entry.LoadRawImageOrThrow!(file), $"{entry.Name} took {what} named {extension}");
    });

  [Test]
  [Category("Integration")]
  public void TheAaaLogoNameReadsAGif() => _ReadsAt(_Gif(9, 6), ".bpr", 9, 6);

  [Test]
  [Category("Integration")]
  public void TheAaaLogoNameRefusesAForeignFile()
    => _EveryClaimantRefuses(_Jpeg(9, 6), ".bpr", "a JPEG");

  [Test]
  [Category("Integration")]
  public void TheOptigraphicsNameReadsATiff() => _ReadsAt(_Tiff(9, 6), ".ctf", 9, 6);

  [Test]
  [Category("Integration")]
  public void TheOptigraphicsNameRefusesAForeignFile()
    => _EveryClaimantRefuses(_Jpeg(9, 6), ".ctf", "a JPEG");

  [Test]
  [Category("Integration")]
  public void ThePhotoFrameNameReadsAJpeg() => _ReadsAt(_Jpeg(16, 16), ".frm", 16, 16);

  [Test]
  [Category("Integration")]
  public void TheAlbumNameReadsAPng() => _ReadsAt(_Png(9, 6), ".frm", 9, 6);

  /// <summary>
  /// A GIF is neither of the two things a <c>.frm</c> can be, so both readers that claim the name
  /// have to turn it away. The sixteen EZ-Forms documents in the corpus under this extension are
  /// refused for the same reason: they are forms rather than pictures, and XnView refuses them too.
  /// </summary>
  [Test]
  [Category("Integration")]
  public void TheFrmNameRefusesAForeignFile()
    => _EveryClaimantRefuses(_Gif(9, 6), ".frm", "a GIF");
}
