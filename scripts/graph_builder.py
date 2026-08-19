"""
VR-Autism Cross-Stack Network & Contract Dependency Graph (Knowledge Graph).
Constructs a unified directed graph connecting intra-language dependencies (calls, inheritance, containment)
and cross-stack communication bridges (LiveKit DataPacket events, Firebase RTDB paths, and REST API routes).

Features:
- LightweightDiGraph: Pure-Python NetworkX-compatible directed graph engine with PageRank power iteration.
- KnowledgeGraph: Multi-platform symbol & contract dependency graph builder with blast radius analysis.
- Zero external dependencies: Works seamlessly out of the box with standard library.
"""

from __future__ import annotations

import collections
import dataclasses
import json
import logging
import math
import os
import re
import sys
from pathlib import Path
from typing import (
    Any,
    Callable,
    Dict,
    Iterable,
    Iterator,
    List,
    Optional,
    Sequence,
    Set,
    Tuple,
    Union,
)

logger = logging.getLogger("graph_builder")

# Import AST Parser & Models
try:
    from scripts.ast_parsers import (
        ASTParserManager,
        ContractReference,
        LIVEKIT_EVENTS,
        REST_API_ROUTES,
        RTDB_PATH_PATTERNS,
        Symbol,
        is_ignored_path,
        normalize_path,
    )
except ImportError:
    # If invoked directly from scripts directory
    repo_root = Path(__file__).resolve().parent.parent
    if str(repo_root) not in sys.path:
        sys.path.insert(0, str(repo_root))
    from scripts.ast_parsers import (
        ASTParserManager,
        ContractReference,
        LIVEKIT_EVENTS,
        REST_API_ROUTES,
        RTDB_PATH_PATTERNS,
        Symbol,
        is_ignored_path,
        normalize_path,
    )


# ===========================================================================
# LightweightDiGraph Views & Data Structures
# ===========================================================================

class NodeView:
    """NetworkX-compatible view for graph nodes and their attributes."""

    def __init__(self, graph: LightweightDiGraph):
        self._graph = graph

    def __len__(self) -> int:
        return len(self._graph._node)

    def __iter__(self) -> Iterator[str]:
        return iter(self._graph._node)

    def __contains__(self, n: Any) -> bool:
        return str(n) in self._graph._node

    def __getitem__(self, n: Any) -> Dict[str, Any]:
        return self._graph._node[str(n)]

    def __call__(
        self,
        data: Union[bool, str] = False,
        default: Any = None,
    ) -> Union[List[str], List[Tuple[str, Any]]]:
        if data is False:
            return list(self._graph._node.keys())
        elif data is True:
            return [(n, dict(attrs)) for n, attrs in self._graph._node.items()]
        elif isinstance(data, str):
            return [(n, attrs.get(data, default)) for n, attrs in self._graph._node.items()]
        return list(self._graph._node.keys())

    def items(self) -> Iterator[Tuple[str, Dict[str, Any]]]:
        return iter(self._graph._node.items())

    def keys(self) -> Iterator[str]:
        return iter(self._graph._node.keys())

    def values(self) -> Iterator[Dict[str, Any]]:
        return iter(self._graph._node.values())

    def get(self, n: Any, default: Any = None) -> Any:
        return self._graph._node.get(str(n), default)


class EdgeView:
    """NetworkX-compatible view for graph edges and their attributes."""

    def __init__(self, graph: LightweightDiGraph):
        self._graph = graph

    def __len__(self) -> int:
        count = 0
        for u in self._graph._succ:
            count += len(self._graph._succ[u])
        return count

    def __iter__(self) -> Iterator[Tuple[str, str]]:
        for u in self._graph._succ:
            for v in self._graph._succ[u]:
                yield (u, v)

    def __contains__(self, edge: Tuple[Any, Any]) -> bool:
        if len(edge) < 2:
            return False
        u, v = str(edge[0]), str(edge[1])
        return self._graph.has_edge(u, v)

    def __getitem__(self, edge: Tuple[Any, Any]) -> Dict[str, Any]:
        u, v = str(edge[0]), str(edge[1])
        if u not in self._graph._succ or v not in self._graph._succ[u]:
            raise KeyError(f"Edge ({u}, {v}) not in graph")
        return self._graph._succ[u][v]

    def get(self, edge: Tuple[Any, Any], default: Any = None) -> Any:
        if len(edge) < 2:
            return default
        u, v = str(edge[0]), str(edge[1])
        return self._graph.get_edge_data(u, v, default=default)

    def __call__(
        self,
        data: Union[bool, str] = False,
        default: Any = None,
    ) -> List[Tuple[Any, ...]]:
        results = []
        for u in self._graph._succ:
            for v, attrs in self._graph._succ[u].items():
                if data is False:
                    results.append((u, v))
                elif data is True:
                    results.append((u, v, dict(attrs)))
                elif isinstance(data, str):
                    results.append((u, v, attrs.get(data, default)))
        return results


class InDegreeView:
    """NetworkX-compatible view for in-degrees."""

    def __init__(self, graph: LightweightDiGraph, weight: Optional[str] = None):
        self._graph = graph
        self._weight = weight

    def __len__(self) -> int:
        return len(self._graph._node)

    def __iter__(self) -> Iterator[Tuple[str, Union[int, float]]]:
        for n in self._graph._node:
            yield (n, self[n])

    def __getitem__(self, n: Any) -> Union[int, float]:
        sn = str(n)
        if sn not in self._graph._pred:
            raise KeyError(f"Node {sn} not in graph")
        if self._weight is None:
            return len(self._graph._pred[sn])
        return sum(float(attrs.get(self._weight, 1.0)) for attrs in self._graph._pred[sn].values())

    def __call__(self, nbunch: Optional[Any] = None, weight: Optional[str] = None) -> Any:
        w = weight if weight is not None else self._weight
        if nbunch is None:
            return InDegreeView(self._graph, weight=w)
        if isinstance(nbunch, (str, int)):
            sn = str(nbunch)
            if self._weight is None and w is None:
                return len(self._graph._pred.get(sn, {}))
            return sum(float(attrs.get(w, 1.0)) for attrs in self._graph._pred.get(sn, {}).values())
        return [(str(n), InDegreeView(self._graph, weight=w)[str(n)]) for n in nbunch]


class OutDegreeView:
    """NetworkX-compatible view for out-degrees."""

    def __init__(self, graph: LightweightDiGraph, weight: Optional[str] = None):
        self._graph = graph
        self._weight = weight

    def __len__(self) -> int:
        return len(self._graph._node)

    def __iter__(self) -> Iterator[Tuple[str, Union[int, float]]]:
        for n in self._graph._node:
            yield (n, self[n])

    def __getitem__(self, n: Any) -> Union[int, float]:
        sn = str(n)
        if sn not in self._graph._succ:
            raise KeyError(f"Node {sn} not in graph")
        if self._weight is None:
            return len(self._graph._succ[sn])
        return sum(float(attrs.get(self._weight, 1.0)) for attrs in self._graph._succ[sn].values())

    def __call__(self, nbunch: Optional[Any] = None, weight: Optional[str] = None) -> Any:
        w = weight if weight is not None else self._weight
        if nbunch is None:
            return OutDegreeView(self._graph, weight=w)
        if isinstance(nbunch, (str, int)):
            sn = str(nbunch)
            if self._weight is None and w is None:
                return len(self._graph._succ.get(sn, {}))
            return sum(float(attrs.get(w, 1.0)) for attrs in self._graph._succ.get(sn, {}).values())
        return [(str(n), OutDegreeView(self._graph, weight=w)[str(n)]) for n in nbunch]


# ===========================================================================
# Pure-Python Directed Graph Engine: LightweightDiGraph
# ===========================================================================

class LightweightDiGraph:
    """
    Pure-Python directed graph implementation mirroring the NetworkX DiGraph API.
    Provides standard graph operations, traversal algorithms (BFS, DFS, shortest path),
    and power-iteration PageRank algorithm with zero external dependencies.
    """

    def __init__(self, **attr: Any):
        self._node: Dict[str, Dict[str, Any]] = {}
        self._succ: Dict[str, Dict[str, Dict[str, Any]]] = {}
        self._pred: Dict[str, Dict[str, Dict[str, Any]]] = {}
        self.graph: Dict[str, Any] = dict(attr)

    # -----------------------------------------------------------------------
    # Node Management
    # -----------------------------------------------------------------------

    def add_node(self, node_for_adding: Any, **attr: Any) -> None:
        """Add a single node to the graph with optional attributes."""
        n = str(node_for_adding)
        if n not in self._node:
            self._node[n] = {}
            self._succ[n] = {}
            self._pred[n] = {}
        self._node[n].update(attr)

    def add_nodes_from(
        self,
        nodes_for_adding: Iterable[Union[Any, Tuple[Any, Dict[str, Any]]]],
        **attr: Any,
    ) -> None:
        """Add multiple nodes from an iterable of node IDs or (node, attr_dict) tuples."""
        for item in nodes_for_adding:
            if isinstance(item, tuple) and len(item) == 2 and isinstance(item[1], dict):
                node, node_attrs = item
                combined_attrs = dict(attr)
                combined_attrs.update(node_attrs)
                self.add_node(node, **combined_attrs)
            else:
                self.add_node(item, **attr)

    def remove_node(self, n: Any) -> None:
        """Remove a node and all its incoming and outgoing edges."""
        sn = str(n)
        if sn not in self._node:
            raise KeyError(f"Node {sn} not in graph")

        # Remove outgoing edges
        for v in list(self._succ[sn].keys()):
            del self._pred[v][sn]
        del self._succ[sn]

        # Remove incoming edges
        for u in list(self._pred[sn].keys()):
            del self._succ[u][sn]
        del self._pred[sn]

        # Remove node
        del self._node[sn]

    def has_node(self, n: Any) -> bool:
        """Return True if the node is present in the graph."""
        return str(n) in self._node

    def number_of_nodes(self) -> int:
        """Return the number of nodes in the graph."""
        return len(self._node)

    # -----------------------------------------------------------------------
    # Edge Management
    # -----------------------------------------------------------------------

    def add_edge(self, u_of_edge: Any, v_of_edge: Any, **attr: Any) -> None:
        """Add a directed edge from u to v with optional attributes."""
        u = str(u_of_edge)
        v = str(v_of_edge)

        if u not in self._node:
            self.add_node(u)
        if v not in self._node:
            self.add_node(v)

        edge_dict = self._succ[u].get(v, {})
        edge_dict.update(attr)
        self._succ[u][v] = edge_dict
        self._pred[v][u] = edge_dict

    def add_edges_from(
        self,
        ebunch_to_add: Iterable[Union[Tuple[Any, Any], Tuple[Any, Any, Dict[str, Any]]]],
        **attr: Any,
    ) -> None:
        """Add multiple edges from an iterable of (u, v) or (u, v, attr_dict) tuples."""
        for item in ebunch_to_add:
            if len(item) == 3 and isinstance(item[2], dict):
                u, v, edge_attrs = item  # type: ignore
                combined = dict(attr)
                combined.update(edge_attrs)
                self.add_edge(u, v, **combined)
            elif len(item) >= 2:
                u, v = item[0], item[1]
                self.add_edge(u, v, **attr)

    def remove_edge(self, u: Any, v: Any) -> None:
        """Remove the directed edge from u to v."""
        su = str(u)
        sv = str(v)
        if su not in self._succ or sv not in self._succ[su]:
            raise KeyError(f"Edge ({su}, {sv}) not in graph")
        del self._succ[su][sv]
        del self._pred[sv][su]

    def has_edge(self, u: Any, v: Any) -> bool:
        """Return True if the directed edge from u to v exists."""
        su = str(u)
        sv = str(v)
        return su in self._succ and sv in self._succ[su]

    def get_edge_data(self, u: Any, v: Any, default: Any = None) -> Any:
        """Return edge attribute dictionary or default if edge does not exist."""
        su = str(u)
        sv = str(v)
        if su in self._succ and sv in self._succ[su]:
            return self._succ[su][sv]
        return default

    def number_of_edges(self) -> int:
        """Return the total number of directed edges."""
        return sum(len(nbrs) for nbrs in self._succ.values())

    # -----------------------------------------------------------------------
    # Views & Properties
    # -----------------------------------------------------------------------

    @property
    def nodes(self) -> NodeView:
        return NodeView(self)

    @property
    def edges(self) -> EdgeView:
        return EdgeView(self)

    @property
    def succ(self) -> Dict[str, Dict[str, Dict[str, Any]]]:
        return self._succ

    @property
    def pred(self) -> Dict[str, Dict[str, Dict[str, Any]]]:
        return self._pred

    @property
    def adj(self) -> Dict[str, Dict[str, Dict[str, Any]]]:
        return self._succ

    def __iter__(self) -> Iterator[str]:
        return iter(self._node)

    def __len__(self) -> int:
        return len(self._node)

    def __contains__(self, n: Any) -> bool:
        return str(n) in self._node

    def __getitem__(self, n: Any) -> Dict[str, Dict[str, Any]]:
        sn = str(n)
        return self._succ[sn]

    # -----------------------------------------------------------------------
    # Adjacency, Successors, Predecessors & Degrees
    # -----------------------------------------------------------------------

    def neighbors(self, n: Any) -> Iterator[str]:
        """Return an iterator over successors of node n."""
        sn = str(n)
        if sn not in self._succ:
            raise KeyError(f"Node {sn} not in graph")
        return iter(self._succ[sn])

    def successors(self, n: Any) -> Iterator[str]:
        """Return an iterator over successor nodes of n."""
        return self.neighbors(n)

    def predecessors(self, n: Any) -> Iterator[str]:
        """Return an iterator over predecessor nodes of n."""
        sn = str(n)
        if sn not in self._pred:
            raise KeyError(f"Node {sn} not in graph")
        return iter(self._pred[sn])

    def in_degree(
        self,
        nbunch: Optional[Any] = None,
        weight: Optional[str] = None,
    ) -> Union[int, float, InDegreeView]:
        """Return in-degree of a single node or InDegreeView for the graph."""
        if nbunch is None:
            return InDegreeView(self, weight=weight)
        sn = str(nbunch)
        if sn not in self._pred:
            raise KeyError(f"Node {sn} not in graph")
        if weight is None:
            return len(self._pred[sn])
        return sum(float(attrs.get(weight, 1.0)) for attrs in self._pred[sn].values())

    def out_degree(
        self,
        nbunch: Optional[Any] = None,
        weight: Optional[str] = None,
    ) -> Union[int, float, OutDegreeView]:
        """Return out-degree of a single node or OutDegreeView for the graph."""
        if nbunch is None:
            return OutDegreeView(self, weight=weight)
        sn = str(nbunch)
        if sn not in self._succ:
            raise KeyError(f"Node {sn} not in graph")
        if weight is None:
            return len(self._succ[sn])
        return sum(float(attrs.get(weight, 1.0)) for attrs in self._succ[sn].values())

    def degree(
        self,
        nbunch: Optional[Any] = None,
        weight: Optional[str] = None,
    ) -> Union[int, float, Dict[str, Union[int, float]]]:
        """Return total degree (in + out) of a single node or dict for all nodes."""
        if nbunch is not None:
            in_d = self.in_degree(nbunch, weight=weight)
            out_d = self.out_degree(nbunch, weight=weight)
            return in_d + out_d  # type: ignore
        return {n: self.in_degree(n, weight=weight) + self.out_degree(n, weight=weight) for n in self._node}  # type: ignore

    # -----------------------------------------------------------------------
    # Subgraph & Copy
    # -----------------------------------------------------------------------

    def subgraph(self, nodes: Iterable[Any]) -> LightweightDiGraph:
        """Return a new LightweightDiGraph induced by the given nodes."""
        sub = LightweightDiGraph(**self.graph)
        node_set = {str(n) for n in nodes if str(n) in self._node}

        for n in node_set:
            sub.add_node(n, **self._node[n])

        for u in node_set:
            for v, attrs in self._succ[u].items():
                if v in node_set:
                    sub.add_edge(u, v, **attrs)

        return sub

    def copy(self) -> LightweightDiGraph:
        """Return a deep copy of the graph."""
        cp = LightweightDiGraph(**self.graph)
        for n, attrs in self._node.items():
            cp.add_node(n, **attrs)
        for u in self._succ:
            for v, attrs in self._succ[u].items():
                cp.add_edge(u, v, **attrs)
        return cp

    def reverse(self, copy: bool = True) -> LightweightDiGraph:
        """Return the reverse directed graph (edges u->v become v->u)."""
        rev = LightweightDiGraph(**self.graph)
        for n, attrs in self._node.items():
            rev.add_node(n, **attrs)
        for u in self._succ:
            for v, attrs in self._succ[u].items():
                rev.add_edge(v, u, **attrs)
        return rev

    def clear(self) -> None:
        """Remove all nodes and edges from the graph."""
        self._node.clear()
        self._succ.clear()
        self._pred.clear()
        self.graph.clear()

    # -----------------------------------------------------------------------
    # Graph Traversal Algorithms: BFS, DFS, Shortest Path
    # -----------------------------------------------------------------------

    def bfs(
        self,
        source: Any,
        max_depth: Optional[int] = None,
        reverse: bool = False,
    ) -> Iterator[Tuple[str, int]]:
        """
        Breadth-First Search traversal generator yielding (node, depth) pairs.
        """
        src = str(source)
        if src not in self._node:
            return

        queue = collections.deque([(src, 0)])
        visited: Set[str] = {src}

        while queue:
            current, depth = queue.popleft()
            yield current, depth

            if max_depth is not None and depth >= max_depth:
                continue

            nbrs = self.predecessors(current) if reverse else self.successors(current)
            for nbr in nbrs:
                if nbr not in visited:
                    visited.add(nbr)
                    queue.append((nbr, depth + 1))

    def dfs(
        self,
        source: Any,
        max_depth: Optional[int] = None,
        reverse: bool = False,
    ) -> Iterator[Tuple[str, int]]:
        """
        Depth-First Search traversal generator yielding (node, depth) pairs.
        """
        src = str(source)
        if src not in self._node:
            return

        stack = [(src, 0)]
        visited: Set[str] = {src}

        while stack:
            current, depth = stack.pop()
            yield current, depth

            if max_depth is not None and depth >= max_depth:
                continue

            nbrs = list(self.predecessors(current) if reverse else self.successors(current))
            for nbr in reversed(nbrs):
                if nbr not in visited:
                    visited.add(nbr)
                    stack.append((nbr, depth + 1))

    def shortest_path(self, source: Any, target: Any) -> List[str]:
        """
        Find shortest unweighted directed path from source to target using BFS.
        Returns list of node IDs from source to target, or empty list if no path exists.
        """
        src = str(source)
        dst = str(target)

        if src not in self._node or dst not in self._node:
            return []
        if src == dst:
            return [src]

        queue = collections.deque([src])
        predecessors: Dict[str, Optional[str]] = {src: None}

        while queue:
            current = queue.popleft()
            if current == dst:
                break

            for nbr in self.successors(current):
                if nbr not in predecessors:
                    predecessors[nbr] = current
                    queue.append(nbr)

        if dst not in predecessors:
            return []

        # Reconstruct path
        path = []
        curr: Optional[str] = dst
        while curr is not None:
            path.append(curr)
            curr = predecessors[curr]
        path.reverse()
        return path

    # -----------------------------------------------------------------------
    # PageRank Algorithm (Power Iteration)
    # -----------------------------------------------------------------------

    def pagerank(
        self,
        alpha: float = 0.85,
        max_iter: int = 100,
        tol: float = 1e-6,
        personalization: Optional[Dict[str, float]] = None,
        nstart: Optional[Dict[str, float]] = None,
        weight: str = "weight",
        ignore_self_loops: bool = True,
    ) -> Dict[str, float]:
        """
        Compute PageRank scores using the power iteration method.
        Handles dangling nodes, cycles, and isolated subgraphs gracefully.
        
        Args:
            alpha: Damping factor for PageRank (default 0.85).
            max_iter: Maximum number of power iterations (default 100).
            tol: Error tolerance to determine convergence (default 1e-6).
            personalization: Optional personalization teleport vector.
            nstart: Optional initial state vector.
            weight: Edge attribute key for edge weight (default 'weight').
            ignore_self_loops: Ignore self-loops during transition calculations.
            
        Returns:
            Dictionary mapping node_id -> PageRank score (normalized so sum == 1.0).
        """
        nodes = list(self._node.keys())
        N = len(nodes)

        if N == 0:
            return {}
        if N == 1:
            return {nodes[0]: 1.0}

        # 1. Base personalization vector p
        if personalization is None:
            p = {n: 1.0 / N for n in nodes}
        else:
            p_sum = sum(personalization.get(n, 0.0) for n in nodes)
            if p_sum == 0.0:
                p = {n: 1.0 / N for n in nodes}
            else:
                p = {n: float(personalization.get(n, 0.0)) / p_sum for n in nodes}

        # 2. Initial state vector x
        if nstart is None:
            x = {n: 1.0 / N for n in nodes}
        else:
            x_sum = sum(nstart.get(n, 0.0) for n in nodes)
            if x_sum == 0.0:
                x = {n: 1.0 / N for n in nodes}
            else:
                x = {n: float(nstart.get(n, 0.0)) / x_sum for n in nodes}

        # 3. Precompute out-degree weights and dangling nodes
        out_weights: Dict[str, float] = {}
        for u in nodes:
            succs = self._succ[u]
            total_w = 0.0
            for v, attrs in succs.items():
                if ignore_self_loops and u == v:
                    continue
                try:
                    w = float(attrs.get(weight, 1.0))
                except (ValueError, TypeError):
                    w = 1.0
                total_w += w
            out_weights[u] = total_w

        dangling_nodes = [u for u in nodes if out_weights[u] == 0.0]

        # 4. Power-iteration loop
        for _ in range(max_iter):
            xlast = x
            x = {n: 0.0 for n in nodes}
            danglesum = alpha * sum(xlast[u] for u in dangling_nodes)

            for u in nodes:
                w_out = out_weights[u]
                if w_out > 0.0:
                    for v, attrs in self._succ[u].items():
                        if ignore_self_loops and u == v:
                            continue
                        try:
                            w = float(attrs.get(weight, 1.0))
                        except (ValueError, TypeError):
                            w = 1.0
                        x[v] += alpha * xlast[u] * (w / w_out)

            # Add teleportation and dangling distribution
            for n in nodes:
                x[n] += danglesum * p[n] + (1.0 - alpha) * p[n]

            # Normalize to avoid floating point drift
            total_x = sum(x.values())
            if total_x > 0.0:
                for n in nodes:
                    x[n] /= total_x

            # Check convergence (L1 norm error < N * tol)
            err = sum(abs(x[n] - xlast[n]) for n in nodes)
            if err < N * tol:
                break

        # Final normalization guarantee
        total_x = sum(x.values())
        if total_x > 0.0:
            for n in nodes:
                x[n] = float(x[n] / total_x)

        return x


# ===========================================================================
# KnowledgeGraph: Cross-Stack Dependency & Contract Bridge Graph
# ===========================================================================

class KnowledgeGraph:
    """
    Multi-platform Code Knowledge Graph (CKG) for VR-Autism.
    Connects Unity C#, Python Voice Agent, and Next.js Web Dashboard symbols
    with cross-stack LiveKit DataPacket events, Firebase RTDB paths, and REST routes.
    """

    def __init__(self, graph: Optional[LightweightDiGraph] = None):
        self.graph = graph if graph is not None else LightweightDiGraph()
        self._symbols: Dict[str, Symbol] = {}
        self._symbol_by_name: Dict[str, List[str]] = collections.defaultdict(list)
        self._symbol_by_file: Dict[str, List[str]] = collections.defaultdict(list)
        self._contract_nodes: Set[str] = set()

    # -----------------------------------------------------------------------
    # Direct Node and Edge Manipulation
    # -----------------------------------------------------------------------

    def add_node(self, node_id: str, **attrs: Any) -> None:
        """Add or update a node in the knowledge graph."""
        nid = str(node_id)
        name = attrs.get("name", nid)
        subsystem = attrs.get("subsystem")

        if subsystem is None:
            if nid.startswith("contract:"):
                subsystem = "contract"
            elif nid.startswith("unity:") or "Assets/" in nid:
                subsystem = "unity"
            elif nid.startswith("python:") or "LiveKitAgent/" in nid:
                subsystem = "python"
            elif nid.startswith("web:") or "src/" in nid or "VRA-web/" in nid:
                subsystem = "web"
            else:
                subsystem = "unknown"

        node_data = dict(attrs)
        node_data["name"] = name
        node_data["subsystem"] = subsystem
        node_data.setdefault("kind", "symbol")

        if subsystem == "contract" or nid.startswith("contract:"):
            self._contract_nodes.add(nid)

        self.graph.add_node(nid, **node_data)
        if nid not in self._symbol_by_name[name]:
            self._symbol_by_name[name].append(nid)

    def add_edge(self, source: str, target: str, **attrs: Any) -> None:
        """Add a directed edge between two nodes in the knowledge graph."""
        src = str(source)
        dst = str(target)

        if not self.graph.has_node(src):
            self.add_node(src)
        if not self.graph.has_node(dst):
            self.add_node(dst)

        edge_data = dict(attrs)
        edge_data.setdefault("kind", "RELATION")
        edge_data.setdefault("weight", 1.0)
        self.graph.add_edge(src, dst, **edge_data)

    def add_symbol_node(self, symbol: Symbol, subsystem: str = "unknown") -> None:
        """Add a Symbol object as a graph node with full metadata."""
        sub = subsystem
        if sub == "unknown" or not sub:
            if symbol.language == "csharp" or "Assets" in symbol.file_path:
                sub = "unity"
            elif symbol.language == "python" or "LiveKitAgent" in symbol.file_path:
                sub = "python"
            elif symbol.language == "typescript" or "VRA-web" in symbol.file_path or "src" in symbol.file_path:
                sub = "web"
            else:
                sub = "unity"

        attrs = {
            "id": symbol.id,
            "name": symbol.name,
            "subsystem": sub,
            "kind": symbol.kind,
            "file_path": symbol.file_path,
            "line_start": symbol.line_start,
            "line_end": symbol.line_end,
            "docstring": symbol.docstring,
            "signature": symbol.signature,
            "language": symbol.language,
            "parent_id": symbol.parent_id,
            "modifiers": symbol.modifiers,
            "dependencies": symbol.dependencies,
            "cross_stack_refs": [
                r.to_dict() if isinstance(r, ContractReference) else r
                for r in symbol.cross_stack_refs
            ],
        }

        self._symbols[symbol.id] = symbol
        if symbol.id not in self._symbol_by_name[symbol.name]:
            self._symbol_by_name[symbol.name].append(symbol.id)
        if symbol.id not in self._symbol_by_file[symbol.file_path]:
            self._symbol_by_file[symbol.file_path].append(symbol.id)

        self.add_node(symbol.id, **attrs)

    # -----------------------------------------------------------------------
    # Graph Construction from AST Symbols & Cross-Stack Bridges
    # -----------------------------------------------------------------------

    def build_from_symbols(
        self,
        symbols_by_subsystem: Dict[str, List[Symbol]],
    ) -> None:
        """
        Construct graph from symbol lists across Unity, Python, and Web subsystems.
        Resolves intra-language edges (CONTAINS, INHERITS, CALLS, IMPORTS) and
        cross-stack contract bridges (LiveKit events, RTDB paths, and REST API routes).
        """
        all_symbols: List[Tuple[str, Symbol]] = []

        # 1. First Pass: Register all symbol nodes
        for sub_name, sym_list in symbols_by_subsystem.items():
            for sym in sym_list:
                self.add_symbol_node(sym, subsystem=sub_name)
                all_symbols.append((sub_name, sym))

        # 2. Second Pass: Intra-Language Edges (CONTAINS, INHERITS, CALLS)
        for sub_name, sym in all_symbols:
            # A. CONTAINS (Parent -> Child)
            if sym.parent_id:
                if self.graph.has_node(sym.parent_id):
                    self.add_edge(sym.parent_id, sym.id, kind="CONTAINS")
                else:
                    # Look up by parent name
                    parent_matches = self._symbol_by_name.get(sym.parent_id, [])
                    for pid in parent_matches:
                        if self.graph.has_node(pid):
                            self.add_edge(pid, sym.id, kind="CONTAINS")
                            break

            # B. INHERITS (Derived -> Base / Interface)
            if sym.kind in {"class", "interface", "struct", "type"} and sym.dependencies:
                for base in sym.dependencies:
                    clean_base = base.split("<")[0].strip()
                    candidates = self._symbol_by_name.get(clean_base, [])
                    for cand_id in candidates:
                        cand_node = self.graph.nodes.get(cand_id, {})
                        if cand_node.get("subsystem") == sub_name or cand_node.get("language") == sym.language:
                            self.add_edge(sym.id, cand_id, kind="INHERITS")
                            break

            # C. CALLS / USES (Caller -> Callee / Class Reference)
            if sym.dependencies:
                for dep in sym.dependencies:
                    clean_fn = dep.split(".")[-1].rstrip("()")
                    if clean_fn in {"base", "this", "super", "__init__", "log", "Log", "println", "print"}:
                        continue
                    callee_candidates = self._symbol_by_name.get(clean_fn, [])
                    for cand_id in callee_candidates:
                        if cand_id != sym.id:
                            cand_node = self.graph.nodes.get(cand_id, {})
                            cand_kind = cand_node.get("kind")
                            if cand_kind in {
                                "method", "function", "async_function", "tool", "constructor", "server_action", "hook"
                            }:
                                self.add_edge(sym.id, cand_id, kind="CALLS")
                                break
                            elif cand_kind in {"class", "interface", "struct", "component"}:
                                self.add_edge(sym.id, cand_id, kind="USES")
                                break

        # 3. Third Pass: Cross-Stack Contract Bridges
        for sub_name, sym in all_symbols:
            for ref in sym.cross_stack_refs:
                ref_type = getattr(ref, "type", "") if hasattr(ref, "type") else ref.get("type", "")
                ref_name = getattr(ref, "name", "") if hasattr(ref, "name") else ref.get("name", "")
                ref_dir = getattr(ref, "direction", "") if hasattr(ref, "direction") else ref.get("direction", "")

                if not ref_type or not ref_name:
                    continue

                if ref_type == "livekit_event":
                    contract_id = f"contract:livekit_event:{ref_name}"
                    if not self.graph.has_node(contract_id):
                        self.add_node(
                            contract_id,
                            name=ref_name,
                            subsystem="contract",
                            kind="livekit_event",
                            contract_type="livekit_event",
                            docstring=f"LiveKit DataPacket Event: {ref_name}",
                        )

                    if ref_dir in {"publisher", "writer", "caller"}:
                        self.add_edge(sym.id, contract_id, kind="PUBLISHES_EVENT", direction="publisher", weight=1.0)
                    elif ref_dir in {"subscriber", "reader", "listener", "handler"}:
                        self.add_edge(sym.id, contract_id, kind="SUBSCRIBES_EVENT", direction="subscriber", weight=1.0)
                    else:
                        self.add_edge(sym.id, contract_id, kind="PUBLISHES_EVENT", direction=ref_dir or "publisher", weight=1.0)

                elif ref_type == "rtdb_path":
                    contract_id = f"contract:rtdb_path:{ref_name}"
                    if not self.graph.has_node(contract_id):
                        self.add_node(
                            contract_id,
                            name=ref_name,
                            subsystem="contract",
                            kind="rtdb_path",
                            contract_type="rtdb_path",
                            docstring=f"Firebase RTDB Path: {ref_name}",
                        )

                    if ref_dir in {"writer", "publisher"}:
                        self.add_edge(sym.id, contract_id, kind="WRITES_RTDB", direction="writer", weight=1.0)
                    elif ref_dir in {"reader", "subscriber", "listener"}:
                        self.add_edge(sym.id, contract_id, kind="READS_RTDB", direction="reader", weight=1.0)
                    else:
                        self.add_edge(sym.id, contract_id, kind="WRITES_RTDB", direction=ref_dir or "writer", weight=1.0)

                elif ref_type == "api_route":
                    contract_id = f"contract:api_route:{ref_name}"
                    if not self.graph.has_node(contract_id):
                        self.add_node(
                            contract_id,
                            name=ref_name,
                            subsystem="contract",
                            kind="api_route",
                            contract_type="api_route",
                            docstring=f"REST API Route: {ref_name}",
                        )

                    if ref_dir in {"caller", "publisher"}:
                        self.add_edge(sym.id, contract_id, kind="CALLS_API", direction="caller", weight=1.0)
                    elif ref_dir in {"handler", "subscriber"}:
                        self.add_edge(sym.id, contract_id, kind="HANDLES_API", direction="handler", weight=1.0)
                    else:
                        self.add_edge(sym.id, contract_id, kind="CALLS_API", direction=ref_dir or "caller", weight=1.0)

    @classmethod
    def build_graph(
        cls,
        unity_path: Optional[Union[str, Path]] = None,
        python_path: Optional[Union[str, Path]] = None,
        web_path: Optional[Union[str, Path]] = None,
    ) -> KnowledgeGraph:
        """
        Scan workspace directories using ASTParserManager, build full KnowledgeGraph,
        and compute baseline PageRank rankings.
        """
        manager = ASTParserManager()

        # Auto-discover paths if not supplied
        repo_root = Path(__file__).resolve().parent.parent
        u_path = Path(unity_path) if unity_path else repo_root / "Assets" / "Project" / "Scripts"
        p_path = Path(python_path) if python_path else repo_root / "LiveKitAgent" / "src"

        w_path = None
        if web_path:
            w_path = Path(web_path)
        else:
            candidates = [
                repo_root.parent / "VRA-web" / "src",
                Path("d:/Lab/VRA-web/src"),
                repo_root / "VRA-web" / "src",
            ]
            for cand in candidates:
                if cand.exists():
                    w_path = cand
                    break

        symbols = manager.parse_project(
            unity_path=u_path if u_path.exists() else None,
            python_path=p_path if p_path.exists() else None,
            web_path=w_path,
        )

        kg = cls()
        kg.build_from_symbols(symbols)
        kg.compute_pagerank()
        return kg

    # -----------------------------------------------------------------------
    # PageRank & Symbol Ranking
    # -----------------------------------------------------------------------

    def compute_pagerank(
        self,
        alpha: float = 0.85,
        max_iter: int = 100,
        tol: float = 1e-6,
    ) -> Dict[str, float]:
        """
        Compute PageRank importance scores across all nodes and store the score
        in each node's attribute dictionary ('pagerank').
        """
        scores = self.graph.pagerank(alpha=alpha, max_iter=max_iter, tol=tol)
        for nid, score in scores.items():
            if nid in self.graph._node:
                self.graph._node[nid]["pagerank"] = score
        return scores

    # -----------------------------------------------------------------------
    # Blast Radius & Impact Analysis
    # -----------------------------------------------------------------------

    def find_impact_radius(
        self,
        symbol_or_contract: str,
        max_depth: int = 3,
    ) -> Dict[str, Any]:
        """
        Traverse upstream callers/subscribers and downstream callees/publishers
        from the given symbol or contract name across all 3 subsystems.
        Returns detailed blast radius report categorized by subsystem (unity, python, web, contract).
        """
        query = symbol_or_contract.strip()
        matched_roots: List[str] = []

        # 1. Exact node ID match
        if self.graph.has_node(query):
            matched_roots.append(query)

        # 2. Exact or suffix contract ID match
        if not matched_roots:
            for contract_candidate in [
                f"contract:livekit_event:{query}",
                f"contract:rtdb_path:{query}",
                f"contract:api_route:{query}",
                f"contract:{query}",
            ]:
                if self.graph.has_node(contract_candidate):
                    matched_roots.append(contract_candidate)

        # 3. Match by node name
        if not matched_roots and query in self._symbol_by_name:
            matched_roots.extend(self._symbol_by_name[query])

        # 4. Search by substring in node ID or name
        if not matched_roots:
            for nid, attrs in self.graph.nodes(data=True):
                name = attrs.get("name", "")
                if query.lower() == name.lower() or query.lower() in nid.lower():
                    matched_roots.append(nid)

        # If still no match, return empty impact report
        if not matched_roots:
            return {
                "root": query,
                "max_depth": max_depth,
                "total_affected": 0,
                "affected_nodes": [],
                "by_subsystem": {
                    "unity": [],
                    "python": [],
                    "web": [],
                    "contract": [],
                },
                "edges": [],
            }

        # 5. Bidirectional BFS Traversal
        affected_nodes_map: Dict[str, Dict[str, Any]] = {}
        visited_depth: Dict[str, int] = {}
        queue: collections.deque[Tuple[str, int, str]] = collections.deque()

        for root_id in matched_roots:
            visited_depth[root_id] = 0
            queue.append((root_id, 0, "root"))

        while queue:
            curr_id, depth, rel_dir = queue.popleft()
            node_attrs = self.graph.nodes.get(curr_id, {})

            if curr_id not in affected_nodes_map:
                affected_nodes_map[curr_id] = {
                    "id": curr_id,
                    "name": node_attrs.get("name", curr_id),
                    "subsystem": node_attrs.get("subsystem", "unknown"),
                    "kind": node_attrs.get("kind", "unknown"),
                    "file_path": node_attrs.get("file_path", ""),
                    "line_start": node_attrs.get("line_start", 0),
                    "line_end": node_attrs.get("line_end", 0),
                    "signature": node_attrs.get("signature", ""),
                    "docstring": node_attrs.get("docstring", ""),
                    "pagerank": node_attrs.get("pagerank", 0.0),
                    "distance": depth,
                    "direction": rel_dir,
                }

            if depth >= max_depth:
                continue

            # Downstream neighbors (successors: callees, published events, members)
            for succ in self.graph.successors(curr_id):
                if succ not in visited_depth or depth + 1 < visited_depth[succ]:
                    visited_depth[succ] = depth + 1
                    queue.append((succ, depth + 1, "downstream"))

            # Upstream neighbors (predecessors: callers, subscribing handlers, parents)
            for pred in self.graph.predecessors(curr_id):
                if pred not in visited_depth or depth + 1 < visited_depth[pred]:
                    visited_depth[pred] = depth + 1
                    queue.append((pred, depth + 1, "upstream"))

        affected_nodes = list(affected_nodes_map.values())
        # Sort by distance ascending, then by pagerank descending
        affected_nodes.sort(key=lambda n: (n["distance"], -n.get("pagerank", 0.0)))

        by_subsystem: Dict[str, List[Dict[str, Any]]] = {
            "unity": [n for n in affected_nodes if n["subsystem"] == "unity"],
            "python": [n for n in affected_nodes if n["subsystem"] == "python"],
            "web": [n for n in affected_nodes if n["subsystem"] == "web"],
            "contract": [n for n in affected_nodes if n["subsystem"] == "contract"],
        }

        return {
            "root": query,
            "max_depth": max_depth,
            "total_affected": len(affected_nodes),
            "affected_nodes": affected_nodes,
            "by_subsystem": by_subsystem,
        }

    # -----------------------------------------------------------------------
    # JIT Subgraph Context Retrieval
    # -----------------------------------------------------------------------

    def get_subgraph_context(
        self,
        query: str,
        token_budget: int = 1500,
    ) -> str:
        """
        Extract a token-budgeted markdown context snippet containing ranked symbols,
        signatures, docstrings, and cross-stack contracts related to a query.
        """
        impact = self.find_impact_radius(query, max_depth=2)
        affected = impact["affected_nodes"]

        if not affected:
            # Fallback to search query
            candidates = []
            q_lower = query.lower()
            for nid, attrs in self.graph.nodes(data=True):
                name = attrs.get("name", "").lower()
                doc = attrs.get("docstring", "").lower()
                if q_lower in name or q_lower in doc:
                    candidates.append({
                        "id": nid,
                        "name": attrs.get("name", nid),
                        "subsystem": attrs.get("subsystem", "unknown"),
                        "kind": attrs.get("kind", "unknown"),
                        "file_path": attrs.get("file_path", ""),
                        "line_start": attrs.get("line_start", 0),
                        "line_end": attrs.get("line_end", 0),
                        "signature": attrs.get("signature", ""),
                        "docstring": attrs.get("docstring", ""),
                        "pagerank": attrs.get("pagerank", 0.0),
                        "distance": 0,
                    })
            affected = sorted(candidates, key=lambda n: -n.get("pagerank", 0.0))

        lines = [
            f"# JIT Context: `{query}`",
            f"*Extracted {len(affected)} relevant symbols across VR-Autism subsystems.*",
            "",
        ]

        if not affected:
            lines.append("No matching symbols or contract bridges found.")
            return "\n".join(lines)

        current_token_count = len("\n".join(lines).split()) * 1.33

        for node in affected:
            block_lines = []
            sub = node["subsystem"].upper()
            kind = node["kind"]
            name = node["name"]
            fpath = node.get("file_path", "")
            lstart = node.get("line_start", 0)
            lend = node.get("line_end", 0)
            sig = node.get("signature", "")
            doc = node.get("docstring", "")

            loc_str = f" `{fpath}:{lstart}-{lend}`" if fpath else ""
            block_lines.append(f"### [{sub}] {kind} `{name}`{loc_str}")
            if sig:
                block_lines.append(f"```\n{sig}\n```")
            if doc:
                block_lines.append(f"> {doc}")

            # Connected contracts
            nid = node["id"]
            succ_contracts = [
                self.graph.nodes[v].get("name", v)
                for v in self.graph.successors(nid)
                if self.graph.nodes[v].get("subsystem") == "contract"
            ]
            pred_contracts = [
                self.graph.nodes[u].get("name", u)
                for u in self.graph.predecessors(nid)
                if self.graph.nodes[u].get("subsystem") == "contract"
            ]
            all_conts = sorted(list(set(succ_contracts + pred_contracts)))
            if all_conts:
                block_lines.append(f"- **Contracts**: {', '.join(f'`{c}`' for c in all_conts)}")

            block_lines.append("")
            block_text = "\n".join(block_lines)
            block_tokens = len(block_text.split()) * 1.33

            if current_token_count + block_tokens > token_budget:
                lines.append("*(Truncated remaining symbols to respect token budget)*")
                break

            lines.append(block_text)
            current_token_count += block_tokens

        return "\n".join(lines)

    # -----------------------------------------------------------------------
    # Serialization & Schema Export
    # -----------------------------------------------------------------------

    def to_dict(self) -> Dict[str, Any]:
        """
        Export complete graph representation as a serializable dictionary conforming
        to the standard VR-Autism CKG schema.
        """
        nodes_list = []
        subsystem_counts = collections.defaultdict(int)

        for nid, attrs in self.graph.nodes(data=True):
            sub = attrs.get("subsystem", "unknown")
            subsystem_counts[sub] += 1
            node_dict = dict(attrs)
            node_dict["id"] = nid
            nodes_list.append(node_dict)

        edges_list = []
        for u, v, attrs in self.graph.edges(data=True):
            edge_dict = dict(attrs)
            edge_dict["source"] = u
            edge_dict["target"] = v
            edge_dict.setdefault("kind", "RELATION")
            edges_list.append(edge_dict)

        # Rankings sorted by PageRank score
        rankings = [
            {
                "id": n["id"],
                "name": n.get("name", n["id"]),
                "subsystem": n.get("subsystem", "unknown"),
                "kind": n.get("kind", "symbol"),
                "pagerank": n.get("pagerank", 0.0),
            }
            for n in nodes_list
        ]
        rankings.sort(key=lambda r: -r["pagerank"])

        return {
            "metadata": {
                "total_nodes": len(nodes_list),
                "total_edges": len(edges_list),
                "subsystems": dict(subsystem_counts),
                "contract_nodes_count": len(self._contract_nodes),
                "version": "1.0.0",
            },
            "nodes": nodes_list,
            "edges": edges_list,
            "rankings": rankings[:100],
        }

    @classmethod
    def from_dict(cls, data: Dict[str, Any]) -> KnowledgeGraph:
        """Construct KnowledgeGraph from a serialized dictionary."""
        kg = cls()
        for node in data.get("nodes", []):
            nid = node.get("id")
            if nid:
                attrs = {k: v for k, v in node.items() if k != "id"}
                kg.add_node(nid, **attrs)

        for edge in data.get("edges", []):
            src = edge.get("source")
            dst = edge.get("target")
            if src and dst:
                attrs = {k: v for k, v in edge.items() if k not in {"source", "target"}}
                kg.add_edge(src, dst, **attrs)

        return kg

    def save_to_json(self, output_path: Union[str, Path]) -> None:
        """Save graph data to a formatted JSON file."""
        out_p = Path(output_path)
        out_p.parent.mkdir(parents=True, exist_ok=True)
        with open(out_p, "w", encoding="utf-8") as f:
            json.dump(self.to_dict(), f, indent=2, ensure_ascii=False)

    @classmethod
    def load_from_json(cls, input_path: Union[str, Path]) -> KnowledgeGraph:
        """Load KnowledgeGraph from a JSON file."""
        in_p = Path(input_path)
        with open(in_p, "r", encoding="utf-8") as f:
            data = json.load(f)
        return cls.from_dict(data)

    def get_contract_bridges(self) -> List[Dict[str, Any]]:
        """Return structured summary of all cross-stack contract bridge hubs."""
        bridges = []
        for nid, attrs in self.graph.nodes(data=True):
            if attrs.get("subsystem") == "contract" or nid.startswith("contract:"):
                # Collect connected symbols
                publishers = [
                    self.graph.nodes[u]
                    for u in self.graph.predecessors(nid)
                    if self.graph.nodes[u].get("subsystem") != "contract"
                ]
                subscribers = [
                    self.graph.nodes[v]
                    for v in self.graph.successors(nid)
                    if self.graph.nodes[v].get("subsystem") != "contract"
                ]

                # Also check incident edges where direction might be stored
                for u in self.graph.predecessors(nid):
                    edge_kind = self.graph.edges.get((u, nid), {}).get("kind", "")
                    if "SUBSCRIBES" in edge_kind and self.graph.nodes[u] not in subscribers:
                        subscribers.append(self.graph.nodes[u])

                bridges.append({
                    "id": nid,
                    "name": attrs.get("name", nid),
                    "kind": attrs.get("kind", "contract"),
                    "publishers": [p.get("name", p.get("id")) for p in publishers],
                    "subscribers": [s.get("name", s.get("id")) for s in subscribers],
                    "total_connections": len(publishers) + len(subscribers),
                })
        bridges.sort(key=lambda b: -b["total_connections"])
        return bridges


# ===========================================================================
# Helper Module-Level Functions
# ===========================================================================

def get_impact_analysis(
    graph: KnowledgeGraph,
    symbol: str,
    max_depth: int = 3,
) -> Dict[str, Any]:
    """Helper function to perform blast radius impact analysis."""
    return graph.find_impact_radius(symbol, max_depth=max_depth)


def get_jit_context(
    graph: KnowledgeGraph,
    query: str,
    token_budget: int = 1500,
) -> str:
    """Helper function to extract token-budgeted JIT context."""
    return graph.get_subgraph_context(query, token_budget=token_budget)


def build_knowledge_graph(
    unity_path: Optional[Union[str, Path]] = None,
    python_path: Optional[Union[str, Path]] = None,
    web_path: Optional[Union[str, Path]] = None,
) -> KnowledgeGraph:
    """Helper function to construct knowledge graph from workspace."""
    return KnowledgeGraph.build_graph(unity_path, python_path, web_path)


# ===========================================================================
# Standalone CLI & Verification Routine
# ===========================================================================

def run_standalone_verification() -> Dict[str, Any]:
    """
    Constructs real repository Knowledge Graph, executes PageRank and impact analysis,
    and returns verification summary.
    """
    repo_root = Path(__file__).resolve().parent.parent

    unity_dir = repo_root / "Assets" / "Project" / "Scripts"
    python_dir = repo_root / "LiveKitAgent" / "src"
    web_dir_candidates = [
        repo_root.parent / "VRA-web" / "src",
        Path("d:/Lab/VRA-web/src"),
        repo_root / "VRA-web" / "src",
    ]
    web_dir = None
    for cand in web_dir_candidates:
        if cand.exists():
            web_dir = cand
            break

    kg = KnowledgeGraph.build_graph(
        unity_path=unity_dir if unity_dir.exists() else None,
        python_path=python_dir if python_dir.exists() else None,
        web_path=web_dir,
    )

    data = kg.to_dict()
    meta = data["metadata"]
    rankings = data.get("rankings", [])
    bridges = kg.get_contract_bridges()

    summary = {
        "status": "PASS" if meta["total_nodes"] > 0 else "FAIL",
        "total_nodes": meta["total_nodes"],
        "total_edges": meta["total_edges"],
        "subsystems": meta["subsystems"],
        "total_contracts": len(bridges),
        "top_ranked_symbols": [
            f"{r['name']} ({r['subsystem']}): {r['pagerank']:.4f}"
            for r in rankings[:8]
        ],
        "top_contract_bridges": [
            f"{b['name']} ({b['kind']}): {b['total_connections']} links"
            for b in bridges[:6]
        ],
    }

    return summary


if __name__ == "__main__":
    import argparse
    parser = argparse.ArgumentParser(description="VR-Autism Code Knowledge Graph (CKG) Builder.")
    parser.add_argument("--export", type=str, help="Export graph to repomap.json path.")
    parser.add_argument("--impact", type=str, help="Run blast radius impact query on symbol.")
    parser.add_argument("--context", type=str, help="Extract JIT context for query.")
    parser.add_argument("--budget", type=int, default=1500, help="Token budget for context.")
    args = parser.parse_args()

    print("=" * 70)
    print("VR-Autism Code Knowledge Graph (CKG) Construction")
    print("=" * 70)

    if args.impact:
        kg = KnowledgeGraph.build_graph()
        impact = kg.find_impact_radius(args.impact)
        print(f"Impact Analysis for: {args.impact}")
        print(f"Total Affected: {impact['total_affected']}")
        for n in impact["affected_nodes"][:15]:
            print(f" - [{n['subsystem']}] {n['kind']} {n['name']} (dist: {n['distance']})")
    elif args.context:
        kg = KnowledgeGraph.build_graph()
        ctx = kg.get_subgraph_context(args.context, token_budget=args.budget)
        print(ctx)
    else:
        res = run_standalone_verification()
        print(json.dumps(res, indent=2, ensure_ascii=False))

    if args.export:
        kg = KnowledgeGraph.build_graph()
        kg.save_to_json(args.export)
        print(f"Graph exported to: {args.export}")
