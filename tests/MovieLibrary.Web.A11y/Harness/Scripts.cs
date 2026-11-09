namespace MovieLibrary.Web.A11y.Harness;

/// <summary>
/// The page scripts this gate evaluates in the browser.
/// </summary>
/// <remarks>
/// They are gathered here because most of them observe something no .NET-side test can see at all -
/// document.activeElement, a computed style, an element's own rendered box - and because a measurement
/// written out twice is a measurement that can come to disagree with itself. Strings are double-quoted
/// throughout so that each script survives being read as one C# literal.
/// </remarks>
internal static class Scripts
{
    /// <summary>Whether the grid holds exactly this many tiles.</summary>
    public const string TileCountIs =
        "n => document.querySelectorAll(\"ul.grid[role=list] > li\").length === n";

    /// <summary>Whether a region, by id, holds exactly this text.</summary>
    public const string RegionHolds =
        "a => { const el = document.getElementById(a[0]); return !!el && el.textContent === a[1]; }";

    /// <summary>The id of whatever holds focus, empty when the body or nothing does.</summary>
    public const string FocusedId =
        "() => document.activeElement ? document.activeElement.id : \"\"";

    /// <summary>A short description of whatever holds focus, for a failure message a reader can act on.</summary>
    public const string DescribeFocus = """
        () => {
          const a = document.activeElement;
          if (!a) return "(nothing)";
          const id = a.id ? "#" + a.id : "";
          const cls = typeof a.className === "string" && a.className.trim().length > 0
            ? "." + a.className.trim().split(/\s+/).join(".")
            : "";
          const text = (a.textContent || "").trim().replace(/\s+/g, " ").slice(0, 48);
          return a.tagName.toLowerCase() + id + cls + " [" + text + "]";
        }
        """;

    /// <summary>The document's own horizontal scroll figures: scrollWidth, then clientWidth.</summary>
    public const string DocumentWidths = """
        () => {
          const e = document.scrollingElement || document.documentElement;
          return [e.scrollWidth, e.clientWidth];
        }
        """;

    /// <summary>
    /// Every visible element whose right edge sits past the viewport, described with its overflow in CSS px.
    /// </summary>
    /// <remarks>
    /// The document metric alone is not enough. document.scrollingElement.scrollWidth stays equal to
    /// clientWidth whenever the overflowing child sits inside an overflow: hidden ancestor, and the reader
    /// still cannot see the part that is off-screen. The 1 px tolerance is not slack: subpixel rounding of a
    /// fractional grid track routinely puts a right edge at innerWidth + 0.5, and without the tolerance this
    /// would cry wolf over a layout that is correct.
    /// </remarks>
    public const string Overflowing = """
        () => {
          const limit = window.innerWidth + 1;
          const out = [];
          for (const el of document.querySelectorAll("body *")) {
            const style = getComputedStyle(el);
            if (style.visibility === "hidden" || style.display === "none") continue;
            const box = el.getBoundingClientRect();
            if (box.width === 0 && box.height === 0) continue;
            if (box.right <= limit) continue;
            const id = el.id ? "#" + el.id : "";
            const cls = typeof el.className === "string" && el.className.trim().length > 0
              ? "." + el.className.trim().split(/\s+/)[0]
              : "";
            out.push(el.tagName.toLowerCase() + id + cls
                     + " right=" + box.right.toFixed(1)
                     + " over=" + (box.right - window.innerWidth).toFixed(1));
          }
          return out;
        }
        """;

    /// <summary>
    /// Every visible standalone control, with its rendered box. This feeds the print-only house-rule report
    /// on --touch: 44px. The conformance question belongs to axe's own target-size rule, which the tag list
    /// enables; nothing here asserts a size.
    /// </summary>
    public const string Controls = """
        () => {
          const selector = "a[href], button, [role=button], input:not([type=hidden]), select, textarea, summary";
          const out = [];
          for (const el of document.querySelectorAll(selector)) {
            const style = getComputedStyle(el);
            if (style.visibility === "hidden" || style.display === "none") continue;
            const box = el.getBoundingClientRect();
            if (box.width === 0 || box.height === 0) continue;
            const id = el.id ? "#" + el.id : "";
            const cls = typeof el.className === "string" && el.className.trim().length > 0
              ? "." + el.className.trim().split(/\s+/).join(".")
              : "";
            out.push({
              what: el.tagName.toLowerCase() + id + cls,
              name: (el.getAttribute("aria-label") || el.textContent || "").trim()
                      .replace(/\s+/g, " ").slice(0, 40),
              width: Math.round(box.width * 10) / 10,
              height: Math.round(box.height * 10) / 10
            });
          }
          return out;
        }
        """;

    /// <summary>
    /// The resolved scroll-padding-bottom on the root in CSS px, beside the two values it is the maximum of
    /// and the viewport height that decides between them: [resolved, 6rem, --bar-max, viewport]. app.css
    /// reserves max(6rem, var(--bar-max)) so that a control scrolled into view is never left under the fixed
    /// error bar.
    /// </summary>
    /// <remarks>
    /// An array and not an object, because Playwright's .NET binding deserialises a returned object only
    /// into a type it can construct with no arguments and fill by property, and it rejects a record outright
    /// with "Return type mismatch ... got Object". Four doubles in a known order cost one comment.
    /// </remarks>
    public const string ScrollReserve = """
        () => {
          const root = getComputedStyle(document.documentElement);
          const rem = parseFloat(root.fontSize);
          return [
            parseFloat(root.scrollPaddingBottom),
            6 * rem,
            0.4 * window.innerHeight,
            window.innerHeight
          ];
        }
        """;

    /// <summary>
    /// The number of grid columns the layout actually produced, read off the resolved template rather than
    /// counted from tile positions, so a wrapped row cannot be mistaken for a column.
    /// </summary>
    public const string GridColumns = """
        () => {
          const grid = document.querySelector("ul.grid");
          if (!grid) return 0;
          return getComputedStyle(grid).gridTemplateColumns.split(" ").filter(v => v.length > 0).length;
        }
        """;

    /// <summary>The computed display of an element, by id.</summary>
    public const string DisplayOf = """
        id => {
          const el = document.getElementById(id);
          return el ? getComputedStyle(el).display : "(absent)";
        }
        """;

    /// <summary>The computed display of the first element matching a selector.</summary>
    public const string DisplayOfSelector = """
        selector => {
          const el = document.querySelector(selector);
          return el ? getComputedStyle(el).display : "(absent)";
        }
        """;

    /// <summary>
    /// Reveals #blazor-error-ui the way the Blazor runtime itself does, with an inline display: block. A
    /// stylesheet cannot override that inline style, which is exactly why app.css puts the bar's layout rules
    /// on .error-ui-inner instead.
    /// </summary>
    public const string RevealErrorBar = """
        () => {
          const bar = document.getElementById("blazor-error-ui");
          if (!bar) return false;
          bar.style.display = "block";
          return true;
        }
        """;

    /// <summary>Every element in the document that claims an assertive announcement channel.</summary>
    public const string AssertiveChannels = """
        () => Array.from(document.querySelectorAll("[role=alert], [aria-live=assertive]"))
                   .map(el => el.tagName.toLowerCase() + (el.id ? "#" + el.id : ""))
        """;

    /// <summary>Whether an element has an attribute at all, which is not the same as its value being empty.</summary>
    public const string HasAttribute =
        "a => { const el = document.getElementById(a[0]); return !!el && el.hasAttribute(a[1]); }";

    /// <summary>
    /// Whether the pager button with this label carries no real disabled state and still takes focus.
    /// Asserted against the DOM because Playwright's own Locator.IsDisabledAsync reports an aria-disabled
    /// button as disabled, which is its actionability heuristic and not what the markup says.
    /// </summary>
    /// <remarks>
    /// It RESTORES focus afterwards. ExpectPagerAsync runs this immediately before every Ready-state scan,
    /// so without the restore a probe named "stays focusable" would be the thing deciding where focus is
    /// for the scan that follows. Harmless for axe, which reads no focus, but a measurement that quietly
    /// changes the state it measures is not one to leave lying about.
    /// </remarks>
    public const string TakesFocusAndIsNotDisabled = """
        label => {
          const buttons = Array.from(document.querySelectorAll("nav.pager button"));
          const el = buttons.find(b => (b.textContent || "").trim() === label);
          if (!el) return false;
          if (el.hasAttribute("disabled") || el.disabled) return false;
          const was = document.activeElement;
          el.focus();
          const took = document.activeElement === el;
          if (was instanceof HTMLElement) was.focus(); else el.blur();
          return took;
        }
        """;

    /// <summary>
    /// How this document was navigated to, from its Navigation Timing entry: "reload", "navigate",
    /// "back_forward" or "prerender". A document replaced by following a link reports "navigate"; only
    /// location.reload(), or the browser's own reload control, reports "reload".
    /// </summary>
    public const string NavigationType = """
        () => {
          const entry = performance.getEntriesByType("navigation")[0];
          return entry ? entry.type : "(no navigation entry)";
        }
        """;

    /// <summary>Marks the window, so that a reload can be detected by the mark having gone.</summary>
    public const string Mark = "() => { window.a11yReloadMark = true; }";

    /// <summary>Whether the mark survives, which is to say whether this is still the same document.</summary>
    public const string MarkSurvives = "() => window.a11yReloadMark === true";
}
