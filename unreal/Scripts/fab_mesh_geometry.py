"""Geometry of the Fab meshes: is a scan "open", and where is its hole? (render test, sub-step 1.7)

A Megascans scan is often a shell captured from one side: its triangles are drawn from the front only,
so through the hole the object disappears. Such a mesh has boundary edges (edges used by one triangle).
No side effect on import: used by analyze_open_meshes.py and build_render_test_map.py.
"""

import math

import unreal

# Kinds of mesh, for placement.
CLOSED = "closed"              # no hole: safe from every side
OPEN_BOTTOM = "open_bottom"    # hole underneath only: bury the rim, no tilt
OPEN_SIDE = "open_side"        # hole on one side (its back): only against a wall of the play area
TWO_SIDED = "two_sided"        # open, but its material is drawn on both sides (leaves): safe

# A boundary that stays in the bottom fifth of the height is "underneath".
BOTTOM_FRACTION = 0.2


def analyze(mesh):
    """Boundary edges of LOD 0 and where they are, in the mesh's own space."""
    description = mesh.get_static_mesh_description(0)
    if description is None:
        return {"error": "no mesh description"}

    # The importer splits vertices along texture seams: two vertices at the same place, each seam edge
    # used by one triangle only. Weld by position first, otherwise every seam looks like a hole.
    weld = {}
    positions = {}

    def welded(vertex_id):
        p = description.get_vertex_position(vertex_id)
        key = (round(p.x, 2), round(p.y, 2), round(p.z, 2))
        if key not in weld:
            weld[key] = len(weld)
            positions[weld[key]] = (p.x, p.y, p.z)
        return weld[key]

    edge_count = description.get_edge_count()
    uses = {}
    for index in range(edge_count):
        edge = unreal.EdgeID(index)
        if not description.is_edge_valid(edge):
            continue
        a = welded(description.get_edge_vertex(edge, 0))
        b = welded(description.get_edge_vertex(edge, 1))
        if a == b:
            continue
        key = (min(a, b), max(a, b))
        uses[key] = uses.get(key, 0) + description.get_num_edge_connected_polygons(edge)

    boundary_vertices = set()
    boundary_edges = 0
    for (a, b), count in uses.items():
        if count == 1:
            boundary_edges += 1
            boundary_vertices.add(a)
            boundary_vertices.add(b)

    bounds = mesh.get_bounds()
    center = bounds.origin
    extent = bounds.box_extent
    result = {
        "edges": edge_count,
        "boundaryEdges": boundary_edges,
        "boundsCenter": [round(center.x, 1), round(center.y, 1), round(center.z, 1)],
        "boundsExtent": [round(extent.x, 1), round(extent.y, 1), round(extent.z, 1)],
        "twoSidedMaterial": _all_materials_two_sided(mesh),
    }
    if boundary_vertices:
        n = float(len(boundary_vertices))
        sx = sum(positions[v][0] for v in boundary_vertices) / n
        sy = sum(positions[v][1] for v in boundary_vertices) / n
        sz = sum(positions[v][2] for v in boundary_vertices) / n
        top_z = max(positions[v][2] for v in boundary_vertices)
        hole = (sx - center.x, sy - center.y, sz - center.z)
        length = math.sqrt(sum(c * c for c in hole)) or 1.0
        bottom = center.z - extent.z
        result.update({
            # Unit vector, mesh space: where the hole is (e.g. [0, 0, -1] = open underneath).
            "holeDirection": [round(c / length, 3) for c in hole],
            # Highest boundary point: absolute (mesh space) and as a fraction of the height.
            "boundaryTopZ": round(top_z, 1),
            "boundaryTopFraction": round((top_z - bottom) / ((2.0 * extent.z) or 1.0), 3),
        })
    result["kind"] = kind_of(result)
    return result


def kind_of(info):
    if info.get("boundaryEdges", 0) == 0:
        return CLOSED
    if info.get("twoSidedMaterial"):
        return TWO_SIDED
    if info.get("boundaryTopFraction", 1.0) <= BOTTOM_FRACTION:
        return OPEN_BOTTOM
    return OPEN_SIDE


def _all_materials_two_sided(mesh):
    slots = mesh.get_editor_property("static_materials")
    if not slots:
        return False
    for slot in slots:
        material = slot.get_editor_property("material_interface")
        base = material.get_base_material() if material else None
        if base is None or not base.get_editor_property("two_sided"):
            return False
    return True
