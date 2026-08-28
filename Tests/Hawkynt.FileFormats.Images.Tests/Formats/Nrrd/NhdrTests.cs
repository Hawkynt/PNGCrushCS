using System;
using System.IO;
using System.Text;
using FileFormat.Core;

namespace FileFormat.Nrrd.Tests;

[TestFixture]
public sealed class NhdrTests {

  [Test]
  [Category("Integration")]
  public void WriteToFile_AndReadBack_RoundTripsDetachedPayload() {
    var directory = Path.Combine(Path.GetTempPath(), "nhdr-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try {
      var header = new FileInfo(Path.Combine(directory, "scan.nhdr"));
      var pixels = new byte[7 * 5 * 3];
      for (var i = 0; i < pixels.Length; ++i)
        pixels[i] = (byte)(i * 29 + 7);
      var image = new RawImage { Width = 7, Height = 5, Format = PixelFormat.Rgb24, PixelData = pixels };

      FormatIO.WriteToFile<NhdrFile>(image, header);

      var headerText = File.ReadAllText(header.FullName, Encoding.ASCII);
      Assert.That(headerText, Does.Contain("data file: scan.raw"));
      Assert.That(File.Exists(Path.Combine(directory, "scan.raw")), Is.True);

      var decoded = FormatIO.Decode<NhdrFile>(header);
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(decoded.PixelData, Is.EqualTo(pixels));
    } finally {
      Directory.Delete(directory, recursive: true);
    }
  }

  [Test]
  [Category("Integration")]
  public void FromFile_ListPayloads_ConcatenatesFilesInDeclaredOrder() {
    var directory = Path.Combine(Path.GetTempPath(), "nhdr-list-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try {
      File.WriteAllBytes(Path.Combine(directory, "a.raw"), [1, 2, 3, 4]);
      File.WriteAllBytes(Path.Combine(directory, "b.raw"), [5, 6, 7, 8]);
      File.WriteAllText(Path.Combine(directory, "list.nhdr"),
        "NRRD0004\n" +
        "type: uint8\n" +
        "dimension: 2\n" +
        "sizes: 4 2\n" +
        "encoding: raw\n" +
        "data file: LIST\n" +
        "a.raw\n" +
        "b.raw\n\n",
        Encoding.ASCII);

      var file = NhdrReader.FromFile(new FileInfo(Path.Combine(directory, "list.nhdr")));
      Assert.That(file.Nrrd.PixelData, Is.EqualTo(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
    } finally {
      Directory.Delete(directory, recursive: true);
    }
  }

  [Test]
  [Category("Integration")]
  public void FromFile_PrintfSequence_ExpandsZeroPaddedNames() {
    var directory = Path.Combine(Path.GetTempPath(), "nhdr-seq-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try {
      File.WriteAllBytes(Path.Combine(directory, "slice001.raw"), [10, 11]);
      File.WriteAllBytes(Path.Combine(directory, "slice002.raw"), [12, 13]);
      File.WriteAllBytes(Path.Combine(directory, "slice003.raw"), [14, 15]);
      File.WriteAllText(Path.Combine(directory, "sequence.nhdr"),
        "NRRD0004\n" +
        "type: uint8\n" +
        "dimension: 2\n" +
        "sizes: 2 3\n" +
        "encoding: raw\n" +
        "data file: slice%03d.raw 1 3 1\n\n",
        Encoding.ASCII);

      var file = NhdrReader.FromFile(new FileInfo(Path.Combine(directory, "sequence.nhdr")));
      Assert.That(file.Nrrd.PixelData, Is.EqualTo(new byte[] { 10, 11, 12, 13, 14, 15 }));
    } finally {
      Directory.Delete(directory, recursive: true);
    }
  }

  [Test]
  [Category("Integration")]
  public void DetachedGzipPayload_RoundTripsThroughOrdinaryNrrdCodec() {
    var directory = Path.Combine(Path.GetTempPath(), "nhdr-gz-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try {
      var pixels = new byte[128];
      for (var i = 0; i < pixels.Length; ++i)
        pixels[i] = (byte)(i % 17);
      var model = new NhdrFile {
        DataFile = "volume.raw.gz",
        Nrrd = new NrrdFile {
          Sizes = [16, 8],
          DataType = NrrdType.UInt8,
          Encoding = NrrdEncoding.Gzip,
          Endian = "little",
          PixelData = pixels,
        },
      };
      var header = new FileInfo(Path.Combine(directory, "volume.nhdr"));
      File.WriteAllBytes(header.FullName, NhdrWriter.ToBytes(model));
      NhdrWriter.WriteCompanions(model, header);

      var restored = NhdrReader.FromFile(header);
      Assert.That(restored.Nrrd.PixelData, Is.EqualTo(pixels));
    } finally {
      Directory.Delete(directory, recursive: true);
    }
  }
}
