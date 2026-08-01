using System;
using System.Collections.Generic;

namespace FileFormat.GrassSlideshow;

/// <summary>Assembles a Grass' Slideshow picture from a <see cref="GrassSlideshowFile"/>.</summary>
public static class GrassSlideshowWriter {

  public static byte[] ToBytes(GrassSlideshowFile file) {
    var screen = file.ScreenData ?? [];
    var packed = new List<byte>(GrassSlideshowFile.ScreenSize + 64);

    // Runs of one repeated byte cost three and save from four upward; anything else goes out as
    // literals, and a literal run's count is a byte, so it stops at 255.
    var at = 0;
    while (at < GrassSlideshowFile.ScreenSize) {
      var value = at < screen.Length ? screen[at] : (byte)0;

      var run = 1;
      while (run < 255 && at + run < GrassSlideshowFile.ScreenSize
             && (at + run < screen.Length ? screen[at + run] : (byte)0) == value)
        ++run;

      if (run >= 4) {
        packed.Add(0);
        packed.Add(value);
        packed.Add((byte)run);
        at += run;
        continue;
      }

      // Gather literals up to the next worthwhile repeat.
      var start = at;
      while (at < GrassSlideshowFile.ScreenSize && at - start < 255) {
        var here = at < screen.Length ? screen[at] : (byte)0;
        var ahead = 1;
        while (ahead < 4 && at + ahead < GrassSlideshowFile.ScreenSize
               && (at + ahead < screen.Length ? screen[at + ahead] : (byte)0) == here)
          ++ahead;

        if (ahead >= 4)
          break;

        ++at;
      }

      packed.Add((byte)(at - start));
      for (var i = start; i < at; ++i)
        packed.Add(i < screen.Length ? screen[i] : (byte)0);
    }

    // The byte after the picture names one of the program's built-in register sets. None of them
    // is written: naming a set the picture was not drawn against would recolour it, and the
    // fallback set is the one this encoder quantised to.
    packed.Add(GrassSlideshowFile.UnnamedRegisterSet);

    return packed.ToArray();
  }
}
