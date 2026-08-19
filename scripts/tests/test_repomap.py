#!/usr/bin/env python3
"""
Comprehensive 4-Tier E2E Test Suite for VR-Autism Code Knowledge Graph (CKG),
RepoMap Generator, and JIT Context Retriever.

Usage:
    python scripts/tests/test_repomap.py
    python -m unittest scripts/tests/test_repomap.py
    python scripts/tests/test_repomap.py --tier 1
    python scripts/tests/test_repomap.py --tier 2
    python scripts/tests/test_repomap.py --tier 3
    python scripts/tests/test_repomap.py --tier 4
"""

import ast
import json
import math
import os
import re
import shutil
import sys
import tempfile
import unittest
from pathlib import Path
from typing import Any, Dict, List, Optional, Set, Tuple

# Ensure repository root is on sys.path
REPO_ROOT = Path(__file__).resolve().parent.parent.parent
if str(REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(REPO_ROOT))

# Import AST Parsers (Milestone M1)
try:
    from scripts.ast_parsers import (
        ASTParserManager,
        ContractReference,
        CSharpASTParser,
        DEFAULT_IGNORED_DIRS,
        DEFAULT_IGNORED_EXTS,
        LIVEKIT_EVENTS,
        PythonASTParser,
        REST_API_ROUTES,
        RTDB_PATH_PATTERNS,
        Symbol,
        TypeScriptASTParser,
        is_ignored_path,
        normalize_path,
    )
    HAVE_AST_PARSERS = True
except ImportError as e:
    HAVE_AST_PARSERS = False
    AST_PARSER_IMPORT_ERROR = str(e)

# Import Graph Builder (Milestone M2)
try:
    from scripts.graph_builder import KnowledgeGraph
    HAVE_GRAPH_BUILDER = True
except ImportError:
    HAVE_GRAPH_BUILDER = False

# Import RepoMap Generator (Milestone M3)
try:
    from scripts.repomap_generator import generate_repomap
    HAVE_REPOMAP_GENERATOR = True
except ImportError:
    HAVE_REPOMAP_GENERATOR = False

# Import JIT Context (Milestone M4)
try:
    from scripts.jit_context import get_impact_analysis, get_jit_context
    HAVE_JIT_CONTEXT = True
except ImportError:
    HAVE_JIT_CONTEXT = False


# ===========================================================================
# Tier 1: Functional Feature Coverage Tests (R1 - R5)
# ===========================================================================

class TestR1ASTParsersTier1(unittest.TestCase):
    """
    Tier 1 tests for Requirement R1: Cross-Stack AST Parsing & Symbol Indexing.
    Exercises >=5 comprehensive test cases covering Python, C#, TypeScript, ignore rules, and contracts.
    """

    def setUp(self):
        if not HAVE_AST_PARSERS:
            self.skipTest(f"ast_parsers module not available: {AST_PARSER_IMPORT_ERROR}")
        self.py_parser = PythonASTParser()
        self.cs_parser = CSharpASTParser()
        self.ts_parser = TypeScriptASTParser()
        self.manager = ASTParserManager()

    def test_r1_1_python_ast_parsing_classes_functions_and_tools(self):
        """T1.1.1: Verify Python AST parsing of classes, methods, async functions, docstrings, and tools."""
        py_code = '''
class QuestState:
    """Tracks active quest state."""
    def __init__(self, active: bool = False):
        self.active = active

    def set_active_quest(self, name: str, phrases: list[str]) -> None:
        """Sets the current active quest."""
        self.active = True

class TeacherAgent:
    """LiveKit Teacher Voice Agent."""
    async def on_enter(self) -> None:
        pass

@llm.function_tool
async def complete_quest(runtime: Any) -> str:
    """Mark quest as complete."""
    return "completed"

async def entrypoint(ctx: Any) -> None:
    """Main voice worker entrypoint."""
    pass
'''
        symbols = self.py_parser.parse_source(py_code, "LiveKitAgent/src/agent.py")
        names = {s.name: s for s in symbols}

        self.assertIn("QuestState", names)
        self.assertEqual(names["QuestState"].kind, "class")
        self.assertEqual(names["QuestState"].docstring, "Tracks active quest state.")

        self.assertIn("set_active_quest", names)
        self.assertEqual(names["set_active_quest"].kind, "method")

        self.assertIn("TeacherAgent", names)
        self.assertEqual(names["TeacherAgent"].kind, "class")

        self.assertIn("complete_quest", names)
        self.assertIn(names["complete_quest"].kind, ["tool", "async_function", "function"])

        self.assertIn("entrypoint", names)
        self.assertEqual(names["entrypoint"].kind, "async_function")

    def test_r1_2_csharp_ast_parsing_classes_methods_inheritance_and_properties(self):
        """T1.1.2: Verify C# AST parsing of classes, structs, interfaces, inheritance, properties, and methods."""
        cs_code = '''
namespace VRAutism.Gameplay
{
    public interface IQuestAction
    {
        void Execute();
    }

    public class VoiceQuest : MonoBehaviour, IQuestAction
    {
        [SerializeField] private string questName;
        public string QuestName { get; set; }

        public void Execute()
        {
            LiveKitService.Instance.SendActiveQuest(questName, new string[] { "hello" });
        }

        private void HandleSpeechMatched()
        {
            Debug.Log("Quest matched!");
        }
    }
}
'''
        symbols = self.cs_parser.parse_source(cs_code, "Assets/Project/Scripts/Gameplay/Actions/Models/VoiceQuest.cs")
        names = {s.name: s for s in symbols}

        self.assertIn("IQuestAction", names)
        self.assertEqual(names["IQuestAction"].kind, "interface")

        self.assertIn("VoiceQuest", names)
        self.assertEqual(names["VoiceQuest"].kind, "class")
        self.assertIn("MonoBehaviour", names["VoiceQuest"].dependencies)
        self.assertIn("IQuestAction", names["VoiceQuest"].dependencies)

        self.assertIn("Execute", names)
        self.assertEqual(names["Execute"].kind, "method")

        self.assertIn("HandleSpeechMatched", names)
        self.assertEqual(names["HandleSpeechMatched"].kind, "method")

    def test_r1_3_typescript_ast_parsing_interfaces_server_actions_and_routes(self):
        """T1.1.3: Verify TypeScript/TSX parsing of interfaces, types, server actions, hooks, and API routes."""
        action_ts_code = '''
"use server";

export interface SessionConfig {
    sessionId: string;
    childId: string;
    token?: string;
}

export type ConnectionStatus = "connected" | "disconnected" | "connecting";

export async function createSession(config: SessionConfig): Promise<boolean> {
    return true;
}
'''
        hook_ts_code = '''
export function useLiveKitDataChannel(room: any) {
    const sendHint = () => {
        room.publishData("VERBAL_HINT");
    };
    return { sendHint };
}
'''
        route_ts_code = '''
export async function GET(request: Request) {
    return new Response(JSON.stringify({ token: "abc" }));
}
'''
        action_symbols = self.ts_parser.parse_source(action_ts_code, "src/actions/session.ts")
        hook_symbols = self.ts_parser.parse_source(hook_ts_code, "src/hooks/useLiveKitDataChannel.ts")
        route_symbols = self.ts_parser.parse_source(route_ts_code, "src/app/api/livekit-token/route.ts")

        action_names = {s.name: s for s in action_symbols}
        hook_names = {s.name: s for s in hook_symbols}
        route_names = {s.name: s for s in route_symbols}

        self.assertIn("SessionConfig", action_names)
        self.assertEqual(action_names["SessionConfig"].kind, "interface")

        self.assertIn("ConnectionStatus", action_names)
        self.assertEqual(action_names["ConnectionStatus"].kind, "type")

        self.assertIn("createSession", action_names)
        self.assertEqual(action_names["createSession"].kind, "server_action")

        self.assertIn("useLiveKitDataChannel", hook_names)
        self.assertEqual(hook_names["useLiveKitDataChannel"].kind, "hook")

        self.assertIn("GET", route_names)
        self.assertEqual(route_names["GET"].kind, "api_route")

    def test_r1_4_ignore_path_filtering_rules(self):
        """T1.1.4: Verify directory and extension ignore filtering against standard project exclusions."""
        ignored_samples = [
            "Library/PackageCache/com.unity.xr/Runtime.cs",
            "node_modules/@livekit/components-react/dist/index.js",
            "LiveKitAgent/.venv/lib/python3.12/site-packages/livekit/agent.py",
            ".git/objects/pack/pack-123.pack",
            "obj/Debug/net8.0/Assembly.cs",
            "Temp/UnityLockfile",
            "Assets/Project/Textures/avatar.png",
            "Assets/Project/Models/character.fbx",
            "Assets/Project/Scripts/Core.cs.meta",
            "VR-Autism.csproj",
            ".next/server/pages/index.js",
            "_bmad/artifacts/spec.md",
        ]
        valid_samples = [
            "Assets/Project/Scripts/Cloud/FirebaseManager.cs",
            "Assets/Project/Scripts/Gameplay/Actions/Models/VoiceQuest.cs",
            "LiveKitAgent/src/agent.py",
            "LiveKitAgent/src/prompt.py",
            "src/actions/center.ts",
            "src/hooks/useLiveKitDataChannel.ts",
            "src/app/api/livekit-token/route.ts",
        ]

        for path in ignored_samples:
            self.assertTrue(
                is_ignored_path(path),
                f"Path should be ignored: {path}"
            )

        for path in valid_samples:
            self.assertFalse(
                is_ignored_path(path),
                f"Path should NOT be ignored: {path}"
            )

    def test_r1_5_cross_stack_contract_detection(self):
        """T1.1.5: Verify cross-stack contract detection for LiveKit events, RTDB paths, and REST endpoints."""
        unity_sample = '''
public class LiveKitBridge {
    public void Notify() {
        SendEvent("SET_ACTIVE_QUEST");
        SendEvent("QUEST_MATCHED");
        var path = "pairing_codes/123456";
    }
}
'''
        python_sample = '''
def handle_event(event_name):
    if event_name == "SET_ACTIVE_QUEST":
        print("Handling SET_ACTIVE_QUEST")
    elif event_name == "QUEST_MATCHED":
        print("Handling QUEST_MATCHED")
'''
        web_sample = '''
export function useSignaling() {
    const rtdbPath = "live_sessions/123/commands";
    fetch("/api/livekit-token");
}
'''
        u_symbols = self.cs_parser.parse_source(unity_sample, "Assets/Scripts/LiveKitBridge.cs")
        u_contracts = [ref.name for s in u_symbols for ref in s.cross_stack_refs]
        self.assertTrue(any("SET_ACTIVE_QUEST" in c for c in u_contracts))
        self.assertTrue(any("QUEST_MATCHED" in c for c in u_contracts))

        p_symbols = self.py_parser.parse_source(python_sample, "LiveKitAgent/src/handler.py")
        p_contracts = [ref.name for s in p_symbols for ref in s.cross_stack_refs]
        self.assertTrue(any("SET_ACTIVE_QUEST" in c for c in p_contracts))

        w_symbols = self.ts_parser.parse_source(web_sample, "src/hooks/useSignaling.ts")
        w_contracts = [ref.name for s in w_symbols for ref in s.cross_stack_refs]
        self.assertTrue(any("live_sessions" in c or "/api/livekit-token" in c for c in w_contracts))


class TestR2KnowledgeGraphTier1(unittest.TestCase):
    """
    Tier 1 tests for Requirement R2: Network & Contract Dependency Graph (Knowledge Graph).
    Exercises node schema, intra-language edges, cross-stack bridges, and PageRank scoring.
    """

    def setUp(self):
        if not HAVE_AST_PARSERS:
            self.skipTest("ast_parsers required for graph builder tests")

    def _create_mock_graph_fixture(self):
        """Construct synthetic multi-subsystem symbol set."""
        symbols = {
            "unity": [
                Symbol(
                    id="unity:Assets/Scripts/VoiceQuest.cs:VoiceQuest",
                    name="VoiceQuest",
                    kind="class",
                    file_path="Assets/Scripts/VoiceQuest.cs",
                    line_start=10,
                    line_end=50,
                    docstring="VR Voice quest handler",
                    language="csharp",
                    dependencies=["MonoBehaviour", "ILiveKitRoomClient"],
                    cross_stack_refs=[
                        ContractReference(type="livekit_event", name="SET_ACTIVE_QUEST", line=25, direction="publisher"),
                        ContractReference(type="livekit_event", name="QUEST_MATCHED", line=40, direction="subscriber"),
                    ]
                ),
                Symbol(
                    id="unity:Assets/Scripts/LiveKitService.cs:LiveKitService",
                    name="LiveKitService",
                    kind="class",
                    file_path="Assets/Scripts/LiveKitService.cs",
                    line_start=1,
                    line_end=100,
                    docstring="LiveKit Unity SDK bridge",
                    language="csharp",
                    dependencies=["MonoBehaviour"],
                    cross_stack_refs=[
                        ContractReference(type="livekit_event", name="SET_ACTIVE_QUEST", line=50, direction="publisher"),
                        ContractReference(type="livekit_event", name="QUEST_MATCHED", line=70, direction="subscriber"),
                    ]
                )
            ],
            "python": [
                Symbol(
                    id="python:LiveKitAgent/src/agent.py:TeacherAgent",
                    name="TeacherAgent",
                    kind="class",
                    file_path="LiveKitAgent/src/agent.py",
                    line_start=1,
                    line_end=80,
                    docstring="Teacher voice agent",
                    language="python",
                    dependencies=["Agent"],
                    cross_stack_refs=[
                        ContractReference(type="livekit_event", name="SET_ACTIVE_QUEST", line=20, direction="subscriber"),
                        ContractReference(type="livekit_event", name="QUEST_MATCHED", line=60, direction="publisher"),
                    ]
                )
            ],
            "web": [
                Symbol(
                    id="web:src/hooks/useLiveKitDataChannel.ts:useLiveKitDataChannel",
                    name="useLiveKitDataChannel",
                    kind="hook",
                    file_path="src/hooks/useLiveKitDataChannel.ts",
                    line_start=1,
                    line_end=50,
                    docstring="Web LiveKit DataChannel hook",
                    language="typescript",
                    dependencies=[],
                    cross_stack_refs=[
                        ContractReference(type="livekit_event", name="QUEST_STATUS", line=15, direction="subscriber"),
                        ContractReference(type="livekit_event", name="VERBAL_HINT", line=30, direction="publisher"),
                    ]
                )
            ]
        }
        return symbols

    def test_r2_1_graph_node_schema_and_attributes(self):
        """T1.2.1: Verify KnowledgeGraph node creation, required fields, and subsystem tagging."""
        if not HAVE_GRAPH_BUILDER:
            self.skipTest("graph_builder not yet implemented")

        kg = KnowledgeGraph()
        kg.add_symbol_node(
            Symbol(
                id="unity:Assets/Scripts/Test.cs:TestClass",
                name="TestClass",
                kind="class",
                file_path="Assets/Scripts/Test.cs",
                line_start=5,
                line_end=25,
                docstring="A test class",
                language="csharp"
            ),
            subsystem="unity"
        )

        d = kg.to_dict()
        self.assertIn("nodes", d)
        self.assertIn("edges", d)
        self.assertTrue(any(n["name"] == "TestClass" for n in d["nodes"]))
        node = next(n for n in d["nodes"] if n["name"] == "TestClass")
        self.assertEqual(node["subsystem"], "unity")
        self.assertEqual(node["kind"], "class")

    def test_r2_2_intra_language_edge_construction(self):
        """T1.2.2: Verify intra-language edge construction (INHERITS, CALLS, CONTAINS)."""
        if not HAVE_GRAPH_BUILDER:
            self.skipTest("graph_builder not yet implemented")

        kg = KnowledgeGraph()
        kg.add_node("unity:BaseClass", name="BaseClass", subsystem="unity", kind="class")
        kg.add_node("unity:DerivedClass", name="DerivedClass", subsystem="unity", kind="class")
        kg.add_edge("unity:DerivedClass", "unity:BaseClass", kind="INHERITS")

        d = kg.to_dict()
        inherits_edges = [e for e in d["edges"] if e.get("kind") == "INHERITS"]
        self.assertEqual(len(inherits_edges), 1)
        self.assertEqual(inherits_edges[0]["source"], "unity:DerivedClass")
        self.assertEqual(inherits_edges[0]["target"], "unity:BaseClass")

    def test_r2_3_livekit_contract_bridge_linking(self):
        """T1.2.3: Verify LiveKit DataPacket cross-stack contract bridging across Unity, Python, and Web."""
        if not HAVE_GRAPH_BUILDER:
            self.skipTest("graph_builder not yet implemented")

        symbols_by_subsystem = self._create_mock_graph_fixture()
        kg = KnowledgeGraph()
        kg.build_from_symbols(symbols_by_subsystem)

        d = kg.to_dict()
        contract_nodes = [n for n in d["nodes"] if n.get("subsystem") == "contract" or "contract:" in n.get("id", "")]
        self.assertTrue(len(contract_nodes) >= 1)

        # Check for SET_ACTIVE_QUEST bridge node
        set_active_nodes = [n for n in contract_nodes if "SET_ACTIVE_QUEST" in n.get("name", "") or "SET_ACTIVE_QUEST" in n.get("id", "")]
        self.assertTrue(len(set_active_nodes) >= 1)

    def test_r2_4_rtdb_contract_bridge_linking(self):
        """T1.2.4: Verify Firebase RTDB paths contract linking between Unity writer and Web listener."""
        if not HAVE_GRAPH_BUILDER:
            self.skipTest("graph_builder not yet implemented")

        kg = KnowledgeGraph()
        s_unity = Symbol(
            id="unity:FirebasePaths.cs:FirebasePaths",
            name="FirebasePaths",
            kind="class",
            file_path="Assets/Scripts/Cloud/FirebasePaths.cs",
            line_start=1,
            line_end=30,
            language="csharp",
            cross_stack_refs=[ContractReference(type="rtdb_path", name="live_sessions/{sessionId}/commands", line=10, direction="writer")]
        )
        s_web = Symbol(
            id="web:rtdb.ts:listenCommands",
            name="listenCommands",
            kind="function",
            file_path="src/lib/firebase/rtdb.ts",
            line_start=20,
            line_end=45,
            language="typescript",
            cross_stack_refs=[ContractReference(type="rtdb_path", name="live_sessions/{sessionId}/commands", line=22, direction="reader")]
        )

        kg.build_from_symbols({"unity": [s_unity], "python": [], "web": [s_web]})
        d = kg.to_dict()

        rtdb_contract_nodes = [n for n in d["nodes"] if "live_sessions" in n.get("name", "") or "live_sessions" in n.get("id", "")]
        self.assertTrue(len(rtdb_contract_nodes) >= 1)

    def test_r2_5_pagerank_computation_and_hub_ranking(self):
        """T1.2.5: Verify PageRank computation converges and ranks connected hubs higher than isolated leaves."""
        if not HAVE_GRAPH_BUILDER:
            self.skipTest("graph_builder not yet implemented")

        kg = KnowledgeGraph()
        # Hub node connected to multiple nodes
        kg.add_node("hub", name="HubNode", subsystem="unity", kind="class")
        kg.add_node("leaf1", name="Leaf1", subsystem="unity", kind="method")
        kg.add_node("leaf2", name="Leaf2", subsystem="python", kind="function")
        kg.add_node("leaf3", name="Leaf3", subsystem="web", kind="component")
        kg.add_node("isolated", name="Isolated", subsystem="unity", kind="class")

        kg.add_edge("leaf1", "hub", kind="CALLS")
        kg.add_edge("leaf2", "hub", kind="CALLS")
        kg.add_edge("leaf3", "hub", kind="CALLS")

        pr = kg.compute_pagerank(alpha=0.85)
        self.assertIn("hub", pr)
        self.assertIn("isolated", pr)
        self.assertGreater(pr["hub"], pr["isolated"], "Hub node must have higher PageRank than isolated node")
        self.assertAlmostEqual(sum(pr.values()), 1.0, places=2)


class TestR3RepoMapGeneratorTier1(unittest.TestCase):
    """
    Tier 1 tests for Requirement R3: Compressed RepoMap Generation.
    Exercises token budget compliance, markdown section layout, json schema, and custom config.
    """

    def setUp(self):
        if not HAVE_GRAPH_BUILDER or not HAVE_REPOMAP_GENERATOR:
            self.skipTest("graph_builder or repomap_generator not yet available")

    def _build_test_graph(self):
        kg = KnowledgeGraph()
        kg.add_node("unity:VoiceQuest", name="VoiceQuest", subsystem="unity", kind="class", docstring="Quest controller", file_path="Assets/Scripts/VoiceQuest.cs")
        kg.add_node("python:TeacherAgent", name="TeacherAgent", subsystem="python", kind="class", docstring="Voice teacher", file_path="LiveKitAgent/src/agent.py")
        kg.add_node("web:LiveKitHook", name="useLiveKit", subsystem="web", kind="hook", docstring="LiveKit Hook", file_path="src/hooks/useLiveKit.ts")
        kg.add_node("contract:SET_ACTIVE_QUEST", name="SET_ACTIVE_QUEST", subsystem="contract", kind="livekit_event")

        kg.add_edge("unity:VoiceQuest", "contract:SET_ACTIVE_QUEST", kind="PUBLISHES")
        kg.add_edge("python:TeacherAgent", "contract:SET_ACTIVE_QUEST", kind="SUBSCRIBES")
        return kg

    def test_r3_1_repomap_markdown_structure(self):
        """T1.3.1: Verify generated REPOMAP.md contains header, subsystem summary, bridges, and symbols."""
        kg = self._build_test_graph()
        md_text, json_data = generate_repomap(kg, config={"token_budget": 2000})

        self.assertIn("# VR-Autism Repository Map", md_text)
        self.assertIn("## Cross-Stack Communication Bridges", md_text)
        self.assertIn("## Core Architecture & Ranked Symbols", md_text)
        self.assertIn("VoiceQuest", md_text)
        self.assertIn("SET_ACTIVE_QUEST", md_text)

    def test_r3_2_repomap_token_budget_enforcement(self):
        """T1.3.2: Verify RepoMap generator strictly enforces configured token budget (<3000 default, 500 strict)."""
        kg = self._build_test_graph()
        # Add 50 extra nodes to test compression
        for i in range(50):
            nid = f"unity:DummyClass_{i}"
            kg.add_node(nid, name=f"DummyClass_{i}", subsystem="unity", kind="class", docstring="Synthetic dummy class for stress test")
            kg.add_edge(nid, "unity:VoiceQuest", kind="CALLS")

        md_strict, _ = generate_repomap(kg, config={"token_budget": 500})
        # Rough token approximation: words * 1.33 or len(text)/4
        estimated_tokens = len(md_strict.split()) * 1.3
        self.assertLessEqual(estimated_tokens, 650, f"RepoMap exceeded strict token budget: {estimated_tokens}")

    def test_r3_3_repomap_json_schema_validation(self):
        """T1.3.3: Verify structured repomap.json contains valid nodes, edges, pagerank rankings, and metadata."""
        kg = self._build_test_graph()
        _, json_data = generate_repomap(kg, config={"token_budget": 2000})

        self.assertIsInstance(json_data, dict)
        self.assertIn("metadata", json_data)
        self.assertIn("nodes", json_data)
        self.assertIn("edges", json_data)
        self.assertIn("rankings", json_data)
        self.assertIsInstance(json_data["nodes"], list)
        self.assertIsInstance(json_data["edges"], list)

    def test_r3_4_config_loading_custom_budget_and_weights(self):
        """T1.3.4: Verify custom repomap.config.json is respected for budgets and weights."""
        with tempfile.NamedTemporaryFile("w", suffix=".json", delete=False) as tf:
            cfg = {
                "token_budget": 1200,
                "weights": {
                    "contract": 3.0,
                    "unity": 1.0,
                    "python": 1.0,
                    "web": 1.0
                }
            }
            json.dump(cfg, tf)
            cfg_path = tf.name

        try:
            kg = self._build_test_graph()
            md, data = generate_repomap(kg, config_path=cfg_path)
            self.assertIn("SET_ACTIVE_QUEST", md)
        finally:
            if os.path.exists(cfg_path):
                os.remove(cfg_path)

    def test_r3_5_cross_stack_table_formatting(self):
        """T1.3.5: Verify markdown table syntax for cross-stack bridge summary."""
        kg = self._build_test_graph()
        md_text, _ = generate_repomap(kg)

        lines = [line.strip() for line in md_text.splitlines() if line.strip().startswith("|")]
        self.assertTrue(len(lines) >= 3, "Expected markdown table rows for cross-stack bridges")
        header = lines[0]
        self.assertIn("Contract", header)


class TestR4JITContextRetrieverTier1(unittest.TestCase):
    """
    Tier 1 tests for Requirement R4: JIT Context & Impact Analysis CLI.
    Exercises symbol retrieval, token budget trimming, and blast radius traversals.
    """

    def setUp(self):
        if not HAVE_GRAPH_BUILDER or not HAVE_JIT_CONTEXT:
            self.skipTest("graph_builder or jit_context not yet available")

    def _build_connected_graph(self):
        kg = KnowledgeGraph()
        # Subsystem Unity
        kg.add_node("unity:VoiceQuest", name="VoiceQuest", subsystem="unity", kind="class", file_path="Assets/Scripts/VoiceQuest.cs", docstring="Voice quest controller")
        kg.add_node("unity:LiveKitService", name="LiveKitService", subsystem="unity", kind="class", file_path="Assets/Scripts/LiveKitService.cs", docstring="LiveKit Unity bridge")
        # Contract
        kg.add_node("contract:SET_ACTIVE_QUEST", name="SET_ACTIVE_QUEST", subsystem="contract", kind="livekit_event")
        # Subsystem Python
        kg.add_node("python:TeacherAgent", name="TeacherAgent", subsystem="python", kind="class", file_path="LiveKitAgent/src/agent.py", docstring="Python Teacher Voice Agent")
        kg.add_node("python:complete_quest", name="complete_quest", subsystem="python", kind="tool", file_path="LiveKitAgent/src/agent.py", docstring="Complete quest tool")
        # Subsystem Web
        kg.add_node("web:useLiveKitDataChannel", name="useLiveKitDataChannel", subsystem="web", kind="hook", file_path="src/hooks/useLiveKitDataChannel.ts", docstring="LiveKit web hook")

        # Edges
        kg.add_edge("unity:VoiceQuest", "unity:LiveKitService", kind="CALLS")
        kg.add_edge("unity:VoiceQuest", "contract:SET_ACTIVE_QUEST", kind="PUBLISHES")
        kg.add_edge("python:TeacherAgent", "contract:SET_ACTIVE_QUEST", kind="SUBSCRIBES")
        kg.add_edge("python:TeacherAgent", "python:complete_quest", kind="CONTAINS")
        kg.add_edge("web:useLiveKitDataChannel", "contract:SET_ACTIVE_QUEST", kind="SUBSCRIBES")
        return kg

    def test_r4_1_jit_query_symbol_retrieval(self):
        """T1.4.1: Query symbol by name (VoiceQuest) and retrieve focused context subgraph."""
        kg = self._build_connected_graph()
        ctx = get_jit_context(kg, query="VoiceQuest", token_budget=1000)

        self.assertIn("VoiceQuest", ctx)
        self.assertIn("LiveKitService", ctx)
        self.assertIn("SET_ACTIVE_QUEST", ctx)

    def test_r4_2_jit_budget_trimming(self):
        """T1.4.2: Retrieve JIT context with a restrictive budget (100 tokens) and verify graceful truncation."""
        kg = self._build_connected_graph()
        ctx = get_jit_context(kg, query="VoiceQuest", token_budget=100)

        words = len(ctx.split())
        self.assertLessEqual(words * 1.3, 150)

    def test_r4_3_impact_analysis_unity_symbol(self):
        """T1.4.3: Perform blast radius impact analysis on Unity VoiceQuest and verify multi-subsystem reach."""
        kg = self._build_connected_graph()
        impact = get_impact_analysis(kg, symbol="VoiceQuest", max_depth=3)

        self.assertIn("affected_nodes", impact)
        affected_names = {n["name"] for n in impact["affected_nodes"]}

        self.assertIn("VoiceQuest", affected_names)
        self.assertIn("SET_ACTIVE_QUEST", affected_names)
        self.assertIn("TeacherAgent", affected_names)
        self.assertIn("useLiveKitDataChannel", affected_names)

        # Verify subsystem breakdown
        subsystems = impact.get("by_subsystem", {})
        self.assertIn("unity", subsystems)
        self.assertIn("python", subsystems)
        self.assertIn("web", subsystems)

    def test_r4_4_impact_analysis_contract_event(self):
        """T1.4.4: Perform blast radius impact analysis directly on LiveKit event SET_ACTIVE_QUEST."""
        kg = self._build_connected_graph()
        impact = get_impact_analysis(kg, symbol="SET_ACTIVE_QUEST", max_depth=2)

        affected_names = {n["name"] for n in impact["affected_nodes"]}
        self.assertIn("SET_ACTIVE_QUEST", affected_names)
        self.assertIn("VoiceQuest", affected_names)
        self.assertIn("TeacherAgent", affected_names)

    def test_r4_5_cli_arguments_parsing(self):
        """T1.4.5: Validate CLI invocations and help screens for scripts/jit_context.py."""
        import subprocess
        cli_path = REPO_ROOT / "scripts" / "jit_context.py"
        if not cli_path.exists():
            self.skipTest("scripts/jit_context.py does not exist yet")

        result = subprocess.run([sys.executable, str(cli_path), "--help"], capture_output=True, text=True)
        self.assertEqual(result.returncode, 0)
        self.assertIn("--query", result.stdout)
        self.assertIn("--impact", result.stdout)
        self.assertIn("--budget", result.stdout)


class TestR5GraphInvariantsAndVerificationTier1(unittest.TestCase):
    """
    Tier 1 tests for Requirement R5: Graph Invariants & Roundtrip Schema Verification.
    Exercises roundtrip consistency, dangling edge absence, PageRank determinism, and line spans.
    """

    def setUp(self):
        if not HAVE_AST_PARSERS:
            self.skipTest("ast_parsers not available")

    def test_r5_1_symbol_model_roundtrip_serialization(self):
        """T1.5.1: Verify Symbol and ContractReference to_dict() and from_dict() roundtrip fidelity."""
        ref = ContractReference(
            type="livekit_event",
            name="SET_ACTIVE_QUEST",
            line=42,
            context="SendActiveQuest",
            direction="publisher"
        )
        sym = Symbol(
            id="unity:Assets/Scripts/VoiceQuest.cs:VoiceQuest",
            name="VoiceQuest",
            kind="class",
            file_path="Assets/Scripts/VoiceQuest.cs",
            line_start=10,
            line_end=60,
            docstring="Voice quest controller",
            signature="public class VoiceQuest : MonoBehaviour",
            parent_id=None,
            language="csharp",
            modifiers=["public"],
            dependencies=["MonoBehaviour"],
            cross_stack_refs=[ref]
        )

        d = sym.to_dict()
        sym_restored = Symbol.from_dict(d)

        self.assertEqual(sym.id, sym_restored.id)
        self.assertEqual(sym.name, sym_restored.name)
        self.assertEqual(sym.line_start, sym_restored.line_start)
        self.assertEqual(len(sym.cross_stack_refs), len(sym_restored.cross_stack_refs))
        self.assertEqual(sym.cross_stack_refs[0].name, sym_restored.cross_stack_refs[0].name)

    def test_r5_2_no_dangling_contract_edges_in_graph(self):
        """T1.5.2: Verify graph integrity: every edge points to valid, defined source and target nodes."""
        if not HAVE_GRAPH_BUILDER:
            self.skipTest("graph_builder not available")

        kg = KnowledgeGraph()
        kg.add_node("A", name="A", subsystem="unity", kind="class")
        kg.add_node("B", name="B", subsystem="python", kind="class")
        kg.add_edge("A", "B", kind="CALLS")

        d = kg.to_dict()
        node_ids = {n["id"] for n in d["nodes"]}
        for edge in d["edges"]:
            self.assertIn(edge["source"], node_ids, f"Dangling edge source: {edge['source']}")
            self.assertIn(edge["target"], node_ids, f"Dangling edge target: {edge['target']}")

    def test_r5_3_pagerank_deterministic_reproducibility(self):
        """T1.5.3: Verify PageRank returns identical deterministic scores on multiple iterations."""
        if not HAVE_GRAPH_BUILDER:
            self.skipTest("graph_builder not available")

        kg = KnowledgeGraph()
        for i in range(5):
            kg.add_node(f"node_{i}", name=f"node_{i}", subsystem="unity", kind="class")
        kg.add_edge("node_0", "node_1", kind="CALLS")
        kg.add_edge("node_1", "node_2", kind="CALLS")
        kg.add_edge("node_2", "node_0", kind="CALLS")
        kg.add_edge("node_3", "node_0", kind="CALLS")

        pr1 = kg.compute_pagerank(alpha=0.85, max_iter=100)
        pr2 = kg.compute_pagerank(alpha=0.85, max_iter=100)

        for k in pr1:
            self.assertAlmostEqual(pr1[k], pr2[k], places=7)

    def test_r5_4_subsystem_partitioning_validity(self):
        """T1.5.4: Verify every node is assigned a valid subsystem ('unity', 'python', 'web', or 'contract')."""
        valid_subsystems = {"unity", "python", "web", "contract"}
        symbols = [
            Symbol(id="1", name="S1", kind="class", file_path="a.cs", line_start=1, line_end=2, language="csharp"),
            Symbol(id="2", name="S2", kind="class", file_path="b.py", line_start=1, line_end=2, language="python"),
            Symbol(id="3", name="S3", kind="class", file_path="c.ts", line_start=1, line_end=2, language="typescript"),
        ]
        for s in symbols:
            sub = "unity" if s.language == "csharp" else ("python" if s.language == "python" else "web")
            self.assertIn(sub, valid_subsystems)

    def test_r5_5_source_line_span_validity(self):
        """T1.5.5: Verify symbol line spans adhere to invariants: line_start >= 1 and line_start <= line_end."""
        code = '''
def calculate(a, b):
    # Sum calculation
    return a + b
'''
        parser = PythonASTParser()
        symbols = parser.parse_source(code, "test.py")
        for sym in symbols:
            self.assertGreaterEqual(sym.line_start, 1)
            self.assertLessEqual(sym.line_start, sym.line_end)


# ===========================================================================
# Tier 2: Boundary & Adversarial Corner Cases
# ===========================================================================

class TestTier2BoundaryAndCornerCases(unittest.TestCase):
    """
    Tier 2 tests: Boundary conditions, malformed inputs, cyclic graphs, extreme budgets, and Unicode.
    """

    def setUp(self):
        if not HAVE_AST_PARSERS:
            self.skipTest("ast_parsers not available")
        self.manager = ASTParserManager()

    def test_tier2_1_empty_directory_handling(self):
        """T2.1: Parse an empty directory without throwing exceptions."""
        with tempfile.TemporaryDirectory() as temp_dir:
            symbols = self.manager.parse_directory(temp_dir)
            self.assertEqual(symbols, [])

    def test_tier2_2_nonexistent_files_and_symbols(self):
        """T2.2: Handle non-existent file paths and symbol lookups gracefully."""
        symbols = self.manager.parse_file("non/existent/file.cs")
        self.assertEqual(symbols, [])

        if HAVE_GRAPH_BUILDER and HAVE_JIT_CONTEXT:
            kg = KnowledgeGraph()
            impact = get_impact_analysis(kg, symbol="NonExistentSymbol_XYZ")
            self.assertEqual(impact.get("affected_nodes", []), [])

    def test_tier2_3_malformed_syntax_resilience(self):
        """T2.3: Robustness against syntax errors, unclosed brackets, and gibberish in source files."""
        broken_py = "def foo( broken syntax {{{ "
        broken_cs = "namespace { public class { void ( { }} "
        broken_ts = "export function >>> { ;;; "

        py_syms = self.manager.python_parser.parse_source(broken_py, "broken.py")
        cs_syms = self.manager.csharp_parser.parse_source(broken_cs, "broken.cs")
        ts_syms = self.manager.typescript_parser.parse_source(broken_ts, "broken.ts")

        self.assertIsInstance(py_syms, list)
        self.assertIsInstance(cs_syms, list)
        self.assertIsInstance(ts_syms, list)

    def test_tier2_4_zero_and_extreme_token_budgets(self):
        """T2.4: Handle token budget boundaries (0, 1, 10, and 1,000,000) without crashing."""
        if not HAVE_GRAPH_BUILDER or not HAVE_REPOMAP_GENERATOR:
            self.skipTest("repomap_generator not available")

        kg = KnowledgeGraph()
        kg.add_node("n1", name="ClassA", subsystem="unity", kind="class", docstring="Doc A")
        kg.add_node("n2", name="ClassB", subsystem="python", kind="class", docstring="Doc B")

        # Zero budget
        md_zero, _ = generate_repomap(kg, config={"token_budget": 0})
        self.assertIsInstance(md_zero, str)

        # Huge budget
        md_huge, _ = generate_repomap(kg, config={"token_budget": 1000000})
        self.assertIn("ClassA", md_huge)
        self.assertIn("ClassB", md_huge)

    def test_tier2_5_cyclic_dependencies_in_graph(self):
        """T2.5: Graph with circular dependencies (A -> B -> C -> A) converges in PageRank and JIT search."""
        if not HAVE_GRAPH_BUILDER:
            self.skipTest("graph_builder not available")

        kg = KnowledgeGraph()
        kg.add_node("A", name="A", subsystem="unity", kind="class")
        kg.add_node("B", name="B", subsystem="unity", kind="class")
        kg.add_node("C", name="C", subsystem="unity", kind="class")

        kg.add_edge("A", "B", kind="CALLS")
        kg.add_edge("B", "C", kind="CALLS")
        kg.add_edge("C", "A", kind="CALLS")
        kg.add_edge("A", "A", kind="SELF_LOOP")

        pr = kg.compute_pagerank(alpha=0.85, max_iter=50)
        self.assertAlmostEqual(pr["A"], pr["B"], places=2)
        self.assertAlmostEqual(pr["B"], pr["C"], places=2)

    def test_tier2_6_disconnected_graph_components(self):
        """T2.6: Graph with isolated nodes and multiple disconnected clusters computes valid PageRank."""
        if not HAVE_GRAPH_BUILDER:
            self.skipTest("graph_builder not available")

        kg = KnowledgeGraph()
        kg.add_node("iso1", name="Isolated1", subsystem="unity", kind="class")
        kg.add_node("iso2", name="Isolated2", subsystem="python", kind="class")
        kg.add_node("clusterA1", name="A1", subsystem="web", kind="component")
        kg.add_node("clusterA2", name="A2", subsystem="web", kind="component")
        kg.add_edge("clusterA1", "clusterA2", kind="CALLS")

        pr = kg.compute_pagerank(alpha=0.85)
        self.assertEqual(len(pr), 4)
        for score in pr.values():
            self.assertFalse(math.isnan(score))
            self.assertFalse(math.isinf(score))

    def test_tier2_7_unicode_and_vietnamese_characters(self):
        """T2.7: Preserve Vietnamese Unicode phrases ('Xin chào bạn', 'Nhiệm vụ', 'Học sinh') and emojis."""
        py_code = '''
class NhiemVuTiengViet:
    """Xử lý nhiệm vụ tương tác với trẻ tự kỷ."""
    def xin_chao(self) -> str:
        return "Xin chào! Bạn có khỏe không? 😊"
'''
        symbols = self.manager.python_parser.parse_source(py_code, "vietnamese_agent.py")
        self.assertEqual(len(symbols), 2)
        names = {s.name: s for s in symbols}
        self.assertIn("NhiemVuTiengViet", names)
        self.assertIn("Xử lý nhiệm vụ", names["NhiemVuTiengViet"].docstring)


# ===========================================================================
# Tier 3: Cross-Feature Integration Pipelines
# ===========================================================================

class TestTier3CrossFeaturePipelines(unittest.TestCase):
    """
    Tier 3 tests: Full dataflow pipelines connecting AST Parsing -> Graph Building -> PageRank -> RepoMap / JIT.
    """

    def setUp(self):
        if not HAVE_AST_PARSERS or not HAVE_GRAPH_BUILDER or not HAVE_REPOMAP_GENERATOR or not HAVE_JIT_CONTEXT:
            self.skipTest("All modules (M1-M4) required for Tier 3 end-to-end pipelines")

    def test_tier3_1_e2e_pipeline_ast_to_repomap(self):
        """T3.1: Full pipeline: parse temporary multi-subsystem files -> build graph -> generate REPOMAP.md."""
        with tempfile.TemporaryDirectory() as temp_dir:
            temp_path = Path(temp_dir)
            unity_dir = temp_path / "Assets" / "Scripts"
            python_dir = temp_path / "LiveKitAgent" / "src"
            web_dir = temp_path / "src"

            unity_dir.mkdir(parents=True)
            python_dir.mkdir(parents=True)
            web_dir.mkdir(parents=True)

            # Write Unity C# file
            (unity_dir / "QuestAction.cs").write_text('''
public class QuestAction {
    public void Start() {
        LiveKit.Send("SET_ACTIVE_QUEST");
    }
}
''', encoding="utf-8")

            # Write Python Agent file
            (python_dir / "agent.py").write_text('''
def on_data(packet):
    if packet == "SET_ACTIVE_QUEST":
        complete()
''', encoding="utf-8")

            # Write Web file
            (web_dir / "hook.ts").write_text('''
export function useQuest() {
    return "SET_ACTIVE_QUEST";
}
''', encoding="utf-8")

            manager = ASTParserManager()
            symbols = manager.parse_project(
                unity_path=unity_dir,
                python_path=python_dir,
                web_path=web_dir
            )

            kg = KnowledgeGraph()
            kg.build_from_symbols(symbols)
            md_text, json_data = generate_repomap(kg, config={"token_budget": 2500})

            self.assertIn("SET_ACTIVE_QUEST", md_text)
            self.assertIn("QuestAction", md_text)
            self.assertTrue(len(json_data["nodes"]) >= 3)

    def test_tier3_2_e2e_pipeline_ast_to_jit_impact(self):
        """T3.2: Full pipeline: parse files -> build graph -> query JIT blast radius for cross-stack symbol."""
        with tempfile.TemporaryDirectory() as temp_dir:
            temp_path = Path(temp_dir)
            p1 = temp_path / "Publisher.cs"
            p2 = temp_path / "Subscriber.py"

            p1.write_text('public class Publisher { void Send() { LiveKit.Send("QUEST_MATCHED"); } }', encoding="utf-8")
            p2.write_text('def handle_event(e): if e == "QUEST_MATCHED": pass', encoding="utf-8")

            manager = ASTParserManager()
            s_u = manager.csharp_parser.parse_source(p1.read_text(), str(p1))
            s_p = manager.python_parser.parse_source(p2.read_text(), str(p2))

            kg = KnowledgeGraph()
            kg.build_from_symbols({"unity": s_u, "python": s_p, "web": []})

            impact = get_impact_analysis(kg, symbol="QUEST_MATCHED")
            names = {n["name"] for n in impact["affected_nodes"]}
            self.assertIn("QUEST_MATCHED", names)
            self.assertIn("Publisher", names)


# ===========================================================================
# Tier 4: Real-World Scenarios (VR-Autism Production Codebase)
# ===========================================================================

class TestTier4RealWorldScenarios(unittest.TestCase):
    """
    Tier 4 tests: Execute analysis directly against actual repository source files in d:\Lab\VR-Autism.
    """

    def setUp(self):
        if not HAVE_AST_PARSERS:
            self.skipTest("ast_parsers required for Tier 4 tests")
        self.manager = ASTParserManager()

    def test_tier4_1_real_livekit_quest_flow_symbols(self):
        """T4.1: Verify real LiveKit quest flow across Unity VoiceQuest.cs and Python agent.py."""
        voice_quest_path = REPO_ROOT / "Assets" / "Project" / "Scripts" / "Gameplay" / "Actions" / "Models" / "VoiceQuest.cs"
        agent_py_path = REPO_ROOT / "LiveKitAgent" / "src" / "agent.py"

        if not voice_quest_path.exists() or not agent_py_path.exists():
            self.skipTest("Real source files not present at expected paths")

        u_symbols = self.manager.parse_file(voice_quest_path)
        p_symbols = self.manager.parse_file(agent_py_path)

        u_names = {s.name for s in u_symbols}
        p_names = {s.name for s in p_symbols}

        self.assertIn("VoiceQuest", u_names)
        self.assertIn("TeacherAgent", p_names)
        self.assertIn("complete_quest", p_names)

    def test_tier4_2_real_rtdb_telemetry_flow_constants(self):
        """T4.2: Verify extraction of real FirebasePaths.cs RTDB constants and paths."""
        firebase_paths_file = REPO_ROOT / "Assets" / "Project" / "Scripts" / "Cloud" / "FirebasePaths.cs"
        if not firebase_paths_file.exists():
            self.skipTest("FirebasePaths.cs not found")

        symbols = self.manager.parse_file(firebase_paths_file)
        names = {s.name for s in symbols}
        self.assertIn("FirebasePaths", names)

    def test_tier4_3_real_pairing_data_model(self):
        """T4.3: Verify extraction of real PairingData.cs data model."""
        pairing_data_file = REPO_ROOT / "Assets" / "Project" / "Scripts" / "Cloud" / "Models" / "PairingData.cs"
        if not pairing_data_file.exists():
            self.skipTest("PairingData.cs not found")

        symbols = self.manager.parse_file(pairing_data_file)
        names = {s.name for s in symbols}
        self.assertIn("PairingData", names)

    def test_tier4_4_real_python_voice_agent_classes_and_tools(self):
        """T4.4: Verify complete extraction of real LiveKitAgent/src/agent.py classes, tools, and entrypoints."""
        agent_path = REPO_ROOT / "LiveKitAgent" / "src" / "agent.py"
        if not agent_path.exists():
            self.skipTest("LiveKitAgent/src/agent.py not found")

        symbols = self.manager.parse_file(agent_path)
        names = {s.name: s for s in symbols}

        self.assertIn("QuestState", names)
        self.assertIn("TeacherAgent", names)
        self.assertIn("complete_quest", names)
        self.assertIn("entrypoint", names)

    def test_tier4_5_real_repomap_production_run(self):
        """T4.5: Full production run on actual codebase (if graph_builder & repomap_generator available)."""
        if not HAVE_GRAPH_BUILDER or not HAVE_REPOMAP_GENERATOR:
            self.skipTest("graph_builder / repomap_generator required for full workspace scan")

        unity_path = REPO_ROOT / "Assets" / "Project" / "Scripts"
        python_path = REPO_ROOT / "LiveKitAgent" / "src"
        web_path = REPO_ROOT / "VRA-web" / "src"
        if not web_path.exists():
            # Check sibling path
            web_path = Path("d:/Lab/VRA-web/src")

        symbols = self.manager.parse_project(
            unity_path=unity_path if unity_path.exists() else None,
            python_path=python_path if python_path.exists() else None,
            web_path=web_path if web_path.exists() else None,
        )

        kg = KnowledgeGraph()
        kg.build_from_symbols(symbols)
        md_text, json_data = generate_repomap(kg, config={"token_budget": 3000})

        self.assertIsInstance(md_text, str)
        self.assertGreater(len(md_text), 100)
        self.assertIn("VR-Autism", md_text)
        self.assertIsInstance(json_data, dict)


# ===========================================================================
# CLI Runner and Custom Filters
# ===========================================================================

def run_tests(tier: Optional[int] = None) -> unittest.TestResult:
    """Run tests filtered optionally by tier (1, 2, 3, 4)."""
    suite = unittest.TestSuite()
    loader = unittest.TestLoader()

    if tier is None or tier == 1:
        suite.addTests(loader.loadTestsFromTestCase(TestR1ASTParsersTier1))
        suite.addTests(loader.loadTestsFromTestCase(TestR2KnowledgeGraphTier1))
        suite.addTests(loader.loadTestsFromTestCase(TestR3RepoMapGeneratorTier1))
        suite.addTests(loader.loadTestsFromTestCase(TestR4JITContextRetrieverTier1))
        suite.addTests(loader.loadTestsFromTestCase(TestR5GraphInvariantsAndVerificationTier1))

    if tier is None or tier == 2:
        suite.addTests(loader.loadTestsFromTestCase(TestTier2BoundaryAndCornerCases))

    if tier is None or tier == 3:
        suite.addTests(loader.loadTestsFromTestCase(TestTier3CrossFeaturePipelines))

    if tier is None or tier == 4:
        suite.addTests(loader.loadTestsFromTestCase(TestTier4RealWorldScenarios))

    runner = unittest.TextTestRunner(verbosity=2)
    return runner.run(suite)


if __name__ == "__main__":
    import argparse
    parser = argparse.ArgumentParser(description="Run VR-Autism CKG & RepoMap 4-Tier Test Suite.")
    parser.add_argument("--tier", type=int, choices=[1, 2, 3, 4], help="Run a specific tier of tests.")
    args = parser.parse_args()

    result = run_tests(tier=args.tier)
    sys.exit(0 if result.wasSuccessful() else 1)
