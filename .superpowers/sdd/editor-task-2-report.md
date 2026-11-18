# Editor Task 2 report

RED: first run failed to compile, CS0234 "The type or namespace name 'Store' does not exist in the namespace
'MovieLibrary.Web.Editor'" in both test files. (Nine/eight runtime failures could not be observed because the
types did not exist yet; compile failure is the stated expected form.)

GREEN: filter RecordLinkTests|StoreDraftTests: 17 passed, 0 failed. Full: build 0 warnings 0 errors;
Core.Tests 1355 passed; Web.Tests 807 passed, 2 skipped (pre-existing), 0 failed.
Test 2 (RejoiningTheDirectorArrayCannotChangeTheKey) passed: D2 holds.

2:06:53 check: RuntimeParser.FilmMinutes = (2*3600+6*60+53)/60 = 7613/60 = 126 (integer truncation). Brief correct.

Files: src/MovieLibrary.Web.Editor/Store/RecordLink.cs, StoreDraft.cs (brief code verbatim);
tests/MovieLibrary.Web.Tests/RecordLinkTests.cs, StoreDraftTests.cs. Core untouched.

Notes: the first heredoc attempt (both tests in one command) hit 'unexpected EOF' and wrote nothing; redone in
pieces. Untracked film-overrides.tsv at repo root predates this task and was not committed.
Unsettled by brief: SeriesEntriesPassThrough asserts reference identity (Assert.Same) of the series entry.

## Fix report (review findings)

- Fix 1: added `AMatchedFilmPublishesTheSheetsOwnTitleRuntimeAndDirectorsSoItsIdentityKeyIsRecomputable` to `MoviePipelineGapTests.cs`. Fake HTTP harness, no network. GREEN: a matched film (id 807, Mc non-zero) publishes the sheet's title, runtime and directors although TMDB's differ, and the key recomputed from the published record equals the key hashed from the sheet row. The central design holds.
- Fix 2: `RecordLink` remarks rewritten (title, runtime and directors can all be overridden; valid only on the no-entry path); boundary fact `IdentityKeyOfIsNotTheSheetsKeyOnceAnOverrideHasReplacedThePublishedTitle` added.
- Fix 3: summaries of test 1 and `Published` reworded to say what they hold and point to the Core.Tests fact.
- Counts: Core.Tests 1356 passed; Web.Tests 808 passed, 2 skipped (pre-existing skips). 0 warnings.
