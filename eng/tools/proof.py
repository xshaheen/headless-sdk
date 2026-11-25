#!/usr/bin/env python3
"""Run verification stages and collect their results into one proof bundle.

An agent's claim that a change works is only as good as the evidence attached to it. This tool
records every stage it runs (command, exit code, duration, full log) and then reduces the raw
outputs (compiler log, TRX files, analyzer report, Cobertura coverage) into summary.json for
machines and summary.md for the author to condense into a pull request body. Standard library only.

Commands:
  run        Run one stage command, tee its output to <dir>/<name>.log, record its result, and
             exit with the command's status.
  summarize  Write <dir>/summary.json and <dir>/summary.md from what the stages left in <dir>.
  key        Print the reuse key: the content id of the working tree (tracked and untracked files,
             ignored ones excluded) followed by each --input NAME=VALUE that changes the verdict.
  reuse      Print the newest passing bundle under --root whose key equals --key; exit 1 if none.
"""

from __future__ import annotations

import argparse
import json
import os
import re
import shutil
import subprocess
import tempfile
import sys
import time
import xml.etree.ElementTree as ET
from dataclasses import asdict, dataclass, field
from datetime import datetime, timezone
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
TRX_NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
# MSBuild canonical diagnostic: path(line,col): warning CODE: message [project]
DIAGNOSTIC = re.compile(
    r"^(?P<file>[^(\s][^(]*)\((?P<line>\d+)(?:,\d+)?\): (?P<severity>warning|error) (?P<id>[A-Za-z]+\d+): (?P<message>.*?)(?: \[(?P<project>[^\]]+)\])?$"
)
MAX_LISTED = 50


@dataclass
class Stage:
    name: str
    command: list[str]
    exit_code: int
    seconds: float
    log: str
    # A skipped stage records exit_code -1 and why it did not run, e.g. tests after a failed build.
    note: str = ""


@dataclass
class TestModule:
    module: str
    passed: int = 0
    failed: int = 0
    skipped: int = 0
    failures: list[dict[str, str]] = field(default_factory=list)


_ROOT_PREFIX = REPO_ROOT.as_posix() + "/"


def relative(path: str) -> str:
    # Tools report absolute paths under the checkout, so a prefix strip is enough; resolving every
    # path costs a filesystem call each, and an analyzer report carries tens of thousands.
    return path.removeprefix(_ROOT_PREFIX)


def run_stage(directory: Path, name: str, command: list[str]) -> int:
    directory.mkdir(parents=True, exist_ok=True)
    log_path = directory / f"{name}.log"
    started = time.monotonic()
    with log_path.open("w", encoding="utf-8") as log:
        process = subprocess.Popen(command, cwd=REPO_ROOT, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
        assert process.stdout is not None
        for line in process.stdout:
            sys.stdout.write(line)
            log.write(line)
        exit_code = process.wait()
    stage = Stage(name, command, exit_code, round(time.monotonic() - started, 1), log_path.name)
    with (directory / "stages.jsonl").open("a", encoding="utf-8") as stages:
        stages.write(json.dumps(asdict(stage)) + "\n")
    return exit_code


def skip_stage(directory: Path, name: str, reason: str) -> int:
    directory.mkdir(parents=True, exist_ok=True)
    print(f"[proof] {name} skipped: {reason}")
    stage = Stage(name, [], -1, 0.0, "", reason)
    with (directory / "stages.jsonl").open("a", encoding="utf-8") as stages:
        stages.write(json.dumps(asdict(stage)) + "\n")
    return 0


def read_stages(directory: Path) -> list[Stage]:
    path = directory / "stages.jsonl"
    if not path.exists():
        return []
    return [Stage(**json.loads(line)) for line in path.read_text(encoding="utf-8").splitlines() if line]


def compiler_diagnostics(directory: Path) -> list[dict[str, str]]:
    seen: set[tuple[str, ...]] = set()
    diagnostics: list[dict[str, str]] = []
    for log in sorted(directory.glob("*.log")):
        for line in log.read_text(encoding="utf-8", errors="replace").splitlines():
            match = DIAGNOSTIC.match(line.strip())
            if not match:
                continue
            key = (match["file"], match["line"], match["id"])
            if key in seen:
                continue
            seen.add(key)
            diagnostics.append(
                {
                    "severity": match["severity"],
                    "id": match["id"],
                    "file": relative(match["file"]),
                    "line": match["line"],
                    "message": match["message"],
                }
            )
    return diagnostics


def analyzer_findings(directory: Path, project_dirs: list[str]) -> list[dict[str, str]]:
    """Findings in the analyzed projects only: `dotnet format` also reports on referenced projects."""
    findings: list[dict[str, str]] = []
    for report in sorted(directory.rglob("format-report.json")):
        for document in json.loads(report.read_text(encoding="utf-8")):
            for change in document.get("FileChanges", []):
                description: str = change.get("FormatDescription", "")
                severity, _, message = description.partition(" ")
                if severity == "hidden":
                    continue
                file = relative(document.get("FilePath", ""))
                if project_dirs and not file.startswith(tuple(project_dirs)):
                    continue
                findings.append(
                    {
                        "severity": severity,
                        "id": change.get("DiagnosticId", ""),
                        "file": file,
                        "line": str(change.get("LineNumber", "")),
                        "message": message.split(": ", 1)[-1],
                    }
                )
    return findings


def test_modules(directory: Path) -> list[TestModule]:
    modules: list[TestModule] = []
    for trx in sorted(directory.rglob("*.trx")):
        root = ET.parse(trx).getroot()
        module = TestModule(module=re.sub(r"_net[\d.]+_\w+$", "", trx.stem))
        for result in root.iterfind(".//t:Results/t:UnitTestResult", TRX_NS):
            outcome = result.get("outcome", "")
            if outcome == "Passed":
                module.passed += 1
            elif outcome in ("Failed", "Error", "Timeout", "Aborted"):
                module.failed += 1
                message = result.findtext(".//t:ErrorInfo/t:Message", default="", namespaces=TRX_NS).strip()
                module.failures.append({"test": result.get("testName", ""), "message": message.splitlines()[0] if message else outcome})
            else:
                module.skipped += 1
        modules.append(module)
    return modules


def coverage(directory: Path, assemblies: set[str]) -> list[dict[str, object]]:
    merged = directory / "coverage" / "merged.cobertura.xml"
    if not merged.exists():
        return []
    rows: list[dict[str, object]] = []
    for package in ET.parse(merged).getroot().iterfind(".//package"):
        name = package.get("name", "")
        if assemblies and name not in assemblies:
            continue
        rows.append(
            {
                "assembly": name,
                "line": round(float(package.get("line-rate", 0)) * 100, 1),
                "branch": round(float(package.get("branch-rate", 0)) * 100, 1),
            }
        )
    return sorted(rows, key=lambda row: str(row["assembly"]))


def git(*args: str) -> str:
    return subprocess.run(["git", *args], cwd=REPO_ROOT, capture_output=True, text=True, check=False).stdout.strip()


def working_tree_id() -> str:
    """Hash the working tree through a throwaway index, so the real index and stash stay untouched."""
    git_dir = Path(git("rev-parse", "--absolute-git-dir"))
    with tempfile.TemporaryDirectory() as scratch:
        index = Path(scratch) / "index"
        if (git_dir / "index").exists():
            shutil.copyfile(git_dir / "index", index)
        env = {**os.environ, "GIT_INDEX_FILE": str(index)}
        subprocess.run(["git", "add", "--all", "--", "."], cwd=REPO_ROOT, env=env, check=True, capture_output=True)
        return subprocess.run(
            ["git", "write-tree"], cwd=REPO_ROOT, env=env, check=True, capture_output=True, text=True
        ).stdout.strip()


def reuse_key(inputs: list[str]) -> str:
    """The sources plus every input that changes what a run proves, such as the consumer SDK the
    suite builds with: a pass on one SDK or configuration is no proof for another."""
    return " ".join([working_tree_id(), *sorted(inputs)])


def reuse(root: Path, key: str) -> int:
    """A passing bundle for the same key proves it again; only its packages must still exist."""
    for bundle in sorted(root.glob("*-verify"), reverse=True):
        summary_path = bundle / "summary.json"
        if not summary_path.exists() or not (bundle / "packages").is_dir():
            continue
        summary = json.loads(summary_path.read_text(encoding="utf-8"))
        if summary.get("verdict") == "pass" and summary.get("reuse_key") == key:
            print(bundle)
            return 0
    return 1


def summarize(directory: Path) -> int:
    stages = read_stages(directory)
    # The key the run started on, kept only when the sources did not change while it ran: an edit
    # mid-run means no single tree was proven, so the bundle must never be reused.
    key_path = directory / "reuse-key"
    started_key = key_path.read_text(encoding="utf-8").strip() if key_path.exists() else ""
    key = started_key if started_key and started_key.split(" ", 1)[0] == working_tree_id() else None
    affected_path = directory / "affected.json"
    affected = json.loads(affected_path.read_text(encoding="utf-8")) if affected_path.exists() else {}
    changed_assemblies: set[str] = set()

    diagnostics = compiler_diagnostics(directory)
    findings = analyzer_findings(directory, [])
    modules = test_modules(directory)
    coverage_rows = coverage(directory, changed_assemblies)
    # `dotnet format --verify-no-changes` exits 2 for hidden-severity findings too, which the gate
    # ignores, so the analyzers stage passes or fails on its visible findings instead.
    for stage in stages:
        if stage.name == "analyzers" and stage.exit_code in (0, 2):
            stage.exit_code = 2 if findings else 0
    failed_stages = [stage.name for stage in stages if stage.exit_code > 0]

    summary = {
        "generated_at": datetime.now(timezone.utc).isoformat(timespec="seconds"),
        "commit": git("rev-parse", "HEAD"),
        "dirty": bool(git("status", "--porcelain")),
        "reuse_key": key,
        "base": affected.get("base"),
        "verdict": "fail" if failed_stages else "pass",
        "stages": [asdict(stage) for stage in stages],

        "tests": {
            "passed": sum(m.passed for m in modules),
            "failed": sum(m.failed for m in modules),
            "skipped": sum(m.skipped for m in modules),
            "modules": [asdict(m) for m in modules],
        },
        "compiler_diagnostics": diagnostics,
        "analyzer_findings": findings,
        "coverage": coverage_rows,
    }
    (directory / "summary.json").write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
    (directory / "summary.md").write_text(render_markdown(summary), encoding="utf-8")
    print((directory / "summary.md").read_text(encoding="utf-8"))
    return 1 if failed_stages else 0


def render_markdown(summary: dict) -> str:
    tests = summary["tests"]
    lines = [
        f"## Verification: {summary['verdict'].upper()}",
        "",
        f"Commit `{summary['commit'][:12]}`{' with uncommitted changes' if summary['dirty'] else ''}, base `{summary['base']}`.",
        "",
        "| Stage | Result | Time |",
        "| --- | --- | --- |",
    ]
    for stage in summary["stages"]:
        exit_code = stage["exit_code"]
        result = "pass" if exit_code == 0 else f"skipped: {stage.get('note', '')}" if exit_code < 0 else f"fail ({exit_code})"
        lines.append(f"| `{stage['name']}` | {result} | {stage['seconds']}s |")



    lines += ["", f"### Tests: {tests['passed']} passed, {tests['failed']} failed, {tests['skipped']} skipped", ""]
    if tests["modules"]:
        lines += ["| Module | Passed | Failed | Skipped |", "| --- | --- | --- | --- |"]
        lines += [f"| {m['module']} | {m['passed']} | {m['failed']} | {m['skipped']} |" for m in tests["modules"]]
    failures = [(m["module"], f) for m in tests["modules"] for f in m["failures"]]
    for module, failure in failures[:MAX_LISTED]:
        lines.append(f"- `{module}` `{failure['test']}`: {failure['message']}")

    for title, key in (("Compiler diagnostics", "compiler_diagnostics"), ("Analyzer findings", "analyzer_findings")):
        items = summary[key]
        lines += ["", f"### {title}: {len(items)}", ""]
        by_id: dict[str, int] = {}
        for item in items:
            by_id[item["id"]] = by_id.get(item["id"], 0) + 1
        if by_id:
            lines.append(", ".join(f"`{rule}` x{count}" for rule, count in sorted(by_id.items(), key=lambda kv: -kv[1])))
            lines.append("")
        for item in items[:MAX_LISTED]:
            lines.append(f"- {item['severity']} `{item['id']}` {item['file']}:{item['line']} {item['message']}")
        if len(items) > MAX_LISTED:
            lines.append(f"- ... {len(items) - MAX_LISTED} more in summary.json")

    if summary["coverage"]:
        lines += ["", "### Coverage of changed assemblies", "", "| Assembly | Line % | Branch % |", "| --- | --- | --- |"]
        lines += [f"| {row['assembly']} | {row['line']} | {row['branch']} |" for row in summary["coverage"]]
    return "\n".join(lines) + "\n"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    commands = parser.add_subparsers(dest="command", required=True)
    run = commands.add_parser("run", help="run and record one stage")
    run.add_argument("--dir", required=True, type=Path)
    run.add_argument("--name", required=True)
    run.add_argument("stage_command", nargs=argparse.REMAINDER, help="command after --")
    skip = commands.add_parser("skip", help="record a stage that did not run, and why")
    skip.add_argument("--dir", required=True, type=Path)
    skip.add_argument("--name", required=True)
    skip.add_argument("--reason", required=True)
    summary = commands.add_parser("summarize", help="write summary.json and summary.md")
    summary.add_argument("--dir", required=True, type=Path)
    key_parser = commands.add_parser("key", help="print the reuse key: tree id plus each --input")
    key_parser.add_argument("--input", action="append", default=[], metavar="NAME=VALUE")
    reuse_parser = commands.add_parser("reuse", help="print a passing bundle that proved --key")
    reuse_parser.add_argument("--root", required=True, type=Path)
    reuse_parser.add_argument("--key", required=True)
    args = parser.parse_args()

    if args.command == "key":
        print(reuse_key(args.input))
        return 0
    if args.command == "reuse":
        return reuse(args.root, args.key)

    if args.command == "run":
        command = args.stage_command[1:] if args.stage_command[:1] == ["--"] else args.stage_command
        if not command:
            parser.error("run needs a command after --")
        return run_stage(args.dir, args.name, command)
    if args.command == "skip":
        return skip_stage(args.dir, args.name, args.reason)
    return summarize(args.dir)


if __name__ == "__main__":
    sys.exit(main())
