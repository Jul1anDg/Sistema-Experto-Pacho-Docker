import os
import pyodbc
from datetime import datetime
from dotenv import load_dotenv
import logging
from typing import Optional
import os
import pyodbc
from datetime import datetime
from dotenv import load_dotenv

logger = logging.getLogger(__name__)
load_dotenv()
# DRIVER = os.getenv("DB_DRIVER")
# SERVER = os.getenv("DB_SERVER")
# DATABASE = os.getenv("DB_NAME")
# TRUSTED = os.getenv("DB_TRUSTED")

# def connect_to_db() -> pyodbc.Connection:
#     cs = build_conn_str()
#     # log para depuración (oculta la contraseña si está)
#     pwd = _g("DB_PASSWORD")
#     safe_cs = cs.replace(pwd, "*****") if pwd else cs
#     print("🔧 Cadena de conexión:", safe_cs)
#     return pyodbc.connect(cs, timeout=5)

# def test_db_connection() -> bool:
#     try:
#         c = connect_to_db()
#         c.close()
#         return True
#     except Exception:
#         return False

def _g(key: str, default: str = "") -> str:
    """Lee y sanea variables de entorno (sin comillas/espacios accidentales)."""
    v = os.getenv(key, default)
    return (v or "").strip().strip("'").strip('"')

def _bool_env(key: str, default: bool = False) -> bool:
    v = _g(key, "1" if default else "0").lower()
    return v in ("1", "true", "yes", "y", "on")

def build_conn_str() -> str:
    driver = _g("DB_DRIVER", "ODBC Driver 17 for SQL Server")
    server = _g("DB_SERVER", "sqlserver")
    port   = _g("DB_PORT", "1433")
    db     = _g("DB_NAME", "BrainPacho")
    trusted = _bool_env("DB_TRUSTED", False)

    if trusted:
        # 🔐 Integrated/Trusted auth (solo si tu contenedor soporta Kerberos/Windows)
        conn_str = (
            f"DRIVER={{{driver}}};"
            f"SERVER={server},{port};"
            f"DATABASE={db};"
            f"Trusted_Connection=yes;"
            f"Encrypt=yes;TrustServerCertificate=yes;"
        )
    else:
        # 🔑 SQL Authentication (recomendado en Docker)
        user = _g("DB_USER")
        pwd  = _g("DB_PASSWORD")
        if not user or not pwd:
            raise RuntimeError("Faltan DB_USER/DB_PASSWORD y DB_TRUSTED != yes")
        conn_str = (
            f"DRIVER={{{driver}}};"
            f"SERVER={server},{port};"
            f"DATABASE={db};"
            f"UID={user};PWD={pwd};"
            f"Encrypt=yes;TrustServerCertificate=yes;"
        )

    return conn_str

def connect_to_db() -> pyodbc.Connection:
    cs = build_conn_str()
    # log para depuración (oculta la contraseña si está)
    pwd = _g("DB_PASSWORD")
    safe_cs = cs.replace(pwd, "*****") if pwd else cs
    print("🔧 Cadena de conexión:", safe_cs)
    return pyodbc.connect(cs, timeout=5)

def test_db_connection() -> bool:
    try:
        with connect_to_db() as conn:
            cur = conn.cursor()
            cur.execute("SELECT DB_NAME()")
            row = cur.fetchone()
            print("✅ Conectado a BD:", row[0] if row else "(desconocida)")
        return True
    except Exception as e:
        print("❌ No se puede conectar a la base de datos:", e)
        logger.exception("DB connection failed")
        return False

def normalize_disease_name(enf: str) -> str:
    if not enf: return ""
    e = enf.lower().strip()
    mapping = {
        "moho gris": "botrytis", "botrytis": "botrytis", "botritis": "botrytis",
        "mancha bacteriana": "xanthomonas", "xanthomonas": "xanthomonas", "xantomona": "xanthomonas",
        "hongo xantomona": "xanthomonas", "sana": "sana", "saludable": "sana", "sin enfermedad": "sana"
    }
    for k,v in mapping.items():
        if k in e: return v
    return e

def normalize_location_name(lugar: str) -> str:
    if not lugar: return "tierra"
    l = lugar.lower().strip()
    if l in ["invernadero", "greenhouse", "protegido", "bajo techo", "hidroponía","hidroponia"]: return "invernadero"
    if l in ["tierra", "campo", "abierto", "campo abierto", "exterior", "sutrato"]: return "tierra"
    return "tierra"

def get_user_stats_db():
    conn = connect_to_db()
    try:
        cur = conn.cursor()
        cur.execute("SELECT COUNT(*) FROM users_bot")
        total_users = cur.fetchone()[0]
        cur.execute("SELECT COUNT(*) FROM users_bot WHERE AgreementStatus = 'True'")
        accepted_terms = cur.fetchone()[0]
        cur.execute("SELECT SUM(total_diagnoses) FROM users_bot")
        total_diag = cur.fetchone()[0] or 0
        return {
            "total_users": total_users,
            "accepted_terms": accepted_terms,
            "pending_terms": total_users - accepted_terms,
            "total_diagnoses": total_diag
        }
    finally:
        conn.close()

def load_user_data_db(user_id):
    """Carga los datos de un usuario desde la base de datos - CORREGIDO PARA VARCHAR"""
    connection = connect_to_db()
    if not connection:
        return None
    
    try:
        cursor = connection.cursor()
        
        sql = """
            SELECT id_userbot, telegram_id, phone, total_diagnoses, 
                   AgreementStatus, DateAgreement, LastUpdated,
                   RecommendationState, RecommendationDate, CreatedAt
            FROM users_bot 
            WHERE id_userbot = ?
        """
        
        cursor.execute(sql, (user_id,))
        row = cursor.fetchone()
        print(f"🔍 Datos cargados para usuario {user_id}: {row}")
        if row:
            # ✅ CONVERTIR AgreementStatus de STRING a BOOLEAN
            AgreementStatus_str = row[4]
            agreement_state = AgreementStatus_str == "True" if AgreementStatus_str else False
            
            return {
                "user_id": row[0],
                "user_name": row[1],
                "phone": row[2],
                "total_diagnoses": row[3],
                "agreement_state": agreement_state,  # Convertido a boolean
                "DateAgreement": row[5].strftime("%d-%m-%Y") if row[5] else None,
                "LastUpdated": row[6].strftime("%d-%m-%Y %H:%M:%S") if row[6] else None,
                "RecommendationState": row[7],
                "RecommendationDate": row[8].strftime("%d-%m-%Y") if row[8] else None,
                "CreatedAt": row[9].strftime("%d-%m-%Y %H:%M:%S") if row[9] else None
            }
        else:
            return None
            
    except pyodbc.Error as e:
        logger.error(f"Error al cargar usuario {user_id} de BD: {e}")
        return None
    finally:
        connection.close()

def update_user_data_db(user_id: int, **fields) -> bool:
    """
    Actualiza datos de un usuario en users_bot.
    Campos posibles: user_name, phone, total_diagnoses, agreement_state,
    DateAgreement, RecommendationState, RecommendationDate
    """
    conn = connect_to_db()
    try:
        cur = conn.cursor()
        cur.execute("SELECT 1 FROM users_bot WHERE id_userbot = ?", (user_id,))
        if not cur.fetchone():
            return False

        sets, vals = [], []
        mapping = {
            "user_name": "telegram_id",
            "phone": "phone",
            "total_diagnoses": "total_diagnoses",
            "agreement_state": "AgreementStatus",
            "DateAgreement": "DateAgreement",
            "RecommendationState": "RecommendationState",
            "RecommendationDate": "RecommendationDate",
        }
        for k, v in fields.items():
            if k not in mapping: continue
            col = mapping[k]
            if k == "agreement_state":
                v = "True" if v else "False"
            if k in ("DateAgreement","RecommendationDate") and isinstance(v, str):
                try: v = datetime.strptime(v, "%d-%m-%Y").date()
                except: v = None
            sets.append(f"{col} = ?"); vals.append(v)

        sets.append("LastUpdated = ?"); vals.append(datetime.now())
        vals.append(user_id)

        if not sets:
            return True  # nada para actualizar

        cur.execute(f"UPDATE users_bot SET {', '.join(sets)} WHERE id_userbot = ?", vals)
        conn.commit()
        return True
    except Exception as e:
        logger.error(f"[DB update_user] {e}")
        conn.rollback()
        return False
    finally:
        conn.close()

def create_user_db(user_id, user_name, agreement_state=False, DateAgreement=None, 
                   RecommendationState=False, RecommendationDate=None, phone=None):
    user_id = int(user_id)
    """Crea un nuevo usuario en la base de datos - CORREGIDO PARA VARCHAR AgreementStatus"""
    connection = connect_to_db()
    if not connection:
        return False
    
    try:
        cursor = connection.cursor()
        
        # Verificar si el usuario ya existe
        cursor.execute("SELECT id_userbot FROM users_bot WHERE id_userbot = ?", (user_id,))
        if cursor.fetchone():
            logger.info(f"Usuario {user_id} ya existe en la base de datos")
            return True
        
        # ✅ MANEJAR phone NULL
        if phone is None or phone == "":
            phone = "sin_telefono"
        
        # Convertir fechas si están en formato string
        if isinstance(DateAgreement, str) and DateAgreement:
            try:
                DateAgreement = datetime.strptime(DateAgreement, "%d-%m-%Y").date()
            except ValueError:
                DateAgreement = None
                
        if isinstance(RecommendationDate, str) and RecommendationDate:
            try:
                RecommendationDate = datetime.strptime(RecommendationDate, "%d-%m-%Y").date()
            except ValueError:
                RecommendationDate = None
        
        # ✅ CONVERTIR agreement_state a STRING para VARCHAR
        AgreementStatus_str = "True" if agreement_state else "False"
        
        # Insertar nuevo usuario
        sql = """
            INSERT INTO users_bot (
                id_userbot, telegram_id, phone, total_diagnoses, 
                AgreementStatus, DateAgreement, LastUpdated,
                RecommendationState, RecommendationDate, CreatedAt
            )
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
        """
        
        current_time = datetime.now()
        
        cursor.execute(sql, (
            user_id,                    # id_userbot
            user_id,                  # telegram_id (username)
            phone,                      # phone
            0,                          # total_diagnoses
            AgreementStatus_str,       # AgreementStatus (como string)
            DateAgreement,             # DateAgreement
            current_time,               # LastUpdated
            RecommendationState,       # RecommendationState
            RecommendationDate,        # RecommendationDate
            current_time                # CreatedAt
        ))
        
        connection.commit()
        logger.info(f"Usuario {user_id} creado exitosamente en la base de datos")
        return True
        
    except pyodbc.Error as e:
        logger.error(f"Error al crear usuario {user_id} en BD: {e}")
        connection.rollback()
        return False
    finally:
        connection.close()

def check_user_exists_db(user_id):
    """Verifica si un usuario existe en la base de datos"""
    connection = connect_to_db()
    if not connection:
        return False
    
    try:
        cursor = connection.cursor()
        cursor.execute("SELECT COUNT(*) FROM users_bot WHERE id_userbot = ?", (user_id,))
        count = cursor.fetchone()[0]
        return count > 0
        
    except pyodbc.Error as e:
        logger.error(f"Error al verificar existencia de usuario {user_id}: {e}")
        return False
    finally:
        connection.close()

def increment_user_diagnosis_db(user_id: int) -> bool:
    """
    Incrementa el total de diagnósticos del usuario (+1),
    establece RecommendationState = True y RecommendationDate = fecha actual.
    """
    conn = connect_to_db()
    try:
        cur = conn.cursor()

        # Verificar existencia del usuario
        cur.execute("SELECT COUNT(*) FROM users_bot WHERE id_userbot = ?", (user_id,))
        if cur.fetchone()[0] == 0:
            logger.warning(f"Usuario {user_id} no encontrado en users_bot")
            return False

        # Ejecutar actualización
        sql = """
            UPDATE users_bot
            SET 
                total_diagnoses = ISNULL(total_diagnoses, 0) + 1,
                RecommendationState = 1,
                RecommendationDate = CAST(GETDATE() AS date),
                LastUpdated = GETDATE()
            WHERE id_userbot = ?
        """
        cur.execute(sql, (user_id,))
        conn.commit()
        logger.info(f"Diagnóstico incrementado y estado actualizado para usuario {user_id}")
        return True

    except Exception as e:
        logger.error(f"[DB increment_user_diagnosis_db] {e}")
        conn.rollback()
        return False
    finally:
        conn.close()

def get_diagnostic_question(question_order):
    """
    Obtiene una pregunta específica con sus respuestas posibles desde la BD
    CORREGIDO para coincidir con la estructura real de la BD
    
    Args:
        question_order (int): Número de orden de la pregunta (1, 2, 3, etc.)
    
    Returns:
        dict: Pregunta con respuestas o None si no existe
    """
    connection = connect_to_db()
    if not connection:
        return None
    
    try:
        cursor = connection.cursor()
        
        # ✅ CORREGIDO: Usar nombres de columnas reales
        sql_question = """
            SELECT Id_question, question_order, question_text
            FROM diagnostic_questions 
            WHERE question_order = ?
        """
        
        cursor.execute(sql_question, (question_order,))
        question_row = cursor.fetchone()
        
        if not question_row:
            return None
        
        question_id = question_row[0]
        
        # ✅ CORREGIDO: Obtener las respuestas con nombres de columnas reales
        sql_answers = """
            SELECT Id_answer, question_id, answer_order, answer_text
            FROM diagnostic_answers 
            WHERE question_id = ?
            AND answer_text IS NOT NULL 
            AND answer_text != ''
            AND answer_text != 'None'
            ORDER BY answer_order
        """
        
        cursor.execute(sql_answers, (question_id,))
        answer_rows = cursor.fetchall()
        
        # Construir respuesta
        question_data = {
            "question_id": question_row[0],
            "question_order": question_row[1],
            "question_text": question_row[2],            
            "answers": []
        }
        
        for answer_row in answer_rows:
            # ✅ FILTRAR RESPUESTAS VÁLIDAS
            answer_text = answer_row[3]  # Posición corregida
            if answer_text and answer_text.strip() and answer_text.strip().lower() != 'none':
                question_data["answers"].append({
                    "answer_id": answer_row[0],
                    "question_id": answer_row[1],
                    "answer_order": answer_row[2],
                    "answer_text": answer_text.strip()
                })
        
        return question_data
        
    except pyodbc.Error as e:
        logger.error(f"Error obteniendo pregunta {question_order}: {e}")
        return None
    finally:
        connection.close()

def get_total_diagnostic_questions():
    """Obtiene el número total de preguntas activas"""
    connection = connect_to_db()
    if not connection:
        return 0
    
    try:
        cursor = connection.cursor()
        cursor.execute("SELECT COUNT(*) FROM diagnostic_questions")
        count = cursor.fetchone()[0]
        return count
        
    except pyodbc.Error as e:
        logger.error(f"Error obteniendo total de preguntas: {e}")
        return 0
    finally:
        connection.close()

def search_treatments_db(enfermedad: str,
                         lugar: Optional[str] = None,
                         limit: int = 5):
    """
    Devuelve una lista de tratamientos para la enfermedad y (opcional) lugar.
    Tablas: diseases, treatments
    lugar: 'Sustrato'/'Sutrato' (se convierte a 2) o 'Hidroponía' (se convierte a 1)
    """
    # Normalizar enfermedad
    enf = normalize_disease_name(enfermedad)
    
    # Convertir lugar de texto a código numérico ANTES de la consulta
    lug = None
    if lugar:
        lugar_lower = lugar.lower().strip()
        if lugar_lower in ["sustrato", "sutrato", "tierra", "campo", "campo abierto", "suelo"]:
            lug = 2  # Código para sustrato/tierra
        elif lugar_lower in ["hidroponía", "hidroponia", "invernadero", "hidropónico", "protegido"]:
            lug = 1  # Código para hidroponía/invernadero
    
    # TOP no acepta parámetros -> sanitizamos en Python
    limit = max(1, min(int(limit or 5), 20))    
    conn = connect_to_db()
    try:
        cur = conn.cursor()

        base_sql = f"""
            SELECT TOP {limit}
                'Tratamiento N°:'+cast(row_number() over (partition by d.scientific_name order by t.creation_date asc) as varchar)+' Tipo de tratamiento: ' + treatment_type + CHAR(13) + CHAR(10) +
                'Producto recomendado: ' + recommended_products + CHAR(13) + CHAR(10) +
                'Frecuencia del tratamiento: ' + frequency + CHAR(13) + CHAR(10) +
                'Precauciones: ' + precautions + CHAR(13) + CHAR(10) +
                'Tiempo estimado de mejoría: ' + CAST(dias_mejoria_visual AS VARCHAR(10)) + ' días' AS detalle_tratamiento,
                d.common_name,
                t.Environment
            FROM diseases d
            INNER JOIN treatments t
                ON d.id_disease = t.disease_id
            WHERE (LOWER(d.scientific_name) LIKE ? OR LOWER(d.common_name) LIKE ?)
                AND d.asset = 1
        """
        params = [f"%{enf}%", f"%{enf}%"]
        
        if lug is not None:
            base_sql += " AND t.Environment = ?"
            params.append(lug)

        cur.execute(base_sql, params)
        rows = cur.fetchall()
        if enfermedad.lower() in ["sana", "saludable", "sin enfermedad"]:
            return [{"detalle_tratamiento": "La planta está sana. ¡Sigue con tus buenas prácticas agrícolas!", "enfermedad": "sana", "lugar": ""}]
        else:
            return [
                {
                    "detalle_tratamiento": r[0],
                    "enfermedad": r[1],
                    "lugar": "Hidroponía" if r[2] == 1 else "Sustrato",
                }
                for r in rows
            ]
    finally:
        conn.close()

def get_treatment_recommendations_db(enfermedad: str, ubicacion: str, top_n: int = 4) -> list[str]:
    """
    Devuelve una lista de textos de tratamiento desde la base de datos directamente.
    """
    try:
        items = search_treatments_db(enfermedad=enfermedad, lugar=ubicacion, limit=top_n)
        return [it.get("tratamiento", "").strip() for it in items if it.get("tratamiento")]
    except Exception as e:
        logger.error(f"[DB tratamientos] {e}")
        return []

def check_user_exists_db(user_id):
    """Verifica si un usuario existe en la base de datos"""
    connection = connect_to_db()
    if not connection:
        return False
    
    try:
        cursor = connection.cursor()
        cursor.execute("SELECT COUNT(*) FROM users_bot WHERE id_userbot = ?", (user_id,))
        count = cursor.fetchone()[0]
        return count > 0
        
    except pyodbc.Error as e:
        logger.error(f"Error al verificar existencia de usuario {user_id}: {e}")
        return False
    finally:
        connection.close()


