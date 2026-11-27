"""Checks that the render test content has no missing references (sub-step 1.7).

Run by tools/dev/setup-render-test.ps1 -VerifyReferences through the editor's "pythonscript" commandlet.
For every asset under the checked folders, lists its hard package dependencies that do not exist,
and writes the result to Saved/RenderTest/content-check.json (the commandlet's exit code says nothing
about the script, so the caller reads this file).
"""

import json
import os

import unreal

CHECKED_ROOTS = ["/Game/Characters", "/Game/Fab"]


def package_exists(registry, package_name):
    if package_name.startswith("/Script/"):
        return True  # native classes, always present
    return len(registry.get_assets_by_package_name(package_name)) > 0


def main():
    registry = unreal.AssetRegistryHelpers.get_asset_registry()
    registry.search_all_assets(True)  # synchronous: the commandlet starts before the scan ends

    hard = unreal.AssetRegistryDependencyOptions(
        include_soft_package_references=False,
        include_hard_package_references=True,
        include_searchable_names=False,
        include_soft_management_references=False,
        include_hard_management_references=False,
    )
    soft = unreal.AssetRegistryDependencyOptions(
        include_soft_package_references=True,
        include_hard_package_references=False,
        include_searchable_names=False,
        include_soft_management_references=False,
        include_hard_management_references=False,
    )

    checked = []
    missing_hard = []
    missing_soft = []
    for root in CHECKED_ROOTS:
        for asset in registry.get_assets_by_path(root, recursive=True):
            package = str(asset.package_name)
            checked.append(package)
            for dependency in registry.get_dependencies(package, hard) or []:
                if not package_exists(registry, str(dependency)):
                    missing_hard.append({"asset": package, "missing": str(dependency)})
            for dependency in registry.get_dependencies(package, soft) or []:
                if not package_exists(registry, str(dependency)):
                    missing_soft.append({"asset": package, "missing": str(dependency)})

    output_dir = os.path.join(unreal.Paths.project_saved_dir(), "RenderTest")
    os.makedirs(output_dir, exist_ok=True)
    result = {
        "checkedPackages": len(checked),
        "missingHard": missing_hard,
        "missingSoft": missing_soft,
    }
    with open(os.path.join(output_dir, "content-check.json"), "w", encoding="utf-8") as stream:
        json.dump(result, stream, indent=2)
    unreal.log(f"Render test content check: {len(checked)} packages, "
               f"{len(missing_hard)} missing hard references, {len(missing_soft)} missing soft references")


main()
