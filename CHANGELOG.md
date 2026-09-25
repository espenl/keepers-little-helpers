# Changelog

## 0.3.7 - recipe pins, translations and optional framework support

- For world repair/build prompts such as the vineyard bridge, Shift-click the project output icon to pin its requirements without adding buttons to the native menu.
- Extend materials pins to crafting recipes. Shift-click pins the selected ingredients and craft count without starting work; repeat to unpin. Keep the panel visible in the crafting menu.
- Add a complete English JSON template and support for partial community translations, including French.
- Follow the game's selected language, with an optional config override and regional/English fallback.
- Validate translated placeholders and use native language font assets for custom text.
- Keep daily checkoff IDs independent from translated reminder text.
- Add a separate optional GK2 Mod Framework bridge sharing all twelve feature switches with the native menu.
- Main mod remains standalone and does not require the Framework.

## 0.3.6 - storage helpers and clearer controls

- Add manual Quick Stack, optional automatic area-entry stacking (off by default), and Ctrl-click item protection.
- Pin up to six different blueprints with combined carried-only material totals. Shift-click pins/unpins; remove plans directly with X.
- Find materials directly from a pin, or search nearby chest contents by name. Find refreshes the result; Clear dismisses markers.
- Add chest naming, contextual workstation status, and smart daily reminder checkoffs.
- Replace cycling settings with Building & plans, Storage and Reminders tabs and direct On/Off controls.
- Keep pins visible in building menus; preserve supported unfinished structures when moving.
- Place storage buttons bottom-left, away from pickup notifications.
- Exclude autopsy tables and default idle states from misleading Work finished labels.
- Use first-person Quick Stack feedback in the character's native thought bubble.

## 0.3.5 - native materials tracker

- Reuse the full native window background, header, border and graphical close button.
- Show native item icons, brighter names and aligned Have / Need counts using native sufficient/missing colours.
- Fit the panel to wrapped names and material rows.
- Drag the header to reposition the panel; remember its position during the session and keep it within HUD bounds.

## 0.3.4 - readable materials hint

- Restore the materials hint on blueprint mouseover.
- Increase note text size and use gold for the hint, including unaffordable recipes.
- Reserve hint space so hovering does not shift the rows.


## 0.3.3 - helpers and placement preview

- Add Shift-click material tracking with carried-item counts and a clickable X.
- Add independent helper switches under Esc > Keeper's Little Helpers.
- Add a Move preview for idle structures in the same building area, including normal yard workstations.
- Recalculate nearby tool-rack links when moving supported benches. Fitted attachments remain restricted.
- Give built counts and the Shift-click hint their own space beneath native blueprint labels and icons.
- Count planted and harvest-ready garden/vineyard plots as built.
- Fix pause-menu startup hook, first-open label, and dialog-button errors.


## 0.2.4 - Keeper's Little Helpers

- Rename the pack and plugin display name.
- Package installation instructions and release notes for public distribution.
- Keep the existing plugin identity and file path so earlier installations update in place.

## 0.2.3 ? public preview candidate

- Compact native building status labels; hide zero counts.
- Show the current kitchen tier instead of separate counts for both tiers.
- Remove repeated fixed-chest details from chest recipes.
- Include morning thought-bubble reminders and the F8 repeat shortcut.

## 0.2.2 ? local testing

- Fix false zero counts caused by stale object removal flags.
- Verify house script results and distinguish existing kitchen tiers.

## 0.2.0?0.2.1 ? local testing

- Add counts to the native building menu.
- Add unlock-aware morning reminders using native thought bubbles.
