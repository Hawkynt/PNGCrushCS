using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FileFormat.Core;
using Hawkynt.FileFormats.Images;

namespace FileFormat.MrSid.Tests;

/// <summary>Header, metadata and georeferencing of MrSID files, and the refusal to decode their pixels.</summary>
/// <remarks>
/// The two fixtures are GDAL's own MrSID test files. Every expectation marked as GDAL's is what GDAL's
/// <c>autotest/gdrivers/mrsid.py</c> asserts for them with the MrSID Decode SDK linked in, so these hold the reader to
/// the vendor decoder's answers without running it. The synthetic generation 4 file is built from the layout recorded
/// in <c>SPEC.md</c>, which was read off a GeoExpress 10 file; <see cref="DecodesTheCorpus"/> checks that file itself.
/// </remarks>
[TestFixture]
public sealed class MrSidTests {

  private static readonly double[] _GdalMercatorGeoTransform = [-15436.385771224039, 60.0, 0.0, 3321987.8617962394, 0.0, -60.0];

  /// <summary>GDAL's expected coordinate system for <c>mercator_new.sid</c> (test_mrsid_4).</summary>
  private const string _GDAL_MERCATOR_NEW_WKT = """
    PROJCS["MER         E000",
        GEOGCS["NAD27",
            DATUM["North_American_Datum_1927",
                SPHEROID["Clarke 1866",6378206.4,294.9786982138982,
                    AUTHORITY["EPSG","7008"]],
                AUTHORITY["EPSG","6267"]],
            PRIMEM["Greenwich",0],
            UNIT["degree",0.0174532925199433],
            AUTHORITY["EPSG","4267"]],
        PROJECTION["Mercator_1SP"],
        PARAMETER["latitude_of_origin",33.76446202777777],
        PARAMETER["central_meridian",-117.4745428888889],
        PARAMETER["scale_factor",1],
        PARAMETER["false_easting",0],
        PARAMETER["false_northing",0],
        UNIT["metre",1,
            AUTHORITY["EPSG","9001"]]]
    """;

  private static byte[] _Fixture(string name) {
    var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "MrSid", name);
    Assert.That(File.Exists(path), Is.True, $"Test fixture missing: {path}");
    return File.ReadAllBytes(path);
  }

  [Test]
  [Category("Unit")]
  public void Mg2_HeaderAgreesWithGdal() {
    var file = MrSidReader.FromBytes(_Fixture("mercator.sid"));
    Assert.Multiple(() => {
      Assert.That(file.Generation, Is.EqualTo(MrSidGeneration.Mg2));
      Assert.That((file.Width, file.Height, file.BandCount), Is.EqualTo((515, 515, 1)));
      Assert.That(file.LevelCount, Is.EqualTo(4), "GDAL reports four overviews (test_mrsid_3)");
      Assert.That(file.BitsPerSample, Is.EqualTo(8));
      Assert.That(file.TileCount, Is.EqualTo(1));
      Assert.That(file.HasUnreadMetadata, Is.False);
    });
  }

  [Test]
  [Category("Unit")]
  public void Mg2_GeoTransformAgreesWithGdal()
    => Assert.That(MrSidReader.FromBytes(_Fixture("mercator.sid")).Georeference!.Value.ToGeoTransform(),
      Is.EqualTo(_GdalMercatorGeoTransform).Within(1e-9));

  [Test]
  [Category("Unit")]
  public void Mg2_MetadataRecordsAreRead() {
    var file = MrSidReader.FromBytes(_Fixture("mercator.sid"));
    Assert.Multiple(() => {
      Assert.That(file.Metadata, Has.Count.EqualTo(48));
      // GDAL's coordinate system for this file is named after this citation (test_mrsid_1).
      Assert.That(file.Find("GEOTIFF_NUM::1026::GTCitationGeoKey")!.Strings, Is.EqualTo(new[] { "MER         E000|" }));
      Assert.That(file.Find("GEOTIFF_NUM::2048::GeographicTypeGeoKey")!.Numbers, Is.EqualTo(new[] { 4267.0 }));
      Assert.That(file.Find("IMAGE::NO_DATA_VALUE")!.Numbers, Is.EqualTo(new[] { 0.0, 0.0, 0.0 }));
      Assert.That(file.Find("IMAGE::TARGET_COMPRESSION_RATIO")!.Numbers[0], Is.EqualTo(15.0).Within(1e-5));
      Assert.That(file.Find("IMAGE::COMPRESSION_NLEV")!.Numbers, Is.EqualTo(new[] { 4.0 }));
      Assert.That(file.Find("IMAGE::COMPRESSION_VERSION")!.Numbers, Is.EqualTo(new[] { 1.0, 4.0, 1.0 }));
      Assert.That(file.Find("IMAGE::CREATION_DATE")!.Strings[0], Does.StartWith("Fri May 02 08:26:46 2003"));
      Assert.That(file.Metadata[^1].Key, Is.EqualTo("IMAGE::EOM"));
    });
  }

  [Test]
  [Category("Unit")]
  public void Mg3_HeaderGeoTransformAndCoordinateSystemAgreeWithGdal() {
    var file = MrSidReader.FromBytes(_Fixture("mercator_new.sid"));
    static string Squeeze(string text) => string.Concat(text.Where(static c => !char.IsWhiteSpace(c)));
    Assert.Multiple(() => {
      Assert.That(file.Generation, Is.EqualTo(MrSidGeneration.Mg3));
      Assert.That((file.Width, file.Height, file.BandCount, file.BitsPerSample), Is.EqualTo((515, 515, 1, 8)));
      Assert.That(file.LevelCount, Is.EqualTo(5));
      Assert.That(file.TileCount, Is.Null);
      Assert.That(file.Georeference!.Value.ToGeoTransform(), Is.EqualTo(_GdalMercatorGeoTransform).Within(1e-9));
      Assert.That(Squeeze(file.Wkt!), Is.EqualTo(Squeeze(_GDAL_MERCATOR_NEW_WKT)));
      Assert.That(file.Find("IMAGE::DYNAMIC_RANGE_WINDOW")!.Numbers, Is.EqualTo(new[] { 256.0 }));
      Assert.That(file.Metadata[^1].Key, Is.EqualTo("IMAGE::EOM"));
    });
  }

  [TestCase("mercator.sid", "MrSID MG2 wavelet")]
  [TestCase("mercator_new.sid", "MrSID MG3 wavelet")]
  [Category("Unit")]
  public void ReadImageInfo_AgreesWithTheFullRead(string name, string compression) {
    var data = _Fixture(name);
    var info = MrSidFile.ReadImageInfo(data.AsSpan(0, 64));
    var file = MrSidReader.FromBytes(data);
    Assert.That(info, Is.EqualTo(new ImageInfo(file.Width, file.Height, file.BandCount * file.BitsPerSample, "Grey", compression)));
  }

  [TestCase("mercator.sid", "MG2")]
  [TestCase("mercator_new.sid", "MG3")]
  [Category("Unit")]
  public void ToRawImage_RefusesByGeneration(string name, string generation) {
    var file = MrSidReader.FromBytes(_Fixture(name));
    var refusal = Assert.Throws<NotSupportedException>(() => MrSidFile.ToRawImage(file));
    Assert.That(refusal!.Message, Does.Contain($"MrSID {generation} pixel decoding is not implemented"));
  }

  [TestCase("mercator.sid")]
  [TestCase("mercator_new.sid")]
  [Category("Unit")]
  public void Registry_DetectsTheFileAndDoesNotPretendToDecodeIt(string name) {
    var data = _Fixture(name);
    Assert.That(FormatRegistry.DetectFromBytes(data), Is.EqualTo(ImageFormat.MrSid));
    var entry = FormatRegistry.GetEntry(ImageFormat.MrSid)!;
    Assert.Multiple(() => {
      Assert.That(entry.LoadRawImageFromBytes(data), Is.Null);
      Assert.That(entry.ReadImageInfo!(data)!.Value.Width, Is.EqualTo(515));
    });
  }

  [Test]
  [Category("Unit")]
  public void OtherPicturesNamedSid_AreRefused() {
    byte[][] others = [
      [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0x00, 0x01, 0x01, 0x00, 0x00, 0x01],
      [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, (byte)'I', (byte)'H', (byte)'D', (byte)'R'],
      [(byte)'B', (byte)'M', 0x46, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x36, 0x00, 0x00, 0x00, 0x28, 0x00],
    ];
    foreach (var other in others) {
      var padded = other.Concat(new byte[256]).ToArray();
      Assert.Multiple(() => {
        Assert.Throws<InvalidDataException>(() => MrSidReader.FromBytes(padded));
        Assert.That(MrSidFile.ReadImageInfo(padded), Is.Null);
        Assert.That(FormatRegistry.DetectFromBytes(padded), Is.Not.EqualTo(ImageFormat.MrSid));
      });
    }
  }

  [Test]
  [Category("Unit")]
  public void Mg4_SyntheticLayoutIsRead() {
    var file = MrSidReader.FromBytes(_SyntheticMg4());
    Assert.Multiple(() => {
      Assert.That(file.Generation, Is.EqualTo(MrSidGeneration.Mg4));
      Assert.That((file.Width, file.Height, file.BandCount, file.BitsPerSample, file.LevelCount), Is.EqualTo((8065, 7297, 4, 8, 7)));
      Assert.That(file.Find("IMAGE::ENCODING_APPLICATION")!.Strings, Is.EqualTo(new[] { "GeoExpress 10.0.1.5035" }));
      Assert.That(file.Georeference, Is.EqualTo(new MrSidGeoreference(311773.6408052709, 2779399.375153821, 0.15, -0.15)));
      Assert.That(MrSidFile.ReadImageInfo(_SyntheticMg4())!.Value.ColorMode, Is.EqualTo("4 bands"));
    });
  }

  [Test]
  [Category("Unit")]
  public void Lidar_IsRefusedByName() {
    var data = "msid"u8.ToArray().Concat(new byte[] { 4, 0, 0, 2 }).Concat(new byte[120]).ToArray();
    var refusal = Assert.Throws<NotSupportedException>(() => MrSidReader.FromBytes(data));
    Assert.Multiple(() => {
      Assert.That(refusal!.Message, Does.Contain("MG4 LiDAR"));
      Assert.That(MrSidFile.ReadImageInfo(data), Is.Null);
      Assert.That(FormatRegistry.DetectFromBytes(data), Is.EqualTo(ImageFormat.MrSid));
    });
  }

  [Test]
  [Category("Unit")]
  public void UnknownVersionByte_IsRefusedByName() {
    var data = _Fixture("mercator.sid");
    data[4] = 2;
    Assert.That(Assert.Throws<NotSupportedException>(() => MrSidReader.FromBytes(data))!.Message, Does.Contain("version byte 2"));
  }

  [Test]
  [Category("Unit")]
  public void Mg3_EveryTruncationIsRefused() {
    var data = _Fixture("mercator_new.sid");
    for (var length = 0; length < data.Length; ++length) {
      Assert.Throws<InvalidDataException>(() => MrSidReader.FromSpan(data.AsSpan(0, length)), $"prefix of {length} bytes");
      Assert.DoesNotThrow(() => MrSidFile.ReadImageInfo(data.AsSpan(0, length)));
    }
  }

  [Test]
  [Category("Unit")]
  public void Mg2_EveryTruncationBeforeTheMetadataEndIsRefused() {
    var data = _Fixture("mercator.sid");
    var metadataEnd = data.Length - 4; // FF D2 closes the metadata, FF A1 closes the file
    Assert.That(data[metadataEnd..], Is.EqualTo(new byte[] { 0xFF, 0xD2, 0xFF, 0xA1 }));
    for (var length = 0; length < metadataEnd; ++length) {
      Assert.Throws<InvalidDataException>(() => MrSidReader.FromSpan(data.AsSpan(0, length)), $"prefix of {length} bytes");
      Assert.DoesNotThrow(() => MrSidFile.ReadImageInfo(data.AsSpan(0, length)));
    }
  }

  [TestCase("mercator.sid")]
  [TestCase("mercator_new.sid")]
  [Category("Unit")]
  public void CorruptBytes_FailWithADescriptiveExceptionOrReadAHeader(string name) {
    var original = _Fixture(name);
    var random = new Random(626);
    for (var trial = 0; trial < 3000; ++trial) {
      var data = (byte[])original.Clone();
      for (var flips = random.Next(1, 4); flips > 0; --flips)
        data[random.Next(Math.Min(data.Length, trial % 2 == 0 ? 128 : data.Length))] = (byte)random.Next(256);
      try {
        MrSidReader.FromBytes(data);
      } catch (Exception exception) when (exception is InvalidDataException or NotSupportedException) {
      }
      Assert.DoesNotThrow(() => MrSidFile.ReadImageInfo(data));
    }
  }

  [Test]
  [Category("Unit")]
  public void OversizedDimensions_AreRefused() {
    var data = _Fixture("mercator.sid");
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(15), 0xFFFFFFFF);
    Assert.Multiple(() => {
      Assert.Throws<InvalidDataException>(() => MrSidReader.FromBytes(data));
      Assert.That(MrSidFile.ReadImageInfo(data), Is.Null);
    });

    var mg3 = _Fixture("mercator_new.sid");
    BinaryPrimitives.WriteUInt32BigEndian(mg3.AsSpan(0x1E), 0x80000000);
    Assert.Throws<InvalidDataException>(() => MrSidReader.FromBytes(mg3));
  }

  [Test]
  [Category("Unit")]
  public void UnknownMetadataType_StopsTheWalkAndSaysSo() {
    var data = _Fixture("mercator_new.sid");
    var at = Encoding.ASCII.GetString(data).IndexOf("IMAGE::HEIGHT", StringComparison.Ordinal) + "IMAGE::HEIGHT".Length + 1;
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(at), 99);
    var file = MrSidReader.FromBytes(data);
    Assert.Multiple(() => {
      Assert.That(file.HasUnreadMetadata, Is.True);
      Assert.That(file.Metadata.Select(static r => r.Key), Does.Not.Contain("IMAGE::HEIGHT").And.Contain("IMAGE::DYNAMIC_RANGE_WINDOW"));
    });
  }

  /// <summary>
  /// Reads every <c>.sid</c> in the directory named by <c>MRSID_CORPUS</c>. The files are not redistributable: the
  /// generation 2 and 3 pictures and the LiDAR file of https://sembiance.com/fileFormatSamples/image/mrSIDImage/, and
  /// <c>UAE_crop1_100to1.sid</c> from LizardTech's sample page, which Extensis' GeoExpress 10.0.1 wrote. GDAL, built
  /// against the Decode SDK, wrote the <c>.aux.xml</c> beside that one; its geotransform is asserted here.
  /// </summary>
  [Test]
  [Explicit("Needs MRSID_CORPUS pointing at a directory of .sid files.")]
  [Category("Integration")]
  public void DecodesTheCorpus() {
    var directory = Environment.GetEnvironmentVariable("MRSID_CORPUS");
    if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
      Assert.Ignore("MRSID_CORPUS is not set to an existing directory.");

    var expected = new Dictionary<string, (MrSidGeneration Generation, int Width, int Height, int Bands, int Levels)>(StringComparer.OrdinalIgnoreCase) {
      ["ABACUS.SID"] = (MrSidGeneration.Mg2, 1748, 1472, 3, 3),
      ["cd2cos07i01.sid"] = (MrSidGeneration.Mg2, 431, 338, 3, 4),
      ["EXCHANGE.sid"] = (MrSidGeneration.Mg2, 3180, 4000, 1, 4),
      ["oxford.sid"] = (MrSidGeneration.Mg2, 4942, 5441, 3, 4),
      ["spurn.sid"] = (MrSidGeneration.Mg2, 3811, 2485, 3, 4),
      ["Test.sid"] = (MrSidGeneration.Mg2, 1400, 1400, 3, 4),
      ["U.SID"] = (MrSidGeneration.Mg2, 8305, 5632, 3, 5),
      ["WEST.SID"] = (MrSidGeneration.Mg2, 8400, 6000, 3, 5),
      ["meg_cr20.sid"] = (MrSidGeneration.Mg3, 640, 480, 3, 4),
      ["UAE_crop1_100to1.sid"] = (MrSidGeneration.Mg4, 8065, 7297, 4, 7),
    };

    var seen = 0;
    foreach (var path in Directory.EnumerateFiles(directory!)) {
      var name = Path.GetFileName(path);
      if (!name.EndsWith(".sid", StringComparison.OrdinalIgnoreCase))
        continue;

      var data = File.ReadAllBytes(path);
      if (name.StartsWith("Tetons", StringComparison.OrdinalIgnoreCase)) {
        Assert.Throws<NotSupportedException>(() => MrSidReader.FromBytes(data), name);
        ++seen;
        continue;
      }

      var file = MrSidReader.FromBytes(data);
      var info = MrSidFile.ReadImageInfo(data)!.Value;
      Assert.That((info.Width, info.Height), Is.EqualTo((file.Width, file.Height)), name);
      if (expected.TryGetValue(name, out var want))
        Assert.That((file.Generation, file.Width, file.Height, file.BandCount, file.LevelCount), Is.EqualTo(want), name);
      if (name.Equals("UAE_crop1_100to1.sid", StringComparison.OrdinalIgnoreCase)) {
        Assert.That(file.Georeference!.Value.ToGeoTransform(),
          Is.EqualTo(new[] { 3.1177356580527086e+05, 1.5000000000000099e-01, 0, 2.7793994501538211e+06, 0, -1.5000000000001143e-01 }).Within(1e-6));
        Assert.That(file.Find("IMAGE::FORMAT")!.Strings, Is.EqualTo(new[] { "MrSID/MG4" }));
      }
      ++seen;
    }

    Assert.That(seen, Is.GreaterThan(0), "No .sid file found in the corpus directory.");
    TestContext.Out.WriteLine($"{seen} MrSID files read.");
  }

  /// <summary>A generation 4 file in the layout GeoExpress 10 writes: header, codestream box, metadata box, georeference box.</summary>
  private static byte[] _SyntheticMg4() {
    var bytes = new List<byte>();
    var header = new byte[0x3A];
    "msid"u8.CopyTo(header);
    header[4] = 3;
    header[5] = 4;
    BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0x1A), 8065);
    BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0x1E), 7297);
    header[0x22] = 7;
    header[0x24] = 4;
    header[0x28] = 8;
    bytes.AddRange(header);

    void U32(uint value) { var b = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, value); bytes.AddRange(b); }
    void U64(ulong value) { var b = new byte[8]; BinaryPrimitives.WriteUInt64BigEndian(b, value); bytes.AddRange(b); }
    void F64(double value) { var b = new byte[8]; BinaryPrimitives.WriteDoubleBigEndian(b, value); bytes.AddRange(b); }

    bytes.AddRange([0x44, 0x03, 0x00, 0x01]);
    U64(4);
    bytes.AddRange([1, 2, 3, 4]);

    var records = new List<byte>();
    void Record(string key, uint type, byte[] value, uint count = 1) {
      var b = new byte[4];
      BinaryPrimitives.WriteUInt32BigEndian(b, (uint)key.Length + 16);
      records.AddRange(b);
      records.AddRange(Encoding.ASCII.GetBytes(key));
      records.Add(0);
      foreach (var field in new[] { type, count, 1u, count }) {
        BinaryPrimitives.WriteUInt32BigEndian(b, field);
        records.AddRange(b);
      }
      records.AddRange(value);
    }

    Record("IMAGE::SOM", 2, [0, 1]);
    Record("IMAGE::ENCODING_APPLICATION", 5, [.. Encoding.ASCII.GetBytes("GeoExpress 10.0.1.5035"), 0]);
    Record("IMAGE::FORMAT", 5, [.. Encoding.ASCII.GetBytes("MrSID/MG4"), 0]);
    Record("IMAGE::EOM", 2, [0, 1]);
    records.AddRange([0xFF, 0xFF, 0xFF, 0xFF]);

    bytes.AddRange([0x02, 0x43, 0x01]);
    U64((ulong)records.Count + 8);
    U32((uint)records.Count + 4);
    bytes.AddRange([0x00, 0x01, 0x00, 0x00]);
    bytes.AddRange(records);

    bytes.AddRange([0x02, 0x43, 0x03]);
    U64(53);
    U32(49);
    bytes.Add(1);
    foreach (var value in new[] { 311773.6408052709, 2779399.375153821, 312983.2408052709, 2778304.975153821, 0.15, -0.15 })
      F64(value);

    return [.. bytes];
  }
}
