"""Render a native-model turntable and independent turret/recoil motion."""
import bpy, sys, math
from pathlib import Path
root=Path(sys.argv[sys.argv.index("--")+1]).resolve()
bpy.ops.wm.open_mainfile(filepath=str(root/"blender/heavy_tank_work.blend"))
scene=bpy.context.scene
scene.render.engine="BLENDER_EEVEE"
scene.render.resolution_x=720
scene.render.resolution_y=720
scene.render.resolution_percentage=100
scene.render.fps=24
scene.frame_start=1
scene.frame_end=96
scene.render.film_transparent=False
floor=bpy.data.objects.get("PreviewOnly_PaperGround")
if floor: floor.hide_render=False
body=bpy.data.objects["HeavyTank_A1_Root"]
turret=bpy.data.objects["TurretRoot"]
barrel=bpy.data.objects["BarrelRecoil"]
barrel_base=barrel.location.copy()
for f,angle in [(1,0),(96,2*math.pi)]:
    body.rotation_euler.z=angle
    body.keyframe_insert(data_path="rotation_euler",frame=f)
for f,angle in [(1,0),(24,-.6),(48,.5),(72,-.35),(96,0)]:
    turret.rotation_euler.z=angle
    turret.keyframe_insert(data_path="rotation_euler",frame=f)
for f,offset in [(1,0),(18,0),(20,-.12),(27,0),(53,0),(55,-.12),(62,0),(96,0)]:
    barrel.location=barrel_base.copy()
    barrel.location.y+=offset
    barrel.keyframe_insert(data_path="location",frame=f)
scene.render.image_settings.media_type="VIDEO"
scene.render.image_settings.file_format="FFMPEG"
scene.render.ffmpeg.format="MPEG4"
scene.render.ffmpeg.codec="H264"
scene.render.ffmpeg.constant_rate_factor="MEDIUM"
scene.render.filepath=str(root/"renders/heavy-tank-turntable.mp4")
scene.frame_set(1)
bpy.ops.render.render(animation=True)
print("A1_TURNTABLE_COMPLETE "+scene.render.filepath)
