"""Build the approved A1 unit.heavy_tank low-poly source asset in Blender 5.2.1.

Run from Blender; this script does not infer production acceptance.  Geometry follows
the checked canonical A1 crop (milk-white tank, charcoal track band, blue turret tabs)
without placing that image into the scene.  Blender convention is Z-up, forward +Y.

Examples:
  blender --background --python build_heavy_tank.py -- --output-root ..\\
  blender --background --python build_heavy_tank.py -- --output-root ..\\v002 --skip-render

The output root must be empty unless --overwrite is passed.  This avoids replacing an
already produced neighboring version by accident.  Exports include model, outline
shells, and animation anchors; cameras and preview lights are excluded.
"""

from __future__ import annotations

import argparse
import bmesh
from collections import defaultdict
import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector


SCRIPT_PATH = Path(__file__).resolve()
VERSION_ROOT = SCRIPT_PATH.parent.parent
REFERENCE_CROP = VERSION_ROOT / "reference" / "canonical.png"
TARGET_BASE_TRIANGLES = 2000
TARGET_OUTLINE_TRIANGLES = 4000


def arguments() -> argparse.Namespace:
    argv = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    parser = argparse.ArgumentParser(description="Build A1 unit.heavy_tank.")
    parser.add_argument("--output-root", type=Path, default=VERSION_ROOT)
    parser.add_argument("--skip-render", action="store_true")
    parser.add_argument("--overwrite", action="store_true")
    return parser.parse_args(argv)


def ensure_output_paths(root: Path, overwrite: bool) -> dict[str, Path]:
    paths = {
        "blend": root / "blender" / "heavy_tank_work.blend",
        "fbx": root / "export" / "HeavyTank_A1.fbx",
        "glb": root / "export" / "HeavyTank_A1.glb",
        "metrics": root / "export" / "metrics.json",
        "beauty": root / "renders" / "beauty.png",
        "views": root / "renders" / "views",
    }
    existing = [path for key, path in paths.items() if key != "views" and path.exists()]
    if existing and not overwrite:
        names = ", ".join(str(path) for path in existing)
        raise RuntimeError(f"Refusing to overwrite existing v001 outputs: {names}. Use a new --output-root.")
    for key, path in paths.items():
        (path if key == "views" else path.parent).mkdir(parents=True, exist_ok=True)
    return paths


def clear_scene() -> None:
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for collection in list(bpy.data.collections):
        bpy.data.collections.remove(collection)


def collection(name: str) -> bpy.types.Collection:
    result = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(result)
    return result


def link_only(obj: bpy.types.Object, target: bpy.types.Collection) -> None:
    for old in list(obj.users_collection):
        old.objects.unlink(obj)
    target.objects.link(obj)


def set_parent_keep_world(obj: bpy.types.Object, parent: bpy.types.Object) -> None:
    """Parent without summing authored world coordinates into the parent transform."""
    bpy.context.view_layer.update()
    world = obj.matrix_world.copy()
    obj.parent = parent
    obj.matrix_parent_inverse = parent.matrix_world.inverted()
    obj.matrix_world = world
    bpy.context.view_layer.update()


def material(name: str, color: tuple[float, float, float, float], roughness: float = 0.72) -> bpy.types.Material:
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    principled = nodes.get("Principled BSDF")
    principled.inputs["Base Color"].default_value = color
    principled.inputs["Roughness"].default_value = roughness
    principled.inputs["Metallic"].default_value = 0.0
    mat.diffuse_color = color
    mat["base_rgba"] = list(color)
    return mat


def outline_material() -> bpy.types.Material:
    mat = bpy.data.materials.new("M_A1_OutlineBlackEmission")
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    nodes.clear()
    output = nodes.new("ShaderNodeOutputMaterial")
    emission = nodes.new("ShaderNodeEmission")
    emission.inputs["Color"].default_value = (0.004, 0.004, 0.004, 1.0)
    emission.inputs["Strength"].default_value = 0.22
    mat.node_tree.links.new(emission.outputs["Emission"], output.inputs["Surface"])
    try:
        mat.use_backface_culling = True
    except AttributeError:
        pass
    mat["backface_culling"] = True
    mat["outline_material_source"] = "black emission; inverted normal-offset shell exported from OUTLINE collection"
    return mat


def recalc_outward(objects: list[bpy.types.Object]) -> None:
    for obj in objects:
        if obj.type != "MESH":
            continue
        bm = bmesh.new()
        bm.from_mesh(obj.data)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bm.to_mesh(obj.data)
        bm.free()
        obj.data.update()


def apply_bevel(obj: bpy.types.Object, width: float, segments: int = 1) -> None:
    modifier = obj.modifiers.new("A1_Controlled_Bevel", "BEVEL")
    modifier.width = width
    modifier.segments = segments
    modifier.limit_method = "ANGLE"
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=modifier.name)


def cube(name: str, location: tuple[float, float, float], dimensions: tuple[float, float, float], mat: bpy.types.Material, coll: bpy.types.Collection, bevel: float = 0.0) -> bpy.types.Object:
    bpy.ops.mesh.primitive_cube_add(location=location)
    obj = bpy.context.object
    obj.name = name
    obj.dimensions = dimensions
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel:
        apply_bevel(obj, bevel)
    obj.data.materials.append(mat)
    link_only(obj, coll)
    return obj


def cylinder(name: str, location: tuple[float, float, float], radius: float, depth: float, mat: bpy.types.Material, coll: bpy.types.Collection, rotation: tuple[float, float, float] = (0.0, 0.0, 0.0), vertices: int = 12) -> bpy.types.Object:
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=depth, location=location, rotation=rotation)
    obj = bpy.context.object
    obj.name = name
    obj.data.materials.append(mat)
    link_only(obj, coll)
    return obj


def annular_muzzle(name: str, y_back: float, y_front: float, outer_radius: float, inner_radius: float, mat: bpy.types.Material, coll: bpy.types.Collection, segments: int = 12) -> bpy.types.Object:
    """Cream annular muzzle rim around a recessed black bore, axis aligned to forward +Y."""
    vertices: list[tuple[float, float, float]] = []
    for y in (y_back, y_front):
        for radius in (outer_radius, inner_radius):
            for index in range(segments):
                angle = math.tau * index / segments
                vertices.append((radius * math.cos(angle), y, 1.29 + radius * math.sin(angle)))
    faces: list[tuple[int, ...]] = []
    for index in range(segments):
        nxt = (index + 1) % segments
        # outer tube / inner bore wall / back annulus / front annulus
        faces.extend(((index, nxt, 2 * segments + nxt, 2 * segments + index),
                      (segments + index, 3 * segments + index, 3 * segments + nxt, segments + nxt),
                      (index, segments + index, segments + nxt, nxt),
                      (2 * segments + index, 2 * segments + nxt, 3 * segments + nxt, 3 * segments + index)))
    return custom_prism(name, vertices, faces, mat, coll)


def custom_prism(name: str, vertices: list[tuple[float, float, float]], faces: list[tuple[int, ...]], mat: bpy.types.Material, coll: bpy.types.Collection, bevel: float = 0.0) -> bpy.types.Object:
    mesh = bpy.data.meshes.new(f"{name}_Mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.materials.append(mat)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    coll.objects.link(obj)
    if bevel:
        apply_bevel(obj, bevel)
    return obj


def hull_wedge(mat: bpy.types.Material, coll: bpy.types.Collection) -> bpy.types.Object:
    # Three profiles replace the former single dozer plate with a rounded two-facet nose.
    # (z, half-width, rear-y, front-y), Blender forward is +Y.
    profiles = ((0.22, 0.67, -1.12, 1.04), (0.53, 1.00, -1.23, 1.24), (0.96, 0.90, -1.08, 1.03))
    vertices: list[tuple[float, float, float]] = []
    for z, half_width, rear, front in profiles:
        vertices.extend(((-half_width, rear, z), (half_width, rear, z), (half_width, front, z), (-half_width, front, z)))
    faces: list[tuple[int, ...]] = [tuple(range(3, -1, -1)), tuple(range(8, 12))]
    for profile in range(len(profiles) - 1):
        a, b = profile * 4, (profile + 1) * 4
        for edge in range(4):
            nxt = (edge + 1) % 4
            faces.append((a + edge, a + nxt, b + nxt, b + edge))
    return custom_prism("Hull_Main", vertices, faces, mat, coll, bevel=0.035)


def turret_frustum(mat: bpy.types.Material, coll: bpy.types.Collection) -> bpy.types.Object:
    # Squat eight-sided/trapezoid turret: 1.20 x 1.25 at z=1.04; .95 x 1.0 at z=1.51.
    base_x, base_y, top_x, top_y = 0.60, 0.625, 0.475, 0.50
    cut = 0.12
    bottom = [(-base_x + cut, -base_y, 1.04), (base_x - cut, -base_y, 1.04), (base_x, -base_y + cut, 1.04), (base_x, base_y - cut, 1.04), (base_x - cut, base_y, 1.04), (-base_x + cut, base_y, 1.04), (-base_x, base_y - cut, 1.04), (-base_x, -base_y + cut, 1.04)]
    top = [(-top_x + cut, -top_y, 1.51), (top_x - cut, -top_y, 1.51), (top_x, -top_y + cut, 1.51), (top_x, top_y - cut, 1.51), (top_x - cut, top_y, 1.51), (-top_x + cut, top_y, 1.51), (-top_x, top_y - cut, 1.51), (-top_x, -top_y + cut, 1.51)]
    vertices = bottom + top
    faces = [tuple(range(7, -1, -1)), tuple(range(8, 16))]
    faces.extend((index, (index + 1) % 8, ((index + 1) % 8) + 8, index + 8) for index in range(8))
    return custom_prism("Turret_Armor", vertices, faces, mat, coll, bevel=0.035)


def capsule_loop(radius: float, half_straight: float, center_z: float, segments: int = 8) -> list[tuple[float, float]]:
    """Closed Y/Z capsule: ends Y=±1.15 and outer Z=0..0.82 for the requested track band."""
    points: list[tuple[float, float]] = []
    for index in range(segments + 1):
        angle = math.pi * index / segments
        points.append((half_straight + radius * math.sin(angle), center_z + radius * math.cos(angle)))
    for index in range(segments + 1):
        angle = math.pi + math.pi * index / segments
        points.append((-half_straight + radius * math.sin(angle), center_z + radius * math.cos(angle)))
    return points


def capsule_track(name: str, side: int, mat: bpy.types.Material, coll: bpy.types.Collection) -> bpy.types.Object:
    x_center, half_depth = 0.86 * side, 0.175
    outer = capsule_loop(radius=0.41, half_straight=0.74, center_z=0.41)
    inner = capsule_loop(radius=0.29, half_straight=0.74, center_z=0.41)
    count = len(outer)
    vertices = [(x_center - half_depth, y, z) for y, z in outer] + [(x_center + half_depth, y, z) for y, z in outer]
    vertices += [(x_center - half_depth, y, z) for y, z in inner] + [(x_center + half_depth, y, z) for y, z in inner]
    faces: list[tuple[int, ...]] = []
    for index in range(count):
        next_index = (index + 1) % count
        # Outer rail, inner rail, and two annular side faces: one continuous ring mesh.
        faces.extend(((index, next_index, count + next_index, count + index),
                      (2 * count + index, 3 * count + index, 3 * count + next_index, 2 * count + next_index),
                      (index, 2 * count + index, 2 * count + next_index, next_index),
                      (count + index, count + next_index, 3 * count + next_index, 3 * count + index)))
    return custom_prism(name, vertices, faces, mat, coll)


def add_track(side: int, materials: dict[str, bpy.types.Material], coll: bpy.types.Collection) -> list[bpy.types.Object]:
    parts: list[bpy.types.Object] = [capsule_track(f"Track_{side:+d}_ClosedBand", side, materials["charcoal"], coll)]
    wheel_x = 0.925 * side
    for index, y in enumerate((-0.62, 0.0, 0.62), start=1):
        parts.append(cylinder(f"Track_{side:+d}_Wheel_{index}", (wheel_x, y, 0.43), 0.26, 0.10, materials["gray"], coll, rotation=(0, math.pi / 2, 0), vertices=12))
    return parts


def add_outline(source: bpy.types.Object, outline_mat: bpy.types.Material, coll: bpy.types.Collection) -> bpy.types.Object:
    """Create one real exported inverted normal-offset shell; no Solidify or Freestyle."""
    outline = source.copy()
    outline.data = source.data.copy()
    outline.name = f"Outline_{source.name}"
    coll.objects.link(outline)
    outline.data.materials.clear()
    outline.data.materials.append(outline_mat)
    bm = bmesh.new()
    bm.from_mesh(outline.data)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    for vertex in bm.verts:
        vertex.co += vertex.normal.normalized() * 0.027
    bmesh.ops.reverse_faces(bm, faces=bm.faces)
    bm.to_mesh(outline.data)
    bm.free()
    outline.data.update()
    if source.parent:
        set_parent_keep_world(outline, source.parent)
    outline["outline_source"] = source.name
    outline["outline_method"] = "inverted single mesh shell, normal offset 0.027, black emission material"
    return outline


def empty(name: str, location: tuple[float, float, float], parent: bpy.types.Object, coll: bpy.types.Collection) -> bpy.types.Object:
    obj = bpy.data.objects.new(name, None)
    obj.empty_display_type = "ARROWS"
    obj.empty_display_size = 0.14
    obj.location = location
    coll.objects.link(obj)
    set_parent_keep_world(obj, parent)
    return obj


def look_at(obj: bpy.types.Object, target: tuple[float, float, float]) -> None:
    obj.rotation_euler = (Vector(target) - obj.location).to_track_quat("-Z", "Y").to_euler()


def camera(name: str, location: tuple[float, float, float], coll: bpy.types.Collection) -> bpy.types.Object:
    data = bpy.data.cameras.new(name)
    data.type = "ORTHO"
    data.ortho_scale = 4.5
    obj = bpy.data.objects.new(name, data)
    obj.location = location
    look_at(obj, (0.0, 0.0, 0.8))
    coll.objects.link(obj)
    return obj


def configure_scene(preview_coll: bpy.types.Collection) -> tuple[bpy.types.Object, bpy.types.Object]:
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.look = "None"
    scene.view_settings.exposure = 0.0
    scene.view_settings.gamma = 1.0
    world = bpy.data.worlds.new("A1_Transparent_World") if not bpy.data.worlds else bpy.data.worlds[0]
    scene.world = world
    world.use_nodes = True
    background = world.node_tree.nodes.get("Background")
    background.inputs["Color"].default_value = (0.96, 0.94, 0.87, 1.0)
    background.inputs["Strength"].default_value = 0.35
    key_data = bpy.data.lights.new("SoftKey", "AREA")
    key_data.energy = 700
    key_data.shape = "DISK"
    key_data.size = 5.0
    key = bpy.data.objects.new("SoftKey", key_data)
    key.location = (-4.0, 4.0, 6.0)
    look_at(key, (0.0, 0.0, 0.7))
    preview_coll.objects.link(key)
    fill_data = bpy.data.lights.new("AmbientFill", "AREA")
    fill_data.energy = 250
    fill_data.size = 4.0
    fill = bpy.data.objects.new("AmbientFill", fill_data)
    fill.location = (4.0, 2.0, 4.0)
    look_at(fill, (0.0, 0.0, 0.8))
    preview_coll.objects.link(fill)
    floor_mat = material("M_A1_PaperPreview", (0.96, 0.94, 0.87, 1.0))
    floor_principled = floor_mat.node_tree.nodes.get("Principled BSDF")
    floor_principled.inputs["Base Color"].default_value = (0.96, 0.94, 0.87, 1.0)
    floor_principled.inputs["Roughness"].default_value = 1.0
    floor_principled.inputs["Emission Color"].default_value = (0.96, 0.94, 0.87, 1.0)
    floor_principled.inputs["Emission Strength"].default_value = 0.35
    bpy.ops.mesh.primitive_plane_add(size=200.0, location=(0.0, 0.0, -0.035))
    floor = bpy.context.object
    floor.name = "PreviewOnly_PaperGround"
    floor.data.materials.append(floor_mat)
    link_only(floor, preview_coll)
    return camera("Camera_A1_3Quarter", (-5.0, 7.0, 6.0), preview_coll), floor


def configure_toon_preview(materials: dict[str, bpy.types.Material]) -> None:
    """Preview-only A1 nodes; call only after native Principled exports."""
    for mat in materials.values():
        base = mat.get("base_rgba", list(mat.diffuse_color))
        nodes = mat.node_tree.nodes
        nodes.clear()
        output = nodes.new("ShaderNodeOutputMaterial")
        diffuse = nodes.new("ShaderNodeBsdfDiffuse")
        to_rgb = nodes.new("ShaderNodeShaderToRGB")
        ramp = nodes.new("ShaderNodeValToRGB")
        ramp.color_ramp.interpolation = "CONSTANT"
        ramp.color_ramp.elements[0].position = 0.32
        ramp.color_ramp.elements[0].color = (0.65, 0.65, 0.65, 1.0)
        middle = ramp.color_ramp.elements.new(0.52)
        middle.color = (0.85, 0.85, 0.85, 1.0)
        ramp.color_ramp.elements[1].position = 0.72
        ramp.color_ramp.elements[1].color = (1.0, 1.0, 1.0, 1.0)
        base_color = nodes.new("ShaderNodeRGB")
        base_color.outputs[0].default_value = base
        multiply = nodes.new("ShaderNodeMixRGB")
        multiply.blend_type = "MULTIPLY"
        multiply.inputs[0].default_value = 1.0
        emission = nodes.new("ShaderNodeEmission")
        mat.node_tree.links.new(diffuse.outputs[0], to_rgb.inputs[0])
        mat.node_tree.links.new(to_rgb.outputs[0], ramp.inputs[0])
        mat.node_tree.links.new(ramp.outputs[0], multiply.inputs[1])
        mat.node_tree.links.new(base_color.outputs[0], multiply.inputs[2])
        mat.node_tree.links.new(multiply.outputs[0], emission.inputs[0])
        mat.node_tree.links.new(emission.outputs[0], output.inputs[0])
        mat["preview_shader"] = "Diffuse -> ShaderToRGB -> Constant ColorRamp (.65/.85/1.0) * base RGBA -> Emission"


def evaluated_triangles(objects: list[bpy.types.Object]) -> int:
    depsgraph = bpy.context.evaluated_depsgraph_get()
    total = 0
    for obj in objects:
        if obj.type != "MESH":
            continue
        evaluated = obj.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh()
        mesh.calc_loop_triangles()
        total += len(mesh.loop_triangles)
        evaluated.to_mesh_clear()
    return total


def select_exportable(model_coll: bpy.types.Collection, outline_coll: bpy.types.Collection, anchor_coll: bpy.types.Collection) -> None:
    bpy.ops.object.select_all(action="DESELECT")
    for coll in (model_coll, outline_coll, anchor_coll):
        for obj in coll.objects:
            obj.select_set(True)
    bpy.context.view_layer.objects.active = next(iter(model_coll.objects))


def join_rigid_meshes(coll: bpy.types.Collection, names: dict[str, str]) -> None:
    """Join only rigid meshes sharing one parent; anchors remain outside this operation."""
    groups: dict[bpy.types.Object | None, list[bpy.types.Object]] = defaultdict(list)
    for obj in list(coll.objects):
        if obj.type == "MESH":
            groups[obj.parent].append(obj)
    bpy.ops.object.select_all(action="DESELECT")
    for parent, objects in groups.items():
        objects = [obj for obj in objects if obj.name in bpy.data.objects]
        if not objects:
            continue
        active = objects[0]
        if len(objects) > 1:
            for obj in objects:
                obj.select_set(True)
            bpy.context.view_layer.objects.active = active
            bpy.ops.object.join()
        active.name = names[parent.name]
        bpy.ops.object.select_all(action="DESELECT")


def export(paths: dict[str, Path], model_coll: bpy.types.Collection, outline_coll: bpy.types.Collection, anchor_coll: bpy.types.Collection) -> None:
    select_exportable(model_coll, outline_coll, anchor_coll)
    bpy.ops.export_scene.fbx(
        filepath=str(paths["fbx"]),
        use_selection=True,
        axis_forward="-Z",
        axis_up="Y",
        apply_unit_scale=True,
        bake_space_transform=False,
        add_leaf_bones=False,
    )
    bpy.ops.export_scene.gltf(filepath=str(paths["glb"]), export_format="GLB", use_selection=True)


def render_views(paths: dict[str, Path], cam: bpy.types.Object, floor: bpy.types.Object) -> None:
    scene = bpy.context.scene
    scene.camera = cam
    scene.render.film_transparent = False
    floor.hide_render = False
    cam.location = (-5.0, 7.0, 6.0)
    look_at(cam, (0.0, 0.0, 0.85))
    scene.render.resolution_x = 1200
    scene.render.resolution_y = 1200
    scene.render.filepath = str(paths["beauty"])
    bpy.ops.render.render(write_still=True)
    scene.render.film_transparent = True
    floor.hide_render = True
    views = {
        "front": (0.0, 6.0, 1.2),
        "rear": (0.0, -6.0, 1.2),
        "left": (-6.0, 0.0, 1.2),
        "right": (6.0, 0.0, 1.2),
        "top": (0.0, 0.0, 7.0),
        "3quarter": (-5.0, 7.0, 5.0),
    }
    scene.render.resolution_x = 512
    scene.render.resolution_y = 512
    for label, position in views.items():
        cam.location = position
        look_at(cam, (0.0, 0.0, 0.8))
        scene.render.filepath = str(paths["views"] / f"{label}.png")
        bpy.ops.render.render(write_still=True)


def main() -> None:
    options = arguments()
    paths = ensure_output_paths(options.output_root.resolve(), options.overwrite)
    clear_scene()
    model_coll = collection("MODEL_HeavyTank_A1")
    outline_coll = collection("OUTLINE_HeavyTank_A1")
    anchors_coll = collection("ANCHORS_HeavyTank_A1")
    preview_coll = collection("PREVIEW_ONLY")
    materials = {
        "milk": material("M_A1_MilkWhite", (0.91, 0.89, 0.80, 1.0)),
        "offwhite": material("M_A1_OffWhite", (0.82, 0.80, 0.72, 1.0)),
        "charcoal": material("M_A1_Charcoal", (0.025, 0.027, 0.025, 1.0), 0.85),
        "gray": material("M_A1_WheelGray", (0.26, 0.27, 0.25, 1.0)),
        "blue": material("M_A1_TeamBlue", (0.02, 0.37, 0.85, 1.0), 0.56),
    }
    outline_mat = outline_material()

    model_parts: list[bpy.types.Object] = []
    model_parts.append(hull_wedge(materials["milk"], model_coll))
    # Separate side armor panels: bevelled and sloped, with restrained black bolt details.
    for side in (-1, 1):
        panel = cube(f"Hull_SideArmor_{side:+d}", (1.01 * side, 0.02, 0.72), (0.12, 2.05, 0.38), materials["offwhite"], model_coll, 0.035)
        panel.rotation_euler.y = -0.10 * side
        model_parts.append(panel)
        for index, y in enumerate((-0.68, 0.02, 0.68), start=1):
            bolt = cylinder(f"Hull_Bolt_{side:+d}_{index}", (1.075 * side, y, 0.73), 0.035, 0.022, materials["charcoal"], model_coll, rotation=(0, math.pi / 2, 0), vertices=8)
            model_parts.append(bolt)
    for side in (-1, 1):
        model_parts.extend(add_track(side, materials, model_coll))

    turret_root = bpy.data.objects.new("TurretRoot", None)
    turret_root.empty_display_type = "PLAIN_AXES"
    turret_root.location = (0.0, 0.0, 1.04)
    anchors_coll.objects.link(turret_root)
    ring = cylinder("Turret_Ring", (0.0, 0.0, 1.035), 0.66, 0.09, materials["charcoal"], model_coll, vertices=16)
    turret = turret_frustum(materials["milk"], model_coll)
    set_parent_keep_world(turret, turret_root)
    set_parent_keep_world(ring, turret_root)
    model_parts.extend((ring, turret))
    for side in (-1, 1):
        patch = cube(f"Turret_BluePatch_{side:+d}", (0.538 * side, 0.03, 1.30), (0.018, 0.27, 0.30), materials["blue"], model_coll, 0.008)
        patch.rotation_euler.y = -0.26 * side
        set_parent_keep_world(patch, turret_root)
        model_parts.append(patch)
    hatch_plate = cube("Turret_HatchCreamPlate", (0.0, -0.05, 1.55), (0.48, 0.42, 0.06), materials["offwhite"], model_coll, 0.035)
    hatch_cube = cube("Turret_HatchDarkWhiteCube", (0.0, -0.05, 1.63), (0.25, 0.23, 0.14), materials["milk"], model_coll, 0.025)
    set_parent_keep_world(hatch_plate, turret_root)
    set_parent_keep_world(hatch_cube, turret_root)
    model_parts.extend((hatch_plate, hatch_cube))

    recoil = bpy.data.objects.new("BarrelRecoil", None)
    recoil.empty_display_type = "SINGLE_ARROW"
    recoil.location = (0.0, 0.64, 1.29)
    anchors_coll.objects.link(recoil)
    set_parent_keep_world(recoil, turret_root)
    mantlet = cylinder("Mantlet_DarkCuff", (0.0, 0.62, 1.29), 0.22, 0.22, materials["charcoal"], model_coll, rotation=(math.pi / 2, 0, 0), vertices=12)
    barrel = cylinder("Barrel_MilkWhite", (0.0, 1.15, 1.29), 0.14, 0.85, materials["milk"], model_coll, rotation=(math.pi / 2, 0, 0), vertices=12)
    muzzle_ring = annular_muzzle("Muzzle_CreamAnnularRim", 1.54, 1.62, 0.155, 0.098, materials["milk"], model_coll)
    muzzle_hole = cylinder("Muzzle_RecessedBlackBore", (0.0, 1.555, 1.29), 0.092, 0.055, materials["charcoal"], model_coll, rotation=(math.pi / 2, 0, 0), vertices=12)
    for part in (mantlet, barrel, muzzle_ring, muzzle_hole):
        set_parent_keep_world(part, recoil)
        model_parts.append(part)
    muzzle_anchor = empty("MuzzleAnchor", (0.0, 1.625, 1.29), recoil, anchors_coll)
    muzzle_anchor["purpose"] = "muzzle VFX origin"

    recalc_outward(list(model_coll.objects))
    outlines = [add_outline(part, outline_mat, outline_coll) for part in model_parts if part.type == "MESH"]
    root = bpy.data.objects.new("HeavyTank_A1_Root", None)
    root.empty_display_type = "CUBE"
    root.empty_display_size = 0.25
    root["content_id"] = "unit.heavy_tank"
    root["approved_reference"] = str(REFERENCE_CROP)
    root["axis_convention"] = "Blender Z-up, forward +Y"
    anchors_coll.objects.link(root)
    for obj in model_coll.objects:
        if obj.parent is None:
            set_parent_keep_world(obj, root)
    for obj in outline_coll.objects:
        if obj.parent is None:
            set_parent_keep_world(obj, root)
    set_parent_keep_world(turret_root, root)
    hit_anchor = empty("HitAnchor", (0.0, 0.0, 0.65), root, anchors_coll)
    hit_anchor["purpose"] = "root-level ground hit anchor"

    join_rigid_meshes(model_coll, {root.name: "HullMesh", turret_root.name: "TurretMesh", recoil.name: "BarrelMesh"})
    join_rigid_meshes(outline_coll, {root.name: "Outline_HullMesh", turret_root.name: "Outline_TurretMesh", recoil.name: "Outline_BarrelMesh"})

    cam, floor = configure_scene(preview_coll)
    bpy.context.scene.camera = cam
    # Native exports retain the shared Principled palette before preview-only toon nodes.
    export(paths, model_coll, outline_coll, anchors_coll)
    base_triangles = evaluated_triangles(list(model_coll.objects))
    outline_triangles = evaluated_triangles(list(outline_coll.objects))
    metrics = {
        "asset": "unit.heavy_tank",
        "reference": str(REFERENCE_CROP),
        "axisConvention": "Z-up, forward +Y",
        "targets": {"baseTrianglesMax": TARGET_BASE_TRIANGLES, "outlineTotalTrianglesMax": TARGET_OUTLINE_TRIANGLES},
        "actual": {
            "baseTriangles": base_triangles,
            "outlineTriangles": outline_triangles,
            "totalTriangles": base_triangles + outline_triangles,
            "meshCount": len([obj for obj in (*model_coll.objects, *outline_coll.objects) if obj.type == "MESH"]),
            "materialCount": len(materials) + 1,
            "materials": [*materials.keys(), outline_mat.name],
            "renderer": bpy.context.scene.render.engine,
            "baseWithinTarget": base_triangles <= TARGET_BASE_TRIANGLES,
            "totalWithinOutlineTarget": base_triangles + outline_triangles <= TARGET_OUTLINE_TRIANGLES,
        },
        "outline": {
            "exported": True,
            "collection": outline_coll.name,
            "method": "single inverted mesh shell, vertex normals offset 0.027, black emission material with backface culling",
            "freestyleFallbackUsed": False,
        },
        "exports": {
            "fbx": str(paths["fbx"]),
            "glb": str(paths["glb"]),
            "fbxAxis": {"forward": "-Z", "up": "Y", "applyUnit": True},
            "verification": "not run by builder script; import verification remains required",
        },
        "renders": "skipped" if options.skip_render else "toon preview requested",
    }
    paths["metrics"].write_text(json.dumps(metrics, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    configure_toon_preview(materials)
    bpy.context.scene["preview_shader_not_unity"] = True
    bpy.context.scene["preview_shader"] = "Diffuse/ShaderToRGB/ConstantColorRamp/baseRGBA/Emission"
    # Persist a review-ready Beauty setup in the .blend before per-view transparent renders.
    bpy.context.scene.camera = cam
    bpy.context.scene.render.resolution_x = 1200
    bpy.context.scene.render.resolution_y = 1200
    bpy.context.scene.render.film_transparent = False
    floor.hide_render = False
    cam.location = (-5.0, 7.0, 6.0)
    look_at(cam, (0.0, 0.0, 0.85))
    bpy.ops.wm.save_as_mainfile(filepath=str(paths["blend"]))
    if not options.skip_render:
        render_views(paths, cam, floor)


if __name__ == "__main__":
    main()
