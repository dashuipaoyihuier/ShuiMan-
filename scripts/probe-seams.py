#!/usr/bin/env python3
"""Development-only seam measurements. These scores are not labels or probabilities."""
import argparse
import io
import json
import zipfile
from pathlib import Path
import numpy as np
from PIL import Image


def features(image):
    h=384; w=max(64,round(image.width/image.height*h))
    a=np.asarray(image.convert('L').resize((w,h),Image.Resampling.LANCZOS),dtype=np.float64)/255
    ink=(a<0.85).mean(axis=0)
    left=next((x for x in range(int(w*.04)+1) if ink[x]>.12),0)
    right=next((x for x in range(w-1,w-int(w*.04)-2,-1) if ink[x]>.12),w-1)
    return {'left':a[:,left:min(w,left+3)].mean(axis=1),'right':a[:,max(0,right-2):right+1].mean(axis=1),
            'small':np.asarray(image.convert('L').resize((24,32)),dtype=float)/255,
            'trim':(left/w,(w-1-right)/w)}


def corr(a,b):
    a=a-a.mean(); b=b-b.mean()
    return float(a@b/max(1e-8,np.linalg.norm(a)*np.linalg.norm(b)))


def score(left,right):
    if abs(left['left'].shape[0]-right['left'].shape[0])>0: return None
    if np.abs(left['small']-right['small']).mean()<.025: return None
    a=left['right']; b=right['left']
    if min(a.std(),b.std())<.08: return None
    choices=[]
    for shift in range(-5,6):
        aa=a[max(0,shift):min(len(a),len(a)+shift)]
        bb=b[max(0,-shift):min(len(b),len(b)-shift)]
        mae=float(np.abs(aa-bb).mean())
        correlation=corr(aa,bb)
        detail=corr(np.diff(aa),np.diff(bb))
        good=0; informative=0
        for chunk in np.array_split(np.arange(len(aa)),12):
            x=aa[chunk]; y=bb[chunk]
            if min(x.std(),y.std())<.055: continue
            informative+=1
            if corr(x,y)>.7 and np.abs(x-y).mean()<.15:good+=1
        combined=.55*max(0,correlation)+.20*max(0,detail)+.25*(good/12)
        choices.append({'score':combined,'correlation':correlation,'detail':detail,'mae':mae,'bands':good,'informative':informative,'shift':shift})
    best=max(choices,key=lambda x:x['score'])
    return dict(best,trimLeft=left['trim'][1],trimRight=right['trim'][0])


def main():
    parser=argparse.ArgumentParser();parser.add_argument('--book',required=True);parser.add_argument('--output',required=True)
    args=parser.parse_args()
    book=next(b for b in json.loads(Path('.build/corpus/inventory.json').read_text())['books'] if b['file']==args.book)
    pages=[]
    with zipfile.ZipFile(args.book) as archive:
        for p in book['pages']:
            ref=p['images'][0]['resource']
            with Image.open(io.BytesIO(archive.read(ref))) as image: pages.append(features(image))
    pairs=[]
    for i in range(1,len(pages)-2):
        for swapped in [False,True]:
            a,b=(pages[i+1],pages[i]) if swapped else (pages[i],pages[i+1])
            result=score(a,b)
            if result:pairs.append(dict(result,position=i+1,swapped=swapped))
    pairs.sort(key=lambda x:-x['score'])
    Path(args.output).write_text(json.dumps({'file':args.book,'pairs':pairs},ensure_ascii=False,indent=2)+'\n')
    print(args.book)
    for pair in pairs[:25]:print(pair)


if __name__=='__main__':main()
