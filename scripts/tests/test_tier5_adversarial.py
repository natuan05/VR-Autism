#!/usr/bin/env python3
"""
Tier 5 Adversarial Coverage Hardening Test Suite for VR-Autism CKG,
RepoMap Generator, and JIT Context Retriever.

Exercises extreme edge cases, stress scenarios, malformed inputs,
extreme token budgets, circular topologies, and CLI resilience.
"""

import ast
import json
import math
import os
import re
import subprocess
import sys
import tempfile
import time
import unittest
from pathlib import Path
from typing import Any, Dict, List, Optional, Set, Tuple

# Ensure repository root is on sys.path
REPO_ROOT = Path(__file__).resolve().parent.parent.parent
if str(REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(REPO_ROOT))

from scripts.ast_parsers import (
    ASTParserManager,
    CSharpASTParser,
    ContractReference,
    PythonASTParser,
    Symbol,
    TypeScriptASTParser,
    is_ignored_path,
    normalize_path,
)
from scripts.graph_builder import (
    KnowledgeGraph,
    LightweightDiGraph,
    build_knowledge_graph,
    get_impact_analysis,
    get_jit_context,
)
from scripts.repomap_generator import (
    estimate_tokens,
    generate_repomap,
    generate_repomap_markdown,
    load_configuration,
)


# ===========================================================================
# 1. Extreme Token Budgets Challenge for RepoMap Generator
# ===========================================================================

class TestTier5RepoMapExtremeBudgets(unittest.TestCase):
    """
    Stress-tests RepoMap generator across boundary and extreme token budgets:
    negative, 0, 10, 50, 100, 500, 1000, 3000, 10000, 50000, 1000000.
    """

    def setUp(self):
        self.kg = KnowledgeGraph()
        # Build a rich synthetic graph with 100 symbols across 3 subsystems + contracts
        for i in range(40):
            self.kg.add_node(
                f"unity:QuestComponent_{i}",
                name=f"QuestComponent_{i}",
                subsystem="unity",
                kind="class",
                file_path=f"Assets/Project/Scripts/Gameplay/QuestComponent_{i}.cs",
                line_start=1,
                line_end=100,
                docstring=f"Unity quest component {i} handling VR interaction and state.",
            )
        for i in range(30):
            self.kg.add_node(
                f"python:VoiceHandler_{i}",
                name=f"VoiceHandler_{i}",
                subsystem="python",
                kind="class",
                file_path=f"LiveKitAgent/src/handlers/voice_{i}.py",
                line_start=1,
                line_end=80,
                docstring=f"Python LiveKit voice handler {i} for autism behavioral coaching.",
            )
        for i in range(30):
            self.kg.add_node(
                f"web:DashboardHook_{i}",
                name=f"useDashboardHook_{i}",
                subsystem="web",
                kind="hook",
                file_path=f"src/hooks/useDashboard_{i}.ts",
                line_start=1,
                line_end=60,
                docstring=f"Web dashboard react hook {i} connecting to telemetry streams.",
            )

        # Contracts
        for event in ["SET_ACTIVE_QUEST", "QUEST_MATCHED", "VERBAL_HINT", "ON_REMINDER"]:
            cid = f"contract:livekit_event:{event}"
            self.kg.add_node(cid, name=event, subsystem="contract", kind="livekit_event")
            # Connect publishers and subscribers
            self.kg.add_edge("unity:QuestComponent_0", cid, kind="PUBLISHES")
            self.kg.add_edge("python:VoiceHandler_0", cid, kind="SUBSCRIBES")
            self.kg.add_edge("web:DashboardHook_0", cid, kind="SUBSCRIBES")

        self.kg.compute_pagerank()

    def test_repomap_budget_zero_and_negative(self):
        """Test RepoMap generator behavior when budget is <= 0."""
        for b in [0, -1, -500]:
            md_text, json_data = generate_repomap(self.kg, config={"token_budget": b})
            self.assertIsInstance(md_text, str)
            self.assertIn("# VR-Autism Repository Map", md_text)
            self.assertIsInstance(json_data, dict)

    def test_repomap_budget_micro_100_tokens(self):
        """Test RepoMap under ultra-tight 100 tokens budget."""
        md_text, json_data = generate_repomap(self.kg, config={"token_budget": 100})
        tok_est = estimate_tokens(md_text)
        self.assertLessEqual(tok_est, 140, f"Exceeded 100 token budget significantly: {tok_est}")
        self.assertIn("# VR-Autism", md_text)
        self.assertIsInstance(json_data, dict)

    def test_repomap_budget_500_tokens(self):
        """Test RepoMap under strict 500 tokens budget."""
        md_text, json_data = generate_repomap(self.kg, config={"token_budget": 500})
        tok_est = estimate_tokens(md_text)
        self.assertLessEqual(tok_est, 650, f"Exceeded 500 token budget: {tok_est}")
        self.assertIn("Cross-Stack", md_text)

    def test_repomap_budget_10000_tokens_and_huge(self):
        """Test RepoMap under generous 10,000 and 1,000,000 token budgets."""
        for budget in [10000, 50000, 1000000]:
            md_text, json_data = generate_repomap(self.kg, config={"token_budget": budget})
            tok_est = estimate_tokens(md_text)
            self.assertLessEqual(tok_est, budget * 1.5)
            # Ensure all subsystems appear in generous budget
            self.assertIn("Unity C# Core Subsystem", md_text)
            self.assertIn("Python Voice Agent Subsystem", md_text)
            self.assertIn("Next.js Web Dashboard Subsystem", md_text)

    def test_repomap_massive_500_symbols_graph(self):
        """Generate a massive 500+ symbol graph and test compression under 500 vs 10000 budget."""
        large_kg = KnowledgeGraph()
        for i in range(500):
            large_kg.add_node(
                f"unity:Symbol_{i}",
                name=f"Symbol_{i}",
                subsystem="unity",
                kind="class",
                file_path=f"Assets/Scripts/File_{i}.cs",
                line_start=1,
                line_end=50,
                docstring=f"Documentation for symbol {i} with extensive detailed descriptions.",
            )
        large_kg.compute_pagerank()

        # Strict budget
        md_small, _ = generate_repomap(large_kg, config={"token_budget": 300})
        tok_small = estimate_tokens(md_small)
        self.assertLessEqual(tok_small, 400)

        # Large budget
        md_large, _ = generate_repomap(large_kg, config={"token_budget": 10000})
        tok_large = estimate_tokens(md_large)
        self.assertGreater(tok_large, tok_small)


# ===========================================================================
# 2. JIT Context Retriever Adversarial Query Stress Tests
# ===========================================================================

class TestTier5JITContextAdversarialQueries(unittest.TestCase):
    """
    Stress-tests JIT Context retrieval against special characters, nonexistent symbols,
    Vietnamese Unicode, injection-like payloads, and ambiguous queries.
    """

    def setUp(self):
        self.kg = KnowledgeGraph()
        self.kg.add_node(
            "unity:Assets/Scripts/VoiceQuest.cs:VoiceQuest",
            name="VoiceQuest",
            subsystem="unity",
            kind="class",
            file_path="Assets/Scripts/VoiceQuest.cs",
            line_start=10,
            line_end=100,
            docstring="Main VR VoiceQuest controller for autism therapy.",
            signature="public class VoiceQuest : MonoBehaviour",
        )
        self.kg.add_node(
            "python:LiveKitAgent/src/agent.py:TeacherAgent",
            name="TeacherAgent",
            subsystem="python",
            kind="class",
            file_path="LiveKitAgent/src/agent.py",
            line_start=1,
            line_end=150,
            docstring="Teacher voice assistant using Gemini LiveKit agent.",
            signature="class TeacherAgent(AgentSession)",
        )
        self.kg.add_node(
            "contract:livekit_event:SET_ACTIVE_QUEST",
            name="SET_ACTIVE_QUEST",
            subsystem="contract",
            kind="livekit_event",
            docstring="LiveKit DataPacket Event for quest activation",
        )
        self.kg.add_edge("unity:Assets/Scripts/VoiceQuest.cs:VoiceQuest", "contract:livekit_event:SET_ACTIVE_QUEST", kind="PUBLISHES")
        self.kg.add_edge("python:LiveKitAgent/src/agent.py:TeacherAgent", "contract:livekit_event:SET_ACTIVE_QUEST", kind="SUBSCRIBES")
        self.kg.compute_pagerank()

    def test_jit_special_characters_queries(self):
        """Test queries with special characters and punctuation."""
        special_queries = [
            "VoiceQuest()",
            "<VoiceQuest>",
            "VoiceQuest::Execute",
            "TeacherAgent.on_enter()",
            "!@#$%^&*()_+{}|:\"<>?",
            "'; DROP TABLE symbols; --",
            "../../../etc/passwd",
            "<script>alert('xss')</script>",
            "   \t\n   ",
            "",
        ]
        for query in special_queries:
            ctx = get_jit_context(self.kg, query=query, token_budget=1500)
            self.assertIsInstance(ctx, str)
            self.assertIn("# JIT Context:", ctx)

    def test_jit_nonexistent_symbols(self):
        """Test queries for nonexistent symbols return clean fallback without crash."""
        nonexistent = [
            "NonExistentSymbol_99999",
            "UnrealEngineActor",
            "Random_Gibberish_XYZ_123",
            "__non_existing_method__",
        ]
        for query in nonexistent:
            ctx = get_jit_context(self.kg, query=query, token_budget=1500)
            self.assertIsInstance(ctx, str)
            self.assertIn("No matching symbols or contract bridges found", ctx)

    def test_jit_vietnamese_unicode_and_emojis(self):
        """Test queries containing Vietnamese accents and emojis."""
        self.kg.add_node(
            "unity:NhiemVuGiaoTiep",
            name="NhiemVuGiaoTiep",
            subsystem="unity",
            kind="class",
            docstring="Nhiệm vụ giao tiếp ngôn ngữ cho trẻ tự kỷ 🌟.",
        )
        unicode_queries = [
            "Nhiệm vụ",
            "trẻ tự kỷ",
            "giao tiếp",
            "🌟",
            "Xin chào các bạn 🇻🇳",
        ]
        for q in unicode_queries:
            ctx = get_jit_context(self.kg, query=q, token_budget=1500)
            self.assertIsInstance(ctx, str)

    def test_jit_ambiguous_common_terms(self):
        """Test ambiguous single-word queries like 'quest', 'agent', 'session'."""
        for term in ["quest", "agent", "set", "voice"]:
            ctx = get_jit_context(self.kg, query=term, token_budget=1500)
            self.assertIsInstance(ctx, str)
            self.assertIn("# JIT Context:", ctx)

    def test_jit_massive_query_string(self):
        """Test query string of 5,000 characters."""
        long_query = "VoiceQuest_" + "A" * 5000
        ctx = get_jit_context(self.kg, query=long_query, token_budget=1500)
        self.assertIsInstance(ctx, str)


# ===========================================================================
# 3. Blast Radius Analysis (`--impact`) Adversarial Graph Topologies
# ===========================================================================

class TestTier5BlastRadiusAdversarialTopologies(unittest.TestCase):
    """
    Stress-tests `--impact` blast radius analysis on circular dependencies,
    deep linear chains, disconnected components, isolated nodes, and contract hubs.
    """

    def test_impact_circular_dependency_cycle(self):
        """Test circular dependency A -> B -> C -> A doesn't cause infinite recursion."""
        kg = KnowledgeGraph()
        kg.add_node("A", name="NodeA", subsystem="unity", kind="class")
        kg.add_node("B", name="NodeB", subsystem="python", kind="class")
        kg.add_node("C", name="NodeC", subsystem="web", kind="class")

        kg.add_edge("A", "B", kind="CALLS")
        kg.add_edge("B", "C", kind="CALLS")
        kg.add_edge("C", "A", kind="CALLS")
        kg.compute_pagerank()

        # Query from A
        impact_a = get_impact_analysis(kg, symbol="NodeA", max_depth=10)
        self.assertEqual(impact_a["total_affected"], 3)
        affected_names = {n["name"] for n in impact_a["affected_nodes"]}
        self.assertEqual(affected_names, {"NodeA", "NodeB", "NodeC"})

    def test_impact_deep_circular_chain_20_nodes(self):
        """Test 20-node circular ring topology under high max_depth."""
        kg = KnowledgeGraph()
        n = 20
        for i in range(n):
            kg.add_node(f"Node_{i}", name=f"Node_{i}", subsystem="unity", kind="class")
        for i in range(n):
            kg.add_edge(f"Node_{i}", f"Node_{(i + 1) % n}", kind="CALLS")
        kg.compute_pagerank()

        impact = get_impact_analysis(kg, symbol="Node_0", max_depth=30)
        self.assertEqual(impact["total_affected"], n)

    def test_impact_isolated_node(self):
        """Test querying an isolated node with 0 in-degree and 0 out-degree."""
        kg = KnowledgeGraph()
        kg.add_node("Island", name="IslandClass", subsystem="unity", kind="class")
        kg.add_node("Other", name="OtherClass", subsystem="python", kind="class")
        kg.compute_pagerank()

        impact = get_impact_analysis(kg, symbol="IslandClass", max_depth=5)
        self.assertEqual(impact["total_affected"], 1)
        self.assertEqual(impact["affected_nodes"][0]["name"], "IslandClass")
        self.assertEqual(impact["affected_nodes"][0]["distance"], 0)

    def test_impact_contract_root_hub(self):
        """Test querying directly on contract root event node connected to 50 nodes."""
        kg = KnowledgeGraph()
        event_id = "contract:livekit_event:SET_ACTIVE_QUEST"
        kg.add_node(event_id, name="SET_ACTIVE_QUEST", subsystem="contract", kind="livekit_event")

        for i in range(25):
            u_id = f"unity:Client_{i}"
            kg.add_node(u_id, name=f"Client_{i}", subsystem="unity", kind="class")
            kg.add_edge(u_id, event_id, kind="PUBLISHES")

        for i in range(25):
            p_id = f"python:Worker_{i}"
            kg.add_node(p_id, name=f"Worker_{i}", subsystem="python", kind="class")
            kg.add_edge(p_id, event_id, kind="SUBSCRIBES")

        kg.compute_pagerank()

        impact = get_impact_analysis(kg, symbol="SET_ACTIVE_QUEST", max_depth=2)
        self.assertEqual(impact["total_affected"], 51)
        self.assertEqual(len(impact["by_subsystem"]["unity"]), 25)
        self.assertEqual(len(impact["by_subsystem"]["python"]), 25)
        self.assertEqual(len(impact["by_subsystem"]["contract"]), 1)

    def test_impact_depth_boundaries(self):
        """Test impact analysis at depth=0, depth=1, depth=5."""
        kg = KnowledgeGraph()
        for i in range(6):
            kg.add_node(f"N{i}", name=f"N{i}", subsystem="unity", kind="class")
        for i in range(5):
            kg.add_edge(f"N{i}", f"N{i+1}", kind="CALLS")
        kg.compute_pagerank()

        # Depth 0: only root
        impact_d0 = get_impact_analysis(kg, symbol="N0", max_depth=0)
        self.assertEqual(impact_d0["total_affected"], 1)

        # Depth 1: N0 and N1
        impact_d1 = get_impact_analysis(kg, symbol="N0", max_depth=1)
        self.assertEqual(impact_d1["total_affected"], 2)

        # Depth 5: All 6 nodes
        impact_d5 = get_impact_analysis(kg, symbol="N0", max_depth=5)
        self.assertEqual(impact_d5["total_affected"], 6)


# ===========================================================================
# 4. CLI Edge Cases and Robustness
# ===========================================================================

class TestTier5CLIEdgeCases(unittest.TestCase):
    """
    Stress-tests command-line interfaces (`jit_context.py` and `repomap_generator.py`)
    against missing files, corrupt configs, non-ASCII arguments, and invalid flags.
    """

    def test_repomap_generator_cli_missing_config(self):
        """Test repomap_generator.py with non-existent config path falls back cleanly."""
        cli = REPO_ROOT / "scripts" / "repomap_generator.py"
        res = subprocess.run(
            [sys.executable, str(cli), "--config", "non_existent_config_12345.json", "--budget", "1000"],
            capture_output=True,
            text=True,
        )
        self.assertEqual(res.returncode, 0)
        self.assertIn("Generated", res.stdout)

    def test_repomap_generator_cli_malformed_json_config(self):
        """Test repomap_generator.py with corrupt JSON config file."""
        with tempfile.NamedTemporaryFile("w", suffix=".json", delete=False) as tf:
            tf.write("{ this is not valid json! ::: }")
            corrupt_path = tf.name

        try:
            cli = REPO_ROOT / "scripts" / "repomap_generator.py"
            res = subprocess.run(
                [sys.executable, str(cli), "--config", corrupt_path, "--budget", "1000"],
                capture_output=True,
                text=True,
            )
            self.assertEqual(res.returncode, 0)
        finally:
            if os.path.exists(corrupt_path):
                os.remove(corrupt_path)

    def test_jit_context_cli_non_ascii_and_vietnamese_query(self):
        """Test jit_context.py with Vietnamese characters and emoji in CLI query."""
        cli = REPO_ROOT / "scripts" / "jit_context.py"
        res = subprocess.run(
            [sys.executable, str(cli), "--query", "Nhiệm vụ VoiceQuest 🚀", "--budget", "1200"],
            capture_output=True,
            text=True,
            encoding="utf-8",
        )
        self.assertEqual(res.returncode, 0)
        self.assertIn("JIT Context", res.stdout)

    def test_jit_context_cli_impact_json_format(self):
        """Test jit_context.py --impact with --format json produces parseable JSON."""
        cli = REPO_ROOT / "scripts" / "jit_context.py"
        res = subprocess.run(
            [sys.executable, str(cli), "--impact", "VoiceQuest", "--format", "json"],
            capture_output=True,
            text=True,
            encoding="utf-8",
        )
        self.assertEqual(res.returncode, 0)
        data = json.loads(res.stdout)
        self.assertIn("root", data)
        self.assertIn("total_affected", data)
        self.assertIn("affected_nodes", data)

    def test_jit_context_cli_missing_source_dirs(self):
        """Test jit_context.py when pointing to non-existent unity/python/web directories."""
        cli = REPO_ROOT / "scripts" / "jit_context.py"
        res = subprocess.run(
            [
                sys.executable,
                str(cli),
                "--unity-dir", "fake/unity/dir",
                "--python-dir", "fake/python/dir",
                "--web-dir", "fake/web/dir",
                "--query", "TestSymbol"
            ],
            capture_output=True,
            text=True,
        )
        self.assertEqual(res.returncode, 0)
        self.assertIn("JIT Context", res.stdout)


# ===========================================================================
# Test Runner
# ===========================================================================

def run_tier5_suite() -> unittest.TestResult:
    suite = unittest.TestSuite()
    loader = unittest.TestLoader()

    suite.addTests(loader.loadTestsFromTestCase(TestTier5RepoMapExtremeBudgets))
    suite.addTests(loader.loadTestsFromTestCase(TestTier5JITContextAdversarialQueries))
    suite.addTests(loader.loadTestsFromTestCase(TestTier5BlastRadiusAdversarialTopologies))
    suite.addTests(loader.loadTestsFromTestCase(TestTier5CLIEdgeCases))

    runner = unittest.TextTestRunner(verbosity=2)
    return runner.run(suite)


if __name__ == "__main__":
    result = run_tier5_suite()
    sys.exit(0 if result.wasSuccessful() else 1)
