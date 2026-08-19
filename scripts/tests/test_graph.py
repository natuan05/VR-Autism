#!/usr/bin/env python3
"""
Unit tests for LightweightDiGraph and KnowledgeGraph (Milestone M2).
Validates pure-Python graph engine, intra-language edges, cross-stack contract bridging,
PageRank power iteration, impact radius analysis, and JSON roundtrip serialization.

Usage:
    python -m unittest scripts/tests/test_graph.py
    python scripts/tests/test_graph.py
"""

import json
import math
import os
import sys
import tempfile
import unittest
from pathlib import Path

# Ensure repository root is on sys.path
REPO_ROOT = Path(__file__).resolve().parent.parent.parent
if str(REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(REPO_ROOT))

from scripts.ast_parsers import ContractReference, Symbol
from scripts.graph_builder import (
    KnowledgeGraph,
    LightweightDiGraph,
    build_knowledge_graph,
    get_impact_analysis,
    get_jit_context,
)


# ===========================================================================
# 1. LightweightDiGraph Unit Tests
# ===========================================================================

class TestLightweightDiGraph(unittest.TestCase):
    """Comprehensive test suite for the LightweightDiGraph pure-Python engine."""

    def test_node_addition_and_attributes(self):
        g = LightweightDiGraph()
        g.add_node("A", kind="class", subsystem="unity", line_start=10)
        g.add_node("B", kind="method")

        self.assertTrue(g.has_node("A"))
        self.assertTrue(g.has_node("B"))
        self.assertFalse(g.has_node("C"))
        self.assertIn("A", g)
        self.assertEqual(len(g), 2)
        self.assertEqual(g.number_of_nodes(), 2)

        # Attribute retrieval
        self.assertEqual(g.nodes["A"]["kind"], "class")
        self.assertEqual(g.nodes["A"]["subsystem"], "unity")
        self.assertEqual(g.nodes["A"]["line_start"], 10)

        # Attribute updating
        g.add_node("A", line_end=50)
        self.assertEqual(g.nodes["A"]["line_start"], 10)
        self.assertEqual(g.nodes["A"]["line_end"], 50)

    def test_add_nodes_from(self):
        g = LightweightDiGraph()
        g.add_nodes_from(["n1", "n2"], subsystem="python")
        g.add_nodes_from([("n3", {"kind": "tool", "subsystem": "python"})])

        self.assertEqual(len(g), 3)
        self.assertEqual(g.nodes["n1"]["subsystem"], "python")
        self.assertEqual(g.nodes["n3"]["kind"], "tool")

    def test_node_removal(self):
        g = LightweightDiGraph()
        g.add_edge("A", "B", kind="CALLS")
        g.add_edge("B", "C", kind="CALLS")
        g.add_edge("D", "B", kind="CALLS")

        self.assertEqual(len(g), 4)
        self.assertEqual(g.number_of_edges(), 3)

        g.remove_node("B")
        self.assertFalse(g.has_node("B"))
        self.assertEqual(len(g), 3)
        self.assertEqual(g.number_of_edges(), 0)
        self.assertFalse(g.has_edge("A", "B"))
        self.assertFalse(g.has_edge("B", "C"))
        self.assertFalse(g.has_edge("D", "B"))

    def test_edge_addition_and_attributes(self):
        g = LightweightDiGraph()
        g.add_edge("u1", "v1", kind="INHERITS", weight=2.5)

        self.assertTrue(g.has_node("u1"))
        self.assertTrue(g.has_node("v1"))
        self.assertTrue(g.has_edge("u1", "v1"))
        self.assertFalse(g.has_edge("v1", "u1"))
        self.assertEqual(g.number_of_edges(), 1)
        self.assertIn(("u1", "v1"), g.edges)

        self.assertEqual(g.edges[("u1", "v1")]["kind"], "INHERITS")
        self.assertEqual(g.edges[("u1", "v1")]["weight"], 2.5)

    def test_add_edges_from_and_removal(self):
        g = LightweightDiGraph()
        g.add_edges_from([("a", "b"), ("b", "c", {"kind": "CALLS"})])
        self.assertEqual(g.number_of_edges(), 2)

        g.remove_edge("a", "b")
        self.assertFalse(g.has_edge("a", "b"))
        self.assertTrue(g.has_edge("b", "c"))
        self.assertEqual(g.number_of_edges(), 1)

    def test_views_node_and_edge(self):
        g = LightweightDiGraph()
        g.add_node("x", kind="class", score=10)
        g.add_node("y", kind="func", score=20)
        g.add_edge("x", "y", kind="CALLS")

        # NodeView
        self.assertEqual(len(g.nodes), 2)
        self.assertIn("x", g.nodes)
        self.assertEqual(dict(g.nodes(data=True))["x"]["score"], 10)
        self.assertEqual(dict(g.nodes(data="score"))["y"], 20)

        # EdgeView
        self.assertEqual(len(g.edges), 1)
        self.assertIn(("x", "y"), g.edges)
        edge_data = g.edges(data=True)
        self.assertEqual(len(edge_data), 1)
        self.assertEqual(edge_data[0][0], "x")
        self.assertEqual(edge_data[0][1], "y")
        self.assertEqual(edge_data[0][2]["kind"], "CALLS")

    def test_neighbors_successors_predecessors_and_indexing(self):
        g = LightweightDiGraph()
        g.add_edge("A", "B")
        g.add_edge("A", "C")
        g.add_edge("D", "A")

        self.assertEqual(set(g.successors("A")), {"B", "C"})
        self.assertEqual(set(g.neighbors("A")), {"B", "C"})
        self.assertEqual(set(g.predecessors("A")), {"D"})

        # G[u] indexing
        self.assertIn("B", g["A"])
        self.assertIn("C", g["A"])

    def test_in_degree_and_out_degree(self):
        g = LightweightDiGraph()
        g.add_edge("A", "B", weight=2.0)
        g.add_edge("A", "C", weight=3.0)
        g.add_edge("D", "B", weight=1.0)

        # Unweighted
        self.assertEqual(g.out_degree("A"), 2)
        self.assertEqual(g.in_degree("A"), 0)
        self.assertEqual(g.in_degree("B"), 2)
        self.assertEqual(g.out_degree("B"), 0)
        self.assertEqual(g.degree("A"), 2)
        self.assertEqual(g.degree("B"), 2)

        # Weighted
        self.assertEqual(g.out_degree("A", weight="weight"), 5.0)
        self.assertEqual(g.in_degree("B", weight="weight"), 3.0)

    def test_subgraph_copy_and_reverse(self):
        g = LightweightDiGraph()
        g.add_node("A", label="root")
        g.add_node("B", label="child1")
        g.add_node("C", label="child2")
        g.add_edge("A", "B", kind="CONTAINS")
        g.add_edge("A", "C", kind="CONTAINS")
        g.add_edge("B", "C", kind="CALLS")

        # Subgraph
        sub = g.subgraph(["A", "B"])
        self.assertEqual(len(sub), 2)
        self.assertEqual(sub.number_of_edges(), 1)
        self.assertTrue(sub.has_edge("A", "B"))
        self.assertFalse(sub.has_edge("A", "C"))
        self.assertEqual(sub.nodes["A"]["label"], "root")

        # Reverse
        rev = g.reverse()
        self.assertTrue(rev.has_edge("B", "A"))
        self.assertTrue(rev.has_edge("C", "A"))
        self.assertTrue(rev.has_edge("C", "B"))
        self.assertFalse(rev.has_edge("A", "B"))

    def test_traversal_bfs_and_dfs(self):
        g = LightweightDiGraph()
        g.add_edge("1", "2")
        g.add_edge("1", "3")
        g.add_edge("2", "4")
        g.add_edge("3", "5")

        # BFS
        bfs_nodes = [n for n, d in g.bfs("1")]
        self.assertEqual(bfs_nodes[0], "1")
        self.assertEqual(set(bfs_nodes[1:3]), {"2", "3"})
        self.assertEqual(set(bfs_nodes[3:5]), {"4", "5"})

        # Depth limited BFS
        bfs_d1 = [n for n, d in g.bfs("1", max_depth=1)]
        self.assertEqual(len(bfs_d1), 3)

        # DFS
        dfs_nodes = [n for n, d in g.dfs("1")]
        self.assertEqual(len(dfs_nodes), 5)
        self.assertEqual(dfs_nodes[0], "1")

    def test_shortest_path(self):
        g = LightweightDiGraph()
        g.add_edge("A", "B")
        g.add_edge("B", "C")
        g.add_edge("C", "D")
        g.add_edge("A", "X")
        g.add_edge("X", "D")
        g.add_node("Isolated")

        path1 = g.shortest_path("A", "D")
        self.assertEqual(path1, ["A", "X", "D"])

        path_self = g.shortest_path("A", "A")
        self.assertEqual(path_self, ["A"])

        path_unreachable = g.shortest_path("A", "Isolated")
        self.assertEqual(path_unreachable, [])

        path_nonexistent = g.shortest_path("A", "NonExistent")
        self.assertEqual(path_nonexistent, [])

    def test_pagerank_power_iteration_properties(self):
        g = LightweightDiGraph()
        # Empty graph
        self.assertEqual(g.pagerank(), {})

        # Single node
        g.add_node("Solo")
        self.assertEqual(g.pagerank(), {"Solo": 1.0})

        # Disconnected nodes
        g.add_node("Solo2")
        pr_disc = g.pagerank()
        self.assertAlmostEqual(pr_disc["Solo"], 0.5)
        self.assertAlmostEqual(pr_disc["Solo2"], 0.5)

        # Star topology (leaves pointing to hub)
        g = LightweightDiGraph()
        g.add_node("Hub")
        for i in range(10):
            g.add_edge(f"Leaf_{i}", "Hub")

        pr = g.pagerank(alpha=0.85)
        self.assertEqual(len(pr), 11)
        self.assertAlmostEqual(sum(pr.values()), 1.0, places=5)
        self.assertGreater(pr["Hub"], pr["Leaf_0"])
        for i in range(10):
            self.assertAlmostEqual(pr[f"Leaf_{i}"], pr["Leaf_0"], places=5)


# ===========================================================================
# 2. KnowledgeGraph Unit Tests
# ===========================================================================

class TestKnowledgeGraph(unittest.TestCase):
    """Test suite for the cross-stack KnowledgeGraph builder."""

    def _build_test_symbols(self):
        u_sym1 = Symbol(
            id="unity:Assets/Scripts/VoiceQuest.cs:VoiceQuest",
            name="VoiceQuest",
            kind="class",
            file_path="Assets/Scripts/VoiceQuest.cs",
            line_start=10,
            line_end=80,
            docstring="Manages voice quests in Unity.",
            signature="public class VoiceQuest : MonoBehaviour",
            language="csharp",
            dependencies=["MonoBehaviour"],
            cross_stack_refs=[
                ContractReference(type="livekit_event", name="SET_ACTIVE_QUEST", line=30, direction="publisher"),
                ContractReference(type="livekit_event", name="QUEST_MATCHED", line=50, direction="subscriber"),
                ContractReference(type="rtdb_path", name="live_sessions/commands", line=60, direction="writer"),
            ],
        )
        u_sym2 = Symbol(
            id="unity:Assets/Scripts/VoiceQuest.cs:VoiceQuest.Execute",
            name="Execute",
            kind="method",
            file_path="Assets/Scripts/VoiceQuest.cs",
            line_start=25,
            line_end=40,
            parent_id="unity:Assets/Scripts/VoiceQuest.cs:VoiceQuest",
            language="csharp",
            dependencies=["SendActiveQuest"],
        )

        p_sym1 = Symbol(
            id="python:LiveKitAgent/src/agent.py:TeacherAgent",
            name="TeacherAgent",
            kind="class",
            file_path="LiveKitAgent/src/agent.py",
            line_start=1,
            line_end=120,
            docstring="LiveKit voice assistant for autism therapy.",
            signature="class TeacherAgent(AgentSession)",
            language="python",
            dependencies=["AgentSession"],
            cross_stack_refs=[
                ContractReference(type="livekit_event", name="SET_ACTIVE_QUEST", line=40, direction="subscriber"),
                ContractReference(type="livekit_event", name="QUEST_MATCHED", line=90, direction="publisher"),
            ],
        )
        p_sym2 = Symbol(
            id="python:LiveKitAgent/src/agent.py:complete_quest",
            name="complete_quest",
            kind="tool",
            file_path="LiveKitAgent/src/agent.py",
            line_start=70,
            line_end=95,
            parent_id="python:LiveKitAgent/src/agent.py:TeacherAgent",
            language="python",
            cross_stack_refs=[
                ContractReference(type="livekit_event", name="QUEST_MATCHED", line=85, direction="publisher"),
            ],
        )

        w_sym1 = Symbol(
            id="web:src/hooks/useLiveKitDataChannel.ts:useLiveKitDataChannel",
            name="useLiveKitDataChannel",
            kind="hook",
            file_path="src/hooks/useLiveKitDataChannel.ts",
            line_start=1,
            line_end=60,
            docstring="Web hook for DataChannel communication.",
            signature="export function useLiveKitDataChannel()",
            language="typescript",
            cross_stack_refs=[
                ContractReference(type="livekit_event", name="SET_ACTIVE_QUEST", line=20, direction="subscriber"),
                ContractReference(type="api_route", name="/api/livekit-token", line=35, direction="caller"),
                ContractReference(type="rtdb_path", name="live_sessions/commands", line=45, direction="reader"),
            ],
        )
        w_sym2 = Symbol(
            id="web:src/app/api/livekit-token/route.ts:GET",
            name="GET",
            kind="api_route",
            file_path="src/app/api/livekit-token/route.ts",
            line_start=1,
            line_end=25,
            signature="export async function GET(req: Request)",
            language="typescript",
            cross_stack_refs=[
                ContractReference(type="api_route", name="/api/livekit-token", line=5, direction="handler"),
            ],
        )

        return {
            "unity": [u_sym1, u_sym2],
            "python": [p_sym1, p_sym2],
            "web": [w_sym1, w_sym2],
        }

    def test_build_from_symbols_and_node_counts(self):
        kg = KnowledgeGraph()
        symbols = self._build_test_symbols()
        kg.build_from_symbols(symbols)

        d = kg.to_dict()
        meta = d["metadata"]
        subsystems = meta["subsystems"]

        self.assertEqual(subsystems.get("unity"), 2)
        self.assertEqual(subsystems.get("python"), 2)
        self.assertEqual(subsystems.get("web"), 2)
        self.assertGreaterEqual(subsystems.get("contract"), 3)

    def test_intra_language_containment_and_inheritance(self):
        kg = KnowledgeGraph()
        symbols = self._build_test_symbols()
        kg.build_from_symbols(symbols)

        # VoiceQuest CONTAINS Execute
        self.assertTrue(
            kg.graph.has_edge(
                "unity:Assets/Scripts/VoiceQuest.cs:VoiceQuest",
                "unity:Assets/Scripts/VoiceQuest.cs:VoiceQuest.Execute",
            )
        )
        edge_data = kg.graph.edges[(
            "unity:Assets/Scripts/VoiceQuest.cs:VoiceQuest",
            "unity:Assets/Scripts/VoiceQuest.cs:VoiceQuest.Execute",
        )]
        self.assertEqual(edge_data["kind"], "CONTAINS")

    def test_cross_stack_contract_bridges_livekit_rtdb_api(self):
        kg = KnowledgeGraph()
        symbols = self._build_test_symbols()
        kg.build_from_symbols(symbols)

        # Check LiveKit event bridge node
        lk_node_id = "contract:livekit_event:SET_ACTIVE_QUEST"
        self.assertTrue(kg.graph.has_node(lk_node_id))
        self.assertEqual(kg.graph.nodes[lk_node_id]["subsystem"], "contract")
        self.assertEqual(kg.graph.nodes[lk_node_id]["kind"], "livekit_event")

        # VoiceQuest publishes SET_ACTIVE_QUEST
        self.assertTrue(
            kg.graph.has_edge("unity:Assets/Scripts/VoiceQuest.cs:VoiceQuest", lk_node_id)
        )

        # TeacherAgent subscribes to SET_ACTIVE_QUEST
        self.assertTrue(
            kg.graph.has_edge("python:LiveKitAgent/src/agent.py:TeacherAgent", lk_node_id)
        )

        # RTDB Bridge
        rtdb_node_id = "contract:rtdb_path:live_sessions/commands"
        self.assertTrue(kg.graph.has_node(rtdb_node_id))
        self.assertTrue(
            kg.graph.has_edge("unity:Assets/Scripts/VoiceQuest.cs:VoiceQuest", rtdb_node_id)
        )
        self.assertTrue(
            kg.graph.has_edge("web:src/hooks/useLiveKitDataChannel.ts:useLiveKitDataChannel", rtdb_node_id)
        )

        # REST API Bridge
        api_node_id = "contract:api_route:/api/livekit-token"
        self.assertTrue(kg.graph.has_node(api_node_id))
        self.assertTrue(
            kg.graph.has_edge("web:src/hooks/useLiveKitDataChannel.ts:useLiveKitDataChannel", api_node_id)
        )
        self.assertTrue(
            kg.graph.has_edge("web:src/app/api/livekit-token/route.ts:GET", api_node_id)
        )

    def test_pagerank_computation_on_knowledge_graph(self):
        kg = KnowledgeGraph()
        symbols = self._build_test_symbols()
        kg.build_from_symbols(symbols)

        pr = kg.compute_pagerank(alpha=0.85)
        self.assertAlmostEqual(sum(pr.values()), 1.0, places=4)

        # Contract hub SET_ACTIVE_QUEST should rank higher than leaf method Execute
        set_active_id = "contract:livekit_event:SET_ACTIVE_QUEST"
        execute_id = "unity:Assets/Scripts/VoiceQuest.cs:VoiceQuest.Execute"
        self.assertGreater(pr[set_active_id], pr[execute_id])

        # Verify stored attributes
        self.assertEqual(kg.graph.nodes[set_active_id]["pagerank"], pr[set_active_id])

    def test_find_impact_radius_unity_symbol(self):
        kg = KnowledgeGraph()
        symbols = self._build_test_symbols()
        kg.build_from_symbols(symbols)
        kg.compute_pagerank()

        impact = kg.find_impact_radius("VoiceQuest", max_depth=3)
        self.assertGreaterEqual(impact["total_affected"], 4)

        affected_names = {n["name"] for n in impact["affected_nodes"]}
        self.assertIn("VoiceQuest", affected_names)
        self.assertIn("SET_ACTIVE_QUEST", affected_names)
        self.assertIn("TeacherAgent", affected_names)
        self.assertIn("useLiveKitDataChannel", affected_names)

        subsystems = impact["by_subsystem"]
        self.assertTrue(len(subsystems["unity"]) >= 1)
        self.assertTrue(len(subsystems["python"]) >= 1)
        self.assertTrue(len(subsystems["web"]) >= 1)
        self.assertTrue(len(subsystems["contract"]) >= 1)

    def test_find_impact_radius_contract_event(self):
        kg = KnowledgeGraph()
        symbols = self._build_test_symbols()
        kg.build_from_symbols(symbols)

        impact = kg.find_impact_radius("SET_ACTIVE_QUEST", max_depth=2)
        affected_names = {n["name"] for n in impact["affected_nodes"]}

        self.assertIn("SET_ACTIVE_QUEST", affected_names)
        self.assertIn("VoiceQuest", affected_names)
        self.assertIn("TeacherAgent", affected_names)
        self.assertIn("useLiveKitDataChannel", affected_names)

    def test_find_impact_radius_nonexistent_symbol(self):
        kg = KnowledgeGraph()
        impact = kg.find_impact_radius("NonExistent_FooBar_123")
        self.assertEqual(impact["total_affected"], 0)
        self.assertEqual(impact["affected_nodes"], [])

    def test_get_subgraph_context_token_budget_enforcement(self):
        kg = KnowledgeGraph()
        symbols = self._build_test_symbols()
        kg.build_from_symbols(symbols)

        ctx_generous = kg.get_subgraph_context("VoiceQuest", token_budget=2000)
        self.assertIn("# JIT Context: `VoiceQuest`", ctx_generous)
        self.assertIn("VoiceQuest", ctx_generous)
        self.assertIn("SET_ACTIVE_QUEST", ctx_generous)

        # Restrictive budget
        ctx_strict = kg.get_subgraph_context("VoiceQuest", token_budget=60)
        self.assertIn("# JIT Context", ctx_strict)
        self.assertLessEqual(len(ctx_strict.split()) * 1.33, 100)

    def test_json_roundtrip_serialization(self):
        kg = KnowledgeGraph()
        symbols = self._build_test_symbols()
        kg.build_from_symbols(symbols)
        kg.compute_pagerank()

        # to_dict
        d = kg.to_dict()
        self.assertIn("metadata", d)
        self.assertIn("nodes", d)
        self.assertIn("edges", d)
        self.assertIn("rankings", d)

        # from_dict
        kg_reconstructed = KnowledgeGraph.from_dict(d)
        d_reconstructed = kg_reconstructed.to_dict()

        self.assertEqual(len(kg.graph.nodes), len(kg_reconstructed.graph.nodes))
        self.assertEqual(len(kg.graph.edges), len(kg_reconstructed.graph.edges))
        self.assertEqual(d["metadata"]["total_nodes"], d_reconstructed["metadata"]["total_nodes"])

        # File save/load
        with tempfile.NamedTemporaryFile("w", suffix=".json", delete=False) as tf:
            temp_file = tf.name

        try:
            kg.save_to_json(temp_file)
            kg_loaded = KnowledgeGraph.load_from_json(temp_file)
            self.assertEqual(len(kg.graph.nodes), len(kg_loaded.graph.nodes))
        finally:
            if os.path.exists(temp_file):
                os.remove(temp_file)

    def test_get_contract_bridges_summary(self):
        kg = KnowledgeGraph()
        symbols = self._build_test_symbols()
        kg.build_from_symbols(symbols)

        bridges = kg.get_contract_bridges()
        self.assertGreaterEqual(len(bridges), 3)

        bridge_names = {b["name"] for b in bridges}
        self.assertIn("SET_ACTIVE_QUEST", bridge_names)
        self.assertIn("QUEST_MATCHED", bridge_names)
        self.assertIn("live_sessions/commands", bridge_names)

        set_active_bridge = next(b for b in bridges if b["name"] == "SET_ACTIVE_QUEST")
        self.assertGreaterEqual(set_active_bridge["total_connections"], 2)


if __name__ == "__main__":
    unittest.main(verbosity=2)
