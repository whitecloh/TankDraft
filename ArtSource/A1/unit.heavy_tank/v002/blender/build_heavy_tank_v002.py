"""Repeatable Blender 5.2.1 builder for A1 heavy-tank upgrade silhouettes.

Run with:
  blender.exe --background --python-exit-code 1 --python build_heavy_tank_v002.py -- --output-root ..

The exported FBX files contain exactly three rigid mesh objects per tier:
HullMesh, TurretMesh, BarrelMesh.  Palette is stored in CORNER FLOAT_COLOR
attribute Color.  Its alpha is zero only on the roof team cap; alpha one means
use the authored RGB.  The shared material is a preview of the intended Unity
vertex-color shader, not a Unity acceptance claim.
"""
from __future__ import annotations

import argparse
import bmesh
import json
import math
import sys
from dataclasses import dataclass
from pathlib import Path

import bpy
from mathutils import Vector


ROOT = Path(__file__).resolve().parent.parent
# #151515 display-space ink encoded for a linear vertex-color export.
BLACK = (0.0075, 0.0075, 0.0075)
CREAM = (0.93, 0.90, 0.81)
OFFWHITE = (0.84, 0.81, 0.72)
GRAY = (0.29, 0.30, 0.28)
BLUE = (0.02, 0.34, 0.93, 1.0)
RED = (0.90, 0.06, 0.06, 1.0)


@dataclass
class Part:
    vertices: list[tuple[float, float, float]]
    faces: list[tuple[int, ...]]
    color: tuple[float, float, float]
    alpha: float = 1.0
    outline: bool = True


def args() -> argparse.Namespace:
    raw = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--output-root", type=Path, default=ROOT)
    parser.add_argument("--overwrite", action="store_true")
    parser.add_argument("--skip-render", action="store_true")
    return parser.parse_args(raw)


def output_paths(root: Path, overwrite: bool) -> dict[str, Path]:
    paths = {"blend": root / "blender" / "heavy_tank_v002.blend", "metrics": root / "metrics" / "metrics.json", "export": root / "export", "renders": root / "renders"}
    existing = [paths["blend"], paths["metrics"], *(paths["export"] / f"HeavyTank_A1_Tier{tier}.fbx" for tier in (1, 2, 3))]
    if not overwrite and any(path.exists() for path in existing):
        raise RuntimeError("v002 outputs exist; use --overwrite only for this same v002 directory")
    for path in paths.values():
        (path.parent if path.suffix else path).mkdir(parents=True, exist_ok=True)
    return paths


def clear() -> None:
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for coll in list(bpy.data.collections):
        bpy.data.collections.remove(coll)


def coll(name: str, parent: bpy.types.Collection | None = None) -> bpy.types.Collection:
    result = bpy.data.collections.new(name)
    (parent or bpy.context.scene.collection).children.link(result)
    return result


def parent_keep_world(obj: bpy.types.Object, parent: bpy.types.Object) -> None:
    bpy.context.view_layer.update()
    world = obj.matrix_world.copy()
    obj.parent = parent
    obj.matrix_parent_inverse = parent.matrix_world.inverted()
    obj.matrix_world = world
    bpy.context.view_layer.update()


def normalize(vec: Vector) -> Vector:
    return vec.normalized() if vec.length else Vector((0.0, 0.0, 1.0))


def face_normal(vertices: list[tuple[float, float, float]], face: tuple[int, ...]) -> Vector:
    a, b, c = (Vector(vertices[index]) for index in face[:3])
    return normalize((b - a).cross(c - a))


def recalc_part_outward(part: Part) -> Part:
    """Use Blender's manifold normal solver before any inverted outline shell is built."""
    mesh = bpy.data.meshes.new("NormalScratch")
    mesh.from_pydata(part.vertices, [], part.faces)
    bm = bmesh.new(); bm.from_mesh(mesh)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(mesh); bm.free()
    result = Part([tuple(vertex.co) for vertex in mesh.vertices], [tuple(poly.vertices) for poly in mesh.polygons], part.color, part.alpha, part.outline)
    bpy.data.meshes.remove(mesh)
    return result


def cube(center: tuple[float, float, float], size: tuple[float, float, float], color: tuple[float, float, float], alpha: float = 1.0, outline: bool = True) -> Part:
    x, y, z = center
    sx, sy, sz = (value / 2.0 for value in size)
    vertices = [(x - sx, y - sy, z - sz), (x + sx, y - sy, z - sz), (x + sx, y + sy, z - sz), (x - sx, y + sy, z - sz), (x - sx, y - sy, z + sz), (x + sx, y - sy, z + sz), (x + sx, y + sy, z + sz), (x - sx, y + sy, z + sz)]
    faces = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    return Part(vertices, faces, color, alpha, outline)


def cylinder_y(center: tuple[float, float, float], radius: float, length: float, color: tuple[float, float, float], sides: int = 8, alpha: float = 1.0, outline: bool = True) -> Part:
    x, y, z = center
    vertices = []
    for dy in (-length / 2.0, length / 2.0):
        for index in range(sides):
            angle = math.tau * index / sides
            vertices.append((x + radius * math.cos(angle), y + dy, z + radius * math.sin(angle)))
    faces = [tuple(range(sides - 1, -1, -1)), tuple(range(sides, 2 * sides))]
    faces += [(index, (index + 1) % sides, sides + (index + 1) % sides, sides + index) for index in range(sides)]
    return Part(vertices, faces, color, alpha, outline)


def cylinder_x(center: tuple[float, float, float], radius: float, length: float, color: tuple[float, float, float], sides: int = 8, alpha: float = 1.0, outline: bool = True) -> Part:
    raw = cylinder_y((0.0, 0.0, 0.0), radius, length, color, sides, alpha, outline)
    cx, cy, cz = center
    raw.vertices = [(cx + y, cy - x, cz + z) for x, y, z in raw.vertices]
    return raw


def cylinder_z(center: tuple[float, float, float], radius: float, length: float, color: tuple[float, float, float], sides: int = 8, alpha: float = 1.0, outline: bool = True) -> Part:
    raw = cylinder_y((0.0, 0.0, 0.0), radius, length, color, sides, alpha, outline)
    cx, cy, cz = center
    raw.vertices = [(cx + x, cy - z, cz + y) for x, y, z in raw.vertices]
    return raw


def annulus_y(y_back: float, y_front: float, outer: float, inner: float, color: tuple[float, float, float], sides: int = 8) -> Part:
    """Actual cream barrel lip: open center with a separate short recessed black bore."""
    vertices: list[tuple[float, float, float]] = []
    for y in (y_back, y_front):
        for radius in (outer, inner):
            for index in range(sides):
                angle = math.tau * index / sides
                vertices.append((radius * math.cos(angle), y, 1.28 + radius * math.sin(angle)))
    faces: list[tuple[int, ...]] = []
    for index in range(sides):
        nxt = (index + 1) % sides
        faces += [(index, 2 * sides + index, 2 * sides + nxt, nxt), (sides + index, sides + nxt, 3 * sides + nxt, 3 * sides + index), (index, sides + index, sides + nxt, nxt), (2 * sides + index, 2 * sides + nxt, 3 * sides + nxt, 3 * sides + index)]
    return Part(vertices, faces, color)


def frustum(z0: float, z1: float, bottom: tuple[float, float], top: tuple[float, float], color: tuple[float, float, float]) -> Part:
    # Eight-side chopped-corner turret, forward +Y.
    def ring(width: float, length: float, z: float) -> list[tuple[float, float, float]]:
        cut = min(width, length) * .18
        return [(-width + cut, -length, z), (width - cut, -length, z), (width, -length + cut, z), (width, length - cut, z), (width - cut, length, z), (-width + cut, length, z), (-width, length - cut, z), (-width, -length + cut, z)]
    vertices = ring(*bottom, z0) + ring(*top, z1)
    faces = [tuple(range(7, -1, -1)), tuple(range(8, 16))]
    faces += [(i, (i + 1) % 8, (i + 1) % 8 + 8, i + 8) for i in range(8)]
    return Part(vertices, faces, color)


def hull_profiles(tier: int) -> Part:
    width = 1.0 + .04 * (tier - 1)
    profiles = ((.20, .66, -1.13, 1.03), (.49, width, -1.22, 1.22), (.91, .88 + .03 * (tier - 1), -1.06, 1.01))
    vertices: list[tuple[float, float, float]] = []
    for z, half_width, rear, front in profiles:
        vertices += [(-half_width, rear, z), (half_width, rear, z), (half_width, front, z), (-half_width, front, z)]
    faces = [tuple(range(3, -1, -1)), tuple(range(8, 12))]
    for index in range(2):
        a, b = index * 4, (index + 1) * 4
        faces += [(a + edge, a + (edge + 1) % 4, b + (edge + 1) % 4, b + edge) for edge in range(4)]
    return Part(vertices, faces, CREAM)


def capsule_track(side: int) -> Part:
    # Low-loop 8+8 capsule ring, black external contour/track band.
    outer, inner, n = [], [], 8
    for radius, target in ((.40, outer), (.26, inner)):
        for index in range(n):
            angle = math.tau * index / n
            y = (.75 if math.cos(angle) >= 0 else -.75) + radius * math.cos(angle)
            target.append((side * .86, y, .42 + radius * math.sin(angle)))
    vertices = []
    for x in (side * .69, side * 1.03):
        vertices += [(x, y, z) for _, y, z in outer] + [(x, y, z) for _, y, z in inner]
    faces: list[tuple[int, ...]] = []
    for index in range(n):
        nxt = (index + 1) % n
        faces += [(index, nxt, n + nxt, n + index), (2 * n + index, 3 * n + index, 3 * n + nxt, 2 * n + nxt), (index, 2 * n + index, 2 * n + nxt, nxt), (n + index, n + nxt, 3 * n + nxt, 3 * n + index)]
    return Part(vertices, faces, BLACK, outline=False)


def add_outline(part: Part, thickness: float = .045) -> Part:
    normals = [Vector((0.0, 0.0, 0.0)) for _ in part.vertices]
    for face in part.faces:
        normal = face_normal(part.vertices, face)
        for index in face:
            normals[index] += normal
    vertices = [tuple(Vector(vertex) + normalize(normals[index]) * thickness) for index, vertex in enumerate(part.vertices)]
    return Part(vertices, [tuple(reversed(face)) for face in part.faces], BLACK, 1.0, False)


def combine(name: str, parts: list[Part], material: bpy.types.Material, collection: bpy.types.Collection, parent: bpy.types.Object) -> bpy.types.Object:
    vertices: list[tuple[float, float, float]] = []
    faces: list[tuple[int, ...]] = []
    colors: list[tuple[float, float, float, float]] = []
    for source in parts:
        source = recalc_part_outward(source)
        for part in (source, add_outline(source) if source.outline else None):
            if part is None:
                continue
            offset = len(vertices)
            vertices += part.vertices
            faces += [tuple(offset + index for index in face) for face in part.faces]
            colors += [(*part.color, part.alpha) for _ in part.faces]
    mesh = bpy.data.meshes.new(f"{name}_Mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.materials.append(material)
    mesh.update()
    color = mesh.color_attributes.new("Color", "FLOAT_COLOR", "CORNER")
    for polygon, rgba in zip(mesh.polygons, colors):
        for loop_index in polygon.loop_indices:
            color.data[loop_index].color = rgba
    mesh.calc_loop_triangles()
    obj = bpy.data.objects.new(name, mesh)
    collection.objects.link(obj)
    parent_keep_world(obj, parent)
    return obj


def make_preview_material() -> bpy.types.Material:
    mat = bpy.data.materials.new("M_A1_VertexColor")
    mat.use_nodes = True
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    nodes.clear()
    out = nodes.new("ShaderNodeOutputMaterial")
    attr = nodes.new("ShaderNodeAttribute")
    attr.attribute_name = "Color"
    team = nodes.new("ShaderNodeRGB")
    team.name = "TeamColor"
    team.outputs[0].default_value = BLUE
    mix = nodes.new("ShaderNodeMixRGB")
    mix.blend_type = "MIX"
    diffuse = nodes.new("ShaderNodeBsdfDiffuse")
    to_rgb = nodes.new("ShaderNodeShaderToRGB")
    ramp = nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.interpolation = "CONSTANT"
    ramp.color_ramp.elements[0].color = (.65, .65, .65, 1)
    ramp.color_ramp.elements[1].color = (1, 1, 1, 1)
    multiply = nodes.new("ShaderNodeMixRGB")
    multiply.blend_type = "MULTIPLY"
    emission = nodes.new("ShaderNodeEmission")
    links.new(attr.outputs["Alpha"], mix.inputs[0]); links.new(team.outputs[0], mix.inputs[1]); links.new(attr.outputs["Color"], mix.inputs[2])
    links.new(mix.outputs[0], diffuse.inputs["Color"]); links.new(diffuse.outputs[0], to_rgb.inputs[0]); links.new(to_rgb.outputs[0], ramp.inputs[0])
    links.new(ramp.outputs[0], multiply.inputs[1]); links.new(mix.outputs[0], multiply.inputs[2]); links.new(mix.outputs[0], emission.inputs[0]); emission.inputs["Strength"].default_value = 1.0; links.new(emission.outputs[0], out.inputs[0])
    try: mat.use_backface_culling = True
    except AttributeError: pass
    mat["unity_intent"] = "lerp(teamColor, vertexRGB, vertexAlpha); vertex-RGB emission preview with flat stepped-normal support; cull back"
    return mat


def empty(name: str, location: tuple[float, float, float], parent: bpy.types.Object, collection: bpy.types.Collection) -> bpy.types.Object:
    obj = bpy.data.objects.new(name, None); obj.empty_display_type = "ARROWS"; obj.location = location; collection.objects.link(obj); parent_keep_world(obj, parent); return obj


def build_tier(tier: int, material: bpy.types.Material, master: bpy.types.Collection) -> dict[str, object]:
    collection = coll(f"Tier{tier}", master)
    anchors = coll("Anchors", collection)
    root = bpy.data.objects.new(f"Tier{tier}_Root", None); root.empty_display_type = "CUBE"; anchors.objects.link(root)
    turret_root = empty("TurretRoot", (0, 0, 1.01), root, anchors)
    recoil = empty("BarrelRecoil", (0, .62, 1.28), turret_root, anchors)
    hull = [hull_profiles(tier), capsule_track(-1), capsule_track(1)]
    for side in (-1, 1):
        for y in (-.60, 0, .60): hull.append(cylinder_x((side * .91, y, .42), .25, .08, GRAY, 8))
    if tier >= 2:
        for side in (-1, 1): hull.append(cube((side * 1.00, 0, .61), (.12, 1.92, .30), OFFWHITE))
    if tier == 3:
        for side in (-1, 1):
            hull += [cube((side * 1.08, -.42, .73), (.10, .58, .22), CREAM), cube((side * 1.08, .42, .73), (.10, .58, .22), CREAM)]
    # black lips supply controlled internal ink seams without extra materials.
    hull += [cube((0, 1.025, .76), (1.48, .035, .055), BLACK, outline=False), cube((0, -1.08, .83), (1.42, .035, .04), BLACK, outline=False)]
    turret_size = {1: ((.55, .57), (.44, .45), 1.44), 2: ((.66, .65), (.52, .52), 1.52), 3: ((.76, .68), (.60, .56), 1.58)}[tier]
    bottom, top, z1 = turret_size
    turret = [frustum(1.03, z1, bottom, top, CREAM), cylinder_z((0, .0, 1.025), .65 if tier < 3 else .73, .07, BLACK, 8, outline=False)]
    turret += [cube((0, -.04, z1 + .035), (.50, .45, .07), OFFWHITE), cube((0, -.04, z1 + .12), (.27, .25, .14), CREAM)]
    # Blue/red cap is alpha=0: renderer supplies team color; no side tabs.
    turret += [cube((0, .02, z1 + .20), (.50, .45, .035), (1.0, 1.0, 1.0), 0.0)]
    if tier == 3:
        # Cheek armor widens the strongest silhouette without covering the roof team cap.
        for side in (-1, 1):
            turret.append(cube((side * .76, .08, 1.34), (.16, .58, .26), OFFWHITE))
    barrel_length = {1: .64, 2: .88, 3: .98}[tier]
    barrel_radius = .16 if tier == 3 else .135
    muzzle_outer, muzzle_inner = ((.22, .105) if tier == 3 else (.165, .092))
    barrel = [cylinder_y((0, .63, 1.28), .20 if tier < 3 else .24, .18, BLACK, 8, outline=False), cylinder_y((0, .98, 1.28), barrel_radius, barrel_length, CREAM, 8)]
    if tier == 3: barrel.append(cylinder_y((0, 1.40, 1.28), .19, .12, BLACK, 8, outline=False))
    muzzle_face = .98 + barrel_length / 2
    barrel += [annulus_y(muzzle_face - .035, muzzle_face + .045, muzzle_outer, muzzle_inner, CREAM, 8), cylinder_y((0, muzzle_face + .010, 1.28), muzzle_inner - .006, .045, BLACK, 8, outline=False)]
    hull_obj = combine("HullMesh", hull, material, collection, root)
    turret_obj = combine("TurretMesh", turret, material, collection, turret_root)
    barrel_obj = combine("BarrelMesh", barrel, material, collection, recoil)
    muzzle_y = .98 + barrel_length / 2 + .045
    muzzle = empty("MuzzleAnchor", (0, muzzle_y, 1.28), recoil, anchors)
    hit = empty("HitAnchor", (0, 0, .62), root, anchors)
    return {"tier": tier, "collection": collection, "root": root, "turret": turret_root, "anchors": anchors, "meshes": (hull_obj, turret_obj, barrel_obj), "muzzle": muzzle, "hit": hit}


def select_tier(item: dict[str, object]) -> None:
    bpy.ops.object.select_all(action="DESELECT")
    for obj in (*item["meshes"], *item["anchors"].objects): obj.select_set(True)
    bpy.context.view_layer.objects.active = item["meshes"][0]


def export_tier(item: dict[str, object], path: Path) -> None:
    select_tier(item)
    bpy.ops.export_scene.fbx(filepath=str(path), use_selection=True, axis_forward="-Z", axis_up="Y", apply_unit_scale=True, add_leaf_bones=False, colors_type="LINEAR")


def triangles(meshes: tuple[bpy.types.Object, ...]) -> int:
    return sum((obj.data.calc_loop_triangles() or len(obj.data.loop_triangles)) for obj in meshes)


def look_at(obj: bpy.types.Object, point: tuple[float, float, float]) -> None:
    obj.rotation_euler = (Vector(point) - obj.location).to_track_quat("-Z", "Y").to_euler()


def preview_setup() -> tuple[bpy.types.Object, bpy.types.Object]:
    scene = bpy.context.scene; scene.render.engine = "BLENDER_EEVEE"; scene.view_settings.view_transform = "Standard"; scene.view_settings.look = "None"; scene.render.image_settings.file_format = "PNG"; scene.world = bpy.data.worlds.new("CreamWorld"); scene.world.color = CREAM
    scene.world.use_nodes = True; scene.world.node_tree.nodes["Background"].inputs["Color"].default_value = (*CREAM, 1); scene.world.node_tree.nodes["Background"].inputs["Strength"].default_value = .45
    pc = coll("Preview")
    bpy.ops.mesh.primitive_plane_add(size=200, location=(0, 0, -.02)); floor = bpy.context.object; floor.name = "CreamGround"; pc.objects.link(floor)
    light_data = bpy.data.lights.new("Key", "AREA"); light_data.energy = 850; light_data.shape = "DISK"; light_data.size = 5; light = bpy.data.objects.new("Key", light_data); light.location = (-4, 5, 7); look_at(light, (0, 0, .8)); pc.objects.link(light)
    camera_data = bpy.data.cameras.new("Camera"); camera_data.type = "ORTHO"; camera_data.ortho_scale = 4.6; camera = bpy.data.objects.new("Camera", camera_data); camera.location = (-5, 7, 5); look_at(camera, (0, 0, .8)); pc.objects.link(camera); scene.camera = camera
    return camera, floor


def set_team(material: bpy.types.Material, rgba: tuple[float, float, float, float]) -> None:
    material.node_tree.nodes["TeamColor"].outputs[0].default_value = rgba


def render(paths: dict[str, Path], tiers: list[dict[str, object]], material: bpy.types.Material, camera: bpy.types.Object, floor: bpy.types.Object) -> None:
    scene = bpy.context.scene; scene.render.resolution_x = scene.render.resolution_y = 900
    for item, x in zip(tiers, (-2.5, 0.0, 2.5)): item["root"].location.x = x; item["collection"].hide_render = False
    for label, color, position in (("comparison_blue", BLUE, (-6, 8, 6)), ("comparison_red", RED, (-6, 8, 6)), ("comparison_top", BLUE, (0, 0, 9))):
        set_team(material, color); camera.data.ortho_scale = 8.6; camera.location = position; look_at(camera, (0, 0, .75)); scene.render.filepath = str(paths["renders"] / f"{label}.png"); bpy.ops.render.render(write_still=True)
    for item in tiers:
        for other in tiers: other["collection"].hide_render = other is not item
        item["root"].location.x = 0
        for team_name, color in (("blue", BLUE), ("red", RED)):
            set_team(material, color); camera.data.ortho_scale = 4.6; camera.location = (-5, 7, 5); look_at(camera, (0, 0, .8)); scene.render.resolution_x = scene.render.resolution_y = 512; scene.render.filepath = str(paths["renders"] / f"tier{item['tier']}_{team_name}.png"); bpy.ops.render.render(write_still=True)
    for item in tiers: item["root"].location = (0, 0, 0); item["collection"].hide_render = item["tier"] != 1


def main() -> None:
    opt = args(); paths = output_paths(opt.output_root.resolve(), opt.overwrite); clear(); material = make_preview_material(); master = coll("HeavyTank_A1_V002"); tiers = [build_tier(tier, material, master) for tier in (1, 2, 3)]
    metrics = {"schema": "a1-heavy-tank-v002", "axis": "Z-up, +Y forward", "materialCountPerTier": 1, "submeshCountPerMesh": 1, "vertexColor": {"attribute": "Color", "domain": "CORNER", "type": "FLOAT_COLOR", "teamCapAlpha": 0, "otherAlpha": 1}, "tiers": []}
    for item in tiers:
        export_tier(item, paths["export"] / f"HeavyTank_A1_Tier{item['tier']}.fbx")
        meshes = item["meshes"]; bounds = [(tuple(min(vertex.co[i] for vertex in obj.data.vertices) for i in range(3)), tuple(max(vertex.co[i] for vertex in obj.data.vertices) for i in range(3))) for obj in meshes]
        metrics["tiers"].append({"tier": item["tier"], "triangles": triangles(meshes), "meshCount": len(meshes), "materialCount": 1, "submeshCountPerMesh": 1, "totalSubmeshes": 3, "boundsPerMesh": bounds, "anchors": {"TurretRoot": tuple(item["turret"].matrix_world.translation), "MuzzleAnchor": tuple(item["muzzle"].matrix_world.translation), "HitAnchor": tuple(item["hit"].matrix_world.translation)}})
    camera, floor = preview_setup()
    for item in tiers:
        item["collection"].hide_viewport = item["tier"] != 1
        item["collection"].hide_render = item["tier"] != 1
    bpy.ops.wm.save_as_mainfile(filepath=str(paths["blend"]))
    paths["metrics"].write_text(json.dumps(metrics, indent=2), encoding="utf-8")
    if not opt.skip_render: render(paths, tiers, material, camera, floor)


if __name__ == "__main__": main()
