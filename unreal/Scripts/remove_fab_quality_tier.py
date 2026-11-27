"""Removes one quality tier (e.g. "Raw") of the Fab assets, through the editor, only when it is unused.

Run by the editor's "pythonscript" commandlet (editor closed), for example from PowerShell:
    $env:EUDORIA_FAB_TIER = 'Raw'; $env:EUDORIA_DRY_RUN = '1'
    UnrealEditor-Cmd.exe <uproject> -run=pythonscript -script=<this file> -unattended -nullrhi
Layout written by the Fab plugin (0.0.23): /Game/Fab/Megascans/<Kind>/<Asset>/<Tier>/... ; each tier
folder is self-contained (mesh, material instance, textures).

A tier folder is deleted only if no package outside the folders being removed references any package
inside them (hard or soft reference). With EUDORIA_DRY_RUN=1 nothing is deleted. The result is written
to Saved/RenderTest/remove-tier.json (the commandlet's exit code says nothing about the script).
"""

import json
import os

import unreal

ROOT = "/Game/Fab/Megascans"


def main():
    tier = os.environ.get("EUDORIA_FAB_TIER", "").strip()
    dry_run = os.environ.get("EUDORIA_DRY_RUN", "1") != "0"
    result = {"tier": tier, "dryRun": dry_run, "folders": [], "blockers": [], "deleted": [], "errors": []}

    if not tier:
        result["errors"].append("EUDORIA_FAB_TIER is not set")
        return result

    registry = unreal.AssetRegistryHelpers.get_asset_registry()
    registry.search_all_assets(True)

    # Tier folders: <ROOT>/<Kind>/<Asset>/<tier>
    folders = []
    for kind in registry.get_sub_paths(ROOT, False):
        for asset in registry.get_sub_paths(str(kind), False):
            candidate = f"{asset}/{tier}"
            if registry.has_assets(candidate, True):
                folders.append(candidate)
    result["folders"] = folders

    inside = set()
    for folder in folders:
        for data in registry.get_assets_by_path(folder, recursive=True):
            inside.add(str(data.package_name))

    options = unreal.AssetRegistryDependencyOptions(
        include_soft_package_references=True,
        include_hard_package_references=True,
        include_searchable_names=False,
        include_soft_management_references=False,
        include_hard_management_references=False,
    )
    for package in sorted(inside):
        for referencer in registry.get_referencers(package, options) or []:
            referencer = str(referencer)
            if referencer not in inside:
                result["blockers"].append({"package": package, "referencedBy": referencer})

    if dry_run or result["blockers"]:
        return result

    for folder in folders:
        if unreal.EditorAssetLibrary.delete_directory(folder):
            result["deleted"].append(folder)
        else:
            result["errors"].append(f"delete_directory failed: {folder}")
    return result


def write(result):
    output_dir = os.path.join(unreal.Paths.project_saved_dir(), "RenderTest")
    os.makedirs(output_dir, exist_ok=True)
    with open(os.path.join(output_dir, "remove-tier.json"), "w", encoding="utf-8") as stream:
        json.dump(result, stream, indent=2)
    unreal.log(f"Fab tier removal: {json.dumps(result)[:2000]}")


write(main())
