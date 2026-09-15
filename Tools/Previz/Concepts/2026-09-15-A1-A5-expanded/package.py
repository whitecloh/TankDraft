#!/usr/bin/env python3
"""Package the approved A1/A5 concept expansion from its generation manifest.

The manifest is deliberately external to this artifact so later generation runs can
replace sources and re-run this script without editing previews or Unity assets.
"""

from __future__ import annotations

import hashlib
import json
import shutil
import sys
from pathlib import Path
from typing import Any

from PIL import Image


PROJECT = Path(r"U:\UNITY_PROJECTS\TankDraft")
OUTPUT = PROJECT / "Tools" / "Previz" / "Concepts" / "2026-09-15-A1-A5-expanded"
INPUT = PROJECT / "Logs" / "A1A5Expansion20260915" / "package-input.json"
PUBLIC_ROOT = "U:/UNITY_PROJECTS/TankDraft/Tools/Previz/Concepts/2026-09-15-A1-A5-expanded"
GUTTER_HALF = 4


def public(path: Path) -> str:
    return f"{PUBLIC_ROOT}/{path.relative_to(OUTPUT).as_posix()}"


def ensure_parent(path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)


def remove_generated(path: Path) -> None:
    """Remove only a file that belongs to this package on a later incomplete run."""
    path.unlink(missing_ok=True)


def copy_png(source: Path, destination: Path) -> None:
    ensure_parent(destination)
    shutil.copyfile(source, destination)


def crop_pair(source: Path, left_destination: Path, right_destination: Path) -> None:
    """Crop a centered pair, dropping four pixels at each side of the divider."""
    with Image.open(source) as image:
        width, height = image.size
        if width % 2:
            raise ValueError(f"Pair width must be even: {source} is {width}px")
        midpoint = width // 2
        if midpoint <= GUTTER_HALF:
            raise ValueError(f"Pair is too narrow to crop: {source}")
        ensure_parent(left_destination)
        ensure_parent(right_destination)
        image.crop((0, 0, midpoint - GUTTER_HALF, height)).save(left_destination)
        image.crop((midpoint + GUTTER_HALF, 0, width, height)).save(right_destination)


def compose_comparison(a1: Path, a5: Path, destination: Path) -> None:
    """Create a derived baseline comparison after the accepted pairs are cropped."""
    with Image.open(a1) as left, Image.open(a5) as right:
        if left.size != right.size:
            raise ValueError(f"Baseline crops differ in size: {a1} / {a5}")
        canvas = Image.new("RGBA", (left.width * 2, left.height))
        canvas.paste(left.convert("RGBA"), (0, 0))
        canvas.paste(right.convert("RGBA"), (left.width, 0))
        ensure_parent(destination)
        canvas.save(destination)


def image_check(path: Path) -> dict[str, Any]:
    with Image.open(path) as image:
        rgba = image.convert("RGBA")
        alpha = rgba.getchannel("A").getextrema()
        return {
            "path": public(path),
            "dimensions": {"width": image.width, "height": image.height},
            "mode": image.mode,
            "alpha": {"min": alpha[0], "max": alpha[1]},
            "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
        }


def clean_sources(value: Any) -> Any:
    if isinstance(value, list):
        return [clean_sources(item) for item in value]
    if isinstance(value, dict):
        return {key: clean_sources(item) for key, item in value.items() if key != "source"}
    return value


def markdown_link(label: str, path: Path) -> str:
    return f"[{label}]({public(path)})"


def write_readme(screens: list[dict[str, Any]], sprites: list[dict[str, Any]], results: dict[str, Any]) -> None:
    rows = [
        "# Расширенные визуальные превизы A1/A5",
        "",
        "В пакете находятся концепт-превизы двух выбранных визуальных направлений. Это **не runtime screenshots**. Статичные sprite sheets — **не анимации и не production atlases**.",
        "",
        "Техника: 16 authored, 2 child и 15 proposed концептов. См. [roster.md](" + public(OUTPUT / "roster.md") + ").",
        "",
        "Для просмотра сравнений на одной странице откройте [Screens.md](" + public(OUTPUT / "Screens.md") + ").",
        "",
        "## Экраны",
        "",
        "| ID | Экран | A1 light ink | A5 dark graphic | Сравнение | Статус |",
        "| --- | --- | --- | --- | --- | --- |",
    ]
    for screen in screens:
        screen_id = screen["id"]
        a1 = OUTPUT / "A1" / "screens" / f"{screen_id}.png"
        a5 = OUTPUT / "A5" / "screens" / f"{screen_id}.png"
        comparison = OUTPUT / "comparisons" / f"{screen_id}.png"
        status = results["screens"].get(screen_id, {}).get("status", "missing")
        status_label = "Готов" if status == "ready" else "Ожидает"
        comparison_cell = markdown_link("сравнение", comparison) if comparison.exists() else "ожидает"
        a1_cell = markdown_link("A1", a1) if a1.exists() else "ожидает"
        a5_cell = markdown_link("A5", a5) if a5.exists() else "ожидает"
        rows.append(f"| `{screen_id}` | {screen.get('name', screen_id)} | {a1_cell} | {a5_cell} | {comparison_cell} | {status_label} |")
    rows.extend(["", "## Листы спрайтов", ""])
    for job in sprites:
        style = job.get("style", "unknown")
        sheet = job.get("sheet", "unknown")
        image = OUTPUT / style / "sprites" / f"sheet-{sheet}.png"
        status = results["sprites"].get(f"{style}-{sheet}", {}).get("status", "missing")
        if image.exists():
            with Image.open(image) as sprite:
                alpha_min, alpha_max = sprite.convert("RGBA").getchannel("A").getextrema()
            transparency = "Прозрачный фон (alpha 0–255)." if (alpha_min, alpha_max) == (0, 255) else ""
            preview = f"![]({public(image)})"
        else:
            transparency = ""
            preview = "ожидает источник"
        status_label = "Готов" if status == "ready" else "Ожидает"
        rows.extend([f"### {style} — sheet {sheet} ({status_label})", "", transparency, preview, ""])
    (OUTPUT / "README.md").write_text("\n".join(rows), encoding="utf-8")


def write_screens(screens: list[dict[str, Any]], results: dict[str, Any]) -> None:
    rows = [
        "# Сравнительные превизы экранов",
        "",
        "Концепт-превизы, не runtime screenshots. Каждое готовое сравнение показано ниже; для частичного входного манифеста отсутствующие источники отмечены как ожидаемые.",
        "",
    ]
    for screen in screens:
        screen_id = screen["id"]
        comparison = OUTPUT / "comparisons" / f"{screen_id}.png"
        a1 = OUTPUT / "A1" / "screens" / f"{screen_id}.png"
        a5 = OUTPUT / "A5" / "screens" / f"{screen_id}.png"
        rows.extend([f"## `{screen_id}` — {screen.get('name', screen_id)}", ""])
        if results["screens"].get(screen_id, {}).get("status") == "ready" and comparison.exists():
            rows.extend([f"[A1]({public(a1)}) · [A5]({public(a5)}) · [полное сравнение]({public(comparison)})", "", f"![]({public(comparison)})", ""])
        else:
            rows.extend(["Ожидает источник.", ""])
    (OUTPUT / "Screens.md").write_text("\n".join(rows), encoding="utf-8")


def write_roster(roster: list[dict[str, Any]]) -> None:
    rows = [
        "# Состав техники",
        "",
        "16 authored машин, 2 child-юнита и 15 proposed концептов. Координаты указаны с единицы внутри каждого sprite sheet 4×3.",
        "",
        "| # | ID | Название | Статус | Лист | Ряд | Столбец |",
        "| --- | --- | --- | --- | --- | --- | --- |",
    ]
    for unit in sorted(roster, key=lambda item: item.get("index", 0)):
        rows.append(
            f"| {unit.get('index', '')} | `{unit.get('id', '')}` | {unit.get('name', '')} | {unit.get('status', '')} | {unit.get('sheet', '')} | {int(unit.get('row', 0)) + 1} | {int(unit.get('column', 0)) + 1} |"
        )
    (OUTPUT / "roster.md").write_text("\n".join(rows) + "\n", encoding="utf-8")


def add_baselines(data: dict[str, Any]) -> list[dict[str, Any]]:
    """Expose the approved menu/battle crops as the two first screen IDs."""
    return [
        {"id": "arena", "name": "Базовая арена", "type": "baseline"},
        {"id": "battle", "name": "Базовый бой", "type": "baseline"},
        *data.get("screens", []),
    ]


def main() -> int:
    if not INPUT.exists():
        print(f"Missing input: {INPUT}", file=sys.stderr)
        return 2
    data = json.loads(INPUT.read_text(encoding="utf-8"))
    OUTPUT.mkdir(parents=True, exist_ok=True)
    screen_jobs = data.get("screens", [])
    sprite_jobs = data.get("spriteJobs", data.get("sprites", []))
    results: dict[str, Any] = {"screens": {}, "sprites": {}, "missing": []}

    # The old accepted A1/A5 menu-battle pairs are split by screen type, not by art style.
    # Migration cleanup for the first partial package run, where arena was named menu.
    for old_menu in (OUTPUT / "A1" / "screens" / "menu.png", OUTPUT / "A5" / "screens" / "menu.png", OUTPUT / "comparisons" / "menu.png"):
        remove_generated(old_menu)

    for screen_id, side in (("arena", "left"), ("battle", "right")):
        available = True
        for style in ("A1", "A5"):
            source_text = data.get("baseline", {}).get(style)
            source = Path(source_text) if source_text else None
            destination = OUTPUT / style / "screens" / f"{screen_id}.png"
            if not source or not source.exists():
                available = False
                results["missing"].append({"kind": "baseline", "id": f"{style}-{screen_id}", "source": source_text})
                remove_generated(destination)
                continue
            temporary_left = OUTPUT / ".tmp" / f"{style}-left.png"
            temporary_right = OUTPUT / ".tmp" / f"{style}-right.png"
            crop_pair(source, temporary_left, temporary_right)
            ensure_parent(destination)
            shutil.move(temporary_left if side == "left" else temporary_right, destination)
            other = temporary_right if side == "left" else temporary_left
            other.unlink(missing_ok=True)
        comparison = OUTPUT / "comparisons" / f"{screen_id}.png"
        a1_crop = OUTPUT / "A1" / "screens" / f"{screen_id}.png"
        a5_crop = OUTPUT / "A5" / "screens" / f"{screen_id}.png"
        if available:
            compose_comparison(a1_crop, a5_crop, comparison)
        else:
            remove_generated(comparison)
        results["screens"][screen_id] = {"status": "ready" if available else "missing"}

    for job in screen_jobs:
        screen_id = job["id"]
        source_text = job.get("source")
        source = Path(source_text) if source_text else None
        if not source or not source.exists():
            results["screens"][screen_id] = {"status": "missing", "source": source_text}
            results["missing"].append({"kind": "screen-pair", "id": screen_id, "source": source_text})
            remove_generated(OUTPUT / "comparisons" / f"{screen_id}.png")
            remove_generated(OUTPUT / "A1" / "screens" / f"{screen_id}.png")
            remove_generated(OUTPUT / "A5" / "screens" / f"{screen_id}.png")
            continue
        comparison = OUTPUT / "comparisons" / f"{screen_id}.png"
        copy_png(source, comparison)
        crop_pair(source, OUTPUT / "A1" / "screens" / f"{screen_id}.png", OUTPUT / "A5" / "screens" / f"{screen_id}.png")
        results["screens"][screen_id] = {"status": "ready", "source": source_text}

    for job in sprite_jobs:
        style, sheet = job.get("style"), job.get("sheet")
        key = f"{style}-{sheet}"
        source_text = job.get("source")
        source = Path(source_text) if source_text else None
        if not source or not source.exists():
            results["sprites"][key] = {"status": "missing", "source": source_text}
            results["missing"].append({"kind": "sprite-sheet", "id": key, "source": source_text})
            remove_generated(OUTPUT / str(style) / "sprites" / f"sheet-{sheet}.png")
            continue
        copy_png(source, OUTPUT / str(style) / "sprites" / f"sheet-{sheet}.png")
        results["sprites"][key] = {"status": "ready", "source": source_text}

    shutil.rmtree(OUTPUT / ".tmp", ignore_errors=True)
    all_screens = add_baselines(data)
    (OUTPUT / "prompts.json").write_text(json.dumps(clean_sources(data), ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    (OUTPUT / "results.json").write_text(json.dumps(results, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    write_roster(data.get("roster", []))
    write_readme(all_screens, sprite_jobs, results)
    write_screens(all_screens, results)
    checks = [image_check(path) for path in sorted(OUTPUT.rglob("*.png"))]
    (OUTPUT / "checks.json").write_text(json.dumps(checks, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"readyScreens": sum(item["status"] == "ready" for item in results["screens"].values()), "missing": len(results["missing"]), "readySprites": sum(item["status"] == "ready" for item in results["sprites"].values())}))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
