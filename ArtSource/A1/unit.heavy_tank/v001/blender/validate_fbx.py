"""FBX round-trip smoke check; does not modify the native source."""
import bpy, json, sys
from pathlib import Path
from mathutils import Vector
root=Path(sys.argv[sys.argv.index("--")+1]).resolve()
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(root/"export/HeavyTank_A1.fbx"))
bpy.context.view_layer.update()
meshes=[o for o in bpy.data.objects if o.type=="MESH"]
points=[o.matrix_world @ Vector(p) for o in meshes for p in o.bound_box]
report={"file":"HeavyTank_A1.fbx","meshCount":len(meshes),"cameraCount":sum(o.type=="CAMERA" for o in bpy.data.objects),
"anchors":{n:bool(bpy.data.objects.get(n)) for n in ["HeavyTank_A1_Root","TurretRoot","BarrelRecoil","MuzzleAnchor","HitAnchor"]},
"bounds":{"min":[min(p[i] for p in points) for i in range(3)],"max":[max(p[i] for p in points) for i in range(3)]},
"scope":"Blender FBX import smoke only; not Unity acceptance."}
report["passed"]=len(meshes)>0 and report["cameraCount"]==0 and all(report["anchors"].values())
(root/"export/fbx-roundtrip.json").write_text(json.dumps(report,indent=2)+"\n",encoding="utf-8")
print("A1_FBX_ROUNDTRIP "+json.dumps(report))
assert report["passed"]
