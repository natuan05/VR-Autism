#!/usr/bin/env python3
"""
VR-Autism JIT Context & Impact Analysis CLI (Milestone M4).
Provides on-demand symbol context extraction within token budget (--query)
and bidirectional blast radius traversal across Unity C#, Python Voice Agent,
and Next.js Web Dashboard subsystems (--impact).

Usage:
    python scripts/jit_context.py --impact VoiceQuest
    python scripts/jit_context.py --impact SET_ACTIVE_QUEST --format json
    python scripts/jit_context.py --query VoiceQuest --budget 1500
    python scripts/jit_context.py --help
"""

from __future__ import annotations

import argparse
import json
import logging
import os
import sys
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple, Union

# Ensure repository root is on sys.path
REPO_ROOT = Path(__file__).resolve().parent.parent
if str(REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(REPO_ROOT))

from scripts.ast_parsers import (
    ASTParserManager,
    DEFAULT_IGNORED_DIRS,
    DEFAULT_IGNORED_EXTS,
    LIVEKIT_EVENTS,
    REST_API_ROUTES,
    RTDB_PATH_PATTERNS,
    Symbol,
    is_ignored_path,
    normalize_path,
)
from scripts.graph_builder import (
    KnowledgeGraph,
    LightweightDiGraph,
    build_knowledge_graph,
)

logger = logging.getLogger("jit_context")

# Ensure UTF-8 output on Windows consoles
if sys.platform == "win32":
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
        sys.stderr.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass


# ---------------------------------------------------------------------------
# Module-level API functions (Required by test_repomap.py)
# ---------------------------------------------------------------------------

def get_impact_analysis(
    graph: KnowledgeGraph,
    symbol: str,
    max_depth: int = 3,
) -> Dict[str, Any]:
    """
    Perform bidirectional blast radius impact analysis on a symbol or contract name.
    Traverses forward and backward across Unity C#, Python Voice Agent, and Web subsystems.
    """
    return graph.find_impact_radius(symbol, max_depth=max_depth)


def get_jit_context(
    graph: KnowledgeGraph,
    query: str,
    token_budget: int = 1500,
) -> str:
    """
    Extract a token-budgeted Markdown context snippet containing ranked symbols,
    signatures, docstrings, and cross-stack contracts related to a query.
    """
    return graph.get_subgraph_context(query, token_budget=token_budget)


# ---------------------------------------------------------------------------
# Formatting Helpers
# ---------------------------------------------------------------------------

def format_impact_text(impact: Dict[str, Any]) -> str:
    """Format impact analysis results as human-readable console text."""
    root = impact.get("root", "")
    total = impact.get("total_affected", 0)
    max_depth = impact.get("max_depth", 3)
    affected = impact.get("affected_nodes", [])
    subsystems = impact.get("by_subsystem", {})

    lines = [
        "=" * 70,
        f"VR-Autism Cross-Stack Blast Radius Analysis",
        f"Target: `{root}` (Max Depth: {max_depth})",
        "=" * 70,
        f"Total Affected Symbols & Contracts: {total}",
        f"Subsystem Distribution: "
        f"Unity: {len(subsystems.get('unity', []))}, "
        f"Python: {len(subsystems.get('python', []))}, "
        f"Web: {len(subsystems.get('web', []))}, "
        f"Contracts: {len(subsystems.get('contract', []))}",
        "-" * 70,
    ]

    if not affected:
        lines.append("No matching symbol or contract bridge found in repository.")
        return "\n".join(lines)

    # Group by distance / depth
    by_distance: Dict[int, List[Dict[str, Any]]] = {}
    for n in affected:
        d = n.get("distance", 0)
        by_distance.setdefault(d, []).append(n)

    for dist in sorted(by_distance.keys()):
        dist_label = "Target Root" if dist == 0 else f"Hop Distance: {dist}"
        lines.append(f"\n[Depth {dist} — {dist_label}]")
        for node in by_distance[dist]:
            sub = node.get("subsystem", "unknown").upper()
            kind = node.get("kind", "unknown")
            name = node.get("name", "")
            fpath = node.get("file_path", "")
            lstart = node.get("line_start", 0)
            rel_dir = node.get("direction", "")
            pr = node.get("pagerank", 0.0)

            loc_str = f" ({fpath}:{lstart})" if fpath else ""
            dir_str = f" [{rel_dir}]" if rel_dir and rel_dir != "root" else ""
            lines.append(f"  • [{sub}] {kind} `{name}`{loc_str}{dir_str} (PR: {pr:.4f})")

    lines.append("\n" + "=" * 70)
    return "\n".join(lines)


def format_impact_json(impact: Dict[str, Any]) -> str:
    """Format impact analysis results as JSON."""
    return json.dumps(impact, indent=2, ensure_ascii=False)


# ---------------------------------------------------------------------------
# CLI Execution
# ---------------------------------------------------------------------------

def main() -> int:
    parser = argparse.ArgumentParser(
        description="VR-Autism JIT Context Retriever & Cross-Stack Blast Radius Analysis CLI."
    )
    parser.add_argument(
        "--query",
        type=str,
        default=None,
        help="Extract focused JIT context for a symbol or search query.",
    )
    parser.add_argument(
        "--budget",
        type=int,
        default=1500,
        help="Token budget for JIT context extraction (default: 1500).",
    )
    parser.add_argument(
        "--impact",
        type=str,
        default=None,
        help="Run cross-stack blast radius analysis on a symbol or contract name.",
    )
    parser.add_argument(
        "--depth",
        type=int,
        default=3,
        help="Maximum traversal depth for blast radius search (default: 3).",
    )
    parser.add_argument(
        "--format",
        type=str,
        choices=["text", "json"],
        default="text",
        help="Output format: 'text' or 'json' (default: text).",
    )
    parser.add_argument(
        "--config",
        type=str,
        default=None,
        help="Path to repomap.config.json.",
    )
    parser.add_argument(
        "--web-dir",
        type=str,
        default=None,
        help="Custom path to Next.js Web dashboard src directory.",
    )
    parser.add_argument(
        "--unity-dir",
        type=str,
        default=None,
        help="Custom path to Unity C# scripts directory.",
    )
    parser.add_argument(
        "--python-dir",
        type=str,
        default=None,
        help="Custom path to Python Voice Agent src directory.",
    )

    args = parser.parse_args()

    if not args.query and not args.impact:
        parser.print_help()
        return 0

    # Build or load Knowledge Graph
    graph = build_knowledge_graph(
        unity_path=args.unity_dir,
        python_path=args.python_dir,
        web_path=args.web_dir,
    )

    if args.impact:
        impact = get_impact_analysis(graph, symbol=args.impact, max_depth=args.depth)
        if args.format == "json":
            print(format_impact_json(impact))
        else:
            print(format_impact_text(impact))

    elif args.query:
        context = get_jit_context(graph, query=args.query, token_budget=args.budget)
        if args.format == "json":
            print(json.dumps({"query": args.query, "budget": args.budget, "context": context}, indent=2))
        else:
            print(context)

    return 0


if __name__ == "__main__":
    sys.exit(main())
