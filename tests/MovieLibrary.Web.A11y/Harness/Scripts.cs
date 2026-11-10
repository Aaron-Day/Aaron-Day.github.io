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


    /// <summary>
    /// The focused element's own box, hit-tested at five points: its centre and its four corners.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The corners are taken ONE PIXEL INSIDE each edge.</b> A rect's right and bottom are exclusive, so
    /// <c>elementFromPoint(box.right, box.bottom)</c> asks about the pixel AFTER the element and answers
    /// about whatever is next to it. Probing the literal corners reports a correct layout as obscured at
    /// two of its four corners, every time.
    /// </para>
    /// <para>
    /// A probe point outside the viewport is counted separately and is NOT a blocker:
    /// <c>elementFromPoint</c> is defined to return null there, and "scrolled out of view" is not "hidden
    /// by author-created content". Counting it as a blocker would be the hit test crying wolf.
    /// </para>
    /// <para>
    /// Which count belongs to which criterion is <c>ObscuredFocusTests</c>'s business and is written out
    /// there: one clear point is SC 2.4.11 Focus Not Obscured (Minimum), Level AA; all five is the shape of
    /// SC 2.4.12 (Enhanced), which is Level AAA and is reported rather than asserted.
    /// </para>
    /// </remarks>
    public const string FocusObscuration = """
        () => {
          const out = { what: "(nothing)", exists: false, visible: false, width: 0, height: 0,
                        points: 0, clear: 0, offscreen: 0, blockers: [] };
          const a = document.activeElement;
          if (!a || a === document.body || a === document.documentElement) return out;

          out.exists = true;
          const id = a.id ? "#" + a.id : "";
          const cls = typeof a.className === "string" && a.className.trim().length > 0
            ? "." + a.className.trim().split(/\s+/).join(".") : "";
          out.what = a.tagName.toLowerCase() + id + cls;

          const style = getComputedStyle(a);
          const box = a.getBoundingClientRect();
          out.width = Math.round(box.width * 10) / 10;
          out.height = Math.round(box.height * 10) / 10;
          out.visible = style.display !== "none" && style.visibility !== "hidden"
                        && box.width > 0 && box.height > 0;
          if (!out.visible) return out;

          const left = box.left + 1, right = box.right - 1;
          const top = box.top + 1, bottom = box.bottom - 1;
          const probes = [[box.left + box.width / 2, box.top + box.height / 2],
                          [left, top], [right, top], [left, bottom], [right, bottom]];

          for (const point of probes) {
            const x = point[0], y = point[1];
            out.points++;
            if (x < 0 || y < 0 || x > window.innerWidth || y > window.innerHeight) {
              out.offscreen++;
              continue;
            }
            const hit = document.elementFromPoint(x, y);
            if (hit === a || a.contains(hit)) { out.clear++; continue; }
            const hid = hit && hit.id ? "#" + hit.id : "";
            const hcls = hit && typeof hit.className === "string" && hit.className.trim().length > 0
              ? "." + hit.className.trim().split(/\s+/)[0] : "";
            out.blockers.push((hit ? hit.tagName.toLowerCase() + hid + hcls : "(nothing)")
                              + " at " + Math.round(x) + "," + Math.round(y));
          }
          return out;
        }
        """;


    /// <summary>
    /// Whether the page has stopped scrolling: true once six consecutive animation frames report the same
    /// <c>scrollY</c>. <see cref="ForgetScroll"/> resets the sample before each wait.
    /// </summary>
    /// <remarks>
    /// <b>This exists because Bootstrap's reboot sets <c>scroll-behavior: smooth</c> on the root</b>, inside
    /// <c>@media (prefers-reduced-motion: no-preference)</c>. A browser moving focus to a control that is
    /// off the optimal viewing region therefore ANIMATES the scroll, and a hit test taken straight after
    /// the keypress measures a transient position rather than where the control comes to rest. That is not
    /// a theory: measured on this project, the pager buttons read as entirely hidden under the error bar at
    /// 320x256 when probed immediately, and entirely clear of it when probed after the scroll settled. A
    /// gate that reported the first would be reporting a conformance failure that does not exist.
    /// <para>
    /// A condition and not a sleep, which is this harness's rule. The condition is "the position has
    /// stopped changing", which is the only honest signal: the duration of a smooth scroll is the
    /// browser's business and depends on the distance.
    /// </para>
    /// </remarks>
    public const string ScrollSettled = """
        () => {
          const y = Math.round(window.scrollY * 100);
          const was = window.a11yScroll;
          window.a11yScroll = (was && was.y === y) ? { y: y, still: was.still + 1 } : { y: y, still: 0 };
          return window.a11yScroll.still >= 6;
        }
        """;


    /// <summary>Forgets the last scroll sample, so a settle wait cannot pass on a stale one.</summary>
    public const string ForgetScroll = "() => { window.a11yScroll = null; }";


    /// <summary>
    /// Whether the first element matching a selector can actually be reached where it is drawn: "ok", or a
    /// sentence saying why not.
    /// </summary>
    /// <remarks>
    /// A string rather than a bool, because every way this can answer "no" is a different defect and one
    /// word loses which. "(no element matches ...)" is the case a bool would turn into a silent pass the
    /// day a selector stops matching - a hit test against an element that is not there is exactly the
    /// trivially-passing assertion this project keeps catching - so it is a distinct answer and not a
    /// false. <c>display: none</c> comes back verbatim too, because at the nav breakpoint it is the
    /// EXPECTED answer for one of the two controls.
    /// </remarks>
    public const string HitTestableCentre = """
        selector => {
          const el = document.querySelector(selector);
          if (!el) return "(no element matches " + selector + ")";
          const style = getComputedStyle(el);
          if (style.display === "none") return "display: none";
          if (style.visibility === "hidden") return "visibility: hidden";
          const box = el.getBoundingClientRect();
          if (box.width === 0 || box.height === 0)
            return "a zero-sized box, " + box.width + "x" + box.height;
          const x = box.left + box.width / 2, y = box.top + box.height / 2;
          if (x < 0 || y < 0 || x > window.innerWidth || y > window.innerHeight)
            return "its centre is outside the viewport, at " + Math.round(x) + "," + Math.round(y);
          const hit = document.elementFromPoint(x, y);
          if (hit === el || el.contains(hit)) return "ok";
          const id = hit && hit.id ? "#" + hit.id : "";
          const cls = hit && typeof hit.className === "string" && hit.className.trim().length > 0
            ? "." + hit.className.trim().split(/\s+/)[0] : "";
          return "its centre at " + Math.round(x) + "," + Math.round(y) + " hits "
                 + (hit ? hit.tagName.toLowerCase() + id + cls : "nothing");
        }
        """;


    /// <summary>
    /// Every visible element that clips its own overflow and holds more content than fits, with the axis
    /// and the overflow in CSS px.
    /// </summary>
    /// <remarks>
    /// <b>The visually-hidden pattern is exempt, and that carve-out is the reason this needs a comment.</b>
    /// Bootstrap's <c>.visually-hidden</c> is a 1x1 box with <c>overflow: hidden</c> and a clip rect, so
    /// EVERY element carrying it clips content by design - the skip link, the boot shell's heading, the
    /// Menu button's label and all three announcer regions. Without the carve-out this reports six clipped
    /// elements on a correct page and can never go green. Rule T1 then applies to the carve-out in turn,
    /// which is what <see cref="ClippingContainers"/> is for: the caller has to be told how many subjects
    /// survive it rather than assume any did.
    /// </remarks>
    public const string ClippedContent = """
        () => {
          const out = [];
          for (const el of document.querySelectorAll("body *")) {
            if (el.closest(".visually-hidden, .visually-hidden-focusable")) continue;
            const s = getComputedStyle(el);
            if (s.display === "none" || s.visibility === "hidden") continue;
            const clipsY = s.overflowY === "hidden" || s.overflowY === "clip";
            const clipsX = s.overflowX === "hidden" || s.overflowX === "clip";
            if (!clipsY && !clipsX) continue;
            const overY = clipsY ? el.scrollHeight - el.clientHeight : 0;
            const overX = clipsX ? el.scrollWidth - el.clientWidth : 0;
            const id = el.id ? "#" + el.id : "";
            const cls = typeof el.className === "string" && el.className.trim().length > 0
              ? "." + el.className.trim().split(/\s+/)[0] : "";
            const what = el.tagName.toLowerCase() + id + cls;
            if (overY > 1) out.push(what + " clips " + overY + "px of its content vertically");
            if (overX > 1) out.push(what + " clips " + overX + "px of its content horizontally");
          }
          return out;
        }
        """;


    /// <summary>
    /// Every element that clips its own overflow, the exempt pattern aside, whether or not it is
    /// overflowing. The subject floor for <see cref="ClippedContent"/>: rule T1, a scan over an empty set
    /// proves nothing.
    /// </summary>
    public const string ClippingContainers = """
        () => {
          const out = [];
          for (const el of document.querySelectorAll("body *")) {
            if (el.closest(".visually-hidden, .visually-hidden-focusable")) continue;
            const s = getComputedStyle(el);
            if (s.display === "none" || s.visibility === "hidden") continue;
            const clips = ["hidden", "clip"];
            if (!clips.includes(s.overflowY) && !clips.includes(s.overflowX)) continue;
            const id = el.id ? "#" + el.id : "";
            const cls = typeof el.className === "string" && el.className.trim().length > 0
              ? "." + el.className.trim().split(/\s+/)[0] : "";
            out.push(el.tagName.toLowerCase() + id + cls + " overflow " + s.overflowX + "/" + s.overflowY);
          }
          return out;
        }
        """;


    /// <summary>
    /// How an element's text is actually spaced, in a fixed order: what it is, font-size, line-height,
    /// letter-spacing, word-spacing.
    /// </summary>
    /// <remarks>
    /// Two jobs. On a plain page it is where app.css's <b>unitless</b> <c>line-height: 1.5</c> can be seen
    /// to have survived to the computed style, which is the promise SC 1.4.12 Text Spacing (Level AA) rests
    /// on - a unitless value computes to font-size times 1.5 and so grows with a reader's own override,
    /// where <c>line-height: 21px</c> would not. With the overrides injected it is how a test proves the
    /// injection TOOK, which matters more than it sounds: a style tag that silently failed to apply would
    /// leave every assertion afterwards measuring the ordinary page and passing.
    /// </remarks>
    public const string TextMetrics = """
        selector => {
          const el = document.querySelector(selector);
          if (!el) return ["(no element matches " + selector + ")", "", "", "", ""];
          const s = getComputedStyle(el);
          const cls = typeof el.className === "string" && el.className.trim().length > 0
            ? "." + el.className.trim().split(/\s+/)[0] : "";
          return [el.tagName.toLowerCase() + cls,
                  s.fontSize, s.lineHeight, s.letterSpacing, s.wordSpacing];
        }
        """;


    /// <summary>
    /// The focus ring the browser actually painted on <c>document.activeElement</c>, in a fixed order:
    /// what it is, outline-style, outline-width, outline-color, outline-offset, box-shadow.
    /// </summary>
    /// <remarks>
    /// An array in a documented order rather than an object, for the reason <see cref="ScrollReserve"/>
    /// gives. box-shadow is read but not asserted on: it is the other common way to draw a ring, so a
    /// failure message that printed only the outline would send a reader looking for the wrong thing.
    /// </remarks>
    public const string FocusRing = """
        () => {
          const a = document.activeElement;
          if (!a || a === document.body || a === document.documentElement)
            return ["(nothing is focused)", "", "", "", "", ""];
          const id = a.id ? "#" + a.id : "";
          const cls = typeof a.className === "string" && a.className.trim().length > 0
            ? "." + a.className.trim().split(/\s+/).join(".") : "";
          const s = getComputedStyle(a);
          return [a.tagName.toLowerCase() + id + cls,
                  s.outlineStyle, s.outlineWidth, s.outlineColor, s.outlineOffset, s.boxShadow];
        }
        """;


    /// <summary>
    /// An in-prose link's non-colour cue and the two colours it sits between, in a fixed order: what it
    /// is, text-decoration-line, the link's own colour, the surrounding prose's colour.
    /// </summary>
    /// <remarks>
    /// The two colours are read so that a failure message can say what the underline is carrying. The
    /// measured ratio between them is 2.18:1, far below the 3:1 SC 1.4.1 Use of Color (Level A) asks for
    /// before colour alone may distinguish a link inside running text, and that is the whole reason the
    /// decoration rather than the colour is the thing under test.
    /// </remarks>
    public const string InProseLink = """
        selector => {
          const el = document.querySelector(selector);
          if (!el) return ["(no element matches " + selector + ")", "", "", ""];
          const prose = el.parentElement;
          return [el.tagName.toLowerCase() + "[" + (el.textContent || "").trim().slice(0, 32) + "]",
                  getComputedStyle(el).textDecorationLine,
                  getComputedStyle(el).color,
                  prose ? getComputedStyle(prose).color : "(no parent)"];
        }
        """;


    /// <summary>
    /// Every visible control that paints a border at all, with that border's width and resolved colour per
    /// side. One string per control, so there is nothing to deserialise.
    /// </summary>
    /// <remarks>
    /// This exists to put a floor under <see cref="DecorationBorderedControls"/>, which returns only the
    /// WRONG ones: an empty bad-list reads identically whether every boundary is correct or no control has
    /// a boundary at all, and the second is the trivially-passing shape rule T1 is about. A control whose
    /// border goes away is a 1.4.11 question in its own right - <c>.try-again</c> and <c>.nav-toggle</c>
    /// both set <c>background: transparent</c>, so for those two the border is the whole control.
    /// </remarks>
    public const string BorderedControls = """
        () => {
          const selector =
            "a[href], button, [role=button], input:not([type=hidden]), select, textarea, summary";
          const out = [];
          for (const el of document.querySelectorAll(selector)) {
            const s = getComputedStyle(el);
            if (s.display === "none" || s.visibility === "hidden") continue;
            const box = el.getBoundingClientRect();
            if (box.width === 0 || box.height === 0) continue;
            const sides = [];
            for (const side of ["Top", "Right", "Bottom", "Left"]) {
              if (s["border" + side + "Style"] === "none") continue;
              if (parseFloat(s["border" + side + "Width"]) === 0) continue;
              sides.push(side.toLowerCase() + " " + s["border" + side + "Width"]
                         + " " + s["border" + side + "Color"]);
            }
            if (sides.length === 0) continue;
            const id = el.id ? "#" + el.id : "";
            const cls = typeof el.className === "string" && el.className.trim().length > 0
              ? "." + el.className.trim().split(/\s+/).join(".") : "";
            out.push(el.tagName.toLowerCase() + id + cls + ": " + sides.join(", "));
          }
          return out;
        }
        """;


    /// <summary>
    /// Every visible control whose only boundary is drawn in the DECORATION token's colour, which app.css
    /// measures at 1.37:1 and records as failing SC 1.4.11 Non-text Contrast (Level AA).
    /// </summary>
    /// <remarks>
    /// <b>The colour is read off the page rather than retyped here.</b> A hidden probe element is given
    /// <c>color: var(--line)</c> and its computed colour is what the comparison uses, so a change to the
    /// token moves this check with it instead of leaving a stale hex in a test. Retyping
    /// <c>rgb(38, 43, 56)</c> would be a second copy of a measurement, which is the thing this file's
    /// header warns about.
    /// <para>
    /// The control selector is the one <see cref="Controls"/> uses, so the subject set is the same set
    /// <c>TouchTargetReportTests</c> already puts a floor under. It is the SELECTOR half that the source
    /// scan in <c>StyleContractTests</c> cannot do: <c>.try-again</c>, <c>.nav-toggle</c> and
    /// <c>.dismiss</c> are all buttons chosen by class, so no text scan for a control's element name
    /// reaches them, while this reaches every control the page actually renders.
    /// </para>
    /// </remarks>
    public const string DecorationBorderedControls = """
        () => {
          const probe = document.createElement("span");
          probe.style.display = "none";
          probe.style.color = "var(--line)";
          document.body.appendChild(probe);
          const decoration = getComputedStyle(probe).color;
          probe.remove();

          const selector =
            "a[href], button, [role=button], input:not([type=hidden]), select, textarea, summary";
          const out = [];
          for (const el of document.querySelectorAll(selector)) {
            const s = getComputedStyle(el);
            if (s.display === "none" || s.visibility === "hidden") continue;
            const box = el.getBoundingClientRect();
            if (box.width === 0 || box.height === 0) continue;
            const bad = [];
            for (const side of ["Top", "Right", "Bottom", "Left"]) {
              if (s["border" + side + "Style"] === "none") continue;
              if (parseFloat(s["border" + side + "Width"]) === 0) continue;
              if (s["border" + side + "Color"] === decoration) bad.push(side.toLowerCase());
            }
            if (bad.length === 0) continue;
            const id = el.id ? "#" + el.id : "";
            const cls = typeof el.className === "string" && el.className.trim().length > 0
              ? "." + el.className.trim().split(/\s+/).join(".") : "";
            out.push(el.tagName.toLowerCase() + id + cls
                     + " [" + (el.textContent || "").trim().replace(/\s+/g, " ").slice(0, 24) + "] "
                     + bad.join("+") + " drawn in " + decoration);
          }
          return out;
        }
        """;
}
