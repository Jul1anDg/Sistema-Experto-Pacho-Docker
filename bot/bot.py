
from telegram import Update, InlineKeyboardButton, InlineKeyboardMarkup
from telegram.ext import Application, MessageHandler, CommandHandler, CallbackQueryHandler, ContextTypes, filters
import os, asyncio
from datetime import datetime
import pandas as pd
import re
import functionality as f
import randomforest as pr
from dotenv import load_dotenv
import db_core as db
f.setup_logging()

# =======================
# Normalizadores de clases
# =======================

# ---- Normalización / mapeos de etiquetas ----
rf_num_to_name = {'0': 'Botrytis', '2': 'Xanthomonas', '1': 'Sana'}
synonyms = {
    'botritis': 'Botrytis', 'botrytis': 'Botrytis',
    'xantomonas': 'Xanthomonas', 'xhantomonas': 'Xanthomonas', 'xanthomonas': 'Xanthomonas',
    'sana': 'Sana', 'healthy': 'Sana'
}
WINDOW_SECONDS = 0


async def process_image_after_window_async(context: ContextTypes.DEFAULT_TYPE, uid: int):
    try:
        # recuperar y limpiar sesión
        win = context.bot_data.get('image_window', {})
        sess = win.pop(uid, None)
        if not sess:
            return

        file_id = sess.get("last_file_id")
        chat_id = sess.get("chat_id", uid)
        uname = sess.get("uname", "sin_username")
        count = int(sess.get("count", 1))

        if not file_id:
            await context.bot.send_message(chat_id=chat_id, text="❌ No pude obtener la imagen. Envía una foto nuevamente.")
            return

        # descargar la ÚLTIMA imagen
        tg_file = await context.bot.get_file(file_id)
        image_path = f"data/uploads/{uid}_diagnosis.jpg"
        os.makedirs(os.path.dirname(image_path), exist_ok=True)
        await tg_file.download_to_drive(image_path)

        if count > 1:
            await context.bot.send_message(
                chat_id=chat_id,
                text="He detectado que enviaste más de una imagen, así que analizaré la última que me enviaste."
            )

        # 1) detectar lechuga
        det = f.detect_lettuce(image_path)
        print(f"[DEBUG] Resultado detectlettuce para {image_path}: {det}")
        if det == "1":
            await context.bot.send_message(chat_id=chat_id, text="✅ Se detectó lechuga en la imagen.")
        elif det == "0":
            await context.bot.send_message(chat_id=chat_id, text="❌ No se detectó lechuga en la imagen. Envía otra foto.")
            return
        else:
            await context.bot.send_message(chat_id=chat_id, text="⚠️ La imagen no parece una lechuga real. Intenta con otra foto. Resultado: " + str(det))
            return
            

        # 2) clasificar en silencio (CNN)
        result_text = f.classify_image(image_path)
        top = extract_top_from_msg(result_text)
        # guardar resultado para el paso final + ruta de imagen
        context.bot_data.setdefault('image_analysis', {})[uid] = {
            'ml_result': result_text,
            'detected_class': top,
            'image_path': image_path,
        }

        # 3) iniciar encuesta RF
        context.bot_data.setdefault('survey_sessions', {})[uid] = {'responses': {}, 'user_name': uname}
        await context.bot.send_message(chat_id=chat_id, text="Ahora te haré unas preguntas rápidas para complementar el diagnóstico 🌱")
        await send_diagnostic_question_simple(context, uid, 1)

    except Exception as e:
        f.logger.error(f"process_image_after_window_async: {e}")
        try:
            await context.bot.send_message(chat_id=uid, text="❌ Ocurrió un error procesando la imagen.")
        except Exception:
            pass

def normalize_label(x: str) -> str:
    s = str(x or "").strip().lower()
    s = (s.replace('á','a').replace('é','e').replace('í','i')
           .replace('ó','o').replace('ú','u'))
    if s in rf_num_to_name:        # '0','1','2'
        return rf_num_to_name[s]
    return synonyms.get(s, str(x or "").strip())

def extract_top_from_msg(msg: str) -> str:
    """Extrae la clase top-1 del texto de la CNN en formato flexible."""
    m = re.search(r"Detecci[oó]n\s+realizada:\s*\**\s*([^\n*]+)", msg or "", flags=re.I)
    if m:
        return normalize_label(m.group(1))
    m = re.search(r"✅\s*([^\n:]+)\s*:", msg or "")
    if m:
        return normalize_label(m.group(1))
    best, label = -1.0, None
    for line in (msg or "").splitlines():
        if ':' not in line:
            continue
        name, rhs = line.split(':', 1)
        try:
            p = float(rhs.replace('%','').replace(',','.'))
        except:
            continue
        if p > best:
            best, label = p, name.replace('•','').replace('✅','').strip()
    return normalize_label(label) if label else "Desconocida"

def _store_cnn_result(context, user_id: int, ml_result: str, detected_class: str, image_path: str = None):
    payload = {'ml_result': ml_result, 'detected_class': detected_class, 'image_path': image_path}
    context.bot_data.setdefault('image_analysis', {})[user_id] = payload
    context.application.bot_data.setdefault('image_analysis', {})[user_id] = payload

def extract_probs_from_msg(msg: str) -> dict:
    """Devuelve dict normalizado {Botrytis/Xanthomonas/Sana: prob(0-1)} desde el texto de la CNN."""
    if not msg: return {}
    probs = {}
    for line in msg.splitlines():
        if ':' not in line: 
            continue
        name, rhs = line.split(':', 1)
        name = normalize_label(name.replace('•','').replace('✅','').strip())
        try:
            val = float(rhs.replace('%','').replace(',','.').strip()) / 100.0
        except:
            continue
        if name in ('Botrytis','Xanthomonas','Sana'):
            probs[name] = val
    return probs


# ---- Wrapper RF por si tu clase no trae predict_disease_from_survey ----
def rf_predict_from_pipeline(modelo, feature_columns, survey_responses: dict):
    """Predice con el pipeline entrenado (RandomForest) usando respuestas de encuesta."""
    if modelo is None or not feature_columns:
        return {"error": True, "message": "Modelo/Features no disponibles"}

    try:
        yes_set = getattr(pr.RandomForest, "YES", {"si","sí","yes","true","1","y"})
        vals = []
        for i, _ in enumerate(feature_columns):
            resp = str(survey_responses.get(i+1, "no")).strip().lower()
            vals.append(1 if resp in yes_set else 0)

        X = pd.DataFrame([vals], columns=feature_columns)

        # obtener clases del estimador final
        clf = None
        try:
            clf = modelo.named_steps["clf"]
        except Exception:
            clf = getattr(modelo, "classes_", None) and modelo
        proba = modelo.predict_proba(X)[0]
        classes = list(getattr(clf, "classes_", ['Botrytis','Xanthomonas','Sana']))
        idx = int(proba.argmax())

        pred = normalize_label(classes[idx])
        return {
            "error": False,
            "clase_predicha": pred,
            "confianza": float(proba[idx]),
            "probabilidades": {normalize_label(c): float(p) for c, p in zip(classes, proba)}
        }
    except Exception as e:
        return {"error": True, "message": str(e)}


# -------------------- FOTO GUÍA --------------------
async def send_photo_guidance(context, user_id, user_name):
    try:
        await context.bot.send_message(chat_id=user_id, text="👋 Envíame una foto clara de tu lechuga para revisarla. 📷")
    except Exception as e:
        f.logger.error(f"send_photo_guidance: {e}")

# -------------------- TERMS --------------------
async def handle_terms_callback(update: Update, context: ContextTypes.DEFAULT_TYPE):
    q = update.callback_query
    await q.answer()
    uid = q.from_user.id
    uname = q.from_user.username or "sin_username"
    fecha = datetime.now().strftime("%d-%m-%Y")

    if q.data.startswith("acepto"):
        db.update_user_data_db(uid, agreement_state=True, DateAgreement=fecha)
        if q.message.text != "✅ Has aceptado los términos y condiciones.":
            await q.edit_message_text("✅ Has aceptado los términos y condiciones.")
        await send_photo_guidance(context, uid, uname)
    else:
        if q.message.text != "❌ Aún no has aceptado los términos.":
            await q.edit_message_text("❌ Aún no has aceptado los términos.")


# -------------------- ENCUESTA RF --------------------
def extract_survey_responses_for_ml(context, user_id):
    ss = context.bot_data.get('survey_sessions', {}).get(user_id)
    if not ss: return {}
    out = {}
    for k,v in ss.get('responses', {}).items():
        if k.startswith('q'):
            try: out[int(k[1:])] = v
            except: pass
    return out

async def send_diagnostic_question_simple(context, user_id, qn, message=None):
    qdata = db.get_diagnostic_question(qn)
    if not qdata:
        txt = "❌ No pude cargar la pregunta. Intenta de nuevo."
        if message: await message.edit_text(txt)
        else: await context.bot.send_message(chat_id=user_id, text=txt)
        return
    total = db.get_total_diagnostic_questions()
    valid = [a for a in qdata['answers'] if a['answer_text'] and a['answer_text'].strip()!='']
    if not valid:
        if qn < total: await send_diagnostic_question_simple(context, user_id, qn+1, message)
        else: await ask_cultivation_location(context, user_id)
        return
    kb = [[InlineKeyboardButton(a['answer_text'], callback_data=f"simple_answer:{qn}:{a['answer_text']}")] for a in valid]
    progress = "🟢"*qn + "⚪"*(total-qn)
    txt = f"📋 PREGUNTA {qn} de {total}**\n{progress}\n\n❓     {qdata['question_text']}**\n\n👇 Elige tu respuesta:"
    if message: await message.edit_text(txt, reply_markup=InlineKeyboardMarkup(kb), parse_mode='Markdown')
    else: await context.bot.send_message(chat_id=user_id, text=txt, reply_markup=InlineKeyboardMarkup(kb), parse_mode='Markdown')

import re

def clean_question_text(text: str) -> str:
    """
    Limpia el texto de la pregunta:
    - Elimina emojis y caracteres especiales.
    - Quita saltos de línea, tabulaciones y espacios extra.
    """
    if not text:
        return ""

    # 1️⃣ Eliminar emojis
    emoji_pattern = re.compile(
        "[" 
        "\U0001F600-\U0001F64F"  # emoticonos
        "\U0001F300-\U0001F5FF"  # símbolos y pictogramas
        "\U0001F680-\U0001F6FF"  # transporte y mapas
        "\U0001F1E0-\U0001F1FF"  # banderas
        "\U00002700-\U000027BF"  # otros símbolos
        "\U0001F900-\U0001F9FF"  # pictogramas suplementarios
        "]+", 
        flags=re.UNICODE
    )
    text = emoji_pattern.sub('', text)

    # 2️⃣ Reemplazar saltos de línea y tabulaciones por un solo espacio
    text = re.sub(r'[\n\r\t]+', ' ', text)

    # 3️⃣ Quitar espacios duplicados
    text = re.sub(r'\s{2,}', ' ', text)

    # 4️⃣ Quitar espacios al inicio y final
    return text.strip()


async def handle_simple_answer_callback(update: Update, context: ContextTypes.DEFAULT_TYPE):
    """Guarda la respuesta y limpia el texto de la pregunta antes de almacenarlo."""
    q = update.callback_query
    await q.answer()
    uid = q.from_user.id
    data = q.data

    if data.startswith("simple_answer:"):
        parts = data.split(":", 2)
        qn = int(parts[1])
        ans = parts[2]

        # Mantener estructura de respuestas
        ss = context.bot_data.setdefault('survey_sessions', {}).setdefault(uid, {'responses': {}})
        ss['responses'][f'q{qn}'] = ans

        # 🧠 Guardar texto limpio de la pregunta
        qdata = db.get_diagnostic_question(qn)
        if qdata and qdata.get('question_text'):
            clean_text = clean_question_text(qdata['question_text'])
            ss.setdefault('question_texts', {})[qn] = clean_text

        # Continuar flujo normal
        total = db.get_total_diagnostic_questions()
        if qn < total:
            await send_diagnostic_question_simple(context, uid, qn + 1, q.message)
        else:
            await q.edit_message_text("✅ Gracias. Ahora cuéntame dónde está tu cultivo.")
            await ask_cultivation_location(context, uid)

async def ask_cultivation_location(context, user_id):
    kb = [[InlineKeyboardButton("🏠 Hidroponía", callback_data=f"location:invernadero:{user_id}")],
          [InlineKeyboardButton("🌍 Sustrato", callback_data=f"location:tierra:{user_id}")]]
    await context.bot.send_message(chat_id=user_id, text="📍 ¿Dónde tienes tu cultivo?", reply_markup=InlineKeyboardMarkup(kb))

async def handle_location_callback(update: Update, context: ContextTypes.DEFAULT_TYPE):
    q = update.callback_query
    await q.answer()
    uid = q.from_user.id
    data = q.data
    if not data.startswith("location:"): return
    ubic = data.split(":")[1]
    ss = context.bot_data.setdefault('survey_sessions', {}).setdefault(uid, {})
    ss['cultivation_location'] = ubic
    await q.edit_message_text("🔄 Procesando diagnóstico final...")
    await asyncio.sleep(1)
    await complete_combined_diagnosis_with_rf(context, uid)


async def handle_image(update: Update, context: ContextTypes.DEFAULT_TYPE):
    uid = update.message.from_user.id
    uname = update.message.from_user.username or "sin_username"

    # validar términos
    if not db.check_user_exists_db(uid):
        db.create_user_db(uid, uname, False, None, False, None)
    user_data = db.load_user_data_db(uid) or {"agreement_state": False}
    if not user_data.get("agreement_state", False):
        kb = [[InlineKeyboardButton("✅ Acepto", callback_data=f"acepto:{uid}")],
              [InlineKeyboardButton("❌ No Acepto", callback_data=f"no_acepto:{uid}")]]
        
        terms_text = (
            "📜 *Términos y Condiciones de Uso*\n\n"
            "Antes de usar este bot, debes aceptar los siguientes términos:\n\n"
            "1️⃣ El bot ofrece asistencia automatizada para diagnóstico y recomendaciones fitopatológicas, "
            "pero *no reemplaza la opinión ni el criterio de un experto agrónomo*.\n\n"
            "2️⃣ Tus datos (como tu ID de Telegram, teléfono y respuestas) se usan únicamente "
            "para mejorar el servicio y *no se comparten con terceros*.\n\n"
            "3️⃣ Las *imágenes enviadas por el usuario* serán almacenadas temporalmente con el único fin "
            "de análisis fitopatológico y *se eliminarán automáticamente después de 5 minutos*. 🕒\n\n"
            "4️⃣ Los resultados proporcionados por el bot son orientativos y pueden contener errores; "
            "úsalos *bajo tu propia responsabilidad*.\n\n"
            "5️⃣ No uses el bot para fines ilegales, ofensivos o distintos a su propósito educativo.\n\n"
            "Al presionar *“Aceptar términos”*, confirmas que *has leído, entendido y aceptas* estas condiciones.\n"
            "Si no estás de acuerdo, simplemente no continúes usando el bot. 🚫"
        )

        
        await update.message.reply_text(terms_text, reply_markup=InlineKeyboardMarkup(kb))
        return

    if not update.message.photo:
        return

    file_id = update.message.photo[-1].file_id

    # ventana de 60 s: guardo última imagen y reprogramo tarea
    win = context.bot_data.setdefault('image_window', {})
    sess = win.setdefault(uid, {"count": 0, "last_file_id": None, "task": None,
                                "chat_id": update.effective_chat.id, "uname": uname})

    # cancelar tarea previa
    if sess.get("task"):
        try: sess["task"].cancel()
        except Exception: pass

    # actualizar estado
    sess["count"] += 1
    sess["last_file_id"] = file_id
    sess["chat_id"] = update.effective_chat.id
    sess["uname"] = uname

    # programar procesamiento en 60 s (solo la última)
    async def _delayed_run():
        try:
            await asyncio.sleep(WINDOW_SECONDS)
            await process_image_after_window_async(context, uid)
        except asyncio.CancelledError:
            pass
        except Exception as e:
            f.logger.error(f"_delayed_run error: {e}")

    sess["task"] = asyncio.create_task(_delayed_run())
    await update.message.reply_text("👍 Recibí tu imagen. Esperaré 1 minuto por si envías más y analizaré la última.")

# -------------------- DIAGNÓSTICO FINAL (Comparación CNN vs RF) --------------------
# ============================================================
# Paso final: comparar CNN (imagen) vs RF (encuesta) y responder
# ============================================================
async def complete_combined_diagnosis_with_rf(context, user_id):
    try:
        # 1) recuperar resultado de CNN
        image_data = (context.bot_data.get('image_analysis', {}).get(user_id)
                      or context.application.bot_data.get('image_analysis', {}).get(user_id))
        if not image_data:
            await context.bot.send_message(chat_id=user_id,
                                           text="❌ No tengo el resultado de la imagen. Envía una foto de nuevo.")
            return

        cnn_class = normalize_label(image_data.get('detected_class', 'Desconocida'))

        # 2) ejecutar RF
        modelo = context.bot_data.get('ml_model')
        features = context.bot_data.get('ml_features')
        if not (modelo and features):
            await context.bot.send_message(chat_id=user_id, text="⚠️ No pude ejecutar el Random Forest.")
            return

        responses = extract_survey_responses_for_ml(context, user_id)
        rf_out = rf_predict_from_pipeline(modelo, features, responses)
        if rf_out.get("error"):
            await context.bot.send_message(chat_id=user_id, text=f"⚠️ Error en RF: {rf_out.get('message','desconocido')}")
            return

        rf_class = normalize_label(rf_out["clase_predicha"])

        # 3) feedback de coincidencia
        if (cnn_class or '').lower() == (rf_class or '').lower():
            msg = (f"✅ **Las clasificaciones COINCIDEN**\n\n"
                   f"• Imagen: {cnn_class}\n"
                   f"• Preguntas: {rf_class}")
            await context.bot.send_message(chat_id=user_id, text=msg, parse_mode='Markdown')            
            db.increment_user_diagnosis_db(user_id)

            # 4) construir bloques para PDF
            from datetime import datetime
            ts = datetime.now().strftime("%Y%m%d_%H%M%S")
            reports_dir = os.path.join("data", "reports")
            os.makedirs(reports_dir, exist_ok=True)
            outfile = os.path.join(reports_dir, f"Pacho_Informe_{user_id}_{ts}.pdf")

            question_texts = context.bot_data.get('survey_sessions', {}).get(user_id, {}).get('question_texts', {})

            rf_block = {
                "clasificacion": rf_class,
                "confianza": float(rf_out.get("confianza", 0.0)),
                "probabilidades": rf_out.get("probabilidades", {}),
                "respuestas": responses,
                "preguntas": question_texts
            }


            cnn_probs = extract_probs_from_msg(image_data.get('ml_result', ''))
            example_path = f.get_example_image_for_disease(cnn_class)
            cnn_block = {
                "clasificacion": cnn_class,
                "probabilidades": cnn_probs,
                "imagen_usuario_path": image_data.get('image_path'),
                "imagen_ejemplo_path": example_path
            }

            # ubicación seleccionada al terminar la encuesta
            ubic = None
            try:
                ubic = context.bot_data.get('survey_sessions', {}).get(user_id, {}).get('cultivation_location')
            except Exception:
                pass
            ubic_norm = (ubic or "tierra").strip().lower()

            # 5) tratamientos: API -> fallback local
            tratamientos = []
            treatment_title = ""
            try:
                resultados = db.search_treatments_db(rf_class, ubic_norm, limit=4)
                if resultados:
                    tratamientos = [r['detalle_tratamiento'] for r in resultados]
                    treatment_title = "Tratamiento recomendado"
                else:
                    treatment_title = "Observaciones"
                    tratamientos = [
                        "⚠️ No se encontraron tratamientos registrados en la base de datos para esta enfermedad y ubicación."
                    ]
            except Exception as e:
                f.logger.error(f"Error consultando tratamientos: {e}")
                treatment_title = "Observaciones"
                tratamientos = [
                    "❌ Error al consultar tratamientos en la base de datos."
                ]

            meta = {"fecha": datetime.now().strftime("%d-%m-%Y %H:%M")}
            load_dotenv()
            logo_path = os.getenv("REPORT_IMAGES_PATH") + "logo_pacho.png"

            # aviso previo
            await context.bot.send_message(
                chat_id=user_id,
                text="📄 A continuación te enviaré un documento con el resumen del diagnóstico y la recomendación de tratamiento."
            )

            # generar y enviar PDF
            f.build_pacho_pdf_report(
                outfile=outfile,
                meta=meta,
                rf_block=rf_block,
                cnn_block=cnn_block,
                tratamiento=tratamientos,
                logo_path=logo_path,
                treatment_title=treatment_title
            )

            with open(outfile, "rb") as fh:
                await context.bot.send_document(
                    chat_id=user_id,
                    document=fh,
                    filename=os.path.basename(outfile),
                    caption="📄 Informe de diagnóstico"
                )

        else:
            msg = (f"⚠️ **Las clasificaciones NO coinciden**\n\n"
                   f"• Imagen: {cnn_class}\n"
                   f"• Preguntas: {rf_class}")
            await context.bot.send_message(chat_id=user_id, text=msg, parse_mode='Markdown')
            await context.bot.send_message(chat_id=user_id, text="⚠️ Por favor, envía otra foto de tu lechuga para un nuevo análisis. Responde las preguntas de acuerdo a tus condiciones.")
            # No se genera ni envía PDF

        # 6) limpieza
        context.bot_data.get('image_analysis', {}).pop(user_id, None)
        context.application.bot_data.get('image_analysis', {}).pop(user_id, None)
        context.bot_data.get('survey_sessions', {}).pop(user_id, None)
        f.delete_user_files(user_id=user_id)        

    except Exception as e:
        f.logger.error(f"complete_combined_diagnosis_with_rf: {e}")
        await context.bot.send_message(chat_id=user_id, text="❌ Error al completar el diagnóstico.")
        f.delete_user_files(user_id=user_id) 


# -------------------- MENSAJES DE TEXTO --------------------
async def handle_message(update: Update, context: ContextTypes.DEFAULT_TYPE):
    uid = update.message.from_user.id
    uname = update.message.from_user.username or "sin_username"
    msg = update.message.text

    # usuario y términos
    if not db.check_user_exists_db(uid): db.create_user_db(uid, uname, False, None, False, None)
    user_data = db.load_user_data_db(uid) or {"agreement_state": False}
    if not user_data.get("agreement_state", False):
        kb = [[InlineKeyboardButton("✅ Acepto", callback_data=f"acepto:{uid}")],
              [InlineKeyboardButton("❌ No Acepto", callback_data=f"no_acepto:{uid}")]]
            # Texto de términos en formato Markdown
        terms_text = (
            "📜 *Términos y Condiciones de Uso*\n\n"
            "Antes de usar este bot, debes aceptar los siguientes términos:\n\n"
            "1️⃣ El bot ofrece asistencia automatizada para diagnóstico y recomendaciones fitopatológicas, "
            "pero *no reemplaza la opinión ni el criterio de un experto agrónomo*.\n\n"
            "2️⃣ Tus datos (como tu ID de Telegram, teléfono y respuestas) se usan únicamente "
            "para mejorar el servicio y *no se comparten con terceros*.\n\n"
            "3️⃣ Las *imágenes enviadas por el usuario* serán almacenadas temporalmente con el único fin "
            "de análisis fitopatológico y *se eliminarán automáticamente después de 5 minutos*. 🕒\n\n"
            "4️⃣ Los resultados proporcionados por el bot son orientativos y pueden contener errores; "
            "úsalos *bajo tu propia responsabilidad*.\n\n"
            "5️⃣ No uses el bot para fines ilegales, ofensivos o distintos a su propósito educativo.\n\n"
            "Al presionar *“Aceptar términos”*, confirmas que *has leído, entendido y aceptas* estas condiciones.\n"
            "Si no estás de acuerdo, simplemente no continúes usando el bot. 🚫"
        )

        # Enviar el mensaje con formato y botones
        await update.message.reply_text(
            terms_text,
            parse_mode="Markdown",
            reply_markup=InlineKeyboardMarkup(kb)
        )        
        return

    # primer mensaje del día: saludar y pedir imagen
    last_seen = context.bot_data.setdefault('last_seen_date',{}).get(uid)
    today = datetime.now().date()
    if last_seen != today:
        context.bot_data['last_seen_date'][uid] = today
        await update.message.reply_text("👋 ¡Hola! Envíame una **foto de tu lechuga** para revisarla. 📷")
    else:
        await update.message.reply_text("📷 Envíame una **foto de tu lechuga** para analizarla.")

# -------------------- INIT ML --------------------
def initialize_bot_with_ml():
    try:
        res = pr.RandomForest.initialize_ml_system()
        if isinstance(res, tuple) and len(res)==4:
            pipe, feature_columns, _, metrics = res
            return pipe, None, feature_columns
        elif isinstance(res, tuple) and len(res)==3:
            return res  # modelo, scaler, features
        else:
            pipe = res[0]; feats = res[1] if len(res)>1 else None
            return pipe, None, feats
    except Exception as e:
        print(f"Error inicializando RF: {e}")
        return None, None, None

# -------------------- MAIN --------------------
def main():
    print("🤖 Iniciando bot...")
    if not db.test_db_connection():
        print("❌ No se puede conectar a la base de datos"); return
    modelo_rf, scaler_rf, feature_columns = initialize_bot_with_ml()
    f.setup_directories(); f.setup_logging(); 
    print("Archivos eliminados: ",f.cleanup_old_files(minutes_old=5))

    token,_,_,_ = f.load_values()
    application = Application.builder().token(token).build()
    application.bot_data['ml_model'] = modelo_rf
    application.bot_data['ml_scaler'] = scaler_rf
    application.bot_data['ml_features'] = feature_columns
    application.bot_data['ml_available'] = modelo_rf is not None

    application.add_handler(CallbackQueryHandler(handle_terms_callback, pattern="^(acepto:|no_acepto:)"))
    
    application.add_handler(CallbackQueryHandler(handle_simple_answer_callback, pattern="^simple_answer:"))
    application.add_handler(CallbackQueryHandler(handle_location_callback, pattern="^location:"))
    application.add_handler(MessageHandler(filters.TEXT & ~filters.COMMAND, handle_message))
    application.add_handler(MessageHandler(filters.PHOTO, handle_image))

    application.run_polling(drop_pending_updates=True)

if __name__ == "__main__":
    main()


