from __future__ import annotations

import csv
import sys
import traceback
from pathlib import Path
from typing import Any

import pythoncom
import tkinter as tk
from tkinter import filedialog, messagebox
import win32com.client


AUTOCAD_PROG_IDS = [
    "AutoCAD.Application",
    "AutoCAD.Application.15",
    "AutoCAD.Application.15.1",
]


def choose_dwg_file() -> Path | None:
    root = tk.Tk()
    root.withdraw()
    root.attributes("-topmost", True)

    selected = filedialog.askopenfilename(
        title="확인할 DWG 파일 선택",
        filetypes=[
            ("AutoCAD DWG 파일", "*.dwg"),
            ("모든 파일", "*.*"),
        ],
    )

    root.destroy()

    if not selected:
        return None

    return Path(selected)


def connect_autocad():
    errors: list[str] = []

    for prog_id in AUTOCAD_PROG_IDS:
        try:
            app = win32com.client.Dispatch(prog_id)
            app.Visible = True

            print(f"AutoCAD 연결 성공: {prog_id}")
            try:
                print(f"AutoCAD 버전: {app.Version}")
            except Exception:
                pass

            return app

        except Exception as exc:
            errors.append(f"{prog_id}: {exc}")

    error_text = "\n".join(errors)

    raise RuntimeError(
        "AutoCAD COM 연결에 실패했습니다.\n\n"
        f"{error_text}\n\n"
        "AutoCAD 2002가 정상 설치되어 있고 COM 등록이 되어 있는지 확인하세요."
    )


def safe_get(entity: Any, property_name: str, default: Any = "") -> Any:
    try:
        value = getattr(entity, property_name)

        if callable(value):
            return default

        return value
    except Exception:
        return default


def format_point(value: Any) -> str:
    try:
        values = list(value)

        if len(values) >= 3:
            return f"{values[0]:.6f}, {values[1]:.6f}, {values[2]:.6f}"

        return ", ".join(str(v) for v in values)

    except Exception:
        return ""


def get_bounding_box(entity: Any) -> tuple[str, str]:
    """
    GetBoundingBox는 객체의 최소점과 최대점을 반환한다.

    pywin32와 오래된 AutoCAD 조합에서는 반환 형식 차이가 있을 수 있어
    여러 방식으로 시도한다.
    """

    # 방식 1: 반환값으로 받는 경우
    try:
        result = entity.GetBoundingBox()

        if result and len(result) == 2:
            minimum, maximum = result
            return format_point(minimum), format_point(maximum)
    except Exception:
        pass

    # 방식 2: VARIANT 참조 인수를 요구하는 경우
    try:
        minimum = pythoncom.MakeVariant(pythoncom.VT_BYREF | pythoncom.VT_VARIANT, None)
        maximum = pythoncom.MakeVariant(pythoncom.VT_BYREF | pythoncom.VT_VARIANT, None)

        entity.GetBoundingBox(minimum, maximum)

        return format_point(minimum.value), format_point(maximum.value)
    except Exception:
        return "", ""


def get_entity_details(entity: Any) -> dict[str, str]:
    object_name = str(safe_get(entity, "ObjectName"))
    handle = str(safe_get(entity, "Handle"))
    layer = str(safe_get(entity, "Layer"))
    color = str(safe_get(entity, "Color"))
    linetype = str(safe_get(entity, "Linetype"))

    bbox_min, bbox_max = get_bounding_box(entity)

    details: dict[str, str] = {
        "ObjectName": object_name,
        "Handle": handle,
        "Layer": layer,
        "Color": color,
        "Linetype": linetype,
        "StartPoint": "",
        "EndPoint": "",
        "Center": "",
        "InsertionPoint": "",
        "TextPosition": "",
        "Radius": "",
        "Diameter": "",
        "TextString": "",
        "Coordinates": "",
        "BoundingBoxMin": bbox_min,
        "BoundingBoxMax": bbox_max,
    }

    if object_name == "AcDbLine":
        details["StartPoint"] = format_point(safe_get(entity, "StartPoint"))
        details["EndPoint"] = format_point(safe_get(entity, "EndPoint"))

    elif object_name in ("AcDbCircle", "AcDbArc"):
        details["Center"] = format_point(safe_get(entity, "Center"))

        radius = safe_get(entity, "Radius")
        if radius != "":
            try:
                details["Radius"] = f"{float(radius):.6f}"
                details["Diameter"] = f"{float(radius) * 2:.6f}"
            except Exception:
                details["Radius"] = str(radius)

    elif object_name in (
        "AcDbText",
        "AcDbMText",
        "AcDbAttribute",
        "AcDbAttributeDefinition",
    ):
        details["InsertionPoint"] = format_point(
            safe_get(entity, "InsertionPoint")
        )
        details["TextString"] = str(safe_get(entity, "TextString"))

    elif "Dimension" in object_name:
        details["TextPosition"] = format_point(
            safe_get(entity, "TextPosition")
        )
        details["TextString"] = str(safe_get(entity, "TextOverride"))

    elif object_name in (
        "AcDbBlockReference",
        "AcDbMInsertBlock",
    ):
        details["InsertionPoint"] = format_point(
            safe_get(entity, "InsertionPoint")
        )

        block_name = safe_get(entity, "Name")
        if block_name:
            details["TextString"] = f"BlockName={block_name}"

    coordinates = safe_get(entity, "Coordinates")

    if coordinates != "":
        try:
            coordinate_list = list(coordinates)
            details["Coordinates"] = ", ".join(
                f"{float(v):.6f}" for v in coordinate_list
            )
        except Exception:
            details["Coordinates"] = str(coordinates)

    return details


def inspect_model_space(doc: Any) -> list[dict[str, str]]:
    rows: list[dict[str, str]] = []

    model_space = doc.ModelSpace
    count = model_space.Count

    print(f"\nModel Space 객체 수: {count}\n")

    for index in range(count):
        try:
            entity = model_space.Item(index)
            details = get_entity_details(entity)
            details["Index"] = str(index)

            rows.append(details)

            print(
                f"[{index}] "
                f"{details['ObjectName']} | "
                f"Handle={details['Handle']} | "
                f"Layer={details['Layer']} | "
                f"BBox={details['BoundingBoxMin']} ~ "
                f"{details['BoundingBoxMax']}"
            )

        except Exception as exc:
            print(f"[{index}] 객체 읽기 실패: {exc}")

            rows.append(
                {
                    "Index": str(index),
                    "ObjectName": "READ_ERROR",
                    "Handle": "",
                    "Layer": "",
                    "Color": "",
                    "Linetype": "",
                    "StartPoint": "",
                    "EndPoint": "",
                    "Center": "",
                    "InsertionPoint": "",
                    "TextPosition": "",
                    "Radius": "",
                    "Diameter": "",
                    "TextString": str(exc),
                    "Coordinates": "",
                    "BoundingBoxMin": "",
                    "BoundingBoxMax": "",
                }
            )

    return rows


def write_csv(rows: list[dict[str, str]], output_path: Path) -> None:
    fieldnames = [
        "Index",
        "ObjectName",
        "Handle",
        "Layer",
        "Color",
        "Linetype",
        "StartPoint",
        "EndPoint",
        "Center",
        "InsertionPoint",
        "TextPosition",
        "Radius",
        "Diameter",
        "TextString",
        "Coordinates",
        "BoundingBoxMin",
        "BoundingBoxMax",
    ]

    with output_path.open(
        "w",
        newline="",
        encoding="utf-8-sig",
    ) as file:
        writer = csv.DictWriter(file, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(rows)


def write_txt(rows: list[dict[str, str]], output_path: Path) -> None:
    with output_path.open("w", encoding="utf-8") as file:
        file.write(f"총 객체 수: {len(rows)}\n\n")

        for row in rows:
            file.write(f"Index: {row['Index']}\n")
            file.write(f"ObjectName: {row['ObjectName']}\n")
            file.write(f"Handle: {row['Handle']}\n")
            file.write(f"Layer: {row['Layer']}\n")
            file.write(f"Color: {row['Color']}\n")
            file.write(f"Linetype: {row['Linetype']}\n")
            file.write(f"StartPoint: {row['StartPoint']}\n")
            file.write(f"EndPoint: {row['EndPoint']}\n")
            file.write(f"Center: {row['Center']}\n")
            file.write(f"InsertionPoint: {row['InsertionPoint']}\n")
            file.write(f"TextPosition: {row['TextPosition']}\n")
            file.write(f"Radius: {row['Radius']}\n")
            file.write(f"Diameter: {row['Diameter']}\n")
            file.write(f"TextString: {row['TextString']}\n")
            file.write(f"Coordinates: {row['Coordinates']}\n")
            file.write(f"BoundingBoxMin: {row['BoundingBoxMin']}\n")
            file.write(f"BoundingBoxMax: {row['BoundingBoxMax']}\n")
            file.write("-" * 80 + "\n")


def show_message(title: str, message: str, error: bool = False) -> None:
    root = tk.Tk()
    root.withdraw()
    root.attributes("-topmost", True)

    if error:
        messagebox.showerror(title, message)
    else:
        messagebox.showinfo(title, message)

    root.destroy()


def main() -> None:
    pythoncom.CoInitialize()

    doc = None

    try:
        dwg_path = choose_dwg_file()

        if dwg_path is None:
            print("파일 선택이 취소되었습니다.")
            return

        print(f"선택 파일: {dwg_path}")

        acad = connect_autocad()
        doc = acad.Documents.Open(str(dwg_path.resolve()))

        print(f"도면 열기 완료: {doc.Name}")

        rows = inspect_model_space(doc)

        csv_path = dwg_path.with_name(
            f"{dwg_path.stem}_objects.csv"
        )
        txt_path = dwg_path.with_name(
            f"{dwg_path.stem}_objects.txt"
        )

        write_csv(rows, csv_path)
        write_txt(rows, txt_path)

        print("\n분석 완료")
        print(f"CSV: {csv_path}")
        print(f"TXT: {txt_path}")

        show_message(
            "분석 완료",
            "DWG 객체 분석이 완료되었습니다.\n\n"
            f"객체 수: {len(rows)}\n\n"
            f"CSV:\n{csv_path}\n\n"
            f"TXT:\n{txt_path}",
        )

    except Exception as exc:
        error_text = (
            f"{exc}\n\n"
            f"{traceback.format_exc()}"
        )

        print(error_text)

        show_message(
            "실행 오류",
            error_text,
            error=True,
        )

        sys.exit(1)

    finally:
        # 분석만 하므로 저장하지 않고 닫는다.
        if doc is not None:
            try:
                doc.Close(False)
            except Exception:
                pass

        pythoncom.CoUninitialize()


if __name__ == "__main__":
    main()