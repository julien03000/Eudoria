"""Lists which Fab meshes are "open" scans, and where their hole is (render test, sub-step 1.7).

Diagnosis only (the map script does the same analysis itself, see fab_mesh_geometry.py).
Run by the editor's "pythonscript" commandlet (editor closed). Result in Saved/RenderTest/open-meshes.json.
"""

import json
import os
import sys

import unreal

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import fab_mesh_geometry  # noqa: E402

ROOT = "/Game/Fab/Megascans"

registry = unreal.AssetRegistryHelpers.get_asset_registry()
registry.search_all_assets(True)
meshes = {}
for data in registry.get_assets_by_path(ROOT, recursive=True):
    if str(data.asset_class_path.asset_name) != "StaticMesh":
        continue
    path = str(data.package_name)
    try:
        meshes[path] = fab_mesh_geometry.analyze(unreal.EditorAssetLibrary.load_asset(path))
    except Exception as error:  # noqa: BLE001 - reported in the result
        meshes[path] = {"error": f"{type(error).__name__}: {error}"}

output_dir = os.path.join(unreal.Paths.project_saved_dir(), "RenderTest")
os.makedirs(output_dir, exist_ok=True)
with open(os.path.join(output_dir, "open-meshes.json"), "w", encoding="utf-8") as stream:
    json.dump({"meshes": meshes}, stream, indent=2)
unreal.log("Open mesh analysis written")
