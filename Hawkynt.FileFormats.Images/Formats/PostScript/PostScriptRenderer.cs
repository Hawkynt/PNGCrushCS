using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Core.Vector;

namespace FileFormat.PostScript;

/// <summary>A rendered page and what can be said about how it was arrived at.</summary>
/// <param name="Image">The page.</param>
/// <param name="SizeSource">Which statement in the file the size came from.</param>
/// <param name="PagesShown">How many pages the program had put out when it was stopped.</param>
/// <param name="HasInk">Whether anything was drawn.</param>
public readonly record struct PostScriptRendering(RawImage Image, string SizeSource, int PagesShown, bool HasInk);

/// <summary>Runs a PostScript program onto a raster of the size the file states.</summary>
public static class PostScriptRenderer {

  /// <summary>Draws the first page.</summary>
  public static PostScriptRendering Render(PostScriptFile file) {
    ArgumentNullException.ThrowIfNull(file.Data);

    var missing = file.Comments.MissingProcedureSets;
    if (missing.Count > 0)
      throw new InvalidDataException(
        $"This file needs the procedure set{(missing.Count == 1 ? "" : "s")} {string.Join(", ", missing)}, which it does not carry. " +
        "Every operator its drawing is made of is defined there, so what it draws cannot be known from the file alone."
      );

    var box = file.Comments.Box;
    if (!box.IsUsable)
      throw new InvalidDataException($"A PostScript file whose page is {box.Width} by {box.Height} points has nothing to draw on.");

    var (width, height) = VectorViewport.Cap(VectorViewport.PixelsFromPoints(box.Width), VectorViewport.PixelsFromPoints(box.Height));
    var viewport = VectorViewport.Fit(box.Left, box.Bottom, box.Right, box.Top, width, height, true);

    var canvas = new VectorCanvas(viewport.Width, viewport.Height, Rgba32.White);
    var page = new PsPage(canvas, viewport.Transform);
    var interpreter = new PostScriptInterpreter(file.Data, file.Start, file.End, page);

    try {
      interpreter.Run();
    } catch (PsUnsupportedException unsupported) {
      throw new InvalidDataException(unsupported.Message, unsupported);
    } catch (PsErrorException error) {
      throw new InvalidDataException($"PostScript error {error.Name} in {interpreter.Running}: {error.Message}", error);
    }

    return new(canvas.ToRawImage(), box.Source, interpreter.PagesShown, page.HasInk);
  }
}
