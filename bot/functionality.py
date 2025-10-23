import os
from dotenv import load_dotenv
import requests
import json
from datetime import date, datetime
import warnings
import numpy as np
import base64
import requests
import logging
import glob
import tensorflow as tf
from keras.preprocessing import image
import time
from keras.applications.mobilenet import preprocess_input as _pp
import traceback
from reportlab.lib import colors
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle, getSampleStyleSheet
from reportlab.platypus import (
    SimpleDocTemplate, Paragraph, Spacer, Table, TableStyle, KeepInFrame,
    Image as RLImage
)
from reportlab.lib.units import mm
from reportlab.lib.pagesizes import A4
from reportlab.lib import colors
from reportlab.lib.units import mm    
from reportlab.lib.styles import getSampleStyleSheet, ParagraphStyle
from reportlab.pdfgen import canvas as _canvas
from datetime import datetime as _dt
import os


def detect_lettuce(ruta_imagen):
    """Detecta si hay lechuga en una imagen usando Gemini API"""
    try:
        load_dotenv()
        API_KEY_LLM = os.getenv('API_KEY_LLM')
        
        if not API_KEY_LLM:
            raise ValueError("❌ API_KEY_LLM no configurada en .env")
        
        
        PROMPT = "¿La imagen muestra una lechuga real? Responde solo con '1' si es una lechuga real , '2' si no es una lechuga real, o '0' si no es una lechuga."

        with open(ruta_imagen, "rb") as img_file:
            b64_image = base64.b64encode(img_file.read()).decode('utf-8')
        
        payload = {
            "contents": [
                {
                    "parts": [
                        {
                            "inlineData": {
                                "mimeType": "image/jpeg",
                                "data": b64_image
                            }
                        },
                        {
                            "text": PROMPT
                        }
                    ]
                }
            ]
        }

        url = f"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={API_KEY_LLM}"
        headers = {"Content-Type": "application/json"}
        
        response = requests.post(url, headers=headers, data=json.dumps(payload))
        if response.status_code == 200:
                result = response.json()
                respuesta = result["candidates"][0]["content"]["parts"][0]["text"]
                return respuesta.strip()
        else:
                print(f"[ERROR detectlettuce] HTTP {response.status_code}: {response.text}")
                return f"Parece que tenemos un fallo técnico. Intenta de nuevo más tarde 😥."
    except Exception as e:
        print("[ERROR detectlettuce]", str(e))
        traceback.print_exc()
        return f"Parece que tenemos un fallo técnico. Intenta de nuevo más tarde 😥."
# ================================================================================================
logger = logging.getLogger(__name__)
_CNN_MODEL = None
_CNN_IMG_SIZE = 224
_CNN_CLASSES = ["Botrytis", "Xanthomonas", "Sana"]   # etiquetas unificadas



def _resolve_model_path():
    model_path = os.getenv("LECHUGA_MODEL_PATH")
    if not model_path:
        raise ValueError("La variable de entorno 'LECHUGA_MODEL_PATH' no está configurada.")
    return model_path


def _load_cnn_model():
    """Carga el modelo una sola vez y lo cachea."""
    global _CNN_MODEL
    if _CNN_MODEL is not None:
        return _CNN_MODEL
    try:
        import tensorflow as tf
        model_path = _resolve_model_path()
        if not os.path.exists(model_path):
            raise FileNotFoundError(f"Modelo .keras no encontrado en: {model_path}")
        _CNN_MODEL = tf.keras.models.load_model(model_path)
        logger.info(f"[CNN] Modelo cargado: {model_path}")
        return _CNN_MODEL
    except Exception as e:
        logger.exception(f"[CNN] Error cargando modelo: {e}")
        raise

def _get_preprocess():
    try:
        
        return _pp
    except Exception:
        try:
            from keras.applications.mobilenet_v2 import preprocess_input as _pp  # Keras 3
            return _pp
        except Exception:
            # Fallback: escalado [-1, 1] (equivalente a MobileNetV2)
            def _pp(x):
                import numpy as np
                return (x / 127.5) - 1.0
            logger.warning("[CNN] Usando fallback de preprocess_input (no se pudo importar desde TF/Keras).")
            return _pp


def classify_image(image_path: str) -> str:
    """
    Clasifica una imagen con MobileNetV2 y devuelve TEXTO con el formato esperado:
      'Detección realizada: **<CLASE_TOP>**' + líneas con porcentajes.
    """
    try:
        import numpy as np
        from PIL import Image, ImageOps
        import tensorflow as tf

        # 1) Cargar modelo y preprocess
        model = _load_cnn_model()
        preprocess_input = _get_preprocess()

        # 2) Cargar/normalizar imagen
        if not os.path.exists(image_path):
            return f"❌ Error al procesar la imagen (CNN): ruta inexistente {image_path}"
        
        img = Image.open(image_path).convert("RGB")
        img = ImageOps.exif_transpose(img)  # corrige orientación
        img = img.resize((_CNN_IMG_SIZE, _CNN_IMG_SIZE))
        arr = np.array(img, dtype=np.float32)
        arr = np.expand_dims(arr, 0)              # (1, H, W, 3)
        arr = preprocess_input(arr)               # [-1,1] para MobileNetV2

        # 3) Inferencia y softmax
        preds = model.predict(arr)
        preds = np.squeeze(preds)
        if preds.ndim == 0:
            preds = np.array([float(preds)])
        probs = tf.nn.softmax(preds).numpy().tolist()

        # 4) Mapear a etiquetas y armar salida
        n = min(len(probs), len(_CNN_CLASSES))
        classes = _CNN_CLASSES[:n]
        probs = probs[:n]
        top_idx = int(np.argmax(probs))
        top_cls = classes[top_idx] if 0 <= top_idx < len(classes) else "Desconocida"

        lines = []
        lines.append("🔬 **Resultado del análisis (CNN)**")
        lines.append(f"Detección realizada: **{top_cls}**")
        lines.append("")
        for i, cls in enumerate(classes):
            pct = probs[i] * 100.0
            prefix = "✅" if i == top_idx else "•"
            lines.append(f"{prefix} {cls}: {pct:.1f}%")

        msg = "\n".join(lines)
        logger.debug(f"[CNN] top={top_cls} probs={dict(zip(classes, probs))}")
        return msg

    except Exception as e:
        logger.exception(f"classify_image error: {e}")
        return f"❌ Error al procesar la imagen (CNN): {e}"

#===================================================================================================
def simplify_disease_name(x: str) -> str:
    if not x: return "Desconocida"
    s = str(x).strip().lower()
    if "bot" in s: return "Botrytis"
    if "xan" in s: return "Xanthomonas"
    if "san" in s or "healthy" in s: return "Sana"
    return x
load_dotenv()  # carga variables del archivo .env
base = os.getenv("REPORT_IMAGES_PATH")  # lee la ruta base del archivo .env

def get_example_image_for_disease(label: str) -> str | None:
    """
    Devuelve la ruta a una imagen de ejemplo para la enfermedad detectada por la CNN.
    Las imágenes deben estar en la ruta configurada en EXAMPLE_IMAGES_PATH.
    """
    print(f"📁 Ruta base de imágenes de ejemplo: {base}")
    if not base:
        # Si la variable de entorno no está definida, usar una ruta por defecto o devolver None
        return None
    
    mapping = {
        "Botrytis": os.path.join(base, "Botritis.jpg"),
        "Xanthomonas": os.path.join(base, "Xanthomonas.jpg"),
        "Sana": os.path.join(base, "Sana.jpg"),
    }
    key = simplify_disease_name(label)
    path = mapping.get(key)
    return path if path and os.path.exists(path) else None

#===================================================================================================

#Ver cual de las dos sirve

#===================================================================================================
def pct_str(p):
    return f"{p*100:.1f}%" if isinstance(p, (int, float)) and p <= 1.0001 else str(p)

from reportlab.lib.utils import ImageReader


def build_pacho_pdf_report(
    outfile,
    meta,
    rf_block,
    cnn_block,
    tratamiento=None,
    logo_path=None,
    treatment_title: str = "Tratamiento recomendado"
):
    images_base = os.getenv("REPORT_IMAGES_PATH") or ""
    candidate = os.path.join(images_base, "logo_pacho.png")
    logo_path = candidate if os.path.exists(candidate) else None

    # ================= Helper banner (integrado) =================
    def _banner_with_circle_logo(logo_path):
        def _draw(canv: _canvas.Canvas, doc):
            width, height = A4
            banner_h = 28 * mm
            canv.saveState()
            # fondo verde
            canv.setFillColor(colors.HexColor("#2d5a27"))
            canv.rect(0, height - banner_h, width, banner_h, stroke=0, fill=1)
            # logo circular (clip)
            try:
                if logo_path and os.path.exists(logo_path):
                    size = min(banner_h - 6 * mm, 26 * mm)
                    cx = 14 * mm
                    cy = height - banner_h / 2
                    r = size / 2
                    p = canv.beginPath()
                    p.circle(cx, cy, r)
                    canv.clipPath(p, stroke=0, fill=0)
                    canv.drawImage(
                        logo_path, cx - r, cy - r,
                        width=2 * r, height=2 * r,
                        preserveAspectRatio=True, mask='auto'
                    )
                    canv.restoreState(); canv.saveState()
                    canv.setStrokeColor(colors.white); canv.setLineWidth(2)
                    canv.circle(cx, cy, r, stroke=1, fill=0)
            except Exception:
                # si el logo falla, seguimos con el título sin interrumpir
                pass
            # título centrado
            canv.setFillColor(colors.white); canv.setFont("Helvetica-Bold", 22)
            title = "Pacho Asistente"
            tw = canv.stringWidth(title, "Helvetica-Bold", 22)
            canv.drawString((width - tw) / 2, height - banner_h / 2 + 4, title)
            canv.restoreState()
        return _draw

    # ================ Resolver logo desde .env si no viene ================
    if not logo_path:
        try:
            load_dotenv()
        except Exception:
            pass
        images_base = os.getenv("REPORT_IMAGES_PATH") or "" 
        candidate = os.path.join(images_base, "logo_pacho.png")
        logo_path = candidate if os.path.exists(candidate) else None

    # ====================== Estilos (seguros) ======================
    styles = getSampleStyleSheet()
    if "H2Green" not in styles:
        styles.add(ParagraphStyle(
            name="H2Green", parent=styles["Heading2"],
            textColor=colors.HexColor("#2d5a27"), fontName="Helvetica-Bold"
        ))
    if "KPI" not in styles:
        styles.add(ParagraphStyle(
            name="KPI", parent=styles["Heading2"],
            fontName="Helvetica-Bold", textColor=colors.HexColor("#0b7a3b"),
            fontSize=16
        ))
    if "Label" not in styles:
        styles.add(ParagraphStyle(
            name="Label", parent=styles["BodyText"],
            textColor=colors.HexColor("#555555"), fontSize=9
        ))
    if "WrapCell" not in styles:
        styles.add(ParagraphStyle(
            name="WrapCell", parent=styles["BodyText"],
            fontSize=8.5, leading=11, wordWrap="CJK", textColor=colors.black
        ))

    # ================== Documento y márgenes ==================
    banner_h_mm = 28.0
    extra_breath_mm = 8.0
    top_margin = (banner_h_mm + extra_breath_mm) * mm

    os.makedirs(os.path.dirname(outfile), exist_ok=True)
    doc = SimpleDocTemplate(
        outfile, pagesize=A4,
        leftMargin=18 * mm, rightMargin=18 * mm,
        topMargin=top_margin, bottomMargin=15 * mm
    )

    # ==================== Utilidades locales ====================
    def pct_str(v):
        return f"{v*100:.1f}%" if isinstance(v, (int, float)) else "—"

    def simplify_disease_name(name):
        return str(name).capitalize().replace("_", " ")

    def _placeholder_flowable(width_mm, height_mm, label="Sin imagen"):
        from reportlab.graphics.shapes import Drawing, Rect, String
        d = Drawing(width_mm, height_mm)
        d.add(Rect(0, 0, width_mm, height_mm,
                   strokeColor=colors.HexColor("#99b69e"),
                   fillColor=None, strokeWidth=1.2))
        d.add(String(width_mm / 2, height_mm / 2, label, textAnchor="middle",
                     fontName="Helvetica", fontSize=10,
                     fillColor=colors.HexColor("#2d5a27")))
        return d

    def _format_treatments_local(trat):
        # usa tu función global si existe
        if "format_treatments_with_ai_or_fallback" in globals():
            try:
                return globals()["format_treatments_with_ai_or_fallback"](trat)
            except Exception:
                pass
        # fallback sencillo
        if isinstance(trat, (list, tuple)):
            return [str(x) for x in trat]
        if isinstance(trat, str):
            return [trat]
        return []

    # ====================== STORY ======================
    story = []
    story.append(Spacer(1, 6 * mm))

    # Fecha
    fecha = meta.get("fecha") if isinstance(meta, dict) else None
    if not fecha:
        from datetime import datetime as _dt
        fecha = _dt.now().strftime("%d-%m-%Y %H:%M")

    meta_tbl = Table(
        [[Paragraph("<b>Fecha:</b>", styles["Label"]),
          Paragraph(fecha, styles["BodyText"])]],
        colWidths=[25 * mm, 150 * mm]
    )
    meta_tbl.setStyle(TableStyle([
        ("VALIGN", (0, 0), (-1, -1), "MIDDLE"),
        ("BOTTOMPADDING", (0, 0), (-1, -1), 6)
    ]))
    story += [meta_tbl, Spacer(1, 6 * mm)]

    # ---------- Bloque Izquierdo: Random Forest ----------
    rf_cls = simplify_disease_name((rf_block or {}).get("clasificacion", "—"))
    rf_conf = (rf_block or {}).get("confianza", None)
    rf_kpi = rf_cls + (f" — {rf_conf*100:.1f}%" if isinstance(rf_conf, (int, float)) else "")

    left_block = [
        Paragraph("Resultado de la encuesta (Contexto)", styles["H2Green"]),
        Paragraph(rf_kpi, styles["KPI"]),
        Spacer(1, 3 * mm)
    ]

    resp = (rf_block or {}).get("respuestas", {}) or {}
    preguntas = (rf_block or {}).get("preguntas", {}) or {}
    rows = [[Paragraph("<b>Pregunta</b>", styles["Label"]),
             Paragraph("<b>Respuesta</b>", styles["Label"])]]

    def _q_key(k):
        try:
            return int(str(k).lstrip("qQ"))
        except Exception:
            return 10**6

    for k in sorted(resp.keys(), key=_q_key):
        q_text = preguntas.get(k, "").strip() or str(k)
        rows.append([
            Paragraph(q_text, styles["WrapCell"]),
            Paragraph(str(resp[k]), styles["WrapCell"])
        ])

    t_resp = Table(rows, colWidths=[75 * mm, 20 * mm])
    t_resp.setStyle(TableStyle([
        ("GRID", (0, 0), (-1, -1), 0.25, colors.HexColor("#c4dec9")),
        ("BACKGROUND", (0, 0), (-1, 0), colors.HexColor("#e9f3ec")),
        ("TEXTCOLOR", (0, 0), (-1, 0), colors.HexColor("#2d5a27")),
        ("VALIGN", (0, 0), (-1, -1), "TOP"),
        ("LEFTPADDING", (0, 0), (-1, -1), 4),
        ("RIGHTPADDING", (0, 0), (-1, -1), 4),
        ("FONTSIZE", (0, 0), (-1, -1), 8.5)
    ]))
    left_block.append(Paragraph("<b>Respuestas del cuestionario</b>", styles["BodyText"]))
    left_block.append(t_resp)

    rf_probs = (rf_block or {}).get("probabilidades") or {}
    if rf_probs:
        rows_rf = [["Clase", "Prob."]]
        for name, p in sorted(rf_probs.items(), key=lambda x: x[1], reverse=True):
            rows_rf.append([
                Paragraph(simplify_disease_name(str(name)), styles["WrapCell"]),
                Paragraph(pct_str(p), styles["WrapCell"])
            ])
        t_rf = Table(rows_rf, colWidths=[50 * mm, 30 * mm])
        t_rf.setStyle(TableStyle([
            ("GRID", (0, 0), (-1, -1), 0.25, colors.HexColor("#c4dec9")),
            ("BACKGROUND", (0, 0), (-1, 0), colors.HexColor("#e9f3ec")),
            ("VALIGN", (0, 0), (-1, -1), "TOP"),
        ]))
        left_block += [Spacer(1, 4 * mm),
                       Paragraph("<b>Probabilidades (Random Forest)</b>", styles["BodyText"]),
                       t_rf]

    rf_frame = KeepInFrame(90 * mm, 200 * mm, left_block, hAlign="LEFT")

    # ---------- Bloque Derecho: CNN ----------
    cnn_cls = simplify_disease_name((cnn_block or {}).get("clasificacion", "—"))
    right_block = [
        Paragraph("Resultado del análisis de la imagen (CNN)", styles["H2Green"]),
        Paragraph(cnn_cls, styles["KPI"]),
        Spacer(1, 3 * mm)
    ]

    probs = (cnn_block or {}).get("probabilidades") or {}
    if probs:
        rows_cnn = [["Clase", "Prob."]]
        for name, p in sorted(probs.items(), key=lambda x: x[1], reverse=True):
            rows_cnn.append([
                Paragraph(simplify_disease_name(str(name)), styles["WrapCell"]),
                Paragraph(pct_str(p), styles["WrapCell"])
            ])
        t_cnn = Table(rows_cnn, colWidths=[50 * mm, 30 * mm])
        t_cnn.setStyle(TableStyle([
            ("GRID", (0, 0), (-1, -1), 0.25, colors.HexColor("#c4dec9")),
            ("BACKGROUND", (0, 0), (-1, 0), colors.HexColor("#e9f3ec")),
        ]))
        right_block += [t_cnn, Spacer(1, 3 * mm)]

    img_user_path = (cnn_block or {}).get("imagen_usuario_path")
    img_example_path = (cnn_block or {}).get("imagen_ejemplo_path")

    def make_img(path, label):
        if path and os.path.exists(path):
            return RLImage(path, width=45 * mm, height=45 * mm, kind='proportional')
        else:
            return _placeholder_flowable(45 * mm, 45 * mm, label)

    user_flow = make_img(img_user_path, "Imagen del usuario")
    demo_flow = make_img(img_example_path, "Imagen de ejemplo")

    img_tbl = Table(
        [[user_flow, demo_flow],
         [Paragraph("<i>Imagen del usuario</i>", styles["BodyText"]),
          Paragraph("<i>Imagen de ejemplo</i>", styles["BodyText"])]],
        colWidths=[47 * mm, 47 * mm]
    )
    img_tbl.setStyle(TableStyle([
        ("ALIGN", (0, 0), (-1, 0), "CENTER"),
        ("VALIGN", (0, 0), (-1, 0), "MIDDLE"),
    ]))
    right_block.append(img_tbl)

    cnn_frame = KeepInFrame(90 * mm, 200 * mm, right_block, hAlign="LEFT")

    # ---------- Dos columnas ----------
    columns_tbl = Table([[rf_frame, cnn_frame]], colWidths=[95 * mm, 95 * mm])
    columns_tbl.setStyle(TableStyle([
        ("VALIGN", (0, 0), (-1, -1), "TOP"),
        ("LEFTPADDING", (0, 0), (-1, -1), 6),
        ("RIGHTPADDING", (0, 0), (-1, -1), 6),
    ]))
    story += [columns_tbl, Spacer(1, 8 * mm)]

    # ---------- Tratamientos ----------
    if tratamiento:
        formatted_treatments = _format_treatments_local(tratamiento)
        story.append(Paragraph(treatment_title, styles["H2Green"]))
        story.append(Spacer(1, 8))
        story.append(Paragraph("Estos son los tratamientos que te podemos recomendar:", styles["BodyText"]))
        story.append(Spacer(1, 10))
        for line in formatted_treatments:
            clean_line = (line or "").strip()
            if not clean_line:
                continue
            if clean_line.startswith(("-", ".", "•")):
                clean_line = f"• {clean_line.lstrip('-.• ')}"
            story.append(Paragraph(clean_line, styles["BodyText"]))
            story.append(Spacer(1, 4))

    # ================== Build con banner en primera página ==================
    first_page_cb = _banner_with_circle_logo(logo_path)
    doc.build(story, onFirstPage=first_page_cb)  # sin onLaterPages => banner solo en la primera
    return outfile

#===================================================================================================
def setup_directories():
    """Crea la estructura de directorios necesaria para el bot"""
    directories = [
        'data', 'data/users', 'data/logs'
    ]
    
    for directory in directories:
        os.makedirs(directory, exist_ok=True)
    
    print("✅ Directorios creados correctamente")

#===================================================================================================
def setup_logging(level=logging.WARNING):
    """Configura el sistema de logging para reducir prints innecesarios"""
    try:
        # ✅ CREAR DIRECTORIOS PRIMERO
        log_dir = os.path.join("data", "logs")
        os.makedirs(log_dir, exist_ok=True)
        
        log_file = os.path.join(log_dir, "bot.log")
        
        logging.basicConfig(
            level=level,
            format='%(asctime)s - %(levelname)s - %(message)s',
            handlers=[
                logging.FileHandler(log_file),
                logging.StreamHandler()  # También mostrar en consola
            ]
        )
        
        # Silenciar logs de bibliotecas externas
        logging.getLogger('tensorflow').setLevel(logging.ERROR)
        logging.getLogger('whisper').setLevel(logging.ERROR)
        logging.getLogger('pydub').setLevel(logging.ERROR)
        warnings.filterwarnings("ignore")
        
        print(f"✅ Logging configurado: {log_file}")
        
    except Exception as e:
        print(f"❌ Error configurando logging: {e}")
        # Configuración de respaldo solo para consola
        logging.basicConfig(
            level=level,
            format='%(asctime)s - %(levelname)s - %(message)s'
        )
#===================================================================================================
def cleanup_old_files(minutes_old=5):
    """Limpia archivos temporales antiguos"""
    import time

    directories_to_clean = [
        os.path.join("data", "uploads"),
        os.path.join("data", "reports")
    ]

    current_time = time.time()
    seconds_in_minute = 60
    cutoff_time = current_time - (minutes_old * seconds_in_minute)

    cleaned_files = 0

    for directory in directories_to_clean:
        if os.path.exists(directory):
            for filename in os.listdir(directory):
                filepath = os.path.join(directory, filename)
                if os.path.isfile(filepath):
                    file_time = os.path.getmtime(filepath)
                    if file_time < cutoff_time:
                        try:
                            os.remove(filepath)
                            cleaned_files += 1
                        except Exception as e:
                            logger.error(f"Error eliminando {filepath}: {e}")

    return cleaned_files
def delete_user_files(user_id: int, report_age_minutes: int = 5):
    """
    Elimina:
      ✅ la imagen del diagnóstico inmediatamente.
      ✅ los informes PDF del usuario si tienen al menos X minutos de antigüedad.

    Args:
        user_id (int): ID del usuario.
        report_age_minutes (int): Minutos mínimos de antigüedad del PDF para ser eliminado.
    """
    deleted = {"image_deleted": False, "reports_deleted": 0}

    try:
        # === 1️⃣ Eliminar imagen del diagnóstico ===
        img_path = os.path.join("data", "uploads", f"{user_id}_diagnosis.jpg")
        if os.path.exists(img_path):
            os.remove(img_path)
            deleted["image_deleted"] = True
            logger.info(f"🗑️ Imagen eliminada: {img_path}")
        else:
            logger.debug(f"No se encontró la imagen para eliminar: {img_path}")

        # === 2️⃣ Eliminar informes PDF antiguos ===
        reports_dir = os.path.join("data", "reports")
        pattern = os.path.join(reports_dir, f"Pacho_Informe_{user_id}_*.pdf")

        current_time = time.time()
        cutoff_seconds = report_age_minutes * 60

        for report_path in glob.glob(pattern):
            try:
                file_time = os.path.getmtime(report_path)
                age_seconds = current_time - file_time
                if age_seconds >= cutoff_seconds:
                    os.remove(report_path)
                    deleted["reports_deleted"] += 1
                    logger.info(f"🗑️ Informe eliminado (antigüedad {age_seconds/60:.1f} min): {report_path}")
            except Exception as e:
                logger.error(f"Error eliminando informe {report_path}: {e}")

    except Exception as e:
        logger.error(f"Error en delete_user_files para usuario {user_id}: {e}")

    return deleted
#===================================================================================================
def load_values():
    """Carga las variables de entorno necesarias"""
    load_dotenv()
    token = os.getenv('BOT_TOKEN')
    user_name = os.getenv('BOT_USERNAME')  
    api_key_LLM = os.getenv('API_KEY_LLM') 
    API_KEY_GROQ = os.getenv('API_KEY_GROQ')    
    

    return token, user_name, api_key_LLM, API_KEY_GROQ
#===================================================================================================

def format_treatments_with_ai_or_fallback(treatments_list):
    
    try:
        if not treatments_list:
            return ["No se encontraron tratamientos disponibles."]

        
        load_dotenv()
        API_KEY_LLM = os.getenv("API_KEY_LLM")
        if not API_KEY_LLM:
            raise ValueError("API_KEY_LLM no configurada en .env")

        # 🪴 Prompt optimizado para PDF legible
        prompt = (
            "Eres un asistente experto en fitopatología agrícola. Tu tarea es organizar y redactar los siguientes tratamientos agrícolas de manera clara, ordenada y fácil de entender para un agricultor.Antes de enumerar los tratamientos (si la planta no está sana), di algo como: Querido agricultor estos son los tratamientos aconsejados para su planta \n\n"
            "Cada tratamiento debe presentarse con un título como 'Tratamiento 1', 'Tratamiento 2', etc. Si la planta esta sana, felicita al agricultor y pidele que mantenga sus buenas prácticas, no la enumeres como si fuera un tratamiento.\n"
            "Luego, redacta de forma breve y natural lo que el agricultor debe hacer, incluyendo:\n"
            "• El tipo de tratamiento y cuándo se recomienda aplicarlo.\n"
            "• Los productos recomendados y cómo deben usarse.\n"
            "• La frecuencia de aplicación.\n"
            "• Las precauciones importantes que debe tener en cuenta.\n"
            "• El tiempo estimado de mejoría o recuperación de la planta.\n\n"
            "Usa frases completas, claras y amables, como si explicaras las recomendaciones en persona a un agricultor de confianza.\n"
            "Evita tecnicismos innecesarios y repeticiones. No uses símbolos Markdown (**, ##, *) ni emojis.\n"
            "Separa visualmente cada tratamiento con una línea de guiones o un espacio en blanco para que sea fácil de leer en un informe PDF.\n\n"
            "Ejemplo de formato esperado:\n"
            "Tratamiento 1\n"
            "Este tratamiento se recomienda en condiciones de alta humedad. Se debe aplicar un fungicida preventivo con los productos indicados, siguiendo la frecuencia y precauciones sugeridas. Con el manejo adecuado, se espera notar mejoría en aproximadamente 10 días.\n"
            "-------------------------------------------------------------\n\n"
            f"A continuación se presentan los tratamientos para organizar:\n{chr(10).join(treatments_list)}"
        )

        url = (
            f"https://generativelanguage.googleapis.com/v1beta/models/"
            f"gemini-2.5-flash:generateContent?key={API_KEY_LLM}"
        )
        headers = {"Content-Type": "application/json"}
        payload = {"contents": [{"parts": [{"text": prompt}]}]}

        print("🌿 Enviando solicitud al modelo Gemini para formatear tratamientos...")
        response = requests.post(url, headers=headers, json=payload, timeout=30)

        if response.status_code == 200:
            result = response.json()
            texto = result["candidates"][0]["content"]["parts"][0]["text"]
            # Limpiar posibles restos de Markdown o símbolos innecesarios
            limpio = (
                texto.replace("**", "")
                .replace("*", "")
                .replace("#", "")
                .replace("##", "")
                .strip()
            )            

            return limpio.split("\n")

        else:
            print(f"[WARN Gemini] Error {response.status_code}: {response.text}")
            # Fallback local si falla la API
            fallback = []
            for i, t in enumerate(treatments_list, 1):
                fallback.append(f"\n----- 🌿 Tratamiento {i} -----\n")
                fallback.append(f"• {t.strip()}\n")
            return fallback

    except Exception as e:
        print(f"[ERROR Gemini fallback] {e}")
        fallback = []
        for i, t in enumerate(treatments_list, 1):
            fallback.append(f"\n----- 🌿 Tratamiento {i} -----\n")
            fallback.append(f"• {t.strip()}\n")
        return fallback
