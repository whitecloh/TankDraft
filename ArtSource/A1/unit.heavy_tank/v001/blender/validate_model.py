"""Validate native A1 pilot geometry and pose independently of the builder."""
import bpy, json, math, sys
from pathlib import Path
from mathutils import Vector
root = Path(sys.argv[sys.argv.index("--")+1]).resolve()
bpy.ops.wm.open_mainfile(filepath=str(root/"blender/heavy_tank_work.blend"))
bpy.context.view_layer.update()
model = bpy.data.collections["MODEL_HeavyTank_A1"]
outlines = bpy.data.collections["OUTLINE_HeavyTank_A1"]
def count(coll):
    triangles=0
    for obj in coll.objects:
        if obj.type == "MESH":
            obj.data.calc_loop_triangles()
            triangles += len(obj.data.loop_triangles)
    return triangles
meshes=[o for o in model.objects if o.type=="MESH"]
points=[o.matrix_world @ Vector(p) for o in meshes for p in o.bound_box]
minimum=[min(p[i] for p in points) for i in range(3)]
maximum=[max(p[i] for p in points) for i in range(3)]
turret=bpy.data.objects["TurretRoot"]
barrel=bpy.data.objects["BarrelRecoil"]
muzzle=bpy.data.objects["MuzzleAnchor"]
hit=bpy.data.objects["HitAnchor"]
native_root=bpy.data.objects["HeavyTank_A1_Root"]
start=muzzle.matrix_world.translation.copy()
pivot=turret.matrix_world.translation.copy()
radius=(start-pivot).length
turret.rotation_euler.z += math.radians(60)
bpy.context.view_layer.update()
turned=muzzle.matrix_world.translation.copy()
turned_radius=(turned-pivot).length
turret.rotation_euler.z -= math.radians(60)
bpy.context.view_layer.update()
reset=muzzle.matrix_world.translation.copy()
checks={
 "nonzero_geometry":count(model)>0,
 "base_triangle_budget":count(model)<=2000,
 "total_triangle_budget":count(model)+count(outlines)<=4000,
 "tank_height_expected":1.3 < maximum[2] < 2.1,
 "ground_contact":abs(minimum[2])<0.03,
 "turret_height":.95 < pivot.z < 1.15,
 "muzzle_height":1.1 < start.z < 1.5,
 "muzzle_forward":start.y>1.3,
 "hit_on_hull":hit.parent == native_root and .2<hit.matrix_world.translation.z<1.1,
 "barrel_parent":barrel.parent==turret,
 "turret_turn_moves_muzzle":(turned-start).length>.5,
 "turret_turn_preserves_radius":abs(turned_radius-radius)<1e-4,
 "turret_reset":(reset-start).length<1e-4,
 "root_scale":all(abs(v-1)<1e-6 for v in native_root.scale),
 "separate_outline_geometry":len(outlines.objects)>0,
 "outline_backface_culling":all(all(m.use_backface_culling for m in o.data.materials) for o in outlines.objects if o.type=="MESH")
}
report={"checks":checks,"allPassed":all(checks.values()),"baseTriangles":count(model),"outlineTriangles":count(outlines),
 "bounds":{"min":minimum,"max":maximum},"turretPivot":list(pivot),"muzzle":list(start),"hit":list(hit.matrix_world.translation),
 "materials":sorted({m.name for o in meshes for m in o.data.materials}),"meshObjects":len(meshes),
 "note":"Native Blender geometry/pose validation only. Unity import, runtime and device performance not tested."}
(root/"export/native-validation.json").write_text(json.dumps(report,indent=2)+"\n",encoding="utf-8")
print("A1_NATIVE_VALIDATION "+json.dumps(report))
if not report["allPassed"]: raise RuntimeError("A1 pilot validation failures: "+", ".join(k for k,v in checks.items() if not v))
