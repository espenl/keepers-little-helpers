from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED
import hashlib

root = Path(__file__).resolve().parent.parent
version = "0.3.7"
archive = root / "dist" / ("KeepersLittleHelpers-" + version + ".zip")
# Explicit allowlist: never bundle saves, diagnostics, local configs, or game assemblies.
files = {"BepInEx/plugins/KeepersJournal/KeepersJournal.dll": root / "dist/KeepersJournal.dll"}
for name in ("README.md", "CHANGELOG.md", "ROADMAP.md", "TRANSLATING.md", "FRAMEWORK.md"):
    files["KeepersLittleHelpers-Docs/" + name] = root / "release" / name
for source in sorted((root / "localization").glob("*.json")):
    files["BepInEx/plugins/KeepersJournal/Localization/" + source.name] = source
with ZipFile(archive, "w", ZIP_DEFLATED) as output:
    for name, source in files.items():
        output.write(source, name)
with ZipFile(archive) as check:
    assert check.testzip() is None
    assert set(check.namelist()) == set(files)
    assert check.read("BepInEx/plugins/KeepersJournal/KeepersJournal.dll") == files["BepInEx/plugins/KeepersJournal/KeepersJournal.dll"].read_bytes()
checksum = hashlib.sha256(archive.read_bytes()).hexdigest()
archive.with_suffix(".zip.sha256").write_text(checksum + "  " + archive.name + "\n", encoding="ascii")
print(archive)
print("Verified archive: " + str(len(files)) + " files; SHA256 " + checksum)
# Optional, separate download: never bundle the Framework itself.
bridge = root / "dist/KeepersLittleHelpers.Framework.dll"
if bridge.exists():
    with ZipFile(root / "dist" / ("KeepersLittleHelpers-Framework-" + version + ".zip"), "w", ZIP_DEFLATED) as output:
        output.write(bridge, "BepInEx/plugins/KeepersJournal/KeepersLittleHelpers.Framework.dll")
        output.write(root / "release/FRAMEWORK.md", "KeepersLittleHelpers-Docs/FRAMEWORK.md")
with ZipFile(root / "dist/KeepersLittleHelpers-TranslatorKit.zip", "w", ZIP_DEFLATED) as output:
    output.write(root / "localization/en.json", "en.json")
    output.write(root / "release/TRANSLATING.md", "TRANSLATING.md")
