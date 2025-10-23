#Librerías
import os
import pandas as pd
from datetime import datetime
from joblib import dump, load
from sklearn.model_selection import train_test_split
from sklearn.pipeline import Pipeline
from sklearn.preprocessing import StandardScaler
from sklearn.ensemble import RandomForestClassifier
from dotenv import load_dotenv

# Carga las variables definidas en archivo .env para configurar rutas y parámetros
load_dotenv()

# Obtiene la ruta del dataset para entrenamiento del Random Forest a partir de variable de entorno DATASET_RF.
rf_model_default = os.getenv(
    "DATASET_RF"    
)

# Obtiene la ruta donde se guardará o cargará el modelo Random Forest entrenado a partir de variable de entorno RANDOM_FOREST_MODEL_PATH,
# con una ruta predeterminada por si no está configurada.
rf_model = os.getenv(
    "RANDOM_FOREST_MODEL_PATH",
    r"C:\PythonLechugaBot\Model"
)

# Definición de clase RandomForest: entrenamiento.
class RandomForest:
    # Define las columnas (características) del dataset que se usan para entrenar el modelo,
    # representando atributos que describen el estado de la planta o cultivo.
    FEATURES_DEFAULT = [
        'VelloGris', 'ManchaAcuosa', 'ManchaMarron', 'AltaHumedad',
        'DiasNublados', 'TempFria', 'MalaVentilacion', 'PlantaTuvoHerida',
        'ClimaCalido', 'RiegoAspersion'
    ]

    # Nombre de la columna etiqueta que contiene la clasificación o diagnóstico.
    LABEL_DEFAULT = 'Clasificacion'

    # Define qué valores textuales se interpretan como afirmativos para respuestas binarias
    YES = {"si", "sí", "yes", "true", "1", "y"}

    # -----------------------------
    # Métodos estáticos para utilidades
    # -----------------------------

    @staticmethod
    def _read_any(path: str) -> pd.DataFrame:
        """
        Dada la ruta de un archivo, detecta si es CSV o Excel y carga sus datos
        en un DataFrame de pandas para facilitar manipulación posterior.
        """
        ext = os.path.splitext(path)[1].lower()  # Extrae la extensión y la convierte a minúsculas.
        if ext == ".csv":
            return pd.read_csv(path)
        elif ext in (".xlsx", ".xls"):
            return pd.read_excel(path)
        # Lanza excepción si el formato no es soportado (ni Excel ni CSV)
        raise ValueError(f"Formato no soportado: {ext}")

    @staticmethod
    def _coerce_binary(df: pd.DataFrame, cols: list[str]) -> pd.DataFrame:
        """
        Convierte las columnas indicadas en el DataFrame en binarias (0 o 1), estándar útil para ML.
        Usa mapeos de valores strings a 0 o 1, y convierte valores booleanos o numéricos igualmente.
        """
        # Mapeo usado para traducir texto a 0/1
        mapping = {
            'si':1, 'sí':1, 'true':1, 't':1, '1':1, 'y':1, 'yes':1,
            'no':0, 'false':0, 'f':0, '0':0, 'n':0
        }
        for c in cols:
            # Valida que la columna exista en el DataFrame
            if c not in df.columns:
                raise ValueError(f"Falta columna: {c}")
            s = df[c]
            # Distintos casos para asegurarse que todos los datos sean enteros 0 o 1:
            if s.dtype == 'bool':  # Si es booleano, convierte True->1, False->0
                df[c] = s.astype(int)
            elif s.dtype.kind in 'iu':  # Si es entero, lo asegura convertido a int
                df[c] = s.astype(int)
            elif s.dtype.kind == 'f':  # Si es flotante, convierte valores positivos a 1, otros 0
                df[c] = (s > 0).astype(int)
            else:  # En caso de ser texto, limpia y mapea usando el diccionario mapping
                df[c] = s.astype(str).str.strip().str.lower().map(mapping)
                # Si quedan valores NaN tras mapa, intenta convertir a numérico y luego a int
                if df[c].isna().any():
                    df[c] = pd.to_numeric(df[c], errors='raise').astype(int)
                df[c] = df[c].astype(int)
        return df

    # -----------------------------
    # Construcción del pipeline ML
    # -----------------------------

    @staticmethod
    def _build_pipeline(use_scaler: bool = False) -> Pipeline:
        """
        Construye el procesamiento y clasificación:        
        """
        steps = []
        # Añade escalado o pasa directo según parámetro
        steps.append(("scaler", StandardScaler())) if use_scaler else steps.append(("scaler", "passthrough"))
        # Añade el clasificador Random Forest con hiperparámetros:
        # 200 árboles, máxima profundidad 100, selección de características por raíz cuadrada,
        # semilla fija para reproducibilidad, y balanceo de clases.
        steps.append(("clf", RandomForestClassifier(
            n_estimators=200, max_depth=100, max_features='sqrt',
            random_state=42, class_weight="balanced"
        )))
        return Pipeline(steps)

    # ------------------------------------------------
    # Entrenamiento del modelo con partición holdout
    # ------------------------------------------------

    @staticmethod
    def initialize_ml_system(
        data_path=rf_model_default,
        feature_columns=None,
        label_col=None,
        test_size=0.2,
        random_state=42,
        use_scaler=False
    ):
        """
        Lee el dataset, valida columnas, transforma columnas binarias,
        divide en train/test, entrena el pipeline, y devuelve métricas.
        Es el punto principal para construir y validar el modelo Random Forest.
        """
        try:
            # Usa columnas de características y etiqueta por defecto si no se indican
            feature_columns = feature_columns or RandomForest.FEATURES_DEFAULT
            label_col = label_col or RandomForest.LABEL_DEFAULT

            # Lee dataset desde archivo (Excel o CSV)
            df = RandomForest._read_any(data_path)

            # Verifica que todas las columnas necesarias estén presentes
            expected = set(feature_columns + [label_col])
            if not expected.issubset(df.columns):
                faltantes = list(expected - set(df.columns))
                raise ValueError(f"Columnas faltantes: {', '.join(faltantes)}")

            # Convierte las columnas binarias a formato numérico estándar (0/1)
            df = RandomForest._coerce_binary(df, feature_columns)

            # Separa variables independientes (features) y variable dependiente (label) del dataset
            X = df[feature_columns]
            y = df[label_col].astype(str)  # Convierte etiqueta a texto para clasificación nominal

            # Divide el dataset en entrenamiento y prueba (holdout) usando estratificación para clases
            X_train, X_test, y_train, y_test = train_test_split(
                X, y, test_size=test_size, random_state=random_state, stratify=y
            )

            # Construye pipeline con o sin escalado según parámetro
            pipe = RandomForest._build_pipeline(use_scaler=use_scaler)

            # Entrena el pipeline completo con datos de entrenamiento
            pipe.fit(X_train, y_train)

            # Obtiene exactitudes sobre datos entrenamiento y prueba
            acc_train = pipe.score(X_train, y_train)
            acc_test = pipe.score(X_test, y_test)

            # Imprime métricas para evaluación rápida
            print("🎯 Exactitud (holdout)")
            print(f" • Entrenamiento: {acc_train:.4f}")
            print(f" • Prueba: {acc_test:.4f}")

            # Prepara diccionario con métricas y clases para retornar
            metrics = {
                "train_accuracy": float(acc_train),
                "test_accuracy": float(acc_test),
                "classes": list(pipe.named_steps["clf"].classes_)
            }

            # Retorna pipeline entrenado, columnas características, etiqueta y métricas
            return pipe, feature_columns, label_col, metrics

        except Exception as e:
            # En caso de error, se imprime en consola y retorna estructura con error
            print(f"❌ Error al entrenar: {e}")
            return None, None, None, {"error": True, "message": str(e)}

    # -----------------------------
    # Predecir usando respuestas de encuesta
    # -----------------------------

    @staticmethod
    def predict_disease_from_survey_bundle(bundle_path, survey_responses: dict):
        """
        Dado un archivo de modelo y un diccionario con respuestas de encuesta binarias,
        realiza la predicción con Random Forest y retorna la clase predicha, probabilidad y confianza.
        """
        # Carga modelo y columnas
        pipe, features, label_col, classes = RandomForest.load_bundle(bundle_path)

        vals = []
        # Recorre cada característica del modelo, y convierte la respuesta de encuesta a 0/1
        for i, col in enumerate(features):
            resp = str(survey_responses.get(i + 1, "no")).strip().lower()
            vals.append(1 if resp in RandomForest.YES else 0)

        # Crea DataFrame con las respuestas procesadas para predecir
        X = pd.DataFrame([vals], columns=features)

        # Obtiene las probabilidades predichas para cada clase
        proba = pipe.predict_proba(X)[0]

        # Encuentra índice de la clase con mayor probabilidad
        idx = proba.argmax()

        # Retorna la clase, confianza, y vector de probabilidades para cada clase
        return {
            "clase_predicha": str(classes[idx]),
            "confianza": float(proba[idx]),
            "probabilidades": {str(c): float(p) for c, p in zip(classes, proba)},
            "timestamp": datetime.now().strftime("%Y-%m-%d %H:%M:%S")
        }