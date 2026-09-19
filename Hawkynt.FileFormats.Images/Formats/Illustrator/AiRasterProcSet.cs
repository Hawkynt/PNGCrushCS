namespace FileFormat.Illustrator;

/// <summary>
/// The procedure set an Illustrator file carries so that its own <c>XI</c> raster operator means
/// something to an interpreter that is not Illustrator.
/// </summary>
/// <remarks>
/// <c>XI</c> is Adobe's, not PostScript's. Nothing in the language defines it, so a file that uses it
/// and supplies nothing stops any general interpreter dead — Ghostscript answers
/// <c>Error: /undefined in XI</c> and renders nothing at all. Illustrator's own files do not have
/// that problem and not because Illustrator is the only thing that reads them: they carry the
/// <c>Adobe_Illustrator_AI5</c> procedure set in their prolog, which defines every private operator
/// the document goes on to use. This is the same arrangement in miniature, covering the one operator
/// this writer emits.
/// <para/>
/// The awkward part is where the samples live. The specification puts them on lines that begin with
/// a percent sign, so that an application which does not know the operator skips them as comments —
/// which also means the scanner never hands them to anything, and <c>readhexstring</c> cannot be
/// pointed at them because the percent sign is not a hex digit. So the definition reads the lines
/// itself, one at a time, drops everything that is not a hex digit, and hands <c>colorimage</c> a
/// row at a time out of what it has decoded. Reading whole lines rather than stopping mid-line is
/// what leaves the file positioned on <c>%AI5_EndRaster</c> when the picture is finished, instead of
/// half-way through a line of hex the scanner would then try to execute.
/// </remarks>
internal static class AiRasterProcSet {

  /// <summary>What the resource calls itself, in the three parts DSC names a procedure set with.</summary>
  public const string Name = "Hawkynt_Illustrator_Raster 1.0 0";

  /// <summary>The definition, ending in a newline.</summary>
  public const string Definition = """
    /XIdict 32 dict def
    XIdict begin
      /buf 3 string def
      /pend 4096 string def
      /pendn 0 def
      /pendat 0 def
      /line 4096 string def
      /hi -1 def
      /at 0 def
    end
    /XIfeed {
      XIdict begin
        /pendn 0 def
        /pendat 0 def
        /hi -1 def
        currentfile line readline
        {
          /s exch def
          0 1 s length 1 sub {
            s exch get
            dup 48 lt
            { pop }
            {
              dup 57 gt { 55 sub } { 48 sub } ifelse
              hi 0 lt
              { /hi exch def }
              {
                hi 16 mul add
                pend pendn 3 -1 roll put
                /pendn pendn 1 add def
                /hi -1 def
              } ifelse
            } ifelse
          } for
        }
        { pop } ifelse
      end
    } bind def
    /XIrow {
      XIdict begin
        /at 0 def
        {
          at buf length ge { exit } if
          pendat pendn ge { XIfeed } if
          pendat pendn ge { exit } if
          buf at pend pendat get put
          /at at 1 add def
          /pendat pendat 1 add def
        } loop
        buf
      end
    } bind def
    /XI {
      XIdict begin
        pop pop pop pop pop pop
        /ph exch def
        /pw exch def
        /ury exch def
        /urx exch def
        /lly exch def
        /llx exch def
        /mtx exch def
        /buf pw 3 mul string def
        /pendn 0 def
        /pendat 0 def
        /hi -1 def
        gsave
          mtx concat
          llx lly translate
          urx llx sub ury lly sub scale
          pw ph 8
          [ pw 0 0 ph neg 0 ph ]
          { XIrow }
          false 3 colorimage
        grestore
      end
    } bind def

    """;
}
