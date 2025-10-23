FROM python:3.11-slim

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1

RUN apt-get update && apt-get install -y --no-install-recommends \
    ca-certificates curl gnupg apt-transport-https \
    build-essential unixodbc unixodbc-dev \
    libssl-dev libffi-dev \
    zlib1g-dev libjpeg62-turbo-dev \
    libxml2-dev libxslt1-dev \
 && rm -rf /var/lib/apt/lists/*

# Repo Microsoft para Debian 12 (bookworm) → ODBC 17
RUN curl -fsSL https://packages.microsoft.com/keys/microsoft.asc \
  | gpg --dearmor -o /usr/share/keyrings/microsoft-prod.gpg && \
  echo "deb [arch=amd64,arm64,armhf signed-by=/usr/share/keyrings/microsoft-prod.gpg] https://packages.microsoft.com/debian/12/prod bookworm main" \
  > /etc/apt/sources.list.d/microsoft-prod.list && \
  apt-get update && ACCEPT_EULA=Y apt-get install -y --no-install-recommends msodbcsql17 && \
  rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY requirements.txt .
RUN python -m pip install --upgrade pip setuptools wheel && \
    pip install --no-cache-dir -r requirements.txt

# Copia el código
COPY . .

# Crea y copia recursos (si no existen ya en el repo)
RUN mkdir -p /app/data/models /app/data/report_images /app/models

# Si ya pusiste los archivos en esas rutas del repo, con el COPY . . arriba
# ya estarán en la imagen. Si quieres asegurar rutas exactas:
# COPY data/models/Enfermedades_entrenamiento_actualizado.xlsx /app/data/models/
# COPY data/report_images/ /app/data/report_images/
# COPY models/ModeloFinal4.keras /app/models/
CMD ["python", "bot.py"]
