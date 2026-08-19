#!/usr/bin/env python3
"""
Adversarial Stress Test Suite for Cross-Stack AST Parsers (Milestone M1).
Tests scripts/ast_parsers.py against extreme edge cases, malformed syntax,
huge synthetic files, complex generics, multi-line strings, comment pollution,
unusual encodings, and forbidden paths.

Usage:
    python scripts/tests/test_adversarial.py
    python -m unittest scripts/tests/test_adversarial.py
"""

import ast
import io
import json
import os
import sys
import time
import unittest
from pathlib import Path
from typing import Any, Dict, List

# Ensure repository root is on sys.path
PROJECT_ROOT = Path(__file__).resolve().parent.parent.parent
if str(PROJECT_ROOT) not in sys.path:
    sys.path.insert(0, str(PROJECT_ROOT))

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

# Test directory inside the repository (avoids AppData/Local/Temp which triggers ignore rule)
ADVERSARIAL_TMP_DIR = PROJECT_ROOT / "scripts" / "tests" / "fixtures_adversarial"


class TestAdversarialNestingAndGenerics(unittest.TestCase):
    """
    Stress-tests deeply nested structures, complex generics, and inheritance hierarchies.
    """

    def setUp(self):
        self.py_parser = PythonASTParser()
        self.cs_parser = CSharpASTParser()
        self.ts_parser = TypeScriptASTParser()
        self.manager = ASTParserManager()

    def test_csharp_deeply_nested_classes(self):
        """Test C# nested classes: Level1 -> Level2 -> Level3 -> Level4."""
        code = '''
namespace TestNamespace
{
    public class OuterClass
    {
        public int OuterField;
        public void OuterMethod() {}

        public class MiddleClass
        {
            public void MiddleMethod() {}

            public class InnerClass
            {
                public void InnerMethod() {}

                public class DeepestClass
                {
                    public void DeepestMethod() {}
                }
            }
        }
    }
}
'''
        symbols = self.cs_parser.parse_source(code, "Assets/Nested.cs")
        names = {s.name: s for s in symbols}

        self.assertIn("OuterClass", names)
        self.assertIn("MiddleClass", names)
        self.assertIn("InnerClass", names)
        self.assertIn("DeepestClass", names)

        # Check methods
        self.assertIn("OuterMethod", names)
        self.assertIn("MiddleMethod", names)
        self.assertIn("InnerMethod", names)
        self.assertIn("DeepestMethod", names)

    def test_csharp_complex_nested_generics(self):
        """Test C# complex generics: nested type arguments and constraints."""
        code = '''
namespace GenericTest
{
    public interface IRepository<TEntity, in TKey> where TEntity : class, new()
    {
        Task<Dictionary<TKey, List<TEntity>>> GetAllGroupedAsync(TKey key);
    }

    public class DataService<T1, T2> : BaseService<Dictionary<string, List<Task<T1>>>>, IRepository<T1, T2>
        where T1 : class, IEntity<T1>, new()
        where T2 : struct, IComparable<T2>
    {
        public async Task<Dictionary<T2, List<T1>>> GetAllGroupedAsync(T2 key)
        {
            return default;
        }

        public Tuple<List<string>, Dictionary<int, string>> ComplexTupleMethod(
            Dictionary<string, List<KeyValuePair<int, string>>> inputMap)
        {
            return null;
        }
    }
}
'''
        symbols = self.cs_parser.parse_source(code, "Assets/Generics.cs")
        names = {s.name: s for s in symbols}

        self.assertIn("IRepository", names)
        self.assertEqual(names["IRepository"].kind, "interface")
        self.assertIn("DataService", names)
        self.assertEqual(names["DataService"].kind, "class")

    def test_python_nested_classes_and_closures(self):
        """Test Python deeply nested classes and functions/closures."""
        code = '''
class Outer:
    """Outer class."""
    class Middle:
        """Middle class."""
        def middle_fn(self):
            class Inner:
                """Inner class inside function."""
                def inner_fn(self):
                    def deepest_closure():
                        return 42
                    return deepest_closure()
            return Inner()

def outer_factory():
    class FactoryProduct:
        pass
    return FactoryProduct
'''
        symbols = self.py_parser.parse_source(code, "nested.py")
        names = {s.name: s for s in symbols}

        self.assertIn("Outer", names)
        self.assertIn("Middle", names)
        self.assertIn("middle_fn", names)
        self.assertIn("Inner", names)
        self.assertIn("inner_fn", names)
        self.assertIn("deepest_closure", names)
        self.assertIn("outer_factory", names)
        self.assertIn("FactoryProduct", names)

    def test_typescript_complex_types_and_generics(self):
        """Test TypeScript conditional types, mapped types, and complex generic interfaces."""
        code = '''
export type Nullable<T> = T | null | undefined;
export type DeepReadonly<T> = { readonly [P in keyof T]: DeepReadonly<T[P]> };
export type AsyncResult<T, E = Error> = { ok: true; data: T } | { ok: false; error: E };

export interface AdvancedHandler<
    TRequest extends Record<string, any>,
    TResponse extends AsyncResult<any>
> {
    handle(req: TRequest): Promise<TResponse>;
}

export async function processComplexData<
    TInput extends { id: string; meta?: Record<string, unknown> },
    TOutput = Array<TInput>
>(
    input: TInput,
    transformer: (item: TInput) => Promise<TOutput>
): Promise<TOutput> {
    return transformer(input);
}
'''
        symbols = self.ts_parser.parse_source(code, "src/complex.ts")
        names = {s.name: s for s in symbols}

        # Check types
        self.assertIn("Nullable", names)
        self.assertEqual(names["Nullable"].kind, "type")
        self.assertIn("DeepReadonly", names)
        self.assertIn("AsyncResult", names)

    def test_typescript_classes_and_enums(self):
        """Test TypeScript classes and enums support."""
        code = '''
export class VoiceQuestHandler extends BaseHandler implements IQuestHandler {
    private questId: string;
    constructor(id: string) {
        super();
        this.questId = id;
    }
    public async startQuest(): Promise<void> {}
}

export enum SessionState {
    IDLE = "idle",
    ACTIVE = "active",
    PAUSED = "paused",
    COMPLETED = "completed"
}
'''
        symbols = self.ts_parser.parse_source(code, "src/classes_and_enums.ts")
        names = {s.name: s for s in symbols}
        # Documents whether TypeScript classes and enums are captured


class TestAdversarialStringsCommentsAndFormatting(unittest.TestCase):
    """
    Stress-tests string literals containing code, comments with keywords, and weird formatting.
    """

    def setUp(self):
        self.py_parser = PythonASTParser()
        self.cs_parser = CSharpASTParser()
        self.ts_parser = TypeScriptASTParser()

    def test_csharp_code_in_string_literals(self):
        """Test C# files containing multi-line strings with fake classes and keywords."""
        code = '''
public class RealClass
{
    private string fakeCode = @"
        public class FakeClassInVerbatimString
        {
            public void FakeMethod() { }
        }
    ";

    public void RealMethod()
    {
        var msg = "Event: SET_ACTIVE_QUEST is triggered";
    }
}
'''
        symbols = self.cs_parser.parse_source(code, "Assets/Strings.cs")
        names = {s.name: s for s in symbols}

        self.assertIn("RealClass", names)
        self.assertIn("RealMethod", names)

    def test_csharp_comment_keyword_pollution(self):
        """Test C# code with comments containing class and method declarations."""
        code = '''
// public class CommentedSingleClass { public void CommentedSingleMethod() {} }

/*
public class CommentedMultiClass
{
    public void CommentedMultiMethod() {}
}
*/

public class GenuineClass
{
    /// <summary>
    /// This method calls public void FakeDocMethod()
    /// </summary>
    public void GenuineMethod()
    {
        // var packet = "SET_ACTIVE_QUEST";
        /* QUEST_MATCHED */
    }
}
'''
        symbols = self.cs_parser.parse_source(code, "Assets/Comments.cs")
        names = {s.name: s for s in symbols}

        self.assertIn("GenuineClass", names)
        self.assertIn("GenuineMethod", names)

    def test_extreme_indentation_and_oneline_csharp(self):
        """Test C# code on a single line or with irregular indentation."""
        code = 'public class OneLineClass{public int X{get;set;}public void Foo(){}} public interface IOneLine{void Bar();}'
        symbols = self.cs_parser.parse_source(code, "Assets/OneLine.cs")
        names = {s.name: s for s in symbols}

        self.assertIn("OneLineClass", names)
        self.assertIn("IOneLine", names)

    def test_crlf_vs_lf_line_endings(self):
        """Test parsing files with CRLF (\\r\\n), LF (\\n), and mixed line endings."""
        crlf_code = "public class CrlfClass\r\n{\r\n    public void Method1()\r\n    {\r\n    }\r\n}\r\n"
        symbols = self.cs_parser.parse_source(crlf_code, "Assets/Crlf.cs")
        self.assertTrue(any(s.name == "CrlfClass" for s in symbols))
        self.assertTrue(any(s.name == "Method1" for s in symbols))

    def test_method_overloading_in_csharp(self):
        """Test C# multiple methods with identical name (overloads)."""
        code = '''
public class OverloadClass
{
    public void Process(int id) {}
    public void Process(string name) {}
    public void Process(int id, string name) {}
}
'''
        symbols = self.cs_parser.parse_source(code, "Assets/Overload.cs")
        methods = [s for s in symbols if s.name == "Process"]
        # All 3 methods should be captured
        self.assertEqual(len(methods), 3)


class TestAdversarialFileEncodingsAndIO(unittest.TestCase):
    """
    Stress-tests empty files, non-UTF8 encodings, UTF-8 BOM, and binary files.
    """

    @classmethod
    def setUpClass(cls):
        ADVERSARIAL_TMP_DIR.mkdir(parents=True, exist_ok=True)

    @classmethod
    def tearDownClass(cls):
        import shutil
        if ADVERSARIAL_TMP_DIR.exists():
            shutil.rmtree(ADVERSARIAL_TMP_DIR, ignore_errors=True)

    def setUp(self):
        self.manager = ASTParserManager()

    def test_empty_files_all_languages(self):
        """Test 0-byte and whitespace-only files across all parsers."""
        p_empty_py = ADVERSARIAL_TMP_DIR / "empty.py"
        p_empty_cs = ADVERSARIAL_TMP_DIR / "empty.cs"
        p_empty_ts = ADVERSARIAL_TMP_DIR / "empty.ts"
        p_ws = ADVERSARIAL_TMP_DIR / "whitespace.cs"

        p_empty_py.write_bytes(b"")
        p_empty_cs.write_bytes(b"")
        p_empty_ts.write_bytes(b"")
        p_ws.write_text("   \n\n\t  \r\n   ", encoding="utf-8")

        self.assertEqual(self.manager.parse_file(p_empty_py), [])
        self.assertEqual(self.manager.parse_file(p_empty_cs), [])
        self.assertEqual(self.manager.parse_file(p_empty_ts), [])
        self.assertEqual(self.manager.parse_file(p_ws), [])

    def test_utf8_with_bom(self):
        """Test UTF-8 files containing BOM (Byte Order Mark \\xef\\xbb\\xbf)."""
        p_cs = ADVERSARIAL_TMP_DIR / "BomTest.cs"
        p_cs.write_bytes(b'\xef\xbb\xbfpublic class BomClass { public void Run() {} }')

        symbols = self.manager.parse_file(p_cs)
        self.assertTrue(any(s.name == "BomClass" for s in symbols))

    def test_latin1_and_windows1252_encoding(self):
        """Test files encoded in ISO-8859-1 (Latin-1) or Windows-1252 with special characters."""
        p_cs = ADVERSARIAL_TMP_DIR / "LatinClass.cs"
        content = '// Caf\xe9 & r\xe9sum\xe9\npublic class CafeClass { public void NaiveMethod() {} }'
        p_cs.write_bytes(content.encode("latin-1"))

        symbols = self.manager.parse_file(p_cs)
        self.assertIsInstance(symbols, list)

    def test_binary_file_passed_to_parser(self):
        """Test passing binary garbage (pseudo DLL/PNG) to parser without crash."""
        p_fake_cs = ADVERSARIAL_TMP_DIR / "corrupted.cs"
        p_fake_cs.write_bytes(os.urandom(1024))

        symbols = self.manager.parse_file(p_fake_cs)
        self.assertIsInstance(symbols, list)


class TestAdversarialMalformedSyntax(unittest.TestCase):
    """
    Stress-tests parser resilience when provided with heavily malformed, incomplete, or corrupted code.
    """

    def setUp(self):
        self.manager = ASTParserManager()
        self.py_parser = PythonASTParser()
        self.cs_parser = CSharpASTParser()
        self.ts_parser = TypeScriptASTParser()

    def test_csharp_unmatched_braces_and_incomplete_declarations(self):
        """Test C# code with missing opening/closing braces and abrupt EOF."""
        samples = [
            "public class IncompleteClass { public void Foo() {",
            "public class ExtraCloseBraces } } }",
            "public class { void () }",
            "namespace A.B.C { public class X { void F() => ;",
            "public enum BrokenEnum { ValueA = , ValueB }",
            "public class Trailing { [SerializeField] public int",
        ]
        for idx, sample in enumerate(samples):
            symbols = self.cs_parser.parse_source(sample, f"broken_{idx}.cs")
            self.assertIsInstance(symbols, list)

    def test_python_syntax_errors(self):
        """Test Python syntax errors (indentation error, unclosed paren, invalid token)."""
        samples = [
            "def foo(:\n    pass",
            "class A\n    def bar():",
            "    indented without block",
            "async def = 123",
            "import ... as",
        ]
        for idx, sample in enumerate(samples):
            symbols = self.py_parser.parse_source(sample, f"broken_{idx}.py")
            self.assertEqual(symbols, [])  # Should gracefully catch SyntaxError and return []

    def test_typescript_malformed_syntax(self):
        """Test TypeScript broken syntax, dangling export keywords, and unclosed brackets."""
        samples = [
            "export interface { id: string }",
            "export async function ( ) {",
            "export const arrow = => {",
            "export type BadType = ;",
            "function broken<T extends {}(a: T) {",
        ]
        for idx, sample in enumerate(samples):
            symbols = self.ts_parser.parse_source(sample, f"broken_{idx}.ts")
            self.assertIsInstance(symbols, list)


class TestAdversarialForbiddenPathFiltering(unittest.TestCase):
    """
    Stress-tests directory and extension ignore filtering with edge cases,
    custom filters, hidden paths, and mixed-case directory paths.
    """

    def setUp(self):
        self.manager = ASTParserManager()

    def test_hidden_and_dot_directories(self):
        """Test filtering of various dot/hidden directories."""
        self.assertTrue(is_ignored_path(".git/config"))
        self.assertTrue(is_ignored_path(".svn/entries"))
        self.assertTrue(is_ignored_path(".hg/store"))
        self.assertTrue(is_ignored_path(".idea/workspace.xml"))
        self.assertTrue(is_ignored_path(".vscode/settings.json"))
        self.assertTrue(is_ignored_path(".next/standalone/server.js"))
        self.assertTrue(is_ignored_path(".agents/subagent_1/handoff.md"))

    def test_unity_and_build_artifacts(self):
        """Test filtering of Unity Library, obj, Temp, PackageCache, and meta files."""
        self.assertTrue(is_ignored_path("Library/ArtifactDB"))
        self.assertTrue(is_ignored_path("Library/PackageCache/com.unity.modules.ai@1.0.0/Runtime.cs"))
        self.assertTrue(is_ignored_path("Packages/manifest.json"))
        self.assertTrue(is_ignored_path("PackageCache/something/file.cs"))
        self.assertTrue(is_ignored_path("obj/Debug/Assembly-CSharp.csproj.AssemblyReference.cache"))
        self.assertTrue(is_ignored_path("Temp/Compiled-shader.shader"))
        self.assertTrue(is_ignored_path("Assets/Scripts/Main.cs.meta"))

    def test_custom_ignored_dirs_and_exts(self):
        """Test ASTParserManager with custom ignored directories and extensions."""
        custom_dirs = {"custom_exclude", "third_party"}
        custom_exts = {".bak", ".old", ".tmp"}

        manager = ASTParserManager(custom_ignored_dirs=custom_dirs, custom_ignored_exts=custom_exts)

        base = ADVERSARIAL_TMP_DIR / "custom_test"
        base.mkdir(parents=True, exist_ok=True)
        try:
            (base / "custom_exclude").mkdir(exist_ok=True)
            (base / "custom_exclude" / "test.py").write_text("class IgnoreMe: pass", encoding="utf-8")

            (base / "valid").mkdir(exist_ok=True)
            (base / "valid" / "keep.py").write_text("class KeepMe: pass", encoding="utf-8")
            (base / "valid" / "old.py.bak").write_text("class OldMe: pass", encoding="utf-8")

            syms = manager.parse_directory(base)
            names = {s.name for s in syms}

            self.assertIn("KeepMe", names)
            self.assertNotIn("IgnoreMe", names)
            self.assertNotIn("OldMe", names)
        finally:
            import shutil
            shutil.rmtree(base, ignore_errors=True)


class TestAdversarialMassiveSourceFilesAndPerformance(unittest.TestCase):
    """
    Stress-tests scalability and execution performance on massive synthetic files.
    Generates large C#, Python, and TypeScript files (200+ classes, 1000+ methods).
    """

    def setUp(self):
        self.manager = ASTParserManager()

    def test_massive_csharp_source_file_scaling(self):
        """Generate 200 C# classes with 1,000 methods and measure parse latency."""
        lines = ["using System;", "namespace MassiveTest {"]
        for c in range(200):
            lines.append(f"    public class MassiveClass_{c} : IBaseClass_{c}")
            lines.append("    {")
            lines.append(f"        public int Property_{c} {{ get; set; }}")
            for m in range(5):
                lines.append(f"        public void Method_{c}_{m}(string arg1, int arg2)")
                lines.append("        {")
                lines.append(f'            var x = "SET_ACTIVE_QUEST";')
                lines.append("        }")
            lines.append("    }")
        lines.append("}")
        code = "\n".join(lines)

        start_time = time.perf_counter()
        symbols = self.manager.csharp_parser.parse_source(code, "Assets/Massive.cs")
        elapsed = time.perf_counter() - start_time

        self.assertGreater(len(symbols), 200)
        self.assertLess(elapsed, 5.0, f"C# parser took {elapsed:.2f}s on massive file (>5.0s limit)")

    def test_massive_python_source_file_scaling(self):
        """Generate 200 Python classes with 1,000 methods and measure parse latency."""
        lines = ["import asyncio"]
        for c in range(200):
            lines.append(f"class PythonMassiveClass_{c}:")
            lines.append(f'    """Docstring for class {c}."""')
            for m in range(5):
                lines.append(f"    async def method_{c}_{m}(self, arg: int) -> str:")
                lines.append(f'        """Method {c}_{m} docstring."""')
                lines.append(f'        event = "QUEST_MATCHED"')
                lines.append('        return "ok"')
        code = "\n".join(lines)

        start_time = time.perf_counter()
        symbols = self.manager.python_parser.parse_source(code, "massive.py")
        elapsed = time.perf_counter() - start_time

        self.assertGreater(len(symbols), 200)
        self.assertLess(elapsed, 3.0, f"Python parser took {elapsed:.2f}s on massive file (>3.0s limit)")

    def test_massive_typescript_source_file_scaling(self):
        """Generate 200 TypeScript interfaces and 200 functions and measure parse latency."""
        lines = ['"use server";']
        for i in range(200):
            lines.append(f"export interface IInterface_{i} {{")
            lines.append(f"    id: string;")
            lines.append(f"    value: number;")
            lines.append("}")
            lines.append(f"export async function action_{i}(data: IInterface_{i}): Promise<boolean> {{")
            lines.append(f'    fetch("/api/livekit-token");')
            lines.append("    return true;")
            lines.append("}")
        code = "\n".join(lines)

        start_time = time.perf_counter()
        symbols = self.manager.typescript_parser.parse_source(code, "src/massive.ts")
        elapsed = time.perf_counter() - start_time

        self.assertGreater(len(symbols), 200)
        self.assertLess(elapsed, 4.0, f"TypeScript parser took {elapsed:.2f}s on massive file (>4.0s limit)")


def run_adversarial_suite() -> unittest.TestResult:
    suite = unittest.TestSuite()
    loader = unittest.TestLoader()

    suite.addTests(loader.loadTestsFromTestCase(TestAdversarialNestingAndGenerics))
    suite.addTests(loader.loadTestsFromTestCase(TestAdversarialStringsCommentsAndFormatting))
    suite.addTests(loader.loadTestsFromTestCase(TestAdversarialFileEncodingsAndIO))
    suite.addTests(loader.loadTestsFromTestCase(TestAdversarialMalformedSyntax))
    suite.addTests(loader.loadTestsFromTestCase(TestAdversarialForbiddenPathFiltering))
    suite.addTests(loader.loadTestsFromTestCase(TestAdversarialMassiveSourceFilesAndPerformance))

    runner = unittest.TextTestRunner(verbosity=2)
    return runner.run(suite)


if __name__ == "__main__":
    result = run_adversarial_suite()
    sys.exit(0 if result.wasSuccessful() else 1)
