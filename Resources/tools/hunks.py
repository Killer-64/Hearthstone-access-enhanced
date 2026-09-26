# Turns HSA's diff.patch into per-hunk token deltas for the IL matcher.
import re, collections, json, sys
txt=open('diff.patch',encoding='utf-8',errors='replace').read()
files=re.split(r'^diff --git ', txt, flags=re.M)[1:]
tok=re.compile(r'"(?:[^"\\]|\\.)*"|[A-Za-z_]\w*')
KW=set('if else for foreach while do return new var true false null this base void int bool string float long uint ulong short byte char double decimal object in out ref is as case break continue switch default try catch finally throw using get set value public private protected internal static readonly override virtual sealed abstract class struct enum interface namespace typeof sizeof await async yield lock goto operator implicit explicit params checked unchecked fixed unsafe delegate event extern const partial where select from let orderby group into join on equals by ascending descending nameof when'.split())
def toks(l):
    out=[]
    code=re.sub(r'//.*$','',l[1:])
    code=re.sub(r'/\*.*?\*/','',code)
    for t in tok.findall(code):
        if t.startswith('"'): out.append('S:'+bytes(t[1:-1],'utf-8').decode('unicode_escape','ignore'))
        elif t not in KW: out.append(t)
    return out
hunks=[]
for f in files:
    head=f.split('\n')[0].split(' ')[0]
    if not head.startswith('a/Assembly-CSharp/') or not head.endswith('.cs') or '/Accessibility/' in head: continue
    cls=head[len('a/Assembly-CSharp/'):-3].replace('/','.')
    for i,h in enumerate(re.split(r'^@@', f, flags=re.M)[1:]):
        lines=h.split('\n')
        ch=[l for l in lines[1:] if l and l[0] in '+-']
        if all(l[1:].strip().startswith('using ') or not l[1:].strip() for l in ch): continue
        r=collections.Counter(t for l in ch if l[0]=='-' for t in toks(l))
        a=collections.Counter(t for l in ch if l[0]=='+' for t in toks(l))
        gained=a-r; lost=r-a
        if not gained and not lost: continue
        hunks.append({'cls':cls,'id':f'{cls}#{i}','header':lines[0][:120],'gained':dict(gained),'lost':dict(lost),
                      'text':'\n'.join(ch)[:1500]})
json.dump(hunks,open('hunks.json','w'))
print(len(hunks),'hunks with token deltas')
