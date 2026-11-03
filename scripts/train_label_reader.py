"""Train pixel-only number CTC and region classifiers; export Unity ONNX models."""
import csv, json, random, argparse
from pathlib import Path
import numpy as np
from PIL import Image
import torch
from torch import nn

REGIONS = ['서울', '대전', '부산']
class Reader(nn.Module):
    def __init__(self, region=False):
        super().__init__(); self.region = region
        self.features = nn.Sequential(nn.Conv2d(1,16,3,padding=1),nn.ReLU(),nn.MaxPool2d(2),
            nn.Conv2d(16,32,3,padding=1),nn.ReLU(),nn.MaxPool2d(2),
            nn.Conv2d(32,64,3,padding=1),nn.ReLU(),nn.Conv2d(64,64,3,padding=1),nn.ReLU())
        self.head = nn.Linear(64*8*24,3) if region else nn.Conv1d(64*8,11,1)
    def forward(self,x):
        x=self.features(x)
        if self.region: return self.head(x.flatten(1))
        return self.head(x.flatten(1,2)).transpose(1,2)

def load(root,kind):
    rows=[r for r in csv.DictReader((root/'labels.csv').open(encoding='utf-8-sig')) if r['kind']==kind]
    x=torch.tensor(np.stack([np.array(Image.open(root/r['file']).convert('L'),dtype=np.float32)/255 for r in rows]))[:,None]
    return x,[r['answer'] for r in rows]
def decode(logits, region=False):
    probs=logits.softmax(-1); scores, ids=probs.max(-1)
    if region:return [(REGIONS[i],float(s)) for i,s in zip(ids,scores)]
    result=[]
    for row,conf in zip(ids.tolist(),scores.tolist()):
        text=''; certainty=[]; prev=10; peak=0.
        for a,p in zip(row+[10],conf+[0.]):
            if a!=prev:
                if prev!=10:text+=str(prev);certainty.append(peak)
                prev=a;peak=p
            else:peak=max(peak,p)
        result.append((text,min(certainty) if certainty else 0.))
    return result
def main():
    ap=argparse.ArgumentParser();ap.add_argument('--epochs',type=int,default=45);args=ap.parse_args()
    torch.set_num_threads(4);torch.manual_seed(20261106);random.seed(20261106)
    out=Path('results/label_reader');out.mkdir(parents=True,exist_ok=True);metrics={}
    for kind in ['number','region']:
        region=kind=='region'; x,answers=load(Path('LabelReaderDataset'),kind)
        order=torch.randperm(len(x));val=order[:300];train=order[300:]
        net=Reader(region);opt=torch.optim.Adam(net.parameters(),lr=.001)
        target=torch.tensor([REGIONS.index(a) for a in answers]) if region else torch.tensor([[int(c) for c in a] for a in answers])
        best=-1; lossfn=nn.CTCLoss(blank=10,zero_infinity=True)
        for epoch in range(args.epochs):
            net.train();total=0
            for batch in train[torch.randperm(len(train))].split(64):
                logits=net(x[batch]);
                loss=nn.functional.cross_entropy(logits,target[batch]) if region else lossfn(logits.log_softmax(-1).transpose(0,1),target[batch],torch.full((len(batch),),logits.shape[1],dtype=torch.long),torch.full((len(batch),),5,dtype=torch.long))
                opt.zero_grad();loss.backward();opt.step();total+=float(loss)
            net.eval()
            with torch.no_grad(): predictions=decode(net(x[val]),region)
            acc=sum(p[0]==answers[i] for p,i in zip(predictions,val))/len(val)
            print(f'{kind} {epoch+1}: loss={total:.3f}, validation={acc:.4f}',flush=True)
            if acc>best:best=acc;torch.save(net.state_dict(),out/f'{kind}.pt')
            if best==1 and epoch>=14:break
        net.load_state_dict(torch.load(out/f'{kind}.pt'));net.eval()
        tx,ta=load(Path('LabelReaderDatasetTest'),kind)
        with torch.no_grad():pred=decode(net(tx),region)
        metrics[kind]={'validation_accuracy':best,'test_correct':sum(a==p[0] for a,p in zip(ta,pred)),'test_total':len(ta)}
        with (out/f'{kind}_test.csv').open('w',encoding='utf-8-sig',newline='') as f:
            w=csv.writer(f);w.writerow(['truth','prediction','confidence']);w.writerows((a,*p) for a,p in zip(ta,pred))
        torch.onnx.export(net,x[:1],str(out/f'Label{kind.title()}.onnx'),input_names=['image'],output_names=['logits'],opset_version=13)
    (out/'metrics.json').write_text(json.dumps(metrics,indent=2),encoding='utf-8');print(metrics)
if __name__=='__main__':main()
