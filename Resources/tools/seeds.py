# Filters hunk matches: drops hunks that only carry decompile/recompile fixes
# (span/format helpers, separators, whitespace in literals), keeps HSA behaviour.
import re, collections
NOISE={'AsSpan','ToString','Substring','Deconstruct','Key','Value','IsInfinity','IsFinite','UnixEpoch','Copy','Index','ID','CurrencyId',
       'OriginalString','StartIndex','BufferSize','GetTokenValueAsString','Format','Concat','Join','Mathf','Math'}
def noise(tok):
    if tok in NOISE: return True
    if tok.startswith('S:') and len(tok[2:].strip()) <= 1: return True
    return False
keep=set(); drop=collections.Counter()
# reviewed by hand: recompile-only fixes
SKIP=('Blizzard.T5.WebView.WebViewService::OnNetCacheFeaturesReady','AssetLoader::SendMissingAssetTelemetry')
for line in open('hunk_matched_detail.txt'):
    meth, hid, rest = line.rstrip('\n').split('\t')
    if any(k in meth for k in SKIP): drop[meth]+=1; continue
    g=re.search(r'gained=\[(.*?)\] lost=\[(.*)\]$', rest)
    toks=[t for t in (g.group(1).split(',')+g.group(2).split(',')) if t]
    # whitespace-only literal changes: gained/lost strings equal after collapsing spaces
    strs=[re.sub(r'\s+',' ',t) for t in toks if t.startswith('S:')]
    if toks and all(noise(t) for t in toks): drop[meth]+=1; continue
    if toks and all(t.startswith('S:') for t in toks) and len(set(strs)) < len(strs): drop[meth]+=1; continue
    keep.add(meth)
open('hunk_edit_seeds.txt','w').write('\n'.join(sorted(keep))+'\n')
print('keep',len(keep),'dropped as recompile noise',len(set(drop)-keep))
