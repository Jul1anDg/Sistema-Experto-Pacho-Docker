
import tensorflow as tf
from tensorflow import keras
from tensorflow.keras import layers
from tensorflow.keras.preprocessing.image import ImageDataGenerator
from tensorflow.keras.applications import MobileNet
from tensorflow.keras.callbacks import EarlyStopping, ReduceLROnPlateau, ModelCheckpoint
from sklearn.utils.class_weight import compute_class_weight
from sklearn.metrics import classification_report, confusion_matrix
import numpy as np
import os
import time
from datetime import datetime
import warnings
warnings.filterwarnings('ignore')

tf.config.set_visible_devices([], 'GPU')
print(f"✓ TensorFlow corriendo en CPU - Versión: {tf.__version__}\n")

# CONFIGURACIÓN GENERAL
IMG_SIZE = 224
BATCH_SIZE = 16
EPOCHS = 30
FINE_TUNE_EPOCHS = 15
NUM_CLASSES = 3
DATA_DIR = "DatasetSplitHibrido"

timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
RESULTS_DIR = f"mobilenet_v1_resultados_{timestamp}"
os.makedirs(RESULTS_DIR, exist_ok=True)
print(f"✓ Carpeta de resultados: {RESULTS_DIR}\n")

# PREPARACIÓN DE DATOS
train_dir = os.path.join(DATA_DIR, 'train')
val_dir = os.path.join(DATA_DIR, 'val')
test_dir = os.path.join(DATA_DIR, 'test')

train_datagen = ImageDataGenerator(
    rescale=1./255,
    rotation_range=30,
    width_shift_range=0.2,
    height_shift_range=0.2,
    shear_range=0.2,
    zoom_range=0.2,
    horizontal_flip=True,
    fill_mode='nearest',
    brightness_range=[0.8, 1.2],
    channel_shift_range=0.1
)

val_test_datagen = ImageDataGenerator(rescale=1./255)

train_generator = train_datagen.flow_from_directory(
    train_dir,
    target_size=(IMG_SIZE, IMG_SIZE),
    batch_size=BATCH_SIZE,
    class_mode='categorical',
    shuffle=True,
    seed=42
)
validation_generator = val_test_datagen.flow_from_directory(
    val_dir,
    target_size=(IMG_SIZE, IMG_SIZE),
    batch_size=BATCH_SIZE,
    class_mode='categorical',
    shuffle=False,
    seed=42
)
test_generator = val_test_datagen.flow_from_directory(
    test_dir,
    target_size=(IMG_SIZE, IMG_SIZE),
    batch_size=BATCH_SIZE,
    class_mode='categorical',
    shuffle=False,
    seed=42
)

class_labels = list(train_generator.class_indices.keys())
class_counts = [sum(train_generator.classes == i) for i in range(NUM_CLASSES)]

print(f"✓ Clases detectadas: {class_labels}")
print(f"✓ Distribución: {dict(zip(class_labels, class_counts))}")

# Calcular pesos balanceados
class_weight_dict = dict(enumerate(
    compute_class_weight('balanced', classes=np.arange(NUM_CLASSES),
                         y=np.repeat(np.arange(NUM_CLASSES), class_counts))
))
print(f"✓ Pesos de clase: {class_weight_dict}\n")

# CREACIÓN DEL MODELO
print("="*80)
print("CREANDO MODELO: MobileNet v1")
print("="*80)

base_model = MobileNet(
    weights='imagenet',
    include_top=False,
    input_shape=(IMG_SIZE, IMG_SIZE, 3)
)
base_model.trainable = False

model = keras.Sequential([
    base_model,
    layers.GlobalAveragePooling2D(),
    layers.Dropout(0.3),
    layers.Dense(256, activation='relu', kernel_regularizer=keras.regularizers.l2(0.001)),
    layers.BatchNormalization(),
    layers.Dropout(0.5),
    layers.Dense(128, activation='relu', kernel_regularizer=keras.regularizers.l2(0.001)),
    layers.BatchNormalization(),
    layers.Dropout(0.3),
    layers.Dense(NUM_CLASSES, activation='softmax', name='predictions')
], name="MobileNet_Lechuga")

model.summary()

# FASE 1: ENTRENAMIENTO CONGELADO
model.compile(
    optimizer=keras.optimizers.Adam(learning_rate=0.001),
    loss='categorical_crossentropy',
    metrics=['accuracy', keras.metrics.Precision(name='precision'), keras.metrics.Recall(name='recall')]
)

best_model_path = os.path.join(RESULTS_DIR, "best_mobilenet.keras")

callbacks = [
    EarlyStopping(monitor='val_accuracy', patience=8, restore_best_weights=True, mode='max'),
    ReduceLROnPlateau(monitor='val_loss', factor=0.2, patience=4, min_lr=1e-7, verbose=1),
    ModelCheckpoint(best_model_path, monitor='val_accuracy', save_best_only=True, mode='max', verbose=1)
]

print("\n[FASE 1/2] Entrenando capas superiores...")
start_time = time.time()

history_frozen = model.fit(
    train_generator,
    validation_data=validation_generator,
    epochs=EPOCHS,
    class_weight=class_weight_dict,
    callbacks=callbacks,
    verbose=1
)

print(f"\n✓ Fase 1 completada en {(time.time() - start_time)/60:.2f} min")
print(f"  Mejor val_acc: {max(history_frozen.history['val_accuracy']):.4f}")

# FASE 2: FINE-TUNING
fine_tune_at = 50
base_model.trainable = True
for layer in base_model.layers[:fine_tune_at]:
    layer.trainable = False

model.compile(
    optimizer=keras.optimizers.Adam(learning_rate=1e-5),
    loss='categorical_crossentropy',
    metrics=['accuracy', keras.metrics.Precision(name='precision'), keras.metrics.Recall(name='recall')]
)

print("\n[FASE 2/2] Fine-tuning desde capa:", fine_tune_at)
start_time = time.time()

history_finetune = model.fit(
    train_generator,
    validation_data=validation_generator,
    epochs=FINE_TUNE_EPOCHS,
    class_weight=class_weight_dict,
    callbacks=callbacks,
    verbose=1
)

print(f"\n✓ Fine-tuning completado en {(time.time() - start_time)/60:.2f} min")
print(f"  Mejor val_acc: {max(history_finetune.history['val_accuracy']):.4f}")

# EVALUACIÓN FINAL
print("\nEVALUANDO MODELO FINAL...\n")
best_model = keras.models.load_model(best_model_path)
test_metrics = best_model.evaluate(test_generator, verbose=0)
test_loss, test_acc, test_precision, test_recall = test_metrics
f1 = 2 * (test_precision * test_recall) / (test_precision + test_recall + 1e-7)

print(f"✓ Test Accuracy: {test_acc:.4f}")
print(f"✓ Test Precision: {test_precision:.4f}")
print(f"✓ Test Recall: {test_recall:.4f}")
print(f"✓ Test F1-Score: {f1:.4f}")

# GUARDAR MODELO FINAL
final_model_path = os.path.join(RESULTS_DIR, "modelo_final_mobilenet.keras")
best_model.save(final_model_path)
print(f"\n✅ Modelo final guardado como: {final_model_path}")
