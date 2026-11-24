# WOR-0080: improve fidelity of postit notes and polaroid

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-16
- **Done:** 2026-11-17

## Description

From a note on the wall (20261116-235820-e5f3).

## How to check

**What changed** (commit `5597796` on `main`, pushed):
- Post-its: shaded paper (a bit darker along the glued strip at the top, lightest across the middle, darker where it curls at the bottom) and a two-part shadow: tight all round, deeper only under the lower edge, as if the bottom lifts off the wall.
- Polaroids: stiff off-white card with an even shadow, thin margins top and sides, a deep bottom margin holding the caption. The photo is square and cropped to fill, with a faint gloss and an inset edge. It hangs from a strip of tape in the note's colour over its top edge, which replaces the colour strip under the photo.
- Choice made for you: a picture that isn't square gets cropped from the centre on the wall. The open note still shows all of it.

**Screenshots:** `D:\GitHub\ThePhilipWilson\Workbench\.shots\wor-0080-before.png` and `wor-0080-after.png`.

**To try it:** update Darg (header ⟳ menu, or `pwsh -File tools/publish.ps1` from a terminal outside Darg) and open Notes.

**What to look for:**
- Do the post-its read as paper rather than flat colour?
- Do the polaroids look right, and does the tape look like tape?
- Is cropping a wide screenshot to a square OK on a polaroid?
- Not checked: dragging a polaroid. The lifted copy is a snapshot of the card's own bounds, so the tape may be cut off while you drag it.
