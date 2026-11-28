using System;
using System.Collections.Generic;
using FileFormat.Pdf.Fonts.Data;

namespace FileFormat.Pdf.Fonts;

/// <summary>
/// An installed font drawing a CID font of one of Adobe's CJK collections that the document does
/// not embed: which of its glyphs each CID is.
/// </summary>
/// <remarks>
/// A CID-keyed CFF face keyed by the same collection holds the CIDs as they are, and is read by CID.
/// Any other face is reached through Unicode, by the rules poppler follows for a substituted CID
/// font (<c>GfxCIDFont::getCodeToGIDMap</c>, used as the specification, the code not taken): the
/// CID's first and then second character from the horizontal Unicode CMaps through the face's cmap;
/// failing both, its character from the vertical CMaps, or from Adobe's ToUnicode CMap, through the
/// cmap and on to the face's vertical alternate (GSUB 'vrt2' or 'vert' in the collection's script),
/// which is what the vertical-form CIDs are; failing that, the ToUnicode character itself; and a CID
/// that is one of the Unicode spaces the space of the face. What is still not found is glyph 0, the
/// face's .notdef, as poppler draws it. Advances are the PDF font's own widths, never the face's.
/// </remarks>
internal sealed class PdfCidSubstitute {

  private static readonly int[] _Spaces = [0x2000, 0x2001, 0x2002, 0x2003, 0x2004, 0x2005, 0x2006, 0x2007, 0x2008, 0x2009, 0x200A, 0x00A0, 0x200B, 0x2060, 0x3000, 0xFEFF];

  private readonly PdfSystemFace _face;
  private readonly PdfCidToUnicode _table;
  private readonly OpenTypeVerticalForms? _vertical;
  private readonly bool _verticalWriting;
  private readonly bool _byCid;
  private readonly Dictionary<int, int> _glyphs = [];

  public PdfCidSubstitute(PdfSystemFace face, PdfCidToUnicode table, bool verticalWriting) {
    this._face = face;
    this._table = table;
    this._verticalWriting = verticalWriting;
    this._byCid = face.CharacterCollection is { Registry: "Adobe" } ros && ros.Ordering == table.Ordering && face.Program is CffFontProgram { IsCidKeyed: true };
    var (script, language) = PdfCidToUnicode.LayoutTags(table.Ordering);
    this._vertical = this._byCid ? null : face.VerticalForms(script, language);
  }

  /// <summary>The installed face.</summary>
  public PdfSystemFace Face => this._face;

  /// <summary>Whether the face is read by CID, being keyed by the PDF font's own collection.</summary>
  public bool ByCid => this._byCid;

  /// <summary>The face's glyph for a CID: 0, its .notdef, when it has none.</summary>
  public int GlyphOf(int cid) {
    if (this._glyphs.TryGetValue(cid, out var gid))
      return gid;

    gid = this._Find(cid);
    this._glyphs[cid] = gid;
    return gid;
  }

  /// <summary>The outline of one of the face's glyphs, read under the face's lock since faces are shared.</summary>
  public GlyphOutline? Outline(int glyphId) {
    lock (this._face)
      return this._face.Program.Outline(glyphId);
  }

  private int _Find(int cid) {
    if (this._byCid)
      return Math.Max(((CffFontProgram)this._face.Program).GlyphIdOfCid(cid), 0);

    var table = this._table;
    var gid = 0;
    for (var candidate = 0; candidate < 2 && gid == 0; ++candidate) {
      var unicode = table.Horizontal(cid, candidate);
      if (unicode == 0)
        break;
      gid = this._Cmap(unicode);
    }

    var adobe = table.Adobe(cid);
    if (gid == 0 && table.Vertical(cid) is var vertical and not 0) {
      gid = this._Vertical(this._Cmap(vertical));
      if (gid == 0 && adobe != 0)
        gid = this._Vertical(this._Cmap(adobe));
    }

    if (gid == 0 && adobe != 0)
      gid = this._Cmap(adobe);

    if (gid == 0) {
      var character = table.Horizontal(cid, 0);
      if (Array.IndexOf(_Spaces, character != 0 ? character : adobe) >= 0) {
        gid = this._Cmap(0x20);
        if (this._verticalWriting)
          gid = this._Vertical(gid);
      }
    }

    return gid;
  }

  private int _Cmap(int unicode) => Math.Max(this._face.Cmap.Lookup(unicode), 0);

  private int _Vertical(int gid) => this._vertical == null ? gid : this._vertical.Substitute(gid) is var v and > 0 ? v : gid;
}
