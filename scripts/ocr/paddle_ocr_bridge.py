import argparse
import hashlib
import inspect
import importlib.metadata
import json
import os
import sys
from pathlib import Path

# PaddleOCR 3.x on Windows uses OneDNN by default, which has a bug with PP-OCRv5 models.
# Must be set before paddleocr/paddlex is imported.
os.environ.setdefault("PADDLE_PDX_ENABLE_MKLDNN_BYDEFAULT", "0")


def write_json(payload):
    sys.stdout.write(json.dumps(payload, ensure_ascii=False))


def package_version(name):
    try:
        return importlib.metadata.version(name)
    except importlib.metadata.PackageNotFoundError:
        return "not-installed"


def check():
    missing = []
    for package in ("paddleocr", "PIL", "pypdfium2"):
        try:
            __import__(package)
        except Exception as exc:
            missing.append(f"{package}: {exc}")

    available = len(missing) == 0
    write_json({
        "available": available,
        "engineVersion": package_version("paddleocr"),
        "message": None if available else "PaddleOCR non configurato: " + "; ".join(missing)
    })
    return 0


def load_source_image(input_path, kind, page, dpi):
    from PIL import Image

    if kind == "pdf":
        import pypdfium2 as pdfium

        pdf = pdfium.PdfDocument(input_path)
        if page < 1 or page > len(pdf):
            raise ValueError(f"Pagina PDF fuori range: {page}")
        pdf_page = pdf[page - 1]
        scale = dpi / 72.0
        return pdf_page.render(scale=scale).to_pil().convert("RGB")

    with Image.open(input_path) as image:
        return image.convert("RGB")


def deskew_if_available(image):
    try:
        import cv2
        import numpy as np
        from PIL import Image
    except Exception:
        return image

    gray = cv2.cvtColor(np.array(image), cv2.COLOR_RGB2GRAY)
    gray = cv2.bitwise_not(gray)
    coords = np.column_stack(np.where(gray > 0))
    if coords.size == 0:
        return image

    angle = cv2.minAreaRect(coords)[-1]
    if angle < -45:
        angle = -(90 + angle)
    else:
        angle = -angle

    if abs(angle) < 0.2 or abs(angle) > 10:
        return image

    height, width = gray.shape[:2]
    matrix = cv2.getRotationMatrix2D((width // 2, height // 2), angle, 1.0)
    rotated = cv2.warpAffine(
        np.array(image),
        matrix,
        (width, height),
        flags=cv2.INTER_CUBIC,
        borderMode=cv2.BORDER_REPLICATE)
    return Image.fromarray(rotated)


def preprocess(image):
    from PIL import ImageOps, ImageFilter

    normalized = ImageOps.autocontrast(image.convert("L"))
    denoised = normalized.filter(ImageFilter.MedianFilter(size=3))
    rgb = ImageOps.autocontrast(denoised).convert("RGB")
    return deskew_if_available(rgb)


def prepare(args):
    output_dir = Path(args.output_dir)
    output_dir.mkdir(parents=True, exist_ok=True)
    image = load_source_image(args.input, args.kind, args.page, args.dpi)
    prepared = preprocess(image)
    output_path = output_dir / f"{Path(args.input).stem}.page-{args.page}.{args.preprocess_version}.png"
    prepared.save(output_path, format="PNG", optimize=True)
    data = output_path.read_bytes()
    write_json({
        "preparedImagePath": str(output_path),
        "pageHash": hashlib.sha256(data).hexdigest(),
        "width": prepared.width,
        "height": prepared.height
    })
    return 0


def normalize_paddle_result(raw):
    if not raw:
        return []

    # PaddleOCR 3.x: predict() returns a list of OCRResult objects with a .json attribute.
    first = raw[0]
    if hasattr(first, "json"):
        res = first.json.get("res", {}) if isinstance(first.json, dict) else {}
        texts = res.get("rec_texts", [])
        scores = res.get("rec_scores", [])
        polys = res.get("rec_polys", res.get("dt_polys", []))
        lines = []
        for text, score, points in zip(texts, scores, polys):
            lines.append({
                "text": str(text),
                "confidence": float(score) if score is not None else None,
                "points": [{"x": float(p[0]), "y": float(p[1])} for p in points]
            })
        return lines

    # PaddleOCR 2.x: ocr() returns [[[[pts], ("text", conf)], ...]]
    page_result = raw[0] if isinstance(raw, list) and len(raw) == 1 else raw
    lines = []
    for item in page_result or []:
        if not item or len(item) < 2:
            continue
        points = item[0] or []
        text_conf = item[1] or ["", None]
        text = text_conf[0] if len(text_conf) > 0 else ""
        confidence = text_conf[1] if len(text_conf) > 1 else None
        lines.append({
            "text": str(text),
            "confidence": float(confidence) if confidence is not None else None,
            "points": [{"x": float(point[0]), "y": float(point[1])} for point in points]
        })
    return lines


def parse_bool(value):
    if isinstance(value, bool):
        return value
    normalized = str(value).strip().lower()
    if normalized in ("1", "true", "yes", "on"):
        return True
    if normalized in ("0", "false", "no", "off"):
        return False
    raise argparse.ArgumentTypeError(f"Valore booleano non valido: {value}")


def add_if_supported(kwargs, supported, name, value):
    if name in supported and value is not None:
        kwargs[name] = value


def build_paddle_kwargs(args):
    from paddleocr import PaddleOCR

    supported = set(inspect.signature(PaddleOCR).parameters)
    kwargs = {}
    add_if_supported(kwargs, supported, "lang", args.language)
    add_if_supported(kwargs, supported, "ocr_version", args.model_version)
    add_if_supported(kwargs, supported, "device", args.device)
    add_if_supported(kwargs, supported, "cpu_threads", args.cpu_threads)
    add_if_supported(kwargs, supported, "use_gpu", args.device == "gpu")

    add_if_supported(kwargs, supported, "text_detection_limit_side_len", args.detection_side_limit)
    add_if_supported(kwargs, supported, "det_limit_side_len", args.detection_side_limit)
    add_if_supported(kwargs, supported, "text_detection_thresh", args.detection_threshold)
    add_if_supported(kwargs, supported, "det_db_thresh", args.detection_threshold)
    add_if_supported(kwargs, supported, "text_detection_box_thresh", args.detection_box_threshold)
    add_if_supported(kwargs, supported, "det_db_box_thresh", args.detection_box_threshold)
    add_if_supported(kwargs, supported, "text_detection_unclip_ratio", args.detection_unclip_ratio)
    add_if_supported(kwargs, supported, "det_db_unclip_ratio", args.detection_unclip_ratio)
    add_if_supported(kwargs, supported, "text_recognition_score_thresh", args.recognition_score_threshold)
    add_if_supported(kwargs, supported, "drop_score", args.recognition_score_threshold)
    add_if_supported(kwargs, supported, "text_recognition_batch_size", args.recognition_batch_size)
    add_if_supported(kwargs, supported, "rec_batch_num", args.recognition_batch_size)
    add_if_supported(kwargs, supported, "use_textline_orientation", args.use_textline_orientation)
    add_if_supported(kwargs, supported, "use_angle_cls", args.use_textline_orientation)
    add_if_supported(
        kwargs,
        supported,
        "use_doc_orientation_classify",
        args.use_document_orientation_classification)
    add_if_supported(
        kwargs,
        supported,
        "use_doc_unwarping",
        args.use_document_unwarping)

    return kwargs


def ocr(args):
    from paddleocr import PaddleOCR

    os.environ["OMP_NUM_THREADS"] = str(args.cpu_threads)
    os.environ["CPU_NUM"] = str(args.cpu_threads)

    # PaddleOCR 3.x removed show_log and renamed use_angle_cls → use_textline_orientation.
    try:
        engine = PaddleOCR(**build_paddle_kwargs(args))
    except Exception:
        engine = PaddleOCR(lang=args.language)

    # PaddleOCR 3.x: use predict(); 2.x: use ocr(). Both formats handled by normalize_paddle_result.
    try:
        raw = list(engine.predict(args.input))
    except (TypeError, AttributeError):
        raw = engine.ocr(args.input, cls=args.use_textline_orientation) if hasattr(engine, "ocr") else []

    boxes = [
        box for box in normalize_paddle_result(raw)
        if box["confidence"] is None or box["confidence"] >= args.recognition_score_threshold
    ]
    text = "\n".join(box["text"] for box in boxes if box["text"].strip())
    confidences = [box["confidence"] for box in boxes if box["confidence"] is not None]
    average = sum(confidences) / len(confidences) if confidences else None
    write_json({
        "text": text,
        "boxes": boxes,
        "confidence": average,
        "engineVersion": package_version("paddleocr")
    })
    return 0


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--mode", choices=["check", "prepare", "ocr"], required=True)
    parser.add_argument("--input")
    parser.add_argument("--kind", choices=["pdf", "image"], default="image")
    parser.add_argument("--page", type=int, default=1)
    parser.add_argument("--dpi", type=int, default=200)
    parser.add_argument("--output-dir")
    parser.add_argument("--preprocess-version", default="onlyrag-preprocess-v1")
    parser.add_argument("--language", default="it")
    parser.add_argument("--profile", choices=["fast", "balanced", "accurate"], default="balanced")
    parser.add_argument("--model-preset", default="PP-OCRv5")
    parser.add_argument("--model-version", default="PP-OCRv5")
    parser.add_argument("--detection-side-limit", type=int, default=960)
    parser.add_argument("--detection-threshold", type=float, default=0.30)
    parser.add_argument("--detection-box-threshold", type=float, default=0.60)
    parser.add_argument("--detection-unclip-ratio", type=float, default=1.50)
    parser.add_argument("--recognition-score-threshold", type=float, default=0.50)
    parser.add_argument("--use-textline-orientation", type=parse_bool, default=True)
    parser.add_argument("--use-document-orientation-classification", type=parse_bool, default=False)
    parser.add_argument("--use-document-unwarping", type=parse_bool, default=False)
    parser.add_argument("--recognition-batch-size", type=int, default=6)
    parser.add_argument("--cpu-threads", type=int, default=2)
    parser.add_argument("--device", choices=["cpu", "gpu"], default="cpu")
    args = parser.parse_args()

    if args.mode == "check":
        return check()
    if args.mode == "prepare":
        if not args.input or not args.output_dir:
            raise ValueError("--input e --output-dir sono obbligatori per prepare")
        return prepare(args)
    if args.mode == "ocr":
        if not args.input:
            raise ValueError("--input e obbligatorio per ocr")
        return ocr(args)
    return 2


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:
        sys.stderr.write(str(exc))
        raise SystemExit(1)
