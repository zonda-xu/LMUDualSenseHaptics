"""Check complete locale catalogs and localized UI references; no translation-quality claim."""
import json,re
from pathlib import Path
root=Path(__file__).resolve().parents[1]
src=root/'src/LMUDualSenseHaptics'
def unique(pairs):
    result={}
    for k,v in pairs:
        assert k not in result, f'duplicate key {k}'
        result[k]=v
    return result
locales=['zh-CN','en','fr','es','ru','ar']
catalogs={code:json.loads((src/f'Localization/{code}.json').read_text(encoding='utf-8'),object_pairs_hook=unique) for code in locales}
base=catalogs['zh-CN']
placeholder=lambda v:sorted(re.findall(r'\{\d+(?:,[^}:]+)?(?::[^}]+)?\}',v))
for code,catalog in catalogs.items():
    assert catalog.keys()==base.keys(),f'{code}: missing/extra keys'
    for key,value in catalog.items():
        assert isinstance(value,str) and value.strip(),f'{code} {key}: blank'
        assert placeholder(value)==placeholder(base[key]),f'{code} {key}: placeholders'
        assert re.findall(r'\*\.\w+',value)==re.findall(r'\*\.\w+',base[key]),f'{code} {key}: file filter'
        assert code=='zh-CN' or not re.search('[\u4e00-\u9fff]',value),f'{code} {key}: untranslated Chinese'
references=set()
for path in list(src.rglob('*.cs'))+list(src.rglob('*.xaml')):
    if 'obj' in path.parts: continue
    text=path.read_text(encoding='utf-8')
    references.update(re.findall(r'Text_[A-F0-9]{10}',text))
    if path.suffix=='.xaml':
        assert not re.search(r'(?:Text|Content|Header|ToolTip|AutomationProperties.Name)="[^"\n]*[\u4e00-\u9fff]',text),str(path)
assert references<=base.keys(),references-base.keys()
assert set(base)<=references,set(base)-references
assert len(catalogs)==6
(root/'artifacts/validation').mkdir(parents=True,exist_ok=True)
(root/'artifacts/validation/localization.json').write_text(json.dumps({'languages':locales,'resources_per_language':len(base),'references':len(references),'passed':True},indent=2))
print(f'LOCALIZATION PASS: {len(base)} resources x six languages; placeholders, filters and UI keys verified.')
