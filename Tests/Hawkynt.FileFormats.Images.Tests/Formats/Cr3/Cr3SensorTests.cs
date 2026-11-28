using System;
using System.IO;
using System.Security.Cryptography;
using FileFormat.Cr3;

namespace FileFormat.Cr3.Tests;

/// <summary>CRX sensor data, lossless and C-RAW, against LibRaw.</summary>
/// <remarks>
/// The expected mosaics are SHA-256 hashes of the system LibRaw's unpacked <c>raw_image</c> (its
/// little-endian 16-bit samples, rows from the top) for six files of <c>raw.pixls.us/data/Canon</c>:
/// lossless from the EOS M50 and RP, C-RAW of CRX version 1.0 from the RP and the dual-pixel EOS R
/// (two tiles), and C-RAW of version 2.0 from the R6 and R50. The samples are not committed; point
/// <c>CR3_SAMPLES</c> at them.
/// </remarks>
[TestFixture]
public sealed class Cr3SensorTests {

  [TestCase("Canon_EOS_M50_RAW_ISO_100_nocrop_nodual.CR3", 6288, 4056, "41524361C0005D0C1147D335E706507F8374F66686A277BE1E239A98FA679A30")]
  [TestCase("Canon_EOS_RP_RAW_ISO_100_crop_nodual.CR3", 4032, 2656, "FF63F6EF610F8EB181824EEEFF174D3891E4E3BDD2D72DB28D97B4427D46C6CB")]
  [TestCase("Canon_EOS_RP_CRAW_ISO_100_crop_nodual.CR3", 4032, 2656, "55221A455137C64D5609307C3562DAE09B3E67050FE5B10E993068B2D1436FF4")]
  [TestCase("Canon_EOS_R_CRAW_ISO_100_dual_pixel_on.CR3", 6888, 4546, "FAE9309CAA2AD165E9437FD02FB46E6415ED65BFEE4A75B4FA9E1B106E5F89D7")]
  [TestCase("Canon_EOS_R6_CRAW_ISO_100_crop_nodual.CR3", 3584, 2386, "73D26BA5F75C2361D8EB197336335F95E1E41F8FFFB2714FE3F7BB2B3E4B0C27")]
  [TestCase("Canon_R50_ISO100_cRAW_mechanicShutter.CR3", 6192, 4060, "4E6D87E3AE2D0D6082BD19AB601130D9CFF91B3EF05AB8A4AFAE9B5E3C8BD209")]
  [Category("Conformance")]
  [Explicit("Needs the CR3 samples; point CR3_SAMPLES at them.")]
  public void TheMosaicIsLibRawsToTheSample(string name, int width, int height, string sha256) {
    var path = Path.Combine(Environment.GetEnvironmentVariable("CR3_SAMPLES") ?? string.Empty, name);
    if (!File.Exists(path))
      Assert.Ignore("CR3_SAMPLES does not name the sample folder.");
    var file = Cr3Reader.FromFile(new(path));
    var sensor = file.Sensor!;
    Assert.That((sensor.Width, sensor.Height, sensor.BitsPerSample), Is.EqualTo((width, height, 14)));
    var bytes = new byte[sensor.Samples.Length * 2];
    Buffer.BlockCopy(sensor.Samples, 0, bytes, 0, bytes.Length);
    Assert.That(Convert.ToHexString(SHA256.HashData(bytes)), Is.EqualTo(sha256));
    if (name.Contains("dual_pixel_on", StringComparison.Ordinal))
      Assert.That((file.DualPixelSensor?.Width, file.DualPixelSensor?.Height), Is.EqualTo(((int?)width, (int?)height)));
    else
      Assert.That(file.DualPixelSensor, Is.Null);
  }
}
