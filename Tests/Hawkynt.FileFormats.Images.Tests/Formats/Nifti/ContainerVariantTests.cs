using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Nifti.Tests;

[TestFixture]
public sealed class ContainerVariantTests {

  private static RawImage _Rgba() {
    var data = new byte[9 * 6 * 4];
    for (var i = 0; i < data.Length / 4; ++i) {
      data[i * 4] = (byte)(i * 5 + 1);
      data[i * 4 + 1] = (byte)(i * 9 + 2);
      data[i * 4 + 2] = (byte)(i * 13 + 3);
      data[i * 4 + 3] = (byte)(255 - i * 3);
    }
    return new() { Width = 9, Height = 6, Format = PixelFormat.Rgba32, PixelData = data };
  }

  [Test]
  [Category("Integration")]
  public void Pair_WriteToFile_CreatesHdrAndImg_AndRoundTripsExactly() {
    var directory = Path.Combine(Path.GetTempPath(), "nifti-pair-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try {
      var source = _Rgba();
      var header = new FileInfo(Path.Combine(directory, "scan.hdr"));
      FormatIO.WriteToFile<NiftiPairFile>(source, header);

      Assert.That(header.Exists, Is.True);
      Assert.That(File.Exists(Path.Combine(directory, "scan.img")), Is.True);

      var decoded = FormatIO.Decode<NiftiPairFile>(header);
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgba32));
      Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
    } finally {
      Directory.Delete(directory, recursive: true);
    }
  }

  [Test]
  [Category("Integration")]
  public void Pair_CanBeOpenedByImgPath() {
    var directory = Path.Combine(Path.GetTempPath(), "nifti-pair-img-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try {
      var source = _Rgba();
      var header = new FileInfo(Path.Combine(directory, "scan.hdr"));
      FormatIO.WriteToFile<NiftiPairFile>(source, header);

      var decoded = NiftiPairFile.ToRawImage(NiftiPairReader.FromFile(new FileInfo(Path.Combine(directory, "scan.img"))));
      Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
    } finally {
      Directory.Delete(directory, recursive: true);
    }
  }

  [Test]
  [Category("Integration")]
  public void NiiGz_RoundTrip_PreservesRgba32Exactly() {
    var source = _Rgba();
    var encoded = FormatIO.Encode<NiftiGzipFile>(source);

    Assert.That(encoded[0], Is.EqualTo(0x1F));
    Assert.That(encoded[1], Is.EqualTo(0x8B));

    var decoded = FormatIO.Decode<NiftiGzipFile>(encoded);
    Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgba32));
    Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void GzipSignature_DoesNotClaimArbitraryGzip() {
    byte[] arbitraryGzip;
    using (var output = new MemoryStream()) {
      using (var gzip = new System.IO.Compression.GZipStream(output, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
        gzip.Write("not a nifti"u8);
      arbitraryGzip = output.ToArray();
    }

    Assert.That(NiftiGzipFile.MatchesSignature(arbitraryGzip), Is.Not.True);
  }
}
