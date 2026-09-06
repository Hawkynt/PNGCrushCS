using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FileFormat.Core;
using FileFormat.Graph2Font;

namespace FileFormat.Graph2FontScroll;

/// <summary>Writes a Graph2Font vertical scroll and the projects it names.</summary>
/// <remarks>
/// A scroll holds no picture: what is filed under its own name is a list of names, and the picture
/// is in the projects that list points at. So the encode that hands back one array of bytes hands
/// back the list alone, and the write that names a file is the one that can also put the projects
/// where the list says they are.
/// <para/>
/// The names are derived from the scroll's own, which is what makes the pair movable: a scroll and
/// its projects share a stem, so the set is recognisable as one picture in a directory holding
/// several.
/// </remarks>
public static class Graph2FontScrollWriter {

  /// <summary>What a named project is called.</summary>
  public const string ProjectExtension = ".g2f";

  /// <summary>The stem a scroll's projects take when the scroll has no name to lend them.</summary>
  public const string DefaultStem = "frame";

  /// <summary>Writes the list, which is the whole of the file that carries the scroll's own name.</summary>
  public static byte[] ToBytes(Graph2FontScrollFile file) {
    var names = file.Names ?? [];
    if (names.Count == 0)
      throw new InvalidDataException("Nothing to write: a scroll naming no projects is not a picture.");

    var list = new StringBuilder();
    foreach (var name in names)
      list.Append(_Checked(name)).Append("\r\n");

    return Encoding.ASCII.GetBytes(list.ToString());
  }

  /// <summary>Puts the named projects beside the list that names them.</summary>
  public static void WriteCompanions(Graph2FontScrollFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);

    var names = file.Names ?? [];
    var frames = file.Frames ?? [];
    if (names.Count != frames.Count)
      throw new InvalidDataException(
        $"A scroll names {names.Count} projects and carries {frames.Count}, so what it names is not what it has. "
        + "Reading one from bytes alone gives the names without the projects, and those cannot be written.");

    var directory = target.DirectoryName
      ?? throw new InvalidDataException("A scroll names files beside it, so it needs a directory to be beside.");

    for (var i = 0; i < names.Count; ++i) {
      var path = Path.GetFullPath(Path.Combine(directory, _Checked(names[i])));
      if (string.Equals(path, target.FullName, StringComparison.OrdinalIgnoreCase))
        throw new InvalidDataException("A scroll's project cannot overwrite the list that names it.");

      File.WriteAllBytes(path, Graph2FontWriter.ToBytes(new() { Data = frames[i] }));
    }
  }

  /// <summary>Builds a scroll, and the projects it names, around a picture.</summary>
  /// <remarks>
  /// A scroll is screen-sized pieces stacked, and nothing else — the editor could edit one screen,
  /// so a taller picture was cut into screens. That is the whole of what this decides: how many
  /// pieces, and which rows go in each. Everything below a piece belongs to Graph2Font and is left
  /// to its encoder.
  /// <para/>
  /// Which is why a picture of any other shape is refused rather than fitted. Scaling one to a whole
  /// number of screens means choosing that number for the caller, and the two plausible choices —
  /// round the rows up, or squeeze them into fewer screens — give different pictures with nothing to
  /// separate them. A picture already this shape has one reading, and that is the one written.
  /// </remarks>
  public static Graph2FontScrollFile Encode(RawImage image, string stem) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != Graph2FontScrollFile.Width)
      throw new ArgumentException(
        $"A Graph2Font scroll is {Graph2FontScrollFile.Width} pixels across, not {image.Width}.", nameof(image));

    if (image.Height <= 0 || image.Height % Graph2FontScrollFile.FrameHeight != 0)
      throw new ArgumentException(
        $"A Graph2Font scroll stacks whole {Graph2FontScrollFile.FrameHeight}-row screens, so it cannot hold a "
        + $"picture {image.Height} rows tall.", nameof(image));

    var rgb = image.EnsureFormat(PixelFormat.Rgb24);
    var count = image.Height / Graph2FontScrollFile.FrameHeight;
    var stride = Graph2FontScrollFile.Width * 3;
    var frames = new List<byte[]>(count);
    var names = new List<string>(count);

    for (var i = 0; i < count; ++i) {
      var band = new byte[stride * Graph2FontScrollFile.FrameHeight];
      Array.Copy(rgb.PixelData, i * band.Length, band, 0, band.Length);

      frames.Add(Graph2FontFile.FromRawImage(new() {
        Width = Graph2FontScrollFile.Width,
        Height = Graph2FontScrollFile.FrameHeight,
        Format = PixelFormat.Rgb24,
        PixelData = band,
      }).Data);

      names.Add(_Checked(stem + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + ProjectExtension));
    }

    return new() { Frames = frames, Names = names };
  }

  /// <summary>The stem the projects beside <paramref name="target"/> take.</summary>
  public static string StemFor(FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);

    var stem = Path.GetFileNameWithoutExtension(target.Name);

    return string.IsNullOrEmpty(stem) ? DefaultStem : stem;
  }

  /// <summary>
  /// Refuses a name a scroll cannot carry, by saying which name and why.
  /// </summary>
  /// <remarks>
  /// The same rule the reader applies, because it is the format: printable ASCII only, and the path
  /// separators refused specifically so that nothing a scroll names can sit outside the directory
  /// the scroll is in. A name derived from the file the caller asked for can break either rule, and
  /// quietly rewriting it would file the projects under names the list does not hold.
  /// </remarks>
  private static string _Checked(string name) {
    if (string.IsNullOrEmpty(name))
      throw new InvalidDataException("A scroll cannot name a project with an empty name.");

    foreach (var c in name)
      switch (c) {
        case '/':
        case ':':
        case '\\':
          throw new InvalidDataException(
            $"A scroll names files beside it, and \"{name}\" would climb out of its own directory.");

        default:
          if (c < ' ' || c > '~')
            throw new InvalidDataException(
              $"A scroll's list is printable ASCII, and \"{name}\" is not — so the scroll it went into "
              + "would no longer be readable as one.");

          break;
      }

    return name;
  }
}
