using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using FileFormat.Core;
using FileFormat.X3f;

namespace FileFormat.X3f.Tests;

/// <summary>Foveon sensor data out of X3F raw sections, against LibRaw.</summary>
/// <remarks>
/// The expected layers are SHA-256 hashes of LibRaw 0.20.2's unpacked <c>color3_image</c> (built with
/// <c>USE_X3FTOOLS</c>, the last LibRaw that reads X3F; its little-endian 16-bit samples, three a
/// site, rows from the top) for the three files of <c>telparia.com/fileFormatSamples/image/x3f</c>:
/// the Polaroid x530's packed Huffman storage, the Sigma SD10's compressed Huffman storage and the
/// Sigma DP1's TRUE engine. The samples are not committed; point <c>X3F_SAMPLES</c> at them.
/// </remarks>
[TestFixture]
public sealed class X3fSensorTests {

  [TestCase("RAW_POLAROID_X530.X3F", 1440, 1088, "570504DFD78F5788B0DB7BF9E837F1530793A7D29A473A83AF55E231FB1C4A7E")]
  [TestCase("RAW_SIGMA_SD10.X3F", 2304, 1531, "44A331AB98772B5C41C5670963D73975CBEDF962AC752FEEAAACAAEDE86C7B35")]
  [TestCase("RAW_SIGMA_DP1.X3F", 2688, 1792, "8488224452C6D70E903ECEDD8EDE9E307787A1880BD769804EE82773E4749318")]
  [Category("Conformance")]
  [Explicit("Needs the x3f samples; point X3F_SAMPLES at them.")]
  public void TheSensorLayersAreLibRawsToTheSample(string name, int width, int height, string sha256) {
    var directory = Environment.GetEnvironmentVariable("X3F_SAMPLES");
    if (string.IsNullOrEmpty(directory) || !File.Exists(Path.Combine(directory, name)))
      Assert.Ignore("X3F_SAMPLES does not name the sample folder.");
    var file = X3fReader.FromFile(new(Path.Combine(directory, name)));
    Assert.That(file.Sensor, Is.Not.Null);
    var sensor = file.Sensor!;
    Assert.That((sensor.Width, sensor.Height), Is.EqualTo((width, height)));
    var bytes = new byte[sensor.Layers.Length * 2];
    Buffer.BlockCopy(sensor.Layers, 0, bytes, 0, bytes.Length);
    Assert.That(Convert.ToHexString(SHA256.HashData(bytes)), Is.EqualTo(sha256));
  }
}
