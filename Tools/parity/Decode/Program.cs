using System;
using System.IO;
using System.Linq;
using System.Text;
using FileFormat.Core;
using Hawkynt.FileFormats.Images;

if (args.Length < 2) {
  Console.Error.WriteLine("usage: Decode <sample directory> <output directory>");
  return 2;
}

var samples = args[0];
var output = args[1];
Directory.CreateDirectory(output);

// The file is read by name rather than by bytes: several families share one layout across a set of
// extensions, and the extension is the only thing saying which variant a file is.
var written = 0;
var total = 0;
foreach (var path in Directory.GetFiles(samples).OrderBy(x => x, StringComparer.Ordinal)) {
  ++total;
  try {
    var image = FormatRegistry.Read(new FileInfo(path));
    if (image == null || image.Width <= 0 || image.Height <= 0)
      continue;

    // A picture far larger than any of these formats can hold is a misidentification, not a decode.
    if ((long)image.Width * image.Height > 40_000_000)
      continue;

    var rgb = image.ToRgb24();
    var wanted = (long)image.Width * image.Height * 3;
    if (rgb.LongLength < wanted)
      continue;

    using var file = File.Create(Path.Combine(output, Path.GetFileName(path) + ".ppm"));
    file.Write(Encoding.ASCII.GetBytes($"P6\n{image.Width} {image.Height}\n255\n"));
    file.Write(rgb, 0, (int)wanted);
    ++written;
  } catch (Exception) {
    // Refusing a file is an answer, and the comparison counts it as one.
  }
}

Console.WriteLine($"we decoded {written} of {total} samples");
return 0;
