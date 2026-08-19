"""
Unit and Integration Tests for Cross-Stack AST Parsers (Milestone M1).
Tests PythonASTParser, CSharpASTParser, TypeScriptASTParser, ASTParserManager, and Ignore Filters.
Uses standard unittest library for zero-dependency execution.
"""

import os
import sys
import unittest
from pathlib import Path

# Add project root to sys.path
PROJECT_ROOT = Path(__file__).resolve().parent.parent.parent
sys.path.insert(0, str(PROJECT_ROOT))

from scripts.ast_parsers import (
    Symbol,
    ContractReference,
    PythonASTParser,
    CSharpASTParser,
    TypeScriptASTParser,
    ASTParserManager,
    is_ignored_path,
    normalize_path,
    LIVEKIT_EVENTS,
    RTDB_PATH_PATTERNS,
    REST_API_ROUTES,
)


class TestDataModelsAndUtilities(unittest.TestCase):
    """Test Symbol, ContractReference, and Ignore Filter functions."""

    def test_contract_reference_serialization(self):
        ref = ContractReference(
            type="livekit_event",
            name="SET_ACTIVE_QUEST",
            line=42,
            context="SendActiveQuest(nameToSend, phrasesToSend)",
            direction="publisher",
        )
        d = ref.to_dict()
        self.assertEqual(d["type"], "livekit_event")
        self.assertEqual(d["name"], "SET_ACTIVE_QUEST")
        self.assertEqual(d["line"], 42)
        self.assertEqual(d["direction"], "publisher")

        reconstructed = ContractReference.from_dict(d)
        self.assertEqual(reconstructed.name, ref.name)
        self.assertEqual(reconstructed.line, ref.line)
        self.assertEqual(reconstructed.direction, ref.direction)

    def test_symbol_serialization(self):
        ref = ContractReference(
            type="livekit_event",
            name="QUEST_MATCHED",
            line=89,
            context="HandleSpeechMatched()",
            direction="subscriber",
        )
        sym = Symbol(
            id="csharp:Assets/Project/Scripts/Gameplay/Actions/Models/VoiceQuest.cs:VoiceQuest",
            name="VoiceQuest",
            kind="class",
            file_path="Assets/Project/Scripts/Gameplay/Actions/Models/VoiceQuest.cs",
            line_start=7,
            line_end=95,
            docstring="Voice Quest handler",
            signature="public class VoiceQuest : Quest",
            parent_id=None,
            language="csharp",
            modifiers=["public"],
            dependencies=["Quest"],
            cross_stack_refs=[ref],
        )

        d = sym.to_dict()
        self.assertEqual(d["name"], "VoiceQuest")
        self.assertEqual(d["kind"], "class")
        self.assertEqual(len(d["cross_stack_refs"]), 1)
        self.assertEqual(d["cross_stack_refs"][0]["name"], "QUEST_MATCHED")

        reconstructed = Symbol.from_dict(d)
        self.assertEqual(reconstructed.id, sym.id)
        self.assertEqual(reconstructed.name, sym.name)
        self.assertEqual(len(reconstructed.cross_stack_refs), 1)
        self.assertIsInstance(reconstructed.cross_stack_refs[0], ContractReference)
        self.assertEqual(reconstructed.cross_stack_refs[0].name, "QUEST_MATCHED")

    def test_ignore_pattern_filtering(self):
        # Ignored paths
        self.assertTrue(is_ignored_path("Library/PackageCache/com.unity.test/file.cs"))
        self.assertTrue(is_ignored_path("node_modules/react/index.js"))
        self.assertTrue(is_ignored_path(".venv/Lib/site-packages/livekit/agent.py"))
        self.assertTrue(is_ignored_path(".git/HEAD"))
        self.assertTrue(is_ignored_path("Packages/manifest.json"))
        self.assertTrue(is_ignored_path("obj/Debug/Assembly-CSharp.dll"))
        self.assertTrue(is_ignored_path("Temp/bin/Debug/file.dll"))
        self.assertTrue(is_ignored_path("Assets/Project/Scripts/Cloud/LiveKitService.cs.meta"))
        self.assertTrue(is_ignored_path("Assets/Scene/Main.unity"))
        self.assertTrue(is_ignored_path(".next/server/app/page.js"))

        # Valid source paths (should NOT be ignored)
        self.assertFalse(is_ignored_path("Assets/Project/Scripts/Cloud/LiveKit/LiveKitService.cs"))
        self.assertFalse(is_ignored_path("LiveKitAgent/src/agent.py"))
        self.assertFalse(is_ignored_path("src/hooks/useLiveKitDataChannel.ts"))
        self.assertFalse(is_ignored_path("src/app/api/livekit-token/route.ts"))


class TestPythonASTParser(unittest.TestCase):
    """Test Python AST parser on synthetic code and real agent.py."""

    def setUp(self):
        self.parser = PythonASTParser()

    def test_synthetic_python_code(self):
        sample_code = '''"""Module docstring."""
import asyncio
from livekit.agents import llm

class QuestState:
    """Active quest state."""
    def __init__(self, name: str):
        self.name = name

    def reset(self) -> None:
        self.name = ""

def make_complete_quest_tool(runtime):
    @llm.function_tool(description="Completes quest")
    async def complete_quest() -> str:
        """Complete the active quest."""
        await send_rtc_event(runtime, "QUEST_MATCHED")
        await send_rtc_event(runtime, "QUEST_STATUS", {"status": "matched"})
        return "OK"
    return complete_quest

async def on_data_received(data):
    if data.get("event") == "SET_ACTIVE_QUEST":
        pass
'''
        symbols = self.parser.parse_source(sample_code, "test_agent.py")
        names = {s.name: s for s in symbols}

        # Verify classes and methods
        self.assertIn("QuestState", names)
        self.assertEqual(names["QuestState"].kind, "class")
        self.assertEqual(names["QuestState"].docstring, "Active quest state.")

        self.assertIn("reset", names)
        self.assertEqual(names["reset"].kind, "method")

        # Verify tool function with decorator
        self.assertIn("complete_quest", names)
        tool_sym = names["complete_quest"]
        self.assertEqual(tool_sym.kind, "tool")
        self.assertEqual(tool_sym.docstring, "Complete the active quest.")
        self.assertTrue(any("llm.function_tool" in m for m in tool_sym.modifiers))

        # Verify cross-stack contract detection
        tool_refs = {r.name: r for r in tool_sym.cross_stack_refs}
        self.assertIn("QUEST_MATCHED", tool_refs)
        self.assertEqual(tool_refs["QUEST_MATCHED"].direction, "publisher")
        self.assertIn("QUEST_STATUS", tool_refs)

        # Verify subscriber event in on_data_received
        self.assertIn("on_data_received", names)
        recv_sym = names["on_data_received"]
        recv_refs = {r.name: r for r in recv_sym.cross_stack_refs}
        self.assertIn("SET_ACTIVE_QUEST", recv_refs)
        self.assertEqual(recv_refs["SET_ACTIVE_QUEST"].direction, "subscriber")

    def test_real_python_agent_file(self):
        agent_path = PROJECT_ROOT / "LiveKitAgent" / "src" / "agent.py"
        if not agent_path.exists():
            self.skipTest(f"LiveKitAgent not found at {agent_path}")

        symbols = ASTParserManager().parse_file(agent_path)
        self.assertGreater(len(symbols), 0)

        sym_names = {s.name: s for s in symbols}
        # Check core classes
        self.assertIn("QuestState", sym_names)
        self.assertIn("JobRuntime", sym_names)
        self.assertIn("TeacherAgent", sym_names)

        # Check functions
        self.assertIn("build_quest_instructions", sym_names)
        self.assertIn("make_complete_quest_tool", sym_names)
        self.assertIn("complete_quest", sym_names)
        self.assertIn("entrypoint", sym_names)

        # Verify contract events extracted
        all_refs = [r for s in symbols for r in s.cross_stack_refs]
        event_names = {r.name for r in all_refs if r.type == "livekit_event"}
        self.assertIn("QUEST_MATCHED", event_names)
        self.assertIn("QUEST_STATUS", event_names)
        self.assertIn("SET_ACTIVE_QUEST", event_names)
        self.assertIn("VERBAL_HINT", event_names)
        self.assertIn("ON_REMINDER", event_names)
        self.assertIn("SPEAK_SCRIPT", event_names)


class TestCSharpASTParser(unittest.TestCase):
    """Test C# AST parser on synthetic code and real Unity scripts."""

    def setUp(self):
        self.parser = CSharpASTParser()

    def test_synthetic_csharp_code(self):
        sample_code = '''using System;
using UnityEngine;

namespace VRAutism.Cloud.LiveKit
{
    public interface ILiveKitRoomClient
    {
        event Action OnSpeechMatched;
        void SendActiveQuest(string questName, string[] defaultPhrases);
    }

    [Serializable]
    public class LiveKitService : MonoBehaviour, ILiveKitRoomClient
    {
        public static LiveKitService Instance { get; private set; }
        public event Action OnSpeechMatched;

        /// <summary>Send active quest to agent.</summary>
        public void SendActiveQuest(string questName, string[] defaultPhrases)
        {
            var packet = new { @event = "SET_ACTIVE_QUEST", quest_name = questName };
            room.LocalParticipant.PublishData(packet);
        }

        private void OnDataReceived(byte[] data)
        {
            if (packet.@event == "QUEST_MATCHED")
            {
                OnSpeechMatched?.Invoke();
            }
        }
    }
}
'''
        symbols = self.parser.parse_source(sample_code, "Assets/Scripts/LiveKitService.cs")
        names = {s.name: s for s in symbols}

        # Interface
        self.assertIn("ILiveKitRoomClient", names)
        self.assertEqual(names["ILiveKitRoomClient"].kind, "interface")

        # Class
        self.assertIn("LiveKitService", names)
        cls_sym = names["LiveKitService"]
        self.assertEqual(cls_sym.kind, "class")
        self.assertIn("MonoBehaviour", cls_sym.dependencies)
        self.assertIn("ILiveKitRoomClient", cls_sym.dependencies)

        # Property
        self.assertIn("Instance", names)
        self.assertEqual(names["Instance"].kind, "property")

        # Event
        self.assertIn("OnSpeechMatched", names)
        self.assertEqual(names["OnSpeechMatched"].kind, "event")

        # Methods
        self.assertIn("SendActiveQuest", names)
        send_sym = names["SendActiveQuest"]
        self.assertEqual(send_sym.kind, "method")
        self.assertIn("Send active quest to agent.", send_sym.docstring)

        # Cross-stack events
        send_refs = {r.name: r for r in send_sym.cross_stack_refs}
        self.assertIn("SET_ACTIVE_QUEST", send_refs)
        self.assertEqual(send_refs["SET_ACTIVE_QUEST"].direction, "publisher")

        self.assertIn("OnDataReceived", names)
        recv_sym = names["OnDataReceived"]
        recv_refs = {r.name: r for r in recv_sym.cross_stack_refs}
        self.assertIn("QUEST_MATCHED", recv_refs)
        self.assertEqual(recv_refs["QUEST_MATCHED"].direction, "subscriber")

    def test_real_unity_files(self):
        lk_service_path = PROJECT_ROOT / "Assets" / "Project" / "Scripts" / "Cloud" / "LiveKit" / "LiveKitService.cs"
        if not lk_service_path.exists():
            self.skipTest(f"LiveKitService.cs not found at {lk_service_path}")

        symbols = ASTParserManager().parse_file(lk_service_path)
        self.assertGreater(len(symbols), 0)

        names = {s.name: s for s in symbols}
        self.assertIn("LiveKitService", names)
        self.assertIn("Instance", names)
        self.assertIn("Connect", names)
        self.assertIn("SendActiveQuest", names)
        self.assertIn("SendVerbalHint", names)
        self.assertIn("SendOnReminder", names)
        self.assertIn("OnDataReceived", names)

        # Check VoiceQuest
        vq_path = PROJECT_ROOT / "Assets" / "Project" / "Scripts" / "Gameplay" / "Actions" / "Models" / "VoiceQuest.cs"
        if vq_path.exists():
            vq_syms = ASTParserManager().parse_file(vq_path)
            vq_names = {s.name: s for s in vq_syms}
            self.assertIn("VoiceQuest", vq_names)
            self.assertIn("OnBegin", vq_names)
            self.assertIn("OnVerbalHint", vq_names)
            self.assertIn("HandleSpeechMatched", vq_names)


class TestTypeScriptASTParser(unittest.TestCase):
    """Test TypeScript/TSX AST parser on synthetic and real web code."""

    def setUp(self):
        self.parser = TypeScriptASTParser()

    def test_synthetic_server_action_and_hook(self):
        sample_ts = '''"use server";

import { adminDb } from "@/lib/firebase/admin";

export interface SessionResult {
    id: string;
    score: number;
}

export type StatusType = "active" | "completed";

/**
 * Creates a new session in Firestore.
 */
export async function createSession(childId: string): Promise<SessionResult> {
    const res = await fetch("/api/livekit-token?room=123");
    return { id: "1", score: 100 };
}
'''
        symbols = self.parser.parse_source(sample_ts, "src/actions/session.ts")
        names = {s.name: s for s in symbols}

        # Interface & Type
        self.assertIn("SessionResult", names)
        self.assertEqual(names["SessionResult"].kind, "interface")
        self.assertIn("StatusType", names)
        self.assertEqual(names["StatusType"].kind, "type")

        # Server Action
        self.assertIn("createSession", names)
        action_sym = names["createSession"]
        self.assertEqual(action_sym.kind, "server_action")
        self.assertIn("use_server", action_sym.modifiers)
        self.assertIn("Creates a new session in Firestore.", action_sym.docstring)

        # Cross-stack API route call
        api_refs = [r for r in action_sym.cross_stack_refs if r.type == "api_route"]
        self.assertEqual(len(api_refs), 1)
        self.assertEqual(api_refs[0].name, "/api/livekit-token")

    def test_synthetic_api_route(self):
        sample_route = '''import { NextRequest, NextResponse } from "next/server";

export async function GET(req: NextRequest) {
    return NextResponse.json({ ok: true });
}

export async function POST(req: NextRequest) {
    return NextResponse.json({ created: true });
}
'''
        symbols = self.parser.parse_source(sample_route, "src/app/api/livekit-token/route.ts")
        names = {s.name: s for s in symbols}

        self.assertIn("GET", names)
        self.assertEqual(names["GET"].kind, "api_route")
        self.assertIn("POST", names)
        self.assertEqual(names["POST"].kind, "api_route")

    def test_real_web_files(self):
        # Look for VRA-web
        cand_paths = [
            PROJECT_ROOT.parent / "VRA-web" / "src" / "hooks" / "useLiveKitDataChannel.ts",
            Path("d:/Lab/VRA-web/src/hooks/useLiveKitDataChannel.ts"),
        ]
        target_path = None
        for p in cand_paths:
            if p.exists():
                target_path = p
                break

        if not target_path:
            self.skipTest("VRA-web not found")

        symbols = ASTParserManager().parse_file(target_path)
        self.assertGreater(len(symbols), 0)

        names = {s.name: s for s in symbols}
        self.assertIn("useLiveKitDataChannel", names)
        self.assertEqual(names["useLiveKitDataChannel"].kind, "hook")

        all_refs = [r for s in symbols for r in s.cross_stack_refs]
        event_names = {r.name for r in all_refs if r.type == "livekit_event"}
        self.assertIn("VERBAL_HINT", event_names)
        self.assertIn("SPEAK_SCRIPT", event_names)
        self.assertIn("QUEST_STATUS", event_names)


class TestASTParserManager(unittest.TestCase):
    """Test ASTParserManager full traversal and project parsing."""

    def test_parse_project_full(self):
        manager = ASTParserManager()
        unity_dir = PROJECT_ROOT / "Assets" / "Project" / "Scripts"
        python_dir = PROJECT_ROOT / "LiveKitAgent" / "src"
        web_dir = Path("d:/Lab/VRA-web/src") if Path("d:/Lab/VRA-web/src").exists() else None

        results = manager.parse_project(
            unity_path=unity_dir if unity_dir.exists() else None,
            python_path=python_dir if python_dir.exists() else None,
            web_path=web_dir,
        )

        self.assertIsInstance(results, dict)
        self.assertIn("unity", results)
        self.assertIn("python", results)
        self.assertIn("web", results)

        if unity_dir.exists():
            self.assertGreater(len(results["unity"]), 50, "Unity symbols should be extracted")
        if python_dir.exists():
            self.assertGreater(len(results["python"]), 10, "Python symbols should be extracted")
        if web_dir and web_dir.exists():
            self.assertGreater(len(results["web"]), 30, "Web symbols should be extracted")


if __name__ == "__main__":
    unittest.main()
