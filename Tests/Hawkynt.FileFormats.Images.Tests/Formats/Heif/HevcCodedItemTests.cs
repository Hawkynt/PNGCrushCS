using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using FileFormat.Heif;
using Hawkynt.FileFormats.Images.Tests;

namespace FileFormat.Heif.Tests;

/// <summary>
/// Covers what happens to a HEIF whose picture is HEVC-coded, which is every HEIF anything else
/// wrote.
/// </summary>
/// <remarks>
/// There is no HEVC decoder here. There used to be something shaped like one, and when it failed —
/// which it did on every stream libheif has ever produced — the reader copied the raw mdat bytes
/// into a buffer sized for the picture and returned that as a successful read. For the 61x37 file
/// ImageMagick 7.1.2 writes that came back as 74 non-zero bytes out of 6771: a black rectangle of
/// the right size, announced as the picture.
/// <para/>
/// A wrong picture that nothing announces is worse than a refusal, so this refuses. The extent is
/// still readable — it comes from the container's ispe and clap boxes, not from the codestream —
/// and <see cref="HeifFile.ReadImageInfo"/> keeps answering it.
/// </remarks>
[TestFixture]
public sealed class HevcCodedItemTests {

  /// <summary>The file this is really about: one a reference encoder wrote.</summary>
  [Test]
  [Category("Conformance")]
  public void FromBytes_LibheifWrittenFile_RefusesRatherThanAnnouncingAnEmptyPicture() {
    var directory = Directory.CreateTempSubdirectory("heif-hevc");
    try {
      var source = Path.Combine(directory.FullName, "source.png");
      var heic = Path.Combine(directory.FullName, "picture.heic");

      using (var draw = ExternalTool.StartOrIgnore("magick", $"-size 61x37 gradient:blue-yellow -colorspace sRGB \"{source}\"")) {
        draw.WaitForExit();
        if (draw.ExitCode != 0)
          Assert.Ignore("ImageMagick would not draw the source here.");
      }

      using (var encode = ExternalTool.StartOrIgnore("magick", $"\"{source}\" \"{heic}\"")) {
        var complaint = encode.StandardError.ReadToEnd();
        encode.WaitForExit();
        if (encode.ExitCode != 0 || !File.Exists(heic))
          Assert.Ignore($"ImageMagick has no HEIF encoder here: {complaint.Trim()}");
      }

      var bytes = File.ReadAllBytes(heic);
      if (!_IsIsoBmff(bytes))
        Assert.Ignore("ImageMagick has no HEIF encoder here: it wrote the source back unconverted rather than refusing.");

      var refusal = Assert.Throws<NotSupportedException>(() => HeifReader.FromBytes(bytes));
      Assert.That(refusal!.Message, Does.Contain("HEVC"), "the refusal must name what is unimplemented");
    } finally {
      try { directory.Delete(recursive: true); } catch { /* best effort */ }
    }
  }

  /// <summary>The extent is the container's to state, so asking for it must still work.</summary>
  [Test]
  [Category("Conformance")]
  public void ReadImageInfo_LibheifWrittenFile_StillReportsTheCleanAperture() {
    var directory = Directory.CreateTempSubdirectory("heif-hevc-info");
    try {
      var source = Path.Combine(directory.FullName, "source.png");
      var heic = Path.Combine(directory.FullName, "picture.heic");

      using (var draw = ExternalTool.StartOrIgnore("magick", $"-size 61x37 gradient:blue-yellow -colorspace sRGB \"{source}\"")) {
        draw.WaitForExit();
        if (draw.ExitCode != 0)
          Assert.Ignore("ImageMagick would not draw the source here.");
      }

      using (var encode = ExternalTool.StartOrIgnore("magick", $"\"{source}\" \"{heic}\"")) {
        var complaint = encode.StandardError.ReadToEnd();
        encode.WaitForExit();
        if (encode.ExitCode != 0 || !File.Exists(heic))
          Assert.Ignore($"ImageMagick has no HEIF encoder here: {complaint.Trim()}");
      }

      var heicBytes = File.ReadAllBytes(heic);
      if (!_IsIsoBmff(heicBytes))
        Assert.Ignore("ImageMagick has no HEIF encoder here: it wrote the source back unconverted rather than refusing.");

      var info = HeifFile.ReadImageInfo(heicBytes);

      Assert.That(info, Is.Not.Null, "the extent comes from the container, which is readable");
      Assert.Multiple(() => {
        // heif-info prints "61x37" and "crop: left=0 top=0 right=3 bottom=27" for this file: the
        // coded extent is 64x64 and the clap box states the picture inside it.
        Assert.That(info!.Value.Width, Is.EqualTo(61));
        Assert.That(info.Value.Height, Is.EqualTo(37));
      });
    } finally {
      try { directory.Delete(recursive: true); } catch { /* best effort */ }
    }
  }

  /// <summary>
  /// Whether a file ImageMagick claims to have written is actually ISOBMFF, rather than the source
  /// PNG copied over with a new extension.
  /// </summary>
  /// <remarks>
  /// A <c>magick</c> build without a HEIF encoder does not refuse to write one: it warns on stderr,
  /// exits 0, and writes the input image back out under the requested name, unconverted. Exit code
  /// and file existence, which is all the encode step above checks, both look identical to a real
  /// write. Asking <see cref="HeifReader"/> to read a PNG in a coat that says ".heic" is not this
  /// test's oracle disagreeing with us; it is not a HEIF on this machine to compare against.
  /// </remarks>
  private static bool _IsIsoBmff(byte[] bytes)
    => bytes.Length >= 8 && bytes[4] == (byte)'f' && bytes[5] == (byte)'t' && bytes[6] == (byte)'y' && bytes[7] == (byte)'p';

  // --- the same two claims, on a container built here, so they hold without ImageMagick ---

  [Test]
  [Category("Unit")]
  public void FromBytes_HvcCConfiguredItem_Refuses() {
    var bytes = _BuildHeif(64, 64, _Clap(61, 1, 37, 1, -3, 2, -27, 2), hvcC: new byte[23], mdatPayload: new byte[64 * 64 * 3]);

    var refusal = Assert.Throws<NotSupportedException>(() => HeifReader.FromBytes(bytes));
    Assert.That(refusal!.Message, Does.Contain("HEVC"));
  }

  [Test]
  [Category("Unit")]
  public void ReadImageInfo_HvcCConfiguredItem_ReportsPictureExtentNotPadding() {
    var bytes = _BuildHeif(64, 64, _Clap(61, 1, 37, 1, -3, 2, -27, 2), hvcC: new byte[23], mdatPayload: new byte[64 * 64 * 3]);

    var info = HeifFile.ReadImageInfo(bytes);

    Assert.That(info, Is.Not.Null);
    Assert.Multiple(() => {
      Assert.That(info!.Value.Width, Is.EqualTo(61));
      Assert.That(info.Value.Height, Is.EqualTo(37));
    });
  }

  [Test]
  [Category("Unit")]
  public void ReadImageInfo_NoClapBox_ReportsIspeExtent() {
    var bytes = _BuildHeif(64, 48, clap: null, hvcC: null, mdatPayload: new byte[64 * 48 * 3]);

    var info = HeifFile.ReadImageInfo(bytes);

    Assert.That(info, Is.Not.Null);
    Assert.Multiple(() => {
      Assert.That(info!.Value.Width, Is.EqualTo(64));
      Assert.That(info.Value.Height, Is.EqualTo(48));
    });
  }

  [Test]
  [Category("Unit")]
  public void ReadImageInfo_NotHeif_ReturnsNull() {
    Assert.That(HeifFile.ReadImageInfo(new byte[8]), Is.Null);
  }

  // --- Helpers (the layout libheif emits: ispe first, then clap, then hvcC) ---

  private static byte[] _Clap(int widthN, int widthD, int heightN, int heightD, int horizOffN, int horizOffD, int vertOffN, int vertOffD) {
    var data = new byte[32];
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(0), widthN);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(4), widthD);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(8), heightN);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(12), heightD);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(16), horizOffN);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(20), horizOffD);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(24), vertOffN);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(28), vertOffD);
    return data;
  }

  private static byte[] _BuildHeif(int codedWidth, int codedHeight, byte[]? clap, byte[]? hvcC, byte[] mdatPayload) {
    var ispeBody = new byte[8];
    BinaryPrimitives.WriteUInt32BigEndian(ispeBody.AsSpan(0), (uint)codedWidth);
    BinaryPrimitives.WriteUInt32BigEndian(ispeBody.AsSpan(4), (uint)codedHeight);

    var ipcoParts = new List<byte[]> { _FullBox("ispe", ispeBody) };
    if (clap != null)
      ipcoParts.Add(_Box("clap", clap));
    if (hvcC != null)
      ipcoParts.Add(_Box("hvcC", hvcC));

    var meta = _FullBox("meta", _Box("iprp", _Box("ipco", _Concat(ipcoParts))));
    return _Concat([_BuildFtyp("heic"), meta, _Box("mdat", mdatPayload)]);
  }

  private static byte[] _BuildFtyp(string brand) {
    var body = new byte[12];
    System.Text.Encoding.ASCII.GetBytes(brand, 0, 4, body, 0);
    System.Text.Encoding.ASCII.GetBytes("mif1", 0, 4, body, 8);
    return _Box("ftyp", body);
  }

  private static byte[] _Box(string type, byte[] body) {
    var result = new byte[8 + body.Length];
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(0), (uint)result.Length);
    System.Text.Encoding.ASCII.GetBytes(type, 0, 4, result, 4);
    body.CopyTo(result.AsSpan(8));
    return result;
  }

  private static byte[] _FullBox(string type, byte[] body) {
    var inner = new byte[4 + body.Length];
    body.CopyTo(inner.AsSpan(4));
    return _Box(type, inner);
  }

  private static byte[] _Concat(IReadOnlyList<byte[]> parts) {
    var total = 0;
    foreach (var part in parts)
      total += part.Length;

    var result = new byte[total];
    var offset = 0;
    foreach (var part in parts) {
      part.CopyTo(result.AsSpan(offset));
      offset += part.Length;
    }

    return result;
  }
}
