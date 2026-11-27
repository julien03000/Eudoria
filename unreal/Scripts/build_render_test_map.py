"""Builds the render test scene /Game/RenderTest/L_RenderTest (sub-step 1.7, plan § 6.1).

Run by tools/dev/setup-render-test.ps1 -BuildMap through the editor's "pythonscript" commandlet (editor
closed). The map is rebuilt from scratch each time, always the same: every random choice comes from a
fixed seed. It is NOT versioned (decision D2): it depends on the Fab content, which is out of git too.

Scene: a Nordic forest clearing of about 100 m x 100 m, daylight, against a granite cliff (light bounces
for Lumen); every Fab mesh is Nanite. Five tagged points (Eudoria.Benchmark.1 to 5) are the viewpoints
of the render benchmark (part C). Result in Saved/RenderTest/build-map.json.
"""

import json
import math
import os
import random

import unreal

MAP_PATH = "/Game/RenderTest/L_RenderTest"
FLOOR_MATERIAL_PATH = "/Game/RenderTest/MI_RenderTest_Floor"
SEED = 1707
GAME_MODE = "/Script/Eudoria.EudoriaRenderTestGameMode"

MEGA = "/Game/Fab/Megascans"
SURFACE_MI = MEGA + "/Surfaces/Mossy_Forest_Floor_vfylbge/High/vfylbge_tier_1/Materials/MI_vfylbge"
MESHES = {
    "ground": MEGA + "/3D/Nordic_Mossy_Ground_vi2ldcomw/High/vi2ldcomw_tier_1/StaticMeshes/vi2ldcomw_tier_1",
    "cliff": MEGA + "/3D/Granite_Cliff_rivav/High/rivav_tier_1/StaticMeshes/rivav_tier_1",
    "rock_face": MEGA + "/3D/Mossy_Rock_Face_ti2udfqda/High/ti2udfqda_tier_1/StaticMeshes/ti2udfqda_tier_1",
    "rock_formation": MEGA + "/3D/Mossy_Rock_Formation_plFhY/High/plFhY_tier_1/StaticMeshes/plFhY_tier_1",
    "boulder": MEGA + "/3D/Mossy_Forest_Boulder_wfstcdnaw/High/wfstcdnaw_tier_1/StaticMeshes/wfstcdnaw_tier_1",
    "ledge_rock": MEGA + "/3D/Nordic_Forest_Ledge_Rock_Medium_xislecsqx/High/xislecsqx_tier_1/StaticMeshes/xislecsqx_tier_1",
    "cluster": MEGA + "/3D/Nordic_Forest_Cluster_Medium_xisgcic/High/xisgcic_tier_1/StaticMeshes/xisgcic_tier_1",
    "roots": MEGA + "/3D/Nordic_Forest_Ledge_Root_Large_xj1odjt/High/xj1odjt_tier_1/StaticMeshes/xj1odjt_tier_1",
    "log": MEGA + "/3D/Nordic_Forest_Tree_Log_Large_xiclaj1r/High/xiclaj1r_tier_1/StaticMeshes/xiclaj1r_tier_1",
    "stump": MEGA + "/3D/Spruce_Tree_Stump_tjuxbgfda/High/tjuxbgfda_tier_1/StaticMeshes/tjuxbgfda_tier_1",
}
FERNS = [MEGA + "/Plants/Lady_Fern_wdvlditia/High/wdvlditia_tier_1/StaticMeshes/SM_wdvlditia_Var" + v for v in "ABCDEFGHI"]
DEBRIS = [MEGA + "/3D/Tree_Debris_Pack_vdqjfjhiw/High/vdqjfjhiw_tier_1/StaticMeshes/vdqjfjhiw_LOD0_TIER1_00" + str(i) for i in range(6)]

# Where the player starts and where the benchmark looks from (cm): kept clear of props.
PLAYER_START = unreal.Vector(0.0, -1500.0, 120.0)
BENCHMARK_POINTS = [
    # (location, rotation as pitch, yaw)
    (unreal.Vector(0.0, -1500.0, 250.0), (-5.0, 90.0)),     # 1. the clearing towards the cliff (first view)
    (unreal.Vector(1800.0, 200.0, 300.0), (-8.0, 170.0)),   # 2. across the clearing, side light
    (unreal.Vector(-1600.0, 1300.0, 220.0), (-4.0, -15.0)), # 3. along the foot of the cliff
    (unreal.Vector(300.0, 1700.0, 650.0), (12.0, 95.0)),    # 4. under the overhang, looking up (Lumen bounces)
    (unreal.Vector(700.0, -300.0, 170.0), (-20.0, 215.0)),  # 5. close-up on rocks, ferns and debris (Nanite detail)
]
CLEAR_RADIUS = 450.0  # around the player start and the benchmark points


class Builder:
    def __init__(self):
        self.actors = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
        self.rng = random.Random(SEED)
        self.placed = []  # (x, y, radius) of the props, to avoid piling them up
        self.counts = {}
        self.errors = []
        self.mesh_cache = {}

    def mesh(self, path):
        if path not in self.mesh_cache:
            loaded = unreal.EditorAssetLibrary.load_asset(path)
            if loaded is None:
                self.errors.append("missing mesh " + path)
            self.mesh_cache[path] = loaded
        return self.mesh_cache[path]

    def spawn_mesh(self, folder, path, location, rotation, scale=1.0, material=None):
        mesh = self.mesh(path)
        if mesh is None:
            return None
        actor = self.actors.spawn_actor_from_class(unreal.StaticMeshActor, location, rotation)
        component = actor.static_mesh_component
        component.set_static_mesh(mesh)
        if material is not None:
            component.set_material(0, material)
        actor.set_actor_scale3d(unreal.Vector(scale, scale, scale))
        actor.set_folder_path(folder)
        self.counts[folder] = self.counts.get(folder, 0) + 1
        return actor

    def is_clear(self, x, y, radius):
        for px, py in [(PLAYER_START.x, PLAYER_START.y)] + [(p.x, p.y) for p, _ in BENCHMARK_POINTS]:
            if math.hypot(x - px, y - py) < CLEAR_RADIUS + radius:
                return False
        for ox, oy, other in self.placed:
            if math.hypot(x - ox, y - oy) < (radius + other) * 0.8:
                return False
        return True

    def scatter(self, folder, paths, count, radius, area, scale_range, z=0.0, keep_clear=True, sink=0.0):
        """Places count props at random (fixed seed) in area = (min_x, max_x, min_y, max_y)."""
        placed = 0
        for _ in range(count * 30):
            if placed >= count:
                break
            x = self.rng.uniform(area[0], area[1])
            y = self.rng.uniform(area[2], area[3])
            scale = self.rng.uniform(*scale_range)
            if keep_clear and not self.is_clear(x, y, radius * scale):
                continue
            path = paths[self.rng.randrange(len(paths))]
            rotation = unreal.Rotator(roll=self.rng.uniform(-4.0, 4.0), pitch=self.rng.uniform(-4.0, 4.0), yaw=self.rng.uniform(0.0, 360.0))
            self.spawn_mesh(folder, path, unreal.Vector(x, y, z - sink * scale), rotation, scale)
            if keep_clear:
                self.placed.append((x, y, radius * scale))
            placed += 1
        if placed < count:
            self.errors.append(f"{folder}: only {placed} of {count} placed")


def spawn(cls, location, rotation, folder):
    actor = unreal.get_editor_subsystem(unreal.EditorActorSubsystem).spawn_actor_from_class(cls, location, rotation)
    actor.set_folder_path(folder)
    return actor


def floor_material():
    """Child of the Megascans surface with a tiling for a 120 m plane (texture about 2 m wide)."""
    if unreal.EditorAssetLibrary.does_asset_exist(FLOOR_MATERIAL_PATH):
        unreal.EditorAssetLibrary.delete_asset(FLOOR_MATERIAL_PATH)
    parent = unreal.EditorAssetLibrary.load_asset(SURFACE_MI)
    if parent is None:
        return None
    tools = unreal.AssetToolsHelpers.get_asset_tools()
    package, name = FLOOR_MATERIAL_PATH.rsplit("/", 1)
    material = tools.create_asset(name, package, unreal.MaterialInstanceConstant, unreal.MaterialInstanceConstantFactoryNew())
    unreal.MaterialEditingLibrary.set_material_instance_parent(material, parent)
    unreal.MaterialEditingLibrary.set_material_instance_scalar_parameter_value(material, "Tiling", 60.0)
    unreal.EditorAssetLibrary.save_loaded_asset(material)
    return material


def build():
    result = {"map": MAP_PATH, "seed": SEED, "counts": {}, "errors": []}
    levels = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)

    if unreal.EditorAssetLibrary.does_asset_exist(MAP_PATH):
        unreal.EditorAssetLibrary.delete_asset(MAP_PATH)
    if not levels.new_level(MAP_PATH, False):  # not World Partition: a small, single level
        result["errors"].append("new_level failed")
        return result

    b = Builder()

    # --- Light and sky (all movable: Lumen, no baked lighting in this project) --------------------
    sun = spawn(unreal.DirectionalLight, unreal.Vector(0, 0, 1000), unreal.Rotator(roll=0.0, pitch=-38.0, yaw=55.0), "Lighting")
    sun_light = sun.get_component_by_class(unreal.DirectionalLightComponent)
    sun_light.set_mobility(unreal.ComponentMobility.MOVABLE)
    sun_light.set_editor_property("atmosphere_sun_light", True)
    sun_light.set_editor_property("intensity", 10.0)
    sky = spawn(unreal.SkyLight, unreal.Vector(0, 0, 800), unreal.Rotator(), "Lighting")
    sky_light = sky.get_component_by_class(unreal.SkyLightComponent)
    sky_light.set_mobility(unreal.ComponentMobility.MOVABLE)
    sky_light.set_editor_property("real_time_capture", True)
    spawn(unreal.SkyAtmosphere, unreal.Vector(0, 0, 0), unreal.Rotator(), "Lighting")
    spawn(unreal.VolumetricCloud, unreal.Vector(0, 0, 0), unreal.Rotator(), "Lighting")
    fog = spawn(unreal.ExponentialHeightFog, unreal.Vector(0, 0, -200), unreal.Rotator(), "Lighting")
    fog.get_component_by_class(unreal.ExponentialHeightFogComponent).set_editor_property("enable_volumetric_fog", True)
    post = spawn(unreal.PostProcessVolume, unreal.Vector(0, 0, 0), unreal.Rotator(), "Lighting")
    post.set_editor_property("unbound", True)

    # --- Ground: a 120 m plane (collision, tiled moss) under Nanite moss plates ---------------------
    floor = b.spawn_mesh("Ground", "/Engine/BasicShapes/Plane", unreal.Vector(0, 0, 0), unreal.Rotator(), 120.0, floor_material())
    if floor is None:
        b.errors.append("floor plane not created")
    for ix in range(-4, 5):
        for iy in range(-6, 6):
            x = ix * 880.0 + b.rng.uniform(-60.0, 60.0)
            y = iy * 650.0 + b.rng.uniform(-60.0, 60.0)
            yaw = b.rng.choice([0.0, 180.0]) + b.rng.uniform(-6.0, 6.0)
            b.spawn_mesh("Ground", MESHES["ground"], unreal.Vector(x, y, -2.0), unreal.Rotator(roll=0.0, pitch=0.0, yaw=yaw))

    # --- The cliff: three pieces to the north, the middle one leaning over the clearing ------------
    b.spawn_mesh("Cliff", MESHES["cliff"], unreal.Vector(-2400.0, 3000.0, -100.0), unreal.Rotator(roll=0.0, pitch=0.0, yaw=8.0), 1.0)
    b.spawn_mesh("Cliff", MESHES["cliff"], unreal.Vector(400.0, 2650.0, -150.0), unreal.Rotator(roll=-14.0, pitch=0.0, yaw=-4.0), 1.15)
    b.spawn_mesh("Cliff", MESHES["cliff"], unreal.Vector(3100.0, 3100.0, -100.0), unreal.Rotator(roll=0.0, pitch=0.0, yaw=-20.0), 1.0)
    for x, y in ((-2400.0, 3000.0), (400.0, 2650.0), (3100.0, 3100.0)):
        b.placed.append((x, y, 1500.0))

    # --- Props (fixed seed) ----------------------------------------------------------------------
    clearing = (-4500.0, 4500.0, -4500.0, 2300.0)
    b.scatter("Rocks", [MESHES["rock_face"]], 8, 180.0, clearing, (1.0, 1.6), sink=20.0)
    b.scatter("Rocks", [MESHES["rock_formation"]], 12, 170.0, clearing, (0.9, 1.5), sink=15.0)
    b.scatter("Rocks", [MESHES["boulder"]], 16, 100.0, clearing, (0.8, 1.4), sink=15.0)
    b.scatter("Rocks", [MESHES["ledge_rock"]], 10, 170.0, clearing, (0.9, 1.4), sink=10.0)
    b.scatter("Rocks", [MESHES["cluster"]], 30, 110.0, clearing, (0.8, 1.3), sink=10.0)
    b.scatter("Wood", [MESHES["roots"]], 6, 400.0, clearing, (0.9, 1.2), sink=10.0)
    b.scatter("Wood", [MESHES["log"]], 10, 280.0, clearing, (0.9, 1.2))
    b.scatter("Wood", [MESHES["stump"]], 12, 70.0, clearing, (0.9, 1.3), sink=5.0)
    # Small things: allowed close to each other, still away from the viewpoints' feet.
    b.scatter("Plants", FERNS, 260, 0.0, clearing, (0.8, 1.5), keep_clear=False)
    b.scatter("Debris", DEBRIS, 160, 0.0, (-3000.0, 3000.0, -3000.0, 2000.0), (1.0, 2.5), keep_clear=False)

    # --- Player start and benchmark viewpoints ---------------------------------------------------
    spawn(unreal.PlayerStart, PLAYER_START, unreal.Rotator(roll=0.0, pitch=0.0, yaw=90.0), "Gameplay")
    for index, (location, (pitch, yaw)) in enumerate(BENCHMARK_POINTS, start=1):
        point = spawn(unreal.TargetPoint, location, unreal.Rotator(roll=0.0, pitch=pitch, yaw=yaw), "Benchmark")
        point.tags = [unreal.Name(f"Eudoria.Benchmark.{index}")]
        point.set_actor_label(f"Benchmark_{index}")

    # --- Game mode of the map --------------------------------------------------------------------
    world = unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem).get_editor_world()
    game_mode = unreal.load_class(None, GAME_MODE)
    if game_mode is None:
        b.errors.append("game mode class not found: " + GAME_MODE)
    else:
        world.get_world_settings().set_editor_property("default_game_mode", game_mode)

    if not levels.save_current_level():
        b.errors.append("save_current_level failed")

    result["counts"] = b.counts
    result["errors"] = b.errors
    return result


try:
    result = build()
except Exception as error:  # noqa: BLE001 - any failure must end in the result file, not only in the log
    result = {"map": MAP_PATH, "seed": SEED, "counts": {}, "errors": [f"{type(error).__name__}: {error}"]}
if result["errors"] and unreal.EditorAssetLibrary.does_asset_exist(MAP_PATH):
    # A half-built scene must never pass for the test scene.
    unreal.EditorAssetLibrary.delete_asset(MAP_PATH)
output_dir = os.path.join(unreal.Paths.project_saved_dir(), "RenderTest")
os.makedirs(output_dir, exist_ok=True)
with open(os.path.join(output_dir, "build-map.json"), "w", encoding="utf-8") as stream:
    json.dump(result, stream, indent=2)
unreal.log(f"Render test map: {json.dumps(result)}")
