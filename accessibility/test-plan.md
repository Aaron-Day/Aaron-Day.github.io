# Accessibility test plan: the films page

This is a checklist for a person with real assistive technology. Nothing in it has been run. Automated tests
cover markup rules and the browser gate covers axe scans, focus order and reflow; they cannot say what a screen
reader announces or whether a person can use the page. That is what these passes are for.

**Every result box below is empty on purpose.** Fill one in only after you have done the pass yourself.

## Before you start

You need the site running somewhere you can open on each device. Use a build with a realistic library, not a
three-film demo: several of the problems below only show up with a full page of 60 tiles and 19 genres.

### What the page is

One page, "Films", at `/` and `/movies`. From top to bottom:

1. A skip link ("Skip to main content"), invisible until it takes focus, then the site nav (brand link, a Menu
   button on narrow screens, the nav links).
2. The heading "Films" and a sentence stating how many films the library holds.
3. A filter panel, one landmark of type *search*: a search box, "Match confidence" (three radio buttons), two
   collapsible groups "Genre" and "Decade", and a "Sort by" menu.
4. A line such as "Showing 1-60 of 1,456 titles.", then a "Pagination" navigation with Previous, "Page 1 of
   25" and Next, then the grid of film tiles.
5. A red error bar fixed to the bottom of the window, hidden unless the app crashes.

A tile shows a poster, a title, a year, and for some films a badge reading **Needs review** or **Not matched**.
Tiles are not links yet: nothing inside the grid is focusable, and that is correct for now.

### The four sample films

The names of real films change, so each sample is described by what it is. Pick one of each from the page.
The example was true on 16 November 2026 for the data the project had then; pick another if it has moved.

| Sample | What to look for | Why this pass needs it |
|---|---|---|
| A. Ordinary | Poster, title, year, no badge | The normal case |
| B. Needs review | Badge "Needs review", with a real poster | A badge over real artwork |
| C. Not matched | Badge "Not matched", no poster (grey box), title, and maybe a runtime, no year | The degraded tile |
| D. Not matched, long title | As C, with a title longer than two lines on a phone | The title must wrap or clamp and never be cut by code |

Example as of 2026-11-16: B is a film in the library's review queue; C and D are rows with no TMDB match. The
`--fixtures` data set used for development has 17 not-matched films, if you are testing against that.

### Recording a result

For each pass fill in the box under it: the date, who ran it, the exact software versions, **Pass** or
**Fail**, and notes. When a step fails, write down the step number, what you heard or saw, and what you
expected, and then open an issue. Do not "fix" the page to make a pass go green.

## Pass 1. NVDA with Chrome, and NVDA with Firefox, on Windows

Run it twice, once in each browser. Start NVDA, then open the page. (NVDA's Speech Viewer, under Tools, lets
you read what was said instead of listening.)

1. **Route announcement.** Open the page fresh, then use the nav to go to a page that does not exist (type a bad
   address) and back to Films with the nav link.
   *Correct:* after each navigation focus lands on the page's main heading and the heading is read, and
   "Films" or "Page not found" is spoken once. Nothing is announced on the very first load.
   *If it fails:* note whether nothing, or two announcements, were spoken.
2. **Landmarks.** Press `D` to cycle landmarks, then `H` for headings.
   *Correct:* Main navigation, a search landmark, Pagination navigation. A heading "Films".
3. **Grid traversal.** From the Pagination navigation, press `Down arrow` through the list.
   *Correct:* NVDA says "list with 60 items", then each tile as plain text: the title, the year, and for a
   B or C film the badge words. There is no "link" or "button" on a tile.
   *If it fails:* anything that says "clickable", reads a file name, or reads a tile as one run-on sentence
   with its neighbour.
4. **Reported totals.** Read the line above the pager, then press Next.
   *Correct:* "Page 2 of 25. Showing 61 to 120 of 1,456 titles." is spoken once, and the visible text matches in
   meaning.
5. **Filter count.** Tab to the Genre group, press `Space` to open it, tick one genre, then stop typing and
   wait about two seconds.
   *Correct:* one sentence, such as "212 titles match.", spoken once after you stop. Not one per keystroke and
   not twice. The Genre summary now reads "Genre (1)".
6. **Unmatched tile name.** Arrow to sample C, then D.
   *Correct:* the title is spoken, then "Not matched". Any year or runtime that is absent is simply not
   mentioned: NVDA must not read "undefined", "null", "0" or "year unknown" for it.
7. **Disclosure.** Tab through the panel with every group closed.
   *Correct:* search box, one stop for the radios (arrows move between the three), "Genre" collapsed,
   "Decade" collapsed, "Sort by". Opening a group says "expanded". A group holding a selection starts open.

Result, NVDA + Chrome:  [ ] Pass  [ ] Fail    Date: ______  By: ______  Versions: ______
Notes:

Result, NVDA + Firefox: [ ] Pass  [ ] Fail    Date: ______  By: ______  Versions: ______
Notes:

## Pass 2. VoiceOver with Safari, on iPhone and on iPad

Mandatory: touch is a declared target of this site. Use a real device, not the simulator, and test both. Turn
VoiceOver on in Settings, Accessibility.

1. **Swipe through the grid.** Swipe right from the heading through the filter panel and the pager into the
   tiles. *Correct:* every control is reached in order; each tile is read as title, year, badge. A closed
   group is one swipe stop; double-tap opens it and VoiceOver says "expanded".
2. **Rotor, Headings.** Turn the rotor to Headings. *Correct:* a "Films" heading is listed.
3. **Rotor, Links.** *Correct:* the nav links and the brand are listed; **no** tile is listed as a link.
4. **Filter count.** Tick a genre and wait. *Correct:* the count sentence is spoken once.
5. **Search box.** Double-tap it. *Correct:* the page does not zoom in when the keyboard opens.
6. **Error bar.** See "Extra: the error bar on mobile screen readers" below.

Result, iPhone: [ ] Pass  [ ] Fail    Date: ______  By: ______  Device and iOS: ______
Result, iPad:   [ ] Pass  [ ] Fail    Date: ______  By: ______  Device and iPadOS: ______
Notes:

## Pass 3. TalkBack with Chrome, on Android (if a device is available)

Turn on TalkBack. Repeat steps 1 to 5 of Pass 2 using swipes, and use the Reading controls menu for headings
and links. Then do the error bar check below.

Result: [ ] Pass  [ ] Fail  [ ] Not run, no device    Date: ______  By: ______  Device: ______
Notes:

## Pass 4. Keyboard only, at 400% zoom and at 320 CSS pixels

No mouse and no screen reader. Use Chrome. Two setups: (a) set the browser zoom to 400% on a 1280-pixel-wide
window, which is the same as 320 CSS pixels at default text size; (b) open the browser's device tools and set
the viewport to 320 wide. Do not also raise the text size to 200%: no criterion asks for both at once.

1. **Reflow (WCAG 1.4.10, level AA).** *Correct:* no sideways scrolling of the page; the grid becomes one or
   two columns; nothing is cut off.
2. **Walk with Tab.** Go through the nav, search box, radios, both groups, the sort menu, the pager, and then
   the page ends. *Correct:* the focus ring is always visible, and about **5 stops** sit between the search box
   and the pager with every group closed.
3. **Open a group.** Press `Enter` or `Space` on "Genre". *Correct:* it opens, and Tab now enters its boxes.
4. **Focus not obscured (WCAG 2.4.11, level AA).** Tab to every control. *Correct:* the focused control is
   never *entirely* hidden behind another thing. The fixed error bar must not cover it, and the page must
   scroll to bring it into view.
5. **Unticking the last box.** With one genre ticked, untick it. *Correct:* the group stays open and focus
   stays on that box. It must not collapse under you.

Result: [ ] Pass  [ ] Fail    Date: ______  By: ______  Browser: ______
Notes:

## Pass 5. Forced colors

Two parts. (a) In Chrome or Edge DevTools, open the Rendering tab and set "Emulate CSS media feature
forced-colors" to *active*. (b) Once on a real Windows machine, turn on a High Contrast theme (Settings,
Accessibility, Contrast themes), then open the page.

Check every item in both parts:
- Confidence badges ("Needs review", "Not matched") are readable and still look like a badge: a border or
  system colours, not a blank gap.
- The focus ring is visible on every control, summary included.
- The pager's Previous on page 1 and Next on the last page are still readable and still reachable by Tab (they
  are "greyed", not removed). The page's own design says the greying is colour alone and that "Page 1 of 25"
  between the buttons is what tells a sighted user which end they are at, so check that the text is there and
  the buttons stay legible. Whether the greyed button looks different from the live one in this mode is worth
  writing down either way.
- Radio buttons, checkboxes, the group markers (the small triangles) and the sort menu are all visible.
- The error bar, if you can trigger it, is readable.

Result: [ ] Pass  [ ] Fail    Date: ______  By: ______  Windows version: ______
Notes (emulation):
Notes (real High Contrast):

## Pass 6. Windows Magnifier at 400%

Turn Magnifier on (Windows key + Plus) and raise it to 400%, in docked or full-screen mode. Tab through the page
and open the error bar if you can.

*Correct:* a focused control is never entirely hidden. Note for the tester: the page has no sticky header
today, so the only fixed things are the skip link (only while focused) and the error bar. If someone later makes
the header sticky, this pass becomes more important, not less.

Result: [ ] Pass  [ ] Fail    Date: ______  By: ______  Magnifier mode: ______
Notes:

## Pass 7. Text spacing (WCAG 1.4.12)

Install a text spacing bookmarklet or extension, or paste this in the browser console:

    document.head.insertAdjacentHTML("beforeend", "<style>*{line-height:1.5!important;letter-spacing:.12em!important;word-spacing:.16em!important}p{margin-bottom:2em!important}</style>")

Look at the grid, especially samples C and D, and at the filter panel.
*Correct:* titles wrap and every word is still visible; no clipped text; badges and labels are not cut off.
*If it fails:* look for a title cut mid-word by a fixed height.

Result: [ ] Pass  [ ] Fail    Date: ______  By: ______  Browser: ______
Notes:

## Extra: the error bar on mobile screen readers

The error bar (`role="alert"`) is reliable on desktop NVDA and JAWS and unreliable on iOS VoiceOver and Android
TalkBack. The site deliberately does not steal focus to compensate. Ask the developer for a build that fails on
purpose; there is no button on the page that triggers it.
*Record what happens:* is "Something went wrong." announced, announced late, or silent? Silent is a known
limitation to write down, not a reason to fail the pass.

Result, iOS VoiceOver: [ ] Announced  [ ] Late  [ ] Silent    Date: ______  By: ______
Result, Android TalkBack: [ ] Announced  [ ] Late  [ ] Silent  Date: ______  By: ______
