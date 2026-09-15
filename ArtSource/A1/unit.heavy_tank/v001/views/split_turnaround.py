from pathlib import Path
from PIL import Image
import json, hashlib
root = Path(__file__).resolve().parent
source = root / "turnaround.png"
with Image.open(source) as im:
    assert im.size == (1254,1254), im.size
    order = ("front","left","rear","right","top","three-quarter")
    records = []
    for i,name in enumerate(order):
        row,col = divmod(i,3)
        box = (col*418+8,row*627+8,(col+1)*418-4,(row+1)*627-4)
        dest = root / (name+".png")
        im.crop(box).save(dest)
        records.append({"view":name,"file":dest.name,"crop":box,"sha256":hashlib.sha256(dest.read_bytes()).hexdigest()})
(root/"views-manifest.json").write_text(json.dumps({"source":source.name,"method":"image_gen + mechanical crop","views":records},indent=2)+"\n",encoding="utf-8")
print(json.dumps({"views":len(records),"size":[406,615]}))
