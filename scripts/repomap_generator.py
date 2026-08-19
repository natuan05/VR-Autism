#!/usr/bin/env python3
"""
VR-Autism Compressed RepoMap Generator (Milestone M3).
Generates token-optimized repository map (REPOMAP.md) and structured graph JSON (repomap.json)
using PageRank ranking, subsystem priority weighting, and cross-stack communication contract bridging.

Usage:
    python scripts/repomap_generator.py [--config repomap.config.json] [--output-md REPOMAP.md] [--output-json repomap.json] [--web-dir <path>]
"""

from __future__ import annotations

import argparse
import datetime
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

logger = logging.getLogger("repomap_generator")

# Ensure UTF-8 output on Windows consoles
if sys.platform == "win32":
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
        sys.stderr.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass

# ---------------------------------------------------------------------------
# Default Configuration
# ---------------------------------------------------------------------------

DEFAULT_CONFIG: Dict[str, Any] = {
    "token_budget": 2800,
    "weights": {
        "unity": 1.0,
        "python": 1.2,
        "web": 1.0,
        "contract": 1.5,
    },
    "paths": {
        "unity_path": "Assets/Project/Scripts",
        "python_path": "LiveKitAgent/src",
        "web_path": "VRA-web/src",
    },
    "ignore_patterns": [
        "Library",
        "node_modules",
        ".venv",
        ".git",
        "Packages",
        "obj",
        "Temp",
        "dist",
        ".next",
    ],
}


def load_configuration(
    config_path: Optional[Union[str, Path]] = None,
    override_config: Optional[Dict[str, Any]] = None,
) -> Dict[str, Any]:
    """Load configuration from JSON file and merge with overrides and defaults."""
    cfg = dict(DEFAULT_CONFIG)

    # 1. Load from file if provided or default exists
    target_path = None
    if config_path:
        target_path = Path(config_path)
    elif (REPO_ROOT / "repomap.config.json").exists():
        target_path = REPO_ROOT / "repomap.config.json"

    if target_path and target_path.exists():
        try:
            with open(target_path, "r", encoding="utf-8") as f:
                loaded = json.load(f)
                if isinstance(loaded, dict):
                    # Deep merge weights and paths
                    if "weights" in loaded and isinstance(loaded["weights"], dict):
                        cfg["weights"] = {**cfg.get("weights", {}), **loaded["weights"]}
                    if "paths" in loaded and isinstance(loaded["paths"], dict):
                        cfg["paths"] = {**cfg.get("paths", {}), **loaded["paths"]}
                    for k, v in loaded.items():
                        if k not in {"weights", "paths"}:
                            cfg[k] = v
        except Exception as e:
            logger.warning(f"Error loading config from {target_path}: {e}")

    # 2. Merge runtime override dictionary
    if override_config:
        if "weights" in override_config and isinstance(override_config["weights"], dict):
            cfg["weights"] = {**cfg.get("weights", {}), **override_config["weights"]}
        if "paths" in override_config and isinstance(override_config["paths"], dict):
            cfg["paths"] = {**cfg.get("paths", {}), **override_config["paths"]}
        for k, v in override_config.items():
            if k not in {"weights", "paths"}:
                cfg[k] = v

    return cfg


def estimate_tokens(text: str) -> int:
    """Estimate token count using standard words heuristic (words * 1.3)."""
    if not text:
        return 0
    return int(len(text.split()) * 1.3)


def format_cross_stack_table(bridges: List[Dict[str, Any]]) -> str:
    """Format Markdown table summarizing cross-stack communication contracts."""
    if not bridges:
        return ""

    lines = [
        "| Contract | Type | Publishers / Writers | Subscribers / Listeners | Connections |",
        "| :--- | :--- | :--- | :--- | :---: |",
    ]

    for b in bridges:
        name = b.get("name", "")
        kind = b.get("kind", "contract").replace("_", " ").title()
        pubs = ", ".join(f"`{p}`" for p in b.get("publishers", [])[:3]) or "*None*"
        subs = ", ".join(f"`{s}`" for s in b.get("subscribers", [])[:3]) or "*None*"
        total = b.get("total_connections", 0)

        lines.append(f"| `{name}` | {kind} | {pubs} | {subs} | {total} |")

    return "\n".join(lines)


def generate_repomap_markdown(
    graph: KnowledgeGraph,
    token_budget: int = 2800,
    weights: Optional[Dict[str, float]] = None,
) -> str:
    """
    Format compact, token-budgeted Markdown repository map (REPOMAP.md).
    Guarantees output remains strictly within token budget.
    """
    if token_budget <= 0:
        return "# VR-Autism Repository Map\n\n*Token budget exhausted.*\n"

    # Summary statistics
    data = graph.to_dict()
    meta = data.get("metadata", {})
    subsystems = meta.get("subsystems", {})
    total_nodes = meta.get("total_nodes", 0)
    total_edges = meta.get("total_edges", 0)

    # 1. Base Header & Overview Section
    header_parts = [
        "# VR-Autism Repository Map",
        "",
        "> **Multi-Platform Architecture**: Unity C# (`Assets/Project/Scripts`), Python LiveKit Voice Agent (`LiveKitAgent/src`), and Next.js Web Dashboard (`src`).",
        "",
        f"- **Graph Stats**: {total_nodes} symbols/nodes, {total_edges} relations/edges across {len(subsystems)} subsystems.",
        f"- **Subsystem Counts**: Unity: {subsystems.get('unity', 0)}, Python: {subsystems.get('python', 0)}, Web: {subsystems.get('web', 0)}, Contracts: {subsystems.get('contract', 0)}.",
        "",
    ]
    header_text = "\n".join(header_parts)
    if estimate_tokens(header_text) >= token_budget:
        return header_text

    # 2. Cross-Stack Bridges Section
    bridges = graph.get_contract_bridges()
    bridge_section_lines = [
        "## Cross-Stack Communication Bridges",
        "",
        "Cross-boundary coordination contracts linking Unity C# clients, Python Voice Agent, and Web Dashboard:",
        "",
    ]

    if bridges:
        bridge_table = format_cross_stack_table(bridges)
        bridge_section_lines.append(bridge_table)
    else:
        contract_nodes = [
            n for n in data.get("nodes", [])
            if n.get("subsystem") == "contract" or n.get("id", "").startswith("contract:")
        ]
        if contract_nodes:
            bridge_section_lines.extend([
                "| Contract | Type | Details |",
                "| :--- | :--- | :--- |",
            ])
            for cn in contract_nodes:
                bridge_section_lines.append(f"| `{cn.get('name')}` | {cn.get('kind')} | {cn.get('docstring', '')} |")
        else:
            bridge_section_lines.append("*No cross-stack communication contracts detected.*")

    bridge_section_lines.append("")
    bridge_section_text = "\n".join(bridge_section_lines)

    # Accumulate markdown lines
    lines: List[str] = list(header_parts)
    
    # Try adding bridges if budget permits
    candidate_with_bridges = "\n".join(lines + [bridge_section_text])
    if estimate_tokens(candidate_with_bridges) < token_budget:
        lines.append(bridge_section_text)

    # 3. Core Architecture & Ranked Symbols Section
    core_intro = [
        "## Core Architecture & Ranked Symbols",
        "",
        "Top architectural hubs and high-centrality symbols ranked by PageRank importance:",
        "",
    ]
    candidate_with_core = "\n".join(lines + core_intro)
    if estimate_tokens(candidate_with_core) < token_budget:
        lines.extend(core_intro)

        # Group symbols by subsystem
        nodes = data.get("nodes", [])
        symbol_nodes = [n for n in nodes if n.get("subsystem") != "contract"]
        symbol_nodes.sort(key=lambda n: -n.get("pagerank", 0.0))

        subsystem_groups: Dict[str, List[Dict[str, Any]]] = {
            "unity": [],
            "python": [],
            "web": [],
        }
        for n in symbol_nodes:
            sub = n.get("subsystem", "unity")
            if sub in subsystem_groups:
                subsystem_groups[sub].append(n)
            else:
                subsystem_groups.setdefault(sub, []).append(n)

        subsystem_titles = {
            "unity": "### Unity C# Core Subsystem (`Assets/Project/Scripts`)",
            "python": "### Python Voice Agent Subsystem (`LiveKitAgent/src`)",
            "web": "### Next.js Web Dashboard Subsystem (`src`)",
        }

        budget_hit = False
        for sub_key, sub_title in subsystem_titles.items():
            if budget_hit:
                break
            sub_symbols = subsystem_groups.get(sub_key, [])
            if not sub_symbols:
                continue

            sub_header_lines = [sub_title, ""]
            candidate_with_sub_hdr = "\n".join(lines + sub_header_lines)
            if estimate_tokens(candidate_with_sub_hdr) >= token_budget:
                budget_hit = True
                break

            lines.extend(sub_header_lines)

            for sym in sub_symbols:
                name = sym.get("name", "")
                kind = sym.get("kind", "")
                fpath = sym.get("file_path", "")
                lstart = sym.get("line_start", 0)
                doc = sym.get("docstring", "")
                pr = sym.get("pagerank", 0.0)

                clean_doc = doc.replace("\n", " ").strip()
                if len(clean_doc) > 80:
                    clean_doc = clean_doc[:77] + "..."

                loc_str = f"`{fpath}:{lstart}`" if fpath else ""
                sym_line = f"- **`{name}`** ({kind}) [{loc_str}] (PR: {pr:.4f})"
                if clean_doc:
                    sym_line += f" — *{clean_doc}*"

                candidate_with_sym = "\n".join(lines + [sym_line])
                if estimate_tokens(candidate_with_sym) >= token_budget:
                    lines.append("  *(Remaining subsystem symbols truncated to respect token budget)*\n")
                    budget_hit = True
                    break

                lines.append(sym_line)
            lines.append("")

    full_markdown = "\n".join(lines)

    # Final safeguard against token overflow
    while estimate_tokens(full_markdown) > token_budget and len(lines) > 2:
        lines.pop()
        full_markdown = "\n".join(lines)

    return full_markdown


def generate_repomap(
    graph: Optional[KnowledgeGraph] = None,
    config: Optional[Dict[str, Any]] = None,
    config_path: Optional[Union[str, Path]] = None,
    output_md: Optional[Union[str, Path]] = None,
    output_json: Optional[Union[str, Path]] = None,
    unity_path: Optional[Union[str, Path]] = None,
    python_path: Optional[Union[str, Path]] = None,
    web_path: Optional[Union[str, Path]] = None,
) -> Tuple[str, Dict[str, Any]]:
    """
    Main entrypoint for RepoMap and CKG JSON generation.
    Computes PageRank over KnowledgeGraph, formats compact Markdown and structured JSON.
    """
    # 1. Resolve configuration
    cfg = load_configuration(config_path=config_path, override_config=config)
    token_budget = int(cfg.get("token_budget", 2800))
    weights = cfg.get("weights", {})
    paths = cfg.get("paths", {})

    # 2. Build or load graph if not supplied
    if graph is None:
        u_p = unity_path or paths.get("unity_path")
        p_p = python_path or paths.get("python_path")
        w_p = web_path or paths.get("web_path")
        graph = build_knowledge_graph(unity_path=u_p, python_path=p_p, web_path=w_p)

    # 3. Compute PageRank with subsystem priority weights if present
    personalization: Optional[Dict[str, float]] = None
    if weights and isinstance(weights, dict):
        personalization = {}
        for nid, attrs in graph.graph.nodes(data=True):
            sub = attrs.get("subsystem", "unity")
            w = float(weights.get(sub, 1.0))
            personalization[nid] = w

    if personalization:
        scores = graph.graph.pagerank(alpha=0.85, personalization=personalization)
        for nid, score in scores.items():
            if nid in graph.graph._node:
                graph.graph._node[nid]["pagerank"] = score
    else:
        graph.compute_pagerank(alpha=0.85)

    # 4. Generate Markdown text
    md_text = generate_repomap_markdown(graph, token_budget=token_budget, weights=weights)

    # 5. Build structured JSON representation
    graph_dict = graph.to_dict()
    json_data: Dict[str, Any] = {
        "metadata": {
            **graph_dict.get("metadata", {}),
            "token_budget": token_budget,
            "weights": weights,
            "generated_at": datetime.datetime.now(datetime.timezone.utc).isoformat(),
        },
        "nodes": graph_dict.get("nodes", []),
        "edges": graph_dict.get("edges", []),
        "rankings": graph_dict.get("rankings", []),
        "contracts": graph.get_contract_bridges(),
    }

    # 6. Save outputs to disk if paths provided
    if output_md:
        out_md_path = Path(output_md)
        out_md_path.parent.mkdir(parents=True, exist_ok=True)
        out_md_path.write_text(md_text, encoding="utf-8")
        logger.info(f"Wrote RepoMap markdown to: {out_md_path}")

    if output_json:
        out_json_path = Path(output_json)
        out_json_path.parent.mkdir(parents=True, exist_ok=True)
        with open(out_json_path, "w", encoding="utf-8") as f:
            json.dump(json_data, f, indent=2, ensure_ascii=False)
        logger.info(f"Wrote RepoMap JSON to: {out_json_path}")

    return md_text, json_data


# ---------------------------------------------------------------------------
# CLI Command
# ---------------------------------------------------------------------------

def main() -> int:
    """CLI execution for repomap_generator.py."""
    parser = argparse.ArgumentParser(
        description="Generate token-optimized REPOMAP.md and structured repomap.json for VR-Autism."
    )
    parser.add_argument(
        "--config",
        type=str,
        default="repomap.config.json",
        help="Path to repomap.config.json configuration file (default: repomap.config.json).",
    )
    parser.add_argument(
        "--output-md",
        type=str,
        default="REPOMAP.md",
        help="Output Markdown path (default: REPOMAP.md).",
    )
    parser.add_argument(
        "--output-json",
        type=str,
        default="repomap.json",
        help="Output JSON path (default: repomap.json).",
    )
    parser.add_argument(
        "--web-dir",
        type=str,
        default=None,
        help="Custom path to Next.js Web dashboard src directory.",
    )
    parser.add_argument(
        "--budget",
        type=int,
        default=None,
        help="Override token budget.",
    )

    args = parser.parse_args()

    override_config: Dict[str, Any] = {}
    if args.budget:
        override_config["token_budget"] = args.budget
    if args.web_dir:
        override_config.setdefault("paths", {})["web_path"] = args.web_dir

    print("=" * 70)
    print("VR-Autism Compressed RepoMap Generator")
    print("=" * 70)

    config_p = args.config if os.path.exists(args.config) else None
    md_text, json_data = generate_repomap(
        config=override_config,
        config_path=config_p,
        output_md=args.output_md,
        output_json=args.output_json,
        web_path=args.web_dir,
    )

    tokens = estimate_tokens(md_text)
    meta = json_data.get("metadata", {})
    print(f"Generated {args.output_md}: ~{tokens} tokens (budget: {meta.get('token_budget')})")
    print(f"Generated {args.output_json}: {meta.get('total_nodes')} nodes, {meta.get('total_edges')} edges")
    print(f"Subsystems: {meta.get('subsystems')}")
    print(f"Cross-Stack Contracts: {len(json_data.get('contracts', []))} bridges")
    print("Done!")
    return 0


if __name__ == "__main__":
    sys.exit(main())
