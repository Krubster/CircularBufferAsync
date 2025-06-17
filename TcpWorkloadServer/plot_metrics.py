import pandas as pd
import matplotlib.pyplot as plt
from datetime import datetime

# Настройки
CSV_PATH = "stats_log.csv"
SEPARATOR = "\t"  # заменяй, если у тебя другой

# Загружаем данные
df = pd.read_csv(CSV_PATH, sep=SEPARATOR)

# Конвертируем время
df["Timestamp"] = pd.to_datetime(df["Timestamp"])

# ==============================
# 📊 1. Байты: получено и отправлено
# ==============================
plt.figure(figsize=(10, 5))
plt.plot(df["Timestamp"], df["BytesReceived"], label="Bytes Received")
plt.plot(df["Timestamp"], df["BytesSent"], label="Bytes Sent")
plt.title("Traffic")
plt.xlabel("Time")
plt.ylabel("Bytes")
plt.legend()
plt.grid(True)
plt.tight_layout()
plt.savefig("plot_bytes.png")
plt.show()

# ==============================
# 📊 2. logicMs
# ==============================
if "logicMs" in df.columns:
    plt.figure(figsize=(10, 4))
    plt.plot(df["Timestamp"], df["logicMs"], label="logicMs", color="orange")
    plt.title("Logic Thread Time (ms)")
    plt.xlabel("Time")
    plt.ylabel("Milliseconds")
    plt.grid(True)
    plt.tight_layout()
    plt.savefig("plot_logicMs.png")
    plt.show()

# ==============================
# 📊 3. pidOutput
# ==============================
if "pidOutput" in df.columns:
    plt.figure(figsize=(10, 4))
    plt.plot(df["Timestamp"], df["pidOutput"], label="PID Output", color="green")
    plt.title("PID Output Value")
    plt.xlabel("Time")
    plt.ylabel("Output")
    plt.grid(True)
    plt.tight_layout()
    plt.savefig("plot_pidOutput.png")
    plt.show()

# ==============================
# 📊 4. allowedPerConn
# ==============================
if "allowedPerConn" in df.columns:
    plt.figure(figsize=(10, 4))
    plt.plot(df["Timestamp"], df["allowedPerConn"], label="Allowed Packets / Conn", color="red")
    plt.title("Allowed Packets Per Connection")
    plt.xlabel("Time")
    plt.ylabel("Count")
    plt.grid(True)
    plt.tight_layout()
    plt.savefig("plot_allowedPerConn.png")
    plt.show()
