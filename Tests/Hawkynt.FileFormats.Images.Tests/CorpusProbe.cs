using System;
using System.IO;
using FileFormat.Core;

namespace Hawkynt.FileFormats.Images.Tests;

/// <summary>Prints what we make of one corpus sample, for chasing a disagreement down.</summary>
[TestFixture]
[Explicit("Diagnostic: point CORPUS_PROBE at a single sample file.")]
public sealed class CorpusProbe {

  [Test]
  public void Dump() {
    var path = Environment.GetEnvironmentVariable("CORPUS_PROBE")!;
    var image = FormatRegistry.Read(new FileInfo(path));
    Assert.That(image, Is.Not.Null, "nothing read it");

    var bgra = PixelConverter.Convert(image!, PixelFormat.Bgra32);
    Console.WriteLine($"{Path.GetFileName(path)}: {bgra.Width}x{bgra.Height} from {image!.Format}");
    for (var y = 0; y < Math.Min(bgra.Height, 10); ++y) {
      var row = "";
      for (var x = 0; x < Math.Min(bgra.Width, 6); ++x) {
        var at = (y * bgra.Width + x) * 4;
        row += $"({bgra.PixelData[at + 2]},{bgra.PixelData[at + 1]},{bgra.PixelData[at]}) ";
      }
      Console.WriteLine($"  y={y}: {row}");
    }
  }
}
