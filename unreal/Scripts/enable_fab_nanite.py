"""Turns Nanite on for every Fab static mesh that has it off, and saves them (render test, plan 1.7 § 6.2).

The Fab plugin (0.0.23) does not always enable Nanite: on 2026-11-27, the "High" tier of the Lady Fern plant
came with Nanite off on its 9 variants, while its "Raw" tier had it on.

Run by the editor's "pythonscript" commandlet (editor closed). With EUDORIA_DRY_RUN=1 nothing is changed.
Result in Saved/RenderTest/enable-nanite.json (the commandlet's exit code says nothing about the script).
"""

import json
import os

import unreal

ROOT = "/Game/Fab"


def main():
    dry_run = os.environ.get("EUDORIA_DRY_RUN", "1") != "0"
    result = {"dryRun": dry_run, "withoutNanite": [], "enabled": [], "errors": []}

    registry = unreal.AssetRegistryHelpers.get_asset_registry()
    registry.search_all_assets(True)
    for data in registry.get_assets_by_path(ROOT, recursive=True):
        if str(data.asset_class_path.asset_name) != "StaticMesh":
            continue
        if str(data.get_tag_value("NaniteEnabled")) == "True":
            continue
        path = str(data.package_name)
        result["withoutNanite"].append(path)
        if dry_run:
            continue

        mesh = unreal.EditorAssetLibrary.load_asset(path)
        if mesh is None:
            result["errors"].append(f"cannot load {path}")
            continue
        settings = mesh.get_editor_property("nanite_settings")
        settings.enabled = True
        mesh.set_editor_property("nanite_settings", settings)  # rebuilds the Nanite data
        if unreal.EditorAssetLibrary.save_loaded_asset(mesh, only_if_is_dirty=False):
            result["enabled"].append(path)
        else:
            result["errors"].append(f"cannot save {path}")
    return result


result = main()
output_dir = os.path.join(unreal.Paths.project_saved_dir(), "RenderTest")
os.makedirs(output_dir, exist_ok=True)
with open(os.path.join(output_dir, "enable-nanite.json"), "w", encoding="utf-8") as stream:
    json.dump(result, stream, indent=2)
unreal.log(f"Fab Nanite: {json.dumps(result)[:2000]}")
