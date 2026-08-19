"""
Cross-Stack AST Parsers and Unified Symbol Model for VR-Autism.
Supports Python (AST), C# (High-Precision Lexer/Token Parser), and TypeScript/TSX (Lexer/Token Parser).
Zero external dependencies (pure Python standard library) with optional Tree-sitter fallback.
"""

from __future__ import annotations

import ast
import dataclasses
import json
import logging
import os
import re
import tempfile
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Dict, List, Optional, Set, Tuple, Union

logger = logging.getLogger("ast_parsers")

# ---------------------------------------------------------------------------
# Contract Constants
# ---------------------------------------------------------------------------

LIVEKIT_EVENTS = {
    "SET_ACTIVE_QUEST",
    "QUEST_MATCHED",
    "QUEST_STATUS",
    "VERBAL_HINT",
    "ON_REMINDER",
    "SPEAK_SCRIPT",
    "AGENT_INIT_FAILED",
}

RTDB_PATH_PATTERNS = {
    "pairing_codes",
    "live_sessions",
    "behavior_snapshots",
    "webrtc_signaling",
    "sessions",
    "lessons",
    "skills",
    "quest_list",
}

REST_API_ROUTES = {
    "/api/livekit-token",
    "/api/tts",
}

DEFAULT_IGNORED_DIRS = {
    "Library",
    "node_modules",
    ".venv",
    "venv",
    ".git",
    "Packages",
    "obj",
    "Temp",
    "dist",
    ".next",
    "build",
    "bin",
    ".vs",
    ".idea",
    ".agents",
    "_bmad",
    "_bmad-output",
    "tmp",
    "Logs",
    "UserSettings",
    "ProjectSettings",
    "PackageCache",
}

DEFAULT_IGNORED_EXTS = {
    ".meta",
    ".csproj",
    ".sln",
    ".slnx",
    ".asset",
    ".prefab",
    ".mat",
    ".unity",
    ".png",
    ".jpg",
    ".jpeg",
    ".mp3",
    ".wav",
    ".fbx",
    ".glb",
    ".gltf",
    ".ttf",
    ".json",
    ".lock",
    ".log",
    ".ico",
    ".css",
}


# ---------------------------------------------------------------------------
# Delimiter Matching & Path Utilities
# ---------------------------------------------------------------------------

def normalize_path(path: Union[str, Path]) -> str:
    """Normalize file path to POSIX style (forward slashes)."""
    return str(path).replace("\\", "/")


def is_ignored_path(
    path: Union[str, Path],
    custom_ignored_dirs: Optional[Set[str]] = None,
    custom_ignored_exts: Optional[Set[str]] = None,
) -> bool:
    """
    Check if a file or directory path should be ignored during parsing.
    Prevents false positives on OS system temp directories while strictly
    ignoring project-relative ignored folders (Library, node_modules, .venv, etc.).
    """
    ignored_dirs = custom_ignored_dirs or DEFAULT_IGNORED_DIRS
    ignored_exts = custom_ignored_exts or DEFAULT_IGNORED_EXTS

    p = Path(path)
    norm_str = normalize_path(p)

    # Check extension
    if p.suffix.lower() in ignored_exts:
        return True

    parts = norm_str.split("/")

    # If path is absolute, strip OS system temp directory prefix if inside temp
    if p.is_absolute():
        for temp_candidate in [
            tempfile.gettempdir(),
            os.environ.get("TEMP", ""),
            os.environ.get("TMP", ""),
            "/tmp",
            "/var/tmp",
        ]:
            if temp_candidate:
                try:
                    norm_temp = normalize_path(Path(temp_candidate).resolve())
                    resolved_p = normalize_path(p.resolve())
                    if resolved_p.lower().startswith(norm_temp.lower()):
                        rel = os.path.relpath(resolved_p, norm_temp)
                        parts = normalize_path(rel).split("/")
                        break
                except Exception:
                    pass

    for part in parts:
        if not part or part == "." or part == "..":
            continue
        if part in ignored_dirs:
            return True
        if part.startswith(".") and part not in {".", ".."}:
            # Ignore hidden directories (.git, .vs, .next, .agents, etc.)
            return True

    return False


def find_matching_delimiter(
    text: str,
    open_idx: int,
    open_char: str = "(",
    close_char: str = ")",
) -> int:
    """
    Find index of matching close_char for open_char at open_idx,
    ignoring strings (single, double, template) and comments (//, /* */).
    """
    if open_idx >= len(text) or text[open_idx] != open_char:
        return -1

    depth = 0
    in_string = False
    in_single_quote = False
    in_template = False
    in_single_comment = False
    in_multi_comment = False
    escape = False

    i = open_idx
    n = len(text)

    while i < n:
        c = text[i]

        # Comments handling
        if not in_string and not in_single_quote and not in_template:
            if not in_multi_comment and not in_single_comment:
                if c == '/' and i + 1 < n:
                    if text[i + 1] == '/':
                        in_single_comment = True
                        i += 2
                        continue
                    elif text[i + 1] == '*':
                        in_multi_comment = True
                        i += 2
                        continue
            elif in_single_comment:
                if c == '\n':
                    in_single_comment = False
                i += 1
                continue
            elif in_multi_comment:
                if c == '*' and i + 1 < n and text[i + 1] == '/':
                    in_multi_comment = False
                    i += 2
                    continue
                i += 1
                continue

        # String literals handling
        if not in_single_comment and not in_multi_comment:
            if c == '"' and not in_single_quote and not in_template and not escape:
                in_string = not in_string
            elif c == "'" and not in_string and not in_template and not escape:
                in_single_quote = not in_single_quote
            elif c == '`' and not in_string and not in_single_quote and not escape:
                in_template = not in_template

            if c == '\\' and not escape:
                escape = True
            else:
                escape = False

            if not in_string and not in_single_quote and not in_template:
                if c == open_char:
                    depth += 1
                elif c == close_char:
                    depth -= 1
                    if depth == 0:
                        return i
        i += 1

    return -1


# ---------------------------------------------------------------------------
# Data Models
# ---------------------------------------------------------------------------

@dataclass
class ContractReference:
    """
    Represents a cross-stack contract reference (LiveKit event, RTDB path, or REST route).
    """
    type: str  # "livekit_event" | "rtdb_path" | "api_route"
    name: str  # e.g. "SET_ACTIVE_QUEST", "pairing_codes", "/api/livekit-token"
    line: int
    context: str = ""  # symbol or snippet context
    direction: str = "unknown"  # "publisher" | "subscriber" | "writer" | "reader" | "caller" | "handler"

    def to_dict(self) -> Dict[str, Any]:
        return dataclasses.asdict(self)

    @classmethod
    def from_dict(cls, data: Dict[str, Any]) -> ContractReference:
        return cls(**data)


@dataclass
class Symbol:
    """
    Unified cross-language symbol model.
    """
    id: str  # Unique deterministic identifier: e.g. "csharp:Assets/.../VoiceQuest.cs:VoiceQuest"
    name: str
    kind: str  # "class" | "interface" | "struct" | "enum" | "method" | "constructor" | "function" | "async_function" | "tool" | "property" | "event" | "server_action" | "hook" | "api_route" | "component" | "type" | "enum_member"
    file_path: str
    line_start: int
    line_end: int
    docstring: str = ""
    signature: str = ""
    parent_id: Optional[str] = None
    language: str = "unknown"  # "csharp" | "python" | "typescript"
    modifiers: List[str] = field(default_factory=list)
    dependencies: List[str] = field(default_factory=list)
    cross_stack_refs: List[ContractReference] = field(default_factory=list)

    def to_dict(self) -> Dict[str, Any]:
        d = dataclasses.asdict(self)
        d["cross_stack_refs"] = [ref.to_dict() if isinstance(ref, ContractReference) else ref for ref in self.cross_stack_refs]
        return d

    @classmethod
    def from_dict(cls, data: Dict[str, Any]) -> Symbol:
        d = dict(data)
        refs = d.get("cross_stack_refs", [])
        d["cross_stack_refs"] = [ContractReference.from_dict(r) if isinstance(r, dict) else r for r in refs]
        return cls(**d)


# ---------------------------------------------------------------------------
# Python AST Parser
# ---------------------------------------------------------------------------

class PythonASTParser:
    """
    Extracts classes, functions, async functions, decorators, docstrings,
    dependencies, and cross-stack contracts from Python source files.
    """

    def __init__(self):
        self.language = "python"

    def parse_source(self, code: str, file_path: str) -> List[Symbol]:
        norm_path = normalize_path(file_path)
        symbols: List[Symbol] = []

        try:
            tree = ast.parse(code, filename=norm_path)
        except SyntaxError as e:
            logger.warning(f"SyntaxError parsing Python file {norm_path}: {e}")
            return symbols

        lines = code.splitlines()

        # Extract file-level contract references
        file_contract_refs = self._extract_contract_references(code, lines)

        # Visitor to collect symbols
        class SymbolVisitor(ast.NodeVisitor):
            def __init__(self, parser: PythonASTParser):
                self.parser = parser
                self.current_parent_id: Optional[str] = None
                self.class_stack: List[str] = []

            def visit_ClassDef(self, node: ast.ClassDef):
                class_name = node.name
                parent_id = self.current_parent_id
                symbol_id = f"python:{norm_path}:{class_name}"
                if self.class_stack:
                    symbol_id = f"python:{norm_path}:{'.'.join(self.class_stack)}.{class_name}"

                doc = ast.get_docstring(node) or ""
                bases = [self.parser._format_expr(b) for b in node.bases]
                decorators = [self.parser._format_expr(d) for d in node.decorator_list]

                sig = f"class {class_name}"
                if bases:
                    sig += f"({', '.join(bases)})"

                line_start = node.lineno
                line_end = getattr(node, "end_lineno", node.lineno)

                # Find contract refs in this node's line span
                node_refs = [
                    ref for ref in file_contract_refs
                    if line_start <= ref.line <= line_end
                ]

                # Class dependencies (bases + decorators)
                deps = list(bases) + [d.lstrip("@") for d in decorators]

                sym = Symbol(
                    id=symbol_id,
                    name=class_name,
                    kind="class",
                    file_path=norm_path,
                    line_start=line_start,
                    line_end=line_end,
                    docstring=doc.strip(),
                    signature=sig,
                    parent_id=parent_id,
                    language="python",
                    modifiers=decorators,
                    dependencies=deps,
                    cross_stack_refs=node_refs,
                )
                symbols.append(sym)

                # Traverse body with updated parent
                prev_parent = self.current_parent_id
                self.current_parent_id = symbol_id
                self.class_stack.append(class_name)
                self.generic_visit(node)
                self.class_stack.pop()
                self.current_parent_id = prev_parent

            def visit_FunctionDef(self, node: ast.FunctionDef):
                self._handle_function(node, is_async=False)

            def visit_AsyncFunctionDef(self, node: ast.AsyncFunctionDef):
                self._handle_function(node, is_async=True)

            def _handle_function(self, node: Union[ast.FunctionDef, ast.AsyncFunctionDef], is_async: bool):
                fn_name = node.name
                parent_id = self.current_parent_id
                prefix = f"{'.'.join(self.class_stack)}." if self.class_stack else ""
                symbol_id = f"python:{norm_path}:{prefix}{fn_name}"

                doc = ast.get_docstring(node) or ""
                decorators = [self.parser._format_expr(d) for d in node.decorator_list]

                # Check for tool decorator e.g. @llm.function_tool
                kind = "async_function" if is_async else "function"
                if any("function_tool" in d or "tool" in d for d in decorators):
                    kind = "tool"
                elif self.class_stack:
                    kind = "method"

                sig = self.parser._format_func_sig(node, is_async=is_async)
                line_start = node.lineno
                line_end = getattr(node, "end_lineno", node.lineno)

                # Extract calls inside function
                called_funcs = self.parser._extract_calls(node)
                deps = list(called_funcs)

                # Node contract references
                node_refs = [
                    ref for ref in file_contract_refs
                    if line_start <= ref.line <= line_end
                ]

                for ref in node_refs:
                    if not ref.context:
                        ref.context = f"{fn_name}()"

                sym = Symbol(
                    id=symbol_id,
                    name=fn_name,
                    kind=kind,
                    file_path=norm_path,
                    line_start=line_start,
                    line_end=line_end,
                    docstring=doc.strip(),
                    signature=sig,
                    parent_id=parent_id,
                    language="python",
                    modifiers=(["async"] if is_async else []) + decorators,
                    dependencies=deps,
                    cross_stack_refs=node_refs,
                )
                symbols.append(sym)

                # Visit inner nodes (nested functions/classes)
                prev_parent = self.current_parent_id
                self.current_parent_id = symbol_id
                self.generic_visit(node)
                self.current_parent_id = prev_parent

        visitor = SymbolVisitor(self)
        visitor.visit(tree)

        return symbols

    def _extract_contract_references(self, code: str, lines: List[str]) -> List[ContractReference]:
        refs: List[ContractReference] = []

        for idx, line in enumerate(lines, start=1):
            stripped = line.strip()

            # 1. LiveKit events
            for event in LIVEKIT_EVENTS:
                if f'"{event}"' in line or f"'{event}'" in line:
                    direction = self._classify_livekit_direction(line, lines, idx)

                    refs.append(ContractReference(
                        type="livekit_event",
                        name=event,
                        line=idx,
                        context=stripped[:100],
                        direction=direction,
                    ))

            # 2. RTDB paths
            for path_pat in RTDB_PATH_PATTERNS:
                if f'"{path_pat}' in line or f"'{path_pat}" in line or f'`{path_pat}' in line:
                    direction = "reader"
                    write_kws = ["set", "push", "update", "write", "remove"]
                    read_kws = ["get", "on", "listen", "read", "subscribe"]

                    if any(re.search(rf'\b{w}\b', line) for w in write_kws):
                        direction = "writer"
                    elif any(re.search(rf'\b{r}\b', line) for r in read_kws):
                        direction = "reader"

                    refs.append(ContractReference(
                        type="rtdb_path",
                        name=path_pat,
                        line=idx,
                        context=stripped[:100],
                        direction=direction,
                    ))

            # 3. REST API routes
            for route in REST_API_ROUTES:
                if route in line:
                    refs.append(ContractReference(
                        type="api_route",
                        name=route,
                        line=idx,
                        context=stripped[:100],
                        direction="caller",
                    ))

        return refs

    def _classify_livekit_direction(self, line: str, lines: List[str], line_idx: int) -> str:
        # Check direct comparison or listener pattern on this line
        if any(k in line for k in ["==", "!=", "case ", "in ", ".get("]):
            return "subscriber"
        if any(pub in line for pub in ["send_rtc_event", "publish_data", "send", "publish", "generate_and_send"]):
            return "publisher"
        if any(sub in line for sub in ["data_received", "on_data_received", "on_message"]):
            return "subscriber"

        # Search upwards for enclosing function
        for i in range(line_idx - 1, max(-1, line_idx - 30), -1):
            l = lines[i]
            m = re.match(r'^\s*(?:async\s+)?def\s+([A-Za-z0-9_]+)\s*\(', l)
            if m:
                fn_name = m.group(1)
                if fn_name.startswith("on_") or fn_name.startswith("handle_") or "recv" in fn_name or "receive" in fn_name:
                    return "subscriber"
                if fn_name.startswith("send_") or fn_name.startswith("publish_") or "complete_quest" in fn_name or "make_complete" in fn_name:
                    return "publisher"
                break
            if re.match(r'^\s*class\s+', l):
                break

        return "subscriber"

    def _extract_calls(self, node: ast.AST) -> Set[str]:
        calls = set()
        for sub in ast.walk(node):
            if isinstance(sub, ast.Call):
                name = self._format_expr(sub.func)
                if name:
                    calls.add(name)
        return calls

    def _format_expr(self, node: Optional[ast.AST]) -> str:
        if node is None:
            return ""
        if isinstance(node, ast.Name):
            return node.id
        elif isinstance(node, ast.Attribute):
            val = self._format_expr(node.value)
            return f"{val}.{node.attr}" if val else node.attr
        elif isinstance(node, ast.Constant):
            return repr(node.value)
        elif isinstance(node, ast.Call):
            func_name = self._format_expr(node.func)
            return f"{func_name}(...)"
        elif isinstance(node, ast.Subscript):
            val = self._format_expr(node.value)
            sl = self._format_expr(node.slice)
            return f"{val}[{sl}]"
        elif isinstance(node, ast.Tuple):
            return ", ".join(self._format_expr(e) for e in node.elts)
        return ""

    def _format_func_sig(self, node: Union[ast.FunctionDef, ast.AsyncFunctionDef], is_async: bool) -> str:
        parts = []
        if is_async:
            parts.append("async def")
        else:
            parts.append("def")
        parts.append(node.name)

        arg_strs = []
        for arg in node.args.args:
            arg_str = arg.arg
            if arg.annotation:
                arg_str += f": {self._format_expr(arg.annotation)}"
            arg_strs.append(arg_str)

        ret_annot = ""
        if node.returns:
            ret_annot = f" -> {self._format_expr(node.returns)}"

        return f"{' '.join(parts)}({', '.join(arg_strs)}){ret_annot}"


# ---------------------------------------------------------------------------
# C# AST Parser (High-Precision Lexer & Pure Python Parser)
# ---------------------------------------------------------------------------

class CSharpASTParser:
    """
    Robust pure-Python token/lexer parser for C# source code.
    Extracts namespaces, classes, interfaces, structs, enums, methods, constructors, properties,
    events, fields, inheritance, and cross-stack contracts (LiveKit, Firebase RTDB, Firestore).
    """

    def __init__(self):
        self.language = "csharp"

    def parse_source(self, code: str, file_path: str) -> List[Symbol]:
        norm_path = normalize_path(file_path)
        symbols: List[Symbol] = []
        lines = code.splitlines()

        # Extract file contract references
        file_contract_refs = self._extract_contract_references(code, lines)

        # 1. Parse namespace
        current_namespace = ""
        ns_match = re.search(r'\bnamespace\s+([A-Za-z0-9_.]+)', code)
        if ns_match:
            current_namespace = ns_match.group(1)

        # 2. Tokenize and extract type declarations (class, interface, struct, enum, record)
        type_regex = re.compile(
            r'(?P<doc>(?:[ \t]*///[^\n]*\n|[ \t]*/\*[\s\S]*?\*/\n)*)'
            r'(?P<attrs>(?:[ \t]*\[[^\]]+\]\s*)*)'
            r'(?P<modifiers>(?:public|private|protected|internal|abstract|static|sealed|partial|readonly|\s)+)?'
            r'\b(?P<type_kind>class|interface|struct|enum|record)\s+'
            r'(?P<name>[A-Za-z0-9_]+)'
            r'(?:<(?P<generics>[^>]+)>)?'
            r'(?:\s*:\s*(?P<bases>[A-Za-z0-9_.,\s<>]+))?'
            r'(?:\s*where\s+[^{]+)?'
            r'\s*\{',
            re.MULTILINE
        )

        for match in type_regex.finditer(code):
            start_pos = match.start()
            line_start = code.count('\n', 0, start_pos) + 1

            type_kind = match.group('type_kind')
            name = match.group('name')
            generics = match.group('generics') or ""
            bases_str = match.group('bases') or ""
            raw_mods = match.group('modifiers') or ""
            raw_doc = match.group('doc') or ""
            raw_attrs = match.group('attrs') or ""

            # Calculate line_end using brace matching
            brace_pos = match.end() - 1  # points to '{'
            end_pos = self._find_matching_brace(code, brace_pos)
            line_end = code.count('\n', 0, end_pos) + 1 if end_pos != -1 else line_start

            modifiers = [m for m in raw_mods.strip().split() if m in {
                "public", "private", "protected", "internal", "abstract", "static", "sealed", "partial", "readonly"
            }]
            if raw_attrs:
                attr_matches = re.findall(r'\[([A-Za-z0-9_]+)', raw_attrs)
                modifiers.extend([f"[{a}]" for a in attr_matches])

            docstring = self._clean_docstring(raw_doc)

            # Bases & Interfaces
            bases = [b.strip() for b in bases_str.split(',') if b.strip()]

            symbol_id = f"csharp:{norm_path}:{name}"

            sig_parts = []
            if modifiers:
                sig_parts.append(" ".join(m for m in modifiers if not m.startswith("[")))
            sig_parts.append(f"{type_kind} {name}")
            if generics:
                sig_parts.append(f"<{generics}>")
            if bases:
                sig_parts.append(f": {', '.join(bases)}")
            signature = " ".join(sig_parts)

            type_refs = [
                ref for ref in file_contract_refs
                if line_start <= ref.line <= line_end
            ]

            type_sym = Symbol(
                id=symbol_id,
                name=name,
                kind=type_kind,
                file_path=norm_path,
                line_start=line_start,
                line_end=line_end,
                docstring=docstring,
                signature=signature,
                parent_id=None,
                language="csharp",
                modifiers=modifiers,
                dependencies=bases,
                cross_stack_refs=type_refs,
            )
            symbols.append(type_sym)

            # Parse members inside this type body
            if end_pos != -1 and brace_pos < end_pos:
                body_code = code[brace_pos + 1:end_pos]
                body_offset = brace_pos + 1

                if type_kind == "enum":
                    enum_members = self._parse_enum_members(
                        body_code, body_offset, code, norm_path, symbol_id
                    )
                    symbols.extend(enum_members)
                else:
                    member_symbols = self._parse_csharp_members(
                        body_code, body_offset, code, norm_path, symbol_id, name, file_contract_refs
                    )
                    symbols.extend(member_symbols)

        return symbols

    def _parse_enum_members(
        self,
        body_code: str,
        body_offset: int,
        full_code: str,
        norm_path: str,
        parent_id: str
    ) -> List[Symbol]:
        members: List[Symbol] = []
        for m in re.finditer(r'(?:^|[,\s])(?P<name>[A-Za-z0-9_]+)(?:\s*=\s*(?P<val>[^,}\n]+))?', body_code):
            name = m.group('name')
            if not name or name in {"public", "private", "internal", "enum"}:
                continue
            abs_start = body_offset + m.start()
            line = full_code.count('\n', 0, abs_start) + 1
            members.append(Symbol(
                id=f"{parent_id}.{name}",
                name=name,
                kind="enum_member",
                file_path=norm_path,
                line_start=line,
                line_end=line,
                signature=name,
                parent_id=parent_id,
                language="csharp",
                modifiers=[],
                dependencies=[],
                cross_stack_refs=[],
            ))
        return members

    def _parse_csharp_members(
        self,
        body_code: str,
        body_offset: int,
        full_code: str,
        norm_path: str,
        parent_id: str,
        enclosing_class_name: str,
        file_contract_refs: List[ContractReference]
    ) -> List[Symbol]:
        members: List[Symbol] = []

        # 1. Parse Constructors
        ctor_regex = re.compile(
            r'(?P<doc>(?:[ \t]*///[^\n]*\n|[ \t]*/\*[\s\S]*?\*/\n)*)'
            r'(?P<modifiers>(?:public|private|protected|internal|static|\s)+)'
            r'\b(?P<name>' + re.escape(enclosing_class_name) + r')\s*'
            r'\((?P<params>[^\)]*)\)'
            r'(?:\s*:\s*(?:base|this)\([^\)]*\))?'
            r'\s*\{',
            re.MULTILINE
        )

        for ctor in ctor_regex.finditer(body_code):
            abs_start = body_offset + ctor.start()
            line_start = full_code.count('\n', 0, abs_start) + 1
            name = ctor.group('name')
            raw_mods = ctor.group('modifiers') or ""
            raw_params = ctor.group('params') or ""

            brace_pos = body_offset + ctor.end() - 1
            end_pos = self._find_matching_brace(full_code, brace_pos)
            line_end = full_code.count('\n', 0, end_pos) + 1 if end_pos != -1 else line_start

            modifiers = [mod for mod in raw_mods.strip().split() if mod in {
                "public", "private", "protected", "internal", "static"
            }]

            ctor_refs = [
                ref for ref in file_contract_refs
                if line_start <= ref.line <= line_end
            ]

            members.append(Symbol(
                id=f"{parent_id}.{name}",
                name=name,
                kind="constructor",
                file_path=norm_path,
                line_start=line_start,
                line_end=line_end,
                docstring=self._clean_docstring(ctor.group('doc') or ""),
                signature=f"{' '.join(modifiers)} {name}({raw_params.strip()})".strip(),
                parent_id=parent_id,
                language="csharp",
                modifiers=modifiers,
                dependencies=[],
                cross_stack_refs=ctor_refs,
            ))

        # 2. Parse Methods
        method_regex = re.compile(
            r'(?P<doc>(?:[ \t]*///[^\n]*\n|[ \t]*/\*[\s\S]*?\*/\n)*)'
            r'(?P<attrs>(?:[ \t]*\[[^\]]+\]\s*)*)'
            r'(?P<modifiers>(?:public|private|protected|internal|abstract|static|virtual|override|async|sealed|extern|\s)+)'
            r'(?P<ret_type>(?:[A-Za-z0-9_<>\[\],\s\?]|Task(?:<[^>]+>)?|IEnumerator)+?)\s+'
            r'(?P<name>[A-Za-z0-9_]+)'
            r'(?:<(?P<generics>[^>]+)>)?'
            r'\s*\((?P<params>[^\)]*)\)'
            r'(?:\s*where\s+[^{;]+)?'
            r'(?P<body_delim>\s*\{|\s*=>)',
            re.MULTILINE
        )

        for m in method_regex.finditer(body_code):
            name = m.group('name')
            if name == enclosing_class_name:
                continue
            if name in {"if", "while", "for", "foreach", "switch", "catch", "using", "lock", "return"}:
                continue

            abs_start = body_offset + m.start()
            line_start = full_code.count('\n', 0, abs_start) + 1

            ret_type = m.group('ret_type').strip()
            raw_mods = m.group('modifiers') or ""
            raw_params = m.group('params') or ""
            raw_doc = m.group('doc') or ""
            body_delim = m.group('body_delim').strip()

            modifiers = [mod for mod in raw_mods.strip().split() if mod in {
                "public", "private", "protected", "internal", "abstract", "static", "virtual", "override", "async", "sealed"
            }]

            docstring = self._clean_docstring(raw_doc)

            # Determine method end line & body text
            body_text = ""
            if body_delim == "=>":
                semi_idx = body_code.find(';', m.end())
                if semi_idx != -1:
                    abs_end = body_offset + semi_idx
                    line_end = full_code.count('\n', 0, abs_end) + 1
                    body_text = body_code[m.end():semi_idx]
                else:
                    line_end = line_start
            else:
                brace_pos = body_offset + m.end() - 1
                end_pos = self._find_matching_brace(full_code, brace_pos)
                line_end = full_code.count('\n', 0, end_pos) + 1 if end_pos != -1 else line_start
                if end_pos != -1 and brace_pos < end_pos:
                    body_text = full_code[brace_pos + 1:end_pos]

            method_id = f"{parent_id}.{name}"
            sig_mods = " ".join(modifiers)
            signature = f"{sig_mods} {ret_type} {name}({raw_params.strip()})".strip()

            method_refs = [
                ref for ref in file_contract_refs
                if line_start <= ref.line <= line_end
            ]

            deps = []
            if ret_type and ret_type not in {"void", "Task", "bool", "int", "float", "string", "double"}:
                deps.append(ret_type.replace("<", "").replace(">", "").strip())

            # Extract calls and type references inside method body
            if body_text:
                call_matches = re.findall(r'\b([A-Za-z0-9_]+)\s*(?:\.([A-Za-z0-9_]+))?\s*\(', body_text)
                for obj_or_cls, fn in call_matches:
                    if obj_or_cls and obj_or_cls not in {
                        "if", "while", "for", "foreach", "switch", "catch", "using", "lock", "return", "sizeof", "typeof", "nameof"
                    }:
                        deps.append(obj_or_cls)
                    if fn:
                        deps.append(fn)

                type_matches = re.findall(r'\b([A-Z][A-Za-z0-9_]+)\b', body_text)
                for t in type_matches:
                    if t not in {
                        "Debug", "Math", "String", "Int32", "Boolean", "Object", "GameObject", "Transform",
                        "Vector3", "Quaternion", "Action", "List", "Dictionary", "Array", "Task", "AudioSource"
                    }:
                        deps.append(t)

            sym = Symbol(
                id=method_id,
                name=name,
                kind="method",
                file_path=norm_path,
                line_start=line_start,
                line_end=line_end,
                docstring=docstring,
                signature=signature,
                parent_id=parent_id,
                language="csharp",
                modifiers=modifiers,
                dependencies=list(set(deps)),
                cross_stack_refs=method_refs,
            )
            members.append(sym)

        # 3. Parse Properties
        prop_regex = re.compile(
            r'(?P<doc>(?:[ \t]*///[^\n]*\n|[ \t]*/\*[\s\S]*?\*/\n)*)'
            r'(?P<attrs>(?:[ \t]*\[[^\]]+\]\s*)*)'
            r'(?P<modifiers>(?:public|private|protected|internal|abstract|static|virtual|override|sealed|\s)+)'
            r'(?P<type>(?:[A-Za-z0-9_<>\[\],\s\?]|Dictionary<[^>]+>|List<[^>]+>)+?)\s+'
            r'(?P<name>[A-Za-z0-9_]+)\s*'
            r'(?P<accessor>\{|\=\>)',
            re.MULTILINE
        )

        for p in prop_regex.finditer(body_code):
            name = p.group('name')
            if any(m.name == name for m in members):
                continue
            if name in {"if", "while", "for", "switch", "get", "set"}:
                continue

            abs_start = body_offset + p.start()
            line_start = full_code.count('\n', 0, abs_start) + 1
            raw_mods = p.group('modifiers') or ""
            type_str = p.group('type').strip()
            accessor = p.group('accessor')

            modifiers = [mod for mod in raw_mods.strip().split() if mod in {
                "public", "private", "protected", "internal", "abstract", "static", "virtual", "override", "sealed"
            }]

            if accessor == "=>":
                semi_idx = body_code.find(';', p.end())
                abs_end = body_offset + semi_idx if semi_idx != -1 else abs_start
            else:
                brace_pos = body_offset + p.end() - 1
                end_pos = self._find_matching_brace(full_code, brace_pos)
                abs_end = end_pos if end_pos != -1 else abs_start

            line_end = full_code.count('\n', 0, abs_end) + 1
            prop_id = f"{parent_id}.{name}"
            sig_mods = " ".join(modifiers)
            signature = f"{sig_mods} {type_str} {name}".strip()

            prop_refs = [
                ref for ref in file_contract_refs
                if line_start <= ref.line <= line_end
            ]

            sym = Symbol(
                id=prop_id,
                name=name,
                kind="property",
                file_path=norm_path,
                line_start=line_start,
                line_end=line_end,
                docstring=self._clean_docstring(p.group('doc') or ""),
                signature=signature,
                parent_id=parent_id,
                language="csharp",
                modifiers=modifiers,
                dependencies=[type_str] if type_str else [],
                cross_stack_refs=prop_refs,
            )
            members.append(sym)

        # 4. Parse Events
        event_regex = re.compile(
            r'(?P<modifiers>(?:public|private|protected|internal|static|\s)+)?'
            r'\bevent\s+(?P<type>[A-Za-z0-9_<>,\s\?]+?)\s+'
            r'(?P<name>[A-Za-z0-9_]+)\s*;',
            re.MULTILINE
        )

        for ev in event_regex.finditer(body_code):
            abs_start = body_offset + ev.start()
            line_start = full_code.count('\n', 0, abs_start) + 1
            name = ev.group('name')
            type_str = ev.group('type').strip()
            raw_mods = ev.group('modifiers') or ""

            modifiers = [m for m in raw_mods.strip().split() if m in {"public", "private", "protected", "internal", "static"}]

            sym = Symbol(
                id=f"{parent_id}.{name}",
                name=name,
                kind="event",
                file_path=norm_path,
                line_start=line_start,
                line_end=line_start,
                signature=f"{' '.join(modifiers)} event {type_str} {name};".strip(),
                parent_id=parent_id,
                language="csharp",
                modifiers=modifiers,
                dependencies=[type_str],
                cross_stack_refs=[],
            )
            members.append(sym)

        return members

    def _extract_contract_references(self, code: str, lines: List[str]) -> List[ContractReference]:
        refs: List[ContractReference] = []

        for idx, line in enumerate(lines, start=1):
            stripped = line.strip()

            # 1. LiveKit events
            for event in LIVEKIT_EVENTS:
                if f'"{event}"' in line or f'@{event}' in line or (f'DataPacketEvent' in line and event in line):
                    direction = self._classify_csharp_livekit_direction(line, lines, idx)

                    refs.append(ContractReference(
                        type="livekit_event",
                        name=event,
                        line=idx,
                        context=stripped[:100],
                        direction=direction,
                    ))

            # 2. RTDB paths
            for path_pat in RTDB_PATH_PATTERNS:
                if f'"{path_pat}' in line or f'/{path_pat}' in line or f'FirebasePaths.{path_pat.capitalize()}' in line:
                    direction = "reader"
                    write_keywords = ["SetValueAsync", "UpdateChildrenAsync", "Push", "GenerateAndPushPIN", "PushAggregatedSnapshot", "SetRawJsonValueAsync"]
                    read_keywords = ["ValueChanged", "ChildAdded", "StartListening", "GetValueAsync", "ListenForSession"]

                    if any(w in line for w in write_keywords):
                        direction = "writer"
                    elif any(r in line for r in read_keywords):
                        direction = "reader"

                    refs.append(ContractReference(
                        type="rtdb_path",
                        name=path_pat,
                        line=idx,
                        context=stripped[:100],
                        direction=direction,
                    ))

            # 3. REST API routes
            for route in REST_API_ROUTES:
                if route in line:
                    refs.append(ContractReference(
                        type="api_route",
                        name=route,
                        line=idx,
                        context=stripped[:100],
                        direction="caller",
                    ))

        return refs

    def _classify_csharp_livekit_direction(self, line: str, lines: List[str], line_idx: int) -> str:
        # Check direct comparison or listener pattern on this line
        if any(k in line for k in ["==", "!=", "case ", "is ", "switch "]):
            return "subscriber"
        if any(sub in line for sub in ["OnDataReceived", "DataReceived", "OnSpeechMatched", "OnAgentError", "OnQuestStatusUpdate", "HandleSpeechMatched", "Subscribe"]):
            return "subscriber"
        if any(pub in line for pub in ["PublishData", "SendActiveQuest", "SendVerbalHint", "SendOnReminder", "SendLiveSession", "publish", "Publish", "sendDataPacket"]):
            return "publisher"

        # Search upwards for enclosing method declaration
        for i in range(line_idx - 1, max(-1, line_idx - 30), -1):
            l = lines[i]
            if re.search(r'\b(public|private|protected|internal|void|Task|async)\s+([A-Za-z0-9_]+)\s*\(', l):
                m = re.search(r'\b([A-Za-z0-9_]+)\s*\(', l)
                if m:
                    enclosing_method = m.group(1)
                    if enclosing_method.startswith("Send") or enclosing_method.startswith("Publish"):
                        return "publisher"
                    if enclosing_method.startswith("On") or enclosing_method.startswith("Handle") or "Receive" in enclosing_method:
                        return "subscriber"
                    break
            if "class " in l or "struct " in l or "interface " in l:
                break

        # Check subsequent lines within the method for PublishData
        for i in range(line_idx, min(len(lines), line_idx + 10)):
            l = lines[i]
            if "PublishData" in l or "Publish(" in l:
                return "publisher"
            if "}" in l and ("private" in l or "public" in l):
                break

        return "subscriber"

    def _find_matching_brace(self, text: str, open_brace_idx: int) -> int:
        return find_matching_delimiter(text, open_brace_idx, '{', '}')

    def _clean_docstring(self, raw: str) -> str:
        if not raw:
            return ""
        lines = []
        for line in raw.splitlines():
            cleaned = re.sub(r'^[ \t]*///\s?', '', line)
            cleaned = re.sub(r'^[ \t]*/\*\*?\s?', '', cleaned)
            cleaned = re.sub(r'\s*\*/$', '', cleaned)
            cleaned = re.sub(r'^[ \t]*\*\s?', '', cleaned)
            cleaned = re.sub(r'<[^>]+>', '', cleaned).strip()
            if cleaned:
                lines.append(cleaned)
        return "\n".join(lines).strip()


# ---------------------------------------------------------------------------
# TypeScript / TSX AST Parser
# ---------------------------------------------------------------------------

class TypeScriptASTParser:
    """
    Robust pure-Python token/lexer parser for TypeScript and TSX files.
    Extracts interfaces, types, classes, enums, functions, Server Actions, React hooks,
    API routes, and cross-stack contracts (LiveKit, Firebase RTDB, REST APIs).
    Uses balanced delimiter scanners for nested parentheses and generics.
    """

    def __init__(self):
        self.language = "typescript"

    def _find_matching_paren(self, text: str, open_paren_idx: int) -> int:
        return find_matching_delimiter(text, open_paren_idx, '(', ')')

    def _find_matching_brace(self, text: str, open_brace_idx: int) -> int:
        return find_matching_delimiter(text, open_brace_idx, '{', '}')

    def _find_matching_bracket(self, text: str, open_bracket_idx: int, open_char: str = '<', close_char: str = '>') -> int:
        return find_matching_delimiter(text, open_bracket_idx, open_char, close_char)

    def parse_source(self, code: str, file_path: str) -> List[Symbol]:
        norm_path = normalize_path(file_path)
        symbols: List[Symbol] = []
        lines = code.splitlines()

        # Extract file contract references
        file_contract_refs = self._extract_contract_references(code, lines, norm_path)

        # Check for file-level "use server" or "use client"
        has_file_use_server = bool(re.search(r'^\s*["\']use server["\']', code, re.MULTILINE))
        is_api_route_file = "api/" in norm_path and (norm_path.endswith("route.ts") or norm_path.endswith("route.js"))

        # 1. Parse Interfaces (with balanced nested generics)
        interface_header_regex = re.compile(
            r'(?P<doc>(?:[ \t]*\/\*\*[\s\S]*?\*\/[ \t]*\n)*)'
            r'(?P<export>export\s+)?interface\s+(?P<name>[A-Za-z0-9_]+)\s*',
            re.MULTILINE
        )

        for match in interface_header_regex.finditer(code):
            start_pos = match.start()
            line_start = code.count('\n', 0, start_pos) + 1
            name = match.group('name')
            is_export = bool(match.group('export'))
            docstring = self._clean_jsdoc(match.group('doc') or "")

            pos = match.end()
            generics = ""
            if pos < len(code) and code[pos] == '<':
                end_gen = self._find_matching_bracket(code, pos, '<', '>')
                if end_gen != -1:
                    generics = code[pos + 1:end_gen].strip()
                    pos = end_gen + 1

            brace_pos = code.find('{', pos)
            if brace_pos == -1:
                continue

            extends_str = ""
            between = code[pos:brace_pos].strip()
            if between.startswith("extends"):
                extends_str = between[7:].strip()

            end_pos = self._find_matching_brace(code, brace_pos)
            line_end = code.count('\n', 0, end_pos) + 1 if end_pos != -1 else line_start

            deps = [e.strip() for e in extends_str.split(',') if e.strip()]
            symbol_id = f"typescript:{norm_path}:{name}"

            sig = f"{'export ' if is_export else ''}interface {name}"
            if generics:
                sig += f"<{generics}>"
            if extends_str:
                sig += f" extends {extends_str}"

            sym_refs = [ref for ref in file_contract_refs if line_start <= ref.line <= line_end]

            sym = Symbol(
                id=symbol_id,
                name=name,
                kind="interface",
                file_path=norm_path,
                line_start=line_start,
                line_end=line_end,
                docstring=docstring,
                signature=sig,
                parent_id=None,
                language="typescript",
                modifiers=["export"] if is_export else [],
                dependencies=deps,
                cross_stack_refs=sym_refs,
            )
            symbols.append(sym)

        # 2. Parse Type Aliases
        type_header_regex = re.compile(
            r'(?P<doc>(?:[ \t]*\/\*\*[\s\S]*?\*\/[ \t]*\n)*)'
            r'(?P<export>export\s+)?type\s+(?P<name>[A-Za-z0-9_]+)\s*',
            re.MULTILINE
        )

        for match in type_header_regex.finditer(code):
            start_pos = match.start()
            line_start = code.count('\n', 0, start_pos) + 1
            name = match.group('name')
            is_export = bool(match.group('export'))
            docstring = self._clean_jsdoc(match.group('doc') or "")

            pos = match.end()
            generics = ""
            if pos < len(code) and code[pos] == '<':
                end_gen = self._find_matching_bracket(code, pos, '<', '>')
                if end_gen != -1:
                    generics = code[pos + 1:end_gen].strip()
                    pos = end_gen + 1

            eq_pos = code.find('=', pos)
            if eq_pos == -1:
                continue

            semi_pos = code.find(';', eq_pos)
            if semi_pos != -1:
                definition = code[eq_pos + 1:semi_pos].strip()
                line_end = code.count('\n', 0, semi_pos) + 1
            else:
                definition = code[eq_pos + 1:eq_pos + 60].strip()
                line_end = line_start

            symbol_id = f"typescript:{norm_path}:{name}"
            sym_refs = [ref for ref in file_contract_refs if line_start <= ref.line <= line_end]

            sig = f"{'export ' if is_export else ''}type {name}{f'<{generics}>' if generics else ''} = {definition[:60]}"
            if len(definition) > 60:
                sig += "..."

            sym = Symbol(
                id=symbol_id,
                name=name,
                kind="type",
                file_path=norm_path,
                line_start=line_start,
                line_end=line_end,
                docstring=docstring,
                signature=sig,
                parent_id=None,
                language="typescript",
                modifiers=["export"] if is_export else [],
                dependencies=[],
                cross_stack_refs=sym_refs,
            )
            symbols.append(sym)

        # 3. Parse Classes
        class_header_regex = re.compile(
            r'(?P<doc>(?:[ \t]*\/\*\*[\s\S]*?\*\/[ \t]*\n)*)'
            r'(?P<export>export\s+)?'
            r'(?P<default>default\s+)?'
            r'(?P<abstract>abstract\s+)?'
            r'class\s+(?P<name>[A-Za-z0-9_]+)\s*',
            re.MULTILINE
        )

        for match in class_header_regex.finditer(code):
            start_pos = match.start()
            line_start = code.count('\n', 0, start_pos) + 1
            name = match.group('name')
            is_export = bool(match.group('export'))
            is_default = bool(match.group('default'))
            is_abstract = bool(match.group('abstract'))
            docstring = self._clean_jsdoc(match.group('doc') or "")

            pos = match.end()
            generics = ""
            if pos < len(code) and code[pos] == '<':
                end_gen = self._find_matching_bracket(code, pos, '<', '>')
                if end_gen != -1:
                    generics = code[pos + 1:end_gen].strip()
                    pos = end_gen + 1

            brace_pos = code.find('{', pos)
            if brace_pos == -1:
                continue

            heritage = code[pos:brace_pos].strip()
            bases: List[str] = []
            ext_match = re.search(r'\bextends\s+([A-Za-z0-9_.,\s<>]+?)(?:\s+implements|\s*$)', heritage)
            if ext_match:
                bases.extend([b.strip() for b in ext_match.group(1).split(',') if b.strip()])
            imp_match = re.search(r'\bimplements\s+([A-Za-z0-9_.,\s<>]+?)$', heritage)
            if imp_match:
                bases.extend([b.strip() for b in imp_match.group(1).split(',') if b.strip()])

            end_pos = self._find_matching_brace(code, brace_pos)
            line_end = code.count('\n', 0, end_pos) + 1 if end_pos != -1 else line_start

            symbol_id = f"typescript:{norm_path}:{name}"
            modifiers = []
            if is_export:
                modifiers.append("export")
            if is_default:
                modifiers.append("default")
            if is_abstract:
                modifiers.append("abstract")

            sig_parts = list(modifiers)
            sig_parts.append(f"class {name}")
            if generics:
                sig_parts.append(f"<{generics}>")
            if heritage:
                sig_parts.append(heritage)
            signature = " ".join(sig_parts)

            sym_refs = [ref for ref in file_contract_refs if line_start <= ref.line <= line_end]

            class_sym = Symbol(
                id=symbol_id,
                name=name,
                kind="class",
                file_path=norm_path,
                line_start=line_start,
                line_end=line_end,
                docstring=docstring,
                signature=signature,
                parent_id=None,
                language="typescript",
                modifiers=modifiers,
                dependencies=bases,
                cross_stack_refs=sym_refs,
            )
            symbols.append(class_sym)

            # Parse members inside class body
            if end_pos != -1 and brace_pos < end_pos:
                body_code = code[brace_pos + 1:end_pos]
                body_offset = brace_pos + 1
                class_members = self._parse_ts_class_members(
                    body_code, body_offset, code, norm_path, symbol_id, name, file_contract_refs
                )
                symbols.extend(class_members)

        # 4. Parse Enums
        enum_header_regex = re.compile(
            r'(?P<doc>(?:[ \t]*\/\*\*[\s\S]*?\*\/[ \t]*\n)*)'
            r'(?P<export>export\s+)?'
            r'(?P<const>const\s+)?'
            r'enum\s+(?P<name>[A-Za-z0-9_]+)\s*\{',
            re.MULTILINE
        )

        for match in enum_header_regex.finditer(code):
            start_pos = match.start()
            line_start = code.count('\n', 0, start_pos) + 1
            name = match.group('name')
            is_export = bool(match.group('export'))
            is_const = bool(match.group('const'))
            docstring = self._clean_jsdoc(match.group('doc') or "")

            brace_pos = match.end() - 1
            end_pos = self._find_matching_brace(code, brace_pos)
            line_end = code.count('\n', 0, end_pos) + 1 if end_pos != -1 else line_start

            symbol_id = f"typescript:{norm_path}:{name}"
            modifiers = []
            if is_export:
                modifiers.append("export")
            if is_const:
                modifiers.append("const")

            sig = f"{' '.join(modifiers)} enum {name}".strip()
            sym_refs = [ref for ref in file_contract_refs if line_start <= ref.line <= line_end]

            enum_sym = Symbol(
                id=symbol_id,
                name=name,
                kind="enum",
                file_path=norm_path,
                line_start=line_start,
                line_end=line_end,
                docstring=docstring,
                signature=sig,
                parent_id=None,
                language="typescript",
                modifiers=modifiers,
                dependencies=[],
                cross_stack_refs=sym_refs,
            )
            symbols.append(enum_sym)

            # Parse enum members
            if end_pos != -1 and brace_pos < end_pos:
                body_code = code[brace_pos + 1:end_pos]
                body_offset = brace_pos + 1
                for m in re.finditer(r'(?:^|[,\s])(?P<mname>[A-Za-z0-9_]+)(?:\s*=\s*(?P<val>[^,}\n]+))?', body_code):
                    mname = m.group('mname')
                    if not mname or mname in {"export", "const", "enum"}:
                        continue
                    m_abs = body_offset + m.start()
                    m_line = code.count('\n', 0, m_abs) + 1
                    symbols.append(Symbol(
                        id=f"{symbol_id}.{mname}",
                        name=mname,
                        kind="enum_member",
                        file_path=norm_path,
                        line_start=m_line,
                        line_end=m_line,
                        signature=mname,
                        parent_id=symbol_id,
                        language="typescript",
                        modifiers=[],
                        dependencies=[],
                        cross_stack_refs=[],
                    ))

        # 5. Parse Functions & Server Actions & API Routes
        func_header_regex = re.compile(
            r'(?P<doc>(?:[ \t]*\/\*\*[\s\S]*?\*\/[ \t]*\n)*)'
            r'(?P<export>export\s+)?'
            r'(?P<default>default\s+)?'
            r'(?P<async>async\s+)?'
            r'function(?:\s+(?P<name>[A-Za-z0-9_]+))\s*',
            re.MULTILINE
        )

        for match in func_header_regex.finditer(code):
            start_pos = match.start()
            line_start = code.count('\n', 0, start_pos) + 1

            name = match.group('name')
            is_export = bool(match.group('export'))
            is_default = bool(match.group('default'))
            is_async = bool(match.group('async'))
            docstring = self._clean_jsdoc(match.group('doc') or "")

            pos = match.end()
            generics = ""
            if pos < len(code) and code[pos] == '<':
                end_gen = self._find_matching_bracket(code, pos, '<', '>')
                if end_gen != -1:
                    generics = code[pos + 1:end_gen].strip()
                    pos = end_gen + 1

            while pos < len(code) and code[pos].isspace():
                pos += 1

            params = ""
            if pos < len(code) and code[pos] == '(':
                end_paren = self._find_matching_paren(code, pos)
                if end_paren != -1:
                    params = code[pos + 1:end_paren].strip()
                    pos = end_paren + 1

            brace_pos = code.find('{', pos)
            if brace_pos == -1:
                continue

            ret_type_str = code[pos:brace_pos].strip()
            ret_type = ret_type_str[1:].strip() if ret_type_str.startswith(':') else ""

            end_pos = self._find_matching_brace(code, brace_pos)
            line_end = code.count('\n', 0, end_pos) + 1 if end_pos != -1 else line_start

            body = code[brace_pos + 1:end_pos] if end_pos != -1 else ""
            has_func_use_server = bool(re.search(r'^\s*["\']use server["\']', body, re.MULTILINE))

            # Classify kind
            kind = "function"
            if is_api_route_file and name in {"GET", "POST", "PUT", "DELETE", "PATCH", "OPTIONS", "HEAD"}:
                kind = "api_route"
            elif (has_file_use_server or has_func_use_server) and is_export:
                kind = "server_action"
            elif name.startswith("use") and len(name) > 3 and name[3].isupper():
                kind = "hook"
            elif name[0].isupper() and (norm_path.endswith(".tsx") or norm_path.endswith(".jsx")):
                kind = "component"
            elif is_async:
                kind = "async_function"

            modifiers = []
            if is_export:
                modifiers.append("export")
            if is_default:
                modifiers.append("default")
            if is_async:
                modifiers.append("async")
            if has_file_use_server or has_func_use_server:
                modifiers.append("use_server")

            sig_parts = []
            if is_export:
                sig_parts.append("export")
            if is_default:
                sig_parts.append("default")
            if is_async:
                sig_parts.append("async")
            sig_parts.append(f"function {name}")
            if generics:
                sig_parts.append(f"<{generics}>")
            sig_parts.append(f"({params})")
            if ret_type:
                sig_parts.append(f": {ret_type}")
            signature = " ".join(sig_parts)

            sym_refs = [ref for ref in file_contract_refs if line_start <= ref.line <= line_end]
            if is_api_route_file and kind == "api_route":
                route_refs = [r for r in file_contract_refs if r.type == "api_route" and r.direction == "handler"]
                for r in route_refs:
                    if r not in sym_refs:
                        sym_refs.append(r)

            deps = self._extract_ts_deps(body)

            sym = Symbol(
                id=f"typescript:{norm_path}:{name}",
                name=name,
                kind=kind,
                file_path=norm_path,
                line_start=line_start,
                line_end=line_end,
                docstring=docstring,
                signature=signature,
                parent_id=None,
                language="typescript",
                modifiers=modifiers,
                dependencies=deps,
                cross_stack_refs=sym_refs,
            )
            symbols.append(sym)

        # 6. Parse Arrow Functions / Const Declarations
        arrow_header_regex = re.compile(
            r'(?P<doc>(?:[ \t]*\/\*\*[\s\S]*?\*\/[ \t]*\n)*)'
            r'(?P<export>export\s+)?'
            r'(?:const|let|var)\s+(?P<name>[A-Za-z0-9_]+)\s*=\s*'
            r'(?:(?P<hook_call>useCallback|useMemo)\s*\(\s*)?'
            r'(?P<async>async\s+)?',
            re.MULTILINE
        )

        for match in arrow_header_regex.finditer(code):
            name = match.group('name')
            if any(s.name == name for s in symbols):
                continue

            start_pos = match.start()
            line_start = code.count('\n', 0, start_pos) + 1

            is_export = bool(match.group('export'))
            is_async = bool(match.group('async'))
            docstring = self._clean_jsdoc(match.group('doc') or "")

            pos = match.end()
            while pos < len(code) and code[pos].isspace():
                pos += 1

            params = ""
            if pos < len(code) and code[pos] == '(':
                end_paren = self._find_matching_paren(code, pos)
                if end_paren != -1:
                    params = code[pos + 1:end_paren].strip()
                    pos = end_paren + 1
            else:
                id_m = re.match(r'([A-Za-z0-9_]+)', code[pos:])
                if id_m:
                    params = id_m.group(1)
                    pos += id_m.end()

            # Find '=>'
            arrow_idx = code.find('=>', pos)
            if arrow_idx == -1:
                continue

            pos = arrow_idx + 2
            while pos < len(code) and code[pos].isspace():
                pos += 1

            if pos < len(code) and code[pos] == '{':
                brace_pos = pos
                end_pos = self._find_matching_brace(code, brace_pos)
                line_end = code.count('\n', 0, end_pos) + 1 if end_pos != -1 else line_start
                body = code[brace_pos + 1:end_pos] if end_pos != -1 else ""
            else:
                semi_pos = code.find(';', pos)
                line_end = code.count('\n', 0, semi_pos) + 1 if semi_pos != -1 else line_start
                body = code[pos:semi_pos] if semi_pos != -1 else code[pos:pos+100]

            has_func_use_server = bool(re.search(r'^\s*["\']use server["\']', body, re.MULTILINE))

            kind = "function"
            if is_api_route_file and name in {"GET", "POST", "PUT", "DELETE", "PATCH"}:
                kind = "api_route"
            elif (has_file_use_server or has_func_use_server) and is_export:
                kind = "server_action"
            elif name.startswith("use") and len(name) > 3 and name[3].isupper():
                kind = "hook"
            elif name[0].isupper() and (norm_path.endswith(".tsx") or norm_path.endswith(".jsx")):
                kind = "component"
            elif is_async:
                kind = "async_function"

            modifiers = []
            if is_export:
                modifiers.append("export")
            if is_async:
                modifiers.append("async")

            sig = f"{'export ' if is_export else ''}const {name} = {'async ' if is_async else ''}({params}) => ..."

            sym_refs = [ref for ref in file_contract_refs if line_start <= ref.line <= line_end]
            if is_api_route_file and kind == "api_route":
                route_refs = [r for r in file_contract_refs if r.type == "api_route" and r.direction == "handler"]
                for r in route_refs:
                    if r not in sym_refs:
                        sym_refs.append(r)

            deps = self._extract_ts_deps(body)

            sym = Symbol(
                id=f"typescript:{norm_path}:{name}",
                name=name,
                kind=kind,
                file_path=norm_path,
                line_start=line_start,
                line_end=line_end,
                docstring=docstring,
                signature=sig,
                parent_id=None,
                language="typescript",
                modifiers=modifiers,
                dependencies=deps,
                cross_stack_refs=sym_refs,
            )
            symbols.append(sym)

        return symbols

    def _parse_ts_class_members(
        self,
        body_code: str,
        body_offset: int,
        full_code: str,
        norm_path: str,
        parent_id: str,
        enclosing_class_name: str,
        file_contract_refs: List[ContractReference]
    ) -> List[Symbol]:
        members: List[Symbol] = []

        # 1. Constructor
        ctor_match = re.search(r'\bconstructor\s*\(', body_code)
        if ctor_match:
            open_paren = body_offset + ctor_match.end() - 1
            end_paren = self._find_matching_paren(full_code, open_paren)
            params = full_code[open_paren + 1:end_paren].strip() if end_paren != -1 else ""

            brace_idx = full_code.find('{', end_paren if end_paren != -1 else open_paren)
            if brace_idx != -1:
                end_brace = self._find_matching_brace(full_code, brace_idx)
                line_start = full_code.count('\n', 0, body_offset + ctor_match.start()) + 1
                line_end = full_code.count('\n', 0, end_brace) + 1 if end_brace != -1 else line_start
                ctor_refs = [ref for ref in file_contract_refs if line_start <= ref.line <= line_end]

                members.append(Symbol(
                    id=f"{parent_id}.constructor",
                    name="constructor",
                    kind="constructor",
                    file_path=norm_path,
                    line_start=line_start,
                    line_end=line_end,
                    signature=f"constructor({params})",
                    parent_id=parent_id,
                    language="typescript",
                    modifiers=[],
                    dependencies=[],
                    cross_stack_refs=ctor_refs,
                ))

        # 2. Methods
        method_regex = re.compile(
            r'(?P<doc>(?:[ \t]*\/\*\*[\s\S]*?\*\/[ \t]*\n)*)'
            r'(?P<modifiers>(?:public|private|protected|static|async|override|readonly|\s)+)?'
            r'(?P<name>[A-Za-z0-9_]+)\s*'
            r'(?:<(?P<generics>[^>]+)>)?\s*\(',
            re.MULTILINE
        )

        for m in method_regex.finditer(body_code):
            name = m.group('name')
            if name in {"constructor", "if", "while", "for", "switch", "catch", "return"}:
                continue

            open_paren = body_offset + m.end() - 1
            end_paren = self._find_matching_paren(full_code, open_paren)
            params = full_code[open_paren + 1:end_paren].strip() if end_paren != -1 else ""

            scan_pos = end_paren + 1 if end_paren != -1 else open_paren + 1
            brace_idx = full_code.find('{', scan_pos)
            if brace_idx == -1:
                continue

            ret_str = full_code[scan_pos:brace_idx].strip()
            ret_type = ret_str[1:].strip() if ret_str.startswith(':') else ""

            end_brace = self._find_matching_brace(full_code, brace_idx)
            abs_start = body_offset + m.start()
            line_start = full_code.count('\n', 0, abs_start) + 1
            line_end = full_code.count('\n', 0, end_brace) + 1 if end_brace != -1 else line_start

            raw_mods = m.group('modifiers') or ""
            modifiers = [mod for mod in raw_mods.strip().split() if mod in {
                "public", "private", "protected", "static", "async", "override", "readonly"
            }]

            kind = "async_function" if "async" in modifiers else "method"
            sig_mods = " ".join(modifiers)
            signature = f"{sig_mods} {name}({params}){f': {ret_type}' if ret_type else ''}".strip()

            m_refs = [ref for ref in file_contract_refs if line_start <= ref.line <= line_end]

            members.append(Symbol(
                id=f"{parent_id}.{name}",
                name=name,
                kind=kind,
                file_path=norm_path,
                line_start=line_start,
                line_end=line_end,
                docstring=self._clean_jsdoc(m.group('doc') or ""),
                signature=signature,
                parent_id=parent_id,
                language="typescript",
                modifiers=modifiers,
                dependencies=[],
                cross_stack_refs=m_refs,
            ))

        # 3. Fields / Properties
        prop_regex = re.compile(
            r'(?P<doc>(?:[ \t]*\/\*\*[\s\S]*?\*\/[ \t]*\n)*)'
            r'(?P<modifiers>(?:public|private|protected|static|readonly|\s)+)'
            r'(?P<name>[A-Za-z0-9_]+)\s*'
            r'(?::\s*(?P<type>[^;=]+))?\s*'
            r'(?:=\s*(?P<val>[^;]+))?\s*;',
            re.MULTILINE
        )

        for p in prop_regex.finditer(body_code):
            name = p.group('name')
            if any(m.name == name for m in members):
                continue
            if name in {"constructor", "if", "while", "for", "switch"}:
                continue

            abs_start = body_offset + p.start()
            line_start = full_code.count('\n', 0, abs_start) + 1
            line_end = line_start

            raw_mods = p.group('modifiers') or ""
            modifiers = [mod for mod in raw_mods.strip().split() if mod in {
                "public", "private", "protected", "static", "readonly"
            }]

            type_str = (p.group('type') or "").strip()
            sig = f"{' '.join(modifiers)} {name}{f': {type_str}' if type_str else ''}".strip()

            p_refs = [ref for ref in file_contract_refs if line_start <= ref.line <= line_end]

            members.append(Symbol(
                id=f"{parent_id}.{name}",
                name=name,
                kind="property",
                file_path=norm_path,
                line_start=line_start,
                line_end=line_end,
                docstring=self._clean_jsdoc(p.group('doc') or ""),
                signature=sig,
                parent_id=parent_id,
                language="typescript",
                modifiers=modifiers,
                dependencies=[type_str] if type_str else [],
                cross_stack_refs=p_refs,
            ))

        return members

    def _extract_contract_references(self, code: str, lines: List[str], norm_path: str) -> List[ContractReference]:
        refs: List[ContractReference] = []

        # If this is an API route handler file (e.g. src/app/api/livekit-token/route.ts)
        if "api/" in norm_path and (norm_path.endswith("/route.ts") or norm_path.endswith("/route.js")):
            route_match = re.search(r'(?:src/app|app)(/api/[^/]+)/route\.[tj]s', norm_path)
            if route_match:
                route_path = route_match.group(1)
                refs.append(ContractReference(
                    type="api_route",
                    name=route_path,
                    line=1,
                    context=f"Route Handler {route_path}",
                    direction="handler",
                ))

        for idx, line in enumerate(lines, start=1):
            stripped = line.strip()

            # 1. LiveKit events
            for event in LIVEKIT_EVENTS:
                if f'"{event}"' in line or f"'{event}'" in line:
                    direction = self._classify_typescript_livekit_direction(line, lines, idx)

                    refs.append(ContractReference(
                        type="livekit_event",
                        name=event,
                        line=idx,
                        context=stripped[:100],
                        direction=direction,
                    ))

            # 2. RTDB paths
            for path_pat in RTDB_PATH_PATTERNS:
                if path_pat in line and ("ref(" in line or "`" in line or '"' in line or "'" in line):
                    direction = "reader"
                    write_kws = ["set(", "update(", "push(", "remove(", "pushRemoteCommand", "startLessonOnDevice"]
                    read_kws = ["get(", "onValue(", "onChildAdded(", "subscribeTo"]

                    if any(w in line for w in write_kws):
                        direction = "writer"
                    elif any(r in line for r in read_kws):
                        direction = "reader"

                    refs.append(ContractReference(
                        type="rtdb_path",
                        name=path_pat,
                        line=idx,
                        context=stripped[:100],
                        direction=direction,
                    ))

            # 3. REST API routes
            for route in REST_API_ROUTES:
                if route in line:
                    direction = "caller"
                    if "export async function" in line or "export function" in line:
                        direction = "handler"
                    refs.append(ContractReference(
                        type="api_route",
                        name=route,
                        line=idx,
                        context=stripped[:100],
                        direction=direction,
                    ))

        return refs

    def _classify_typescript_livekit_direction(self, line: str, lines: List[str], line_idx: int) -> str:
        # Check direct comparison or listener pattern on this line
        if any(k in line for k in ["===", "==", "!=", "case ", ".event ===", ".event =="]):
            return "subscriber"
        if any(pub in line for pub in ["publishData", "sendDataPacket", "sendVerbalHint", "sendSpeakScript"]):
            return "publisher"
        if any(sub in line for sub in ["handleData", "onQuestStatus", "DataReceived", "room.on", "addEventListener"]):
            return "subscriber"

        # Search upwards for enclosing function / hook / arrow
        for i in range(line_idx - 1, max(-1, line_idx - 30), -1):
            l = lines[i]
            m = re.search(r'(?:function\s+|(?:const|let|var)\s+)([A-Za-z0-9_]+)\s*=', l) or re.search(r'function\s+([A-Za-z0-9_]+)\s*\(', l)
            if m:
                enclosing = m.group(1)
                if enclosing.startswith("send") or enclosing.startswith("publish"):
                    return "publisher"
                if enclosing.startswith("on") or enclosing.startswith("handle") or "receive" in enclosing.lower():
                    return "subscriber"
                break

        return "subscriber"

    def _extract_ts_deps(self, body: str) -> List[str]:
        deps = set()
        call_matches = re.findall(r'\b([A-Za-z0-9_]+)\s*\(', body)
        for c in call_matches:
            if c not in {"if", "while", "for", "switch", "catch", "return", "console", "log", "error", "warn", "Boolean", "String", "Number", "Array", "Object", "Promise", "JSON", "encode", "decode", "map", "filter", "reduce", "find"}:
                deps.add(c)
        return sorted(list(deps))[:20]

    def _clean_jsdoc(self, raw: str) -> str:
        if not raw:
            return ""
        lines = []
        for line in raw.splitlines():
            cleaned = re.sub(r'^[ \t]*/\*\*?\s?', '', line)
            cleaned = re.sub(r'\s*\*/$', '', cleaned)
            cleaned = re.sub(r'^[ \t]*\*\s?', '', cleaned).strip()
            if cleaned and not cleaned.startswith("@"):
                lines.append(cleaned)
        return "\n".join(lines).strip()


# ---------------------------------------------------------------------------
# Unified AST Parser Manager
# ---------------------------------------------------------------------------

class ASTParserManager:
    """
    Unified manager orchestrating Python, C#, and TypeScript/TSX AST parsers.
    Handles directory crawling, ignore filters, symbol deduplication, and cross-stack references.
    """

    def __init__(
        self,
        custom_ignored_dirs: Optional[Set[str]] = None,
        custom_ignored_exts: Optional[Set[str]] = None,
    ):
        self.custom_ignored_dirs = custom_ignored_dirs or DEFAULT_IGNORED_DIRS
        self.custom_ignored_exts = custom_ignored_exts or DEFAULT_IGNORED_EXTS

        self.python_parser = PythonASTParser()
        self.csharp_parser = CSharpASTParser()
        self.typescript_parser = TypeScriptASTParser()

    def parse_file(self, file_path: Union[str, Path]) -> List[Symbol]:
        """
        Parse a single source file and return its extracted symbols.
        Uses utf-8-sig to cleanly handle BOM.
        """
        path = Path(file_path)
        if is_ignored_path(path, self.custom_ignored_dirs, self.custom_ignored_exts):
            return []

        if not path.is_file():
            logger.warning(f"File does not exist: {path}")
            return []

        try:
            with open(path, "r", encoding="utf-8-sig", errors="replace") as f:
                code = f.read()
        except Exception as e:
            logger.error(f"Error reading file {path}: {e}")
            return []

        suffix = path.suffix.lower()
        norm_path = normalize_path(path)

        if suffix == ".py":
            return self.python_parser.parse_source(code, norm_path)
        elif suffix == ".cs":
            return self.csharp_parser.parse_source(code, norm_path)
        elif suffix in {".ts", ".tsx", ".js", ".jsx"}:
            return self.typescript_parser.parse_source(code, norm_path)
        else:
            return []

    def parse_directory(
        self,
        dir_path: Union[str, Path],
        languages: Optional[List[str]] = None
    ) -> List[Symbol]:
        """
        Recursively traverse a directory, filter ignored paths, and parse all supported source files.
        """
        root = Path(dir_path)
        if not root.exists():
            logger.warning(f"Directory does not exist: {root}")
            return []

        symbols: List[Symbol] = []
        target_exts = set()

        if languages is None or "python" in languages:
            target_exts.add(".py")
        if languages is None or "csharp" in languages:
            target_exts.add(".cs")
        if languages is None or "typescript" in languages:
            target_exts.update({".ts", ".tsx", ".js", ".jsx"})

        for current_root, dirs, files in os.walk(root):
            # Prune ignored directories in-place for performance
            dirs[:] = [
                d for d in dirs
                if not is_ignored_path(Path(current_root) / d, self.custom_ignored_dirs, self.custom_ignored_exts)
            ]

            for file in files:
                file_p = Path(current_root) / file
                if file_p.suffix.lower() in target_exts:
                    if not is_ignored_path(file_p, self.custom_ignored_dirs, self.custom_ignored_exts):
                        file_symbols = self.parse_file(file_p)
                        symbols.extend(file_symbols)

        return symbols

    def parse_project(
        self,
        unity_path: Optional[Union[str, Path]] = None,
        python_path: Optional[Union[str, Path]] = None,
        web_path: Optional[Union[str, Path]] = None,
    ) -> Dict[str, List[Symbol]]:
        """
        Parse all three subsystems of the VR-Autism repository.
        Returns a dictionary mapping subsystem name -> list of symbols.
        """
        results: Dict[str, List[Symbol]] = {
            "unity": [],
            "python": [],
            "web": [],
        }

        # 1. Unity C# Subsystem
        if unity_path:
            u_path = Path(unity_path)
            if u_path.exists():
                logger.info(f"Parsing Unity C# from: {u_path}")
                results["unity"] = self.parse_directory(u_path, languages=["csharp"])

        # 2. Python Agent Subsystem
        if python_path:
            p_path = Path(python_path)
            if p_path.exists():
                logger.info(f"Parsing Python Agent from: {p_path}")
                results["python"] = self.parse_directory(p_path, languages=["python"])

        # 3. Next.js Web Subsystem
        if web_path:
            w_path = Path(web_path)
            if w_path.exists():
                logger.info(f"Parsing Next.js Web from: {w_path}")
                results["web"] = self.parse_directory(w_path, languages=["typescript"])

        return results

    def extract_cross_stack_references(self, file_path: Union[str, Path]) -> List[ContractReference]:
        """
        Extract all contract references from a single file.
        """
        symbols = self.parse_file(file_path)
        all_refs: List[ContractReference] = []
        seen = set()

        for s in symbols:
            for ref in s.cross_stack_refs:
                key = (ref.type, ref.name, ref.line, ref.direction)
                if key not in seen:
                    seen.add(key)
                    all_refs.append(ref)

        return all_refs

    @staticmethod
    def save_symbols_to_json(symbols: List[Symbol], output_file: Union[str, Path]) -> None:
        """Serialize a list of Symbol objects to a JSON file."""
        out_p = Path(output_file)
        out_p.parent.mkdir(parents=True, exist_ok=True)
        data = [s.to_dict() for s in symbols]
        with open(out_p, "w", encoding="utf-8") as f:
            json.dump(data, f, indent=2, ensure_ascii=False)

    @staticmethod
    def load_symbols_from_json(input_file: Union[str, Path]) -> List[Symbol]:
        """Load a list of Symbol objects from a JSON file."""
        in_p = Path(input_file)
        with open(in_p, "r", encoding="utf-8") as f:
            data = json.load(f)
        return [Symbol.from_dict(d) for d in data]


# ---------------------------------------------------------------------------
# Self-Test & Verification Routine
# ---------------------------------------------------------------------------

def run_standalone_verification() -> Dict[str, Any]:
    """
    Runs automated verification against the local codebase.
    """
    project_root = Path(__file__).resolve().parent.parent

    # Candidate paths for the 3 subsystems
    unity_dir = project_root / "Assets" / "Project" / "Scripts"
    python_dir = project_root / "LiveKitAgent" / "src"

    web_dir_candidates = [
        project_root.parent / "VRA-web" / "src",
        Path("d:/Lab/VRA-web/src"),
        project_root / "VRA-web" / "src",
    ]
    web_dir = None
    for cand in web_dir_candidates:
        if cand.exists():
            web_dir = cand
            break

    manager = ASTParserManager()

    results = manager.parse_project(
        unity_path=unity_dir if unity_dir.exists() else None,
        python_path=python_dir if python_dir.exists() else None,
        web_path=web_dir,
    )

    unity_syms = results["unity"]
    python_syms = results["python"]
    web_syms = results["web"]
    total_syms = len(unity_syms) + len(python_syms) + len(web_syms)

    # Collect cross-stack contracts
    all_refs = []
    for s in unity_syms + python_syms + web_syms:
        all_refs.extend(s.cross_stack_refs)

    summary = {
        "status": "PASS" if total_syms > 0 else "FAIL",
        "unity_symbols_count": len(unity_syms),
        "python_symbols_count": len(python_syms),
        "web_symbols_count": len(web_syms),
        "total_symbols_count": total_syms,
        "total_contract_refs": len(all_refs),
        "unique_contract_events": sorted(list(set(r.name for r in all_refs if r.type == "livekit_event"))),
        "unique_rtdb_paths": sorted(list(set(r.name for r in all_refs if r.type == "rtdb_path"))),
        "unique_api_routes": sorted(list(set(r.name for r in all_refs if r.type == "api_route"))),
    }

    return summary


if __name__ == "__main__":
    print("=" * 70)
    print("VR-Autism Cross-Stack AST Parser Verification")
    print("=" * 70)
    res = run_standalone_verification()
    print(json.dumps(res, indent=2, ensure_ascii=False))
