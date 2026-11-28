using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using FileFormat.Core;

namespace FileFormat.PictureGearPocket.Tests;

/// <summary>PictureGear Pocket pictures, held to files Sony's own libprcim.dll wrote and decoded.</summary>
/// <remarks>
/// Fixtures/PROVENANCE.txt says where each file came from. Every one was written by one of the three
/// releases of Sony's library from a picture made for this suite, and the hashes are of what that
/// library's own decoder gives back for it.
/// </remarks>
[TestFixture]
public sealed class PictureGearPocketTests {

  private const string _CORPUS_VARIABLE = "PRC_CORPUS";

  private static byte[] _Fixture(string name) {
    var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "PictureGearPocket", name);
    Assert.That(File.Exists(path), Is.True, $"Test fixture missing: {path}");
    return File.ReadAllBytes(path);
  }

  private static (int Width, int Height, byte[] Rgb) _Ppm(byte[] data) {
    var fields = new List<string>();
    var position = 0;
    while (fields.Count < 4) {
      while (char.IsWhiteSpace((char)data[position]))
        ++position;
      var start = position;
      while (!char.IsWhiteSpace((char)data[position]))
        ++position;
      fields.Add(Encoding.ASCII.GetString(data, start, position - start));
    }

    var width = int.Parse(fields[1]);
    var height = int.Parse(fields[2]);
    return (width, height, data.AsSpan(position + 1, width * height * 3).ToArray());
  }

  private static byte[] _Rgb(PictureGearPocketFile file) => PictureGearPocketFile.ToRawImage(file).EnsureFormat(PixelFormat.Rgb24).PixelData;

  private static string _Sha256(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

  // ==========================================================================================
  // Files Sony's library wrote
  // ==========================================================================================

  [TestCase("sony2000_flat_37x23_rle8.prc", 37, 23, 8, 32, true, "de70e4465c88908bfe68e037cc7ef2cd899d18c1e6f00801a9ad8af716aab2e4")]
  [TestCase("sony2001_flat_37x23_plain8.prc", 37, 23, 8, 32, false, "de70e4465c88908bfe68e037cc7ef2cd899d18c1e6f00801a9ad8af716aab2e4")]
  [TestCase("sony2001_grad_100x70_rle8.prc", 100, 70, 8, 32, true, "482b28c36a7955555761721a694a01fbc2152b387f4e602ea81c9b20245c2901")]
  [TestCase("sony2003_blocks_330x324_rle8.prc", 330, 324, 8, 64, true, "432e6ee0b908bb91cab6668a3ae4dc77b6b788a889a880b4a2da8e85c6a0cf46")]
  [TestCase("sony2003_sweep_61x45_rle16.prc", 61, 45, 16, 32, true, "749e63146602fd29a9927ed4f2a99ef64097fa0c4877f6006ae3bf735b8f1d2a")]
  [TestCase("sony2003_sweep_61x45_plain16.prc", 61, 45, 16, 32, false, "749e63146602fd29a9927ed4f2a99ef64097fa0c4877f6006ae3bf735b8f1d2a")]
  [TestCase("sony2003_blocks_330x324_rle16.prc", 330, 324, 16, 64, true, "01d9c78deffc6c8697d935bc57d60977f7f27bbde58d3b275a8bc57f7d4de043")]
  [Category("Conformance")]
  public void SonyWrittenFile_DecodesToWhatSonysDecoderGives(string name, int width, int height, int depth, int tileSize, bool compressed, string sha256) {
    var file = PictureGearPocketReader.FromBytes(_Fixture(name));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(width));
      Assert.That(file.Height, Is.EqualTo(height));
      Assert.That(file.BitsPerPixel, Is.EqualTo(depth));
      Assert.That(file.TileSize, Is.EqualTo(tileSize));
      Assert.That(file.IsCompressed, Is.EqualTo(compressed));
      Assert.That(file.Palette, depth == 8 ? Is.Not.Null : Is.Null);
      Assert.That(file.Thumbnail, Has.Length.EqualTo(PictureGearPocketFile.ThumbnailSize * PictureGearPocketFile.ThumbnailSize));
      Assert.That(_Sha256(_Rgb(file)), Is.EqualTo(sha256));
    });
  }

  [TestCase("sony2000_flat_37x23_rle8.prc")]
  [TestCase("sony2001_flat_37x23_plain8.prc")]
  [TestCase("sony2001_grad_100x70_rle8.prc")]
  [TestCase("sony2003_blocks_330x324_rle8.prc")]
  [TestCase("sony2003_sweep_61x45_rle16.prc")]
  [TestCase("sony2003_sweep_61x45_plain16.prc")]
  [TestCase("sony2003_blocks_330x324_rle16.prc")]
  [Category("Conformance")]
  public void SonyWrittenFile_IsWrittenBackByteForByte(string name) {
    var original = _Fixture(name);
    var written = PictureGearPocketWriter.ToBytes(PictureGearPocketReader.FromBytes(original));

    Assert.That(written, Is.EqualTo(original));
  }

  [TestCase("sony2000_flat_37x23_rle8.prc", "flat")]
  [TestCase("sony2001_grad_100x70_rle8.prc", "grad")]
  [TestCase("sony2003_sweep_61x45_rle16.prc", "sweep")]
  [Category("Conformance")]
  public void SonyWrittenFile_CarriesTheNameAndDatesSonysLibraryGaveIt(string name, string databaseName) {
    var file = PictureGearPocketReader.FromBytes(_Fixture(name));

    Assert.Multiple(() => {
      Assert.That(file.Name, Is.EqualTo(databaseName));
      Assert.That(file.Created.Year, Is.EqualTo(2026));
      Assert.That(file.Modified, Is.EqualTo(file.Created));
    });
  }

  [TestCase("sony2000_flat_37x23_rle8.prc")]
  [TestCase("sony2001_flat_37x23_plain8.prc")]
  [Category("Conformance")]
  public void FewColourPicture_SonysFileKeepsEveryColourExactly(string name) {
    var file = PictureGearPocketReader.FromBytes(_Fixture(name));
    var (width, height, expected) = _Ppm(_Fixture("flat_37x23.ppm"));

    Assert.Multiple(() => {
      Assert.That((file.Width, file.Height), Is.EqualTo((width, height)));
      Assert.That(_Rgb(file), Is.EqualTo(expected));
    });
  }

  [Test]
  [Category("Conformance")]
  public void HighColourPicture_IsWhatSonysEncoderWritesForTheSamePicture() {
    var sony = PictureGearPocketReader.FromBytes(_Fixture("sony2003_sweep_61x45_rle16.prc"));
    var (width, height, rgb) = _Ppm(_Fixture("sweep_61x45.ppm"));
    var ours = PictureGearPocketFile.FromRawImage(new RawImage { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb }, 16);

    // The thumbnail and the database's name and dates are the only things not made from the pixels.
    var written = PictureGearPocketWriter.ToBytes(ours with { Thumbnail = sony.Thumbnail, Name = sony.Name, Created = sony.Created, Modified = sony.Modified });

    Assert.That(written, Is.EqualTo(_Fixture("sony2003_sweep_61x45_rle16.prc")));
  }

  [Test]
  [Category("Conformance")]
  public void SonysThumbnail_IsInTheSystemPalette() {
    var file = PictureGearPocketReader.FromBytes(_Fixture("sony2001_grad_100x70_rle8.prc"));
    var thumbnail = PictureGearPocketFile.ThumbnailToRawImage(file);

    Assert.That(thumbnail, Is.Not.Null);
    Assert.Multiple(() => {
      Assert.That(thumbnail!.Width, Is.EqualTo(44));
      Assert.That(thumbnail.Height, Is.EqualTo(44));
      Assert.That(thumbnail.PaletteCount, Is.EqualTo(256));
      Assert.That(thumbnail.Palette![..6], Is.EqualTo(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xCC, 0xFF }));
      Assert.That(thumbnail.Palette.AsSpan(225 * 3, 6).ToArray(), Is.EqualTo(new byte[] { 0xC0, 0xC0, 0xC0, 0x80, 0, 0 }));
      Assert.That(file.Thumbnail!.Max(), Is.LessThan(231));
      Assert.That(file.Thumbnail!.Distinct().Count(), Is.GreaterThan(10));
    });
  }

  // ==========================================================================================
  // What other software writes
  // ==========================================================================================

  [Test]
  public void NConvertsFile_IsRefused() {
    // XnView's writer stores four bits a pixel, which neither Sony's software nor this reader
    // takes, and it stops writing before its last tile, which the resource list still points to.
    // See Fixtures/PROVENANCE.txt.
    var data = _Fixture("nconvert_40x36_colours16.prc");

    Assert.That(PictureGearPocketReader.HasSignature(data), Is.True);
    var error = Assert.Throws<InvalidDataException>(() => PictureGearPocketReader.FromBytes(data));
    Assert.That(error!.Message, Does.Contain("past the end"));

    // Given the end it lost, what is left is still a depth nobody else writes.
    var completed = new byte[3776 + 2];
    data.CopyTo(completed, 0);
    error = Assert.Throws<InvalidDataException>(() => PictureGearPocketReader.FromBytes(completed));
    Assert.That(error!.Message, Does.Contain("4 bits"));
  }

  // ==========================================================================================
  // The writer
  // ==========================================================================================

  private static RawImage _Picture(int width, int height, Func<int, int, (byte R, byte G, byte B)> colour) {
    var rgb = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var (r, g, b) = colour(x, y);
        rgb[(y * width + x) * 3] = r;
        rgb[(y * width + x) * 3 + 1] = g;
        rgb[(y * width + x) * 3 + 2] = b;
      }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  [Test]
  public void FromRawImage_FewColours_RoundTripsExactly() {
    var (width, height, rgb) = _Ppm(_Fixture("flat_37x23.ppm"));
    var file = PictureGearPocketFile.FromRawImage(new RawImage { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb });
    var back = PictureGearPocketReader.FromBytes(PictureGearPocketWriter.ToBytes(file));

    Assert.Multiple(() => {
      Assert.That(back.BitsPerPixel, Is.EqualTo(8));
      Assert.That(back.IsCompressed, Is.True);
      Assert.That(back.TileSize, Is.EqualTo(32));
      Assert.That(back.Palette!.Length / 3, Is.LessThanOrEqualTo(PictureGearPocketFile.MaximumColors));
      Assert.That(_Rgb(back), Is.EqualTo(rgb));
    });
  }

  [Test]
  public void FromRawImage_ManyColours_StaysWithinSonysPaletteSize() {
    var file = PictureGearPocketFile.FromRawImage(_Picture(64, 48, (x, y) => ((byte)(x * 4), (byte)(y * 5), (byte)(x * y))));

    Assert.Multiple(() => {
      Assert.That(file.Palette!.Length / 3, Is.InRange(2, PictureGearPocketFile.MaximumColors));
      Assert.That(file.PixelData.Max(), Is.LessThan(file.Palette.Length / 3));
      Assert.That(file.Thumbnail!.Max(), Is.LessThan(231));
    });
  }

  [Test]
  public void FromRawImage_HighColour_KeepsTheTopBitsOfEveryChannel() {
    var source = _Picture(70, 33, (x, y) => ((byte)(x * 3), (byte)(y * 7), (byte)(255 - x)));
    var back = PictureGearPocketReader.FromBytes(PictureGearPocketWriter.ToBytes(PictureGearPocketFile.FromRawImage(source, 16)));
    var rgb = _Rgb(back);

    Assert.That(back.BitsPerPixel, Is.EqualTo(16));
    for (var i = 0; i < rgb.Length; i += 3)
      Assert.That((rgb[i] & 0xF8, rgb[i + 1] & 0xFC, rgb[i + 2] & 0xF8),
        Is.EqualTo((source.PixelData[i] & 0xF8, source.PixelData[i + 1] & 0xFC, source.PixelData[i + 2] & 0xF8)), $"pixel {i / 3}");
  }

  [TestCase(160, 160, 32)]
  [TestCase(319, 400, 32)]
  [TestCase(400, 319, 32)]
  [TestCase(320, 320, 64)]
  public void FromRawImage_ChoosesSonysTileSize(int width, int height, int tileSize) {
    var file = PictureGearPocketFile.FromRawImage(_Picture(width, height, (x, y) => ((byte)(x / 50 * 40), 0, (byte)(y / 50 * 40))));
    Assert.That(file.TileSize, Is.EqualTo(tileSize));
  }

  [Test]
  public void LongRuns_SurviveTheTileCoding() {
    // A run longer than the 257 one code can hold, a lone pixel, and a pair that ends a tile.
    var source = _Picture(64, 64, (x, y) => y < 40 ? ((byte)200, (byte)10, (byte)10) : x == 5 && y == 41 ? ((byte)0, (byte)0, (byte)255) : ((byte)0, (byte)90, (byte)0));
    var file = PictureGearPocketFile.FromRawImage(source);
    var bytes = PictureGearPocketWriter.ToBytes(file);

    Assert.That(_Rgb(PictureGearPocketReader.FromBytes(bytes)), Is.EqualTo(source.PixelData));
  }

  [Test]
  public void Uncompressed_RoundTripsAndSaysSo() {
    var file = PictureGearPocketFile.FromRawImage(_Picture(45, 50, (x, y) => ((byte)(x * 5), (byte)(y * 5), 128))) with { IsCompressed = false };
    var bytes = PictureGearPocketWriter.ToBytes(file);
    var back = PictureGearPocketReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(back.IsCompressed, Is.False);
      Assert.That(back.PixelData, Is.EqualTo(file.PixelData));
      Assert.That(bytes, Is.EqualTo(PictureGearPocketWriter.ToBytes(back)));
    });
  }

  [Test]
  public void Writer_WithoutAThumbnail_MakesOne() {
    var file = PictureGearPocketFile.FromRawImage(_Picture(20, 90, (x, y) => ((byte)(y * 2), 50, 50))) with { Thumbnail = null };
    var back = PictureGearPocketReader.FromBytes(PictureGearPocketWriter.ToBytes(file));

    Assert.That(back.Thumbnail, Has.Length.EqualTo(44 * 44));
  }

  [Test]
  public void SystemPalettePicture_HasNoPaletteResourceAndReadsInThePalmColours() {
    var file = new PictureGearPocketFile {
      Width = 3, Height = 2, BitsPerPixel = 8, IsCompressed = true, Name = "palm",
      PixelData = [0, 125, 230, 230, 6, 0],
    };
    var bytes = PictureGearPocketWriter.ToBytes(file);
    var back = PictureGearPocketReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(Encoding.ASCII.GetString(bytes).Contains("iPLT"), Is.False);
      Assert.That(back.Palette, Is.Null);
      Assert.That(_Rgb(back), Is.EqualTo(new byte[] { 255, 255, 255, 255, 0, 0, 0, 0, 0, 0, 0, 0, 255, 255, 204, 255, 255, 255 }));
    });
  }

  [Test]
  public void DamagedThumbnail_CostsOnlyTheThumbnail() {
    var data = (byte[])_Fixture("sony2001_grad_100x70_rle8.prc").Clone();

    // The thumbnail's frame list is the second resource; point it at a tile that is not there.
    var frameList = data[78 + 10 + 6] << 24 | data[78 + 10 + 7] << 16 | data[78 + 10 + 8] << 8 | data[78 + 10 + 9];
    data[frameList + 6] = 0x7F;
    var file = PictureGearPocketReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(file.Thumbnail, Is.Null);
      Assert.That(_Sha256(_Rgb(file)), Is.EqualTo("482b28c36a7955555761721a694a01fbc2152b387f4e602ea81c9b20245c2901"));
      Assert.That(PictureGearPocketReader.FromBytes(PictureGearPocketWriter.ToBytes(file)).Thumbnail, Has.Length.EqualTo(44 * 44));
    });
  }

  [Test]
  public void Writer_NameIsCutToThirtyOneCharacters() {
    var file = PictureGearPocketFile.FromRawImage(_Picture(4, 4, (_, _) => (1, 2, 3))) with { Name = new string('x', 40) };
    var back = PictureGearPocketReader.FromBytes(PictureGearPocketWriter.ToBytes(file));

    Assert.That(back.Name, Is.EqualTo(new string('x', 31)));
  }

  [Test]
  public void Writer_RefusesAnIndexPastThePalette() {
    var file = PictureGearPocketFile.FromRawImage(_Picture(4, 4, (_, _) => (1, 2, 3)));
    var bad = file with { PixelData = [.. file.PixelData.Select(_ => (byte)9)] };

    Assert.Throws<ArgumentException>(() => PictureGearPocketWriter.ToBytes(bad));
  }

  [Test]
  public void FromRawImage_RefusesAnOversizedPicture() {
    Assert.Throws<ArgumentException>(() => PictureGearPocketFile.FromRawImage(_Picture(PictureGearPocketFile.MaximumExtent + 1, 1, (_, _) => (0, 0, 0))));
  }

  // ==========================================================================================
  // Recognising and refusing
  // ==========================================================================================

  [Test]
  public void Signature_IsTypeAndCreatorImvs() {
    var data = _Fixture("sony2001_grad_100x70_rle8.prc");

    Assert.Multiple(() => {
      Assert.That(PictureGearPocketReader.HasSignature(data), Is.True);
      Assert.That(PictureGearPocketReader.HasSignature(data.AsSpan(0, 60)), Is.False);
      var application = (byte[])data.Clone();
      "applPRC1"u8.CopyTo(application.AsSpan(60));
      Assert.That(PictureGearPocketReader.HasSignature(application), Is.False);
      Assert.Throws<InvalidDataException>(() => PictureGearPocketReader.FromBytes(application));
    });
  }

  [Test]
  public void MissingTile_IsRefused() {
    var data = _Fixture("sony2001_grad_100x70_rle8.prc");
    var broken = (byte[])data.Clone();

    // Renumber the last tile, so the picture names one the database does not hold.
    var count = broken[77] | broken[76] << 8;
    var entry = 78 + (count - 1) * 10;
    broken[entry + 4] = 0x7F;

    Assert.Throws<InvalidDataException>(() => PictureGearPocketReader.FromBytes(broken));
  }

  [Test]
  public void TruncatedResourceList_IsRefused() {
    var data = _Fixture("sony2001_grad_100x70_rle8.prc");
    Assert.Throws<InvalidDataException>(() => PictureGearPocketReader.FromBytes(data[..100]));
  }

  [Test]
  public void TooSmall_IsRefused() =>
    Assert.Throws<InvalidDataException>(() => PictureGearPocketReader.FromBytes(new byte[40]));

  // ==========================================================================================
  // A corpus, when there is one
  // ==========================================================================================

  [Test]
  [Category("Conformance")]
  [Explicit("Needs a directory of PictureGear Pocket files in PRC_CORPUS.")]
  public void Corpus_EveryPictureDecodesAndIsWrittenBackAsTheSamePicture() {
    // A file Sony's software wrote comes back byte for byte. One that has been through a handheld
    // and HotSync's backup does not quite: Palm OS pads the name with 0xFF, leaves two bytes after
    // the resource list and sets its own seed, and none of that is part of the picture.
    var directory = Environment.GetEnvironmentVariable(_CORPUS_VARIABLE);
    if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
      Assert.Inconclusive($"Set {_CORPUS_VARIABLE} to a directory of .prc files.");

    var failures = new List<string>();
    var count = 0;
    foreach (var path in Directory.EnumerateFiles(directory!, "*.prc", SearchOption.AllDirectories)) {
      var data = File.ReadAllBytes(path);
      if (!PictureGearPocketReader.HasSignature(data))
        continue;

      ++count;
      try {
        var file = PictureGearPocketReader.FromBytes(data);
        var back = PictureGearPocketReader.FromBytes(PictureGearPocketWriter.ToBytes(file));
        if (!_Rgb(back).AsSpan().SequenceEqual(_Rgb(file))
            || !back.Thumbnail.AsSpan().SequenceEqual(file.Thumbnail)
            || (back.Name, back.Created, back.Modified, back.TileSize, back.IsCompressed) != (file.Name, file.Created, file.Modified, file.TileSize, file.IsCompressed))
          failures.Add($"{path}: not written back as the same picture");
      } catch (Exception e) {
        failures.Add($"{path}: {e.Message}");
      }
    }

    Assert.That(count, Is.GreaterThan(0), "no PictureGear Pocket files found");
    Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures));
  }
}
