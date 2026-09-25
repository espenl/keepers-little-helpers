"""Generate the complete English translation template; never overwrite other languages."""
from pathlib import Path
import json, re
root = Path(__file__).resolve().parent.parent
literal = r'"(?:[^"\\]|\\.)*"'
strings = set()
for path in [*root.joinpath('src').glob('*.cs'), *root.joinpath('bridge').glob('*.cs')]:
    text = path.read_text(encoding='utf-8-sig')
    for raw in re.findall(r'\bL\.(?:T|F)\(('+literal+r')', text):
        strings.add(json.loads(raw))
    if path.name == 'HelpersSettings.cs':
        for name in ('names', 'summaries', 'categories'):
            body = re.search(r'\b'+name+r'\s*=\s*\{(.*?)\};', text, re.S).group(1)
            strings.update(json.loads(raw) for raw in re.findall(literal, body))
directory = root / 'localization'
directory.mkdir(exist_ok=True)
(directory/'en.json').write_text(json.dumps({s:s for s in sorted(strings)},ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(f'{len(strings)} translation entries exported')
