using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace BoxLab
{
    public sealed partial class BoardView
    {
        // These are the dimensions of the generated LowWall prefab, also used by its
        // geometric fallback. A joint is a short piece of the same stonework, not a tall post.
        private const float WallModelLength = .95f;
        private const float WallJointWidth = .23f;
        private const float WallFreeEndInset = .025f;
        private const float WallJoinedEndInset = .105f;

        private Transform wallJunctionRoot;
        private string wallJunctionKey;

        private struct WallRun
        {
            public Transform root;
            public Vector2Int first, last;
            public bool gate;
        }

        /// <summary>
        /// Call after RenderEdges, including during animated frames. Only the rendered topology
        /// is read; no edge, rule, collider, or authoring data is created or changed here.
        /// </summary>
        private void RenderWallJunctions(LevelData level)
        {
            if (!generated || level == null || level.width < 1 || level.height < 1)
            {
                ClearWallJunctions();
                return;
            }
            string key = WallTopologyKey(level);
            if (wallJunctionRoot && wallJunctionRoot.parent == generated && wallJunctionKey == key) return;

            ClearWallJunctions();
            wallJunctionKey = key;
            var runs = new List<WallRun>();
            var nodes = new Dictionary<Vector2Int, int>();

            // The permanent perimeter is already rendered by EnsureGeometry. Restore each
            // segment's true grid centre before replacing the old corner-specific butt trim.
            for (int i = 0; i < generated.childCount; i++)
            {
                Transform segment = generated.GetChild(i);
                if (segment.name != "Permanent perimeter stone wall") continue;
                Vector3 position = segment.localPosition;
                bool vertical = Mathf.Abs((segment.localRotation * Vector3.right).z) > .5f;
                Vector2Int first = vertical
                    ? new Vector2Int(Mathf.RoundToInt(position.x + .5f), Mathf.RoundToInt(position.z))
                    : new Vector2Int(Mathf.RoundToInt(position.x), Mathf.RoundToInt(position.z + .5f));
                Vector2Int last = first + (vertical ? Vector2Int.up : Vector2Int.right);
                AddWallRun(runs, nodes, segment, first, last, false);
            }

            if (level.edges != null)
            {
                for (int i = 0; i < level.edges.Count && i < edgeRoots.Count; i++)
                {
                    EdgeData edge = level.edges[i];
                    if (edge == null || !level.Contains(edge.cell) || !edgeRoots[i]
                        || edge.kind == EdgeKind.Open || IsExteriorEdge(level, edge)) continue;
                    int x = edge.cell % level.width, y = edge.cell / level.width;
                    Vector2Int first, last;
                    switch (edge.direction)
                    {
                        case Direction.North:
                            first = new Vector2Int(x, y + 1); last = first + Vector2Int.right; break;
                        case Direction.East:
                            first = new Vector2Int(x + 1, y); last = first + Vector2Int.up; break;
                        case Direction.South:
                            first = new Vector2Int(x, y); last = first + Vector2Int.right; break;
                        default:
                            first = new Vector2Int(x, y); last = first + Vector2Int.up; break;
                    }
                    // An open gate still has its two stone frame posts. Its connections remain
                    // in the graph, while its passage and opening animation stay untouched.
                    AddWallRun(runs, nodes, edgeRoots[i], first, last, edge.kind == EdgeKind.Gate);
                }
            }

            var container = new GameObject("Wall joins (presentation)");
            container.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            wallJunctionRoot = container.transform;
            wallJunctionRoot.SetParent(generated, false);

            foreach (WallRun run in runs)
            {
                if (!run.root || run.gate) continue; // Never narrow or move a gate opening.
                float firstInset = WallDirectionCount(nodes[run.first]) >= 2 ? WallJoinedEndInset : WallFreeEndInset;
                float lastInset = WallDirectionCount(nodes[run.last]) >= 2 ? WallJoinedEndInset : WallFreeEndInset;
                Vector3 first = WallVertexPosition(run.first), last = WallVertexPosition(run.last);
                Vector3 direction = last - first;
                run.root.localPosition = (first + last) * .5f + direction * ((firstInset - lastInset) * .5f);
                run.root.localScale = new Vector3((1 - firstInset - lastInset) / WallModelLength, 1, 1);
            }

            foreach (var node in nodes)
            {
                int count = WallDirectionCount(node.Value);
                if (count < 2) continue; // An isolated wall already has a modelled end face.
                string shape = count == 4 ? "cross" : count == 3 ? "T"
                    : node.Value == 5 || node.Value == 10 ? "straight" : "L";
                var joint = new GameObject("Wall joint " + node.Key.x + "," + node.Key.y + " - " + shape).transform;
                joint.SetParent(wallJunctionRoot, false);
                // Slightly recessed rather than raised: it cannot hide the gate's status marks.
                // The .01 overlap at each seam closes bevel pinholes without coplanar cap faces.
                joint.localPosition = WallVertexPosition(node.Key) + Vector3.down * .004f;
                DrawWall(joint);
                joint.localScale = new Vector3(WallJointWidth / WallModelLength, 1, 1);
                SetShadows(joint, true, true);
            }
        }

        /// <summary>Call at the start of ClearGenerated; the container belongs to generated.</summary>
        private void ClearWallJunctions()
        {
            if (wallJunctionRoot) DisposeObject(wallJunctionRoot.gameObject);
            wallJunctionRoot = null;
            wallJunctionKey = null;
        }

        private static void AddWallRun(List<WallRun> runs, Dictionary<Vector2Int, int> nodes,
            Transform root, Vector2Int first, Vector2Int last, bool gate)
        {
            runs.Add(new WallRun { root = root, first = first, last = last, gate = gate });
            bool horizontal = first.y == last.y;
            // N/E/S/W bits. OR avoids counting the same physical shared edge twice in a draft.
            nodes.TryGetValue(first, out int firstMask);
            nodes.TryGetValue(last, out int lastMask);
            nodes[first] = firstMask | (horizontal ? 2 : 1);
            nodes[last] = lastMask | (horizontal ? 8 : 4);
        }

        private static Vector3 WallVertexPosition(Vector2Int node)
        { return new Vector3(node.x - .5f, 0, node.y - .5f); }

        private static int WallDirectionCount(int mask)
        {
            int count = 0;
            for (; mask != 0; mask &= mask - 1) count++;
            return count;
        }

        private static string WallTopologyKey(LevelData level)
        {
            var key = new StringBuilder();
            key.Append(level.width).Append('x').Append(level.height);
            if (level.edges != null)
                foreach (EdgeData edge in level.edges)
                {
                    key.Append('|');
                    if (edge == null) key.Append('-');
                    else key.Append(edge.cell).Append(':').Append((int)edge.direction).Append(':').Append((int)edge.kind);
                }
            return key.ToString();
        }
    }
}
