# Translating Keeper's Little Helpers

Translations work with the standalone mod. GK2 Mod Framework is optional.

1. Copy `BepInEx/plugins/KeepersJournal/Localization/en.json` to `fr.json` for French (or `ko.json`, `ru.json`, `de.json`, etc.) in the same folder.
2. Edit the text on the **right** of each colon. Keep the English keys on the left unchanged. Save as UTF-8 JSON.
3. Keep placeholders such as `{0}` and `{1}` and any markup such as `<size=70%>` and `</size>`. You may reorder placeholders to suit your language. Keep escaped newlines (`\n`) inside strings. Do not add comments or trailing commas.
4. Select that language in the game. The mod's `Localization.Language` setting defaults to `auto`. You can override it with a code in `BepInEx/config/local.espen.keepersjournal.cfg`, or in the optional Framework settings. Restart the game after editing files.

Example `fr.json` (a partial file is valid):

```json
{
  "Materials": "Matériaux",
  "Built: {0}": "Construits : {0}",
  "Carrying {0} / Need {1}": "Sur soi : {0} / Nécessaire : {1}"
}
```

Missing entries fall back to the neutral language (`pt_br` → `pt`), then `en.json`, then built-in English. Invalid files or mismatched placeholders produce a warning in `BepInEx/LogOutput.log` and fall back safely. Use `ko` for Korean (`kr`/`kor` aliases are accepted by the setting). Language filenames use lowercase and underscores, for example `pt_br.json`.

Item and building names already come from the game. Translate the mod's own labels, explanations, status messages and reminders. Keep reminders in the character's first person. Keep short buttons concise; test long text in the building menu, pinned panel and each settings tab. Native game language fonts are used for the mod's custom labels.

The English text is the stable lookup key. When an update adds entries, copy those new entries into your language file; do not replace your existing translations. Share just your language JSON and the mod version, not your config or save files. Translation contributors can choose how they would like to be credited.

Framework settings names are translated when the bridge starts: restart after changing language to refresh those names. The standalone settings refresh when reopened, and the materials panel and storage buttons refresh after a language change.
